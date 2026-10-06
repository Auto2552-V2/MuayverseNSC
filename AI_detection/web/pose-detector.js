/* ---------------------------------------------------------------------------------------------
 * pose-detector.js — ตัวตรวจจับท่ามวยในเบราว์เซอร์ (MediaPipe Pose + LSTM บน TF.js)
 *
 * หน้าที่: เปิดกล้อง → หา landmark → สร้าง feature → ให้ LSTM ทำนาย → ส่งท่าที่มั่นใจพอ
 * ออกไปให้ Unity เป็น { pose: "jab", confidence: 0.92 }
 *
 * ทุกอย่างรันบนเครื่องคนเล่น ภาพจากกล้องไม่ออกจากเครื่องเลย เซิร์ฟเวอร์ไม่ต้องคำนวณอะไร
 *
 * ─── ของที่ต้องมีก่อนใช้ ──────────────────────────────────────────────────────────
 *   tfjs_build/pose_action/model/model.json   โมเดลที่แปลงแล้ว
 *   tfjs_build/pose_action/meta.json          ลำดับคลาส + สเปก feature
 *   models/pose_landmarker_full.task          โมเดล pose ของ MediaPipe
 *
 * ⚠ ต้องเป็น "full" ไม่ใช่ "lite" — ตอนเก็บข้อมูลเทรนใช้ model_complexity=1 ซึ่งคือ full
 *   ถ้าใช้ lite ตำแหน่ง landmark จะคลาดจากที่โมเดลเคยเห็น ความแม่นจะตกโดยไม่มี error บอก
 *   ตั้ง poseModelUrl ชี้ไป lite ได้ถ้ายอมรับความแม่นที่ลดลง (ดู WARN ใน console)
 *
 * ─── ลำดับคลาสสำคัญมาก ───────────────────────────────────────────────────────────
 *   argmax คืนเลข index ลำดับคลาสจึงต้องอ่านจาก meta.json เท่านั้น ห้ามเดาเอง
 *   (ไม่ใช่เรียงตามตัวอักษร — ของจริงคือ jab, cross, hook, uppercut, idle, block)
 * ------------------------------------------------------------------------------------------- */

import { buildFeatures, extractRawPose, DEFAULTS } from './pose-features.js';

export const TRANSPORTS = ['auto', 'unity-webview', 'sendmessage', 'websocket'];

/**
 * อ่าน transport จาก query string เช่น game.html?transport=unity-webview
 *
 * มีไว้เพราะที่รันสามแบบต้องใช้ปลายทางคนละอย่าง และเมื่อก่อนต้องแก้ไฟล์สลับไปมา
 * ซึ่งพลาดง่าย (แก้แล้วลืมแก้กลับ แล้วไปงงบนมือถือว่าทำไมไม่มีอะไรส่งออก)
 *
 * ค่าที่ไม่รู้จักให้เตือนแล้วใช้ค่าเดิม ไม่ throw — พิมพ์ query ผิดตัวเดียวแล้วเกม
 * ไม่ขึ้นเลยนั้นแย่กว่าเกมที่ยังเล่นได้ด้วยปลายทางเริ่มต้น
 */
export function transportFromUrl(fallback = 'auto', search = globalThis.location?.search ?? '') {
  const v = new URLSearchParams(search).get('transport');
  if (!v) return fallback;
  if (TRANSPORTS.includes(v)) return v;
  console.warn(`[PoseDetector] ?transport=${v} ไม่รู้จัก ` +
               `(ใช้ได้: ${TRANSPORTS.join(', ')}) — ใช้ ${fallback} ต่อ`);
  return fallback;
}

const CFG_DEFAULT = {
  // ── ที่อยู่ไฟล์ (relative เสมอ ห้าม hardcode host) ──
  modelUrl: 'tfjs_build/pose_action/model/model.json',
  metaUrl: 'tfjs_build/pose_action/meta.json',
  poseModelUrl: 'models/pose_landmarker_full.task',
  // MediaPipe runtime — ลองของที่โฮสต์เองก่อน แล้วค่อยตก CDN
  //
  // โฮสต์เองเป็นหลักเพราะถ้าหน้าเว็บโหลดได้ ไฟล์พวกนี้ก็ต้องโหลดได้ด้วย (origin เดียวกัน)
  // ส่วน CDN เป็นคนละโดเมน ซึ่งเน็ตขององค์กร/งานแข่งบล็อกได้ แล้วเกมจะพังทั้งที่มีเน็ต
  //
  // CDN ยังเก็บไว้เป็นตัวสำรอง เผื่อ deploy ไม่ครบหรือเครื่องต้องใช้ build nosimd
  // ที่เราไม่ได้โฮสต์ไว้
  wasmBase: 'vendor/mediapipe',
  visionModule: 'vendor/mediapipe/vision_bundle.mjs',
  wasmBaseFallback: 'https://cdn.jsdelivr.net/npm/@mediapipe/tasks-vision@0.10.14/wasm',
  visionModuleFallback: 'https://cdn.jsdelivr.net/npm/@mediapipe/tasks-vision@0.10.14',

  // ── กล้อง ──
  // ขอ 640x480 ให้ตรงกับตอนเก็บข้อมูล (ASPECT 4:3) ถ้าเบราว์เซอร์ให้มาเป็น 16:9
  // โค้ดจะคำนวณ aspect จากขนาดจริงให้เอง ไม่ยึดค่าใน meta ตายตัว
  width: 640,
  height: 480,

  // ตอนเก็บข้อมูลเทรนใช้ cv2.flip(frame, 1) "ก่อน" ส่งเข้า MediaPipe
  // ที่นี่จึงต้องพลิกภาพก่อนเข้า MediaPipe เหมือนกัน ไม่ใช่ไปแก้พิกัดทีหลัง
  //
  // ⚠ ห้ามเปลี่ยนไปใช้วิธี x -> 1-x เด็ดขาด — วัดจริงแล้วการพลิกภาพทำให้ MediaPipe
  //   "สลับ index ซ้าย/ขวา" ของ landmark ด้วย (11<->12, 15<->16, ...) ไม่ใช่แค่ย้าย
  //   ตำแหน่ง x  วัดด้วย Video/JAB.mp4: พลิก x เฉย ๆ คลาดเฉลี่ย 0.084
  //   แต่พลิก x พร้อมสลับ index คลาดแค่ 0.0059 — ต่างกัน 14 เท่า
  //
  //   ถ้าพลิกแค่พิกัด ช่อง "ข้อมือซ้าย" ใน feature จะได้ข้อมือซ้ายจริง แต่ตอนเทรน
  //   ช่องนั้นบรรจุข้อมือ "ขวา" → ซ้ายขวาสลับกับที่เทรนมาทั้งหมด jab กับ cross พังทันที
  mirror: true,

  // ── เกณฑ์ตัดสินว่าจะส่งท่าออกไปไหม ──
  threshold: 0.7,          // ความมั่นใจขั้นต่ำ (ตรงกับที่ Unity กรองอีกชั้น)
  consensus: 3,            // ต้องทายซ้ำกันกี่เฟรมติด กันการสั่นตอนท่ากำลังเปลี่ยน
  idleAction: 'idle',      // ท่านี้ไม่ส่งออกไป เป็นแค่สถานะ "ยังไม่ออกท่า"
  releaseFrames: 3,        // ต้องหลุดจากท่าที่ส่งแล้วกี่เฟรม ก่อนจะส่งท่าเดิมซ้ำได้
  emitCooldownMs: 250,     // กันส่งรวดเร็วเกินจริง (ต่ำกว่านี้คนทำไม่ทันอยู่ดี)

  // ── ท่าที่ "ค้างไว้ได้" ──
  //
  // หมัดเป็นท่าชั่วขณะ ออกแล้วดึงกลับ การล็อกไว้จนเลิกทำจึงถูกต้อง
  // แต่ block คือการ์ดสูงที่ผู้เล่นยกค้างไว้ได้เป็นสิบวินาที ถ้าล็อกแบบเดียวกัน
  // มันจะส่งออกครั้งเดียวตอนเริ่มยก แล้วรอบถัด ๆ ไปเกมจะไม่ได้รับอะไรเลย
  // ทั้งที่ผู้เล่นยังกันอยู่ -> โดนนับเป็น MISS ทั้งที่ทำถูก
  //
  // ท่าในรายการนี้จึงส่งซ้ำเป็นจังหวะตราบใดที่ยังทำค้างอยู่
  sustainedPoses: ['block'],
  sustainedRepeatMs: 700,

  // ── ส่งท่าเดิมซ้ำติด ๆ กันได้ ──
  //
  // ปกติท่าชั่วขณะถูกล็อกไว้จนกว่าจะเห็นว่าผู้เล่นออกจากท่านั้นจริง (releaseFrames)
  // ซึ่งถูกต้องตอนต่อยหมัดเดียว แต่เวลารัว uppercut สามครั้งติด โมเดลมักอ่านว่า
  // "uppercut" ต่อเนื่องโดยไม่มีช่วงหลุดคั่นเลย เกมจึงได้หมัดเดียว
  //
  // ตั้งเป็นมิลลิวินาที > 0 เพื่อให้ส่งท่าเดิมซ้ำได้หลังเว้นระยะเท่านี้ แม้ยังไม่หลุดจากท่า
  // 0 = ปิด (พฤติกรรมเดิม)
  //
  // ⚠ แลกกัน: window เลื่อนทีละเฟรม หมัดเดียวถูกทายเป็นท่านั้นติดกันหลายสิบเฟรม
  // ตั้งต่ำเกินไปหมัดเดียวจะกลายเป็นสองสามหมัด ตั้งให้ยาวกว่าเวลาที่คนออกหมัดจริง
  // หนึ่งครั้ง (ราว 400-600ms) จึงจะแยกหมัดจริงออกจากหมัดหลอนได้
  repeatSameAfterMs: 0,

  // ── ส่งภาพกล้องไปให้ Unity วาดเอง ──
  //
  // บน Android กล้องเปิดได้ที่เดียว และ WebView เป็นคนถืออยู่ Unity จึงใช้
  // WebCamTexture ไม่ได้ ถ้าอยากให้ภาพกล้องไปโผล่ใน UI ของเกม (ซ้อนกับของอื่นได้
  // วางตรงไหนก็ได้) ต้องส่งเฟรมไปให้
  //
  // ตั้งเล็กและช้าไว้ก่อน — เป็นแค่ภาพ preview ให้ผู้เล่นเห็นว่าตัวเองอยู่ในกรอบไหม
  // ไม่ได้เอาไปประมวลผลต่อ 160x120 @10fps ก็พอเห็นแล้วและเบามาก
  sendPreviewToUnity: false,
  previewFps: 10,
  previewWidth: 160,
  previewQuality: 0.5,

  // วาดโครงกระดูกลงบนภาพก่อนส่ง — Unity ไม่ต้องทำอะไรเพิ่ม ได้ภาพมาพร้อมเส้นเลย
  // ใช้ดูว่าระบบจับร่างกายติดตรงไหน ถ้าท่าอ่านเพี้ยนจะเห็นทันทีว่าเพราะจับผิดจุด
  //
  // ข้อแลก: เส้นถูกบีบเป็น JPEG ไปด้วย ที่ 160px เส้นบางจะเบลอเล็กน้อย
  // ถ้าต้องการเส้นคมให้ปิดอันนี้แล้วส่งพิกัดไปวาดฝั่ง Unity แทน
  drawLandmarksOnPreview: true,
  previewSkeletonColor: '#4da3ff',
  previewJointColor: '#3ddc84',

  // ── ต้องเห็นคนจริงก่อนถึงจะทำนาย ──
  // สำคัญกว่าที่คิด: โมเดลไม่มีคลาส "ไม่มีคน" เฟรมเปล่าจึงถูก normalise เป็น
  // ศูนย์ทั้งแถว แล้วโมเดลตอบ block ด้วยความมั่นใจ 1.000 (verify_pipeline.mjs
  // จับได้) ถ้าไม่กรองตรงนี้ คนเล่นเดินออกจากกล้องจะได้บล็อคสำเร็จฟรีไปเรื่อย ๆ
  requirePresence: true,
  minPresenceRatio: 0.8,   // ต้องมีเฟรมที่เจอคนอย่างน้อยเท่านี้ใน window

  // ส่งสถานะ nopose/pose-ok ไปให้ Unity ด้วยไหม — เปิดตอนเล่นจริงที่ WebView ถูกซ่อน
  // ปิดไว้ตอนใช้หน้าทดสอบ เพราะเห็นสถานะบนจอเว็บอยู่แล้ว
  reportPresenceToUnity: false,

  // ส่ง "ท่าที่โมเดลเห็นอยู่ตอนนี้" ไปให้ Unity ด้วย (type: "live")
  //
  // ต่างจากท่าที่ส่งจริง: ตัวนั้นผ่านตัวกรองสามชั้นแล้วจึงออกครั้งเดียวต่อหนึ่งหมัด
  // ส่วนตัวนี้คือผลดิบจากโมเดล ใช้ให้ผู้เล่นเห็นว่าระบบกำลังอ่านท่าตนว่าอะไร
  // ไม่งั้นเวลาชกแล้วไม่ติด จะแยกไม่ออกว่าอ่านไม่ออกหรืออ่านออกแต่ตอบผิด
  //
  // ส่งเฉพาะตอน "ท่าเปลี่ยน" ไม่ใช่ทุกเฟรม เพื่อไม่ให้บริดจ์โดนยิง 30 ครั้ง/วินาที
  reportLivePose: false,
  liveMinConfidence: 0.35,   // ต่ำกว่านี้ถือว่ายังอ่านไม่ออก ส่งเป็นค่าว่าง

  // ── ปลายทาง ──
  // 'unity-webview' = Android: gree/unity-webview ฉีด window.Unity.call() ให้
  // 'sendmessage'   = WebGL: Unity อยู่ใน canvas หน้าเดียวกัน
  // 'websocket'     = ต่อไป WS server (ใช้ตอนดีบักกับ mock_unity_ws.mjs)
  // 'auto'          = เลือกจากที่มีจริง: unity-webview -> sendmessage -> websocket
  //
  // auto ดูว่า "มีอะไรให้ใช้" ไม่ใช่ไล่ลองต่อ จึงไม่เปิด WebSocket ทิ้งไว้เปล่า ๆ
  // ตอนอยู่ใน WebView (สำคัญบนมือถือ — WS ที่ต่อไม่ติดจะ retry กินแบตไปเรื่อย)
  transport: 'auto',
  wsUrl: 'ws://127.0.0.1:8787',
  wsRetryMs: 1500,
  unityObject: 'PoseReceiver',
  unityMethod: 'OnPoseJson',

  logEvents: true,
};

export class PoseDetector {
  constructor(cfg = {}) {
    this.cfg = { ...CFG_DEFAULT, ...cfg };
    this.meta = null;
    this.model = null;
    this.landmarker = null;
    this.video = null;
    this.stream = null;

    // ภาพที่พลิกแล้ว — เป็น "ภาพที่ MediaPipe เห็นจริง" หน้า UI ควรโชว์ตัวนี้
    // ไม่ใช่โชว์ <video> แล้วพลิกด้วย CSS เพราะจะทำให้พิกัด landmark ไม่ตรงกับภาพ
    this.mirrorCanvas = null;
    this._mirrorCtx = null;

    this.rawBuffer = [];       // 36 เฟรมล่าสุดของ landmark ดิบ (132 ตัวเลข/เฟรม)
    this.presenceBuffer = [];  // เฟรมนั้น MediaPipe เจอคนไหม (ยาวเท่า rawBuffer)
    this.recentArgmax = [];    // ผลทายล่าสุด ใช้นับ consensus
    this.lockedAction = null;  // ท่าที่ส่งไปแล้ว รอหลุดก่อนส่งซ้ำ
    this.running = false;
    this._resetState();

    this.ws = null;
    this.wsReady = false;
    this.activeTransport = null;
    this.emitSeq = 0;          // ไม่รีเซ็ตตอน start ใหม่ — ต่อเนื่องทั้งเซสชัน

    // hook ให้หน้า UI เอาไปแสดง — ไม่บังคับใช้
    this.onProbabilities = null;   // (probsArray, actions) => void
    this.onPose = null;            // ({pose, confidence}) => void
    this.onStatus = null;          // (stringMessage) => void
    this.onNoPose = null;          // (presenceRatio) => void — ไม่เห็นคนในกล้อง
    this.onStatusMessage = null;   // (payload) => void — สถานะที่ส่งให้ Unity
    this.presenceRatio = 0;

    // ส่ง nopose/pose-ok เฉพาะตอน "เปลี่ยนสถานะ" ไม่ใช่ทุกเฟรม
    // ไม่งั้นบริดจ์จะโดนยิง 30 ข้อความต่อวินาทีตอนไม่มีใครอยู่หน้ากล้อง
    this._lastPresenceState = null;
    this._lastLiveLabel = null;
    // -Infinity ไม่ใช่ 0 — performance.now() นับจากตอนเปิดหน้าเว็บ ถ้าตั้ง 0
    // แปลว่า "เคยส่งตอนเวลา 0" แล้วเฟรมแรก ๆ จะถูกข้ามทิ้งโดยไม่จำเป็น
    this._lastPreviewAt = -Infinity;
    this._previewCanvas = null;
    this._previewCtx = null;
  }

  /**
   * ล้างสถานะการตัดสินใจทั้งหมดกลับไปเป็น "ยังไม่เคยเห็นอะไรเลย"
   *
   * เรียกทั้งตอนสร้างและตอน start() ใหม่ — กดหยุดแล้วเริ่มอีกครั้งไม่ควรเอา
   * buffer เฟรมเก่ากับท่าที่ล็อกไว้จากเซสชันก่อนมาปน
   *
   * lastEmitAt ต้องเป็น -Infinity ไม่ใช่ 0: performance.now() นับจากตอนเปิดหน้าเว็บ
   * ถ้าตั้ง 0 แปลว่า "เคยส่งท่าตอนเวลา 0" ซึ่งทำให้ cooldown บล็อกท่าแรกทิ้ง
   * เมื่อหน้าเว็บเพิ่งเปิดมาไม่ถึง emitCooldownMs
   */
  _resetState() {
    this.rawBuffer.length = 0;
    this.presenceBuffer.length = 0;
    this.recentArgmax.length = 0;
    this.lockedAction = null;
    this.releaseCount = 0;
    this.lastEmitAt = -Infinity;
    this.lastVideoTime = -1;
    this.presenceRatio = 0;
  }

  _status(msg) {
    if (this.cfg.logEvents) console.log('[PoseDetector]', msg);
    this.onStatus?.(msg);
  }

  // ── โหลดทุกอย่างที่ต้องใช้ ────────────────────────────────────────────────────
  async init() {
    const tf = globalThis.tf;
    if (!tf) throw new Error('ไม่พบ TF.js — โหลด @tensorflow/tfjs ก่อนเรียก init()');

    // อ้างอิง path จาก "ที่อยู่ของไฟล์นี้" ไม่ใช่จาก URL ของหน้าเว็บ
    //
    // path แบบ relative ปกติจะ resolve เทียบกับ URL ของหน้า ซึ่งพังเมื่อเข้าแบบ
    // ไม่มี / ปิดท้าย:  https://host/pose  -> ขอ https://host/tfjs_build/... (404)
    //                   https://host/pose/ -> ขอ https://host/pose/tfjs_build/... (ถูก)
    // ไฟล์โมเดลวางอยู่ข้าง ๆ สคริปต์นี้เสมอ เทียบกับ import.meta.url จึงถูกทุกกรณี
    // และยังรองรับ path เต็มหรือ path ที่ขึ้นต้นด้วย / ที่ส่งเข้ามาเองได้ด้วย
    const near = (u) => new URL(u, import.meta.url).href;
    this.cfg.metaUrl = near(this.cfg.metaUrl);
    this.cfg.modelUrl = near(this.cfg.modelUrl);
    this.cfg.poseModelUrl = near(this.cfg.poseModelUrl);

    // meta ต้องมาก่อน เพราะมันบอกว่าโมเดลคาดหวังอะไร
    const res = await fetch(this.cfg.metaUrl);
    if (!res.ok) {
      throw new Error(`โหลด ${this.cfg.metaUrl} ไม่ได้ (${res.status}) — ` +
        'รัน scripts/train_pose_model.py ก่อนเพื่อสร้าง meta.json');
    }
    this.meta = await res.json();

    this.actions = this.meta.actions ?? DEFAULTS.actions;
    this.sequenceLength = this.meta.sequence_length ?? DEFAULTS.sequenceLength;
    this.featureCfg = {
      poseIdx: this.meta.pose_idx ?? DEFAULTS.poseIdx,
      aspect: this.meta.aspect ?? DEFAULTS.aspect,
    };
    this._status(`meta: ${this.actions.length} คลาส ${this.actions.join(', ')} | ` +
      `window ${this.sequenceLength}x${this.meta.n_features}`);

    this.model = await tf.loadLayersModel(this.cfg.modelUrl);

    // เช็ก shape ที่โมเดลรับกับที่ meta บอกให้ตรงกัน ถ้าไม่ตรงคือหยิบโมเดลผิดตัว
    // (เช่นเผลอชี้ไป action.h5 ตัวเก่าที่เป็น (30,1662) -> 3 คลาส)
    const inShape = this.model.inputs[0].shape;     // [null, T, F]
    const outUnits = this.model.outputs[0].shape.at(-1);
    if (inShape[1] !== this.sequenceLength || inShape[2] !== this.meta.n_features) {
      throw new Error(`โมเดลรับ (${inShape[1]}, ${inShape[2]}) แต่ meta บอก ` +
        `(${this.sequenceLength}, ${this.meta.n_features}) — คนละโมเดลกัน`);
    }
    if (outUnits !== this.actions.length) {
      throw new Error(`โมเดลออก ${outUnits} คลาส แต่ meta มี ` +
        `${this.actions.length} ชื่อ — คนละโมเดลกัน`);
    }
    this._status(`โมเดลพร้อม (${inShape[1]}, ${inShape[2]}) -> ${outUnits} คลาส`);

    if (/lite/i.test(this.cfg.poseModelUrl)) {
      console.warn('[PoseDetector] WARN: ใช้ pose_landmarker_lite แต่ข้อมูลเทรนเก็บด้วย ' +
        'model_complexity=1 (full) — ความแม่นจะตกลง แนะนำให้ใช้ไฟล์ full');
    }

    const { vision, fileset } = await this._loadVision();
    this.landmarker = await vision.PoseLandmarker.createFromOptions(fileset, {
      baseOptions: { modelAssetPath: this.cfg.poseModelUrl, delegate: 'GPU' },
      runningMode: 'VIDEO',
      numPoses: 1,
      // ให้ตรงกับ mp_pose.Pose(min_detection_confidence=0.5, min_tracking_confidence=0.5)
      minPoseDetectionConfidence: 0.5,
      minPosePresenceConfidence: 0.5,
      minTrackingConfidence: 0.5,
    });
    this._status('MediaPipe PoseLandmarker พร้อม');

    this._connectTransport();
    return this;
  }

  /**
   * โหลด MediaPipe runtime — ของที่โฮสต์เองก่อน ไม่ได้ค่อยไป CDN
   *
   * แยกเป็นเมธอดเพราะต้องลองสองรอบ และต้องลองทั้ง module กับ wasm เป็นคู่กัน
   * (ผสมข้ามเวอร์ชันระหว่างสองแหล่งแล้วพังแบบงง ๆ)
   */
  async _loadVision() {
    const attempts = [
      { mod: this.cfg.visionModule, wasm: this.cfg.wasmBase, label: 'โฮสต์เอง' },
      { mod: this.cfg.visionModuleFallback, wasm: this.cfg.wasmBaseFallback, label: 'CDN' },
    ].filter((a) => a.mod && a.wasm);

    let lastError;
    for (const a of attempts) {
      try {
        // path แบบ relative ต้องเทียบกับที่อยู่ของไฟล์นี้ ไม่ใช่ URL ของหน้า
        // (เหตุผลเดียวกับ near() ใน init)
        const modUrl = new URL(a.mod, import.meta.url).href;
        const wasmUrl = new URL(a.wasm, import.meta.url).href;

        const vision = await import(/* @vite-ignore */ modUrl);
        const fileset = await vision.FilesetResolver.forVisionTasks(wasmUrl);
        this._status(`MediaPipe runtime: ${a.label}`);
        return { vision, fileset };
      } catch (e) {
        lastError = e;
        console.warn(`[PoseDetector] โหลด MediaPipe จาก${a.label}ไม่ได้: ${e.message}`);
      }
    }
    throw new Error(`โหลด MediaPipe ไม่ได้เลยทั้งสองทาง — ${lastError?.message}`);
  }

  // ── กล้อง ────────────────────────────────────────────────────────────────────
  async start(videoEl) {
    this._resetState();
    this.video = videoEl ?? document.createElement('video');
    this.stream = await navigator.mediaDevices.getUserMedia({
      video: { width: this.cfg.width, height: this.cfg.height },
      audio: false,
    });
    this.video.srcObject = this.stream;
    this.video.playsInline = true;
    this.video.muted = true;
    await this.video.play();

    const w = this.video.videoWidth, h = this.video.videoHeight;
    // aspect จริงของภาพที่ได้ ไม่ใช่ค่าที่ขอไป — เบราว์เซอร์มักให้ 16:9 มาแทน
    // ตัว normalise ต้องใช้ค่าจริงไม่งั้นมุมทุกมุมใน feature จะเบี้ยว
    this.featureCfg.aspect = w / h;
    this._status(`กล้อง ${w}x${h} (aspect ${this.featureCfg.aspect.toFixed(3)})`);
    if (Math.abs(w / h - (this.meta.aspect ?? 4 / 3)) > 0.05) {
      console.warn(`[PoseDetector] WARN: กล้องให้ ${w}x${h} แต่ข้อมูลเทรนเก็บที่ ` +
        `${this.meta.frame_size?.join('x')} — ใช้ aspect จริงคำนวณแล้ว ` +
        'แต่มุมกล้องที่ต่างจากตอนเก็บข้อมูลยังทำให้ความแม่นตกได้');
    }

    this.running = true;
    this._loop();
    return this;
  }

  stop() {
    this.running = false;
    this.stream?.getTracks().forEach((t) => t.stop());
    this.stream = null;
    if (this.ws) { this.ws.onclose = null; this.ws.close(); this.ws = null; }
    this.wsReady = false;
  }

  // ── ลูปหลัก ──────────────────────────────────────────────────────────────────
  _loop() {
    if (!this.running) return;

    // MediaPipe ไม่ยอมรับ timestamp ซ้ำ ถ้าเฟรมยังไม่เปลี่ยนให้ข้ามไปเลย
    if (this.video.currentTime !== this.lastVideoTime) {
      this.lastVideoTime = this.video.currentTime;
      try {
        const source = this._sourceFrame();
        const result = this.landmarker.detectForVideo(source, performance.now());
        this._onFrame(result?.landmarks?.[0] ?? null);
        this._sendPreview();
      } catch (e) {
        console.error('[PoseDetector] detectForVideo ล้ม:', e);
      }
    }
    requestAnimationFrame(() => this._loop());
  }

  /**
   * ภาพที่จะส่งให้ MediaPipe — พลิกซ้ายขวาแล้วถ้า cfg.mirror
   *
   * ต้องพลิกที่ตัวภาพ ไม่ใช่ที่พิกัดผลลัพธ์ เพราะ MediaPipe ตั้งชื่อ landmark
   * ตามกายวิภาคที่มันมองเห็น พอภาพพลิก มันจึงสลับ index ซ้าย/ขวาให้เอง
   * ซึ่งเป็นพฤติกรรมที่ข้อมูลเทรนฝังมาด้วย (ดูคอมเมนต์ที่ cfg.mirror)
   */
  _sourceFrame() {
    if (!this.cfg.mirror) return this.video;

    const w = this.video.videoWidth, h = this.video.videoHeight;
    if (!w || !h) return this.video;            // กล้องยังไม่พร้อม

    if (!this.mirrorCanvas) {
      this.mirrorCanvas = document.createElement('canvas');
      // willReadFrequently ไม่ต้อง — เราแค่วาดลงไปแล้วส่งต่อให้ GPU อ่าน
      this._mirrorCtx = this.mirrorCanvas.getContext('2d');
    }
    if (this.mirrorCanvas.width !== w || this.mirrorCanvas.height !== h) {
      this.mirrorCanvas.width = w;
      this.mirrorCanvas.height = h;
    }

    const ctx = this._mirrorCtx;
    ctx.save();
    ctx.scale(-1, 1);
    ctx.drawImage(this.video, -w, 0, w, h);
    ctx.restore();
    return this.mirrorCanvas;
  }

  _onFrame(landmarks) {
    // landmarks มาจากภาพที่พลิกแล้วตั้งแต่ _sourceFrame() พิกัดกับ index จึงอยู่ใน
    // ระบบเดียวกับข้อมูลเทรนอยู่แล้ว — ห้ามมาพลิก x ซ้ำที่นี่
    const raw = extractRawPose(landmarks, this.meta.n_raw ?? DEFAULTS.nRaw);

    this.rawBuffer.push(raw);
    this.presenceBuffer.push(landmarks ? 1 : 0);
    if (this.rawBuffer.length > this.sequenceLength) {
      this.rawBuffer.shift();
      this.presenceBuffer.shift();
    }
    if (this.rawBuffer.length < this.sequenceLength) return;   // ยังไม่ครบ window

    // ไม่เห็นคนมากพอ = ไม่ทำนายเลย ไม่ใช่ทำนายแล้วค่อยทิ้ง เพราะถ้าปล่อยผ่าน
    // ไปถึง _gate() แถบความมั่นใจบนหน้าจอจะโชว์ block 100% ทำให้เข้าใจผิดว่า
    // โมเดลเพี้ยน ทั้งที่จริงคือไม่มีใครอยู่ในกล้อง
    this.presenceRatio =
      this.presenceBuffer.reduce((a, b) => a + b, 0) / this.presenceBuffer.length;
    if (this.cfg.requirePresence && this.presenceRatio < this.cfg.minPresenceRatio) {
      this.recentArgmax.length = 0;      // กัน consensus ค้างข้ามช่วงที่คนหลุดเฟรม
      this.onProbabilities?.(new Float32Array(this.actions.length), this.actions);
      this.onNoPose?.(this.presenceRatio);
      this._reportPresence('nopose');
      return;
    }
    this._reportPresence('pose-ok');

    const feats = buildFeatures(this.rawBuffer, this.featureCfg);
    const probs = this._predict(feats);
    this.onProbabilities?.(probs, this.actions);
    this._reportLive(probs);
    this._gate(probs);
  }

  /**
   * วาดโครงกระดูกทับภาพ preview
   *
   * ใช้ rawBuffer เฟรมล่าสุด ซึ่งเป็นพิกัดจาก "ภาพที่พลิกแล้ว" ตัวเดียวกับที่วาดอยู่
   * จึงแมปลงตรง ๆ ด้วย x*w, y*h โดยไม่ต้องพลิกอะไรซ้ำ — พลิกซ้ำเมื่อไหร่เส้นจะ
   * ไม่ทับตัวคนทันที (เคยพลาดมาแล้วตอนทำ overlay ในหน้าทดสอบ)
   */
  _drawSkeleton(ctx, w, h) {
    const raw = this.rawBuffer.at(-1);
    if (!raw) return;

    // เส้นเชื่อมลำตัวกับแขน — ใช้เฉพาะส่วนที่โมเดลสนใจ ขาไม่ต้องวาดให้รก
    const BONES = [[11, 12], [11, 13], [13, 15], [12, 14], [14, 16],
                   [11, 23], [12, 24], [23, 24]];
    const JOINTS = [11, 12, 13, 14, 15, 16, 19, 20, 23, 24];

    const px = (i) => [raw[i * 4] * w, raw[i * 4 + 1] * h];
    // เฟรมที่ไม่เจอคนเป็นศูนย์ทั้งแถว — ข้ามไปไม่งั้นจะได้เส้นลากไปมุมซ้ายบน
    const ok = (i) => raw[i * 4] !== 0 || raw[i * 4 + 1] !== 0;

    // ความหนาเส้นสเกลตามขนาดภาพ ไม่งั้นที่ 160px เส้นจะหนาจนบังตัวคน
    ctx.lineWidth = Math.max(1, Math.round(w / 90));
    ctx.strokeStyle = this.cfg.previewSkeletonColor;
    ctx.lineCap = 'round';

    for (const [a, b] of BONES) {
      if (!ok(a) || !ok(b)) continue;
      const [x1, y1] = px(a), [x2, y2] = px(b);
      ctx.beginPath();
      ctx.moveTo(x1, y1);
      ctx.lineTo(x2, y2);
      ctx.stroke();
    }

    const r = Math.max(1.5, w / 70);
    ctx.fillStyle = this.cfg.previewJointColor;
    for (const i of JOINTS) {
      if (!ok(i)) continue;
      const [x, y] = px(i);
      ctx.beginPath();
      ctx.arc(x, y, r, 0, Math.PI * 2);
      ctx.fill();
    }
  }

  /**
   * ส่งภาพกล้องย่อส่วนไปให้ Unity วาดเป็น preview
   *
   * ส่งภาพที่ "พลิกแล้ว" ตัวเดียวกับที่โมเดลเห็น ผู้เล่นจะได้เห็นแบบส่องกระจก
   * และตรงกับที่ระบบกำลังอ่านจริง ๆ
   *
   * เรียกจากลูปหลักทุกเฟรม แต่ส่งจริงตาม previewFps เท่านั้น — encode JPEG
   * ทุกเฟรมที่ 30fps กิน CPU ฟรี ๆ ทั้งที่ตาคนดูไม่ออกอยู่แล้ว
   */
  _sendPreview() {
    if (!this.cfg.sendPreviewToUnity || !this.mirrorCanvas) return;

    const now = performance.now();
    const interval = 1000 / Math.max(1, this.cfg.previewFps);
    if (now - this._lastPreviewAt < interval) return;
    this._lastPreviewAt = now;

    const src = this.mirrorCanvas;
    if (!src.width || !src.height) return;

    const w = Math.max(32, Math.round(this.cfg.previewWidth));
    const h = Math.round(w * src.height / src.width);

    if (!this._previewCanvas) {
      this._previewCanvas = document.createElement('canvas');
      this._previewCtx = this._previewCanvas.getContext('2d');
    }
    if (this._previewCanvas.width !== w || this._previewCanvas.height !== h) {
      this._previewCanvas.width = w;
      this._previewCanvas.height = h;
    }
    this._previewCtx.drawImage(src, 0, 0, w, h);
    if (this.cfg.drawLandmarksOnPreview) this._drawSkeleton(this._previewCtx, w, h);

    let data;
    try {
      // ตัด "data:image/jpeg;base64," ทิ้ง ฝั่ง C# รับ base64 ล้วน
      data = this._previewCanvas.toDataURL('image/jpeg', this.cfg.previewQuality).split(',')[1];
    } catch (e) {
      console.warn('[PoseDetector] encode preview ล้ม:', e);
      this.cfg.sendPreviewToUnity = false;     // ล้มแล้วล้มอีก ปิดไปเลยดีกว่า
      return;
    }

    const json = JSON.stringify({ type: 'frame', w, h, data });
    for (const send of this._sinks()) if (send(json)) return;
  }

  /// ส่งท่าที่โมเดลเห็นตอนนี้ เฉพาะตอนที่มันเปลี่ยนจากครั้งก่อน
  _reportLive(probs) {
    if (!this.cfg.reportLivePose) return;

    let best = 0;
    for (let i = 1; i < probs.length; i++) if (probs[i] > probs[best]) best = i;
    const conf = probs[best];
    const label = conf >= this.cfg.liveMinConfidence ? this.actions[best] : '';

    if (label === this._lastLiveLabel) return;
    this._lastLiveLabel = label;

    // ใช้ type แยกจาก pose/status เพื่อให้ฝั่ง Unity แยกได้ว่าอันนี้ "ยังไม่นับ"
    // และไม่ไปปนกับการนับ seq ของข้อความที่มีผลต่อเกม
    const json = JSON.stringify({ type: 'live', pose: label, confidence: Math.round(conf * 1e3) / 1e3 });
    for (const send of this._sinks()) if (send(json)) return;
  }

  /// ส่งสถานะการเห็นคน เฉพาะตอนที่มันเปลี่ยนจากเดิมเท่านั้น
  _reportPresence(state) {
    if (this._lastPresenceState === state) return;
    this._lastPresenceState = state;
    if (this.cfg.reportPresenceToUnity) {
      this.emitStatus(state, `presence ${(this.presenceRatio * 100).toFixed(0)}%`);
    }
  }

  _predict(feats) {
    const tf = globalThis.tf;
    // tidy ปล่อย tensor ให้ทุกตัว ถ้าไม่ใช้จะรั่วทีละเฟรมจนหน่วยความจำ GPU หมด
    return tf.tidy(() => {
      const flat = new Float32Array(this.sequenceLength * this.meta.n_features);
      for (let t = 0; t < this.sequenceLength; t++) {
        flat.set(feats[t], t * this.meta.n_features);
      }
      const x = tf.tensor3d(flat, [1, this.sequenceLength, this.meta.n_features]);
      return this.model.predict(x).dataSync();
    });
  }

  /**
   * ตัดสินว่าผลทายนี้ควรส่งออกไปไหม — สามชั้นกันการส่งรั่ว
   *   1. consensus : ทายซ้ำกัน N เฟรมติด (กันการสั่นช่วงท่ากำลังเปลี่ยน)
   *   2. threshold : ความมั่นใจถึงเกณฑ์
   *   3. lock      : ส่งท่าหนึ่งแล้วล็อกไว้ จนกว่าจะหลุดจากท่านั้นจริง ๆ
   *
   * ข้อ 3 สำคัญกว่าที่คิด: window เลื่อนทีละเฟรม หมัดเดียวจะถูกทายเป็น "jab"
   * ติดกันหลายสิบเฟรม ถ้าไม่ล็อกไว้ Unity จะได้ jab ซ้ำ 30 ครั้งจากหมัดเดียว
   *
   * ต่างจาก notebook ที่ใช้ `word != sentence[-1]` ซึ่งทำให้ "ชกท่าเดิมสองครั้งติด"
   * ส่งได้แค่ครั้งเดียว — ในเกมจริงต่อย jab สองหมัดต้องนับสองหมัด จึงปลดล็อกด้วย
   * "หลุดจากท่านั้น releaseFrames เฟรม" แทนการเทียบกับท่าก่อนหน้า
   */
  _gate(probs) {
    let best = 0;
    for (let i = 1; i < probs.length; i++) if (probs[i] > probs[best]) best = i;
    const action = this.actions[best];
    const confidence = probs[best];

    this.recentArgmax.push(best);
    if (this.recentArgmax.length > this.cfg.consensus) this.recentArgmax.shift();

    // ปลดล็อก: ต้องเห็นว่าออกจากท่าที่ส่งไปแล้วจริง ๆ ติดกันหลายเฟรม
    if (this.lockedAction !== null) {
      if (action !== this.lockedAction) {
        if (++this.releaseCount >= this.cfg.releaseFrames) {
          this.lockedAction = null;
          this.releaseCount = 0;
        }
      } else {
        this.releaseCount = 0;
      }
    }

    if (action === this.cfg.idleAction) return;      // idle ไม่ใช่ท่า ไม่ส่ง
    if (confidence < this.cfg.threshold) return;
    if (this.recentArgmax.length < this.cfg.consensus) return;
    if (!this.recentArgmax.every((v) => v === best)) return;

    const now = performance.now();
    const sustained = this.cfg.sustainedPoses?.includes(action);

    if (this.lockedAction === action) {
      // ท่าค้างได้ -> ส่งซ้ำเป็นจังหวะ ไม่ปลดล็อก (ยังถือว่าเป็นการค้างท่าเดิมอยู่)
      // ท่าชั่วขณะ -> เงียบจนกว่าจะเลิกทำ เว้นแต่เปิด repeatSameAfterMs ไว้
      const repeatMs = sustained
        ? this.cfg.sustainedRepeatMs
        : (this.cfg.repeatSameAfterMs > 0 ? this.cfg.repeatSameAfterMs : Infinity);
      if (now - this.lastEmitAt < repeatMs) return;
    } else if (now - this.lastEmitAt < this.cfg.emitCooldownMs) {
      return;
    }

    this.lockedAction = action;
    this.releaseCount = 0;
    this.lastEmitAt = now;
    this._emit(action, confidence);
  }

  // ── ส่งออกไปให้ Unity ────────────────────────────────────────────────────────
  _emit(pose, confidence) {
    // seq เพิ่มทีละ 1 ไม่มีวันข้าม ฝั่ง Unity จึงรู้ได้ว่ามีข้อความหายระหว่างทางไหม
    // (ถ้าได้ 5 แล้วกระโดดไป 7 แปลว่า 6 หาย) สำคัญตอนดีบักบนมือถือที่ไม่มี console
    const payload = {
      type: 'pose',
      pose,
      // ปัดทศนิยมให้สั้นลง: ตัวเลข 17 หลักไม่ได้ให้ข้อมูลเพิ่ม แต่ทำให้ log อ่านยาก
      confidence: Math.round(confidence * 1e4) / 1e4,
      seq: ++this.emitSeq,
      t: Math.round(performance.now()),
    };
    if (this.cfg.logEvents) console.log('[PoseDetector] ->', payload);
    this.onPose?.(payload);

    const json = JSON.stringify(payload);
    for (const send of this._sinks()) {
      if (send(json)) return;         // ตัวแรกที่ส่งสำเร็จพอ
    }
    if (this.cfg.logEvents) {
      console.warn('[PoseDetector] ไม่มีปลายทางให้ส่ง — ท่านี้หายไปเฉย ๆ', payload);
    }
  }

  /**
   * ส่ง "สถานะ" กลับไปให้ Unity ผ่านช่องทางเดียวกับท่า
   *
   * จำเป็นเพราะตอนเล่นจริง WebView ถูกซ่อนไว้ ผู้เล่นมองไม่เห็นหน้าเว็บเลย
   * ถ้ากล้องเปิดไม่ได้หรือหลุดเฟรม เกมต้องรู้เพื่อขึ้นข้อความเตือนแทน —
   * ไม่งั้นผู้เล่นจะเห็นแค่ "ชกแล้วเกมไม่ตอบสนอง" โดยไม่รู้สาเหตุ
   *
   * @param {string} state   ready | nocamera | nopose | pose-ok | error
   * @param {string} detail  ข้อความอธิบาย (ไม่บังคับ)
   */
  emitStatus(state, detail = '') {
    const payload = {
      type: 'status',
      state,
      detail: String(detail).slice(0, 300),   // กันข้อความยาวเกินจนบริดจ์สะดุด
      seq: ++this.emitSeq,
      t: Math.round(performance.now()),
    };
    if (this.cfg.logEvents) console.log('[PoseDetector] status ->', payload);
    this.onStatusMessage?.(payload);

    const json = JSON.stringify(payload);
    for (const send of this._sinks()) {
      if (send(json)) return true;
    }
    return false;
  }

  /**
   * ปลายทางที่จะลองส่งตามลำดับ คืน true เมื่อส่งสำเร็จ
   *
   * เรียงจาก "แน่นอนที่สุด" ไปหา "เผื่อไว้" เพราะหน้าเดียวกันนี้ถูกเปิดได้สามแบบ
   * และเราอยากให้มันทำงานได้โดยไม่ต้องแก้ config ทุกครั้งที่เปลี่ยนที่รัน
   */
  _sinks() {
    const t = this.cfg.transport;
    const all = {
      // Android: gree/unity-webview ฉีด window.Unity ผ่าน addJavascriptInterface
      // ชื่อ "Unity" method "call" -> เข้า WebViewObject.CallFromJS() ฝั่ง C#
      // เป็น JS bridge จริง ไม่ใช่ URL scheme จึงไม่มีลิมิตความยาว/ความถี่แบบนั้น
      'unity-webview': (json) => {
        const u = globalThis.Unity;
        if (typeof u?.call !== 'function') return false;
        try { u.call(json); return true; } catch (e) {
          console.warn('[PoseDetector] Unity.call ล้ม:', e); return false;
        }
      },
      // WebGL: Unity อยู่ใน canvas หน้าเดียวกัน
      sendmessage: (json) => {
        const inst = globalThis.unityInstance ?? globalThis.gameInstance;
        if (!inst?.SendMessage) return false;
        // SendMessage หา GameObject จาก "ชื่อ" ถ้าชื่อไม่ตรงจะเงียบหายไปเฉย ๆ
        inst.SendMessage(this.cfg.unityObject, this.cfg.unityMethod, json);
        return true;
      },
      websocket: (json) => {
        if (!this.wsReady) return false;
        try { this.ws.send(json); return true; } catch (e) {
          console.warn('[PoseDetector] ws.send ล้ม:', e); return false;
        }
      },
    };
    if (t !== 'auto') return [all[t]].filter(Boolean);
    return [all['unity-webview'], all.sendmessage, all.websocket];
  }

  _connectTransport() {
    const t = this.cfg.transport;

    // ตรวจว่าอยู่ใน WebView ของ Unity หรือเปล่า — ถ้าใช่ไม่ต้องเปิด WebSocket เลย
    const hasUnityCall = typeof globalThis.Unity?.call === 'function';
    const hasSendMessage = Boolean(globalThis.unityInstance?.SendMessage);

    if (t === 'unity-webview' || (t === 'auto' && hasUnityCall)) {
      this.activeTransport = 'unity-webview';
      // ปลายทางที่ถูก pin ไว้ไม่มีตัวสำรอง จึงต้องบอกให้ชัดว่าบริดจ์ไม่มีจริง
      // ไม่งั้นจะอ่านว่า "ต่อแล้ว" ทั้งที่ทุกท่าหายไปเงียบ ๆ (เจอบ่อยตอนเปิดหน้านี้
      // ใน Chrome บนคอมโดยเผลอติด ?transport=unity-webview มาจากครั้งก่อน)
      this._status(hasUnityCall
        ? 'ส่งผ่าน Unity.call (gree/unity-webview บน Android)'
        : 'pin unity-webview แล้วแต่ไม่มี window.Unity.call — หน้านี้ไม่ได้เปิดใน WebView ' +
          'ของ Unity ท่าที่ตรวจได้จะไม่ถูกส่งไปไหน');
      return;
    }
    if (t === 'sendmessage' || (t === 'auto' && hasSendMessage)) {
      this.activeTransport = 'sendmessage';
      this._status(hasSendMessage
        ? 'ส่งผ่าน SendMessage (Unity WebGL ในหน้าเดียวกัน)'
        : 'pin sendmessage แล้วแต่ไม่มี unityInstance — หน้านี้ไม่ได้ฝังอยู่กับ Unity WebGL ' +
          'ท่าที่ตรวจได้จะไม่ถูกส่งไปไหน');
      return;
    }
    this._openWs();
  }

  _openWs() {
    try {
      this.ws = new WebSocket(this.cfg.wsUrl);
    } catch (e) {
      this._status(`เปิด WebSocket ไม่ได้: ${e.message}`);
      return;
    }

    this.ws.onopen = () => {
      this.wsReady = true;
      this.activeTransport = 'websocket';
      this._status(`WebSocket ต่อแล้ว -> ${this.cfg.wsUrl}`);
    };
    this.ws.onclose = () => {
      this.wsReady = false;
      // ต่อใหม่เรื่อย ๆ เพราะ Unity มักเปิดช้ากว่าหน้าเว็บ และผู้เล่นอาจรีสตาร์ตเกม
      // กลางเซสชันโดยไม่รีโหลดหน้า
      const wantsWs = this.cfg.transport === 'auto' || this.cfg.transport === 'websocket';
      if (wantsWs && this.ws) {
        this.activeTransport = this.cfg.transport === 'auto' ? 'sendmessage' : null;
        setTimeout(() => { if (this.ws) this._openWs(); }, this.cfg.wsRetryMs);
      }
    };
    this.ws.onerror = () => {
      // onclose ตามมาเองทุกครั้ง จัดการ retry ที่นั่นที่เดียว ไม่ต้องทำซ้ำที่นี่
      this.wsReady = false;
    };
  }
}

export default PoseDetector;
