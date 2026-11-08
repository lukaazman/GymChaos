'use strict';
const assert = require('assert');
const WebSocket = require('ws');
const { startRelay } = require('./relay');

function client(port) {
  const ws = new WebSocket(`ws://127.0.0.1:${port}`);
  const inbox = [];
  const waiters = [];
  ws.on('message', (raw) => {
    const m = JSON.parse(raw.toString());
    const i = waiters.findIndex((w) => w.t === m.t);
    if (i >= 0) waiters.splice(i, 1)[0].resolve(m); else inbox.push(m);
  });
  ws.next = (t) => new Promise((resolve, reject) => {
    const i = inbox.findIndex((m) => m.t === t);
    if (i >= 0) return resolve(inbox.splice(i, 1)[0]);
    const timer = setTimeout(() => reject(new Error('timeout ' + t)), 2000);
    waiters.push({ t, resolve: (m) => { clearTimeout(timer); resolve(m); } });
  });
  ws.json = (o) => ws.send(JSON.stringify(o));
  return new Promise((res) => ws.on('open', () => res(ws)));
}

(async () => {
  const relay = await startRelay({ port: 0, reserveMs: 600 });
  const host = await client(relay.port);
  host.json({ t: 'host', chr: 'char-host' });
  const room = await host.next('room');
  assert.match(room.code, /^[A-Z]{6}$/, 'room codes are six letters');
  // One live session per character: the host's character cannot join its own room again.
  const twin = await client(relay.port);
  twin.json({ t: 'join', code: room.code, chr: 'char-host' });
  assert.strictEqual((await twin.next('err')).m, 'character in use');
  twin.json({ t: 'join', code: room.code, chr: 'char-twin' });
  assert.strictEqual((await twin.next('joined')).id, 1);
  twin.terminate();
  assert.strictEqual((await host.next('left')).id, 1);
  assert.strictEqual(room.id, 0);
  const guests = [];
  for (let n = 1; n <= 3; n++) {
    const g = await client(relay.port);
    g.json({ t: 'join', code: room.code.toLowerCase() });
    assert.strictEqual((await g.next('joined')).id, n);
    guests.push(g);
  }
  const fifth = await client(relay.port);
  fifth.json({ t: 'join', code: room.code });
  assert.strictEqual((await fifth.next('err')).m, 'room full');
  host.json({ t: 'msg', to: -1, d: 'hello' });
  for (const g of guests) assert.strictEqual((await g.next('msg')).d, 'hello');
  // Frames that parse but are not objects must not take the relay down.
  for (const junk of ['null', '5', '"x"', '[1]']) fifth.send(junk);
  assert.strictEqual((await fifth.next('err')).m, 'bad message');
  guests[0].json({ t: 'msg', to: 0, d: 'to-host' });
  const got = await host.next('msg');
  assert.strictEqual(got.from, 1);
  guests[1].terminate();
  assert.strictEqual((await host.next('left')).id, 2);
  const token = room.token;
  assert.ok(typeof token === 'string' && token.length >= 8, 'room reply carries a reservation token');
  host.terminate();
  assert.strictEqual((await guests[0].next('closed')).t, 'closed');

  // The code is kept for a short time: guests are told the host is down, strangers cannot take the code.
  guests[0].json({ t: 'join', code: room.code, chr: 'char-g1' });
  assert.strictEqual((await guests[0].next('err')).m, 'host down');
  const thief = await client(relay.port);
  thief.json({ t: 'host', code: room.code, token: 'not-the-token' });
  const thiefRoom = await thief.next('room');
  assert.notStrictEqual(thiefRoom.code, room.code, 'a wrong token must not get the reserved code');
  thief.terminate();

  // The host comes back with its token: same code, and the waiting guest can join again.
  const back = await client(relay.port);
  back.json({ t: 'host', chr: 'char-host', code: room.code, token });
  const again = await back.next('room');
  assert.strictEqual(again.code, room.code);
  assert.strictEqual(again.id, 0);
  guests[0].json({ t: 'join', code: room.code, chr: 'char-g1' });
  assert.strictEqual((await guests[0].next('joined')).id, 1);
  // A reservation is used once.
  const late = await client(relay.port);
  late.json({ t: 'host', code: room.code, token });
  assert.notStrictEqual((await late.next('room')).code, room.code);
  late.terminate();

  // An unclaimed reservation expires: the code is gone and joining says so.
  back.terminate();
  await new Promise((r) => setTimeout(r, 100));
  guests[0].json({ t: 'join', code: room.code, chr: 'char-g1' });
  assert.strictEqual((await guests[0].next('err')).m, 'host down');
  await new Promise((r) => setTimeout(r, 800));
  guests[0].json({ t: 'join', code: room.code, chr: 'char-g1' });
  assert.strictEqual((await guests[0].next('err')).m, 'no such room');

  // The guests' character leases were released when the room closed.
  const leaseCheck = await client(relay.port);
  leaseCheck.json({ t: 'host', chr: 'char-g1' });
  assert.strictEqual((await leaseCheck.next('room')).id, 0);
  await relay.close();

  // Abuse limits: a socket flooding messages or guessing codes is dropped.
  const strict = await startRelay({ port: 0, maxMessagesPerSecond: 20 });
  const flooder = await client(strict.port);
  const closed = new Promise((r) => flooder.on('close', r));
  for (let i = 0; i < 60; i++) flooder.json({ t: 'ping' });
  await Promise.race([closed, new Promise((_, rej) => setTimeout(() => rej(new Error('flooder not dropped')), 2000))]);
  const guesser = await client(strict.port);
  const guessClosed = new Promise((r) => guesser.on('close', r));
  for (let i = 0; i < 12; i++) guesser.json({ t: 'join', code: 'ZZ' + (i % 10) });
  await Promise.race([guessClosed, new Promise((_, rej) => setTimeout(() => rej(new Error('code guesser not dropped')), 2000))]);
  await strict.close();
  console.log('GYMCHAOS_RELAY_TEST_OK');
  process.exit(0);
})().catch((e) => { console.error('GYMCHAOS_RELAY_TEST_FAILED', e.message); process.exit(1); });
