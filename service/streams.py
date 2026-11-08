"""Bounded, revision-aware inference sessions. A discarded response never moves
the client's accepted motion cursor; the next request can branch from that cursor.
"""
from collections import OrderedDict
import secrets
import threading
import time


class Streams:
    def __init__(self, provider, executor):
        self.provider=provider;self.executor=executor;self.lock=threading.Lock();self.sessions={}
    def create(self,seed):
        with self.lock:
            now=time.monotonic()
            self.sessions={k:v for k,v in self.sessions.items() if now-v['updated']<600}
            if len(self.sessions)>=8:raise OverflowError("Too many motion sessions")
            key=secrets.token_hex(12)
            self.sessions[key]={'states':OrderedDict(), 'seed':seed,'revision':0,'next':1,'updated':now,'closed':False}
            return {'session':key,'sequence':0}
    def close(self,key):
        with self.lock:
            session=self.sessions.pop(key,None)
            if session:session['closed']=True
    def step(self,key,request):
        base=request['sequence'];revision=request['revision']
        with self.lock:
            session=self.sessions.get(key)
            if session is None:raise KeyError("Session expired")
            if revision<session['revision']:raise ValueError("Stale revision")
            session['revision']=revision;session['updated']=time.monotonic()
        # Shares the single-GPU queue with one-shot requests.
        return self.executor.submit(self._run,session,base,revision,request['prompt']).result(timeout=180)
    def _run(self,session,base,revision,prompt):
        with self.lock:
            if session['closed'] or session['revision']!=revision:raise ValueError("Stale revision")
            saved=session['states'].get(base)
            if base and saved is None:raise KeyError("Motion cursor expired")
        if saved is None:
            state=self.provider.new_state(session['seed'])
        else:
            state=dict(saved);state['features']=list(saved['features'])
        result=self.provider.step(prompt,state)
        with self.lock:
            if session['closed'] or session['revision']!=revision:raise ValueError("Stale revision")
            sequence=session['next'];session['next']+=1
            session['states'][sequence]=state
            while len(session['states'])>4:session['states'].popitem(last=False)
            result['sequence']=sequence;result['revision']=revision
        return result
