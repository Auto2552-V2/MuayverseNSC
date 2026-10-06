// GET /api/therapy-sessions/:id — เกมอ่านผลเซสชันกลับมาโชว์ในหน้า Reward
//
// เรียกโดย TherapyReward.cs ตอนเบราว์เซอร์กลับเข้าเกมพร้อม ?session=<id>
//
// เป็นการ "อ่าน" ล้วน ๆ ไม่แตะยอดเหรียญเลย — กด refresh ซ้ำหรือ bookmark URL นี้
// ไว้ก็ไม่ได้เหรียญเพิ่ม เพราะการจ่ายรางวัลเกิดไปแล้วตอน POST จากฝั่งเว็บ

import { getTherapySession } from "@/lib/therapy";
import { storageErrorResponse } from "@/lib/supabase";

export const dynamic = "force-dynamic";

export async function GET(
  _request: Request,
  { params }: { params: Promise<{ id: string }> }
) {
  const { id } = await params;

  try {
    const session = await getTherapySession(id);
    if (!session) {
      return Response.json({ error: "session not found" }, { status: 404 });
    }
    return Response.json(session);
  } catch (e) {
    return storageErrorResponse(e);
  }
}
