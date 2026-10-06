using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// แจ้งผลมินิเกมให้ backend แล้วรับยอดเหรียญ/ดาวล่าสุดกลับมา
//
//   POST /api/minigame/complete   — { patientId, game, won }
//
// เกมบอกได้แค่ "เกมไหน ชนะไหม" จำนวนเหรียญกำหนดฝั่งเซิร์ฟเวอร์เสมอ
// (ดู ticta-deploy/api/app/minigame/complete/route.ts) ถ้าปล่อยให้ client
// ส่งตัวเลขมาเอง ใครแก้ request ก็ปั๊มเหรียญได้ไม่จำกัด
//
// รูปแบบเดียวกับ AuthApi/TherapyApi — แปะไว้บน GameObject ที่ active ตลอด
// แล้วให้สคริปต์ที่จบแมตช์อ้างถึง

[System.Serializable]
public class MinigameRewardResult
{
    public string completionId;
    public int coinEarned;   // รอบนี้ได้เพิ่มกี่เหรียญ (แพ้ = 0)
    public int starEarned;
    public int coins;        // ยอดรวมล่าสุด เอาไปอัปเดตแถบบนสุดได้เลย
    public int stars;
}

[System.Serializable]
public class MinigameCompleteRequest
{
    public string patientId;
    public string game;
    public bool won;
}

public class MinigameApi : MonoBehaviour
{
    [Header("Backend")]
    [Tooltip("ใช้เฉพาะตอนรันใน Editor/PC (dev คือ http://localhost:3000) " +
             "WebGL build ไม่สนใจค่านี้ ยิง same-origin เสมอ — ดู ApiConfig.cs")]
    [SerializeField] private string baseUrl = "http://localhost:3000";

    private string Url(string path) => ApiConfig.Url(baseUrl, path);

    /// <summary>
    /// แจ้งว่าเล่นมินิเกมจบแล้ว — แพ้ก็ส่ง เพราะฝั่งหมอต้องเห็นว่าผู้ป่วยได้เล่น
    /// </summary>
    /// <param name="game">คีย์ของเกม ต้องตรงกับ REWARDS ฝั่งเซิร์ฟเวอร์ เช่น "sparring"</param>
    public void Complete(string game, bool won,
        Action<MinigameRewardResult> onSuccess, Action<string> onError)
    {
        if (!UserSession.IsLoggedIn)
        {
            onError?.Invoke("ยังไม่ได้ล็อกอิน — ไม่รู้ว่าจะจ่ายเหรียญให้ใคร");
            return;
        }

        string json = JsonUtility.ToJson(new MinigameCompleteRequest
        {
            patientId = UserSession.UserId,
            game = game,
            won = won,
        });

        StartCoroutine(PostJson(Url("/api/minigame/complete"), json, onSuccess, onError));
    }

    private IEnumerator PostJson(string url, string json,
        Action<MinigameRewardResult> onSuccess, Action<string> onError)
    {
        using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

            string text = req.downloadHandler != null ? req.downloadHandler.text : "";

            if (req.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke(ExtractError(req, text));
                yield break;
            }

            MinigameRewardResult parsed;
            try
            {
                parsed = JsonUtility.FromJson<MinigameRewardResult>(text);
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
