using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

// คุยกับ API เรื่องเซสชันกายภาพจากเว็บ AI-Rehab
//
//   GET /api/therapy-sessions/:id   — ผลของเซสชันที่เพิ่งฝึกจบ (หน้า Reward)
//   GET /api/progress?patient=<id>  — เฉลี่ยรายท่า (Progress overview ในหน้า Dashboard)
//
// ทั้งสองเส้นเป็นการ "อ่าน" ล้วน ๆ เกมไม่เคยเป็นคนบันทึกผลหรือสั่งจ่ายเหรียญเอง —
// เว็บบำบัดยิง POST บันทึกไว้ตั้งแต่ตอนกดจบเซสชันแล้ว (ดู AI-Rehab/app/api/session)
// เกมแค่มารับตัวเลขไปโชว์ กด refresh ซ้ำกี่รอบยอดเหรียญก็ไม่ขยับ
//
// รูปแบบเดียวกับ AuthApi.cs — แปะ component นี้ไว้บน GameObject ที่ active ตลอด
// (เช่น _UIController) แล้วให้สคริปต์ UI อ้างถึง

// ── รูป JSON ที่ API ตอบกลับ ──
// JsonUtility จับคู่ด้วยชื่อ field ต้องตรงกับ key ใน JSON เป๊ะ ๆ
// key ที่ไม่ได้ประกาศไว้จะถูกทิ้งเงียบ ๆ (เช่น completedAt, patientId)

[System.Serializable]
public class TherapySessionResult
{
    public string sessionId;
    public string patientId;
    public string poseKey;   // shoulder_abduction_left / _right / _combo
    public string poseName;
    public int accuracy;     // 0-100 ปัดเป็นจำนวนเต็มมาจากฝั่ง API แล้ว
    public bool scored;      // false = ประเมินท่าไม่ได้ ตัวเลข accuracy ไม่มีความหมาย
    public int reps;
    public bool completed;
    public int coinEarned;   // รอบนี้ได้เพิ่มกี่เหรียญ (0 = วันนี้เคลมท่านี้ไปแล้ว)
    public int starEarned;
    public int coins;        // ยอดรวมล่าสุด เอาไปอัปเดตแถบบนสุดได้เลย
    public int stars;
    public bool alreadyClaimedToday;
}

[System.Serializable]
public class PoseProgress
{
    public string poseKey;
    public string poseName;
    public string poseThai;
    public int sessions;        // ฝึกท่านี้ไปแล้วกี่ครั้ง
    public int scoredSessions;  // ในนั้นประเมินคะแนนได้กี่ครั้ง
    public int averageAccuracy; // เฉลี่ยของท่านี้ 0-100 (int แล้ว ไม่ต้องปัดซ้ำ)
    public int lastAccuracy;
}

[System.Serializable]
public class TherapyProgressResult
{
    public string patientId;
    // เรียงคงที่ตาม THERAPY_POSES ฝั่ง API: [0] ซ้าย [1] ขวา [2] สองข้าง
    // เติมลงแถวตาม index ได้เลย — เหตุผลเดียวกับ schedule ของ /api/quests
    public PoseProgress[] poses;
    public int overallAccuracy;
    public int totalSessions;
}

public class TherapyApi : MonoBehaviour
{
    [Header("Backend")]
    [Tooltip("ใช้เฉพาะตอนรันใน Editor/PC (dev คือ http://localhost:3000 = service api) " +
             "WebGL build ไม่สนใจค่านี้ ยิง same-origin เสมอ — ดู ApiConfig.cs")]
    [SerializeField] private string baseUrl = "http://localhost:3000";

    private string Url(string path) => ApiConfig.Url(baseUrl, path);

    // ผลของเซสชันเดียว — ใช้ตอนกลับจากเว็บบำบัดพร้อม ?session=<id>
    public void GetSession(string sessionId,
        Action<TherapySessionResult> onSuccess, Action<string> onError)
    {
        StartCoroutine(Get(
            Url("/api/therapy-sessions/" + UnityWebRequest.EscapeURL(sessionId)),
            onSuccess, onError));
    }

    // เฉลี่ยรายท่าของผู้ป่วยคนหนึ่ง — ใช้ในหน้า Dashboard
    public void GetProgress(string patientId,
        Action<TherapyProgressResult> onSuccess, Action<string> onError)
    {
        // ต่อ _=ticks กัน cache ของเบราว์เซอร์ เหมือนที่ QuestLoader ทำ — ไม่งั้น
        // เพิ่งฝึกจบเสร็จเปิดหน้า Dashboard แล้วยังเห็นตัวเลขของเมื่อกี้
        string url = Url($"/api/progress?patient={UnityWebRequest.EscapeURL(patientId)}" +
                         $"&_={DateTime.Now.Ticks}");
        StartCoroutine(Get(url, onSuccess, onError));
    }

    private IEnumerator Get<T>(string url, Action<T> onSuccess, Action<string> onError)
    {
        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            req.SetRequestHeader("Cache-Control", "no-cache");
            yield return req.SendWebRequest();

            string text = req.downloadHandler != null ? req.downloadHandler.text : "";

            if (req.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke(ExtractError(req, text));
                yield break;
            }

            T parsed;
            try
            {
                parsed = JsonUtility.FromJson<T>(text);
            }
            catch (Exception e)
            {
                onError?.Invoke("อ่านคำตอบจากเซิร์ฟเวอร์ไม่ได้: " + e.Message);
                yield break;
            }

            if (parsed == null)
            {
                onError?.Invoke("เซิร์ฟเวอร์ตอบกลับว่าง");
                yield break;
            }

            onSuccess?.Invoke(parsed);
        }
    }

    // เหมือน AuthApi.ExtractError — พยายามอ่าน { "error": "..." } ก่อน
    private string ExtractError(UnityWebRequest req, string text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            try
            {
                ErrorResponse err = JsonUtility.FromJson<ErrorResponse>(text);
                if (err != null && !string.IsNullOrEmpty(err.error)) return err.error;
            }
            catch { /* body ไม่ใช่ JSON ที่คาดไว้ */ }
        }

        if (req.result == UnityWebRequest.Result.ConnectionError)
            return "เชื่อมต่อเซิร์ฟเวอร์ไม่ได้ (เปิด npm run dev อยู่ไหม?)";

        return "เกิดข้อผิดพลาด กรุณาลองใหม่";
    }
}
