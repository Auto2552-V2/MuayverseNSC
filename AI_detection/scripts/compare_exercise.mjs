/**
 * Prove web/exercise-scorer.js matches ExerciseMode's Python side.
 *
 *   node scripts/compare_exercise.mjs
 *
 * The jab scorer exists twice: normalize_keypoints/get_elbow_angle/embed live in
 * ExerciseMode (Python, authoritative) and again in web/exercise-scorer.js so the
 * browser can score without a server. Nothing links the two, so a drift in either
 * one is silent -- the model keeps answering, just about the wrong numbers.
 *
 * Fixtures come from ExerciseMode/scripts/export_web.py, which feeds UN-normalised
 * keypoints in on purpose: that way this also covers the hip-centring and the
 * unit-norm step, not only the two matrix multiplies.
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  JabScorer, normalizeKeypoints, mirrorFlat, elbowAngle,
} from '../web/exercise-scorer.js';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const dir = path.join(root, 'tfjs_build', 'jab_siamese');

for (const f of ['bundle.json', 'fixtures.json']) {
  if (!fs.existsSync(path.join(dir, f))) {
    console.error(`missing tfjs_build/jab_siamese/${f}\n` +
      '  run: cd ../ExerciseMode && python scripts/export_web.py');
    process.exit(1);
  }
}

const bundle = JSON.parse(fs.readFileSync(path.join(dir, 'bundle.json'), 'utf8'));
const { cases } = JSON.parse(fs.readFileSync(path.join(dir, 'fixtures.json'), 'utf8'));
const scorer = new JabScorer(bundle);

// The bundle stores weights rounded to 7 decimals, so agreement is capped there.
// 1e-5 is loose enough for that rounding and tight enough to catch a real change
// (a wrong mirror pair or a dropped bias moves these by 1e-2 or more).
const TOL = 1e-5;
const TOL_DEG = 1e-4;

let failed = 0;
const fail = (name, what, got, want) => {
  failed += 1;
  console.error(`  FAIL ${name} ${what}: js=${got} py=${want}`);
};

for (const c of cases) {
  const { flat, centered } = normalizeKeypoints(c.raw);

  let worst = 0;
  for (let i = 0; i < flat.length; i++) {
    worst = Math.max(worst, Math.abs(flat[i] - c.flat[i]));
  }
  if (!(worst <= 1e-8)) fail(c.name, 'normalise', worst.toExponential(2), '<=1e-8');

  const emb = scorer.embed(flat);
  let we = 0;
  for (let i = 0; i < emb.length; i++) we = Math.max(we, Math.abs(emb[i] - c.embedding[i]));
  if (!(we <= TOL)) fail(c.name, 'embedding', we.toExponential(2), `<=${TOL}`);

  const direct = scorer.minDistance(emb).dist;
  if (Math.abs(direct - c.min_dist) > TOL) fail(c.name, 'min_dist', direct, c.min_dist);

  const mir = scorer.minDistance(scorer.embed(mirrorFlat(flat, scorer.mirrorPairs))).dist;
  if (Math.abs(mir - c.min_dist_mirrored) > TOL) {
    fail(c.name, 'min_dist_mirrored', mir, c.min_dist_mirrored);
  }

  const sc = scorer.score(flat);
  if (Math.abs(sc.percent - c.percent) > 1e-3) fail(c.name, 'percent', sc.percent, c.percent);
  if (Math.abs(sc.cosine - c.cosine) > TOL) fail(c.name, 'cosine', sc.cosine, c.cosine);
  if (sc.plausible !== c.plausible) fail(c.name, 'plausible', sc.plausible, c.plausible);

  for (const [arm, idx] of Object.entries(scorer.arms)) {
    const got = elbowAngle(centered, idx);
    if (Math.abs(got - c.elbow[arm]) > TOL_DEG) fail(c.name, `elbow.${arm}`, got, c.elbow[arm]);
  }
}

// Mirroring twice must land back where it started -- catches a mirror table that
// pairs an index with itself, which would otherwise look plausible everywhere else.
// Not exact: each pass divides by (norm + 1e-6), so two passes shrink the vector by
// ~2e-6 relative. Python's mirror_flat does the same, hence a tolerance not zero.
{
  const { flat } = normalizeKeypoints(cases[0].raw);
  const back = mirrorFlat(mirrorFlat(flat, scorer.mirrorPairs), scorer.mirrorPairs);
  let worst = 0;
  for (let i = 0; i < flat.length; i++) worst = Math.max(worst, Math.abs(flat[i] - back[i]));
  if (!(worst <= 1e-5)) fail('mirror', 'round-trip', worst.toExponential(2), '<=1e-5');
}

// An extension frame fed straight back in has to land on itself: the nearest
// reference embedding IS its own, so the distance collapses to the epsilon floor
// sqrt(0 + 1e-6) = 1e-3 exactly. Anything above that means the bundle's weights and
// its precomputed reference embeddings came from different training runs.
//
// The guard frames come from the SAME clip and must land far away. That is the check
// that the reference set stayed trimmed to the extension frames: with the whole clip
// in there, standing in guard scores 100% and the whole score means nothing.
{
  const distOf = (c) => scorer.minDistance(scorer.embed(normalizeKeypoints(c.raw).flat)).dist;
  const ext = cases.filter((c) => c.name.startsWith('ext')).map(distOf);
  const guard = cases.filter((c) => c.name.startsWith('guard')).map(distOf);
  if (!ext.length || !guard.length) fail('fixtures', 'need ext* and guard* cases', ext.length, '>0');
  const worstExt = Math.max(...ext);
  const closestGuard = Math.min(...guard);
  if (!(worstExt <= 1.001e-3)) {
    fail('self-match', 'extension frame vs itself', worstExt.toExponential(2), '<=1e-3');
  }
  if (!(closestGuard > 1)) {
    fail('reference set', 'guard frame must not match', closestGuard.toFixed(3), '>1');
  }
  console.log(`  extension frames match themselves (${worstExt.toExponential(2)}), ` +
    `guard frames do not (${closestGuard.toFixed(2)})`);
}

// Garbage in must not score. The embedding goes degenerate off-distribution -- random
// noise and an all-zero frame both land 0.004-0.04 from a reference, i.e. "99% like the
// coach" -- so the cosine gate is the only thing standing between the player and a free
// perfect score for walking out of frame.
{
  for (const name of ['random', 'zeros']) {
    const c = cases.find((x) => x.name === name);
    if (!c) { fail('gate', `fixture ${name} missing`, 'absent', 'present'); continue; }
    const sc = scorer.score(normalizeKeypoints(c.raw).flat);
    if (sc.plausible) fail('gate', `${name} passed the cosine gate`, sc.cosine, `<${scorer.cfg.cosineMin}`);
    if (sc.percent !== 0) fail('gate', `${name} scored above zero`, sc.percent, 0);
  }
  console.log('  cosine gate rejects random noise and empty frames');
}

// The rep counter must need a full guard -> extend -> guard cycle. Holding the arm
// out must not keep scoring, which is exactly what the old per-frame angle window
// in main.py did.
{
  const s = new JabScorer(bundle);
  // start from a real body so the cosine gate sees a plausible pose, then move
  // only the right arm's three joints to drive the angle
  const ref = cases.find((c) => c.name.startsWith('ext'));
  const lm = [];
  for (let i = 0; i < 33; i++) {
    lm.push({ x: ref.raw[i * 3], y: ref.raw[i * 3 + 1], z: ref.raw[i * 3 + 2] });
  }
  // Drive the right arm (shoulder 12, elbow 14, wrist 16) through a punch by hand:
  // place the three joints so the elbow angle is what we want it to be.
  const setArm = (deg, reach) => {
    const r = deg * Math.PI / 180;
    lm[12] = { x: 0.5, y: 0.3, z: 0 };
    lm[14] = { x: 0.5, y: 0.45, z: 0 };
    // shoulder is straight up from the elbow, so the wrist direction sets the angle
    lm[16] = { x: 0.5 + Math.sin(r) * reach, y: 0.45 - Math.cos(r) * reach, z: 0 };
  };
  let t = 0;
  const step = (deg, reach) => { setArm(deg, reach); t += 33; return s.update(lm, t); };

  step(20, 0.15); step(20, 0.15);
  let got = null;
  for (const d of [60, 100, 140, 175]) got = step(d, 0.15) ?? got;
  if (s.reps !== 1) fail('reps', 'one punch counted once', s.reps, 1);

  for (let i = 0; i < 40; i++) step(175, 0.15);        // held out at full extension
  if (s.reps !== 1) fail('reps', 'holding extension adds reps', s.reps, 1);

  step(20, 0.15);                                      // back to guard
  for (const d of [60, 100, 140, 175]) step(d, 0.15);
  if (s.reps !== 2) fail('reps', 'second punch after returning to guard', s.reps, 2);

  // A slow extension (tiny moves, long gaps) must not count.
  const s2 = new JabScorer(bundle);
  let t2 = 0;
  for (const d of [20, 40, 60, 90, 120, 150, 175]) {
    setArm(d, 0.15);
    t2 += 400;                                         // 0.4 s per step = crawling
    s2.update(lm, t2);
  }
  if (s2.reps !== 0) fail('reps', 'slow extension must not count', s2.reps, 0);
  console.log('  rep counter: one punch = 1, held extension = 1, slow punch = 0');
}

if (failed) {
  console.error(`\n${failed} check(s) failed -- exercise-scorer.js drifted from ExerciseMode`);
  process.exit(1);
}
console.log(`exercise scorer matches Python on ${cases.length} fixtures (tol ${TOL})`);
