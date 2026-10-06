"""
Convert a Keras model (.h5 / .keras / SavedModel) to TF.js Layers format.

Run with the CONVERTER venv (.tfjsenv), not the project venv -- tensorflowjs
pins its own tensorflow/jax and would break the TF 2.15 that mediapipe and the
notebooks sit on.

    .tfjsenv\Scripts\python.exe scripts\convert_to_tfjs.py action.h5
    ... --quantize uint16          # ~2x smaller weights

Why this calls the Python API instead of the tensorflowjs_converter CLI:
tensorflowjs 4.x imports tensorflow_decision_forests at package import time,
and tfdf publishes no Windows wheels, so the CLI cannot even start here. The
stub below satisfies that import; tfdf is only ever used for decision-forest
models, never for a Keras LSTM (the module imports it and never touches it).
"""
import argparse
import os
import sys
import types


def _install_stubs():
    """Make `import tensorflowjs` survive on Windows (see module docstring)."""
    import importlib.util
    if importlib.util.find_spec('tensorflow_decision_forests') is None:
        sys.modules['tensorflow_decision_forests'] = types.ModuleType(
            'tensorflow_decision_forests')


def dir_size(path):
    total = 0
    for root, _, files in os.walk(path):
        for name in files:
            total += os.path.getsize(os.path.join(root, name))
    return total


def human(n):
    if n < 1024:
        return f'{n} B'
    for unit in ('KB', 'MB', 'GB'):
        n /= 1024
        if n < 1024 or unit == 'GB':
            return f'{n:.1f} {unit}'


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('model')
    ap.add_argument('--out-dir', default=None,
                    help='default: tfjs_build/<model stem>/model')
    ap.add_argument('--quantize', choices=['none', 'uint8', 'uint16'],
                    default='none')
    ap.add_argument('--shard-mb', type=float, default=4.0,
                    help='weight shard size; 4 MB keeps shards cacheable')
    args = ap.parse_args()

    os.environ.setdefault('TF_CPP_MIN_LOG_LEVEL', '2')
    _install_stubs()

    import tensorflowjs as tfjs
    from tensorflow.keras.models import load_model

    src = os.path.normpath(args.model)
    stem = os.path.splitext(os.path.basename(src))[0]
    out_dir = args.out_dir or os.path.join('tfjs_build', stem, 'model')
    os.makedirs(out_dir, exist_ok=True)

    # load_model handles .h5, .keras and a Keras-exported SavedModel directory
    # alike, which keeps one code path for all three input formats.
    model = load_model(src, compile=False)
    model.summary()

    quant = None if args.quantize == 'none' else {args.quantize: '*'}
    tfjs.converters.save_keras_model(
        model, out_dir,
        quantization_dtype_map=quant,
        weight_shard_size_bytes=int(args.shard_mb * 1024 * 1024))

    src_size = dir_size(src) if os.path.isdir(src) else os.path.getsize(src)
    out_size = dir_size(out_dir)
    print(f'\nsource      {src}  {human(src_size)}')
    print(f'quantize    {args.quantize}')
    print(f'tfjs output {out_dir}')
    for name in sorted(os.listdir(out_dir)):
        p = os.path.join(out_dir, name)
        print(f'  {name:<32} {human(os.path.getsize(p))}')
    print(f'  {"TOTAL":<32} {human(out_size)}'
          f'   ({out_size / src_size * 100:.0f}% of source)')


if __name__ == '__main__':
    main()
