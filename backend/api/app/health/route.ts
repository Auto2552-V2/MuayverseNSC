// GET /health — liveness probe
//
// เรียกตรงเข้า container ไม่ผ่าน proxy (proxy ตัด /api ให้เส้นอื่น แต่เส้นนี้
// deploy job ยิงตรงที่ http://127.0.0.1:${BASE_1}/health และ healthcheck ใน
// docker-compose ก็ยิงเส้นเดียวกัน)
//
// ตั้งใจ "ไม่" แตะ Supabase — ถ้าเช็ค Supabase ด้วย พอ Supabase สะดุดแวบเดียว
// deploy job จะ fail ทั้งที่แอปยังปกติดี อยากรู้ว่าต่อ DB ติดไหมใช้ /ready

export const dynamic = "force-dynamic";

export async function GET() {
  return Response.json({ status: "ok" });
}
