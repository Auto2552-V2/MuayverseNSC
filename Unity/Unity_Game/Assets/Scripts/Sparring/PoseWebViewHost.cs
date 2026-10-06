using Gree.UnityWebView;
using UnityEngine;
using UnityEngine.Android;

namespace Sparring
{
    /// <summary>
    /// เปิด WebView ที่รัน AI detection แล้วต่อสายให้ข้อความไหลเข้า <see cref="PoseReceiver"/>
    ///
    /// ─── ภาพรวม ─────────────────────────────────────────────────────────────────
    ///   WebView (MediaPipe + LSTM)  --Unity.call(json)-->  WebViewObject
    ///        --cb callback-->  PoseReceiver.OnPoseJson()  --คิว-->  เกม
    ///
    /// กล้องกับโมเดลรันในหน้าเว็บทั้งหมด ฝั่ง Unity ไม่แตะกล้องเลย
    ///
    /// ─── สามอย่างที่ต้องครบ กล้องถึงจะทำงานบน Android ──────────────────────────
    ///   1. android.permission.CAMERA ใน manifest
    ///      -> UnityWebViewPostprocessBuild.cs ใส่ให้เอง แต่ต้องตั้ง scripting define
    ///         UNITYWEBVIEW_ANDROID_ENABLE_CAMERA ก่อน (ตั้งไว้แล้วใน ProjectSettings)
    ///   2. ขอ runtime permission ก่อนโหลดหน้าเว็บ (Android 6+ / minSdk ที่นี่ 23)
    ///      -> สคริปต์นี้ทำให้ใน Start()
    ///   3. WebChromeClient.onPermissionRequest() ต้อง grant VIDEO_CAPTURE
    ///      -> gree ทำมาให้แล้วใน .aar (ตรวจจาก bytecode แล้ว ไม่ต้องแก้ Java)
    ///
    /// ─── URL ต้องเป็น secure context ───────────────────────────────────────────
    /// getUserMedia ยอมรับแค่ https:// หรือ http://localhost
    /// ตอนพัฒนาใช้ adb reverse tcp:8000 tcp:8000 แล้วชี้มาที่ http://localhost:8000
    /// ตอน deploy จริงค่อยเปลี่ยนเป็น https:// ของจริง
    /// </summary>
    [RequireComponent(typeof(PoseReceiver))]
    public class PoseWebViewHost : MonoBehaviour
    {
        [Header("หน้าเว็บ AI detection")]
        [Tooltip("ต้องเป็น https:// หรือ http://localhost เท่านั้น ไม่งั้นกล้องจะไม่ทำงาน\n" +
                 "ตอนพัฒนา: adb reverse tcp:8000 tcp:8000 แล้วใช้ http://localhost:8000/web/\n" +
                 "อย่าลืม / ปิดท้าย")]
        [SerializeField] private string url = "http://localhost:8000/web/";

        [Header("การแสดงผล")]
        [Tooltip("ติ๊ก = เห็นหน้าเว็บทับจอเกม ใช้ตอนดีบักว่ากล้องติดไหม\n" +
                 "ไม่ติ๊ก = ซ่อนไว้ ทำงานอยู่เบื้องหลัง (โหมดเล่นจริง)")]
        [SerializeField] private bool visible = true;

        // ใช้ int สี่ตัว ไม่ใช้ RectOffset — RectOffset เป็น object ที่ฝั่ง native ถือไว้
        // สร้างมันใน field initializer จะได้ UnityException "set_left is not allowed to be
        // called from a MonoBehaviour constructor" เพราะตอนนั้น engine ยังไม่พร้อม
        [Tooltip("ระยะขอบของ WebView เป็นพิกเซล — ตอนดีบักตั้งให้เว้นขอบ " +
                 "จะเห็นทั้งจอเกมและภาพกล้องพร้อมกัน")]
        [SerializeField] private int marginLeft;
        [SerializeField] private int marginTop;
        [SerializeField] private int marginRight;
        [SerializeField] private int marginBottom;

        [Header("ดีบัก")]
        [Tooltip("พิมพ์ทุก event ของ WebView (โหลดเสร็จ, error, http error) ลง Console")]
        [SerializeField] private bool logWebViewEvents = true;

        private WebViewObject webView;
        private PoseReceiver receiver;
        private bool launched;

        private void Awake()
        {
            receiver = GetComponent<PoseReceiver>();
        }

        private void Start()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // ต้องได้ permission ก่อนโหลดหน้าเว็บ ไม่ใช่หลัง — ถ้าหน้าเว็บเรียก
            // getUserMedia ตอนที่แอปยังไม่ได้สิทธิ์ WebView จะปฏิเสธทันที
            // และหน้าเว็บจะไม่ลองใหม่เอง ต้องรีโหลด
            if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
            {
                var callbacks = new PermissionCallbacks();
                callbacks.PermissionGranted += _ => Launch();
                callbacks.PermissionDenied += _ =>
                    Debug.LogError("[PoseWebViewHost] ผู้ใช้ไม่อนุญาตกล้อง — detection ใช้ไม่ได้");
                Permission.RequestUserPermission(Permission.Camera, callbacks);
                return;
            }
#endif
            Launch();
        }

        private void Launch()
        {
            if (launched) return;      // PermissionGranted ยิงซ้ำได้ในบางเครื่อง
            launched = true;

            webView = new GameObject("PoseWebView").AddComponent<WebViewObject>();
            webView.transform.SetParent(transform, false);

            webView.Init(
                // ตัวนี้คือสายหลัก: Unity.call(json) ฝั่ง JS มาโผล่ที่นี่
                cb: msg => receiver.OnPoseJson(msg),
                err: msg => Debug.LogError($"[PoseWebView] error: {msg}"),
                httpErr: msg => Debug.LogError($"[PoseWebView] http error: {msg}"),
                started: msg => { if (logWebViewEvents) Debug.Log($"[PoseWebView] เริ่มโหลด {msg}"); },
                ld: msg =>
                {
                    if (logWebViewEvents) Debug.Log($"[PoseWebView] โหลดเสร็จ {msg}");
                    // บอกหน้าเว็บว่าฝั่ง Unity พร้อมแล้ว เผื่อหน้าเว็บอยากเริ่มกล้องเอง
                    webView.EvaluateJS("window.dispatchEvent(new Event('unity-ready'));");
                },
                transparent: false,
                zoom: false,                    // ปิด pinch zoom ไม่งั้นผู้เล่นซูมหลุดโดยไม่ตั้งใจ
                enableWKWebView: true);

            webView.SetMargins(marginLeft, marginTop, marginRight, marginBottom);
            webView.SetVisibility(visible);
            webView.LoadURL(url);

            Debug.Log($"[PoseWebViewHost] กำลังโหลด {url}");
            WarnIfInsecure();
        }

        /// <summary>
        /// เตือนล่วงหน้าถ้า URL ไม่ใช่ secure context
        ///
        /// ไม่เตือนตรงนี้ อาการที่เจอคือ "หน้าเว็บขึ้นแต่กล้องดำ" ซึ่งบนมือถือไม่มี
        /// console ให้ดู แล้วจะไปเสียเวลาไล่หาที่ permission หรือที่ plugin แทน
        /// </summary>
        private void WarnIfInsecure()
        {
            bool secure = url.StartsWith("https://")
                          || url.StartsWith("http://localhost")
                          || url.StartsWith("http://127.0.0.1");
            if (!secure)
            {
                Debug.LogError($"[PoseWebViewHost] URL ไม่ใช่ secure context: {url}\n" +
                               "getUserMedia จะถูกปฏิเสธ กล้องจะไม่ทำงาน — ต้องเป็น https:// " +
                               "หรือ http://localhost (ใช้ adb reverse tcp:8000 tcp:8000)");
            }
            if (url.EndsWith("/web") || url.EndsWith("/pose"))
            {
                Debug.LogWarning($"[PoseWebViewHost] URL ไม่มี / ปิดท้าย: {url}\n" +
                                 "ปกติยังโหลดได้ แต่ใส่ / ไว้ชัดเจนกว่า");
            }
        }

        /// <summary>เปิด/ปิดการมองเห็น WebView ตอนรัน (ผูกกับปุ่มดีบักได้)</summary>
        public void SetVisible(bool show)
        {
            visible = show;
            webView?.SetVisibility(show);
        }

        /// <summary>โหลดหน้าเว็บใหม่ — ใช้ตอนผู้ใช้เพิ่งกดอนุญาตกล้องทีหลัง</summary>
        public void Reload()
        {
            if (webView != null) webView.LoadURL(url);
        }
    }
}
