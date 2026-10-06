"""
Export reference input/output vectors from the original Keras model.

Run with the PROJECT venv (.venv) so the Keras version matches the one that
trained the model -- these numbers are the ground truth the TF.js model is
compared against.

    .venv/Scripts/python.exe scripts/export_reference.py action.h5

Writes <out_dir>/reference.json  {input_shape, output_shape, seed, inputs, outputs}
"""
import argparse
import json
import os

import numpy as np


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('model', help='path to .h5 / .keras / SavedModel dir')
    ap.add_argument('--out-dir', default=None,
                    help='default: tfjs_build/<model stem>')
    ap.add_argument('--samples', type=int, default=8)
    ap.add_argument('--seed', type=int, default=1234)
    args = ap.parse_args()

    os.environ.setdefault('TF_CPP_MIN_LOG_LEVEL', '2')
    from tensorflow.keras.models import load_model

    model = load_model(args.model, compile=False)
    model.summary()

    in_shape = [d for d in model.inputs[0].shape]        # (None, T, F)
    out_shape = [d for d in model.outputs[0].shape]
    if any(d is None for d in in_shape[1:]):
        raise SystemExit(f'input shape has undefined dims: {in_shape}')

    stem = os.path.splitext(os.path.basename(os.path.normpath(args.model)))[0]
    out_dir = args.out_dir or os.path.join('tfjs_build', stem)
    os.makedirs(out_dir, exist_ok=True)

    rng = np.random.default_rng(args.seed)
    frame_shape = [int(d) for d in in_shape[1:]]

    # A single input scale is a trap: feed the model noise far outside its
    # training distribution and the softmax saturates to one-hot, every diff
    # collapses to ~0, and the test passes no matter how wrong the conversion
    # is. Spread the batch over several magnitudes so some samples land in a
    # regime where the output probabilities are actually mixed.
    scales = np.geomspace(0.02, 2.0, args.samples)
    x = np.stack([(rng.standard_normal(frame_shape) * s).astype(np.float32)
                  for s in scales])

    y = model.predict(x, verbose=0).astype(np.float32)
    max_prob = y.max(axis=1)

    ref = {
        'model': os.path.abspath(args.model),
        'input_shape': [int(d) for d in in_shape[1:]],
        'output_shape': [int(d) for d in out_shape[1:]],
        'seed': args.seed,
        'samples': args.samples,
        'scales': scales.tolist(),
        'inputs': x.reshape(args.samples, -1).tolist(),
        'outputs': y.tolist(),
    }
    path = os.path.join(out_dir, 'reference.json')
    with open(path, 'w') as f:
        json.dump(ref, f)

    n_soft = int((max_prob < 0.99).sum())
    print(f'\ninput    {x.shape}')
    print(f'output   {y.shape}')
    print(f'argmax   {y.argmax(axis=1).tolist()}')
    print(f'max prob {np.array2string(max_prob, precision=3)}')
    print(f'non-saturated samples: {n_soft}/{args.samples}')
    if n_soft == 0:
        print('WARNING: every softmax is one-hot -- a comparison on these\n'
              '         vectors proves little. Lower the input scales.')
    print(f'wrote    {path}  ({os.path.getsize(path)/1024:.0f} KB)')


if __name__ == '__main__':
    main()
