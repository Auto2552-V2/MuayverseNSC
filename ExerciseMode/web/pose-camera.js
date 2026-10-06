/* ---------------------------------------------------------------------------------------------
 * pose-camera.js — กล้อง + MediaPipe PoseLandmarker เปล่า ๆ ไม่มีโมเดลไม่มีท่อส่งข้อมูล
 *
 * มีไว้ให้หน้าโหมดฝึกซ้อม (exercise.html) ใช้ เพราะมันต้องการแค่ "landmark ของเฟรมนี้"
 * ส่วน pose-detector.js พ่วง LSTM 36 เฟรม + ตัวกรอง + ท่อส่งเข้า Unity มาด้วยทั้งชุด
 * ซึ่งหน้านั้นไม่ได้ใช้เลย
 *
 * ⚠ ไฟล์นี้ทับซ้อนกับส่วนกล้อง/MediaPipe ใน pose-detector.js โดยตั้งใจ
 *   ตอนแรกคิดจะแยกส่วนนั้นออกมาให้ทั้งสองหน้าใช้ร่วมกัน แต่เทสต์ชุดที่มี (npm test)
 *   รันบน node ไม่มี DOM จึงไม่ได้แตะโค้ดกล้องเลย — แก้ pose-detector.js แล้วจะไม่มี
 *   อะไรจับได้ว่าทางเกม (ที่ทดสอบจบแล้ว) พังหรือไม่ จึงเลือกเขียนแยกไว้ก่อน
 *
 *   ถ้าจะรวมสองที่นี้เข้าด้วยกันวันหลัง ต้องมีเทสต์ที่รันในเบราว์เซอร์จริงก่อน
 *
 * ค่าคงที่ที่ "ห้ามเปลี่ยนโดยไม่อ่านก่อน" คือ mirror — ดูคอมเมนต์ที่ _sourceFrame()
 * ------------------------------------------------------------------------------------------- */

export const CAMERA_DEFAULTS = {
  poseModelUrl: 'models/pose_landmarker_full.task',
  wasmBase: 'vendor/mediapipe',
  visionModule: 'vendor/mediapipe/vision_bundle.mjs',
  wasmBaseFallback: 'https://cdn.jsdelivr.net/npm/@mediapipe/tasks-vision@0.10.14/wasm',
  visionModuleFallback: 'https://cdn.jsdelivr.net/npm/@mediapipe/tasks-vision@0.10.14',
  width: 640,
  height: 480,
  mirror: true,
};

export class PoseCamera {
  constructor(cfg = {}) {
    this.cfg = { ...CAMERA_DEFAULTS, ...cfg };
    this.landmarker = null;
    this.video = null;
    this.stream = null;
    this.mirrorCanvas = null;     // ภาพที่ MediaPipe เห็นจริง — หน้า UI ควรโชว์ตัวนี้
    this._mirrorCtx = null;
    this.running = false;
    this.lastVideoTime = -1;
    this.onFrame = null;          // (landmarks|null, nowMs) => void
    this.onStatus = null;         // (string) => void
  }

  _status(msg) {
    console.log('[PoseCamera]', msg);
    this.onStatus?.(msg);
  }

  async init() {
    // path ต้องเทียบกับที่อยู่ของไฟล์นี้ ไม่ใช่ URL ของหน้า — ไม่งั้นเข้าแบบไม่มี /
    // ปิดท้าย (https://host/pose) จะไปขอไฟล์ผิดที่แล้ว 404 โดยไม่มีอะไรบอกสาเหตุ
    const near = (u) => new URL(u, import.meta.url).href;
    const poseModelUrl = near(this.cfg.poseModelUrl);
    if (/lite/i.test(poseModelUrl)) {
      console.warn('[PoseCamera] WARN: ใช้ pose_landmarker_lite — ชุดท่าต้นแบบเก็บด้วย ' +
        'โมเดลความละเอียดเต็ม ความแม่นของคะแนนจะตกโดยไม่มี error บอก');
    }

    const { vision, fileset } = await this._loadVision();
    this.landmarker = await vision.PoseLandmarker.createFromOptions(fileset, {
      baseOptions: { modelAssetPath: poseModelUrl, delegate: 'GPU' },
      runningMode: 'VIDEO',
      numPoses: 1,
      minPoseDetectionConfidence: 0.5,
      minPosePresenceConfidence: 0.5,
      minTrackingConfidence: 0.5,
    });
    this._status('MediaPipe PoseLandmarker พร้อม');
    return this;
  }

  /** โหลด MediaPipe runtime — ของที่โฮสต์เองก่อน ไม่ได้ค่อยไป CDN */
  async _loadVision() {
    const attempts = [
      { mod: this.cfg.visionModule, wasm: this.cfg.wasmBase, label: 'โฮสต์เอง' },
      { mod: this.cfg.visionModuleFallback, wasm: this.cfg.wasmBaseFallback, label: 'CDN' },
    ].filter((a) => a.mod && a.wasm);

    let lastError;
    for (const a of attempts) {
      try {
        // module กับ wasm ต้องมาจากแหล่งเดียวกันเป็นคู่ ผสมข้ามเวอร์ชันแล้วพังแบบงง ๆ
        const vision = await import(/* @vite-ignore */ new URL(a.mod, import.meta.url).href);
        const fileset = await vision.FilesetResolver.forVisionTasks(
          new URL(a.wasm, import.meta.url).href);
        this._status(`MediaPipe runtime: ${a.label}`);
        return { vision, fileset };
      } catch (e) {
        lastError = e;
        console.warn(`[PoseCamera] โหลด MediaPipe จาก${a.label}ไม่ได้: ${e.message}`);
      }
    }
    throw new Error(`โหลด MediaPipe ไม่ได้เลยทั้งสองทาง — ${lastError?.message}`);
  }

  async start(videoEl) {
    this.video = videoEl ?? document.createElement('video');
    this.stream = await navigator.mediaDevices.getUserMedia({
      video: { width: this.cfg.width, height: this.cfg.height },
      audio: false,
    });
    this.video.srcObject = this.stream;
    this.video.playsInline = true;
    this.video.muted = true;
    await this.video.play();
    this._status(`กล้อง ${this.video.videoWidth}x${this.video.videoHeight}`);

    this.running = true;
    this.lastVideoTime = -1;
    this._loop();
    return this;
  }

  stop() {
    this.running = false;
    this.stream?.getTracks().forEach((t) => t.stop());
    this.stream = null;
  }

  _loop() {
    if (!this.running) return;
    // MediaPipe ไม่รับ timestamp ซ้ำ ถ้าเฟรมยังไม่เปลี่ยนให้ข้ามไปเลย
    if (this.video.currentTime !== this.lastVideoTime) {
      this.lastVideoTime = this.video.currentTime;
      try {
        const res = this.landmarker.detectForVideo(this._sourceFrame(), performance.now());
        this.onFrame?.(res?.landmarks?.[0] ?? null, performance.now());
      } catch (e) {
        console.error('[PoseCamera] detectForVideo ล้ม:', e);
      }
    }
    requestAnimationFrame(() => this._loop());
  }

  /**
   * ภาพที่ส่งให้ MediaPipe — พลิกซ้ายขวาแล้วถ้า cfg.mirror
   *
   * ต้องพลิกที่ตัวภาพ ไม่ใช่ที่พิกัดผลลัพธ์ เพราะ MediaPipe ตั้งชื่อ landmark ตาม
   * กายวิภาคที่มันมองเห็น พอภาพพลิก มันสลับ index ซ้าย/ขวาให้เองด้วย (11<->12,
   * 15<->16, ...) ไม่ใช่แค่ย้ายตำแหน่ง x
   *
   * ⚠ ห้ามเปลี่ยนไปใช้ x -> 1-x — วัดจริงแล้วต่างกัน 7-15 เท่า (ดู CLAUDE.md)
   *   สำหรับหน้าฝึกซ้อม การพลิกตรงนี้มีผลแค่ "ภาพที่ผู้เล่นเห็นเป็นเหมือนส่องกระจก"
   *   เพราะตัวให้คะแนนลองเทียบทั้งสองทิศอยู่แล้ว (ดู JabScorer.score)
   */
  _sourceFrame() {
    if (!this.cfg.mirror) return this.video;
    const w = this.video.videoWidth, h = this.video.videoHeight;
    if (!w || !h) return this.video;              // กล้องยังไม่พร้อม

    if (!this.mirrorCanvas) {
      this.mirrorCanvas = document.createElement('canvas');
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
}

export default PoseCamera;
