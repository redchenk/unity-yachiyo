import http.client
from http.server import ThreadingHTTPServer
import json
from pathlib import Path
import tempfile
import threading
import time
import unittest
from unittest.mock import patch, MagicMock
from service.server import Jobs, KimodoProvider, ReplayProvider, make_handler, normalize_request

ROOT=Path(__file__).resolve().parents[1]

class EncoderConfigurationTests(unittest.TestCase):
    def test_remote_encoder_does_not_require_local_llama_login(self):
        hub=MagicMock()
        with patch.dict("os.environ", {"YACHIYO_ENCODER":"api", "TEXT_ENCODER_URL":"http://127.0.0.1:7860"}), patch.dict("sys.modules", {"huggingface_hub":hub}):
            provider=KimodoProvider("test")
            self.assertEqual(provider.state,"unloaded")
            self.assertIn("Remote LLM2Vec",provider.model_name)
            hub.get_token.assert_not_called()
    def test_local_encoder_still_requires_login(self):
        hub=MagicMock();hub.get_token.return_value=None
        with patch.dict("os.environ", {"YACHIYO_ENCODER":"nf4"}), patch.dict("sys.modules", {"huggingface_hub":hub}):
            self.assertEqual(KimodoProvider("test").state,"awaiting_hf_login")
    def test_remote_url_validation_and_cache_separation(self):
        with patch.dict("os.environ", {"YACHIYO_ENCODER":"api", "TEXT_ENCODER_URL":""}):
            with self.assertRaises(ValueError):KimodoProvider("test")
        identities=[]
        for port in (7860,7861):
            with patch.dict("os.environ", {"YACHIYO_ENCODER":"api", "TEXT_ENCODER_URL":f"http://127.0.0.1:{port}"}):
                identities.append(KimodoProvider("test").cache_identity)
        self.assertNotEqual(*identities)

class ServerTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory()
        self.jobs=Jobs(ReplayProvider(ROOT/"UnityProject/Assets/StreamingAssets/Motions/kimodo-official.json"),Path(self.tmp.name))
        self.http=ThreadingHTTPServer(("127.0.0.1",0),make_handler(self.jobs))
        self.thread=threading.Thread(target=self.http.serve_forever,daemon=True);self.thread.start()
    def tearDown(self):
        self.http.shutdown();self.http.server_close();self.thread.join();self.jobs.pool.shutdown();self.tmp.cleanup()
    def request(self,method,path,data=None):
        c=http.client.HTTPConnection(*self.http.server_address,timeout=5)
        c.request(method,path,body=json.dumps(data)if data is not None else None,headers={"Content-Type":"application/json"})
        r=c.getresponse();status=r.status;body=json.loads(r.read());c.close();return status,body
    def test_http_generation_and_cache(self):
        self.assertEqual(self.request("GET","/health")[1]["provider"],"replay")
        status,job=self.request("POST","/jobs",{"prompt":"跳舞"});self.assertEqual(status,202)
        for _ in range(100):
            result=self.request("GET","/jobs/"+job["id"])[1]
            if result["status"]=="done":break
            time.sleep(.01)
        self.assertEqual(result["status"],"done")
        status,motion=self.request("GET","/jobs/"+job["id"]+"/motion")
        self.assertEqual(status,200);self.assertIn("非实时生成",motion["source"]);self.assertGreater(len(motion["frames"]),2)
        again=self.request("POST","/jobs",{"prompt":"跳舞"})[1];self.assertEqual(again["id"],job["id"]);self.assertTrue(again['cached'])
        fresh=Jobs(self.jobs.provider,Path(self.tmp.name))
        self.assertTrue(fresh.submit(normalize_request({"prompt":"跳舞"}))["cached"]);fresh.pool.shutdown()
    def test_bad_request_and_missing_jobs(self):
        for data in [{"prompt":""},{"prompt":"a","duration":float("nan")},{"prompt":"a","duration":100},[]]:
            self.assertEqual(self.request("POST","/jobs",data)[0],400)
        self.assertEqual(self.request("GET","/jobs/"+"0"*24)[0],404)
        self.assertEqual(self.request("GET","/jobs/../../etc/passwd")[0],404)
    def test_bounded_queue(self):
        event=threading.Event()
        class Slow:
            name="slow";model_name="test";state="ready"
            def generate(self,request):event.wait(3);raise RuntimeError("test failure")
        jobs=Jobs(Slow(),Path(self.tmp.name)/"slow")
        try:
            for i in range(2):jobs.submit(normalize_request({"prompt":"walk","seed":i}))
            with self.assertRaises(OverflowError):jobs.submit(normalize_request({"prompt":"walk","seed":3}))
        finally:event.set();jobs.pool.shutdown()

if __name__=="__main__":unittest.main()
