/**
 * Test the emit gate in web/pose-detector.js without a camera or a model.
 *
 *   node scripts/verify_gating.mjs
 *
 * The gate decides what Unity actually receives, and every one of its rules
 * exists because the obvious version is wrong:
 *
 *   - the window slides one frame at a time, so ONE punch is classified as
 *     "jab" for dozens of consecutive frames -> without the lock, Unity gets
 *     30 jabs from one punch
 *   - but two real jabs in a row must count as two, so the lock cannot simply
 *     be "don't repeat the last move" (which is what the notebook did)
 *   - and an empty frame makes the model answer "block" at confidence 1.000,
 *     so no pose means no prediction at all
 *
 * Those three pull against each other; this file pins the behaviour down.
 *
 * It drives the real _gate()/_onFrame() methods with synthetic probabilities,
 * so it tests the shipped code rather than a copy of its logic.
 */
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, '..');

// pose-detector.js reads globalThis.tf only inside init()/_predict(), neither
// of which the gate tests touch -- the stub just has to exist for the import.
globalThis.tf = { tidy: (fn) => fn() };

const { PoseDetector, transportFromUrl, TRANSPORTS } = await import(
  'file://' + path.join(root, 'web', 'pose-detector.js'));

const ACTIONS = ['jab', 'cross', 'hook', 'uppercut', 'idle', 'block'];

/** A detector wired for gate testing: no model, no camera, emits into an array. */
function makeDetector(cfg = {}) {
  const d = new PoseDetector({ logEvents: false, transport: 'unity-webview', ...cfg });
  d.actions = ACTIONS;
  d.sequenceLength = 36;
  d.meta = { n_features: 60, n_raw: 132 };
  d.featureCfg = { poseIdx: [11, 12, 13, 14, 15, 16, 19, 20, 23, 24], aspect: 4 / 3 };
  d.emitted = [];
  d._emit = (pose, confidence) => d.emitted.push({ pose, confidence });
  return d;
}

/** One-hot probability row for `action` at `conf`, remainder spread evenly. */
function probs(action, conf) {
  const i = ACTIONS.indexOf(action);
  const rest = (1 - conf) / (ACTIONS.length - 1);
  const p = new Float32Array(ACTIONS.length).fill(rest);
  p[i] = conf;
  return p;
}

const feed = (d, action, conf, frames) => {
  for (let i = 0; i < frames; i++) d._gate(probs(action, conf));
};

let failed = 0;
function check(name, got, want) {
  const ok = JSON.stringify(got) === JSON.stringify(want);
  if (!ok) failed++;
  console.log(`${ok ? 'ok  ' : 'FAIL'}  ${name}`);
  if (!ok) console.log(`        got  ${JSON.stringify(got)}\n        want ${JSON.stringify(want)}`);
}

// ── one punch, held for many frames, must emit exactly once ──────────────────
{
  const d = makeDetector();
  feed(d, 'jab', 0.95, 40);
  check('one sustained jab -> 1 emit', d.emitted.map((e) => e.pose), ['jab']);
}

// ── two separate jabs with idle between -> two emits ────────────────────────
{
  const d = makeDetector({ emitCooldownMs: 0 });
  feed(d, 'jab', 0.95, 20);
  feed(d, 'idle', 0.95, 10);      // lowering the hands releases the lock
  feed(d, 'jab', 0.95, 20);
  check('jab, idle, jab -> 2 emits', d.emitted.map((e) => e.pose), ['jab', 'jab']);
}

// ── the combo the game needs: jab then cross, back to back ──────────────────
{
  const d = makeDetector({ emitCooldownMs: 0 });
  feed(d, 'jab', 0.95, 12);
  feed(d, 'cross', 0.95, 12);
  check('jab -> cross combo passes through', d.emitted.map((e) => e.pose),
    ['jab', 'cross']);
}

// ── below threshold never emits ─────────────────────────────────────────────
{
  const d = makeDetector();
  feed(d, 'hook', 0.5, 40);
  check('conf 0.50 < threshold 0.7 -> no emit', d.emitted, []);
}

// ── idle is a state, not a move ─────────────────────────────────────────────
{
  const d = makeDetector();
  feed(d, 'idle', 0.99, 40);
  check('idle never emits', d.emitted, []);
}

// ── a one-frame spike must not emit (consensus) ─────────────────────────────
{
  const d = makeDetector();
  feed(d, 'idle', 0.95, 10);
  feed(d, 'uppercut', 0.95, 1);   // single noisy frame
  feed(d, 'idle', 0.95, 10);
  check('1-frame spike -> no emit (consensus 3)', d.emitted, []);
}

// ── cooldown suppresses an implausibly fast second move ─────────────────────
{
  const d = makeDetector({ emitCooldownMs: 10_000 });
  feed(d, 'jab', 0.95, 10);
  feed(d, 'cross', 0.95, 10);
  check('cooldown blocks the 2nd move', d.emitted.map((e) => e.pose), ['jab']);
}

// ── no pose in frame -> never predicts, never emits ─────────────────────────
// The regression that matters most: an empty window makes the model answer
// "block" at confidence 1.000, which would hand the player free blocks.
{
  const d = makeDetector();
  let predicted = 0;
  d._predict = () => { predicted++; return probs('block', 1.0); };

  for (let i = 0; i < 60; i++) d._onFrame(null);   // nobody in frame
  check('no pose -> no prediction at all', { predicted, emits: d.emitted.length },
    { predicted: 0, emits: 0 });
}

// ── pose returns -> predicting resumes ─────────────────────────────────────
{
  const d = makeDetector();
  d._predict = () => probs('jab', 0.95);
  const lm = Array.from({ length: 33 }, () => ({ x: 0.5, y: 0.5, z: 0, visibility: 1 }));

  for (let i = 0; i < 40; i++) d._onFrame(null);
  for (let i = 0; i < 40; i++) d._onFrame(lm);
  check('pose returns -> emits again', d.emitted.map((e) => e.pose), ['jab']);
}

// ── a brief tracking dropout inside a window is tolerated ──────────────────
// minPresenceRatio 0.8 of 36 frames = up to 7 lost frames is still usable;
// demanding every frame would stall the game on normal MediaPipe flicker.
{
  const d = makeDetector();
  d._predict = () => probs('cross', 0.95);
  const lm = Array.from({ length: 33 }, () => ({ x: 0.5, y: 0.5, z: 0, visibility: 1 }));

  for (let i = 0; i < 36; i++) d._onFrame(lm);
  for (let i = 0; i < 4; i++) d._onFrame(null);    // 4/36 lost = ratio 0.89
  for (let i = 0; i < 6; i++) d._onFrame(lm);
  check('4-frame dropout still predicts', d.emitted.map((e) => e.pose), ['cross']);
}

// ── _onFrame must NOT touch landmark coordinates ───────────────────────────
// The mirroring happens to the IMAGE in _sourceFrame(), before MediaPipe sees
// it, because flipping the image also makes MediaPipe swap the left/right
// landmark INDICES (11<->12, 15<->16, ...) -- measured at 14x better agreement
// than flipping x alone (see scripts/probe_mirror.py).
//
// An earlier version flipped x here instead, which left/right-swapped every
// feature relative to training and wrecked jab vs cross. Nothing else in the
// suite catches that, so this pins down that coordinates pass through intact.
{
  const d = makeDetector();
  d._predict = () => probs('jab', 0.95);

  const lm = Array.from({ length: 33 }, (_, i) => ({
    x: 0.1 + i * 0.02, y: 0.3, z: -0.1, visibility: 0.9,
  }));
  d._onFrame(lm);

  const stored = d.rawBuffer.at(-1);
  const xsMatch = lm.every((p, i) => Math.abs(stored[i * 4] - p.x) < 1e-6);
  check('_onFrame stores landmark x unchanged (no 1-x flip)', xsMatch, true);
}

// ── transport: picks the sink that actually exists ─────────────────────────
// The same page runs in three places (Android WebView, WebGL, dev browser) and
// must reach Unity in each without a config change. Picking wrongly fails
// silently -- the detector keeps working and Unity simply never hears anything.
{
  const saved = { Unity: globalThis.Unity, unityInstance: globalThis.unityInstance };
  const calls = [];

  globalThis.Unity = { call: (m) => calls.push(['unity-webview', m]) };
  globalThis.unityInstance = { SendMessage: (o, m, j) => calls.push(['sendmessage', j]) };

  const auto = makeDetector({ transport: 'auto' });
  auto._connectTransport();
  check('auto picks unity-webview when window.Unity exists',
    auto.activeTransport, 'unity-webview');

  delete globalThis.Unity;
  const auto2 = makeDetector({ transport: 'auto' });
  auto2._connectTransport();
  check('auto falls back to sendmessage without window.Unity',
    auto2.activeTransport, 'sendmessage');

  // Real delivery, not just the label: run a punch through the full gate and
  // confirm the JSON reached the sink.
  globalThis.Unity = { call: (m) => calls.push(['unity-webview', m]) };
  calls.length = 0;
  const live = new PoseDetector({ logEvents: false, transport: 'unity-webview' });
  live.actions = ACTIONS;
  feed(live, 'jab', 0.95, 12);
  const [via, json] = calls[0] ?? [];
  const msg = json ? JSON.parse(json) : {};
  check('a punch reaches Unity.call as JSON',
    { via, pose: msg.pose, hasSeq: Number.isInteger(msg.seq) },
    { via: 'unity-webview', pose: 'jab', hasSeq: true });

  // seq must increment with no gaps so Unity can detect a dropped message.
  calls.length = 0;
  const seqd = new PoseDetector({ logEvents: false, transport: 'unity-webview',
                                  emitCooldownMs: 0 });
  seqd.actions = ACTIONS;
  for (const a of ['jab', 'idle', 'cross', 'idle', 'hook']) feed(seqd, a, 0.95, 10);
  const seqs = calls.map(([, j]) => JSON.parse(j).seq);
  check('seq increments 1,2,3 with no gaps', seqs, [1, 2, 3]);

  globalThis.Unity = saved.Unity;
  globalThis.unityInstance = saved.unityInstance;
  if (saved.Unity === undefined) delete globalThis.Unity;
  if (saved.unityInstance === undefined) delete globalThis.unityInstance;
}

// ── ?transport= override ───────────────────────────────────────────────────
// Both pages read the transport off the query string so one build can be
// pointed at a specific sink without editing a file. A typo must not take the
// page down, and an unknown value must not be passed through to _sinks() --
// there it would match nothing and every pose would vanish without a word.
{
  check('no ?transport -> keeps the page default',
    transportFromUrl('auto', '?debug'), 'auto');
  check('?transport=unity-webview pins the Android sink',
    transportFromUrl('auto', '?transport=unity-webview'), 'unity-webview');
  check('?transport=websocket pins the desktop sink',
    transportFromUrl('auto', '?transport=websocket'), 'websocket');
  check('reads the param alongside others',
    transportFromUrl('auto', '?debug=1&transport=sendmessage'), 'sendmessage');
  check('unknown value falls back instead of throwing',
    transportFromUrl('auto', '?transport=carrier-pigeon'), 'auto');
  check('empty value falls back',
    transportFromUrl('auto', '?transport='), 'auto');

  // Every name the helper accepts must be a sink _sinks() can actually build,
  // or a legal-looking URL would still emit into the void.
  const d = new PoseDetector({ logEvents: false });
  const buildable = TRANSPORTS.filter((t) => {
    d.cfg.transport = t;
    return d._sinks().length > 0;
  });
  check('every accepted transport maps to a real sink', buildable, TRANSPORTS);
}

// ── status messages reach Unity on the same channel as poses ───────────────
// Once the WebView is hidden there is no other way for the player to learn the
// camera died -- the game would just stop responding with no explanation.
{
  const saved = globalThis.Unity;
  const calls = [];
  globalThis.Unity = { call: (m) => calls.push(JSON.parse(m)) };

  const d = makeDetector({ transport: 'unity-webview' });
  d.emitStatus('nocamera', 'NotAllowedError');
  check('status message is tagged type=status',
    { type: calls[0]?.type, state: calls[0]?.state }, { type: 'status', state: 'nocamera' });

  // Unity tells pose from status by the `type` field, so poses must carry it too
  calls.length = 0;
  const p = new PoseDetector({ logEvents: false, transport: 'unity-webview' });
  p.actions = ACTIONS;
  feed(p, 'jab', 0.95, 12);
  check('pose message is tagged type=pose', calls[0]?.type, 'pose');

  // poses and statuses share one counter, so a gap really means a lost message
  calls.length = 0;
  const q = new PoseDetector({ logEvents: false, transport: 'unity-webview',
                               emitCooldownMs: 0 });
  q.actions = ACTIONS;
  q.emitStatus('ready');
  feed(q, 'jab', 0.95, 10);
  feed(q, 'idle', 0.95, 10);
  feed(q, 'cross', 0.95, 10);
  check('seq is shared across pose and status', calls.map((c) => c.seq), [1, 2, 3]);

  globalThis.Unity = saved;
  if (saved === undefined) delete globalThis.Unity;
}

// ── presence changes are reported, but not every frame ─────────────────────
// A 30 fps "nobody there" spam would flood the bridge for no new information.
{
  const saved = globalThis.Unity;
  const calls = [];
  globalThis.Unity = { call: (m) => calls.push(JSON.parse(m)) };

  const d = makeDetector({ transport: 'unity-webview', reportPresenceToUnity: true });
  d._predict = () => probs('jab', 0.95);
  const lm = Array.from({ length: 33 }, () => ({ x: 0.5, y: 0.5, z: 0, visibility: 1 }));

  for (let i = 0; i < 60; i++) d._onFrame(null);    // nobody, 60 frames
  const noposeMsgs = calls.filter((c) => c.state === 'nopose').length;
  check('60 empty frames -> exactly 1 nopose message', noposeMsgs, 1);

  for (let i = 0; i < 50; i++) d._onFrame(lm);      // somebody returns
  const okMsgs = calls.filter((c) => c.state === 'pose-ok').length;
  check('presence returning -> exactly 1 pose-ok message', okMsgs, 1);

  globalThis.Unity = saved;
  if (saved === undefined) delete globalThis.Unity;
}

// ── a held pose keeps counting; a held punch does not ──────────────────────
// block is High Guard: the player holds it for seconds at a time. The lock that
// stops one punch becoming thirty would also silence a held guard after its
// first frame, so every later round would score MISS while the player is in
// fact still blocking. Sustained poses re-emit on a slow beat instead.
{
  const d = makeDetector();
  feed(d, 'block', 0.95, 20);
  check('held block emits once at first', d.emitted.map((e) => e.pose), ['block']);

  // rewind the clock past sustainedRepeatMs without waiting for real time
  d.lastEmitAt -= 800;
  feed(d, 'block', 0.95, 5);
  check('still holding block -> emits again', d.emitted.length, 2);

  d.lastEmitAt -= 800;
  feed(d, 'block', 0.95, 5);
  check('and keeps re-emitting while held', d.emitted.length, 3);
}

// ── repeatSameAfterMs: rattling off the same punch ─────────────────────────
// Throwing three uppercuts in a row reads as one continuous "uppercut" to the
// model -- the sliding window never shows a clean gap between them -- so the
// release rule collapses the burst into a single punch. This option trades
// that safety for the ability to count a burst, so both halves are pinned:
// it must repeat when enabled, and must still respect the interval.
{
  const d = makeDetector({ repeatSameAfterMs: 500 });
  feed(d, 'uppercut', 0.95, 20);
  check('repeat off cooldown -> first uppercut emits', d.emitted.length, 1);

  d.lastEmitAt -= 600;           // past the repeat interval
  feed(d, 'uppercut', 0.95, 5);
  check('same pose repeats once the interval passes', d.emitted.length, 2);

  d.lastEmitAt -= 100;           // still inside the interval
  feed(d, 'uppercut', 0.95, 5);
  check('but not before the interval is up', d.emitted.length, 2);

  check('every repeat carries the same pose',
    new Set(d.emitted.map((e) => e.pose)).size, 1);
}

{
  const d = makeDetector();
  feed(d, 'jab', 0.95, 20);
  d.lastEmitAt -= 5000;          // far longer than any repeat interval
  feed(d, 'jab', 0.95, 20);
  check('held jab stays a single punch (repeat off by default)',
    d.emitted.map((e) => e.pose), ['jab']);
}

{
  // releasing and re-forming the guard is two separate blocks, as expected
  const d = makeDetector({ emitCooldownMs: 0 });
  feed(d, 'block', 0.95, 12);
  feed(d, 'idle', 0.95, 10);
  feed(d, 'block', 0.95, 12);
  check('block, drop, block -> 2 emits', d.emitted.length, 2);
}

// ── restart clears state ───────────────────────────────────────────────────
{
  const d = makeDetector();
  feed(d, 'jab', 0.95, 20);
  d._resetState();                 // the same reset start() performs
  feed(d, 'jab', 0.95, 20);
  check('after reset, same move emits again', d.emitted.map((e) => e.pose),
    ['jab', 'jab']);
}

console.log(failed ? `\nFAIL: ${failed} test(s)` : '\nPASS: all gate rules hold');
process.exit(failed ? 1 : 0);
