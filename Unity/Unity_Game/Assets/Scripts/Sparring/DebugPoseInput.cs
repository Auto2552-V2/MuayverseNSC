using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Sparring
{
    /// <summary>
    /// จำลองท่าผู้เล่นด้วยคีย์บอร์ด เพื่อทดสอบ game logic โดยไม่ต้องใช้กล้อง
    ///
    /// ─── ทำไมต้องมี ─────────────────────────────────────────────────────────────
    /// ถ้าทดสอบด้วยการชกจริงอย่างเดียว เวลาเกมทำงานผิดจะแยกไม่ออกว่าผิดที่ game logic
    /// หรือที่ detection ตัวนี้ตัด detection ออกจากสมการทั้งหมด
    ///
    /// และบางเคสชกจริงทดสอบยากมาก:
    ///   · combo jab->cross ต้องมาภายใน 1 วินาที — กดปุ่มคุมเวลาได้แม่นกว่าชก
    ///   · "ไม่ตอบเลยจนหมดเวลา" — แค่ไม่กด
    ///   · ท่าผิดเจาะจง เช่น ตอบ hook ตอน Enemy การ์ด guard1
    ///   · ทำซ้ำให้ได้ผลเดิมเป๊ะ ๆ ซึ่งการชกทำไม่ได้
    ///
    /// ─── ส่งเข้าทางเดียวกับของจริง ──────────────────────────────────────────────
    /// ยิง JSON หน้าตาเหมือน pose-detector.js ทุกประการเข้า PoseReceiver.OnPoseJson()
    /// ไม่ได้ลัดไปเรียก event ตรง ๆ — เพราะถ้าลัด ตัวกรอง confidence กับการนับ seq
    /// จะไม่ถูกทดสอบไปด้วย แล้วบั๊กในนั้นจะไปโผล่ตอนใช้กล้องจริง
    ///
    /// ตัวนี้ไม่ควรติดไปกับ build จริง — ตั้ง Enabled In Build = false ไว้ (ค่าเริ่มต้น)
    /// </summary>
    [RequireComponent(typeof(PoseReceiver))]
    public class DebugPoseInput : MonoBehaviour
    {
        [Serializable]
        public class KeyBinding
        {
            [Tooltip("ปุ่มที่กด")]
            public Key key;

            [Tooltip("ชื่อท่าที่จะส่ง — ต้องสะกดตรงกับที่โมเดลใช้ (ตัวพิมพ์เล็ก)")]
            public string pose;

            public KeyBinding(Key key, string pose) { this.key = key; this.pose = pose; }
        }

        [Header("ปุ่ม")]
        [SerializeField]
        private KeyBinding[] bindings =
        {
            new KeyBinding(Key.J, "jab"),
            new KeyBinding(Key.C, "cross"),
            new KeyBinding(Key.H, "hook"),
            new KeyBinding(Key.U, "uppercut"),
            new KeyBinding(Key.B, "block"),
        };

        [Header("ค่าที่ส่งไปกับท่า")]
        [Tooltip("ความมั่นใจที่จะแนบไปกับท่าที่กด — ตั้งต่ำกว่า Min Confidence ของ " +
                 "PoseReceiver เพื่อทดสอบว่าตัวกรองทำงานจริงไหม")]
        [SerializeField][Range(0f, 1f)] private float confidence = 0.95f;

        [Header("ปุ่มบนจอ")]
        [Tooltip("แสดงปุ่มกดด้วยเมาส์บนจอ Game\n\n" +
                 "มีไว้เพราะคีย์บอร์ดใช้ไม่ได้ถ้า Game view ไม่ได้ focus — Input System " +
                 "ตัด keyboard ทิ้งเมื่อหน้าต่างอยู่เบื้องหลัง (ค่า default ของ Background " +
                 "Behavior) ซึ่งเจอบ่อยมากตอนสลับไปคลิก Inspector แล้วกลับมากดปุ่ม\n" +
                 "ปุ่มเมาส์ไม่มีปัญหานี้")]
        [SerializeField] private bool showOnScreenButtons = true;

        [Tooltip("ขนาดปุ่มบนจอ (พิกเซล)")]
        [SerializeField] private Vector2 buttonSize = new Vector2(130, 46);

        [Header("เปิดใช้งาน")]
        [Tooltip("ปล่อยไม่ติ๊ก = ทำงานเฉพาะใน Editor เท่านั้น\n" +
                 "ติ๊ก = ติดไปกับ build ด้วย (ใช้ตอนอยากเทสต์บนเครื่องจริงโดยไม่ใช้กล้อง)")]
        [SerializeField] private bool enabledInBuild = false;

        [SerializeField] private bool logPresses = true;

        private PoseReceiver receiver;
        private int seq;

        private void Awake()
        {
            receiver = GetComponent<PoseReceiver>();

            // เช็คตอนรันแทน #if !UNITY_EDITOR — ถ้าใช้ #if ตัว enabledInBuild จะกลาย
            // เป็นฟิลด์ที่ไม่มีใครอ่านตอนคอมไพล์ใน Editor แล้วได้ warning CS0414 เปล่า ๆ
            if (!Application.isEditor && !enabledInBuild)
            {
                enabled = false;
                return;
            }

            if (logPresses) Debug.Log($"[DebugPoseInput] พร้อม — {BindingSummary()}");
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;      // ไม่มีคีย์บอร์ด (มือถือ) — ไม่ใช่ error

            foreach (KeyBinding b in bindings)
            {
                if (b == null || string.IsNullOrEmpty(b.pose)) continue;
                if (b.key == Key.None) continue;
                if (kb[b.key].wasPressedThisFrame) Send(b.pose);
            }
        }

        /// <summary>
        /// ปุ่มกดด้วยเมาส์บนจอ Game
        ///
        /// ใช้ OnGUI เพราะอยากให้ใช้ได้ทันทีโดยไม่ต้องไปสร้าง Canvas/Button ในซีน —
        /// เป็นเครื่องมือดีบักชั่วคราว ไม่ควรไปปนกับ UI จริงของเกม
        /// </summary>
        private void OnGUI()
        {
            if (!showOnScreenButtons || bindings == null) return;

            const float pad = 8f;
            float x = pad;
            float y = Screen.height - buttonSize.y - pad;

            var style = new GUIStyle(GUI.skin.button) { fontSize = 16 };
            foreach (KeyBinding b in bindings)
            {
                if (b == null || string.IsNullOrEmpty(b.pose)) continue;
                string label = b.key == Key.None ? b.pose : $"{b.pose} ({b.key})";
                if (GUI.Button(new Rect(x, y, buttonSize.x, buttonSize.y), label, style))
                {
                    Send(b.pose);
                }
                x += buttonSize.x + pad;
            }
        }

        /// <summary>ส่งท่าหนึ่งท่าเข้าระบบ เรียกจากปุ่ม UI หรือสคริปต์เทสต์ได้ด้วย</summary>
        public void Send(string pose)
        {
            // ประกอบ JSON เองแทน JsonUtility.ToJson เพื่อให้เห็นชัดว่ารูปแบบตรงกับ
            // ฝั่งเว็บ และเพื่อให้แก้ตามได้ทันทีถ้าฝั่งเว็บเพิ่ม field
            string json = $"{{\"pose\":\"{pose}\",\"confidence\":{confidence:F4}," +
                          $"\"seq\":{++seq},\"t\":{(long)(Time.realtimeSinceStartup * 1000)}}}";

            if (logPresses) Debug.Log($"[DebugPoseInput] ส่ง {pose} conf {confidence:F2}");
            receiver.OnPoseJson(json);
        }

        private string BindingSummary()
        {
            var parts = new System.Text.StringBuilder();
            foreach (KeyBinding b in bindings)
            {
                if (b == null || string.IsNullOrEmpty(b.pose)) continue;
                if (parts.Length > 0) parts.Append("  ");
                parts.Append($"{b.key}={b.pose}");
            }
            return parts.ToString();
        }
    }
}
