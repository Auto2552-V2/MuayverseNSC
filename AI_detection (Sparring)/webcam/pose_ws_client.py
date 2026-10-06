"""
Webcam (or video file) -> MediaPipe Pose -> LSTM -> WebSocket -> Unity.

    python webcam/pose_ws_client.py                  # webcam -> ws://127.0.0.1:8787
    python webcam/pose_ws_client.py --no-ws          # webcam, print only
    python webcam/pose_ws_client.py --video ../Video/JAB.mp4 --no-ws

Desktop test path only. The shipping path is the browser detector
(web/pose-detector.js) inside the Android WebView -- see CLAUDE.md.

Consistency with the rest of the pipeline:
  * Capture + MediaPipe use the legacy `mp.solutions.pose` at model_complexity=1,
    the same call the training notebook made (LSTM learn/Classification_Pose.ipynb).
  * Features come from scripts/pose_features.py, the single source of truth.
  * The image is flipped with cv2.flip BEFORE MediaPipe, matching training.
    Do not flip the coordinates afterwards -- see CLAUDE.md "Mirror".
  * The gate below is a port of web/pose-detector.js (_onFrame / _gate / _emit),
    so both paths emit the same messages for the same input.
"""
import argparse
import json
import os
import sys
import time
from collections import deque

import cv2
import mediapipe as mp
import numpy as np

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
sys.path.insert(0, os.path.join(ROOT, 'scripts'))
from pose_features import build_features, ACTIONS, SEQUENCE_LENGTH, N_RAW  # noqa: E402

MODEL_PATH = os.path.join(ROOT, 'pose_action.h5')
TFLITE_PATH = os.path.join(os.path.dirname(__file__), 'pose_action.tflite')
POSE_KW = dict(model_complexity=1, min_detection_confidence=0.5, min_tracking_confidence=0.5)


# ── โมเดลจำแนกท่า ──────────────────────────────────────────────────────────────

class Classifier:
    """โหลด pose_action.h5 แล้วรันผ่าน TFLite

    ทำไมไม่เรียก model.predict() ตรง ๆ: วัดบนเครื่องนี้ได้ 154 ms ต่อเฟรม (6 fps)
    ซึ่งเล่นไม่ได้ ตัวโมเดลเล็กมาก (45k params) ที่ช้าคือ overhead ของ Keras
    ต่อการเรียกหนึ่งครั้ง TFLite ตัวเดียวกันวัดได้ 4.9 ms (เร็วกว่า 30 เท่า)
    และให้ผลตรงกันที่ atol=1e-4
    """

    def __init__(self, h5_path=MODEL_PATH, tflite_path=TFLITE_PATH):
        import tensorflow as tf
        if not os.path.exists(tflite_path) or os.path.getmtime(tflite_path) < os.path.getmtime(h5_path):
            print(f'[model] แปลง {os.path.basename(h5_path)} -> tflite (ทำครั้งเดียว)', flush=True)
            self._convert(tf, h5_path, tflite_path)

        self.it = tf.lite.Interpreter(model_path=tflite_path)
        self.it.allocate_tensors()
        self.inp = self.it.get_input_details()[0]
        self.out = self.it.get_output_details()[0]

        shape = tuple(self.inp['shape'][1:])
        if shape != (SEQUENCE_LENGTH, 60):
            raise SystemExit(f'โมเดลรับ {shape} แต่ pipeline ส่ง ({SEQUENCE_LENGTH}, 60) — คนละโมเดลกัน')
        print(f'[model] {os.path.basename(tflite_path)} input={shape}', flush=True)

    @staticmethod
    def _convert(tf, h5_path, tflite_path):
        from tensorflow import keras
        model = keras.models.load_model(h5_path, compile=False)
        conv = tf.lite.TFLiteConverter.from_keras_model(model)
        # LSTM บางส่วนไม่มี builtin op ใน TFLite ต้องเปิด select ops (ใช้ runtime ของ TF ที่มีอยู่แล้ว)
        conv.target_spec.supported_ops = [tf.lite.OpsSet.TFLITE_BUILTINS, tf.lite.OpsSet.SELECT_TF_OPS]
        with open(tflite_path, 'wb') as f:
            f.write(conv.convert())

    def __call__(self, feats):
        self.it.set_tensor(self.inp['index'], feats[None, ...].astype(np.float32))
        self.it.invoke()
        return self.it.get_tensor(self.out['index'])[0]


# ── ตัวกรอง (พอร์ตจาก web/pose-detector.js) ──────────────────────────────────────

class PoseGate:
    def __init__(self, threshold=0.7, consensus=3, release_frames=3,
                 emit_cooldown_ms=250, min_presence=0.8,
                 sustained=('block',), sustained_repeat_ms=700, idle='idle',
                 repeat_same_after_ms=0):
        self.threshold = threshold
        self.consensus = consensus
        self.release_frames = release_frames
        self.emit_cooldown_ms = emit_cooldown_ms
        self.min_presence = min_presence
        self.sustained = set(sustained)
        self.sustained_repeat_ms = sustained_repeat_ms
        self.idle = idle
        # > 0 = ส่งท่าเดิมซ้ำได้หลังเว้นระยะเท่านี้ แม้ยังไม่หลุดจากท่า
        # ต้องตรงกับ repeatSameAfterMs ใน web/pose-detector.js
        self.repeat_same_after_ms = repeat_same_after_ms

        self.recent = deque(maxlen=consensus)
        self.locked = None
        self.release_count = 0
        self.last_emit_ms = -1e12
        self.emit_seq = 0

    def reset_recent(self):
        self.recent.clear()

    def feed(self, probs, now_ms):
        """คืน (pose, confidence) ถ้าควรส่ง ไม่งั้นคืน None"""
        best = int(np.argmax(probs))
        action = ACTIONS[best]
        confidence = float(probs[best])

        self.recent.append(best)

        if self.locked is not None:
            if action != self.locked:
                self.release_count += 1
                if self.release_count >= self.release_frames:
                    self.locked = None
                    self.release_count = 0
            else:
                self.release_count = 0

        if action == self.idle:
            return None
        if confidence < self.threshold:
            return None
        if len(self.recent) < self.consensus:
            return None
        if any(v != best for v in self.recent):
            return None

        if self.locked == action:
            if action in self.sustained:
                repeat_ms = self.sustained_repeat_ms
            elif self.repeat_same_after_ms > 0:
                repeat_ms = self.repeat_same_after_ms
            else:
                return None
            if now_ms - self.last_emit_ms < repeat_ms:
                return None
        elif now_ms - self.last_emit_ms < self.emit_cooldown_ms:
            return None

        self.locked = action
        self.release_count = 0
        self.last_emit_ms = now_ms
        return action, confidence

    def payload(self, pose, confidence, now_ms):
        self.emit_seq += 1
        return {
            'type': 'pose',
            'pose': pose,
            'confidence': round(confidence, 4),
            'seq': self.emit_seq,
            't': int(now_ms),
        }


# ── ผู้ส่ง WebSocket (ต่อใหม่เองถ้าหลุด) ───────────────────────────────────────

class Sender:
    def __init__(self, url):
        self.url = url
        self.conn = None

    def _connect(self):
        from websockets.sync.client import connect
        try:
            self.conn = connect(self.url, open_timeout=2)
            print(f'[ws] ต่อ {self.url} แล้ว')
        except Exception as e:
            self.conn = None
            print(f'[ws] ต่อไม่ได้ ({e}) — เปิด Unity Play mode ก่อนหรือยัง?')

    def send(self, obj):
        if self.conn is None:
            self._connect()
            if self.conn is None:
                return False
        try:
            self.conn.send(json.dumps(obj))
            return True
        except Exception as e:
            print(f'[ws] ส่งไม่ได้ ({e}) — จะลองต่อใหม่รอบถัดไป')
            self.conn = None
            return False


# ── หน้าต่างพรีวิว ──────────────────────────────────────────────────────────────

def draw_preview(frame, results, probs, presence, frames, now_ms, last_sent=None, scale=0.5):
    """วาดโครงกระดูก + แถบความมั่นใจ คืน False เมื่อผู้ใช้กด q

    มีไว้ให้ผู้เล่นเห็นว่าตัวเองอยู่ในกรอบไหมและระบบกำลังอ่านท่าอะไรอยู่
    ถ้าไม่มีอันนี้ เวลาท่าไม่เข้าจะแยกไม่ออกว่าเพราะยืนนอกกรอบ แสงไม่พอ
    หรือโมเดลอ่านเป็นท่าอื่น — วาดบนภาพที่พลิกแล้ว พิกัดจึงตรงกับที่โมเดลเห็น

    `scale` ย่อเฉพาะ "หน้าต่างที่แสดง" เท่านั้น ภาพที่ป้อนเข้า MediaPipe
    ยังเป็นขนาดเต็มเสมอ เพราะต้องตรงกับ 640x480 ตอนเก็บข้อมูลเทรน
    ย่อก่อนวาด (ไม่ใช่วาดแล้วค่อยย่อ) ตัวอักษรจะได้ไม่ถูกบีบจนอ่านไม่ออก
    """
    if scale != 1.0:
        frame = cv2.resize(frame, None, fx=scale, fy=scale, interpolation=cv2.INTER_AREA)
    h, w = frame.shape[:2]
    k = h / 480.0                      # ตัวคูณขนาดตัวอักษร อิงความสูงจริงของภาพ

    mp_drawing = mp.solutions.drawing_utils
    mp_drawing.draw_landmarks(frame, results.pose_landmarks, mp.solutions.pose.POSE_CONNECTIONS,
                              mp_drawing.DrawingSpec(color=(80, 22, 10),
                                                     thickness=max(1, int(2 * k)),
                                                     circle_radius=max(1, int(3 * k))),
                              mp_drawing.DrawingSpec(color=(80, 44, 121), thickness=max(1, int(2 * k))))

    pad = max(4, int(10 * k))
    if probs is not None:
        row = max(13, int(26 * k))
        for i, (name, p) in enumerate(zip(ACTIONS, probs)):
            y = int(70 * k) + i * row
            cv2.rectangle(frame, (pad, y), (pad + int(p * w * 0.33), y + int(row * 0.7)),
                          (90, 200, 90), -1)
            cv2.putText(frame, f'{name} {p:.2f}', (pad + 4, y + int(row * 0.56)),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.5 * k, (255, 255, 255), 1, cv2.LINE_AA)
    else:
        # ข้อความบนภาพเป็นอังกฤษล้วน — cv2.putText วาดภาษาไทยไม่ได้ จะได้สี่เหลี่ยมเปล่า
        cv2.putText(frame, 'waiting for person / buffer', (pad, int(86 * k)),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.6 * k, (60, 200, 255), max(1, int(2 * k)), cv2.LINE_AA)

    fps = frames / max(now_ms / 1000, 1e-6)
    head = f'{fps:4.1f} fps   presence {presence*100:3.0f}%'
    cv2.putText(frame, head, (pad, int(28 * k)), cv2.FONT_HERSHEY_SIMPLEX,
                0.6 * k, (255, 255, 255), max(1, int(2 * k)), cv2.LINE_AA)

    # ท่าที่เพิ่งส่งออกไป ค้างไว้ 1 วินาทีให้ทันอ่าน
    if last_sent and now_ms - last_sent[1] < 1000:
        cv2.putText(frame, last_sent[0].upper(), (pad, int(58 * k)), cv2.FONT_HERSHEY_SIMPLEX,
                    1.0 * k, (0, 230, 255), max(2, int(3 * k)), cv2.LINE_AA)

    cv2.imshow('pose preview - press q to quit', frame)
    return (cv2.waitKey(1) & 0xFF) != ord('q')


# ── ลูปหลัก ────────────────────────────────────────────────────────────────────

def extract_raw(results):
    if results.pose_landmarks:
        return np.array([[lm.x, lm.y, lm.z, lm.visibility]
                         for lm in results.pose_landmarks.landmark],
                        dtype=np.float32).flatten()
    return np.zeros(N_RAW, dtype=np.float32)


def run(args):
    model = Classifier()

    gate = PoseGate(threshold=args.threshold, consensus=args.consensus,
                    release_frames=args.release_frames, min_presence=args.min_presence,
                    repeat_same_after_ms=args.repeat_same_ms)
    sender = None if args.no_ws else Sender(args.ws)

    src = args.video if args.video else args.camera
    cap = cv2.VideoCapture(src)
    if not cap.isOpened():
        print(f'เปิดแหล่งภาพไม่ได้: {src}')
        return 1
    if args.video is None:
        # ขอ 640x480 ให้ตรงกับตอนเก็บข้อมูลเทรน และเบากว่า 720p ด้วย
        cap.set(cv2.CAP_PROP_FRAME_WIDTH, 640)
        cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 480)

    # aspect ต้องมาจากขนาดภาพจริง ไม่ใช่ 4:3 ตายตัว — MediaPipe หาร x ด้วยความกว้าง
    # และ y ด้วยความสูงแยกกัน ภาพ 16:9 ที่ใช้ค่า 4:3 จะทำให้ทุกมุมใน feature เบี้ยว
    # แล้วโมเดลตอบผิดอย่างมั่นใจ (วัดแล้ว: คลิป jab/cross กลายเป็น hook conf 0.95+)
    # web/pose-detector.js ทำแบบเดียวกันที่ featureCfg.aspect = w / h
    w = cap.get(cv2.CAP_PROP_FRAME_WIDTH)
    h = cap.get(cv2.CAP_PROP_FRAME_HEIGHT)
    aspect = (w / h) if h else 4 / 3
    print(f'[video] {int(w)}x{int(h)} aspect={aspect:.3f}', flush=True)
    if abs(aspect - 4 / 3) > 0.05:
        print(f'[video] เตือน: ข้อมูลเทรนเก็บที่ 640x480 (4:3) มุมกล้องที่ต่างกัน '
              f'ยังทำให้ความแม่นตกได้แม้ชดเชย aspect แล้ว', flush=True)

    raw_buf = deque(maxlen=SEQUENCE_LENGTH)
    presence_buf = deque(maxlen=SEQUENCE_LENGTH)
    t0 = time.monotonic()
    frames = 0
    last_sent = None

    with mp.solutions.pose.Pose(**POSE_KW) as pose:
        while True:
            ok, frame = cap.read()
            if not ok:
                break
            frames += 1
            if args.max_frames and frames > args.max_frames:
                break
            if not args.no_flip:
                frame = cv2.flip(frame, 1)          # ต้อง flip ภาพก่อน MediaPipe (ดู docstring)

            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            results = pose.process(rgb)
            now_ms = (time.monotonic() - t0) * 1000

            raw_buf.append(extract_raw(results))
            presence_buf.append(1 if results.pose_landmarks else 0)
            if len(raw_buf) < SEQUENCE_LENGTH:
                if args.preview and not draw_preview(frame, results, None, 0.0, frames, now_ms, scale=args.preview_scale):
                    break
                continue

            # ไม่เห็นคนพอ = ไม่ทำนาย (กันโมเดลตอบ block 100% ทั้งที่ไม่มีคน)
            presence = sum(presence_buf) / len(presence_buf)
            if presence < gate.min_presence:
                gate.reset_recent()
                if args.preview and not draw_preview(frame, results, None, presence, frames, now_ms, scale=args.preview_scale):
                    break
                continue

            feats = build_features(np.stack(raw_buf), aspect=aspect)
            probs = model(feats)

            hit = gate.feed(probs, now_ms)
            if hit is not None:
                pose_name, conf = hit
                msg = gate.payload(pose_name, conf, now_ms)
                print(f"-> {pose_name:<9} conf={conf:.3f}  seq={msg['seq']}  t={now_ms/1000:.1f}s", flush=True)
                if sender is not None:
                    sender.send(msg)
                last_sent = (pose_name, now_ms)

            if args.preview:
                if not draw_preview(frame, results, probs, presence, frames, now_ms, last_sent,
                                    scale=args.preview_scale):
                    break

    cap.release()
    if args.preview:
        cv2.destroyAllWindows()
    elapsed = time.monotonic() - t0
    print(f'[done] {frames} เฟรม / {elapsed:.1f}s = {frames/max(elapsed,1e-6):.1f} fps  '
          f'ส่งออก {gate.emit_seq} ท่า', flush=True)
    return 0


def main():
    # คอนโซล Windows ใช้ cp874/cp1252 ข้อความไทยจะเพี้ยนถ้าไม่บังคับ
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except Exception:
        pass

    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument('--camera', type=int, default=0)
    p.add_argument('--video', help='ไฟล์วิดีโอแทนกล้อง (ใช้ทดสอบ offline)')
    p.add_argument('--ws', default='ws://127.0.0.1:8787')
    p.add_argument('--no-ws', action='store_true', help='พิมพ์ผลอย่างเดียว ไม่ต่อ Unity')
    p.add_argument('--threshold', type=float, default=0.7)
    p.add_argument('--consensus', type=int, default=3)
    p.add_argument('--release-frames', type=int, default=3)
    p.add_argument('--repeat-same-ms', type=int, default=500,
                   help='ส่งท่าเดิมซ้ำได้หลังกี่ ms แม้ยังไม่หลุดจากท่า (0 = ปิด) '
                        'ต้องตรงกับ repeatSameAfterMs ในหน้าเว็บ')
    p.add_argument('--min-presence', type=float, default=0.8)
    p.add_argument('--preview', action='store_true',
                   help='เปิดหน้าต่างกล้องพร้อมโครงกระดูกและแถบความมั่นใจ (กด q เพื่อออก)')
    p.add_argument('--preview-scale', type=float, default=0.5,
                   help='ย่อหน้าต่างพรีวิว (0.5 = ครึ่งเดียว, 1.0 = เท่าภาพจริง) '
                        'ไม่กระทบภาพที่ป้อนเข้าโมเดล')
    p.add_argument('--max-frames', type=int, default=0, help='หยุดหลังกี่เฟรม (0 = ไม่จำกัด)')
    p.add_argument('--no-flip', action='store_true',
                   help='ไม่พลิกภาพ — ใช้กับคลิปที่บันทึกมาแบบพลิกแล้ว (กล้องสดต้องพลิกเสมอ)')
    return run(p.parse_args())


if __name__ == '__main__':
    sys.exit(main())
