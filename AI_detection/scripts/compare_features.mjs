/**
 * Prove web/pose-features.js computes exactly what scripts/pose_features.py does.
 *
 *   .venv/Scripts/python.exe scripts/export_feature_fixtures.py   # once
 *   node scripts/compare_features.mjs
 *
 * Exit 0 = the JS port is equivalent. Run it after every edit to either
 * implementation; a drift here does not crash anything, it just makes the model
 * confidently wrong, which is far more expensive to debug later.
 *
 * The tolerance is tight on purpose (1e-5 absolute). Both sides do the same
 * arithmetic in float32, so the only legitimate difference is operation
 * ordering inside the mean/norm -- worth ~1e-7. Anything near the tolerance
 * means a real logic difference, not rounding.
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, '..');

const { buildFeatures, DEFAULTS } = await import(
  'file://' + path.join(root, 'web', 'pose-features.js'));

const fixturePath = process.argv[2] ||
  path.join(root, 'tfjs_build', 'pose_action', 'feature_fixtures.json');
const tol = Number(process.env.TOL ?? 1e-5);

if (!fs.existsSync(fixturePath)) {
  console.error(`missing ${fixturePath}\n` +
    'run: .venv/Scripts/python.exe scripts/export_feature_fixtures.py');
  process.exit(2);
}

const fx = JSON.parse(fs.readFileSync(fixturePath, 'utf8'));
console.log(`fixtures  ${path.relative(root, fixturePath)}`);
console.log(`cases     ${fx.cases.length}`);
console.log(`window    ${fx.sequence_length} x ${fx.n_features}`);
console.log(`tolerance ${tol}\n`);

if (fx.n_features !== DEFAULTS.nFeatures) {
  console.error(`FAIL: fixture has ${fx.n_features} features, ` +
    `JS DEFAULTS says ${DEFAULTS.nFeatures}`);
  process.exit(1);
}

let worst = 0, worstWhere = '', failed = 0, nonFinite = 0;

for (const c of fx.cases) {
  const got = buildFeatures(c.raw.map((r) => Float32Array.from(r)));
  const want = c.expected;

  if (got.length !== want.length) {
    console.log(`FAIL ${c.note}: ${got.length} frames, expected ${want.length}`);
    failed++;
    continue;
  }

  let caseWorst = 0;
  for (let t = 0; t < want.length; t++) {
    for (let j = 0; j < want[t].length; j++) {
      const a = got[t][j], b = want[t][j];
      if (!Number.isFinite(a)) { nonFinite++; continue; }
      const d = Math.abs(a - b);
      if (d > caseWorst) caseWorst = d;
      if (d > worst) { worst = d; worstWhere = `${c.note} frame ${t} feat ${j}`; }
    }
  }
  const ok = caseWorst <= tol;
  if (!ok) failed++;
  console.log(`${ok ? 'ok  ' : 'FAIL'}  ${c.note.padEnd(34)} max diff ${caseWorst.toExponential(2)}`);
}

console.log(`\nworst     ${worst.toExponential(3)}  (${worstWhere})`);
if (nonFinite) console.log(`non-finite values in JS output: ${nonFinite}`);

if (failed || nonFinite) {
  console.log(`\nFAIL: ${failed} case(s) over tolerance, ${nonFinite} non-finite`);
  process.exit(1);
}
console.log('\nPASS: JS features match Python exactly');
