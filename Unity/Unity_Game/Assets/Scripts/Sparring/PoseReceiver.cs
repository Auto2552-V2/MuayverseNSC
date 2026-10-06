using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace Sparring
{
    /// <summary>
    /// ท่าหนึ่งท่าที่ AI detection ส่งมา — ต้องตรงกับ JSON ที่ pose-detector.js ยิงออก
    /// ชื่อ field ต้องสะกดตรงกับ key ใน JSON เป๊ะ ๆ ไม่งั้น JsonUtility จะใส่ค่า default
    /// ให้เงียบ ๆ (pose = null, confidence = 0) โดยไม่มี error บอก
    /// </summary>
    [Serializable]
    public class PoseMessage
    {
        public string type;        // "pose" | "status" | "live" | "frame"
        public string pose;        // jab | cross | hook | uppercut | block
        public float confidence;   // 0..1
        public int seq;            // นับขึ้นทีละ 1 ใช้ตรวจว่าข้อความหายระหว่างทางไหม
        public long t;             // performance.now() ฝั่งเว็บ (ms) ใช้วัด latency คร่าว ๆ

        // เฉพาะ type = "status"
        public string state;       // ready | nocamera | nopose | pose-ok | error
        public string detail;

        // เฉพาะ type = "frame" — ภาพกล้องย่อส่วน JPEG เข้ารหัส base64
        public int w, h;
        public string data;
    }

    /// <summary>
    /// รับท่าจาก AI detection ที่รันในหน้าเว็บ แล้วส่งต่อให้ระบบเกมบน main thread
    ///
    /// ─── ทำไมต้องมีคิว ───────────────────────────────────────────────────────────
    /// ข้อความเข้ามาจาก callback ของ WebView ซึ่งไม่รับประกันว่าอยู่บน main thread
    /// แตะ Unity API นอก main thread = พังแบบสุ่ม หาสาเหตุยาก จึงรับเข้าคิวอย่างเดียว
    /// แล้วค่อยแกะใน Update()
    ///
    /// ใช้ ConcurrentQueue ธรรมดา ไม่ต้องมี MainThreadDispatcher — รูปแบบเดียวกับที่
    /// PoseUdpReceiver.cs ในโปรเจกต์นี้ใช้อยู่แล้ว
    ///
    /// ─── ช่องทางที่รับได้ ────────────────────────────────────────────────────────
    /// ทุกช่องทางลงเมธอด OnPoseJson() เหมือนกันหมด logic จึงมีชุดเดียว
    ///   · Android  : gree/unity-webview เรียก WebViewObject callback -> OnPoseJson()
    ///   · WebGL    : unityInstance.SendMessage("PoseReceiver", "OnPoseJson", json)
    ///   · ทดสอบ    : เรียก OnPoseJson() ตรง ๆ จากสคริปต์อื่น
    ///
    /// GameObject ที่ถือสคริปต์นี้ต้องชื่อ "PoseReceiver" ให้ตรงกับที่ฝั่ง JS เรียก
    /// ไม่งั้น SendMessage จะเงียบหายไปเฉย ๆ ไม่มี error (เฉพาะทาง WebGL)
    /// </summary>
    public class PoseReceiver : MonoBehaviour
    {
        [Header("การกรอง")]
        [Tooltip("ความมั่นใจขั้นต่ำที่จะรับท่านั้น — ต่ำกว่านี้ทิ้งทันที\n" +
                 "ฝั่งเว็บกรองมาชั้นหนึ่งแล้ว ตรงนี้เป็นด่านสองเผื่อฝั่งเว็บถูกแก้ค่า")]
        [SerializeField][Range(0f, 1f)] private float minConfidence = 0.7f;

        [Tooltip("ชื่อท่าที่ยอมรับ — ท่านอกรายการนี้ทิ้ง\n" +
                 "กัน idle กับท่าที่เพิ่มในโมเดลภายหลังหลุดเข้ามาเป็น input ของเกม")]
        [SerializeField]
        private string[] acceptedPoses = { "jab", "cross", "hook", "uppercut", "block" };

        [Header("ดีบัก")]
        [Tooltip("พิมพ์ทุกท่าที่รับได้และที่ถูกทิ้งลง Console")]
        [SerializeField] private bool logEvents = true;

        [Tooltip("เตือนเมื่อเลข seq ข้าม = มีข้อความหายระหว่างทาง\n" +
                 "ปิดได้ถ้ารู้อยู่แล้วว่าหน้าเว็บถูกรีโหลดบ่อย")]
        [SerializeField] private bool warnOnGaps = true;

        /// <summary>ยิงบน main thread ทุกครั้งที่มีท่าผ่านการกรอง</summary>
        public event Action<string, float> OnPose;

        /// <summary>
        /// สถานะจากฝั่งเว็บ (state, detail)
        ///   ready    = กล้องกับโมเดลพร้อม เริ่มเล่นได้
        ///   nocamera = เปิดกล้องไม่ได้ หรือกล้องหลุดกลางทาง
        ///   nopose   = กล้องทำงานแต่ไม่เห็นคนในเฟรม
        ///   pose-ok  = กลับมาเห็นคนแล้ว
        ///   error    = อย่างอื่นพัง (ดู detail)
        ///
        /// ตอนเล่นจริง WebView ถูกซ่อน ผู้เล่นมองไม่เห็นหน้าเว็บ UI จึงต้องพึ่ง
        /// สัญญาณนี้ในการบอกว่า "ยืนให้กล้องเห็นก่อน" แทนที่จะปล่อยให้เงียบ
        /// </summary>
        public event Action<string, string> OnDetectionStatus;

        /// <summary>
        /// ท่าที่โมเดลเห็นอยู่ "ตอนนี้" (pose, confidence) — ยังไม่ผ่านตัวกรอง
        ///
        /// ใช้โชว์บนจอให้ผู้เล่นรู้ว่าระบบอ่านท่าตนว่าอะไร ห้ามเอาไปตัดสินเกม
        /// pose เป็น "" เมื่อโมเดลยังอ่านไม่ออก
        /// </summary>
        public event Action<string, float> OnLivePose;

        public string LivePose { get; private set; } = "";
        public float LiveConfidence { get; private set; }

        /// <summary>สถานะล่าสุดที่ได้จากฝั่งเว็บ ("" = ยังไม่เคยได้)</summary>
        public string DetectionState { get; private set; } = "";

        /// <summary>true เมื่อฝั่งเว็บบอกว่าพร้อมและยังไม่มีปัญหากล้อง</summary>
        public bool DetectionReady =>
            DetectionState == "ready" || DetectionState == "pose-ok" || DetectionState == "nopose";

        /// <summary>ท่าล่าสุดที่ผ่านการกรอง (ว่างถ้ายังไม่เคยได้)</summary>
        public string LastPose { get; private set; } = "";
        public float LastConfidence { get; private set; }

        // สถิติ ดูได้จาก Inspector ตอนรัน ใช้ตอบคำถาม "มันรับอะไรอยู่หรือเปล่า"
        public int AcceptedCount { get; private set; }
        public int DroppedCount { get; private set; }
        public int MalformedCount { get; private set; }
        public int GapCount { get; private set; }

        /// <summary>ภาพกล้องล่าสุด (null ถ้ายังไม่เคยได้) — ใช้ซ้ำ อย่า Destroy เอง</summary>
        public Texture2D CameraTexture { get; private set; }

        /// <summary>ยิงทุกครั้งที่ได้ภาพใหม่ — PoseCameraPreview เอาไปแสดง</summary>
        public event Action<Texture2D> OnCameraFrame;

        public int FramesReceived { get; private set; }

        private readonly ConcurrentQueue<string> inbox = new ConcurrentQueue<string>();
        private string pendingFrame;
        private int lastSeq;

        // ──────────────────────────────────────────────────────────────────────
        // ทางเข้า — เรียกได้จาก thread ไหนก็ได้
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// จุดรับข้อความเดียวของระบบ ปลอดภัยกับทุก thread
        ///
        /// ห้ามแตะ Unity API ในเมธอดนี้เด็ดขาด (รวมถึง Debug.Log) — เข้าคิวอย่างเดียว
        /// แล้วให้ Update() จัดการ
        /// </summary>
        public void OnPoseJson(string json)
        {
            if (!string.IsNullOrEmpty(json)) inbox.Enqueue(json);
        }

        /// <summary>ชื่อพ้อง เผื่อฝั่งเว็บเรียกด้วยชื่อนี้</summary>
        public void ReceiveJson(string json) => OnPoseJson(json);

        // ──────────────────────────────────────────────────────────────────────
        // main thread
        // ──────────────────────────────────────────────────────────────────────

        private void Update()
        {
            // แกะทั้งคิวในเฟรมเดียว ไม่จำกัดจำนวน — ท่าเข้ามาไม่กี่ครั้งต่อวินาที
            // ถ้าจำกัดแล้วมีท่าค้างคิว มันจะไปโผล่ในรอบถัดไปซึ่งผิดจังหวะเกม
            while (inbox.TryDequeue(out string json)) Handle(json);

            // ถอดรหัสภาพแค่เฟรมล่าสุดเฟรมเดียวต่อหนึ่ง Update
            //
            // ถ้าถอดทุกเฟรมที่ค้างในคิว เวลาเกมกระตุกแล้วภาพกองกัน เราจะเสียเวลา
            // ถอดภาพเก่าที่ไม่มีใครได้เห็นอยู่ดี แล้วยิ่งกระตุกหนักเข้าไปอีก
            if (pendingFrame != null)
            {
                DecodeFrame(pendingFrame);
                pendingFrame = null;
            }
        }

        /// <summary>แปลง base64 JPEG เป็น Texture2D — ทำบน main thread เท่านั้น</summary>
        private void DecodeFrame(string base64)
        {
            try
            {
                byte[] jpeg = Convert.FromBase64String(base64);

                // สร้าง Texture ครั้งเดียวแล้วใช้ซ้ำ — LoadImage ปรับขนาดให้เองตามไฟล์
                // ถ้าสร้างใหม่ทุกเฟรมจะรั่วจนหน่วยความจำกราฟิกหมดในไม่กี่นาที
                if (CameraTexture == null)
                {
                    CameraTexture = new Texture2D(2, 2, TextureFormat.RGB24, false);
                }
                if (CameraTexture.LoadImage(jpeg))
                {
                    FramesReceived++;
                    OnCameraFrame?.Invoke(CameraTexture);
                }
                else if (logEvents)
                {
                    Debug.LogWarning("[PoseReceiver] ถอดรหัสภาพกล้องไม่สำเร็จ");
                }
            }
            catch (Exception e)
            {
                if (logEvents) Debug.LogWarning($"[PoseReceiver] ภาพกล้องพัง: {e.Message}");
            }
        }

        private void Handle(string json)
        {
            PoseMessage msg;
            try { msg = JsonUtility.FromJson<PoseMessage>(json); }
            catch (Exception e)
            {
                MalformedCount++;
                if (logEvents) Debug.LogWarning($"[PoseReceiver] JSON พัง: {e.Message} | {json}");
                return;
            }

            if (msg == null)
            {
                MalformedCount++;
                if (logEvents) Debug.LogWarning($"[PoseReceiver] แปลง JSON ไม่ได้: {json}");
                return;
            }

            // ภาพกล้อง — เก็บแค่เฟรมล่าสุด ของเก่าทิ้งได้เลยเพราะไม่มีใครได้เห็น
            if (msg.type == "frame")
            {
                if (!string.IsNullOrEmpty(msg.data)) pendingFrame = msg.data;
                return;
            }

            // "live" = ท่าที่โมเดลเห็นตอนนี้ ยังไม่ผ่านตัวกรอง ยังไม่นับเป็นการตอบ
            // มีไว้ให้ UI โชว์ว่าระบบอ่านท่าผู้เล่นว่าอะไรแบบ realtime เท่านั้น
            // ไม่นับ seq เพราะไม่มีผลต่อเกม ถ้าหายไปบ้างก็ไม่เป็นไร
            if (msg.type == "live")
            {
                LivePose = msg.pose ?? "";
                LiveConfidence = msg.confidence;
                OnLivePose?.Invoke(LivePose, msg.confidence);
                return;
            }

            // ข้อความสถานะเดินคนละทางกับท่า — ไม่ผ่านตัวกรอง confidence และไม่นับเป็นท่า
            if (msg.type == "status")
            {
                TrackSeq(msg.seq);
                DetectionState = msg.state ?? "";
                if (logEvents) Debug.Log($"[PoseReceiver] สถานะ: {msg.state} {msg.detail}");
                OnDetectionStatus?.Invoke(msg.state ?? "", msg.detail ?? "");
                return;
            }

            if (string.IsNullOrEmpty(msg.pose))
            {
                MalformedCount++;
                if (logEvents) Debug.LogWarning($"[PoseReceiver] ไม่มีช่อง pose ใน: {json}");
                return;
            }

            TrackSeq(msg.seq);

            if (msg.confidence < minConfidence)
            {
                DroppedCount++;
                if (logEvents)
                {
                    Debug.Log($"[PoseReceiver] ทิ้ง {msg.pose} conf {msg.confidence:F3} " +
                              $"< {minConfidence:F2}");
                }
                return;
            }

            if (!IsAccepted(msg.pose))
            {
                DroppedCount++;
                if (logEvents) Debug.Log($"[PoseReceiver] ทิ้งท่านอกรายการ: {msg.pose}");
                return;
            }

            AcceptedCount++;
            LastPose = msg.pose;
            LastConfidence = msg.confidence;

            if (logEvents)
            {
                Debug.Log($"[PoseReceiver] รับ {msg.pose} conf {msg.confidence:F3} seq {msg.seq}");
            }

            OnPose?.Invoke(msg.pose, msg.confidence);
        }

        /// <summary>
        /// เฝ้าดูเลขลำดับเพื่อจับว่ามีข้อความหายระหว่างทางไหม
        ///
        /// ข้อความหายไม่ร้ายแรงพอจะหยุดเกม แต่ต้องรู้ เพราะอาการปลายทางคือ
        /// "เกมไม่ตอบสนองเป็นบางครั้ง" ซึ่งหาสาเหตุยากมากถ้าไม่มีสัญญาณนี้
        /// นับรวมทั้งข้อความท่าและสถานะ เพราะทั้งสองใช้ลำดับเดียวกันฝั่งเว็บ
        /// </summary>
        private void TrackSeq(int seq)
        {
            if (seq <= 0) return;

            if (lastSeq > 0 && seq > lastSeq + 1)
            {
                GapCount += seq - lastSeq - 1;
                if (warnOnGaps)
                {
                    Debug.LogWarning($"[PoseReceiver] ข้อความหาย {seq - lastSeq - 1} ชิ้น " +
                                     $"(seq {lastSeq} -> {seq})");
                }
            }
            // seq ย้อนกลับ = หน้าเว็บโหลดใหม่ ไม่ใช่ของหาย ตั้งต้นนับใหม่
            if (seq < lastSeq && logEvents)
            {
                Debug.Log($"[PoseReceiver] seq ย้อนกลับ ({lastSeq} -> {seq}) — หน้าเว็บโหลดใหม่");
            }
            lastSeq = seq;
        }

        private bool IsAccepted(string pose)
        {
            if (acceptedPoses == null || acceptedPoses.Length == 0) return true;
            foreach (string p in acceptedPoses)
            {
                if (string.Equals(p, pose, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
