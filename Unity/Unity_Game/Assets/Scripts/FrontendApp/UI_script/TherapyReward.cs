using System.Collections;
using UnityEngine;
using TMPro;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

// พาผู้เล่นเข้าหน้า Reward_Page ทันทีที่กลับจากเว็บบำบัด พร้อมตัวเลขของจริง
//
// ─── เส้นทางทั้งเส้น ──────────────────────────────────────────────────
//
//   1. ปุ่ม "เริ่มการบำบัด" (TherapyLink.cs) พาออกจากเกมไปเว็บ AI-Rehab
//        /mode/session-left?patient=<uuid>&return=/game/
//   2. ฝึกจบ เว็บ POST ผลเข้า API เอง → ได้ sessionId + จ่ายเหรียญเรียบร้อย
//   3. กด "กลับหน้าหลัก" เว็บพากลับมาที่ return พร้อม ?session=<sessionId>
//   4. สคริปต์นี้อ่าน id นั้น ไป GET ค่าจริงมาเติมหน้า Reward
//
// ตัวเลขบนหน้ารางวัลจึงมาจากฐานข้อมูลเสมอ ไม่ใช่จาก URL — ใครแก้ ?accuracy=100
// ใน address bar ก็ไม่มีผล เพราะ URL ไม่ได้พกตัวเลขมาเลยตั้งแต่แรก
//
// ─── วิธีติดตั้ง ─────────────────────────────────────────────────────
//
//   แปะไว้บน _UIController (ต้องเป็น object ที่ active ตลอด — Reward_Page เองถูก
//   ปิดอยู่ตอนเปิดเกม สคริปต์ที่แปะบนนั้นจะไม่ทำงาน) แล้วลากช่องให้ครบ
//
// ⚠️ ถ้า PlayerPrefs ยังจำ session ไว้ (ยังไม่ได้ logout) เกมจะเด้งเข้าหน้า Reward
//    ทับหน้าที่ PageManager เปิดไว้ตอน Start — ตั้งใจให้เป็นแบบนั้น เพราะโหลดหน้า
//    ใหม่ทีเกมก็เริ่มจากศูนย์ทุกครั้ง ถ้ารอให้ล็อกอินก่อนผู้เล่นจะไม่มีวันได้เห็น
//    หน้ารางวัลของรอบที่เพิ่งฝึกจบเลย
//    ส่วนคนที่ logout ไปแล้ว (หรือ id ไม่ตรงกับเจ้าของเซสชัน) จะไม่เห็นอะไรทั้งสิ้น
[RequireComponent(typeof(TherapyApi))]
public class TherapyReward : MonoBehaviour
{
    [Header("หน้าที่จะเปิด")]
    [Tooltip("ตัวจัดการหน้า — ปกติอยู่บน object เดียวกันนี้")]
    [SerializeField] private PageManager pageManager;

    [Tooltip("GameObject ของ Reward_Page")]
    [SerializeField] private GameObject rewardPage;

    [Header("ช่องบนหน้า Reward")]
    [Tooltip("ตัวเลข % ใน Acuuracy_image — ใส่แค่ตัวเลข สคริปต์เติม % ให้เอง")]
    [SerializeField] private TMP_Text accuracyText;

    [Tooltip("เหรียญที่ได้รอบนี้ (แสดงเป็น +50)")]
    [SerializeField] private TMP_Text coinEarnedText;

    [Tooltip("ดาวที่ได้รอบนี้ (แสดงเป็น +5)")]
    [SerializeField] private TMP_Text starEarnedText;

    [Tooltip("ข้อความหัวเรื่อง (Text (TMP)) — เว้นว่างได้ถ้าไม่อยากให้เปลี่ยนข้อความ")]
    [SerializeField] private TMP_Text headlineText;

    [Header("ยอดรวมบนแถบบนสุด (เว้นว่างได้)")]
    [Tooltip("ยอดเหรียญรวมของบัญชี — response พกมาให้อยู่แล้ว ไม่ต้องยิงถามซ้ำ\n" +
             "ไม่ใส่ = แถบบนจะยังโชว์เลขเดิมจนกว่าจะโหลดเกมใหม่")]
    [SerializeField] private TMP_Text coinTotalText;

    [SerializeField] private TMP_Text starTotalText;

    [Header("ข้อความตอนแพ้มินิเกม")]
    [Tooltip("ชนะไม่ต้องตั้งอะไร — ใช้ข้อความ/ตัวเลขที่ออกแบบไว้บนหน้า Reward ตามเดิม")]
    [SerializeField] private string loseHeadline = "เสียใจด้วย";

    [Header("ทดสอบใน Editor")]
    [Tooltip("Editor ไม่มี URL ให้อ่าน ใส่ sessionId จากตาราง quest_completions " +
             "ตรงนี้เพื่อจำลองการกลับจากเว็บตอนกด Play\n" +
             "ปล่อยว่างไว้ = ไม่ทำอะไรเลย (ค่าที่ควรเป็นตอน build จริง)")]
    [SerializeField] private string editorTestSessionId = "";

#if UNITY_WEBGL && !UNITY_EDITOR
    // นิยามอยู่ใน Assets/Plugins/WebGL/SiteNavigation.jslib
    [DllImport("__Internal")]
    private static extern void TictaClearUrlQuery();
#endif

    private TherapyApi api;

    private void Awake()
    {
        api = GetComponent<TherapyApi>();
        if (pageManager == null) pageManager = GetComponent<PageManager>();
    }

    private void Start()
    {
        // เพิ่งเล่นมินิเกมจบแล้ว StageController พากลับมาซีนนี้ — ผลอยู่ในหน่วยความจำแล้ว
        // ไม่ต้องรอ API และไม่เกี่ยวกับ ?session= ของเว็บบำบัดเลย
        if (MinigameResult.Consume(out bool won))
        {
            StartCoroutine(ShowMatchResult(won));
            return;
        }

        string sessionId = ReadSessionId();
        if (string.IsNullOrEmpty(sessionId)) return;

        // ไม่ได้ล็อกอินอยู่ = ไม่รู้ว่าใครกลับมา ไม่ควรโชว์ผลของใครก็ไม่รู้
        if (!UserSession.IsLoggedIn)
        {
            Debug.Log("[TherapyReward] กลับมาพร้อม session แต่ยังไม่ได้ล็อกอิน — ข้ามหน้ารางวัล");
            return;
        }

        api.GetSession(sessionId, Show, error =>
        {
            // ล้มตรงนี้ไม่ควรทำให้เข้าเกมไม่ได้ — เหรียญถูกจ่ายไปแล้วตั้งแต่ฝั่งเว็บ
            // อย่างแย่ที่สุดคือไม่ได้เห็นหน้าสรุป แต่ยอดยังขึ้นถูกต้อง
            Debug.LogWarning($"[TherapyReward] ดึงผลเซสชันไม่ได้: {error}");
        });
    }

    // อ่าน ?session=<id> จาก URL ของหน้าเกม
    private string ReadSessionId()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        string id = QueryValue(Application.absoluteURL, "session");
        // ล้าง query ทิ้งทันทีที่อ่านได้ กัน refresh แล้วเด้งหน้ารางวัลซ้ำ
        if (!string.IsNullOrEmpty(id)) TictaClearUrlQuery();
        return id;
#else
        return (editorTestSessionId ?? "").Trim();
#endif
    }

    // แกะ query string เอง — Unity ไม่มีตัวช่วยที่ใช้ได้ทั้ง WebGL
    // (System.Web ไม่มีใน Mono profile ที่ WebGL build ใช้)
    private static string QueryValue(string url, string key)
    {
        if (string.IsNullOrEmpty(url)) return "";

        int start = url.IndexOf('?');
        if (start < 0) return "";

        string query = url.Substring(start + 1);
        int hash = query.IndexOf('#');
        if (hash >= 0) query = query.Substring(0, hash);

        foreach (string pair in query.Split('&'))
        {
            if (pair.Length == 0) continue;
            int eq = pair.IndexOf('=');
            string name = eq < 0 ? pair : pair.Substring(0, eq);
            if (name != key) continue;
            string value = eq < 0 ? "" : pair.Substring(eq + 1);
            return UnityEngine.Networking.UnityWebRequest.UnEscapeURL(value);
        }
        return "";
    }

    private void Show(TherapySessionResult result)
    {
        if (result == null) return;

        // เซสชันของคนอื่น — เกิดได้ถ้าสลับบัญชีระหว่างที่ค้างอยู่ที่เว็บบำบัด
        // หรือมีคนส่งลิงก์พร้อม session ของตัวเองมาให้กด
        if (!string.IsNullOrEmpty(result.patientId) && result.patientId != UserSession.UserId)
        {
            Debug.LogWarning("[TherapyReward] session นี้ไม่ใช่ของบัญชีที่ล็อกอินอยู่ — ไม่โชว์");
            return;
        }

        if (accuracyText != null)
            accuracyText.text = result.scored ? result.accuracy + "%" : "—";

        // เว้นวรรคหลังเครื่องหมายไม่ได้ ตัวเลขจะห่างจาก + จนดูเป็นคนละก้อน
        if (coinEarnedText != null) coinEarnedText.text = "+" + result.coinEarned;
        if (starEarnedText != null) starEarnedText.text = "+" + result.starEarned;

        if (coinTotalText != null) coinTotalText.text = result.coins.ToString();
        if (starTotalText != null) starTotalText.text = result.stars.ToString();

        // ยอดนี้สดกว่าที่ cache ไว้ — บอกให้แถบบนสุดวาดใหม่ด้วย
        UserSession.SetBalance(result.coins, result.stars);

        if (headlineText != null)
        {
            // ได้ 0 เหรียญทั้งที่ทำจบ = วันนี้เคลมท่านี้ไปแล้ว ต้องบอกให้รู้ ไม่งั้น
            // เห็น "+0" แล้วนึกว่าระบบพัง
            headlineText.text = result.alreadyClaimedToday
                ? "เก่งมาก! วันนี้รับรางวัลท่านี้ไปแล้ว"
                : "ยินดีด้วย! คุณชนะ";
        }

        OpenRewardPage();
    }

    // จบมินิเกมชกมวย
    //
    // เดิมหน้านี้โชว์เลขที่วางไว้บนหน้า Reward ตรง ๆ เพราะมินิเกมไม่ได้จ่ายเหรียญจริง
    // ตอนนี้โหมดชกมวยจ่ายผ่าน /api/minigame/complete แล้ว จึงต้องโชว์ยอดที่เซิร์ฟเวอร์
    // ตอบกลับมา ไม่งั้นจอบอกเลขหนึ่ง ฐานข้อมูลเก็บอีกเลขหนึ่ง
    //
    // ยังเผื่อกรณีไม่รู้ยอดไว้ (API ยังไม่ตอบ/ยิงไม่สำเร็จ/ปิด grantReward) —
    // กรณีนั้นปล่อยเลขบนหน้าไว้อย่างเดิม ดีกว่าโชว์ 0 ทั้งที่อาจได้เหรียญจริง
    private IEnumerator ShowMatchResult(bool won)
    {
        // รอหนึ่งเฟรมให้ PageManager.Start เปิดหน้าเริ่มต้นของมันไปก่อน — Start ของสอง
        // สคริปต์นี้อยู่บน object เดียวกัน ลำดับขึ้นกับลำดับ component ถ้า PageManager
        // ได้ทำงานทีหลัง หน้ารางวัลที่เพิ่งเปิดจะโดนปิดทับทันทีจนจอว่าง
        yield return null;

        if (!won)
        {
            if (headlineText != null && !string.IsNullOrEmpty(loseHeadline))
                headlineText.text = loseHeadline;

            // แพ้แล้วต้องเคลียร์เลขตัวอย่างบนหน้าจอด้วย ไม่งั้นจะเห็น "+50" ทั้งที่แพ้
            if (coinEarnedText != null) coinEarnedText.text = "+0";
            if (starEarnedText != null) starEarnedText.text = "+0";
        }

        if (MinigameResult.ConsumeReward(out int coinEarned, out int starEarned,
                                         out int coins, out int stars))
        {
            if (coinEarnedText != null) coinEarnedText.text = "+" + coinEarned;
            if (starEarnedText != null) starEarnedText.text = "+" + starEarned;
            if (coinTotalText != null) coinTotalText.text = coins.ToString();
            if (starTotalText != null) starTotalText.text = stars.ToString();
            UserSession.SetBalance(coins, stars);
        }
        else
        {
            Debug.Log("[TherapyReward] ไม่รู้ยอดรางวัลของแมตช์นี้ — โชว์เลขบนหน้าไว้ตามเดิม");
        }

        OpenRewardPage();
    }

    // ให้ PageManager ปิดหน้าอื่นให้ก่อน แล้วค่อยเปิดหน้ารางวัลเองอีกที
    //
    // ShowPage เปิดเฉพาะหน้าที่อยู่ใน list `pages` ของมัน — ถ้าลืมใส่ Reward_Page
    // เข้าไปใน list ผลคือหน้าอื่นดับหมดแล้วไม่มีอะไรขึ้นมาแทน จอว่างเปล่า
    // SetActive ซ้ำตรงนี้ทำให้ลืมใส่แล้วยังใช้ได้ และใส่แล้วก็ไม่เสียหายอะไร
    private void OpenRewardPage()
    {
        if (rewardPage == null)
        {
            Debug.LogWarning("[TherapyReward] ยังไม่ได้ลาก Reward_Page ใส่ช่อง Reward Page");
            return;
        }

        if (pageManager != null) pageManager.ShowPage(rewardPage);
        rewardPage.SetActive(true);
    }
}
