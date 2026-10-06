"""
Export real clips as fixtures so the JS feature port can be proven equivalent.

`build_features` is implemented twice -- once in Python for training, once in
JS for the browser. Nothing enforces that they agree, and when they drift the
model does not crash: it keeps returning confident, wrong answers. That is the
worst possible failure mode, so it gets a test.

    .venv/Scripts/python.exe scripts/export_feature_fixtures.py

Writes tfjs_build/pose_action/feature_fixtures.json
    {sequence_length, n_features, cases: [{action, raw[T][132], expected[T][60]}]}

Then:  node scripts/compare_features.mjs

Real landmarks, not random noise: noise never exercises the degenerate paths
that actually occur live -- a frame where MediaPipe found no one (all zeros,
scale clamped) or a player stood so square that mid-shoulder sits on mid-hip.
One all-zero case is injected for exactly that reason.
"""
import argparse
import json
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pose_features import ACTIONS, N_FEATURES, SEQUENCE_LENGTH, build_features


def load_clip(data_path, session, action, seq):
    return np.array([
        np.load(os.path.join(data_path, f'session_{session}', action,
                             str(seq), f'{f}.npy'))
        for f in range(SEQUENCE_LENGTH)
    ], dtype=np.float32)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--data', default=os.path.join('LSTM learn', 'MP_Data_Diag'))
    ap.add_argument('--session', type=int, default=3)   # the held-out session
    ap.add_argument('--per-action', type=int, default=2)
    ap.add_argument('--out-dir', default=os.path.join('tfjs_build', 'pose_action'))
    args = ap.parse_args()

    cases = []
    for action in ACTIONS:
        for seq in range(args.per_action):
            raw = load_clip(args.data, args.session, action, seq)
            cases.append({
                'action': action,
                'note': f'session_{args.session}/{action}/{seq}',
                'raw': raw.tolist(),
                'expected': build_features(raw).tolist(),
            })

    # Degenerate cases the live feed produces and random data never does.
    zeros = np.zeros((SEQUENCE_LENGTH, 33 * 4), dtype=np.float32)
    cases.append({'action': 'none', 'note': 'all-zero frames (no pose found)',
                  'raw': zeros.tolist(),
                  'expected': build_features(zeros).tolist()})

    # A real clip with a dropout burst spliced in -- the buffer straddling a
    # tracking loss is the common live case, and it mixes clamped and normal
    # frames inside one window where the velocity diff crosses the boundary.
    partial = load_clip(args.data, args.session, 'jab', 0).copy()
    partial[10:14] = 0.0
    cases.append({'action': 'jab', 'note': 'jab with frames 10-13 dropped',
                  'raw': partial.tolist(),
                  'expected': build_features(partial).tolist()})

    os.makedirs(args.out_dir, exist_ok=True)
    path = os.path.join(args.out_dir, 'feature_fixtures.json')
    with open(path, 'w') as f:
        json.dump({'sequence_length': SEQUENCE_LENGTH,
                   'n_features': N_FEATURES,
                   'cases': cases}, f)

    exp = np.array([c['expected'] for c in cases], dtype=np.float32)
    print(f'cases    {len(cases)}')
    print(f'shape    {exp.shape}')
    print(f'finite   {bool(np.isfinite(exp).all())}')
    print(f'range    [{exp.min():.4f}, {exp.max():.4f}]')
    print(f'wrote    {path}  ({os.path.getsize(path)/1024:.0f} KB)')


if __name__ == '__main__':
    main()
