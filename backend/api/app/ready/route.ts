// GET /ready — readiness probe (แตะ Supabase จริง)
//
// ใช้ตอนไล่ปัญหา: 200 = ต่อ Supabase ได้และตารางมีจริง
//                503 = env ไม่ครบ / ยังไม่ได้รัน schema.sql / ต่อไม่ติด
//
// อย่าเอาไปผูกกับ healthcheck ใน docker-compose — ดูเหตุผลใน app/health/route.ts

import { getSupabase, isNotConfigured, isTableMissing } from "@/lib/supabase";

export const dynamic = "force-dynamic";

export async function GET() {
  try {
    // นับแบบ head ไม่ดึงแถวจริง ถูกที่สุดที่ยังพิสูจน์ว่าตารางเข้าถึงได้
    const { error } = await getSupabase()
      .from("patients")
      .select("id", { count: "exact", head: true });

    if (error) {
      return Response.json(
        { status: "error", reason: error.message },
        { status: 503 }
      );
    }
    return Response.json({ status: "ready" });
  } catch (e) {
    const reason = isNotConfigured(e)
      ? "SUPABASE_URL / SUPABASE_SERVICE_ROLE_KEY ยังไม่ได้ตั้ง"
      : isTableMissing(e)
        ? "ยังไม่ได้รัน supabase/schema.sql"
        : e instanceof Error
          ? e.message
          : "unknown";
    return Response.json({ status: "error", reason }, { status: 503 });
  }
}
