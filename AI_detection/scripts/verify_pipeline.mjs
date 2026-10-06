/**
 * End-to-end check of the BROWSER pipeline on real recorded clips.
 *
 *   node scripts/verify_pipeline.mjs
 *
 * The other two tests each check one half:
 *   compare_features.mjs  JS features == Python features
 *   compare_tfjs.mjs      TF.js model == Keras model  (on synthetic input)
 *
 * This one runs both halves together the way the browser does -- raw landmarks
 * in, class name out -- on held-out session clips whose true label is known.
 * A pass here means the only thing left that can differ in the browser is the
 * camera and MediaPipe itself, not any of our own code.
 *
 * Exit 0 = every clip classified correctly.
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import * as tf from '@tensorflow/tfjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, '..');

const { buildFeatures } = await import(
  'file://' + path.join(root, 'web', 'pose-features.js'));

const buildDir = process.argv[2] || path.join(root, 'tfjs_build', 'pose_action');
const metaPath = path.join(buildDir, 'meta.json');
const fixturePath = path.join(buildDir, 'feature_fixtures.json');
const modelPath = path.join(buildDir, 'model', 'model.json');

for (const p of [metaPath, fixturePath, modelPath]) {
  if (!fs.existsSync(p)) {
    console.error(`missing ${p}\nrun train_pose_model.py, export_feature_fixtures.py ` +
      'and convert_to_tfjs.py first');
    process.exit(2);
  }
}

const meta = JSON.parse(fs.readFileSync(metaPath, 'utf8'));
const fx = JSON.parse(fs.readFileSync(fixturePath, 'utf8'));

// Load through the same tf.loadLayersModel the browser uses. file:// is not a
// scheme tfjs handles, so hand it an explicit IO handler over local files.
const modelDir = path.dirname(modelPath);
const handler = {
  load: async () => {
    const spec = JSON.parse(fs.readFileSync(modelPath, 'utf8'));
    const specs = [];
    const buffers = [];
    for (const group of spec.weightsManifest) {
      specs.push(...group.weights);
      for (const name of group.paths) {
        buffers.push(fs.readFileSync(path.join(modelDir, name)));
      }
    }
    const all = Buffer.concat(buffers);
    return {
      modelTopology: spec.modelTopology,
      weightSpecs: specs,
      weightData: all.buffer.slice(all.byteOffset, all.byteOffset + all.byteLength),
      format: spec.format,
      generatedBy: spec.generatedBy,
      convertedBy: spec.convertedBy,
    };
  },
};

const model = await tf.loadLayersModel(handler);
const actions = meta.actions;
const T = meta.sequence_length;
const F = meta.n_features;

console.log(`model     (${T}, ${F}) -> ${actions.length} classes`);
console.log(`classes   ${actions.join(', ')}`);
console.log(`clips     ${fx.cases.length}\n`);

let correct = 0, scored = 0, failures = [];

for (const c of fx.cases) {
  // Exactly what pose-detector.js does per frame: raw buffer -> features ->
  // flatten into one [1, T, F] tensor -> predict.
  const feats = buildFeatures(c.raw.map((r) => Float32Array.from(r)));
  const flat = new Float32Array(T * F);
  for (let t = 0; t < T; t++) flat.set(feats[t], t * F);

  const probs = tf.tidy(() =>
    model.predict(tf.tensor3d(flat, [1, T, F])).dataSync());

  let best = 0;
  for (let i = 1; i < probs.length; i++) if (probs[i] > probs[best]) best = i;
  const pred = actions[best];
  const conf = probs[best];

  // The all-zero case has no true label -- it is here to prove the pipeline
  // produces a finite answer instead of NaN when MediaPipe finds nobody.
  if (c.action === 'none') {
    const finite = Number.isFinite(conf);
    console.log(`${finite ? 'ok  ' : 'FAIL'}  ${c.note.padEnd(34)} ` +
      `-> ${pred.padEnd(9)} conf ${conf.toFixed(3)}  (no true label)`);
    if (!finite) failures.push(c.note);
    continue;
  }

  scored++;
  const ok = pred === c.action;
  if (ok) correct++; else failures.push(`${c.note}: got ${pred}`);
  console.log(`${ok ? 'ok  ' : 'FAIL'}  ${c.note.padEnd(34)} ` +
    `-> ${pred.padEnd(9)} conf ${conf.toFixed(3)}` +
    (ok ? '' : `   expected ${c.action}`));
}

const pct = scored ? (correct / scored * 100).toFixed(1) : '0.0';
console.log(`\nlabelled clips  ${correct}/${scored}  (${pct}%)`);

if (failures.length) {
  console.log('\nFAIL:');
  for (const f of failures) console.log('  ' + f);
  process.exit(1);
}
console.log('\nPASS: browser pipeline reproduces the right class on every clip');
