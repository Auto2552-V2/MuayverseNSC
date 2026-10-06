using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// ตัวกลางยิง HTTP request ไปหา backend (register / login)
// ใส่ component นี้ไว้บน GameObject ตัวเดียว แล้วให้ทั้งหน้า Signin/Login อ้างถึง
public class AuthApi : MonoBehaviour
{
    [Header("Backend")]
    [Tooltip("ใช้เฉพาะตอนรันใน Editor/PC (dev คือ http://localhost:3000) " +
             "WebGL build ไม่สนใจค่านี้ ยิง same-origin เสมอ — ดู ApiConfig.cs")]
    [SerializeField] private string baseUrl = "http://localhost:3000";

    private string Url(string path) => ApiConfig.Url(baseUrl, path);

    // สมัครสมาชิก
    public void Register(string name, string email, string password,
        Action<AuthResponse> onSuccess, Action<string> onError)
    {
        string json = JsonUtility.ToJson(new RegisterRequest
        {
            name = name,
            email = email,
            password = password
        });
        StartCoroutine(PostJson(Url("/api/auth/register"), json, onSuccess, onError));
    }

    // เข้าสู่ระบบ
    public void Login(string email, string password,
        Action<AuthResponse> onSuccess, Action<string> onError)
    {
        string json = JsonUtility.ToJson(new LoginRequest
        {
            email = email,
            password = password
        });
        StartCoroutine(PostJson(Url("/api/auth/login"), json, onSuccess, onError));
    }

    // ดึงข้อมูล profile ของ user มาแสดง
    public void GetProfile(string userId,
        Action<ProfileData> onSuccess, Action<string> onError)
    {
        StartCoroutine(SendProfile("GET", Url("/api/users/" + userId), null, onSuccess, onError));
    }

    // บันทึกข้อมูล profile ที่แก้แล้วขึ้น backend
    public void UpdateProfile(string userId, ProfileData data,
        Action<ProfileData> onSuccess, Action<string> onError)
    {
        string json = JsonUtility.ToJson(data);
        StartCoroutine(SendProfile("PUT", Url("/api/users/" + userId), json, onSuccess, onError));
    }

    // ยิง POST พร้อม body เป็น JSON แล้วเรียก callback ตามผลลัพธ์
    private IEnumerator PostJson(string url, string json,
        Action<AuthResponse> onSuccess, Action<string> onError)
    {
        using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

            string text = req.downloadHandler != null ? req.downloadHandler.text : "";

            if (req.result == UnityWebRequest.Result.Success)
            {
                AuthResponse res = JsonUtility.FromJson<AuthResponse>(text);
                onSuccess?.Invoke(res);
            }
            else
            {
                onError?.Invoke(ExtractError(req, text));
            }
        }
    }

    // ยิง request สำหรับ profile (GET ไม่มี body / PUT มี body) แล้ว parse เป็น ProfileData
    private IEnumerator SendProfile(string method, string url, string json,
        Action<ProfileData> onSuccess, Action<string> onError)
    {
        using (UnityWebRequest req = new UnityWebRequest(url, method))
        {
            if (!string.IsNullOrEmpty(json))
            {
                byte[] body = Encoding.UTF8.GetBytes(json);
                req.uploadHandler = new UploadHandlerRaw(body);
            }
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

            string text = req.downloadHandler != null ? req.downloadHandler.text : "";

            if (req.result == UnityWebRequest.Result.Success)
            {
                ProfileData res = JsonUtility.FromJson<ProfileData>(text);
                onSuccess?.Invoke(res);
            }
            else
            {
                onError?.Invoke(ExtractError(req, text));
            }
        }
    }

    // พยายามดึงข้อความ error จาก body ก่อน ถ้าไม่มีค่อยใช้ข้อความทั่วไป
    private string ExtractError(UnityWebRequest req, string text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            try
            {
                ErrorResponse err = JsonUtility.FromJson<ErrorResponse>(text);
                if (err != null && !string.IsNullOrEmpty(err.error))
                    return err.error;
            }
            catch { /* body ไม่ใช่ JSON ที่คาดไว้ */ }
        }

        if (req.result == UnityWebRequest.Result.ConnectionError)
            return "เชื่อมต่อเซิร์ฟเวอร์ไม่ได้ (เปิด npm run dev อยู่ไหม?)";

        return "เกิดข้อผิดพลาด กรุณาลองใหม่";
    }
}
