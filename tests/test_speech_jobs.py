import base64
from concurrent.futures import ThreadPoolExecutor
import threading
import time
import unittest
from service.speech_jobs import SpeechJobs


class SpeechJobsTest(unittest.TestCase):
    def test_partial_frames_cancel_and_late_result(self):
        release = threading.Event()
        class Provider:
            state = 'ready'
            def generate(self, audio, seed, cancelled):
                yield dict(startFrame=0, frames=list(range(52)), source='test')
                release.wait(2)
                yield dict(startFrame=52, frames=list(range(52,105)), source='test')
        with ThreadPoolExecutor(max_workers=1) as pool:
            jobs=SpeechJobs(Provider(),pool)
            job=jobs.submit(dict(sampleRate=16000,pcm16=base64.b64encode(bytes(3200)).decode()))
            for _ in range(100):
                first=jobs.get(job['id'])
                if first['availableFrames']:break
                time.sleep(.005)
            self.assertEqual(first['motion']['frames'],list(range(52)))
            release.set()
            for _ in range(100):
                rest=jobs.get(job['id'],52)
                if rest['status']=='done':break
                time.sleep(.005)
            self.assertEqual(rest['motion']['frames'],list(range(52,105)))
            jobs.cancel(job['id'])
            self.assertIsNone(jobs.get(job['id']))

    def test_invalid_audio_is_rejected_before_gpu_queue(self):
        with ThreadPoolExecutor(max_workers=1) as pool:
            jobs=SpeechJobs(None,pool)
            for raw in ({},dict(sampleRate=48000,pcm16=''),dict(sampleRate=16000,pcm16='invalid!'),dict(sampleRate=16000,pcm16='AAAA')):
                with self.assertRaises(ValueError): jobs.submit(raw)


if __name__=='__main__':unittest.main()
