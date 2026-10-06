/* ---------------------------------------------------------------------------------------------
 * pose-bridge.js — แทนที่ game_bridge.py สำหรับ WebGL
 *
 * บนเดสก์ท็อป: Python เปิดกล้อง → คำนวณท่า → ยิง JSON เข้า Unity ทาง UDP
 * บนเว็บ:      ไฟล์นี้เปิดกล้อง → คำนวณท่า → ยิง JSON เข้า Unity ทาง SendMessage
 *
 * JSON ที่ส่งต้องหน้าตาเหมือน game_bridge.py เป๊ะ ๆ เพราะปลายทางคือ PoseUdpReceiver.OnPoseJson()
 * ซึ่งโยนเข้าคิวเดียวกับฝั่ง UDP — logic dedup / attack binding / PoseBodyDriver ใช้ตัวเดิมหมด
 *
 * ─── ทำไมต้องอยู่ในหน้าเว็บ ไม่ใช่ในเซิร์ฟเวอร์ ───────────────────────────────────────
 * กล้องกับ MediaPipe รันบนเครื่องคนเล่นทั้งหมด เซิร์ฟเวอร์ไม่ต้องประมวลผลอะไรเลย
 * สำคัญมากเพราะเครื่องของงานเป็น CPU ล้วน (คู่มือข้อ 16) และ frontend ได้แค่ 2 CPU
 * ภาพจากกล้องไม่ออกจากเครื่องคนเล่นด้วย
 *
 * ─── ไฟล์ที่ต้องมี ─────────────────────────────────────────────────────────────────
 * โมเดล pose ต้องวางที่  frontend/public/models/pose_landmarker_lite.task
 * เรียกด้วย path แบบไม่มี host ตามกติกาข้อ 6 — ย้ายโดเมนแล้วไม่ต้อง build ใหม่
 * ------------------------------------------------------------------------------------------- */

(function () {
  "use strict";

  const CFG = {
    // ชื่อ GameObject ในซีน SampleScene ที่มี PoseUdpReceiver แปะอยู่
    unityObject: "PoseReceiver",
    unityMethod: "OnPoseJson",

    // same-origin เสมอ ห้าม hardcode host (คู่มือข้อ 6)
    modelUrl: "/models/pose_landmarker_lite.task",

    // WASM ของ MediaPipe — ถ้าเน็ตงานบล็อก CDN ให้โหลดมาวางที่ /mediapipe/ แล้วแก้เป็น "/mediapipe"
    wasmBase: "https://cdn.jsdelivr.net/npm/@mediapipe/tasks-vision@0.10.14/wasm",
    visionModule: "https://cdn.jsdelivr.net/npm/@mediapipe/tasks-vision@0.10.14",

    stateHz: 30,        // ความถี่ที่ส่ง state — เกินนี้ Unity ก็ทิ้งของเก่าอยู่ดี
    mirror: true,       // กลับซ้ายขวาให้เหมือนส่องกระจก
  };

  // ดัชนี landmark ของ MediaPipe Pose (33 จุด)
  const L = {
    shoulderL: 11, shoulderR: 12, elbowL: 13, elbowR: 14, wristL: 15, wristR: 16,
    hipL: 23, hipR: 24, kneeL: 25, kneeR: 26, ankleL: 27, ankleR: 28,
  };

  let seq = 0;
  let eventId = 0;
  let lastStateAt = 0;
  const punchState = { left: null, right: null };

  // ── ตัวช่วยเรขาคณิต ────────────────────────────────────────────────────────────────
  const mid = (a, b) => ({ x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 });
  const dist = (a, b) => Math.hypot(a.x - b.x, a.y - b.y);

  // มุมที่จุด b ระหว่างแขน ba กับ bc — องศา
  function angleAt(a, b, c) {
    const v1x = a.x - b.x, v1y = a.y - b.y;
    const v2x = c.x - b.x, v2y = c.y - b.y;
    const d = Math.hypot(v1x, v1y) * Math.hypot(v2x, v2y);
    if (d < 1e-6) return 0;
    const cos = Math.min(1, Math.max(-1, (v1x * v2x + v1y * v2y) / d));
    return (Math.acos(cos) * 180) / Math.PI;
  }

  // มุมกางแขนเทียบแนวลำตัว: 0 = แขนแนบข้าง, 90 = ระดับไหล่, 180 = ชูสุด
  function abduction(shoulder, elbow, hipMid, shoulderMid) {
    const trunk = { x: hipMid.x - shoulderMid.x, y: hipMid.y - shoulderMid.y };
    const arm = { x: elbow.x - shoulder.x, y: elbow.y - shoulder.y };
    const d = Math.hypot(trunk.x, trunk.y) * Math.hypot(arm.x, arm.y);
    if (d < 1e-6) return 0;
    const cos = Math.min(1, Math.max(-1, (trunk.x * arm.x + trunk.y * arm.y) / d));
    return (Math.acos(cos) * 180) / Math.PI;
  }

  // ── แปลง landmark เป็น payload ตามสัญญาของ PoseUdpReceiver ────────────────────────
  function buildState(lm, nowMs) {
    const shoulderMid = mid(lm[L.shoulderL], lm[L.shoulderR]);
    const hipMid = mid(lm[L.hipL], lm[L.hipR]);

    // torso length เอาไว้ normalize ทุกอย่าง — คนยืนใกล้/ไกลกล้องจะได้ค่าเท่ากัน
    const torso = Math.max(1e-3, dist(shoulderMid, hipMid));

    // ลำตัวเอียงกี่องศาจากแนวดิ่ง เครื่องหมาย + = เอียงไปทาง +x บนจอ
    const lean = (Math.atan2(shoulderMid.x - hipMid.x, hipMid.y - shoulderMid.y) * 180) / Math.PI;

    const shoulderAbd = {
      left: abduction(lm[L.shoulderL], lm[L.elbowL], hipMid, shoulderMid),
      right: abduction(lm[L.shoulderR], lm[L.elbowR], hipMid, shoulderMid),
    };
    const elbowAng = {
      left: angleAt(lm[L.shoulderL], lm[L.elbowL], lm[L.wristL]),
      right: angleAt(lm[L.shoulderR], lm[L.elbowR], lm[L.wristR]),
    };
    const hipAbd = {
      left: angleAt(lm[L.shoulderL], lm[L.hipL], lm[L.kneeL]),
      right: angleAt(lm[L.shoulderR], lm[L.hipR], lm[L.kneeR]),
    };

    // hand aim ในหน่วย torso-length — ตรงกับที่ PoseHandPoint คาดไว้
    const hand = (wrist, shoulder) => {
      const ax = (wrist.x - shoulder.x) / torso;
      const ay = (shoulder.y - wrist.y) / torso;   // +y = ยกขึ้น
      return { x: wrist.x, y: wrist.y, ax, ay, reach: Math.hypot(ax, ay) };
    };
    const hands = {
      left: hand(lm[L.wristL], lm[L.shoulderL]),
      right: hand(lm[L.wristR], lm[L.shoulderR]),
    };

    // "armed" = ยกแขน/ขาขึ้นมาพร้อมออกอาวุธแล้ว ใช้เกณฑ์เดียวกับที่ bridge เดิมใช้คร่าว ๆ
    const armed = {
      punch_left: hands.left.reach > 0.9,
      punch_right: hands.right.reach > 0.9,
      elbow_left: elbowAng.left < 70 && shoulderAbd.left > 60,
      elbow_right: elbowAng.right < 70 && shoulderAbd.right > 60,
      kick_left: hipAbd.left < 140,
      kick_right: hipAbd.right < 140,
    };

    // จุดไหนกล้องมองไม่เห็นชัด ปล่อยให้เกมรู้ว่าอย่าเชื่อค่าฝั่งนั้น
    const seen = (i) => (lm[i].visibility === undefined ? true : lm[i].visibility > 0.5);
    const limbOk = {
      arm_left: seen(L.shoulderL) && seen(L.elbowL) && seen(L.wristL),
      arm_right: seen(L.shoulderR) && seen(L.elbowR) && seen(L.wristR),
      leg_left: seen(L.hipL) && seen(L.kneeL) && seen(L.ankleL),
      leg_right: seen(L.hipR) && seen(L.kneeR) && seen(L.ankleR),
    };

    return {
      payload: {
        pose_detected: true,
        shoulder_abduction: shoulderAbd,
        elbow: elbowAng,
        hip_abduction: hipAbd,
        alpha: Math.abs(lean),          // ความเอียงลำตัว = การโกงท่า
        beta: 0,
        armed,
        limb_ok: limbOk,
        body: {
          center_x: hipMid.x * 2 - 1,   // 0..1 -> -1..+1 ตามที่ PoseBody ระบุ
          center_y: hipMid.y * 2 - 1,
          lean,
          scale: torso,
        },
        hands,
        gesture: { left: -1, right: -1 },
      },
      torso,
      hands,
      elbowAng,
      lean,
    };
  }

  // ตรวจหมัด: แขนเหยียดออกเร็วแล้วหยุด = หนึ่งหมัด ส่งเป็น event ให้ Unity สั่งต่อย
  // ของจริงใน game_bridge.py ละเอียดกว่านี้มาก อันนี้เป็นรุ่นแรกให้เล่นได้ก่อน
  function detectPunch(side, reach, elbowDeg, lean, nowMs, out) {
    const st = punchState[side] || (punchState[side] = { phase: "idle", peak: 0, at: 0 });

    if (st.phase === "idle") {
      if (reach > 1.0 && elbowDeg > 150) {          // เหยียดสุด = เริ่มหมัด
        st.phase = "extended";
        st.peak = reach;
        st.at = nowMs;
      }
    } else if (st.phase === "extended") {
      st.peak = Math.max(st.peak, reach);
      if (reach < 0.8) {                            // ดึงแขนกลับ = จบหมัด
        st.phase = "idle";
        if (nowMs - st.at < 900) {                  // ช้ากว่านี้ถือว่าแค่ยืดแขนเล่น
          const alpha = Math.abs(lean);
          out.push({
            type: "event",
            seq: seq++,
            t_ms: Math.round(nowMs),
            payload: {
              event_id: ++eventId,
              move: side === "left" ? "punch_left" : "punch_right",
              side,
              peak_rom: st.peak * 100,
              alpha,
              beta: 0,
              // โกงด้วยการเอียงตัวช่วย = คุณภาพตก ตรงกับแนวคิดของ bridge เดิม
              quality: alpha < 8 ? "perfect" : alpha < 18 ? "good" : "poor",
            },
          });
        }
      }
    }
  }

  // ── ส่งเข้า Unity ─────────────────────────────────────────────────────────────────
  function send(msg) {
    const u = window.unityInstance;
    if (!u) return;                                  // เกมยังโหลดไม่เสร็จ ทิ้งเฟรมนี้ไป
    try {
      u.SendMessage(CFG.unityObject, CFG.unityMethod, JSON.stringify(msg));
    } catch (e) {
      // GameObject ไม่มีในซีนปัจจุบัน (เช่นยังอยู่หน้า FrontendAPP) — ไม่ใช่เรื่องผิดปกติ
    }
  }

  // ── เริ่มทำงาน ────────────────────────────────────────────────────────────────────
  async function start(videoEl, onStatus) {
    const say = onStatus || function () {};

    say("กำลังขอสิทธิ์ใช้กล้อง…");
    let stream;
    try {
      stream = await navigator.mediaDevices.getUserMedia({
        video: { width: { ideal: 640 }, height: { ideal: 480 }, facingMode: "user" },
        audio: false,
      });
    } catch (e) {
      say("เปิดกล้องไม่ได้: " + e.name + " — เกมยังเล่นได้แต่ตรวจท่าไม่ได้");
      return null;
    }
    videoEl.srcObject = stream;
    await videoEl.play();

    say("กำลังโหลดโมเดลตรวจท่า…");
    let PoseLandmarker, FilesetResolver;
    try {
      ({ PoseLandmarker, FilesetResolver } = await import(CFG.visionModule));
    } catch (e) {
      say("โหลด MediaPipe ไม่ได้ (เน็ตบล็อก CDN?) — ดู CFG.wasmBase ใน pose-bridge.js");
      return null;
    }

    let landmarker;
    try {
      const fileset = await FilesetResolver.forVisionTasks(CFG.wasmBase);
      landmarker = await PoseLandmarker.createFromOptions(fileset, {
        baseOptions: { modelAssetPath: CFG.modelUrl },
        runningMode: "VIDEO",
        numPoses: 1,
      });
    } catch (e) {
      say("โหลดโมเดลไม่ได้ — วางไฟล์ที่ frontend/public/models/pose_landmarker_lite.task หรือยัง");
      return null;
    }

    say("");
    const minGap = 1000 / CFG.stateHz;

    function loop() {
      requestAnimationFrame(loop);
      if (videoEl.readyState < 2) return;

      const now = performance.now();
      if (now - lastStateAt < minGap) return;
      lastStateAt = now;

      let res;
      try { res = landmarker.detectForVideo(videoEl, now); } catch (e) { return; }

      const lms = res && res.landmarks && res.landmarks[0];
      if (!lms || lms.length < 29) {
        send({ type: "state", seq: seq++, t_ms: Math.round(now),
               payload: { pose_detected: false } });
        return;
      }

      // กลับด้านให้เหมือนส่องกระจก ทำก่อนคำนวณทุกอย่าง ค่าที่ออกไปจะได้ตรงกับที่คนเห็น
      const lm = CFG.mirror ? lms.map((p) => ({ ...p, x: 1 - p.x })) : lms;

      const built = buildState(lm, now);
      send({ type: "state", seq: seq++, t_ms: Math.round(now), payload: built.payload });

      const events = [];
      detectPunch("left", built.hands.left.reach, built.elbowAng.left, built.lean, now, events);
      detectPunch("right", built.hands.right.reach, built.elbowAng.right, built.lean, now, events);
      events.forEach(send);
    }

    loop();
    return { stop: () => stream.getTracks().forEach((t) => t.stop()) };
  }

  window.TictaPose = { start, CFG };
})();
