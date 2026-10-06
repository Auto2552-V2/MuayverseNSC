/**
 * Compare the converted TF.js model against the Keras reference vectors.
 *
 *   node scripts/compare_tfjs.mjs tfjs_build/action
 *
 * Expects <dir>/model/model.json and <dir>/reference.json.
 *
 * Uses the plain @tensorflow/tfjs CPU backend -- the same kernels the browser
 * runs, so a pass here means the browser agrees too. (tfjs-node would use
 * different native kernels and prove less.)
 */
import fs from 'node:fs';
import path from 'node:path';
import * as tf from '@tensorflow/tfjs';

const dir = process.argv[2];
if (!dir) {
  console.error('usage: node scripts/compare_tfjs.mjs <tfjs_build/<name>> [--tol 1e-5]');
  process.exit(2);
}
const tolArg = process.argv.indexOf('--tol');
const TOL = tolArg > -1 ? Number(process.argv[tolArg + 1]) : 1e-5;

const modelDir = path.join(dir, 'model');
const ref = JSON.parse(fs.readFileSync(path.join(dir, 'reference.json'), 'utf8'));

// Minimal IOHandler so we can load from disk without pulling in tfjs-node.
const fileHandler = {
  load: async () => {
    const spec = JSON.parse(fs.readFileSync(path.join(modelDir, 'model.json'), 'utf8'));
    const buffers = spec.weightsManifest.flatMap((g) =>
      g.paths.map((p) => {
        const b = fs.readFileSync(path.join(modelDir, p));
        return b.buffer.slice(b.byteOffset, b.byteOffset + b.byteLength);
      }),
    );
    return {
      modelTopology: spec.modelTopology,
      weightSpecs: spec.weightsManifest.flatMap((g) => g.weights),
      weightData: concat(buffers),
      format: spec.format,
      generatedBy: spec.generatedBy,
      convertedBy: spec.convertedBy,
    };
  },
};

function concat(buffers) {
  const total = buffers.reduce((n, b) => n + b.byteLength, 0);
  const out = new Uint8Array(total);
  let off = 0;
  for (const b of buffers) {
    out.set(new Uint8Array(b), off);
    off += b.byteLength;
  }
  return out.buffer;
}

const model = await tf.loadLayersModel(fileHandler);

const [T, F] = ref.input_shape;
const n = ref.samples;
const flat = ref.inputs.flat();
const x = tf.tensor(flat, [n, T, F], 'float32');
const y = await model.predict(x).array();

let maxAbs = 0;
let maxRel = 0;
let argmaxMismatch = 0;
const rows = [];

for (let i = 0; i < n; i++) {
  const expect = ref.outputs[i];
  const got = y[i];
  let rowAbs = 0;
  for (let j = 0; j < expect.length; j++) {
    const d = Math.abs(expect[j] - got[j]);
    rowAbs = Math.max(rowAbs, d);
    maxRel = Math.max(maxRel, d / Math.max(Math.abs(expect[j]), 1e-7));
  }
  maxAbs = Math.max(maxAbs, rowAbs);
  const ea = expect.indexOf(Math.max(...expect));
  const ga = got.indexOf(Math.max(...got));
  if (ea !== ga) argmaxMismatch++;
  rows.push({ sample: i, keras_argmax: ea, tfjs_argmax: ga, max_abs_diff: rowAbs.toExponential(2) });
}

console.log(`model    ${modelDir}`);
console.log(`input    [${n}, ${T}, ${F}]`);
console.log(`backend  ${tf.getBackend()}  tfjs ${tf.version.tfjs}\n`);
console.table(rows);
console.log(`\nmax |keras - tfjs|      ${maxAbs.toExponential(3)}`);
console.log(`max relative diff       ${maxRel.toExponential(3)}`);
console.log(`argmax mismatches       ${argmaxMismatch} / ${n}`);
console.log(`tolerance               ${TOL.toExponential(0)}`);

const pass = maxAbs <= TOL && argmaxMismatch === 0;
console.log(`\n${pass ? 'PASS - outputs match' : 'FAIL - outputs differ'}`);
process.exit(pass ? 0 : 1);
