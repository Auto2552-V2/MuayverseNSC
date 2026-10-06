"""
Measure what mirroring actually does to MediaPipe's output.

    .venv/Scripts/python.exe scripts/probe_mirror.py
    .venv/Scripts/python.exe scripts/probe_mirror.py --video Video/CROSS.mp4

This exists because getting it wrong is silent and expensive. The training
capture mirrored the frame before MediaPipe saw it:

    frame = cv2.flip(frame, 1)
    image, results = mediapipe_detection(frame, pose)

so every label the model learned is in mirrored-image space. The browser has to
reproduce that, and there are two plausible ways to do it:

    H1  flip the output coordinates:  x -> 1-x, indices unchanged
    H2  flip the input image:         x -> 1-x AND left/right indices swap

They are NOT equivalent. MediaPipe names landmarks by the anatomy it sees, so a
mirrored person has their apparent left and right reversed and index 15
("left wrist") lands on the real RIGHT wrist. H1 skips that swap, which
left/right-swaps every feature against training -- and jab vs cross is exactly
a left-hand vs right-hand distinction, so it breaks the two most common moves
while still returning confident answers.

This script measures both hypotheses against ground truth. Run it if anyone
proposes "simplifying" web/pose-detector.js to flip coordinates instead of the
image.

Measured on Video/JAB.mp4 frame 30:   H1 0.0837   H2 0.0059   -> H2, by 14x.
"""
import argparse

import cv2
import numpy as np
import mediapipe as mp

mp_pose = mp.solutions.pose

NAMES = {
    11: 'shoulderL', 12: 'shoulderR', 13: 'elbowL', 14: 'elbowR',
    15: 'wristL', 16: 'wristR', 23: 'hipL', 24: 'hipR',
}

# MediaPipe's 33-point topology, as left/right pairs. Index 0 (nose) and the
# mouth/eye inner points have no partner that matters here.
LR_PAIRS = [(1, 4), (2, 5), (3, 6), (7, 8), (9, 10), (11, 12), (13, 14),
            (15, 16), (17, 18), (19, 20), (21, 22), (23, 24), (25, 26),
            (27, 28), (29, 30), (31, 32)]


def landmarks(img, pose):
    res = pose.process(cv2.cvtColor(img, cv2.COLOR_BGR2RGB))
    if not res.pose_landmarks:
        return None
    return np.array([[l.x, l.y, l.z, l.visibility]
                     for l in res.pose_landmarks.landmark])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--video', default='Video/JAB.mp4')
    ap.add_argument('--frame', type=int, default=30)
    args = ap.parse_args()

    cap = cv2.VideoCapture(args.video)
    cap.set(cv2.CAP_PROP_POS_FRAMES, args.frame)
    ok, frame = cap.read()
    cap.release()
    if not ok:
        raise SystemExit(f'cannot read frame {args.frame} of {args.video}')

    with mp_pose.Pose(model_complexity=1, min_detection_confidence=0.5,
                      min_tracking_confidence=0.5, static_image_mode=True) as pose:
        plain = landmarks(frame, pose)
        flipped = landmarks(cv2.flip(frame, 1), pose)

    if plain is None or flipped is None:
        raise SystemExit('no pose detected -- try a different --frame')

    print(f'{args.video} frame {args.frame}, {frame.shape[1]}x{frame.shape[0]}\n')
    print(f'{"idx":>4} {"name":<10} {"plain.x":>8} {"flip.x":>8} {"1-flip.x":>9}')
    for i in sorted(NAMES):
        print(f'{i:>4} {NAMES[i]:<10} {plain[i,0]:>8.4f} '
              f'{flipped[i,0]:>8.4f} {1 - flipped[i,0]:>9.4f}')

    swap = list(range(33))
    for a, b in LR_PAIRS:
        swap[a], swap[b] = b, a

    h1 = float(np.abs(plain[:, 0] - (1 - flipped[:, 0])).mean())
    h2 = float(np.abs(plain[:, 0] - (1 - flipped[swap, 0])).mean())

    print(f'\nH1  x -> 1-x only                  mean |diff| = {h1:.5f}')
    print(f'H2  x -> 1-x AND swap L/R indices  mean |diff| = {h2:.5f}')

    winner = 'H2' if h2 < h1 else 'H1'
    print(f'\n-> {winner} describes MediaPipe, by {max(h1, h2) / max(min(h1, h2), 1e-9):.1f}x')

    if winner == 'H2':
        print('\nSo mirroring the image relabels left/right. web/pose-detector.js\n'
              'mirrors the IMAGE in _sourceFrame() for exactly this reason --\n'
              'flipping coordinates afterwards would swap every limb against\n'
              'what the model was trained on.')
    else:
        print('\nUnexpected: this contradicts the design of pose-detector.js.\n'
              'Re-check with another --video/--frame before changing anything.')


if __name__ == '__main__':
    main()
