# Keras -> TensorFlow.js conversion + equivalence check

Three steps: export reference vectors from Keras, convert the model, compare.

## One-time setup

```powershell
# converter env -- MUST be built from the project's Python 3.10
.venv\Scripts\python.exe -m venv .tfjsenv
.tfjsenv\Scripts\python.exe -m pip install "tensorflow==2.15.0" "tensorflow-hub==0.16.1" packaging six importlib_resources
.tfjsenv\Scripts\python.exe -m pip install --no-deps "tensorflowjs==4.17.0" "jax==0.4.30" "jaxlib==0.4.30" "scipy==1.11.4"

npm install
```

Four things make this setup fussier than the docs suggest, all Windows-specific:

- **Python 3.10, not the system interpreter.** The `python` on PATH here is
  3.14, which has no TensorFlow wheels at all, so the install fails with an
  unhelpful resolver error.
- **A separate env.** `tensorflowjs` pins its own `tensorflow`/`jax`.
  Installing it into `.venv` risks moving the TF 2.15 that `mediapipe` and the
  notebooks sit on.
- **`--no-deps` with pinned versions.** `tensorflowjs` depends on
  `tensorflow-decision-forests`, which publishes *no Windows wheels*. Letting
  pip resolve freely sends it backtracking until it gives up with
  "Dependency resolution exceeded maximum depth".
- **A short env path.** TensorFlow's own paths are deep; putting the env under
  a long temp path trips the 260-character Windows path limit mid-install and
  leaves a half-written `tensorflow` package.

`scripts/convert_to_tfjs.py` also stubs out the `tensorflow_decision_forests`
import (the module imports it and never uses it) because the
`tensorflowjs_converter` CLI cannot start on Windows without it.

## 1. Reference vectors (project venv -- this is the ground truth)

```powershell
.venv\Scripts\python.exe scripts\export_reference.py action.h5
```

Reads the input shape off the model, generates `--samples` inputs across
several magnitudes, and writes inputs + Keras outputs to
`tfjs_build\<name>\reference.json`.

The spread of magnitudes matters. Noise at one fixed scale drives the softmax
to one-hot, which makes every diff ~0 and the comparison pass regardless of
whether the conversion was correct. The script prints `non-saturated samples`
so you can see the test has teeth.

## 2. Convert (converter venv)

```powershell
.tfjsenv\Scripts\python.exe scripts\convert_to_tfjs.py action.h5
.tfjsenv\Scripts\python.exe scripts\convert_to_tfjs.py action.h5 --quantize uint16
```

Writes `tfjs_build\<name>\model\` (`model.json` + weight shards) and prints the
size of every file.

## 3. Compare

```powershell
node scripts\compare_tfjs.mjs tfjs_build\action
node scripts\compare_tfjs.mjs tfjs_build\action --tol 1e-3   # for uint16 weights
```

Runs the converted model on the identical inputs and reports max absolute diff,
max relative diff, and argmax mismatches. Exit code 0 = pass.

It uses the plain `@tensorflow/tfjs` CPU backend, the same kernels the browser
runs, so a pass here carries over to the browser. `tfjs-node` would exercise
different native kernels and prove less.

## Measured result for `action.h5` ((30, 1662) -> 3 classes, 596,675 params)

| build | size | max abs diff vs Keras | argmax mismatches |
|---|---|---|---|
| `action.h5` (source) | 6.9 MB | - | - |
| float32 TF.js | 2.3 MB (`model.json` 6.1 KB + 2.3 MB shard) | 2.98e-8 | 0 / 8 |
| uint16 TF.js | 1.1 MB (`model.json` 8.0 KB + 1.1 MB shard) | 1.85e-6 | 0 / 8 |

float32 lands at float32 rounding noise, i.e. exact. The `.h5` is 3x bigger
than the float32 TF.js build mostly because it also carries the optimizer
state, which the browser does not need.

## The pose model (`pose_action.h5`) -- this is the one the game uses

`train_pose_model.py` replaces the training path in
`LSTM learn/Classification_Pose.ipynb`, which trains this model but never calls
`model.save()` -- its weights only ever existed in a notebook kernel.

```powershell
.venv\Scripts\python.exe scripts\train_pose_model.py
.venv\Scripts\python.exe scripts\export_reference.py pose_action.h5
.tfjsenv\Scripts\python.exe scripts\convert_to_tfjs.py pose_action.h5
.venv\Scripts\python.exe scripts\export_feature_fixtures.py
npm test
```

`(36, 60) -> 6 classes`, 45,670 params. Measured: 93.3% on held-out session 3.

| build | size | max abs diff vs Keras | argmax mismatches |
|---|---|---|---|
| `pose_action.h5` (source) | 585 KB | - | - |
| float32 TF.js | 183 KB (`model.json` 4.5 KB + 178 KB shard) | 1.19e-7 | 0 / 8 |

`train_pose_model.py` also writes `tfjs_build/pose_action/meta.json` with the
class order and feature spec. The browser reads the class order from there
rather than hardcoding it, because `argmax` returns an integer and the order is
**not** alphabetical (`jab, cross, hook, uppercut, idle, block`) -- guessing it
remaps every prediction with no error.

`build_features` (aspect scaling, hip-centering, shoulder-hip scale, velocity
diff) is **not** part of the model. It lives in `pose_features.py` and is ported
to `web/pose-features.js`; `compare_features.mjs` proves the two agree, because
feeding the model raw landmarks produces confident nonsense rather than a crash.

## `action.h5` is the OLD model -- not used by the game

`(30, 1662) -> 3 classes`, from `LSTM-learn.ipynb`. The classes are
`hello`, `thanks`, `iloveyou` -- this is a **sign-language tutorial** the
project started from, not a boxing model. 1662 features means MediaPipe
**Holistic** (pose + face mesh + both hands), a different input pipeline
entirely. Kept only for reference; `tfjs_build/action/` and
`tfjs_build/action_uint16/` are its builds.
