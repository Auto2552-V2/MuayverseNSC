using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

// ปุ่ม "เริ่มการบำบัด" ในหน้า manual_Page — พาออกจากเกมไปหน้าเซสชันของเว็บ AI-Rehab
// (เว็บอ่านท่าทางด้วยกล้อง + โค้ชเสียงไทย คนละโปรเจกต์กับ ticta-deploy)
//
// ญาติกับ DoctorPortalLink.cs — ต่างกันแค่ปลายทาง ที่แยกสคริปต์เพราะปลายทางของ
// เว็บบำบัด "ยังไม่แน่ว่าจะอยู่โดเมนเดียวกันไหม" ต่างจากเว็บหมอที่ same-origin แน่นอน
//
// ─── ตั้งช่อง Destination ยังไง ────────────────────────────────────────────
//
// ขึ้นกับว่าเอา AI-Rehab ไป deploy ที่ไหน มีสองแบบ ใส่ได้ทั้งคู่ในช่องเดียวกัน
//
//   1. เป็น service ที่ 3 ในโดเมนของงาน (ต้องขอ path เพิ่ม — คู่มือข้อ 13)
//        Destination = /rehab/mode/session
//      เว้น host ไว้ให้เบราว์เซอร์เติม origin เอง same-origin เลยไม่ต้องแตะ CORS
//      และไม่ต้อง build เกมใหม่ตอนย้ายโดเมน เหตุผลเดียวกับ ApiConfig.cs
//
//   2. แยกไปโฮสต์ที่อื่น (Vercel ฯลฯ) — ใส่ URL เต็ม
//        Destination = https://ai-rehab.vercel.app/mode/session
//      กรณีนี้ช่อง Editor Base Url ไม่ถูกใช้เลย
//
// ─── ทำไมไม่ใช้ Application.OpenURL ───────────────────────────────────────
//
// บน WebGL มันถูกแปลเป็น window.open() ซึ่งโดน popup blocker กินบ่อย เพราะ Unity
// ประมวลผลคลิกช้ากว่า event จริงของเบราว์เซอร์อยู่หนึ่งเฟรม เบราว์เซอร์เลยไม่นับ
// ว่าเป็น user gesture — เปลี่ยน location ของแท็บเดิมไม่มีข้อจำกัดนี้
// (ดู Assets/Plugins/WebGL/SiteNavigation.jslib)

[RequireComponent(typeof(Button))]
public class TherapyLink : MonoBehaviour
{
    [Header("ปลายทาง")]
    [Tooltip("path ล้วน (/rehab/mode/session) = โดเมนเดียวกับเกม | " +
             "หรือ URL เต็ม (https://...) = โฮสต์แยก")]
    [SerializeField] private string destination = "/mode/session";

    [Tooltip("ใช้เฉพาะตอนรันใน Editor/PC และเฉพาะเมื่อ Destination เป็น path ล้วน\n" +
             "dev: อย่าใช้ 3000 เด็ดขาด — พอร์ตนั้นเป็นของ service api\n" +
             "รัน AI-Rehab ด้วย  npm run dev -- -p 3002")]
    [SerializeField] private string editorBaseUrl = "http://localhost:3002";

    [Header("ส่งตัวตนผู้ป่วยไปด้วย")]
    [Tooltip("ต่อ ?patient=<uuid> ท้าย URL\n" +
             "⚠️ ต้องเปิดไว้ ไม่งั้นเว็บไม่รู้ว่าใครฝึก แล้วจะฝึกได้แต่ไม่บันทึกผล " +
             "ไม่ได้เหรียญ และกราฟในหน้า Dashboard ไม่ขยับ")]
    [SerializeField] private bool sendPatientId = true;

    [Header("ทางกลับเข้าเกม")]
    [Tooltip("ต่อ &return=<หน้านี้> ไปด้วย พอฝึกจบเว็บจะพากลับมาที่นี่พร้อม " +
             "?session=<id> แล้ว TherapyReward.cs เปิดหน้า Reward ให้เอง\n" +
             "ปิด = ฝึกจบแล้วค้างอยู่ที่เว็บ ต้องกด back เอง (ผลยังถูกบันทึกตามปกติ)")]
    [SerializeField] private bool sendReturnUrl = true;

    [Tooltip("ใช้เฉพาะตอนรันใน Editor/PC ซึ่งไม่มีหน้าเว็บของเกมให้อ้างอิง\n" +
             "ใส่ URL ของ WebGL build ที่เสิร์ฟอยู่ (เว็บหมอวางไว้ที่ public/game/)\n" +
             "เว็บจะรับ URL เต็มเฉพาะ localhost และเฉพาะตอน dev เท่านั้น")]
    [SerializeField] private string editorReturnUrl = "http://localhost:3001/game/";

#if UNITY_WEBGL && !UNITY_EDITOR
    // นิยามอยู่ใน Assets/Plugins/WebGL/SiteNavigation.jslib — ตัวเดียวกับที่
    // DoctorPortalLink ใช้ ประกาศซ้ำได้ ผูกกับ symbol เดียวกัน
    [DllImport("__Internal")]
    private static extern void TictaOpenSiteUrl(string url);
#endif

    // ผูก listener เองตั้งแต่ Awake แบบเดียวกับ DoctorPortalLink
    // ห้ามไปเพิ่มใน On Click () ของปุ่มใน Inspector อีก — ใส่ซ้ำจะยิงสองครั้ง
    private void Awake()
    {
        GetComponent<Button>().onClick.AddListener(Open);
    }

    public void Open()
    {
        string url = Resolve();

        if (string.IsNullOrEmpty(url))
        {
            Debug.LogWarning("[TherapyLink] ช่อง Destination ว่าง — ไม่รู้จะพาไปไหน");
            return;
        }

        // ต้องมาก่อน return เพราะถ้าไม่รู้ว่าใครฝึก เว็บก็บันทึกผลไม่ได้อยู่ดี
        if (sendPatientId && UserSession.IsLoggedIn)
            url = Append(url, "patient", UserSession.UserId);
        else if (sendPatientId)
            Debug.LogWarning("[TherapyLink] ยังไม่ได้ล็อกอิน — เว็บจะฝึกได้แต่ไม่บันทึกผล");

        if (sendReturnUrl)
        {
            string back = ReturnUrl();
            if (!string.IsNullOrEmpty(back)) url = Append(url, "return", back);
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        TictaOpenSiteUrl(url);
#else
        Application.OpenURL(url);   // Editor/PC: เปิดเบราว์เซอร์ของเครื่อง
#endif
    }

    private static string Append(string url, string key, string value)
    {
        return url + (url.Contains("?") ? "&" : "?")
             + key + "=" + UnityWebRequest.EscapeURL(value);
    }

    // "หน้าเว็บของเกม" ที่จะให้เว็บบำบัดพากลับมาหลังฝึกจบ
    //
    // ส่ง URL เต็ม (https://host/game/) ไม่ใช่ path ล้วน — ต่างจาก ApiConfig.cs
    // โดยตั้งใจ เพราะปลายทางที่จะเอา path ไปต่อ origin ให้ ไม่ใช่หน้าเกมอีกแล้ว
    // แต่เป็นหน้าของเว็บบำบัด ตอน dev เว็บนั้นอยู่พอร์ต 3003 ส่วนเกมอยู่ 3001
    // ส่ง "/game/" ไปเบราว์เซอร์จะพากลับไป localhost:3003/game/ ซึ่งไม่มีอยู่จริง
    //
    // อ่านจาก Application.absoluteURL ตอนกดปุ่ม ไม่ได้ฝังไว้ตอน build ย้ายโดเมน
    // เมื่อไหร่ค่านี้ก็เปลี่ยนตามเอง และบนเซิร์ฟเวอร์มันคือ origin เดียวกับเว็บบำบัด
    // อยู่แล้ว ฝั่งโน้นจึงรับผ่านในฐานะ same-origin (ดู sanitizeReturnUrl ใน
    // AI-Rehab/lib/session-return.ts — URL เต็มที่ไม่ใช่ origin ตัวเองจะถูกปฏิเสธ
    // เพื่อกัน open redirect ยกเว้น localhost ตอน dev)
    //
    // ตัด query string เดิมทิ้งด้วย ไม่งั้น ?session= ของรอบก่อนจะติดกลับมาอีกรอบ
    private string ReturnUrl()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        string absolute = Application.absoluteURL;
        if (string.IsNullOrEmpty(absolute)) return "";
        try
        {
            System.Uri uri = new System.Uri(absolute);
            // "https://team12.aiforthai.in.th" + "/game/index.html"
            return uri.Scheme + "://" + uri.Authority + uri.AbsolutePath;
        }
        catch
        {
            // absoluteURL หน้าตาแปลกจนแกะไม่ออก — ตัดเอาดื้อ ๆ ดีกว่าไม่มีทางกลับ
            int q = absolute.IndexOf('?');
            return q < 0 ? absolute : absolute.Substring(0, q);
        }
#else
        return (editorReturnUrl ?? "").Trim();
#endif
    }

    private string Resolve()
    {
        string dest = (destination ?? "").Trim();
        if (dest.Length == 0) return "";

        // URL เต็มใช้ได้ตรง ๆ ทั้งสองสภาพแวดล้อม ไม่ต้องเติมอะไร
        if (dest.StartsWith("http://") || dest.StartsWith("https://")) return dest;

        if (!dest.StartsWith("/")) dest = "/" + dest;

#if UNITY_WEBGL && !UNITY_EDITOR
        return dest;   // เบราว์เซอร์เติม origin ให้เอง
#else
        return (editorBaseUrl ?? "").TrimEnd('/') + dest;
#endif
    }
}
