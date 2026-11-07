'use strict';
// Room relay: the host (id 0) owns the room, up to MAX_PLAYERS sockets per room.
// The relay only forwards; game authority lives on the host client.
const crypto = require('crypto');
const { WebSocketServer } = require('ws');

const MAX_PLAYERS = 4;
const MAX_MESSAGE_BYTES = 64 * 1024;
const CODE_LETTERS = 'ABCDEFGHJKLMNPQRSTUVWXYZ';
// 24^6 codes: with the failed-join limit a stranger cannot find a live room by guessing.
const CODE_LENGTH = 6;
// After a host goes away its code is kept this long for it to claim again (its token proves it is the same host).
const RESERVE_MS = 120 * 1000;
// At most this many codes are held for returning hosts (a host leaving over and over must not fill the code space).
const MAX_RESERVED = 256;
const PING_MS = 20 * 1000;
// A socket sending more than this many messages in one second, or failing this many joins, is dropped.
const MAX_MESSAGES_PER_SECOND = 200;
const MAX_FAILED_JOINS = 10;

function startRelay({ port = Number(process.env.PORT) || 8787, host, reserveMs = RESERVE_MS, pingMs = PING_MS, maxMessagesPerSecond = MAX_MESSAGES_PER_SECOND } = {}) {
  const rooms = new Map(); // code -> { players: Map<id, ws> }
  const leases = new Map(); // character id -> the socket playing it (one live session per character)
  const reserved = new Map(); // code -> { token, timer }: a room whose host left and may come back
  const wss = new WebSocketServer({ port, host, maxPayload: MAX_MESSAGE_BYTES });

  const send = (ws, obj) => { if (ws.readyState === 1) ws.send(JSON.stringify(obj)); };

  function newCode() {
    for (let tries = 0; tries < 10000; tries++) {
      let code = '';
      for (let i = 0; i < CODE_LENGTH; i++) code += CODE_LETTERS[Math.floor(Math.random() * CODE_LETTERS.length)];
      if (!rooms.has(code) && !reserved.has(code)) return code;
    }
  }

  // A character plays in one room at a time. False when another live socket holds it.
  function lease(ws, chr) {
    if (typeof chr !== 'string' || chr.length === 0) return true;
    if (chr.length > 64) return false;
    const holder = leases.get(chr);
    if (holder && holder !== ws && holder.readyState === 1) return false;
    leases.set(chr, ws);
    ws.lease = chr;
    return true;
  }

  function release(ws) {
    if (ws.lease && leases.get(ws.lease) === ws) leases.delete(ws.lease);
    ws.lease = null;
  }

  function leave(ws) {
    release(ws);
    const room = rooms.get(ws.room);
    if (!room) return;
    room.players.delete(ws.pid);
    if (ws.pid === 0) {
      for (const peer of room.players.values()) { send(peer, { t: 'closed' }); peer.room = null; release(peer); }
      rooms.delete(ws.room);
      const code = ws.room;
      if (reserved.size < MAX_RESERVED) {
        const timer = setTimeout(() => reserved.delete(code), reserveMs);
        timer.unref();
        reserved.set(code, { token: room.token, timer });
      }
    } else {
      for (const peer of room.players.values()) send(peer, { t: 'left', id: ws.pid });
    }
    ws.room = null;
  }

  // A socket that stops answering pings (cable pulled, no close frame) is dropped so its room and lease free up.
  const heartbeat = setInterval(() => {
    for (const c of wss.clients) {
      if (c.alive === false) { c.terminate(); continue; }
      c.alive = false;
      c.ping();
    }
  }, pingMs);
  heartbeat.unref();
  wss.on('close', () => clearInterval(heartbeat));

  wss.on('connection', (ws) => {
    ws.room = null;
    ws.pid = -1;
    ws.alive = true;
    ws.failedJoins = 0;
    ws.windowStart = Date.now();
    ws.windowCount = 0;
    ws.on('pong', () => { ws.alive = true; });
    ws.on('message', (raw) => {
      const now = Date.now();
      if (now - ws.windowStart >= 1000) { ws.windowStart = now; ws.windowCount = 0; }
      if (++ws.windowCount > maxMessagesPerSecond) return ws.terminate();
      let m;
      try { m = JSON.parse(raw.toString()); } catch { return send(ws, { t: 'err', m: 'bad json' }); }
      if (!m || typeof m !== 'object') return send(ws, { t: 'err', m: 'bad message' });
      if (m.t === 'host' && !ws.room) {
        if (!lease(ws, m.chr)) return send(ws, { t: 'err', m: 'character in use' });
        // A host that lost its room may claim the same code once, with the token it was given.
        const claim = typeof m.code === 'string' ? m.code.toUpperCase() : null;
        const held = claim ? reserved.get(claim) : null;
        let code;
        if (held && typeof m.token === 'string' && m.token === held.token && !rooms.has(claim)) {
          clearTimeout(held.timer);
          reserved.delete(claim);
          code = claim;
        } else {
          code = newCode();
        }
        if (!code) { release(ws); return send(ws, { t: 'err', m: 'no free room code' }); }
        const token = crypto.randomBytes(12).toString('hex');
        rooms.set(code, { players: new Map([[0, ws]]), token });
        ws.room = code; ws.pid = 0;
        send(ws, { t: 'room', code, id: 0, token });
      } else if (m.t === 'join' && !ws.room) {
        const wanted = String(m.code || '').toUpperCase();
        const room = rooms.get(wanted);
        if (!room) {
          // Guessing codes: a socket that keeps asking for rooms that do not exist is dropped.
          if (!reserved.has(wanted) && ++ws.failedJoins > MAX_FAILED_JOINS) return ws.terminate();
          return send(ws, { t: 'err', m: reserved.has(wanted) ? 'host down' : 'no such room' });
        }
        if (room.players.size >= MAX_PLAYERS) return send(ws, { t: 'err', m: 'room full' });
        if (!lease(ws, m.chr)) return send(ws, { t: 'err', m: 'character in use' });
        let id = 1;
        while (room.players.has(id)) id++;
        for (const peer of room.players.values()) send(peer, { t: 'peer', id });
        ws.room = String(m.code).toUpperCase(); ws.pid = id;
        send(ws, { t: 'joined', code: ws.room, id, peers: [...room.players.keys()] });
        room.players.set(id, ws);
      } else if (m.t === 'msg' && ws.room) {
        const room = rooms.get(ws.room);
        if (typeof m.d !== 'string') return;
        const out = { t: 'msg', from: ws.pid, d: m.d };
        if (m.to === -1 || m.to === undefined) {
          for (const [id, peer] of room.players) if (id !== ws.pid) send(peer, out);
        } else {
          const peer = room.players.get(m.to);
          if (peer && peer !== ws) send(peer, out);
        }
      }
    });
    ws.on('close', () => leave(ws));
    ws.on('error', () => leave(ws));
  });

  return new Promise((resolve) => wss.on('listening', () => resolve({
    port: wss.address().port,
    roomCount: () => rooms.size,
    close: () => new Promise((done) => { for (const c of wss.clients) c.terminate(); wss.close(done); })
  })));
}

module.exports = { startRelay, MAX_PLAYERS };

if (require.main === module) {
  startRelay().then((r) => console.log(`GYMCHAOS_RELAY_LISTENING port=${r.port}`));
}
