import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

test('unified service starts without a second GUI, rejects unauthenticated shutdown and exits with its owner', { timeout: 20000 }, async t => {
 const temp=fs.mkdtempSync(path.join(os.tmpdir(),'yachiyo-owner-test-')),state=path.join(temp,'run/ready.json');
 const parent=spawn(process.execPath,['-e','setInterval(()=>{},1000)'],{stdio:'ignore'});
 const service=spawn(process.execPath,[path.resolve(import.meta.dirname,'../headless.mjs'),'--parent-pid',String(parent.pid),'--state',state,'--port','0'],{stdio:'ignore',env:{...process.env,YACHIYO_DATA_DIR:path.join(temp,'data')}});
 t.after(()=>{parent.kill();service.kill();fs.rmSync(temp,{recursive:true,force:true});});
 for(let n=0;n<150&&!fs.existsSync(state);n++)await new Promise(r=>setTimeout(r,50));
 assert(fs.existsSync(state),'owned runtime failed to become ready');
 const {origin,token}=JSON.parse(fs.readFileSync(state));
 assert.equal((await fetch(origin+'/healthz')).status,200);
 const forbidden=await fetch(origin+'/api/app/shutdown',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({token:'wrong-token'})});
 assert.equal(forbidden.status,403);assert.equal((await fetch(origin+'/healthz')).status,200);
 assert.equal(typeof token,'string');
 const exited=once(service,'exit');parent.kill();
 assert.equal((await exited)[0],0);assert(!fs.existsSync(state),'run token must be removed on exit');
 await assert.rejects(fetch(origin+'/healthz'));
});
