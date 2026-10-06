// ที่เดียวที่ตัดสินว่า "API อยู่ที่ไหน" — ทุกสคริปต์ที่ยิง backend ต้องผ่านตรงนี้
//
// บนเซิร์ฟเวอร์ของงาน เกมกับ API อยู่โดเมนเดียวกัน (same-origin)
//
//     เกม   https://teamNN.aiforthai.in.th/game/
//     API   https://teamNN.aiforthai.in.th/api/...
//
// WebGL build จึงยิงด้วย path ล้วน ("/api/quests") แล้วให้เบราว์เซอร์เติม origin
// ให้เอง — ได้ URL ถูกต้องโดยไม่ต้อง build ใหม่ตอนย้ายโดเมน และไม่ต้องแตะ CORS เลย
//
// ใน Editor ไม่มี origin ให้อ้างอิง ต้องมี host เต็ม จึงใช้ค่าจาก Inspector
// (ค่า dev คือ http://localhost:3000 = service `api`)
//
// path ที่ส่งเข้ามาต้องขึ้นต้นด้วย "/api/" เสมอ ทั้งสองสภาพแวดล้อมตัด prefix นี้
// ออกเหมือนกัน — dev ตัดด้วย rewrites ใน api/next.config.ts ส่วนบนเซิร์ฟเวอร์
// reverse proxy ของงานตัดให้ ปลายทางจึงเป็น route เดียวกันเป๊ะ
public static class ApiConfig
{
    public static string Url(string editorBaseUrl, string path)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return path;
#else
        return (editorBaseUrl ?? "").TrimEnd('/') + path;
#endif
    }
}
