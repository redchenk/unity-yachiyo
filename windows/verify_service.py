"""Exercise real model inference through the same streaming HTTP API as Unity."""
import argparse
import hashlib
import json
import math
from pathlib import Path
import time
from urllib.request import Request, build_opener, ProxyHandler
from urllib.error import HTTPError


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--base', default='http://127.0.0.1:8787')
    parser.add_argument('--output', type=Path, default=Path(__file__).resolve().parents[1] / 'evidence/inference.json')
    args = parser.parse_args()
    http = build_opener(ProxyHandler({}))
    def request(method, route, payload=None):
        body = None if payload is None else json.dumps(payload).encode()
        with http.open(Request(args.base + route, body, {'Content-Type': 'application/json'}, method=method), timeout=180) as response:
            return json.load(response)
    health = request('GET', '/health')
    assert health['provider'] == 'dip' and health['state'] == 'ready' and health['streaming']
    session = request('POST', '/streams', {'seed': 20261107})['session']
    sequence = 0
    clips = []
    checks = []
    try:
        for revision, prompt in enumerate(['挥手', '向前走', '站着'], 1):
            started = time.perf_counter()
            clip = request('POST', '/streams/' + session + '/step',
                           {'sequence': sequence, 'revision': revision, 'prompt': prompt})
            elapsed = time.perf_counter() - started
            assert clip['sequence'] > sequence and clip['revision'] == revision
            assert clip['fps'] == 20 and len(clip['frames']) == 40
            assert 'DiP' in clip['source']
            for frame in clip['frames']:
                assert all(math.isfinite(x) for x in frame['root'].values())
                assert len(frame['rotations']) == len(clip['bones'])
                for q in frame['rotations']:
                    assert all(math.isfinite(x) for x in q.values())
                    assert abs(sum(x*x for x in q.values()) - 1) < .002
            boundary = 0
            if clips:
                a = clips[-1]['frames'][-1]['root']
                b = clip['frames'][0]['root']
                boundary = math.sqrt(sum((a[k]-b[k])**2 for k in 'xyz'))
                assert boundary < .2
            checks.append(dict(prompt=prompt, sequence=clip['sequence'], frames=40,
                               http_seconds=elapsed, generation_seconds=clip['generationSeconds'],
                               boundary_step_m=boundary,
                               motion_sha256=hashlib.sha256(json.dumps(clip['frames'], sort_keys=True).encode()).hexdigest()))
            sequence = clip['sequence']
            clips.append(clip)
    finally:
        request('DELETE', '/streams/' + session)
    assert len({item['motion_sha256'] for item in checks}) == len(checks)
    stopped = False
    try:
        request('POST', '/streams/' + session + '/step',
                {'sequence': sequence, 'revision': 4, 'prompt': '站着'})
    except HTTPError as error:
        stopped = error.code == 404
    assert stopped, 'A stopped session still accepted inference'
    result = dict(health=health, fresh_generation=True, finite=True, checks=checks,
                  stopped_session=stopped)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2, ensure_ascii=False), encoding='utf-8')
    args.output.with_name('last-generated-motion.json').write_text(json.dumps(clips[-1]), encoding='utf-8')
    print(json.dumps(result, indent=2, ensure_ascii=False))


if __name__ == '__main__':
    main()
