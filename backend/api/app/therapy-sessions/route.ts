// POST /api/therapy-sessions — เว็บ AI-Rehab แจ้งว่าผู้ป่วยฝึกจบหนึ่งเซสชัน
//
// body: { patientId, poseKey, accuracy?, reps?, durationS?, completed? }
// ตอบ:  { sessionId, poseKey, accuracy, reps, coinEarned, starEarned, coins, stars,
//          alreadyClaimedToday }
//
// ใครเรียก: app/api/session/route.ts ของ AI-Rehab (ฝั่ง server) ไม่ใช่เบราว์เซอร์ตรง ๆ
// พอจบเซสชันเว็บยิงเส้นนี้ก่อน แล้วค่อยพาผู้ใช้กลับเกมพร้อม ?session=<sessionId>
// เกมเอา id ไป GET ค่าจริงมาโชว์ในหน้า Reward — ตัวเลขบนหน้ารางวัลจึงมาจาก
// ฐานข้อมูลเสมอ ไม่ใช่จาก URL ที่แก้ได้ใน address bar
//
// จำนวนเหรียญ/ดาวกำหนดโดย THERAPY_REWARD ฝั่งเซิร์ฟเวอร์ (ดู lib/therapy.ts)
// body บอกได้แค่ว่าทำท่าอะไร กี่ครั้ง แม่นแค่ไหน

import { getPatientById } from "@/lib/patients";
import { isTherapyPose, recordTherapySession } from "@/lib/therapy";
import { storageErrorResponse } from "@/lib/supabase";

function clampInt(value: unknown, min: number, max: number, fallback: number) {
  const n = typeof value === "number" ? value : Number(value);
  if (!Number.isFinite(n)) return fallback;
  return Math.min(max, Math.max(min, Math.round(n)));
}

// accuracy ต่างจากช่องอื่นตรงที่ "ไม่ส่งมา" กับ "ส่งมาเป็น 0" ไม่เหมือนกัน:
// null = ฝึกจบแต่โมเดลให้คะแนนไม่ทำงาน (ไม่นับเข้าค่าเฉลี่ยในหน้า Dashboard)
// 0    = วัดได้จริงแล้วได้ศูนย์
function clampAccuracy(value: unknown): number | null {
  if (value === null || value === undefined || value === "") return null;
  const n = typeof value === "number" ? value : Number(value);
  if (!Number.isFinite(n)) return null;
  return Math.min(100, Math.max(0, Math.round(n)));
}

export async function POST(request: Request) {
  const body = await request.json().catch(() => null);
  if (!body || typeof body !== "object") {
    return Response.json({ error: "invalid json body" }, { status: 400 });
  }

  const { patientId, poseKey } = body as {
    patientId?: string;
    poseKey?: string;
  };

  if (!patientId) {
    return Response.json({ error: "patientId is required" }, { status: 400 });
  }
  if (!poseKey || !isTherapyPose(poseKey)) {
    return Response.json(
      { error: "poseKey is required and must be a known therapy pose" },
      { status: 400 }
    );
  }

  try {
    // เช็คก่อน insert เพราะ foreign key violation จะกลายเป็น 500 ทั้งที่ความจริง
    // แค่ "ส่ง id ผิด" — เกิดง่ายมากเพราะ id เดินทางผ่าน query string ของเบราว์เซอร์
    const patient = await getPatientById(patientId);
    if (!patient) {
      return Response.json({ error: "patient not found" }, { status: 404 });
    }

    const session = await recordTherapySession({
      patientId,
      poseKey,
      accuracy: clampAccuracy((body as { accuracy?: unknown }).accuracy),
      reps: clampInt((body as { reps?: unknown }).reps, 0, 100_000, 0),
      durationS: clampInt((body as { durationS?: unknown }).durationS, 0, 86_400, 0),
      completed: (body as { completed?: unknown }).completed !== false,
    });

    return Response.json(session, { status: 201 });
  } catch (e) {
    return storageErrorResponse(e);
  }
}
