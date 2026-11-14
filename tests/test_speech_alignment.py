from concurrent.futures import Future, ThreadPoolExecutor
import base64
import threading
import time
import unittest
from service.speech_alignment import aligned_words
from service.speech_jobs import SpeechJobs


class AlignmentTest(unittest.TestCase):
    def test_exact_japanese_utf16_spans_repeated_words_and_punctuation(self):
        words = aligned_words('🌙はい、はい。大丈夫。', [
            dict(text='はい', start=.1, end=.5), dict(text='はい', start=.6, end=1),
            dict(text='大丈夫', start=1.1, end=1.8)], 2)
        self.assertEqual([(w['startOffset'], w['endOffset']) for w in words], [(2,4),(5,7),(8,11)])
        self.assertEqual(words[-1]['start'], 1.1)

    def test_mismatch_missing_tokens_and_invalid_clock_fail_closed(self):
        for items in ([dict(text='大丈夫', start=0, end=.5)],
                      [dict(text='はい', start=.6, end=.5)],
                      [dict(text='はい', start=0, end=5)],
                      [dict(text='はい', start=float('nan'), end=1)],
                      [dict(text='は', start=0, end=1)]):
            with self.assertRaises(ValueError): aligned_words('はい', items, 2)
        with self.assertRaises(ValueError):
            aligned_words('はいはい', [dict(text='はい', start=.5, end=1),dict(text='はい',start=.2,end=.4)], 2)

    def test_alignment_is_parallel_optional_and_bound_to_the_job(self):
        future = Future()
        class Aligner:
            def submit(self, text, samples, cancelled):
                self.text, self.cancelled = text, cancelled
                return future
        class Provider:
            def generate(self, *args): yield dict(startFrame=0, frames=list(range(30)), source='fixture')
        with ThreadPoolExecutor(max_workers=1) as pool:
            aligner=Aligner(); jobs=SpeechJobs(Provider(),pool,aligner)
            job=jobs.submit(dict(sampleRate=16000,pcm16=base64.b64encode(bytes(32000)).decode(),spokenText='大丈夫'))
            for _ in range(100):
                result=jobs.get(job['id'])
                if result['status']=='done':break
                time.sleep(.005)
            self.assertEqual(result['status'],'done')
            self.assertEqual(result['alignmentStatus'],'pending')
            self.assertEqual(len(result['motion']['frames']),30)
            future.set_result(dict(text='大丈夫', words=[dict(start=0,end=1,startOffset=0,endOffset=3)]))
            self.assertEqual(jobs.get(job['id'])['alignment']['text'],'大丈夫')
            jobs.cancel(job['id'])
            self.assertTrue(aligner.cancelled())
            self.assertIsNone(jobs.get(job['id']))

if __name__ == '__main__': unittest.main()
