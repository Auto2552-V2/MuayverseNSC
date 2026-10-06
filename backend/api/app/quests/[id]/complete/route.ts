// POST /api/quests/:id/complete — เกมแจ้งว่าทำ quest นี้จบแล้ว
//
// body: { patientId, accuracy?, repsDone?, durationS?, completed? }
// ตอบ:  { completionId, coinEarned, starEarned, coins, stars }
//
// `coins`/`stars` ที่ตอบกลับคือยอดรวมล่าสุด เอาไปอัปเดตแถบบนสุดของเกมได้เลย
// ไม่ต้องยิงถามยอดซ้ำ
//
// จำนวนเหรียญกำหนดโดยแถว quests ฝั่งเซิร์ฟเวอร์เสมอ — body บอกได้แค่ว่า
// "ทำจบไหม แม่นแค่ไหน" ไม่ได้บอกว่าจะได้กี่เหรียญ

import { getQuestById } from "@/lib/quests";
import {
  AlreadyClaimedError,
  getBalance,
  hasClaimedToday,
  recordCompletion,
} from "@/lib/rewards";
import { storageErrorResponse } from "@/lib/supabase";

function clampInt(value: unknown, min: number, max: number, fallback: number) {
  const n = typeof value === "number" ? value : Number(value);
  if (!Number.isFinite(n)) return fallback;
  return Math.min(max, Math.max(min, Math.round(n)));
}

export async function POST(
  request: Request,
  { params }: { params: Promise<{ id: string }> }
) {
  const { id } = await params;

  const body = await request.json().catch(() => null);
  if (!body || typeof body !== "object") {
    return Response.json({ error: "invalid json body" }, { status: 400 });
  }

  const { patientId } = body as { patientId?: string };
  if (!patientId) {
    return Response.json({ error: "patientId is required" }, { status: 400 });
  }

  try {
    const quest = await getQuestById(id);
    if (!quest) {
      return Response.json({ error: "quest not found" }, { status: 404 });
    }

    // กันเคลม quest ของคนอื่น — id ของ quest เดาไม่ยากถ้าหลุดออกไป
    if (quest.patient_id !== patientId) {
      return Response.json(
        { error: "quest does not belong to this patient" },
        { status: 403 }
      );
    }

    const completed = (body as { completed?: unknown }).completed !== false;

    // quest เดิมได้เหรียญวันละครั้ง เล่นซ้ำได้ไม่จำกัดแต่ไม่ได้เหรียญเพิ่ม
    if (completed && (await hasClaimedToday(patientId, id))) {
      const balance = await getBalance(patientId);
      return Response.json(
        {
          error: "already claimed today",
          coinEarned: 0,
          starEarned: 0,
          ...balance,
        },
        { status: 409 }
      );
    }

    const result = await recordCompletion(quest, {
      accuracy: clampInt((body as { accuracy?: unknown }).accuracy, 0, 100, 0),
      repsDone: clampInt((body as { repsDone?: unknown }).repsDone, 0, 100_000, 0),
      durationS: clampInt((body as { durationS?: unknown }).durationS, 0, 86_400, 0),
      completed,
    });

    return Response.json(result, { status: 201 });
  } catch (e) {
    // เช็คข้างบนพลาดได้ถ้ามีสอง request เข้ามาพร้อมกัน — unique index เป็นด่านจริง
    if (e instanceof AlreadyClaimedError) {
      const balance = await getBalance(patientId).catch(() => ({ coins: 0, stars: 0 }));
      return Response.json(
        { error: "already claimed today", coinEarned: 0, starEarned: 0, ...balance },
        { status: 409 }
      );
    }
    return storageErrorResponse(e);
  }
}
