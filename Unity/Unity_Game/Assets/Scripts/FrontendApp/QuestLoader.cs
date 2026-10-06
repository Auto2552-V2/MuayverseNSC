using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;

// ดึงตารางฝึกที่หมอจัดไว้ในเว็บ มาเติมลงการ์ด Quest_1..Quest_5 ในหน้า Quest
//
// กติกาการเรียง: การ์ดใบที่ 1 = วันที่มาก่อนในสัปดาห์เสมอ (จ. ก่อน อ. ก่อน พ. …)
// ไม่ใช่ลำดับที่หมอกดเพิ่มในเว็บ — ฝั่ง API เรียงมาให้แล้วใน field `schedule`
// (ดู byWeekOrder ใน ticta-deploy/api/lib/quests.ts) ตรงนี้แค่วนเติมตาม index
//
//   หมอสั่ง จันทร์ + อังคาร + ศุกร์  →  Quest_1=จ.  Quest_2=อ.  Quest_3=ศ.  ที่เหลือซ่อน
//   หมอลบอังคารทิ้ง                →  Quest_1=จ.  Quest_2=ศ.  ที่เหลือซ่อน
//
// การลบไม่ต้องมี logic พิเศษ — poll รอบถัดไปได้ list ที่สั้นลง แล้ววาดใหม่ทั้งชุด

[System.Serializable]
public class Quest
{
    public string id;
    public string day;        // "monday"
    public string dayShort;   // "จ." — ตัวที่เอาไปโชว์ใน Day (text)
    public string poseName;
    public string poseThai;
    public string time;       // "3 นาที" (ฝั่ง API ประกอบมาให้แล้ว)
    public string count;      // ง่าย / กลาง / ยาก
    public int coin;
    public int star;
}

// ตรงกับ GET /api/quests?patient=<id> (ไม่ใส่ &day)
// `days{}` ที่ API ส่งมาด้วยตั้งใจไม่รับตรงนี้ — JsonUtility อ่าน dictionary ไม่ได้
[System.Serializable]
public class WeekQuests
{
    public string patientId;
    public string patientName;
    public Quest[] schedule;
}

// ช่องหนึ่งช่อง = การ์ด Quest_N หนึ่งใบ ลาก object ลูก ๆ ของมันมาใส่ให้ครบ
// ช่องไหนไม่มีใน UI เว้นว่างได้ (เช็ค null ทุกตัวก่อนเขียน)
[System.Serializable]
public class QuestSlot
{
    public GameObject card;        // ตัว Quest_N เอง — ตัวที่ถูกซ่อน/โชว์
    public TMP_Text dayText;       // Day (text)
    public TMP_Text poseNameText;  // PoseName (foredit)
    public TMP_Text timeText;      // Time (foredit)
    public TMP_Text countText;     // Count (foredit)
    public TMP_Text coinText;      // coin
    public TMP_Text starText;      // starcount
}

public class QuestLoader : MonoBehaviour
{
    [Header("Backend")]
    [Tooltip("ใช้เฉพาะตอนรันใน Editor/PC — ตัวเดียวกับที่ AuthApi ใช้ (dev คือพอร์ต 3000 " +
             "ไม่ใช่เว็บหมอที่พอร์ต 3001) WebGL build ไม่สนใจค่านี้ ยิง same-origin เสมอ")]
    [SerializeField] private string baseUrl = "http://localhost:3000";

    [Tooltip("เว้นว่างไว้ = ใช้ id ของคนที่ล็อกอินอยู่ (UserSession.UserId) " +
             "ใส่ค่าเองได้เฉพาะตอนเทสต์ ต้องเป็น uuid จริงจากตาราง patients")]
    [SerializeField] private string patientIdOverride = "";

    [Tooltip("ยิงถามซ้ำทุกกี่วินาที — หมอแก้ตารางในเว็บแล้วเกมจะตามภายในเวลานี้")]
    [SerializeField] private float pollInterval = 2f;

    [Header("การ์ด Quest (ลาก Quest_1..Quest_5 มาใส่ตามลำดับ)")]
    [SerializeField] private QuestSlot[] slots = new QuestSlot[5];

    // body ล่าสุดที่วาดไปแล้ว — ถ้าเหมือนเดิมก็ไม่ต้องแตะ UI ซ้ำทุก 2 วิ
    private string lastBody = "";
    private string lastPatientId = "";

    private string PatientId =>
        string.IsNullOrEmpty(patientIdOverride) ? UserSession.UserId : patientIdOverride;

    void OnEnable()
    {
        // เปิดหน้ามาให้ว่างไว้ก่อน กันการ์ดของผู้ป่วยคนก่อนค้างให้เห็นระหว่างรอ response
        HideAll();
        lastBody = "";
        StartCoroutine(Loop());
    }

    // หน้า Quest ถูกเปิด/ปิดด้วย SetActive ทุกครั้งที่สลับหน้า ถ้าไม่หยุด coroutine
    // ตรงนี้ Loop() จะซ้อนกันเพิ่มขึ้นทุกรอบที่กลับเข้าหน้านี้ แล้วยิง request ถี่ขึ้นเรื่อย ๆ
    void OnDisable()
    {
        StopAllCoroutines();
    }

    IEnumerator Loop()
    {
        while (true)
        {
            yield return StartCoroutine(Fetch());
            yield return new WaitForSeconds(pollInterval);
        }
    }

    IEnumerator Fetch()
    {
        string patientId = PatientId;

        // ยังไม่ล็อกอิน — ไม่มีอะไรให้ดึง อย่ายิง request รัว ๆ ทิ้ง
        if (string.IsNullOrEmpty(patientId))
        {
            if (lastBody != "") { HideAll(); lastBody = ""; }
            yield break;
        }

        // สลับบัญชีแล้ว ของคนเก่าต้องไม่ค้างอยู่บนจอ
        if (patientId != lastPatientId)
        {
            lastPatientId = patientId;
            lastBody = "";
            HideAll();
        }

        string url = ApiConfig.Url(
            baseUrl,
            $"/api/quests?patient={UnityWebRequest.EscapeURL(patientId)}" +
            $"&_={System.DateTime.Now.Ticks}");

        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            req.SetRequestHeader("Cache-Control", "no-cache");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                // เน็ตสะดุดชั่วคราว — คงของเดิมไว้ดีกว่าล้างจอให้กะพริบ
                Debug.LogWarning($"[QuestLoader] ดึงตารางฝึกไม่ได้: {req.error} ({url})");
                yield break;
            }

            string body = req.downloadHandler.text;
            if (body == lastBody) yield break;   // ไม่มีอะไรเปลี่ยน
            lastBody = body;

            WeekQuests data = JsonUtility.FromJson<WeekQuests>(body);
            Render(data != null ? data.schedule : null);
        }
    }

    // วาดใหม่ทั้งชุดทุกครั้ง ไม่แก้เฉพาะจุด — เพิ่ม/ลบ/สลับวัน จบด้วยทางเดียวกันหมด
    void Render(Quest[] schedule)
    {
        int total = schedule != null ? schedule.Length : 0;

        for (int i = 0; i < slots.Length; i++)
        {
            QuestSlot slot = slots[i];
            if (slot == null) continue;

            if (i >= total)
            {
                if (slot.card) slot.card.SetActive(false);
                continue;
            }

            Fill(slot, schedule[i]);
        }

        // มีแค่ 5 ช่อง แต่สัปดาห์มี 7 วัน — ที่เกินตัดทิ้ง แต่ต้องบอกให้รู้
        // ไม่งั้นหมอสั่ง 7 วันแล้วงงว่าทำไมเกมเห็นแค่ 5
        if (total > slots.Length)
            Debug.LogWarning($"[QuestLoader] หมอสั่งไว้ {total} ท่า แต่มีการ์ดแค่ {slots.Length} ใบ — แสดง {slots.Length} ใบแรก");
    }

    void Fill(QuestSlot slot, Quest q)
    {
        if (slot.card) slot.card.SetActive(true);
        if (slot.dayText) slot.dayText.text = q.dayShort;
        if (slot.poseNameText) slot.poseNameText.text = q.poseName;
        if (slot.timeText) slot.timeText.text = q.time;
        if (slot.countText) slot.countText.text = q.count;
        if (slot.coinText) slot.coinText.text = q.coin.ToString();
        if (slot.starText) slot.starText.text = q.star.ToString();
    }

    void HideAll()
    {
        foreach (QuestSlot slot in slots)
            if (slot != null && slot.card) slot.card.SetActive(false);
    }
}
