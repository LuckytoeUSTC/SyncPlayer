import { Miniflare, convertV4MiniflareOptions } from 'miniflare';
import assert from 'node:assert/strict';
import { test } from 'node:test';
const wait = ms => new Promise(r=>setTimeout(r,ms));
const mf = new Miniflare(convertV4MiniflareOptions({ workers:[{ modules:true, scriptPath:'worker.js', compatibilityDate:'2026-10-08', compatibilityFlags:['nodejs_compat'], durableObjects:{ROOMS:{className:'Room',useSQLite:true}}, ratelimits:{CREATE_LIMIT:{namespace_id:'1001',simple:{limit:10,period:60}}}}] }));
const sockets=[];
async function open(code, role, token) {
  const r=await mf.dispatchFetch(`https://relay/rooms/${code}/${role}`,{headers:{Upgrade:'websocket',Authorization:`Bearer ${token}`}});
  if(!r.webSocket) return r;
  const ws=r.webSocket; ws.accept(); ws.messages=[];
  ws.addEventListener('message', e=>ws.messages.push(JSON.parse(e.data)));
  sockets.push(ws); return ws;
}
async function until(fn) { for(let i=0;i<100;i++) { const v=fn(); if(v)return v; await wait(20); } throw new Error('timeout'); }
const hello=(ws,id)=>ws.send(JSON.stringify({Type:'hello',Id:id,Name:id,Windows:[{Id:'win',Name:'video',Primary:true,Ready:true}]}));
const send=(ws,p)=>ws.send(JSON.stringify(p));
const control=(session,seq,cur=12000)=>({Type:'control',Session:session,Seq:seq,Event:{type:'event',cur,state:2,speed:1500},Target:{Id:'win',Offset:500,Muted:true}});
test('real Durable Object pairing, direction, replay, rejection and reconnect',async()=>{
 try {
  const created=await (await mf.dispatchFetch('https://relay/rooms',{method:'POST'})).json();
  assert.equal(created.code.length,12);
  assert.equal((await open(created.code,'owner','0'.repeat(32))).status,403);
  const master=await open(created.code,'owner',created.token); hello(master,'master');
  const guestToken='1'.repeat(32); const guest=await open(created.code,'guest',guestToken); hello(guest,'guest');
  const req=await until(()=>master.messages.find(p=>p.Type==='request'));
  send(guest,{Type:'ping',Session:'fake',Nonce:'a'.repeat(32)});await wait(50);assert(!master.messages.some(p=>p.Type==='ping'));
  send(master,control('fake',1)); await wait(70); assert(!guest.messages.some(p=>p.Type==='control'));
  send(master,{Type:'accept',Nonce:req.Nonce});
  const a=await until(()=>master.messages.find(p=>p.Type==='paired'));
  const b=await until(()=>guest.messages.find(p=>p.Type==='paired')); assert.equal(a.Session,b.Session);
  send(master,{Type:'ping',Session:'fake',Nonce:'a'.repeat(32)});
  send(master,{Type:'ping',Session:a.Session,Nonce:'invalid'});await wait(50);assert(!guest.messages.some(p=>p.Type==='ping'));
  send(master,{Type:'ping',Session:a.Session,Nonce:'a'.repeat(32)});
  const ping=await until(()=>guest.messages.find(p=>p.Type==='ping'));assert.equal(ping.Nonce,'a'.repeat(32));assert(!master.messages.some(p=>p.Type==='pong'));
  send(guest,{Type:'pong',Session:a.Session,Nonce:ping.Nonce});await until(()=>master.messages.find(p=>p.Type==='pong'));
  send(guest,{Type:'ping',Session:a.Session,Nonce:'b'.repeat(32)});await until(()=>master.messages.find(p=>p.Type==='ping' && p.Nonce==='b'.repeat(32)));
  assert.equal((await open(created.code,'guest','2'.repeat(32))).status,409);
  send(guest,control(a.Session,1)); await wait(70); assert(!master.messages.some(p=>p.Type==='control'));
  send(master,control(a.Session,1)); const c=await until(()=>guest.messages.find(p=>p.Type==='control')); assert.equal(c.Event.cur,12000); assert.equal(c.Target.Offset,500);
  send(master,control(a.Session,1,22000)); send(master,control('old-session',2)); await wait(70); assert.equal(guest.messages.filter(p=>p.Type==='control').length,1);
  send(master,control(a.Session,2,-1)); await wait(70); assert.equal(guest.messages.filter(p=>p.Type==='control').length,1);
  guest.close(1000,'test reconnect'); await until(()=>master.messages.find(p=>p.Type==='offline'));
  const rejoined=await open(created.code,'guest',guestToken); hello(rejoined,'guest');
  const fresh=await until(()=>rejoined.messages.find(p=>p.Type==='paired')); assert.notEqual(fresh.Session,a.Session);
  send(master,control(a.Session,3)); await wait(70); assert(!rejoined.messages.some(p=>p.Type==='control'));
  for(let n=1;n<=20;n++)send(master,control(fresh.Session,n,n*1000));
  await until(()=>rejoined.messages.filter(p=>p.Type==='control').length===20); assert.equal(rejoined.messages.at(-1).Event.cur,20000);
  // Denied and forged approval must not change the controller.
  send(rejoined,{Type:'master-request',Session:fresh.Session});
  const swapRequest=await until(()=>master.messages.find(p=>p.Type==='master-request'));
  send(rejoined,{Type:'master-accept',Nonce:swapRequest.Nonce,Session:fresh.Session});await wait(50);
  assert.equal(rejoined.messages.filter(p=>p.Type==='paired').length,1);
  send(master,{Type:'master-reject',Nonce:swapRequest.Nonce,Session:fresh.Session});await until(()=>rejoined.messages.find(p=>p.Type==='master-denied'));
  master.messages=master.messages.filter(p=>p.Type!=='master-request');
  send(rejoined,{Type:'master-request',Session:fresh.Session});
  const acceptedRequest=await until(()=>master.messages.find(p=>p.Type==='master-request'));
  send(master,{Type:'master-accept',Nonce:acceptedRequest.Nonce,Session:fresh.Session});
  const swapped=await until(()=>rejoined.messages.find(p=>p.Type==='paired' && p.Session!==fresh.Session));
  assert.equal(swapped.CanControl,true);assert.equal(master.messages.at(-1).CanControl,false);
  const countBefore=rejoined.messages.filter(p=>p.Type==='control').length;
  send(master,control(fresh.Session,99));send(master,control(swapped.Session,1));await wait(70);assert.equal(rejoined.messages.filter(p=>p.Type==='control').length,countBefore);
  send(rejoined,control(swapped.Session,1,33000));await until(()=>master.messages.some(p=>p.Type==='control' && p.Event.cur===33000));
  // Reconnect preserves the guest's granted control but replaces the session.
  rejoined.close(1000,'reconnect after swap');await until(()=>master.messages.filter(p=>p.Type==='offline').length===2);
  const active=await open(created.code,'guest',guestToken);hello(active,'guest');const retained=await until(()=>active.messages.find(p=>p.Type==='paired'));
  assert.equal(retained.CanControl,true);assert.notEqual(retained.Session,swapped.Session);
  send(active,control(retained.Session,1,44000));await until(()=>master.messages.some(p=>p.Type==='control' && p.Event.cur===44000));
  send(master,{Type:'master-request',Session:retained.Session});const back=await until(()=>active.messages.find(p=>p.Type==='master-request'));
  send(active,{Type:'master-accept',Nonce:back.Nonce,Session:retained.Session});
  const restored=await until(()=>master.messages.find(p=>p.Type==='paired' && p.Session!==retained.Session && p.Session!==swapped.Session && p.Session!==fresh.Session && p.Session!==a.Session));
  assert.equal(restored.CanControl,true);
  send(master,control(restored.Session,1,55000));await until(()=>active.messages.some(p=>p.Type==='control' && p.Event.cur===55000));
  const room2=await (await mf.dispatchFetch('https://relay/rooms',{method:'POST'})).json();
  const m2=await open(room2.code,'owner',room2.token);hello(m2,'master2');const g2=await open(room2.code,'guest','3'.repeat(32));hello(g2,'guest2');
  const request2=await until(()=>m2.messages.find(p=>p.Type==='request'));send(m2,{Type:'reject',Nonce:request2.Nonce});await until(()=>g2.messages.find(p=>p.Type==='rejected'));assert(!g2.messages.some(p=>p.Type==='paired'));
  const room3=await (await mf.dispatchFetch('https://relay/rooms',{method:'POST'})).json();
  const m3=await open(room3.code,'owner',room3.token);hello(m3,'selector');
  const waiting=[];
  for(let i=0;i<5;i++) { const pending=await open(room3.code,'guest',(i+4).toString(16).repeat(32));hello(pending,'waiting-'+i);waiting.push(pending); }
  await until(()=>m3.messages.filter(p=>p.Type==='request').length===5);
  waiting[0].close(1000,'withdraw');
  const withdrawn=m3.messages.find(p=>p.Type==='request' && p.Name==='waiting-0');
  await until(()=>m3.messages.find(p=>p.Type==='request-cancelled' && p.Nonce===withdrawn.Nonce));
  const chosen=m3.messages.find(p=>p.Type==='request' && p.Name==='waiting-3');
  send(m3,{Type:'accept',Nonce:chosen.Nonce});
  const selectedPair=await until(()=>waiting[3].messages.find(p=>p.Type==='paired'));
  const ownerPair=await until(()=>m3.messages.find(p=>p.Type==='paired'));assert.equal(ownerPair.Id,'waiting-3');
  for(const i of [1,2,4]) { await until(()=>waiting[i].messages.find(p=>p.Type==='rejected'));assert(!waiting[i].messages.some(p=>p.Type==='paired')); }
  await wait(70);assert(!m3.messages.some(p=>p.Type==='offline'));
  send(m3,control(selectedPair.Session,1,66000));await until(()=>waiting[3].messages.find(p=>p.Type==='control' && p.Event.cur===66000));
  assert.equal((await open(room3.code,'guest','f'.repeat(32))).status,409);
  console.log('PASS pairing, authorization, multiple applicants, withdrawal, chosen applicant, one-peer limit, RTT forwarding, swaps, reconnect and rapid seek');
 } finally {for(const ws of sockets)try{ws.close();}catch{}await mf.dispose();}
});




