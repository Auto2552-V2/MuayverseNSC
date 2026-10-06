/* ---------------------------------------------------------------------------------------------
 * exercise-scorer.js — ให้คะแนนท่า jab เทียบกับท่าต้นแบบของครูมวย (โหมดฝึกซ้อม)
 *
 * พอร์ตมาจาก ExerciseMode/main.py ซึ่งเดิมเป็น FastAPI WebSocket server
 * ที่นี่ไม่มีเซิร์ฟเวอร์ — คำนวณบนเครื่องผู้เล่นทั้งหมด เหมือนฝั่ง pose-detector.js
 *
 * ต้นฉบับที่ต้องให้ผลตรงกัน:
 *   ExerciseMode/main.py             normalize_keypoints() / get_elbow_angle()
 *   ExerciseMode/scripts/export_web.py  embed() / mirror_flat() / การตั้งเกณฑ์
 * พิสูจน์ด้วย  node scripts/compare_exercise.mjs  (รวมอยู่ใน npm test)
 *
 * ทำไมไม่ใช้ TF.js: base network คือ 99 -> Dense32 relu -> Dense16 relu เท่านั้น
 * (3,728 พารามิเตอร์) และ embedding ของท่าต้นแบบ 119 เฟรมถูกคำนวณไว้ล่วงหน้าแล้ว
 * ตอนรันจึงเหลือแค่คูณเมทริกซ์สองครั้งกับหาระยะที่ใกล้สุดใน 119 จุด
 *
 * ใช้ Float64 ทั้งหมด (number ปกติของ JS) ไม่ใช้ Float32Array — เพื่อให้เทียบกับ
 * numpy ได้ในระดับ 1e-6 ถ้าเปลี่ยนไปใช้ float32 เทสต์ parity จะคลาดทันที
 * ------------------------------------------------------------------------------------------- */

export const N_LANDMARKS = 33;
export const INPUT_DIM = 99;

/** ค่าเริ่มต้น เผื่อ bundle.json โหลดไม่ได้ — ต้องตรงกับที่ export_web.py เขียน */
export const DEFAULTS = {
  distZeroPercent: 0.3,
  cosineMin: 0.5,
  extendMinDeg: 150,
  guardMaxDeg: 70,
  wristSpeedMin: 0.35,
  repCooldownMs: 600,
  goodPercent: 60,
  perfectPercent: 80,
  arms: { left: [11, 13, 15], right: [12, 14, 16] },
  mirrorPairs: [[1, 4], [2, 5], [3, 6], [7, 8], [9, 10], [11, 12], [13, 14],
                [15, 16], [17, 18], [19, 20], [21, 22], [23, 24], [25, 26],
                [27, 28], [29, 30], [31, 32]],
};

/**
 * ยึดกลางสะโพกเป็นจุด (0,0,0) แล้วหารด้วยความยาวของเวกเตอร์ทั้ง 99 ค่า
 *
 * ตรงกับ normalize_keypoints() ใน main.py — การหารด้วย norm ของทั้งก้อน (ไม่ใช่
 * หารด้วยความยาวลำตัวแบบ pose-features.js) เป็นวิธีที่ท่าต้นแบบถูกเก็บมา
 * ถ้าเปลี่ยนวิธี normalise ต้องเก็บท่าต้นแบบใหม่ทั้งชุด
 *
 * ไม่มีการแก้ aspect ที่นี่ เพราะชุดต้นแบบก็ไม่ได้แก้ — แลกมาด้วยว่ากล้องที่สัดส่วน
 * ต่างจากตอนเก็บต้นแบบจะทำให้แกน x ยืด/ย่อเล็กน้อย ดู README หัวข้อข้อจำกัด
 *
 * @param {number[]|Float64Array} raw  99 ค่า (33 จุด x [x, y, z])
 * @returns {{flat: Float64Array, centered: Float64Array}}
 *   flat = หน่วยความยาว 1 (ตัวที่ป้อนเข้าโมเดล), centered = ยึดสะโพกแต่ยังไม่ย่อ
 */
export function normalizeKeypoints(raw) {
  const centered = new Float64Array(INPUT_DIM);
  const hipX = (raw[23 * 3] + raw[24 * 3]) / 2;
  const hipY = (raw[23 * 3 + 1] + raw[24 * 3 + 1]) / 2;
  const hipZ = (raw[23 * 3 + 2] + raw[24 * 3 + 2]) / 2;
  for (let i = 0; i < N_LANDMARKS; i++) {
    centered[i * 3] = raw[i * 3] - hipX;
    centered[i * 3 + 1] = raw[i * 3 + 1] - hipY;
    centered[i * 3 + 2] = raw[i * 3 + 2] - hipZ;
  }
  let sum = 0;
  for (let i = 0; i < INPUT_DIM; i++) sum += centered[i] * centered[i];
  const norm = Math.sqrt(sum) + 1e-6;       // +1e-6 ก่อนหาร ไม่ใช่หลัง — ตรงกับ numpy
  const flat = new Float64Array(INPUT_DIM);
  for (let i = 0; i < INPUT_DIM; i++) flat[i] = centered[i] / norm;
  return { flat, centered };
}

/**
 * พลิกซ้าย-ขวาบนเวกเตอร์ที่ยึดสะโพกแล้ว — กลับเครื่องหมาย x "และ" สลับ index
 *
 * ต้องทำสองอย่างคู่กัน เพราะ MediaPipe ตั้งชื่อ landmark ตามกายวิภาคที่เห็น
 * พอภาพพลิก ช่อง "ไหล่ซ้าย" จะบรรจุไหล่ขวาของคนจริง (เหตุผลเดียวกับที่เขียนไว้
 * ยาว ๆ ใน pose-detector.js) พลิกแค่ x เฉย ๆ ให้ท่าที่ผิดข้าง
 *
 * ใช้เพื่อ "ไม่ต้องเดา" ว่าชุดต้นแบบเก็บมาแบบพลิกภาพหรือไม่ — ตัวให้คะแนนลองทั้ง
 * สองทิศแล้วเอาทิศที่คล้ายกว่า ดู JabScorer.score()
 */
export function mirrorFlat(flat, pairs = DEFAULTS.mirrorPairs) {
  const k = new Float64Array(INPUT_DIM);
  for (let i = 0; i < INPUT_DIM; i++) k[i] = flat[i];
  for (let i = 0; i < N_LANDMARKS; i++) k[i * 3] = -k[i * 3];
  for (const [a, b] of pairs) {
    for (let c = 0; c < 3; c++) {
      const t = k[a * 3 + c];
      k[a * 3 + c] = k[b * 3 + c];
      k[b * 3 + c] = t;
    }
  }
  let sum = 0;
  for (let i = 0; i < INPUT_DIM; i++) sum += k[i] * k[i];
  const norm = Math.sqrt(sum) + 1e-6;
  for (let i = 0; i < INPUT_DIM; i++) k[i] /= norm;
  return k;
}

/**
 * องศาที่ข้อศอก ระหว่างเวกเตอร์ชี้ไปไหล่กับชี้ไปข้อมือ (คิดบนระนาบ x,y)
 *
 * ยืดสุด ~180 องศา งอเป็นการ์ด ~20-40 องศา (วัดจากคลิปต้นแบบ)
 * เป็นค่าที่ไม่ขึ้นกับขนาดตัวและตำแหน่งในเฟรม จึงใช้กับ centered หรือ flat ก็ได้เท่ากัน
 */
export function elbowAngle(k, [s, e, w]) {
  const ax = k[s * 3] - k[e * 3], ay = k[s * 3 + 1] - k[e * 3 + 1];
  const bx = k[w * 3] - k[e * 3], by = k[w * 3 + 1] - k[e * 3 + 1];
  const la = Math.hypot(ax, ay), lb = Math.hypot(bx, by);
  const cos = (ax * bx + ay * by) / (la * lb + 1e-6);
  return Math.acos(Math.min(1, Math.max(-1, cos))) * 180 / Math.PI;
}

/** แปลง landmark จาก MediaPipe tasks-vision ให้เป็น 99 ค่า (ทิ้ง visibility) */
export function landmarksToRaw(landmarks) {
  const out = new Float64Array(INPUT_DIM);
  if (!landmarks || landmarks.length < N_LANDMARKS) return out;
  for (let i = 0; i < N_LANDMARKS; i++) {
    out[i * 3] = landmarks[i].x;
    out[i * 3 + 1] = landmarks[i].y;
    out[i * 3 + 2] = landmarks[i].z;
  }
  return out;
}

// ─────────────────────────────────────────────────────────────────────────────

export class JabScorer {
  constructor(bundle) {
    this.bundle = bundle;
    this.cfg = {
      distZeroPercent: bundle.dist_zero_percent ?? DEFAULTS.distZeroPercent,
      cosineMin: bundle.cosine_min ?? DEFAULTS.cosineMin,
      extendMinDeg: bundle.extend_min_deg ?? DEFAULTS.extendMinDeg,
      guardMaxDeg: bundle.guard_max_deg ?? DEFAULTS.guardMaxDeg,
      wristSpeedMin: bundle.wrist_speed_min ?? DEFAULTS.wristSpeedMin,
      repCooldownMs: bundle.rep_cooldown_ms ?? DEFAULTS.repCooldownMs,
      goodPercent: bundle.good_percent ?? DEFAULTS.goodPercent,
      perfectPercent: bundle.perfect_percent ?? DEFAULTS.perfectPercent,
    };
    this.arms = bundle.arms ?? DEFAULTS.arms;
    this.mirrorPairs = (bundle.mirror_pairs ?? DEFAULTS.mirrorPairs);

    this.layers = (bundle.layers ?? []).map((l) => ({
      w: Float64Array.from(l.w),       // [out][in] เรียงต่อกัน
      b: Float64Array.from(l.b),
      out: l.shape[0],
      in: l.shape[1],
    }));
    if (!this.layers.length) throw new Error('bundle.json ไม่มี layers — รัน export_web.py ก่อน');

    const [n, d] = bundle.ref_embedding_shape ?? [0, 0];
    if (!n || d !== this.layers.at(-1).out) {
      throw new Error(`ref_embedding_shape ${n}x${d} ไม่เข้ากับโมเดลที่ออก ` +
        `${this.layers.at(-1).out} มิติ — bundle มาจากการเทรนคนละรอบ`);
    }
    this.nRefs = n;
    this.embDim = d;
    this.refEmb = Float64Array.from(bundle.ref_embeddings);

    // พิกัดต้นแบบที่ normalise แล้ว — ใช้สำหรับด่าน cosine (ดู cosineMax)
    const [un, ud] = bundle.ref_unit_shape ?? [0, 0];
    if (un !== n || ud !== INPUT_DIM) {
      throw new Error(`ref_unit_shape ${un}x${ud} ไม่ตรงกับ ${n}x${INPUT_DIM} — ` +
        'bundle ไม่ครบ รัน export_web.py ใหม่');
    }
    this.refUnit = Float64Array.from(bundle.ref_unit);

    this.reset();
  }

  /** โหลด bundle.json ที่ export_web.py สร้างไว้ */
  static async load(url = '../tfjs_build/jab_siamese/bundle.json') {
    const href = new URL(url, import.meta.url).href;   // เทียบกับที่อยู่ไฟล์นี้ ไม่ใช่ URL หน้า
    const res = await fetch(href);
    if (!res.ok) {
      throw new Error(`โหลด ${href} ไม่ได้ (${res.status}) — รัน ` +
        'ExerciseMode/scripts/export_web.py ก่อน');
    }
    return new JabScorer(await res.json());
  }

  reset() {
    /** สถานะการนับหมัดแยกตามแขน — คนถนัดซ้ายกับขวาใช้หน้าเดียวกันได้ */
    this.armState = {};
    for (const name of Object.keys(this.arms)) {
      this.armState[name] = {
        phase: 'guard',      // guard = งอแขนอยู่ | extending = กำลังยืดออกไป
        peakAngle: 0,
        peakSpeed: 0,
        bestPercent: 0,
        wrist: null,         // พิกัดข้อมือเฟรมก่อน ใช้หาความเร็ว
      };
    }
    this.lastT = null;
    this.lastRepT = -Infinity;
    this.reps = 0;
  }

  /** relu(relu(x·W1 + b1)·W2 + b2) */
  embed(flat) {
    let x = flat;
    for (const L of this.layers) {
      const y = new Float64Array(L.out);
      for (let o = 0; o < L.out; o++) {
        let acc = L.b[o];
        const base = o * L.in;
        for (let i = 0; i < L.in; i++) acc += L.w[base + i] * x[i];
        y[o] = acc > 0 ? acc : 0;
      }
      x = y;
    }
    return x;
  }

  /**
   * cosine สูงสุดเทียบกับท่าต้นแบบ — ด่านกรองว่า "เป็นท่าคนจริงหรือไม่"
   *
   * ต้องมีด่านนี้เพราะ embedding ของโมเดลเสื่อมนอกช่วงข้อมูลที่เทรน: ป้อนเวกเตอร์
   * ศูนย์ (เฟรมที่หาคนไม่เจอ) หรือ noise สุ่มเข้าไป ReLU ตัดเกือบหมดเหลือ 1-2 มิติ
   * แล้วไปอยู่ใกล้ embedding ของเฟรมต้นแบบที่เสื่อมแบบเดียวกัน ได้ระยะ 0.004
   * = "เหมือนท่าครูมวย 99%" ทั้งที่ไม่มีคนในเฟรม
   *
   * เป็นอาการเดียวกับที่ CLAUDE.md เตือนไว้เรื่อง minPresenceRatio ของโมเดล LSTM
   * (โมเดลไม่มีคลาส "ไม่มีคน" จึงตอบมั่วด้วยความมั่นใจเต็ม)
   *
   * วัดจากของจริง: ท่าคนจริง 0.57-1.00 · noise สุ่ม 0.10 · เวกเตอร์ศูนย์ 0.00
   */
  cosineMax(flat) {
    let nrm = 0;
    for (let i = 0; i < INPUT_DIM; i++) nrm += flat[i] * flat[i];
    nrm = Math.sqrt(nrm) + 1e-6;
    let best = -1;
    for (let r = 0; r < this.nRefs; r++) {
      let dot = 0;
      const base = r * INPUT_DIM;
      for (let i = 0; i < INPUT_DIM; i++) dot += this.refUnit[base + i] * flat[i];
      dot /= nrm;
      if (dot > best) best = dot;
    }
    return best;
  }

  /**
   * ระยะที่ใกล้ที่สุดจาก embedding ของท่าต้นแบบ
   *
   * ชุดต้นแบบใน bundle มีแค่เฟรมที่หมัดยืดสุด (22 จาก 119 เฟรมของคลิป) ไม่ใช่ทั้งคลิป
   * เพราะคลิปครอบทั้งวงจรการ์ด -> ออกหมัด -> กลับการ์ด ถ้าเทียบกับทุกเฟรม การยืน
   * ตั้งการ์ดเฉย ๆ ก็ "เหมือนท่าต้นแบบ 100%" (วัดได้ 0.001) ซึ่งไม่ใช่สิ่งที่เรากำลังให้คะแนน
   */
  minDistance(emb) {
    let best = Infinity;
    let bestIdx = -1;
    for (let r = 0; r < this.nRefs; r++) {
      let sum = 0;
      const base = r * this.embDim;
      for (let i = 0; i < this.embDim; i++) {
        const d = this.refEmb[base + i] - emb[i];
        sum += d * d;
      }
      // +1e-6 ใต้รากเหมือน L2Distance ฝั่ง Keras ถ้าไม่ใส่ ค่าจะต่างที่หลักที่ 4
      const dist = Math.sqrt(sum + 1e-6);
      if (dist < best) { best = dist; bestIdx = r; }
    }
    return { dist: best, refIndex: bestIdx };
  }

  /**
   * ความคล้ายของท่าหนึ่งเฟรม
   *
   * ลองทั้งทิศที่เห็นและทิศพลิกซ้าย-ขวา แล้วเอาทิศที่คล้ายกว่า — ไม่มีเอกสารบอกว่า
   * ชุดต้นแบบ (ที่เก็บจากแอปมือถือคนละตัว) พลิกภาพมาหรือไม่ และเดาผิดทางเดียว
   * ท่าจะไม่เข้าเลยทั้งที่ชกถูก ราคาที่จ่ายคือคูณเมทริกซ์เพิ่มหนึ่งรอบต่อเฟรม
   * ซึ่งเทียบกับ MediaPipe แล้วไม่มีผล
   *
   * ผลข้างเคียงที่ต้องรู้: ชกผิดข้างจะไม่ถูกจับผิด ถ้าวันหลังอยากบังคับข้าง
   * ให้ส่ง strictSide = true แล้วใช้เฉพาะทิศที่เห็น
   */
  score(flat, strictSide = false) {
    let best = this.minDistance(this.embed(flat));
    let cos = this.cosineMax(flat);
    let mirrored = false;
    if (!strictSide) {
      const mf = mirrorFlat(flat, this.mirrorPairs);
      const m = this.minDistance(this.embed(mf));
      cos = Math.max(cos, this.cosineMax(mf));
      if (m.dist < best.dist) { best = m; mirrored = true; }
    }

    const plausible = cos >= this.cfg.cosineMin;
    // main.py: percent = clip((1 - dist - 0.7) / 0.3, 0, 1) * 100 * 2
    //   คูณ 2 ทำให้ทะลุ 100 ไปถึง 200 และสเกล 0.7-1.0 มาจากระยะตอนเทรน
    //   (ท่าเหมือน 0.10 / ท่าต่าง 1.00) แต่ระยะจริงหลังหา min อยู่ที่ 0.001-0.05
    //   ทุกอย่างจึงเต็ม 100  ที่นี่ใช้สเกลที่วัดจากของจริง (ดู dist_zero_percent)
    const dz = this.cfg.distZeroPercent;
    const percent = plausible
      ? Math.min(1, Math.max(0, (dz - best.dist) / dz)) * 100
      : 0;
    return {
      percent, minDist: best.dist, refIndex: best.refIndex, mirrored,
      cosine: cos, plausible,
    };
  }

  /**
   * ป้อนหนึ่งเฟรมเข้าไป ได้สถานะกลับมา และได้ rep เมื่อชกครบจังหวะ
   *
   * จังหวะที่นับเป็นหนึ่งหมัด: งอแขนเป็นการ์ด (< guardMaxDeg) -> ยืดออกจนสุด
   * (>= extendMinDeg) โดยข้อมือมีความเร็วถึงเกณฑ์ -> ปิด rep ตอนยืดสุด
   * แล้วต้องกลับมาเป็นการ์ดก่อนจึงนับหมัดถัดไป
   *
   * ต่างจาก main.py ที่เช็ก `30 <= elbow <= 50` ทีละเฟรมโดยไม่ดูลำดับ: ช่วงนั้น
   * เป็นจังหวะกลางทางของการเหวี่ยง ไม่ใช่จังหวะหมัดยืด (คลิปต้นแบบยืดถึง 173-177
   * และตั้งการ์ดที่ 2-39) เงื่อนไขเดิมจึงแทบไม่เคยเข้า และนับหมัดซ้ำได้เรื่อย ๆ
   * ตราบใดที่ค้างมือไว้ในช่วงองศานั้น
   *
   * @param {Array|null} landmarks  33 จุดจาก PoseLandmarker หรือ null เมื่อไม่เจอคน
   * @param {number} nowMs          performance.now()
   */
  update(landmarks, nowMs) {
    if (!landmarks || landmarks.length < N_LANDMARKS) {
      // ไม่เจอคน: ล้างสถานะแขนทิ้ง ไม่ใช่ค้างไว้ ไม่งั้นคนเดินออกจากเฟรมกลางหมัด
      // แล้วเดินกลับมา จะถูกนับเป็นหมัดที่ยืดสุดทันที
      for (const st of Object.values(this.armState)) {
        st.phase = 'guard'; st.peakAngle = 0; st.peakSpeed = 0;
        st.bestPercent = 0; st.wrist = null;
      }
      this.lastT = nowMs;
      return { present: false, feedback: 'nopose', reps: this.reps };
    }

    const raw = landmarksToRaw(landmarks);
    const { flat, centered } = normalizeKeypoints(raw);
    const scored = this.score(flat);

    // dt จริงจากนาฬิกา ไม่ใช่สมมติ fps — ความเร็วจึงเทียบกับเกณฑ์ได้ข้ามเครื่อง
    const dt = this.lastT == null ? 0 : (nowMs - this.lastT) / 1000;
    this.lastT = nowMs;

    const arms = {};
    let rep = null;
    let hint = null;

    for (const [name, idx] of Object.entries(this.arms)) {
      const st = this.armState[name];
      const angle = elbowAngle(centered, idx);

      // ความเร็วข้อมือวัดบนพิกัดที่ย่อเป็นหน่วยแล้ว (flat) ไม่ใช่ centered —
      // ไม่งั้นคนที่ยืนใกล้กล้องจะได้ความเร็วสูงกว่าโดยที่ชกเท่ากัน
      // และเกณฑ์ใน bundle ก็วัดมาจากต้นแบบที่ย่อหน่วยแล้วเหมือนกัน
      const wx = flat[idx[2] * 3], wy = flat[idx[2] * 3 + 1];
      let speed = 0;
      if (st.wrist && dt > 0 && dt < 0.5) {   // dt > 0.5s = แท็บถูกพักไว้ ข้ามไป
        speed = Math.hypot(wx - st.wrist[0], wy - st.wrist[1]) / dt;
      }
      st.wrist = [wx, wy];

      if (st.phase === 'spent') {
        // ปิด rep ไปแล้ว ต้องเห็นว่าดึงมือกลับมาเป็นการ์ดจริงก่อนจะนับหมัดถัดไป
        // (ถ้าไม่มีสถานะนี้ การค้างแขนยืดไว้จะถูกนับเป็นหมัดใหม่ทุกครั้งที่พ้น cooldown)
        if (angle <= this.cfg.guardMaxDeg) {
          st.phase = 'guard';
          st.peakAngle = 0; st.peakSpeed = 0; st.bestPercent = 0;
        }
      } else if (st.phase === 'guard') {
        if (angle > this.cfg.guardMaxDeg) {
          st.phase = 'extending';
          st.peakAngle = angle;
          st.peakSpeed = speed;
          st.bestPercent = scored.percent;
        }
      } else {
        st.peakAngle = Math.max(st.peakAngle, angle);
        st.peakSpeed = Math.max(st.peakSpeed, speed);
        st.bestPercent = Math.max(st.bestPercent, scored.percent);

        if (angle >= this.cfg.extendMinDeg) {
          // ยืดสุดแล้ว — ปิด rep ที่นี่เพื่อให้ฟีดแบ็กมาทันจังหวะที่ผู้เล่นรู้สึก
          const cool = nowMs - this.lastRepT >= this.cfg.repCooldownMs;
          if (!scored.plausible) {
            // MediaPipe เจอคน แต่ท่าไม่ใกล้อะไรในชุดต้นแบบเลย (ยืนข้าง นอน
            // หรือเห็นแค่ครึ่งตัว) ให้คะแนนไปก็ไม่มีความหมาย
            hint = hint ?? 'offpose';
          } else if (st.peakSpeed < this.cfg.wristSpeedMin) {
            hint = hint ?? 'slow';
          } else if (cool && !rep) {
            this.reps += 1;
            this.lastRepT = nowMs;
            rep = {
              index: this.reps,
              arm: name,
              percent: st.bestPercent,
              peakAngle: st.peakAngle,
              peakSpeed: st.peakSpeed,
              grade: st.bestPercent >= this.cfg.perfectPercent ? 'PERFECT'
                   : st.bestPercent >= this.cfg.goodPercent ? 'GOOD' : 'OK',
            };
          }
          st.phase = 'spent';          // รอกลับมาเป็นการ์ดก่อนนับหมัดถัดไป
        } else if (angle <= this.cfg.guardMaxDeg) {
          // ดึงมือกลับก่อนที่หมัดจะยืดสุด
          if (st.phase === 'extending' && st.peakAngle > 100) hint = hint ?? 'extend';
          st.phase = 'guard';
          st.peakAngle = 0; st.peakSpeed = 0; st.bestPercent = 0;
        }
      }

      arms[name] = { angle, speed, phase: st.phase };
    }

    // แขนที่ "นำ" = แขนที่ยืดกว่าในเฟรมนี้ ใช้แสดงผลอย่างเดียว การนับหมัดดูทุกแขน
    const lead = Object.entries(arms).sort((a, b) => b[1].angle - a[1].angle)[0][0];

    return {
      present: true,
      percent: scored.percent,
      plausible: scored.plausible,
      cosine: scored.cosine,
      minDist: scored.minDist,
      refIndex: scored.refIndex,
      mirrored: scored.mirrored,
      arms,
      lead,
      reps: this.reps,
      rep,
      feedback: rep ? rep.grade.toLowerCase() : (hint ?? null),
    };
  }
}

export default JabScorer;
