"""
Feature extraction for the boxing-pose LSTM -- the single source of truth.

This is a straight lift of `build_features` from
`LSTM learn/Classification_Pose.ipynb`. It lives here, not in the notebook,
because three separate places have to agree on it exactly:

  1. training              (scripts/train_pose_model.py)
  2. the browser detector  (web/pose-features.js -- a hand port)
  3. the parity check      (scripts/export_feature_fixtures.py)

The model never sees raw landmarks. Feed it raw landmarks and it returns
confident nonsense, because every number it was trained on went through the
normalisation below. That is the single easiest way to break this pipeline,
and it fails silently -- hence the fixtures and the JS parity test.

Wire format of a raw frame: MediaPipe Pose's 33 landmarks flattened as
(x, y, z, visibility) each -> 132 floats. `visibility` is read but never used
as a feature; it is dropped here.
"""
import numpy as np

# Class order is part of the model. argmax returns an index into THIS list, so
# a reordering here silently remaps every prediction (jab becomes cross, ...).
# It is saved alongside the weights for exactly that reason.
ACTIONS = ['jab', 'cross', 'hook', 'uppercut', 'idle', 'block']

N_RAW = 33 * 4           # pose landmarks: 33 points x (x, y, z, visibility)
SEQUENCE_LENGTH = 36     # ~1.3 s at the ~28 fps the capture loop measured

# The capture ran at 640x480, and MediaPipe normalises x and y independently by
# frame width/height -- so one unit of x is 4/3 as wide as one unit of y. Undo
# that here or every angle in the feature vector is sheared.
FRAME_W, FRAME_H = 640, 480
ASPECT = FRAME_W / FRAME_H

# shoulders(11,12) elbows(13,14) wrists(15,16) index(19,20) hips(23,24)
# Hips are in for the trunk-rotation reference that separates hook from
# uppercut; legs and face carry no signal for punches and only add noise.
POSE_IDX = [11, 12, 13, 14, 15, 16, 19, 20, 23, 24]

N_FEATURES = len(POSE_IDX) * 3 * 2      # 10 points x 3 axes x [pos, vel] = 60


def build_features(raw_seq, aspect=ASPECT):
    """(T, 132) raw landmarks -> (T, 60) normalised position + velocity.

    Normalisation is per frame and makes the features invariant to where the
    player stands and how big they look:
      - centre on mid-hip   -> position in frame stops mattering
      - divide by torso len -> distance from the camera stops mattering
    Velocity is the frame-to-frame difference of those positions, and is what
    separates moves that pass through the same shapes at different speeds
    (uppercut rises fast, block holds still).
    """
    raw_seq = np.asarray(raw_seq, dtype=np.float32)
    lm = raw_seq.reshape(len(raw_seq), 33, 4)
    xyz = lm[:, :, :3].copy()
    xyz[:, :, 0] *= aspect

    mid_hip = (xyz[:, 23] + xyz[:, 24]) / 2
    mid_sh = (xyz[:, 11] + xyz[:, 12]) / 2

    # Torso length as the scale unit. Clamped because a frame where MediaPipe
    # found nothing is all zeros -> scale 0 -> a feature vector of inf/nan that
    # poisons the whole 36-frame window.
    scale = np.linalg.norm(mid_sh - mid_hip, axis=1, keepdims=True)
    scale = np.maximum(scale, 1e-6)

    norm = (xyz - mid_hip[:, None, :]) / scale[:, None, :]
    pos = norm[:, POSE_IDX, :].reshape(len(raw_seq), -1)

    vel = np.zeros_like(pos)
    vel[1:] = np.diff(pos, axis=0)      # frame 0 has no predecessor -> zeros

    return np.concatenate([pos, vel], axis=1)


def shift_clip(raw, k):
    """Slide a clip k frames in time, repeating the end frames.

    Training augmentation: the recorded clips all have the punch at the same
    offset, so without this the model learns "the punch starts at frame 12"
    and falls apart on a live rolling buffer where the phase is arbitrary.
    """
    arr = np.asarray(raw, dtype=np.float32)
    idx = np.clip(np.arange(len(arr)) - k, 0, len(arr) - 1)
    return arr[idx]
