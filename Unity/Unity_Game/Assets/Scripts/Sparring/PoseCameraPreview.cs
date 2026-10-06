using UnityEngine;
using UnityEngine.UI;

namespace Sparring
{
    /// <summary>
    /// แสดงภาพกล้องที่ส่งมาจากหน้าเว็บ ลงบน RawImage ในซีน
    ///
    /// ─── ทำไมไม่ใช้ WebCamTexture ───────────────────────────────────────────────
    /// บน Android กล้องเปิดได้ที่เดียว และ WebView เป็นคนถืออยู่แล้ว ถ้า Unity
    /// ไปเปิดซ้ำ ฝ่ายใดฝ่ายหนึ่งจะไม่ได้ภาพ — detection สำคัญกว่า preview
    /// จึงให้หน้าเว็บเป็นเจ้าของกล้องแต่ผู้เดียว แล้วส่งภาพย่อมาให้วาด
    ///
    /// ─── ภาพที่ได้คือภาพที่โมเดลเห็นจริง ──────────────────────────────────────
    /// หน้าเว็บส่งภาพ "หลังพลิกกระจกแล้ว" มา ตรงกับที่ MediaPipe อ่าน ผู้เล่นจึง
    /// เห็นแบบส่องกระจก และถ้าท่าอ่านเพี้ยนก็เทียบกับภาพนี้ได้ตรง ๆ
    /// **อย่าใส่ scaleX(-1) ซ้ำที่นี่** ไม่งั้นจะพลิกสองรอบกลับไปไม่ตรงกับโมเดล
    ///
    /// ─── ต้องเปิดฝั่งเว็บด้วย ────────────────────────────────────────────────────
    /// ตั้ง sendPreviewToUnity: true ใน pose-detector.js (game.html เปิดไว้แล้ว)
    /// ถ้าไม่เปิด จะไม่มีภาพส่งมาเลยและช่องนี้จะว่าง
    /// </summary>
    public class PoseCameraPreview : MonoBehaviour
    {
        [Header("ต่อสาย")]
        [Tooltip("เว้นว่างได้ — จะไปหา PoseReceiver ในซีนให้เอง")]
        [SerializeField] private PoseReceiver poseReceiver;

        [Tooltip("RawImage ที่จะใช้วาดภาพกล้อง (สร้างใน Canvas)")]
        [SerializeField] private RawImage target;

        [Header("พฤติกรรม")]
        [Tooltip("ซ่อน RawImage ไว้จนกว่าจะได้ภาพแรก — กันกรอบดำโล่ง ๆ ตอนกล้องยังไม่มา")]
        [SerializeField] private bool hideUntilFirstFrame = true;

        [Tooltip("ไม่ได้ภาพใหม่นานกี่วินาทีถึงถือว่ากล้องหยุดส่ง แล้วซ่อนทิ้ง\n" +
                 "0 = ไม่ต้องซ่อน ค้างภาพสุดท้ายไว้")]
        [SerializeField][Min(0f)] private float hideAfterSilentSeconds = 3f;

        [Tooltip("ปรับสัดส่วนกรอบตามภาพที่ได้จริง — กันภาพยืด\n" +
                 "ต้องมี Aspect Ratio Fitter บน RawImage ถึงจะมีผล")]
        [SerializeField] private AspectRatioFitter aspectFitter;

        [Header("ตำแหน่งตอนรัน")]
        [Tooltip("ติ๊ก = บังคับตำแหน่งตอนเริ่มเล่น\n" +
                 "มีไว้เพราะ Layout Group หรือ Aspect Ratio Fitter บน parent " +
                 "อาจย้ายกรอบตอนรัน ทำให้ตำแหน่งที่จัดไว้ใน Scene ไม่ตรง")]
        [SerializeField] private bool overridePosition = true;

        [Tooltip("ตำแหน่งที่จะบังคับ (Anchored Position)")]
        [SerializeField] private Vector2 anchoredPosition = new Vector2(-324f, -1078f);

        private float lastFrameAt = -999f;
        private bool shown;

        private void Awake()
        {
            if (poseReceiver == null) poseReceiver = FindFirstObjectByType<PoseReceiver>();
            if (target == null) target = GetComponent<RawImage>();
        }

        private void OnEnable()
        {
            if (poseReceiver == null)
            {
                Debug.LogError($"[{name}] ไม่เจอ PoseReceiver — ไม่มีภาพกล้องให้แสดง", this);
                enabled = false;
                return;
            }
            if (target == null)
            {
                Debug.LogError($"[{name}] ต้องลาก RawImage ใส่ช่อง Target", this);
                enabled = false;
                return;
            }

            poseReceiver.OnCameraFrame += HandleFrame;

            if (overridePosition)
            {
                // ติ๊กไว้แต่ค่าเป็นศูนย์ มักแปลว่าลืมกรอก — แล้วผลคือกรอบวิ่งไปมุมกลางจอ
                // ซึ่งดูเหมือน "สคริปต์ไม่ทำงาน" ทั้งที่มันทำงานแล้วแต่ย้ายไปที่ผิด
                if (anchoredPosition == Vector2.zero)
                {
                    Debug.LogWarning($"[{name}] Override Position ติ๊กไว้แต่ค่าเป็น (0,0) " +
                                     "— ถ้าไม่ได้ตั้งใจ ให้กรอกตำแหน่ง หรือเอาติ๊กออก" +
                                     "แล้วไปตั้งที่ Rect Transform แทน", this);
                }
                target.rectTransform.anchoredPosition = anchoredPosition;
            }

            if (hideUntilFirstFrame)
            {
                target.enabled = false;
                shown = false;
            }
        }

        private void OnDisable()
        {
            if (poseReceiver != null) poseReceiver.OnCameraFrame -= HandleFrame;
        }

        private void Update()
        {
            if (hideAfterSilentSeconds <= 0f || !shown) return;
            if (Time.time - lastFrameAt > hideAfterSilentSeconds)
            {
                // กล้องเงียบไปแล้ว ซ่อนดีกว่าค้างภาพเก่าไว้ให้เข้าใจผิดว่ายังทำงานอยู่
                target.enabled = false;
                shown = false;
            }
        }

        private void HandleFrame(Texture2D tex)
        {
            lastFrameAt = Time.time;

            // ตั้งทุกเฟรม ไม่ใช่ครั้งเดียว — Texture2D.LoadImage สร้าง texture ใหม่
            // ภายในได้เมื่อขนาดภาพเปลี่ยน ถ้าตั้งครั้งเดียวจอจะค้างภาพเก่า
            target.texture = tex;

            if (aspectFitter != null && tex.height > 0)
            {
                aspectFitter.aspectRatio = (float)tex.width / tex.height;
            }

            if (!shown)
            {
                target.enabled = true;
                shown = true;
            }
        }
    }
}
