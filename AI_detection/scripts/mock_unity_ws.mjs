/**
 * Stand in for Unity so the detector can be tested before any C# exists.
 *
 *   node scripts/mock_unity_ws.mjs            # listens on 8787
 *   node scripts/mock_unity_ws.mjs --port 9000
 *
 * Prints every message the game would receive, and applies the SAME
 * confidence >= 0.7 rule the Unity side will, so what shows as ACCEPT here is
 * what the game would actually act on.
 *
 * The page sends four kinds of message down one socket and only `pose` reaches
 * game logic. They are split here because lumping them together makes the
 * readout lie in both directions: `type:'frame'` preview JPEGs have no `pose`
 * field and used to be tallied as dropped punches (hundreds of them, which
 * reads as catastrophic detection failure), while `type:'live'` carries a pose
 * the game explicitly does NOT act on -- including `idle`, which never reaches
 * Unity as a real punch.
 *
 * No dependencies: this speaks enough of RFC 6455 to accept a browser
 * connection and read short text frames. `ws` would be three lines shorter and
 * one more thing to install on a machine where npm installs are a nuisance.
 *
 * Only ever bound to 127.0.0.1 -- this is a debug aid, not a service.
 */
import net from 'node:net';
import crypto from 'node:crypto';

const argv = process.argv.slice(2);
const argOf = (name, dflt) => {
  const i = argv.indexOf(name);
  return i >= 0 && argv[i + 1] ? argv[i + 1] : dflt;
};
const PORT = Number(argOf('--port', 8787));
const THRESHOLD = Number(argOf('--threshold', 0.7));

const GUID = '258EAFA5-E914-47DA-95CA-C5AB0DC85B11';   // RFC 6455 handshake salt
const started = Date.now();
let accepted = 0, dropped = 0, live = 0, frames = 0, lastAt = null;

const server = net.createServer((sock) => {
  sock.once('data', (chunk) => {
    const req = chunk.toString('utf8');
    const key = /sec-websocket-key:\s*(.+)/i.exec(req)?.[1]?.trim();
    if (!key) { sock.end(); return; }

    const accept = crypto.createHash('sha1').update(key + GUID).digest('base64');
    sock.write(
      'HTTP/1.1 101 Switching Protocols\r\n' +
      'Upgrade: websocket\r\nConnection: Upgrade\r\n' +
      `Sec-WebSocket-Accept: ${accept}\r\n\r\n`);

    console.log(`\n[mock-unity] browser connected from ${sock.remoteAddress}\n`);
    sock.on('data', (buf) => readFrames(buf, sock));
    sock.on('close', () => console.log('\n[mock-unity] browser disconnected\n'));
    sock.on('error', () => {});
  });
  sock.on('error', () => {});
});

/**
 * Pull text payloads out of a TCP chunk.
 *
 * Deliberately minimal: these messages are ~40 bytes, so they always arrive as
 * one unfragmented frame with a 7-bit length and never span chunks. A real
 * server would buffer across chunks and handle continuation frames; if you see
 * truncated output here, that assumption broke and this is where to fix it.
 */
function readFrames(buf, sock) {
  let off = 0;
  while (off + 2 <= buf.length) {
    const opcode = buf[off] & 0x0f;
    const masked = (buf[off + 1] & 0x80) !== 0;
    let len = buf[off + 1] & 0x7f;
    let p = off + 2;

    if (len === 126) { len = buf.readUInt16BE(p); p += 2; }
    else if (len === 127) { len = Number(buf.readBigUInt64BE(p)); p += 8; }

    let mask = null;
    if (masked) { mask = buf.subarray(p, p + 4); p += 4; }
    if (p + len > buf.length) return;                 // incomplete -- give up

    const payload = Buffer.from(buf.subarray(p, p + len));
    if (mask) for (let i = 0; i < payload.length; i++) payload[i] ^= mask[i % 4];

    if (opcode === 0x8) return;                       // close
    // A ping MUST be answered or the client gives up on us. The Python client
    // sends keepalive pings while the player is just standing there, and
    // without a pong it closes the socket mid-session and drops the punch it
    // was sending -- which looks exactly like a detection failure.
    // PoseWebSocketServer.cs answers pings, so a mock that does not is lying
    // about the thing it stands in for.
    else if (opcode === 0x9) sendFrame(sock, 0xa, payload);
    else if (opcode === 0x1) handle(payload.toString('utf8'));
    off = p + len;
  }
}

/** Server-to-client frame: never masked, and ours are always under 126 bytes. */
function sendFrame(sock, opcode, payload = Buffer.alloc(0)) {
  if (!sock || sock.destroyed || payload.length > 125) return;
  const frame = Buffer.alloc(2 + payload.length);
  frame[0] = 0x80 | opcode;
  frame[1] = payload.length;
  payload.copy(frame, 2);
  sock.write(frame);
}

function handle(text) {
  const t = ((Date.now() - started) / 1000).toFixed(2).padStart(7);
  const gap = lastAt ? `+${String(Date.now() - lastAt).padStart(4)}ms` : '   ----';
  lastAt = Date.now();

  let msg;
  try { msg = JSON.parse(text); } catch {
    console.log(`${t}s ${gap}  BAD JSON  ${text}`);
    return;
  }

  const conf = typeof msg.confidence === 'number' ? msg.confidence.toFixed(3) : '?';

  // Camera preview: one JPEG per previewFps tick. Far too many to print, and
  // nothing to judge -- just keep a running count so a stalled preview shows.
  if (msg.type === 'frame') {
    frames++;
    if (frames % 50 === 0) {
      console.log(`${t}s ${gap}  preview  ${frames} frames  ${msg.w}x${msg.h}`);
    }
    return;
  }

  // What the model is reading right now, sent only when the label changes.
  // The game shows it on screen; it never scores off it. Printed dimmer with
  // no verdict because applying the threshold here would be meaningless.
  if (msg.type === 'live') {
    live++;
    console.log(`${t}s ${gap}  live    ${String(msg.pose || '--').padEnd(9)} conf ${conf}`);
    return;
  }

  if (msg.type === 'status') {
    console.log(`${t}s ${gap}  STATUS  ${msg.state}` +
      (msg.detail ? `  ${msg.detail}` : ''));
    return;
  }

  // Exactly the rule the Unity receiver applies, so this output matches what
  // the game would do -- a message listed as DROP never reaches game logic.
  const ok = typeof msg.pose === 'string' &&
    typeof msg.confidence === 'number' && msg.confidence >= THRESHOLD;
  if (ok) accepted++; else dropped++;

  const verdict = ok ? 'ACCEPT' : 'DROP  ';
  console.log(`${t}s ${gap}  ${verdict}  ${String(msg.pose).padEnd(9)} ` +
    `conf ${conf}   (accepted ${accepted}, dropped ${dropped})`);
}

server.listen(PORT, '127.0.0.1', () => {
  console.log(`[mock-unity] listening on ws://127.0.0.1:${PORT}`);
  console.log(`[mock-unity] dropping anything with confidence < ${THRESHOLD}`);
  console.log('[mock-unity] now open http://localhost:8000/web/ and punch');
});
server.on('error', (e) => {
  if (e.code === 'EADDRINUSE') {
    console.error(`[mock-unity] port ${PORT} is busy -- another copy running, ` +
      'or Unity already holds it');
    process.exit(1);
  }
  throw e;
});
