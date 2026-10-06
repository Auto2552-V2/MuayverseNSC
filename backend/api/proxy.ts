import { NextResponse, type NextRequest } from "next/server";

// CORS — เปิดเฉพาะตอน dev
//
// (ไฟล์นี้เคยชื่อ middleware.ts — Next.js 16 เปลี่ยนชื่อ convention เป็น proxy
//  ความสามารถเหมือนเดิมทุกอย่าง)
//
// บนเซิร์ฟเวอร์ของงาน เกมกับ API อยู่โดเมนเดียวกัน (team12.aiforthai.in.th)
// เบราว์เซอร์จึงไม่ถาม CORS เลย การใส่ Allow-Origin: * ใน production เท่ากับ
// เปิดให้เว็บไหนก็ได้ยิง API ข้อมูลผู้ป่วยจากเบราว์เซอร์ของคนที่หลงเข้าไป
//
// ตอน dev ยังต้องมี เพราะถ้าเปิด WebGL build ในเครื่อง (เช่น :8080) แล้วยิงมาที่
// API (:3000) จะเป็นคนละ origin — ส่วน Unity Editor ไม่สนใจ CORS อยู่แล้ว
// เพราะไม่ใช่เบราว์เซอร์
const DEV = process.env.NODE_ENV !== "production";

const CORS = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Methods": "GET, POST, PUT, OPTIONS",
  "Access-Control-Allow-Headers": "Content-Type",
};

export function proxy(request: NextRequest) {
  if (!DEV) return NextResponse.next();

  if (request.method === "OPTIONS") {
    return new NextResponse(null, { status: 204, headers: CORS });
  }

  const res = NextResponse.next();
  for (const [k, v] of Object.entries(CORS)) res.headers.set(k, v);
  return res;
}

// ครอบทั้ง /api/* (ตอน dev ที่ยังมี prefix) และ path ระดับบนสุด
// (ตอนที่ reverse proxy ของงานตัด /api ออกไปแล้ว)
export const config = {
  matcher: ["/api/:path*", "/auth/:path*", "/users/:path*", "/quests/:path*"],
};
