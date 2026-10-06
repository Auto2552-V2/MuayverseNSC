/* ---------------------------------------------------------------------------------------------
 * pose-features.js — JS port of scripts/pose_features.py
 *
 * ต้องให้ผลลัพธ์ "ตรงเป๊ะ" กับฝั่ง Python ไม่ใช่ใกล้เคียง เพราะโมเดลเห็นแต่ตัวเลขหลัง
 * normalise ถ้า JS คำนวณเพี้ยนไปนิดเดียว โมเดลจะไม่ error — มันจะตอบผิดอย่างมั่นใจ
 * ซึ่งเป็นอาการที่หาสาเหตุยากที่สุด
 *
 * พิสูจน์ความตรงกันด้วย:  node scripts/compare_features.mjs
 * แก้ไฟล์นี้แล้วต้องรันคำสั่งนั้นใหม่ทุกครั้ง
 *
 * ค่าคงที่ทั้งหมดต้องมาจาก meta.json ที่ train_pose_model.py เขียนไว้ ไม่ hardcode ซ้ำ
 * (ยกเว้นค่า default ที่ใส่ไว้เผื่อโหลด meta ไม่ได้ ซึ่งตรงกับ pose_features.py)
 * ------------------------------------------------------------------------------------------- */

export const DEFAULTS = {
  actions: ['jab', 'cross', 'hook', 'uppercut', 'idle', 'block'],
  sequenceLength: 36,
  nFeatures: 60,
  nRaw: 132,
  poseIdx: [11, 12, 13, 14, 15, 16, 19, 20, 23, 24],
  aspect: 640 / 480,
};

/**
 * สร้าง feature vector จาก landmark ดิบ — เทียบเท่า build_features() ฝั่ง Python
 *
 * @param {Float32Array[]|number[][]} rawSeq  T เฟรม แต่ละเฟรมคือ 132 ตัวเลข
 *                                            (33 จุด x [x, y, z, visibility])
 * @param {object} cfg  { poseIdx, aspect }
 * @returns {Float32Array[]}  T เฟรม แต่ละเฟรมคือ 60 ตัวเลข [pos(30), vel(30)]
 */
export function buildFeatures(rawSeq, cfg = DEFAULTS) {
  const T = rawSeq.length;
  const poseIdx = cfg.poseIdx ?? DEFAULTS.poseIdx;
  const aspect = cfg.aspect ?? DEFAULTS.aspect;
  const nPos = poseIdx.length * 3;

  // pos ก่อน แล้วค่อยหา vel จากผลต่างของ pos — ลำดับนี้สำคัญ ฝั่ง Python ก็ทำ
  // np.diff บน pos ที่ normalise แล้ว ไม่ใช่บน landmark ดิบ
  const pos = [];

  for (let t = 0; t < T; t++) {
    const f = rawSeq[t];
    const out = new Float32Array(nPos);

    // อ่าน x,y,z ของจุดที่ต้องใช้ ข้าม visibility (ช่องที่ 4) — index ของ
    // landmark i อยู่ที่ i*4
    const gx = (i) => f[i * 4] * aspect;
    const gy = (i) => f[i * 4 + 1];
    const gz = (i) => f[i * 4 + 2];

    const hipX = (gx(23) + gx(24)) / 2;
    const hipY = (gy(23) + gy(24)) / 2;
    const hipZ = (gz(23) + gz(24)) / 2;

    const shX = (gx(11) + gx(12)) / 2;
    const shY = (gy(11) + gy(12)) / 2;
    const shZ = (gz(11) + gz(12)) / 2;

    const dx = shX - hipX, dy = shY - hipY, dz = shZ - hipZ;

    // clamp เหมือน np.maximum(scale, 1e-6) — เฟรมที่ MediaPipe หาคนไม่เจอจะเป็น
    // ศูนย์ทั้งแถว ถ้าไม่ clamp จะได้ inf/nan แล้วพัง window ทั้ง 36 เฟรม
    let scale = Math.sqrt(dx * dx + dy * dy + dz * dz);
    if (!(scale > 1e-6)) scale = 1e-6;

    for (let k = 0; k < poseIdx.length; k++) {
      const i = poseIdx[k];
      out[k * 3] = (gx(i) - hipX) / scale;
      out[k * 3 + 1] = (gy(i) - hipY) / scale;
      out[k * 3 + 2] = (gz(i) - hipZ) / scale;
    }
    pos.push(out);
  }

  const feats = [];
  for (let t = 0; t < T; t++) {
    const row = new Float32Array(nPos * 2);
    row.set(pos[t], 0);
    // เฟรมแรกไม่มีเฟรมก่อนหน้า vel = 0 (ตรงกับ vel[1:] = np.diff(...))
    if (t > 0) {
      for (let j = 0; j < nPos; j++) row[nPos + j] = pos[t][j] - pos[t - 1][j];
    }
    feats.push(row);
  }
  return feats;
}

/**
 * แปลง landmark ที่ได้จาก MediaPipe tasks-vision ให้เป็นแถว 132 ตัวเลข
 * ให้ตรงรูปแบบกับ extract_raw_pose() ฝั่ง Python
 *
 * ฝั่ง Python ใช้ mp.solutions.pose ซึ่งคืน .visibility มาด้วย ส่วน tasks-vision
 * ใช้ชื่อ .visibility เหมือนกันแต่บางเวอร์ชันเป็น undefined จึงมี ?? 0 กันไว้
 * (feature ไม่ได้ใช้ visibility อยู่แล้ว แต่ต้องกินที่ให้ครบ 4 ช่องไม่ให้ offset เลื่อน)
 *
 * @param {Array|null} landmarks  33 จุดจาก PoseLandmarker หรือ null เมื่อไม่เจอคน
 * @returns {Float32Array} 132 ตัวเลข — ศูนย์ทั้งแถวเมื่อไม่เจอคน
 */
export function extractRawPose(landmarks, nRaw = DEFAULTS.nRaw) {
  const out = new Float32Array(nRaw);          // ไม่เจอคน = ศูนย์ทั้งแถว
  if (!landmarks || landmarks.length < 33) return out;
  for (let i = 0; i < 33; i++) {
    const lm = landmarks[i];
    out[i * 4] = lm.x;
    out[i * 4 + 1] = lm.y;
    out[i * 4 + 2] = lm.z;
    out[i * 4 + 3] = lm.visibility ?? 0;
  }
  return out;
}
