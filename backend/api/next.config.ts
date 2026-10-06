import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // Docker: build ออกมาเป็น server เล็ก ๆ พร้อม dependency เท่าที่ใช้จริง
  // ไม่ต้องขน node_modules ทั้งก้อนเข้า image (build เร็วขึ้นมากใน CI ที่จำกัด 20 นาที)
  output: "standalone",

  // ปักหมุด root ไว้ที่โฟลเดอร์นี้
  // ไม่ตั้ง Next จะไล่หา lockfile ขึ้นไปข้างบนแล้วเดา root ผิด (เจอ
  // D:\ticta\package-lock.json ที่ไม่เกี่ยวข้อง) ซึ่ง standalone ใช้ค่านี้
  // ไล่ trace ไฟล์ที่ต้องก๊อปเข้า image — เดาผิดแล้วได้ image ที่รันไม่ขึ้น
  turbopack: { root: process.cwd() },

  async rewrites() {
    return [
      // Route ในแอปนี้อยู่ที่ระดับบนสุด (app/auth/login, app/users/[id], ...)
      // เพราะ reverse proxy ของงาน "ตัด /api ออก" ก่อนส่งเข้า container:
      //
      //   เบราว์เซอร์เรียก  https://team12.aiforthai.in.th/api/auth/login
      //   container ได้รับ  /auth/login
      //
      // ตอน dev ไม่มี proxy ตัวนั้น rewrite นี้เลยทำหน้าที่แทน ทำให้ Unity ใช้ URL
      // หน้าตาเดียวกัน ({base}/api/auth/login) ได้ทั้งในเครื่องและบนเซิร์ฟเวอร์
      { source: "/api/:path*", destination: "/:path*" },
    ];
  },
};

export default nextConfig;
