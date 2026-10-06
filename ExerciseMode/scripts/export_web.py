"""
export_web.py — ย่อโมเดล Siamese ให้เป็น JSON ที่หน้าเว็บคำนวณเองได้

    python scripts/export_web.py
    python scripts/export_web.py --out ../Muayverse_Remake/tfjs_build/jab_siamese

ทำไมไม่แปลงเป็น TF.js: base network มีแค่ 99->32->16 (3,728 พารามิเตอร์)
สองเมทริกซ์คูณกันเขียนด้วย JS ธรรมดาได้เลย ไม่ต้องลาก TF.js เข้ามาในหน้านี้
และ embedding ของท่าต้นแบบ 119 เฟรมคำนวณล่วงหน้าไว้ได้ เพราะมันไม่เคยเปลี่ยน
-> ตอนรันเหลือแค่ embed ท่าผู้เล่น 1 ครั้ง แล้วหาระยะที่ใกล้ที่สุดใน 119 จุด

ค่าเกณฑ์ทั้งหมดที่เขียนลง bundle วัดจากคลิปต้นแบบ (jab_ref_video.npy) ไม่ใช่ตั้งลอย ๆ
สคริปต์นี้พิมพ์ตัวเลขที่วัดได้ออกมาทุกครั้งเพื่อให้ตรวจซ้ำได้
"""
import argparse
import json
import pathlib
import datetime

import sys

import numpy as np
import keras

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))
from train_model import (DECISION_THRESHOLD, INPUT_DIM, contrastive_loss,
                         l2_rows, siamese_accuracy, L2Distance)

HERE = pathlib.Path(__file__).resolve().parent.parent

# คู่ index ที่ต้องสลับเมื่อพลิกภาพซ้าย-ขวา (MediaPipe Pose 33 จุด)
# จุด 0 (จมูก) ไม่มีคู่ สลับแล้ว x ต้องกลับเครื่องหมายด้วย
MIRROR_PAIRS = [(1, 4), (2, 5), (3, 6), (7, 8), (9, 10), (11, 12), (13, 14),
                (15, 16), (17, 18), (19, 20), (21, 22), (23, 24), (25, 26),
                (27, 28), (29, 30), (31, 32)]

# แขนที่ "ชก" ในคลิปต้นแบบคือฝั่ง index 12/14/16 (วัดได้ว่ายืดถึง 173-177 องศา
# ส่วนฝั่ง 11/13/15 อยู่ที่ 2-39 องศา คือตั้งการ์ดไว้) แต่หน้าเว็บไม่ยึดฝั่ง —
# มันดูทั้งสองแขนแล้วเลือกแขนที่ยืดกว่า จึงใช้ได้ทั้งคนถนัดซ้ายและขวา
ARMS = {'left': [11, 13, 15], 'right': [12, 14, 16]}


def mirror_flat(flat):
    """พลิกซ้าย-ขวาบนเวกเตอร์ 99 ค่าที่ยึดกลางสะโพกแล้ว"""
    k = np.array(flat, dtype=np.float64).reshape(-1, 3).copy()
    k[:, 0] *= -1
    for a, b in MIRROR_PAIRS:
        k[[a, b]] = k[[b, a]]
    out = k.flatten()
    return out / (np.linalg.norm(out) + 1e-6)


def normalize_keypoints(raw):
    """ยึดกลางสะโพกเป็นจุดศูนย์ แล้วหารด้วยความยาวเวกเตอร์ทั้งก้อน

    ตรงกับ normalize_keypoints() ใน main.py — ฝั่ง JS ต้องให้ผลเท่านี้เป๊ะ
    ไม่มีการแก้ aspect ที่นี่ เพราะท่าต้นแบบก็เก็บมาแบบไม่แก้
    """
    k = np.array(raw, dtype=np.float64).reshape(-1, 3)
    k = k - (k[23] + k[24]) / 2
    flat = k.flatten()
    return flat / (np.linalg.norm(flat) + 1e-6), k


def elbow_angle(k, idx):
    """องศาที่ข้อศอก ระหว่างเวกเตอร์ไปไหล่กับไปข้อมือ (ใช้แค่ x,y)"""
    s, e, w = idx
    a = k[s][:2] - k[e][:2]
    b = k[w][:2] - k[e][:2]
    cos = np.dot(a, b) / (np.linalg.norm(a) * np.linalg.norm(b) + 1e-6)
    return float(np.degrees(np.arccos(np.clip(cos, -1.0, 1.0))))


def load_base():
    """ดึง base network (99->16) ออกมาจากโมเดล Siamese

    siamese_base.keras ถูกเขียนไว้ตอนเทรน แต่เผื่อมีแต่ตัว Siamese ก็ขุดจากข้างใน
    โมเดลสองขาใช้ base ตัวเดียวกันแชร์น้ำหนัก หยิบขาไหนมาก็ได้ค่าเท่ากัน
    """
    direct = HERE / 'siamese_base.keras'
    if direct.exists():
        return keras.models.load_model(direct)
    m = keras.models.load_model(HERE / 'siamese_model.keras', custom_objects={
        'contrastive_loss': contrastive_loss,
        'siamese_accuracy': siamese_accuracy,
        'L2Distance': L2Distance})
    for layer in m.layers:
        if isinstance(layer, keras.Model):
            return layer
    raise SystemExit('หา base network ใน siamese_model.keras ไม่เจอ — เทรนใหม่ก่อน')


def dense_weights(base):
    ws = [l.get_weights() for l in base.layers if l.get_weights()]
    if [w[0].shape for w in ws] != [(INPUT_DIM, 32), (32, 16)]:
        raise SystemExit(f'โครงสร้าง base ไม่ใช่ 99->32->16: {[w[0].shape for w in ws]}')
    return ws


def embed(flat, ws):
    x = np.asarray(flat, dtype=np.float64)
    for W, b in ws:
        x = np.maximum(x @ W + b, 0.0)
    return x


def cosine_max(x, refs_norm):
    """cosine สูงสุดเทียบกับท่าต้นแบบ — ใช้เป็นด่านกรองว่า "เป็นท่าคนจริงหรือไม่"

    จำเป็นเพราะ embedding ของโมเดลเสื่อมนอกช่วงข้อมูลที่เทรน: ป้อน noise สุ่ม
    หรือเวกเตอร์ศูนย์เข้าไป มันให้ embedding ที่ ReLU ตัดเกือบหมด (เหลือ 1-2 มิติ)
    แล้วไปอยู่ใกล้ embedding ของเฟรมต้นแบบที่เสื่อมแบบเดียวกัน → ระยะ 0.004-0.010
    ซึ่งแปลว่า "เหมือนท่าครูมวย 99%" ทั้งที่ไม่มีคนอยู่ในเฟรม
    (อาการเดียวกับที่ CLAUDE.md เตือนเรื่อง minPresenceRatio ของโมเดล LSTM)

    cosine บนพิกัดดิบแยกสองกรณีนี้ขาด — วัดได้: ท่าคนจริง 0.88-1.00
    noise สุ่ม 0.03  เวกเตอร์ศูนย์ 0.00
    """
    n = np.linalg.norm(x) + 1e-6
    return float((refs_norm @ (np.asarray(x, dtype=np.float64) / n)).max())


def probe_reference(refs):
    """วัดคลิปต้นแบบเพื่อตั้งเกณฑ์ แทนการเดาค่า"""
    k = refs.reshape(-1, 33, 3)
    out = {}
    for name, idx in ARMS.items():
        ang = np.array([elbow_angle(f, idx) for f in k])
        step = np.linalg.norm(np.diff(k[:, idx[2], :2], axis=0), axis=1)
        out[name] = {
            'angle_min': round(float(ang.min()), 1),
            'angle_med': round(float(np.median(ang)), 1),
            'angle_max': round(float(ang.max()), 1),
            'wrist_step_med': round(float(np.median(step)), 4),
            'wrist_step_max': round(float(step.max()), 4),
        }
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', default='../Muayverse_Remake/tfjs_build/jab_siamese')
    args = ap.parse_args()
    out = (HERE / args.out).resolve()
    out.mkdir(parents=True, exist_ok=True)

    base = load_base()
    ws = dense_weights(base)
    refs = np.load(HERE / 'jab_ref_video.npy').astype(np.float64)
    if refs.shape[1] != INPUT_DIM:
        raise SystemExit(f'jab_ref_video.npy ต้องเป็น (N, 99) ได้ {refs.shape}')

    # ท่าต้นแบบถูก normalise มาแล้ว (วัดได้ว่าทุกแถวยาว ~1.0) แต่ทำซ้ำให้ชัดเจน
    # เผื่อวันหลังมีคนเอาไฟล์ต้นแบบชุดใหม่ที่ยังไม่ normalise มาวาง
    refs = l2_rows(refs)
    ref_emb = np.stack([embed(r, ws) for r in refs])

    # ── เลือกเฉพาะเฟรมที่หมัดยืดสุด เป็นชุดที่ใช้ให้คะแนน ────────────────
    #
    # ใช้ทั้งคลิป 119 เฟรมไม่ได้ เพราะคลิปครอบทั้งวงจร การ์ด -> ออกหมัด -> กลับการ์ด
    # แล้วการให้คะแนนแบบ "ใกล้เฟรมไหนก็ได้ในคลิป" กลายเป็นว่าท่าอะไรก็ผ่าน
    # วัดได้: เฟรมตั้งการ์ดของคลิปต้นแบบเองยังได้ระยะ 0.001 เมื่อเทียบกับทั้งคลิป
    # (= เหมือนท่าต้นแบบ 100%) ซึ่งไม่ควร เพราะเรากำลังให้คะแนน "หมัดที่ยืดสุด"
    #
    # จำกัดเหลือเฟรมที่ยืด >= extend_min_deg แล้วเฟรมตั้งการ์ดได้ 1.27 ตามที่ควรเป็น
    EXTEND_MIN_DEG = 150
    kp = refs.reshape(-1, 33, 3)
    punch_arm = max(ARMS, key=lambda n: max(elbow_angle(f, ARMS[n]) for f in kp))
    angles = np.array([elbow_angle(f, ARMS[punch_arm]) for f in kp])
    ext = angles >= EXTEND_MIN_DEG
    if ext.sum() < 3:
        raise SystemExit(f'คลิปต้นแบบมีเฟรมที่ยืด >= {EXTEND_MIN_DEG} องศาแค่ {ext.sum()} '
                         'เฟรม — ตรวจว่า jab_ref_video.npy เป็นคลิปหมัดจริงไหม')
    ref_ext = refs[ext]
    ref_emb = ref_emb[ext]
    ref_norm = ref_ext / (np.linalg.norm(ref_ext, axis=1, keepdims=True) + 1e-6)
    print(f'แขนที่ชกในคลิปต้นแบบ: {punch_arm} (index {ARMS[punch_arm]})')
    print(f'เฟรมที่ยืดสุด >= {EXTEND_MIN_DEG} องศา: {int(ext.sum())}/{len(refs)} เฟรม '
          f'(ที่เหลือเป็นจังหวะตั้งการ์ดกับดึงมือกลับ)')

    probe = probe_reference(refs)
    print('วัดจากคลิปต้นแบบ:')
    for name, v in probe.items():
        print(f'  arm {name:5s} elbow {v["angle_min"]:.0f}-{v["angle_max"]:.0f} deg '
              f'(median {v["angle_med"]:.0f})  wrist step/frame median '
              f'{v["wrist_step_med"]:.4f} max {v["wrist_step_max"]:.4f}')

    bundle = {
        'created': datetime.date.today().isoformat(),
        'input_dim': INPUT_DIM,
        'n_refs': int(len(ref_ext)),
        'n_ref_frames_total': int(len(refs)),
        'punch_arm': punch_arm,
        'decision_threshold': DECISION_THRESHOLD,
        'mirror_pairs': MIRROR_PAIRS,
        'arms': ARMS,
        'reference_probe': probe,
        'layers': [{'w': W.T.flatten().round(7).tolist(),      # เก็บเป็น [out][in]
                    'b': b.round(7).tolist(),
                    'shape': [int(W.shape[1]), int(W.shape[0])]} for W, b in ws],
        'ref_embeddings': ref_emb.round(7).flatten().tolist(),
        'ref_embedding_shape': list(ref_emb.shape),
        # พิกัดต้นแบบที่ normalise แล้ว ใช้สำหรับด่าน cosine (ดู cosine_max)
        'ref_unit': ref_norm.round(7).flatten().tolist(),
        'ref_unit_shape': list(ref_norm.shape),
        'cosine_min': 0.5,
        # -- เกณฑ์ตัดสิน -----------------------------------------------------
        # percent = clip((dist_zero - minDist) / dist_zero, 0, 1) * 100
        #
        # main.py ใช้ percent = clip((1 - minDist - 0.7) / 0.3, 0, 1) * 100 * 2
        #   ปัญหาสอง: คูณ 2 ทำให้ทะลุ 100 ไปถึง 200  และสเกล 0.7-1.0 ตั้งมาจาก
        #   ระยะตอนเทรน (ท่าเหมือน 0.10 / ท่าต่าง 1.00) แต่พอหา "ระยะที่ใกล้ที่สุด"
        #   ในหลายเฟรมต้นแบบ ระยะจริงมาอยู่ที่ 0.001-0.05 ทุกอย่างจึงเต็ม 100
        #
        # 0.30 มาจากของจริง: หมัดที่ยืดสุดในชุด X1 ได้ระยะ median 0.013 p90 0.050
        #   จังหวะกลางทาง 0.024  ท่าตั้งการ์ด 0.43 ขึ้นไป
        #   => เกิน 0.30 ถือว่าไม่ใช่หมัดที่ยืดแล้ว ให้ 0%
        'dist_zero_percent': 0.30,
        # องศา: ตอนยืดสุดในคลิปต้นแบบคือ 173-177 ตอนตั้งการ์ด 2-39
        #   main.py ใช้ช่วง 30-50 ซึ่งเป็นแค่จังหวะกลางทางของการเหวี่ยง
        #   ไม่ใช่จังหวะหมัดยืด จึงแทบไม่เคยเข้าเงื่อนไข
        'extend_min_deg': EXTEND_MIN_DEG,
        'guard_max_deg': 70,
        # ความเร็วข้อมือหน่วยเป็น "ต่อวินาที" ไม่ใช่ต่อเฟรม — main.py เทียบ
        #   ผลต่างต่อเฟรมกับค่าคงที่ ซึ่งผูกกับ fps ของเครื่อง เครื่องเร็วกว่า
        #   ได้ค่าน้อยกว่าทั้งที่ชกแรงเท่ากัน  ค่านี้ = ก้าว/เฟรมของคลิปต้นแบบ x 30fps
        'wrist_speed_min': 0.35,
        'rep_cooldown_ms': 600,
        'good_percent': 60,
        'perfect_percent': 80,
    }
    (out / 'bundle.json').write_text(json.dumps(bundle), encoding='utf-8')

    # -- fixtures สำหรับเทียบ JS กับ Python ---------------------------------
    # ป้อน keypoint ที่ยัง "ไม่" normalise เข้าไป เพื่อให้เทสต์ครอบ normalise ด้วย
    # ไม่ใช่ครอบแค่ตัวคูณเมทริกซ์
    rng = np.random.default_rng(7)
    raws = []
    # ชื่อ fixture บอกชนิดของเฟรม เพราะเทสต์ฝั่ง JS เช็กต่างกัน:
    #   ext*   = เฟรมที่หมัดยืดสุด ต้องแมตช์ตัวเองได้ (ระยะ = 1e-3 ซึ่งคือพื้น epsilon)
    #   guard* = เฟรมตั้งการ์ดของคลิปเดียวกัน ต้อง "ไม่" แมตช์ (ระยะ > 1)
    hit = np.flatnonzero(ext)
    for i in [hit[0], hit[len(hit) // 2], hit[-1]]:
        k = refs[i].reshape(33, 3)
        # ขยายขนาดตัวและเลื่อนตำแหน่ง — normalise ต้องหักล้างทั้งสองอย่างออกได้
        raws.append((f'ext{i}', k * 2.7 + np.array([0.31, 0.44, 0.02])))
    for i in np.flatnonzero(angles < 60)[:2]:
        k = refs[i].reshape(33, 3)
        raws.append((f'guard{i}', k * 2.7 + np.array([0.31, 0.44, 0.02])))
    X1 = np.load(HERE / 'X1.npy').astype(np.float64)
    for i in [0, 800, 1500]:
        raws.append((f'x1_{i}', X1[i].reshape(33, 3) * 1.4 + 0.05))
    noise = rng.normal(size=(33, 3)) * 0.3 + 0.5
    raws.append(('random', noise))
    raws.append(('zeros', np.zeros((33, 3))))

    cases = []
    for name, k in raws:
        flat, kps = normalize_keypoints(k)
        e = embed(flat, ws)
        em = embed(mirror_flat(flat), ws)
        d = float(np.sqrt(((ref_emb - e) ** 2).sum(1) + 1e-6).min())
        dm = float(np.sqrt(((ref_emb - em) ** 2).sum(1) + 1e-6).min())
        best = min(d, dm)
        cos = max(cosine_max(flat, ref_norm), cosine_max(mirror_flat(flat), ref_norm))
        dz = bundle['dist_zero_percent']
        pct = 0.0 if cos < bundle['cosine_min'] else             float(np.clip((dz - best) / dz, 0, 1) * 100)
        cases.append({
            'name': name,
            'raw': k.flatten().round(9).tolist(),
            'flat': flat.round(9).tolist(),
            'embedding': e.round(7).tolist(),
            'min_dist': round(d, 7),
            'min_dist_mirrored': round(dm, 7),
            'cosine': round(cos, 7),
            'plausible': bool(cos >= bundle['cosine_min']),
            'percent': round(pct, 5),
            'elbow': {n: round(elbow_angle(kps, idx), 6) for n, idx in ARMS.items()},
        })
    (out / 'fixtures.json').write_text(json.dumps({'cases': cases}), encoding='utf-8')

    for f in ['bundle.json', 'fixtures.json']:
        print(f'  {f:16s} {(out / f).stat().st_size / 1024:6.0f} KB')
    print(f'wrote {out}')


if __name__ == '__main__':
    main()
