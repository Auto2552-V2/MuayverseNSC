// GET /api/progress?patient=<patientId> — ป้อน Progress overview ในหน้า Dashboard ของเกม
//
// ตอบ: { patientId, poses: [{ poseKey, poseName, poseThai, sessions,
//                             averageAccuracy, lastAccuracy }], overallAccuracy, totalSessions }
//
// `poses` คืนครบทั้ง 3 ท่าเสมอและเรียงตาม THERAPY_POSES คงที่ (left, right, combo)
// ProgressOverviewLoader.cs เติมลงแถวตาม index ได้เลย — เหตุผลเดียวกับ `schedule`
// ของ /api/quests คือ JsonUtility อ่าน object ที่ key ไม่ตายตัวไม่ได้
//
// ค่า averageAccuracy ปัดเป็น int มาจากฝั่งนี้แล้ว ฝั่งเกมไม่ต้องปัดซ้ำ

import { getPatientById } from "@/lib/patients";
import { getTherapyProgress } from "@/lib/therapy";
import { storageErrorResponse } from "@/lib/supabase";

export const dynamic = "force-dynamic";

export async function GET(request: Request) {
  const url = new URL(request.url);
  const patientId = url.searchParams.get("patient");

  if (!patientId) {
    return Response.json({ error: "missing ?patient=<patientId>" }, { status: 400 });
  }

  try {
    const patient = await getPatientById(patientId);
    if (!patient) {
      return Response.json({ error: "patient not found" }, { status: 404 });
    }

    return Response.json(await getTherapyProgress(patientId));
  } catch (e) {
    return storageErrorResponse(e);
  }
}
