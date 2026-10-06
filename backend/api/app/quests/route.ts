// GET /api/quests?patient=<patientId>            → ทั้งสัปดาห์ (schedule[] + days{})
// GET /api/quests?patient=<patientId>&day=monday → เฉพาะวันนั้น
//
// ย้ายมาจาก frontend/app/api/quests/route.ts — เดิมอยู่ฝั่งเว็บหมอซึ่งบนเซิร์ฟเวอร์
// ถูก mount ที่ `/` ไม่ใช่ `/api/` ทำให้ QuestLoader.cs ยิงแล้วได้ 404
// endpoint ที่ Unity เรียกต้องอยู่ใน service นี้ทั้งหมด

import { getPatientById } from "@/lib/patients";
import {
  DAYS,
  byWeekOrder,
  getQuestsForPatient,
  isDay,
  toUnity,
  type Day,
} from "@/lib/quests";
import { storageErrorResponse } from "@/lib/supabase";

export const dynamic = "force-dynamic";

export async function GET(request: Request) {
  const url = new URL(request.url);
  const patientId = url.searchParams.get("patient");
  const dayParam = url.searchParams.get("day");

  if (!patientId) {
    return Response.json({ error: "missing ?patient=<patientId>" }, { status: 400 });
  }

  if (dayParam && !isDay(dayParam)) {
    return Response.json({ error: "invalid day" }, { status: 400 });
  }

  try {
    const patient = await getPatientById(patientId);
    if (!patient) {
      return Response.json({ error: "patient not found" }, { status: 404 });
    }

    const rows = await getQuestsForPatient(
      patientId,
      dayParam ? (dayParam as Day) : undefined
    );

    // รูปนี้ตรงกับ class DayQuests ใน QuestLoader.cs
    if (dayParam) {
      return Response.json({
        patientId,
        patientName: patient.username,
        day: dayParam,
        quests: rows.map(toUnity),
      });
    }

    const days = Object.fromEntries(DAYS.map((d) => [d, [] as ReturnType<typeof toUnity>[]]));
    for (const q of rows) days[q.day].push(toUnity(q));

    // `schedule` คือรูปที่ QuestLoader.cs กินจริง — แบนและเรียง จ.→อา. มาแล้ว
    // ให้ Unity เติมลง Quest_1, Quest_2, … ตาม index ได้เลย
    //
    // ต้องส่งเป็น array เพราะ JsonUtility อ่าน days{} ไม่ได้ (ไม่รองรับ
    // dictionary ที่ key ไม่ตายตัว) — days{} เก็บไว้ให้ฝั่งอื่นที่อ่าน JSON ปกติ
    const schedule = byWeekOrder(rows).map(toUnity);

    return Response.json({
      patientId,
      patientName: patient.username,
      schedule,
      days,
      dayOrder: DAYS,
    });
  } catch (e) {
    return storageErrorResponse(e);
  }
}
