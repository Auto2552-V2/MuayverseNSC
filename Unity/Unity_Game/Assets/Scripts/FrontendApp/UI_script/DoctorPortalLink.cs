using UnityEngine;
using UnityEngine.UI;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

// ปุ่ม "แพทย์/นักกายภาพบำบัด" ในหน้า ChooseMode — พาออกจากเกมไปหน้า login ของเว็บหมอ
//
// บนเซิร์ฟเวอร์ของงาน เกมกับเว็บหมอเป็นโดเมนเดียวกัน (same-origin) ต่างกันแค่ path
// เพราะ WebGL build ถูกวางไว้ใน public/game/ ของเว็บหมอเอง
//
//     เกม      https://teamNN.aiforthai.in.th/game/
//     เว็บหมอ   https://teamNN.aiforthai.in.th/login
//
// WebGL build จึงส่ง path ล้วน ("/login") ให้เบราว์เซอร์เติม origin ให้เอง — ย้ายโดเมน
// เมื่อไหร่ก็ไม่ต้อง build ใหม่ และไม่ต้องแตะ CORS เลย เหตุผลเดียวกับ ApiConfig.cs
// ตรงกับคู่มือผู้เข้าแข่งขันข้อ 6: "เรียกด้วย relative path เสมอ — hardcode host
// แล้วจะพังตอนขึ้นเซิร์ฟเวอร์"
//
// ใน Editor ไม่มี origin ให้อ้างอิง ต้องมี host เต็ม จึงใช้ค่าจาก Inspector
// (dev: เว็บหมออยู่พอร์ต 3001 — คนละตัวกับ api ที่พอร์ต 3000)

[RequireComponent(typeof(Button))]
public class DoctorPortalLink : MonoBehaviour
{
    [Header("ปลายทาง")]
    [Tooltip("path บนเว็บหมอ ต้องขึ้นต้นด้วย / — WebGL ส่งค่านี้ให้เบราว์เซอร์ตรง ๆ")]
    [SerializeField] private string path = "/login";

    [Tooltip("ใช้เฉพาะตอนรันใน Editor/PC (dev คือ http://localhost:3001) " +
             "WebGL build ไม่สนใจค่านี้ ไปแบบ same-origin เสมอ")]
    [SerializeField] private string editorBaseUrl = "http://localhost:3001";

#if UNITY_WEBGL && !UNITY_EDITOR
    // นิยามอยู่ใน Assets/Plugins/WebGL/SiteNavigation.jslib
    [DllImport("__Internal")]
    private static extern void TictaOpenSiteUrl(string url);
#endif

    // ผูก listener ให้เองตั้งแต่ Awake แบบเดียวกับ LoginController
    // ไม่ต้องไปเพิ่มใน OnClick ของปุ่มใน Inspector อีก — ใส่ซ้ำจะกลายเป็นยิงสองครั้ง
    private void Awake()
    {
        GetComponent<Button>().onClick.AddListener(Open);
    }

    public void Open()
    {
        string url = Resolve(editorBaseUrl, Normalize(path));

#if UNITY_WEBGL && !UNITY_EDITOR
        // ไม่ใช้ Application.OpenURL เพราะบน WebGL มันแปลเป็น window.open() ซึ่งโดน
        // popup blocker กินบ่อย — Unity ประมวลผลคลิกช้ากว่า event จริงของเบราว์เซอร์
        // อยู่หนึ่งเฟรม เบราว์เซอร์เลยไม่นับว่าเป็น user gesture แล้วบล็อกทิ้ง
        TictaOpenSiteUrl(url);
#else
        Application.OpenURL(url);   // Editor/PC: เปิดเบราว์เซอร์ของเครื่อง
#endif
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "/";
        string p = value.Trim();
        return p.StartsWith("/") ? p : "/" + p;
    }

    // ตรรกะเดียวกับ ApiConfig.Url แต่ไม่เรียกใช้ตัวนั้น เพราะคนละ base กัน —
    // ApiConfig ชี้ service `api` (3000) ส่วนตรงนี้ชี้เว็บหมอ (3001) และ path
    // ที่ส่งเข้ามาก็ไม่ได้ขึ้นต้นด้วย /api/ ตามสัญญาของ ApiConfig
    private static string Resolve(string editorBase, string path)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return path;
#else
        return (editorBase ?? "").TrimEnd('/') + path;
#endif
    }
}
