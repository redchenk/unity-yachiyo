"""Optional, isolated forced aligner. No audio/text is written to disk.

One worker is owned by the motion service; its transformers version is isolated
from StreamTalk. Missing installation never blocks body generation.
"""
import atexit
import base64
from concurrent.futures import ThreadPoolExecutor
import json
import math
import os
from pathlib import Path
import queue
import subprocess
import sys
import threading
import unicodedata

ROOT = Path(__file__).resolve().parents[1]


def aligned_words(text, items, duration):
    """Map model tokens monotonically to exact UTF-16 spans; never fuzzy-match.

    Punctuation/space may be omitted by the tokenizer. Missing lexical content,
    invalid times, or a different spoken line fail closed rather than shifting a
    later repeated phrase onto the wrong occurrence.
    """
    def kept(c): return unicodedata.category(c)[0] in 'LN' or c == "'"
    offsets, chars, utf16 = [], [], 0
    for char in text:
        if kept(char): chars.append(char); offsets.append((utf16, utf16 + len(char.encode('utf-16-le')) // 2))
        utf16 += len(char.encode('utf-16-le')) // 2
    normalized = ''.join(chars)
    cursor, previous, words = 0, 0., []
    for item in items:
        token = ''.join(c for c in item['text'] if kept(c))
        if not token: continue
        start, end = float(item['start']), float(item['end'])
        if (not math.isfinite(start + end) or start < previous - .025 or
                end <= start or start < 0 or end > duration + .08 or
                not normalized.startswith(token, cursor)):
            raise ValueError('Alignment does not match the exact spoken line')
        stop = cursor + len(token)
        words.append(dict(text=item['text'], startOffset=offsets[cursor][0],
                          endOffset=offsets[stop-1][1], start=start, end=min(duration, end)))
        cursor, previous = stop, end
    if cursor != len(chars) or not words: raise ValueError('Incomplete alignment')
    return words


class SpeechAligner:
    def __init__(self, root=ROOT):
        self.root, self.process = Path(root), None
        self.state = 'unavailable'
        self.device = ''
        self.pool = ThreadPoolExecutor(max_workers=1, thread_name_prefix='speech-alignment')
        self.outputs = queue.Queue()
        if (self.root / 'models/forced-aligner/config.json').is_file() and (self.root / 'runtime-alignment/site-packages/qwen_asr').is_dir():
            self.state = 'loading'
            self.pool.submit(self._warm)
        atexit.register(self.close)

    def _warm(self):
        try:
            env = dict(os.environ, PYTHONUTF8='1', HF_HUB_OFFLINE='1', TOKENIZERS_PARALLELISM='false')
            self.process = subprocess.Popen([sys.executable, str(self.root / 'service/alignment_worker.py')],
                stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                text=True, encoding='utf-8', env=env,
                creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
            def reader():
                for line in self.process.stdout:
                    try: self.outputs.put(json.loads(line))
                    except ValueError: continue  # third-party progress is not protocol
                self.outputs.put({'error': 'worker-exited'})
            threading.Thread(target=reader, daemon=True).start()
            ready = self.outputs.get(timeout=120)
            if ready.get('ready') is not True: raise RuntimeError('Worker unavailable')
            self.device = ready.get('device', '')
            self.state = 'ready'
        except Exception:
            self.close(); self.state = 'unavailable'

    def submit(self, text, samples, cancelled):
        if self.state == 'unavailable': return None
        return self.pool.submit(self._align, text, samples, cancelled)

    def _align(self, text, samples, cancelled):
        if cancelled() or self.state != 'ready': return None
        import numpy as np
        pcm = np.clip(samples * 32768, -32768, 32767).astype('<i2').tobytes()
        try:
            self.process.stdin.write(json.dumps({'text': text, 'pcm16': base64.b64encode(pcm).decode()}, ensure_ascii=False) + '\n')
            self.process.stdin.flush()
            result = self.outputs.get(timeout=25)
            if cancelled() or 'error' in result: return None
            try:
                words = aligned_words(text, result['words'], len(samples) / 16000)
                return dict(text=text, words=words, source='qwen3-forced-aligner', seconds=result['seconds'])
            except (ValueError, KeyError, TypeError):
                # One imperfect alignment must not disable subsequent speech.
                return None
        except Exception:
            self.close(); self.state = 'unavailable'
            return None

    def close(self):
        if self.process and self.process.poll() is None:
            self.process.terminate()
            try: self.process.wait(timeout=3)
            except subprocess.TimeoutExpired: self.process.kill()
        self.pool.shutdown(wait=False, cancel_futures=True)
