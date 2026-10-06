"""
Train the 6-class boxing-pose LSTM and SAVE IT.

`Classification_Pose.ipynb` trains this model but never calls `model.save()`,
so the weights only ever existed in a notebook kernel. This script is that
notebook's training path, reproducible from the command line, writing both the
weights and the metadata the browser needs to use them correctly.

    .venv/Scripts/python.exe scripts/train_pose_model.py

Outputs:
    pose_action.h5                    weights (feed to convert_to_tfjs.py)
    tfjs_build/pose_action/meta.json  class order + feature spec

The metadata is not optional bookkeeping. `argmax` gives an integer; without
the stored class order the browser cannot turn it into "jab", and guessing
(alphabetical, say) maps every prediction to the wrong move.

Sessions are split by RECORDING SESSION, not by shuffling clips. Clips inside
one session share lighting, camera placement and the player's warmed-up form,
so a random split leaks that across train/test and reports an accuracy the
live game will never reproduce.
"""
import argparse
import json
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pose_features import (ACTIONS, N_FEATURES, POSE_IDX, SEQUENCE_LENGTH,
                           ASPECT, build_features, shift_clip)

# Time-shift augmentation, +-25% of the window. The live detector classifies a
# rolling buffer whose phase is arbitrary, so the model has to recognise a jab
# that is half-finished at the edge of the window.
SHIFTS = (-9, -4, 0, 4, 9)


def load_sessions(data_path, session_ids, shifts=(0,)):
    from tensorflow.keras.utils import to_categorical
    label_map = {label: num for num, label in enumerate(ACTIONS)}

    Xs, ys = [], []
    for sid in session_ids:
        spath = os.path.join(data_path, f'session_{sid}')
        if not os.path.isdir(spath):
            print(f'  skip session_{sid} (not recorded)')
            continue
        for action in ACTIONS:
            apath = os.path.join(spath, action)
            for seq in sorted(os.listdir(apath), key=int):
                raw = [np.load(os.path.join(apath, seq, f'{f}.npy'))
                       for f in range(SEQUENCE_LENGTH)]
                for k in shifts:
                    Xs.append(build_features(shift_clip(raw, k)))
                    ys.append(label_map[action])

    if not Xs:
        raise SystemExit(f'no clips loaded from {data_path} for {session_ids}')
    return (np.array(Xs, dtype=np.float32),
            to_categorical(ys, len(ACTIONS)).astype(int))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--data', default=os.path.join('LSTM learn', 'MP_Data_Diag'))
    ap.add_argument('--train', type=int, nargs='+', default=[0, 1])
    ap.add_argument('--val', type=int, nargs='+', default=[2])
    ap.add_argument('--test', type=int, nargs='+', default=[3])
    ap.add_argument('--epochs', type=int, default=500)
    ap.add_argument('--patience', type=int, default=40)
    ap.add_argument('--batch-size', type=int, default=16)
    ap.add_argument('--seed', type=int, default=1234)
    ap.add_argument('--out', default='pose_action.h5')
    args = ap.parse_args()

    os.environ.setdefault('TF_CPP_MIN_LOG_LEVEL', '2')
    import tensorflow as tf
    from tensorflow.keras.models import Sequential
    from tensorflow.keras.layers import LSTM, Dense, Dropout
    from tensorflow.keras.callbacks import EarlyStopping

    # Without this the run is unreproducible and "did my change help?" becomes
    # unanswerable -- run-to-run variance on 90 clips is larger than most
    # real improvements.
    tf.keras.utils.set_random_seed(args.seed)

    print(f'data     {args.data}')
    print(f'classes  {ACTIONS}')
    print(f'window   {SEQUENCE_LENGTH} frames x {N_FEATURES} features\n')

    X_train, y_train = load_sessions(args.data, args.train, SHIFTS)
    X_val, y_val = load_sessions(args.data, args.val)       # no augmentation:
    X_test, y_test = load_sessions(args.data, args.test)    # must match live
    print(f'train {X_train.shape}  sessions={args.train}  x{len(SHIFTS)} shifts')
    print(f'val   {X_val.shape}  sessions={args.val}')
    print(f'test  {X_test.shape}  sessions={args.test}\n')

    model = Sequential([
        LSTM(64, return_sequences=True,
             input_shape=(SEQUENCE_LENGTH, N_FEATURES)),
        Dropout(0.3),
        LSTM(32),
        Dropout(0.3),
        Dense(32, activation='relu'),
        Dense(len(ACTIONS), activation='softmax'),
    ])
    model.compile(optimizer='Adam', loss='categorical_crossentropy',
                  metrics=['categorical_accuracy'])
    model.summary()

    # restore_best_weights matters here: val loss on this little data is noisy
    # and the last epoch is routinely worse than the best one.
    model.fit(X_train, y_train, epochs=args.epochs,
              batch_size=args.batch_size, validation_data=(X_val, y_val),
              callbacks=[EarlyStopping(monitor='val_loss',
                                       patience=args.patience,
                                       restore_best_weights=True)],
              verbose=2)

    print('\n' + '=' * 60)
    from sklearn.metrics import classification_report, confusion_matrix
    for name, X, y in (('TRAIN', X_train, y_train),
                       ('VAL', X_val, y_val),
                       ('TEST', X_test, y_test)):
        y_true = np.argmax(y, axis=1)
        y_pred = np.argmax(model.predict(X, verbose=0), axis=1)
        acc = float(np.mean(y_pred == y_true))
        print(f'\n===== {name}  n={len(y_true)}  acc={acc:.1%} =====')
        print(classification_report(y_true, y_pred,
                                    labels=list(range(len(ACTIONS))),
                                    target_names=ACTIONS, zero_division=0))
        cm = confusion_matrix(y_true, y_pred, labels=list(range(len(ACTIONS))))
        print('confusion (rows=true, cols=pred), order:', ACTIONS)
        print(cm)

    model.save(args.out)

    stem = os.path.splitext(os.path.basename(args.out))[0]
    meta_dir = os.path.join('tfjs_build', stem)
    os.makedirs(meta_dir, exist_ok=True)
    meta = {
        'actions': ACTIONS,
        'sequence_length': SEQUENCE_LENGTH,
        'n_features': N_FEATURES,
        'n_raw': 33 * 4,
        'pose_idx': POSE_IDX,
        'aspect': ASPECT,
        'frame_size': [640, 480],
        'mirror': True,          # capture used cv2.flip(frame, 1)
        'feature_layout': 'concat[pos(10x3), vel(10x3)] per frame',
        'normalisation': 'x*=aspect; centre mid-hip(23,24); '
                         'scale=|mid_shoulder-mid_hip| clamp 1e-6',
        'train_sessions': args.train,
        'val_sessions': args.val,
        'test_sessions': args.test,
        'seed': args.seed,
    }
    meta_path = os.path.join(meta_dir, 'meta.json')
    with open(meta_path, 'w') as f:
        json.dump(meta, f, indent=2)

    print(f'\nsaved  {args.out}  ({os.path.getsize(args.out)/1024:.0f} KB)')
    print(f'saved  {meta_path}')


if __name__ == '__main__':
    main()
