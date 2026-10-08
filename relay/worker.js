import { DurableObject } from 'cloudflare:workers';
const alphabet = '23456789ABCDEFGHJKLMNPQRSTUVWXYZ';
const randomCode = () => Array.from(crypto.getRandomValues(new Uint8Array(12)), x => alphabet[x % 32]).join('');
const reply = (body, status = 200) => Response.json(body, { status, headers: { 'Cache-Control': 'no-store' } });
function equal(a, b) {
  if (typeof a !== 'string' || typeof b !== 'string' || a.length !== b.length) return false;
  return crypto.subtle.timingSafeEqual(new TextEncoder().encode(a), new TextEncoder().encode(b));
}
function validHello(p) {
  return typeof p.Id === 'string' && p.Id.length > 0 && p.Id.length <= 64 && typeof p.Name === 'string' && p.Name.length <= 32 && Array.isArray(p.Windows) && p.Windows.length <= 1 && p.Windows.every(w => w && typeof w.Id === 'string' && w.Id.length <= 64 && typeof w.Name === 'string' && w.Name.length <= 1000 && w.Primary === true && typeof w.Ready === 'boolean');
}
function validControl(p) {
  const e = p.Event, t = p.Target;
  return Number.isSafeInteger(p.Seq) && p.Seq > 0 && t && typeof t.Id === 'string' && t.Id.length <= 64 && Number.isInteger(t.Offset) && Math.abs(t.Offset) <= 86400000 && (t.Muted == null || typeof t.Muted === 'boolean') && (e == null || (typeof e === 'object' && (e.cur == null || Number.isInteger(e.cur) && e.cur >= 0 && e.cur <= 2147483647) && (e.state == null || [-1,0,1,2].includes(e.state)) && (e.speed == null || Number.isInteger(e.speed) && e.speed >= 200 && e.speed <= 12000)));
}
export class Room extends DurableObject {
  async fetch(request) {
    const path = new URL(request.url).pathname;
    if (path === '/init' && request.method === 'POST') {
      if (await this.ctx.storage.get('room')) return reply({ error: 'collision' }, 409);
      const room = { token: crypto.randomUUID().replaceAll('-', ''), joinUntil: Date.now() + 600000, expires: Date.now() + 28800000, attempts: 0, guestToken: '', controller: 'owner' };
      await this.ctx.storage.put('room', room);
      await this.ctx.storage.setAlarm(room.expires);
      return reply({ token: room.token });
    }
    const room = await this.ctx.storage.get('room');
    if (!room || Date.now() > room.expires) return reply({ error: 'room expired' }, 410);
    if (request.headers.get('Upgrade')?.toLowerCase() !== 'websocket') return reply({ error: 'websocket required' }, 426);
    const role = path.endsWith('/owner') ? 'owner' : 'guest';
    const token = (request.headers.get('Authorization') || '').replace(/^Bearer /, '');
    if (!/^[a-f0-9]{32}$/.test(token)) return reply({ error: 'invalid identity' }, 403);
    if (role === 'owner' && !equal(token, room.token)) return reply({ error: 'unauthorized' }, 403);
    if (role === 'guest') {
      if (room.guestToken && !equal(token, room.guestToken)) return reply({ error: 'room full' }, 409);
      if (!room.guestToken) {
        if (Date.now() > room.joinUntil || room.attempts >= 10) return reply({ error: 'join expired' }, 403);
        room.attempts++;
        await this.ctx.storage.put('room', room);
      }
    }
    if (role === 'owner' ? this.ctx.getWebSockets(role).length : this.ctx.getWebSockets(role).some(ws => equal(ws.deserializeAttachment().token, token))) return reply({ error: 'device already online' }, 409);
    if (role === 'guest' && !this.ctx.getWebSockets('owner').length) return reply({ error: 'master offline' }, 409);
    const [client, server] = Object.values(new WebSocketPair());
    this.ctx.acceptWebSocket(server, [role]);
    server.serializeAttachment({ role, token, accepted: role === 'owner' || equal(token, room.guestToken), identity: null, session: '', lastSeq: 0, nonce: crypto.randomUUID(), count: 0, second: 0 });
    return new Response(null, { status: 101, webSocket: client });
  }
  send(ws, p) { try { ws.send(JSON.stringify(p)); } catch { ws.close(1011, 'send failed'); } }
  guest() { return this.ctx.getWebSockets('guest').find(ws => ws.deserializeAttachment().accepted); }
  async pair(room = null, transfer = false) {
    room ??= await this.ctx.storage.get('room');
    const owner = this.ctx.getWebSockets('owner')[0], guest = this.guest();
    if (!owner || !guest) return;
    const a = owner.deserializeAttachment(), b = guest.deserializeAttachment();
    if (!a.identity || !b.identity || !b.accepted) return;
    const session = crypto.randomUUID();
    a.session = b.session = session; a.lastSeq = b.lastSeq = 0;
    a.canControl = (room.controller || 'owner') === 'owner'; b.canControl = !a.canControl; a.transfer = b.transfer = null;
    owner.serializeAttachment(a); guest.serializeAttachment(b);
    this.send(owner, { Type: 'paired', Session: session, MasterChanged: transfer, CanControl: a.canControl, ...b.identity });
    this.send(guest, { Type: 'paired', Session: session, MasterChanged: transfer, CanControl: b.canControl, ...a.identity });
  }
  async webSocketMessage(ws, message) {
    if (typeof message !== 'string' || message.length > 60000) { ws.close(1009, 'invalid message'); return; }
    const a = ws.deserializeAttachment();
    const second = Math.floor(Date.now()/1000);
    a.count = a.second === second ? a.count + 1 : 1; a.second = second;
    if (a.count > 100) { ws.close(1008, 'rate limit'); return; }
    ws.serializeAttachment(a);
    let p; try { p = JSON.parse(message); } catch { ws.close(1008, 'invalid json'); return; }
    if (!p || typeof p !== 'object') return;
    if (p.Type === 'hello') {
      if (!validHello(p)) { ws.close(1008, 'invalid identity'); return; }
      const first = !a.identity;
      a.identity = { Id:p.Id, Name:p.Name, Windows:p.Windows };
      ws.serializeAttachment(a);
      const other = a.role === 'owner' ? this.guest() : this.ctx.getWebSockets('owner')[0];
      if (a.role === 'guest' && !a.accepted) { if(other) this.send(other, { Type:'request', Nonce:a.nonce, Name:p.Name }); }
      else if(first) await this.pair();
      else if(other && a.session) this.send(other, { Type:'hello', ...a.identity });
      // Owner may reconnect while an unaccepted guest is waiting.
      if (first && a.role === 'owner') for(const pending of this.ctx.getWebSockets('guest')) { const b=pending.deserializeAttachment(); if(!b.accepted && b.identity) this.send(ws,{Type:'request',Nonce:b.nonce,Name:b.identity.Name}); }
      return;
    }
    if (a.role === 'owner' && ['accept','reject'].includes(p.Type)) {
      const guest = this.ctx.getWebSockets('guest').find(s => { const b=s.deserializeAttachment(); return !b.accepted && b.nonce === p.Nonce; }); if(!guest) return;
      const b = guest.deserializeAttachment(); if(p.Nonce !== b.nonce || b.accepted) return;
      if(p.Type === 'reject') { this.send(guest,{Type:'rejected'}); guest.close(1000,'rejected'); return; }
      const room = await this.ctx.storage.get('room'); if(room.guestToken)return; room.guestToken=b.token; await this.ctx.storage.put('room',room);
      b.accepted=true; guest.serializeAttachment(b);
      for(const pending of this.ctx.getWebSockets('guest')) if(pending !== guest) { this.send(pending,{Type:'rejected'}); pending.close(1000,'another device accepted'); }
      await this.pair(); return;
    }
    if (p.Type === 'master-request') {
      if (!a.accepted || a.canControl || !a.session || p.Session !== a.session) return;
      const other = a.role === 'owner' ? this.guest() : this.ctx.getWebSockets('owner')[0]; if(!other) return;
      const b = other.deserializeAttachment(); if(!b.canControl || b.session !== a.session) return;
      if(b.transfer && b.transfer.until > Date.now()) return;
      b.transfer = { nonce:crypto.randomUUID(), session:a.session, from:a.role, until:Date.now()+30000 };
      other.serializeAttachment(b);
      this.send(other,{Type:'master-request',Nonce:b.transfer.nonce,Name:a.identity.Name,Session:a.session});
      this.send(ws,{Type:'master-pending',Nonce:b.transfer.nonce});
      return;
    }
    if (['master-accept','master-reject'].includes(p.Type)) {
      const t = a.transfer;
      if(!a.canControl || !t || t.nonce !== p.Nonce || t.session !== a.session || p.Session !== a.session) return;
      const other = this.ctx.getWebSockets(t.from)[0];
      a.transfer=null; ws.serializeAttachment(a);
      if(!other) return;
      if(p.Type === 'master-reject' || t.until <= Date.now()) { this.send(other,{Type:'master-denied'}); return; }
      const b=other.deserializeAttachment(); if(!b.accepted || b.session !== a.session) return;
      const room = await this.ctx.storage.get('room'); room.controller=t.from;
      await this.ctx.storage.put('room',room);
      await this.pair(room, true); return;
    }
    if (p.Type === 'ping' || p.Type === 'pong') {
      if(!a.accepted || !a.session || p.Session !== a.session || typeof p.Nonce !== 'string' || !/^[a-f0-9]{32}$/.test(p.Nonce)) return;
      const other = a.role === 'owner' ? this.guest() : this.ctx.getWebSockets('owner')[0]; if(!other) return;
      const b = other.deserializeAttachment(); if(!b.accepted || b.session !== a.session) return;
      this.send(other,{Type:p.Type,Session:a.session,Nonce:p.Nonce}); return;
    }
    if (p.Type === 'control') {
      if(!a.canControl || !a.session || p.Session !== a.session || p.Seq <= a.lastSeq || !validControl(p)) return;
      const guest = a.role === 'owner' ? this.guest() : this.ctx.getWebSockets('owner')[0]; if(!guest) return;
      const b = guest.deserializeAttachment(); if(!b.accepted || b.canControl || b.session !== a.session) return;
      if(!b.identity?.Windows.some(w=>w.Primary && w.Ready && w.Id === p.Target.Id))return;
      a.lastSeq=p.Seq; ws.serializeAttachment(a);
      this.send(guest,{Type:'control',Session:a.session,Seq:p.Seq,Event:p.Event,Target:p.Target});
    }
  }
  webSocketClose(ws, code, reason) {
    const a = ws.deserializeAttachment(); ws.close(code === 1006 || code === 1005 ? 1000 : code, reason);
    if(!a.accepted) { for(const owner of this.ctx.getWebSockets('owner')) this.send(owner,{Type:'request-cancelled',Nonce:a.nonce}); return; }
    for(const other of this.ctx.getWebSockets(a.role === 'owner' ? 'guest' : 'owner')) { const b=other.deserializeAttachment(); if(!b.accepted)continue; b.session=''; b.transfer=null; other.serializeAttachment(b); this.send(other,{Type:'offline'}); }
  }
  webSocketError(ws) { this.webSocketClose(ws,1011,'connection error'); }
  async alarm() { for(const ws of this.ctx.getWebSockets()) ws.close(1000,'room expired'); await this.ctx.storage.deleteAll(); }
}
export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if(url.pathname === '/health') return reply({service:'SyncPlayer',version:1});
    if(url.pathname === '/rooms' && request.method === 'POST') {
      if(!await env.CREATE_LIMIT.limit({key: request.headers.get('CF-Connecting-IP') || 'local'}).then(r=>r.success)) return reply({error:'rate limit'},429);
      for(let i=0;i<3;i++) {
        const code = randomCode();
        const result = await env.ROOMS.getByName(code).fetch(new Request('https://room/init',{method:'POST'}));
        if(result.ok) { const {token} = await result.json(); return reply({code,token}); }
      }
      return reply({error:'try again'},503);
    }
    if(/^\/rooms\/[23456789ABCDEFGHJKLMNPQRSTUVWXYZ]{12}\/(owner|guest)$/.test(url.pathname)) {
      const code = url.pathname.split('/')[2];
      return env.ROOMS.getByName(code).fetch(request);
    }
    return reply({error:'not found'},404);
  }
};



