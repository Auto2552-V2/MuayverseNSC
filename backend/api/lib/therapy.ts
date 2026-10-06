// เซสชันกายภาพจากเว็บ AI-Rehab — บันทึกผล + จ่ายรางวัล + สรุปความคืบหน้ารายท่า
//
// กติกาเดียวกับ rewards.ts: **จำนวนเหรียญ/ดาวตัดสินที่นี่ฝั่งเซิร์ฟเวอร์เท่านั้น**
// body ที่เว็บส่งมาบอกได้แค่ "ทำท่าอะไร กี่ครั้ง แม่นแค่ไหน" — ไม่เคยบอกว่าจะได้กี่เหรียญ
// (เว็บ AI-Rehab ยังไม่มีระบบ auth ใครเปิด DevTools ก็ยิง POST เองได้)
//
// เก็บลง quest_completions ตัวเดียวกับ quest ที่หมอสั่ง ต่างกันที่ source='therapy'
// และ quest_id เป็น null — ดู supabase/2026-08-16-therapy-session.sql

import { getSupabase, isUuid, toError } from "./supabase";
import { bangkokToday, getBalance } from "./rewards";

// ─────────────────────────────────────────────────────────────
//  ท่าที่เว็บ AI-Rehab ฝึกได้ — ตรงกับ 3 แถวใน Progress overview ของหน้า Dashboard
//
//  key ต้องตรงกับ THERAPY_POSES ใน AI-Rehab/lib/therapy-poses.ts เป๊ะ ๆ
//  (ฝั่งโน้นแปลง plan ของเซสชัน left/right/both → key พวกนี้)
//
//  ลำดับในอาร์เรย์ = ลำดับที่ Unity เติมลงแถว left/right/combo ตาม index
//  ดู ProgressOverviewLoader.cs — อย่าสลับตำแหน่งโดยไม่แก้ฝั่งเกมด้วย
// ─────────────────────────────────────────────────────────────
export const THERAPY_POSES = [
  {
    key: "shoulder_abduction_left",
    name: "Shoulder mobility Left",
    thai: "ยกแขนซ้ายออกด้านข้าง",
  },
  {
    key: "shoulder_abduction_right",
    name: "Shoulder mobility Right",
    thai: "ยกแขนขวาออกด้านข้าง",
  },
  {
    key: "shoulder_abduction_combo",
    name: "Shoulder mobility Combo",
    thai: "ยกแขนออกด้านข้างสองข้าง",
  },
] as const;

export type TherapyPoseKey = (typeof THERAPY_POSES)[number]["key"];

export function isTherapyPose(key: string): key is TherapyPoseKey {
  return THERAPY_POSES.some((p) => p.key === key);
}

export function poseName(key: string): string {
  return THERAPY_POSES.find((p) => p.key === key)?.name ?? key;
}

// รางวัลต่อเซสชันที่ทำจบ — ปักไว้ตรงนี้ที่เดียว ทั้งเว็บและเกมอ่านค่าจริงจาก
// response ไม่มีใครฮาร์ดโค้ด 50/5 ไว้ในฝั่ง client
export const THERAPY_REWARD = { coin: 50, star: 5 } as const;

// ─────────────────────────────────────────────────────────────
//  บันทึกเซสชัน
// ─────────────────────────────────────────────────────────────

export type TherapySessionInput = {
  patientId: string;
  poseKey: TherapyPoseKey;
  /** 0-100 จากโมเดลให้คะแนนท่า — null = ฝึกจบแต่ประเมินไม่ได้ ไม่นับเข้าค่าเฉลี่ย */
  accuracy: number | null;
  reps: number;
  durationS: number;
  completed: boolean;
};

export type TherapySession = {
  sessionId: string;
  patientId: string;
  poseKey: string;
  poseName: string;
  /** 0-100 ปัดเป็น int แล้ว — เป็น 0 เมื่อ scored=false ให้ JsonUtility ฝั่ง Unity อ่านได้ */
  accuracy: number;
  /** false = ประเมินคุณภาพท่าไม่ได้ ตัวเลข accuracy ข้างบนไม่มีความหมาย */
  scored: boolean;
  reps: number;
  completed: boolean;
  coinEarned: number;
  starEarned: number;
  coins: number; // ยอดรวมล่าสุด เอาไปอัปเดตแถบบนสุดของเกมได้เลย
  stars: number;
  /** true = วันนี้เคยได้รางวัลของท่านี้ไปแล้ว เซสชันนี้เลยบันทึกแต่ไม่จ่ายซ้ำ */
  alreadyClaimedToday: boolean;
  completedAt: string;
};

type CompletionRow = {
  id: string;
  patient_id: string;
  pose_key: string;
  pose_name: string;
  accuracy: number | null;
  reps_done: number | null;
  completed: boolean | null;
  coin_earned: number | null;
  star_earned: number | null;
  completed_at: string;
};

const ROW_COLUMNS =
  "id, patient_id, pose_key, pose_name, accuracy, reps_done, completed, coin_earned, star_earned, completed_at";

// ชน quest_completions_therapy_one_claim_per_day = วันนี้เคลมท่านี้ไปแล้ว
class AlreadyClaimedToday extends Error {}

async function insertRow(
  input: TherapySessionInput,
  claimDay: string | null
): Promise<CompletionRow> {
  const earned = claimDay !== null;

  const { data, error } = await getSupabase()
    .from("quest_completions")
    .insert({
      patient_id: input.patientId,
      quest_id: null, // ผู้ป่วยกดฝึกเอง ไม่ได้มาจากตารางที่หมอสั่ง
      source: "therapy",
      pose_key: input.poseKey,
      pose_name: poseName(input.poseKey),
      accuracy: input.accuracy,
      completed: input.completed,
      reps_done: input.reps,
      duration_s: input.durationS,
      coin_earned: earned ? THERAPY_REWARD.coin : 0,
      star_earned: earned ? THERAPY_REWARD.star : 0,
      claim_day: claimDay,
    })
    .select(ROW_COLUMNS)
    .single<CompletionRow>();

  if (error) {
    // 23505 = ชน quest_completions_therapy_one_claim_per_day
    // ปล่อยขึ้นไปให้ recordTherapySession ตัดสินใจว่าจะบันทึกซ้ำแบบไม่เอารางวัล
    if (error.code === "23505") throw new AlreadyClaimedToday();
    throw toError(error);
  }
  return data;
}

/**
 * บันทึก 1 เซสชันที่เล่นจบจากเว็บ AI-Rehab แล้วจ่ายรางวัลถ้าเป็นครั้งแรกของวัน
 *
 * ฝึกซ้ำในวันเดียวกันไม่ได้ถูกปฏิเสธ — ยังบันทึกไว้ครบ (ค่าเฉลี่ยในหน้า Dashboard
 * และกราฟฝั่งหมอต้องนับทุกครั้งที่ฝึกจริง) แค่ไม่จ่ายเหรียญ/ดาวซ้ำเท่านั้น
 */
export async function recordTherapySession(
  input: TherapySessionInput
): Promise<TherapySession> {
  const sb = getSupabase();

  // ทำไม่ครบไม่มีสิทธิ์รับรางวัลตั้งแต่แรก claim_day จึงเป็น null
  // (unique index ครอบเฉพาะแถวที่ completed and claim_day is not null)
  let claimDay: string | null = input.completed ? bangkokToday() : null;
  let alreadyClaimedToday = false;
  let row: CompletionRow;

  try {
    row = await insertRow(input, claimDay);
  } catch (e) {
    if (!(e instanceof AlreadyClaimedToday)) throw e;
    // วันนี้เคลมท่านี้ไปแล้ว — บันทึกเซสชันไว้เฉย ๆ ไม่เอารางวัล
    claimDay = null;
    alreadyClaimedToday = true;
    row = await insertRow(input, null);
  }

  // ให้ผู้ป่วยขึ้นบนสุดของรายการ "เล่นล่าสุด" ฝั่งหมอ เหมือน quest ปกติ
  await sb
    .from("patients")
    .update({ last_active_at: new Date().toISOString() })
    .eq("id", input.patientId);

  const coinEarned = row.coin_earned ?? 0;
  const starEarned = row.star_earned ?? 0;

  if (coinEarned === 0 && starEarned === 0) {
    const balance = await getBalance(input.patientId);
    return toSession(row, { ...balance, alreadyClaimedToday });
  }

  // grant_reward() เขียน ledger + ขยับยอดใน transaction เดียว (ดู schema.sql)
  const { data: patient, error: rpcError } = await sb
    .rpc("grant_reward", {
      p_patient: input.patientId,
      p_coins: coinEarned,
      p_stars: starEarned,
      p_reason: "therapy",
      p_ref: row.id,
      p_note: row.pose_name,
    })
    .single<{ coins: number; stars: number }>();

  if (rpcError) throw toError(rpcError);

  return toSession(row, {
    coins: patient.coins,
    stars: patient.stars,
    alreadyClaimedToday,
  });
}

function toSession(
  row: CompletionRow,
  extra: { coins: number; stars: number; alreadyClaimedToday: boolean }
): TherapySession {
  return {
    sessionId: row.id,
    patientId: row.patient_id,
    poseKey: row.pose_key,
    poseName: row.pose_name,
    accuracy: row.accuracy ?? 0,
    scored: row.accuracy !== null,
    reps: row.reps_done ?? 0,
    completed: row.completed ?? false,
    coinEarned: row.coin_earned ?? 0,
    starEarned: row.star_earned ?? 0,
    completedAt: row.completed_at,
    ...extra,
  };
}

/** อ่านเซสชันเดียวกลับมา — เกมใช้ตอนกลับจากเว็บเพื่อโชว์หน้า Reward */
export async function getTherapySession(
  id: string
): Promise<TherapySession | null> {
  if (!isUuid(id)) return null;

  const { data, error } = await getSupabase()
    .from("quest_completions")
    .select(ROW_COLUMNS)
    .eq("id", id)
    .eq("source", "therapy")
    .maybeSingle<CompletionRow>();

  if (error) throw toError(error);
  if (!data) return null;

  const balance = await getBalance(data.patient_id);
  // อ่านย้อนหลังบอกไม่ได้ว่าตอนนั้นชนกติกาวันละครั้งไหม — coin_earned = 0
  // ทั้งที่ทำจบ แปลว่าไม่ได้รางวัล ซึ่งเป็นข้อมูลเดียวที่เกมต้องใช้
  return toSession(data, {
    ...balance,
    alreadyClaimedToday: !!data.completed && (data.coin_earned ?? 0) === 0,
  });
}

// ─────────────────────────────────────────────────────────────
//  ความคืบหน้ารายท่า — ป้อน Progress overview ในหน้า Dashboard ของเกม
// ─────────────────────────────────────────────────────────────

export type PoseProgress = {
  poseKey: string;
  poseName: string;
  poseThai: string;
  /** จำนวนครั้งที่ฝึกท่านี้ (นับเฉพาะเซสชันที่ทำจบ) */
  sessions: number;
  /** ในนั้นมีกี่ครั้งที่ประเมินคุณภาพท่าได้จริง — 0 แปลว่าค่าเฉลี่ยยังไม่มีความหมาย */
  scoredSessions: number;
  /** ค่าเฉลี่ย accuracy ของท่านี้ ปัดเป็นจำนวนเต็ม 0-100 — ยังไม่เคยฝึก = 0 */
  averageAccuracy: number;
  /** ครั้งล่าสุดที่ประเมินได้ ได้กี่ % — ยังไม่มี = 0 */
  lastAccuracy: number;
};

export type TherapyProgress = {
  patientId: string;
  poses: PoseProgress[];
  /** เฉลี่ยรวมทุกท่า ปัดเป็นจำนวนเต็ม */
  overallAccuracy: number;
  totalSessions: number;
};

type ProgressRow = {
  pose_key: string;
  accuracy: number | null;
  completed_at: string;
};

/**
 * เฉลี่ยของแต่ละท่า = ผลรวม accuracy ของท่านั้น ÷ จำนวนครั้งที่ฝึกท่านั้น
 *
 * คืนครบ 3 ท่าเสมอถึงจะยังไม่เคยฝึก — ฝั่งเกมจะได้วาดแถบ 0% ไว้ ไม่ใช่ซ่อนแถวหาย
 * ไปเฉย ๆ แล้วคนเล่นงงว่าท่าที่เหลืออยู่ไหน
 */
export async function getTherapyProgress(
  patientId: string
): Promise<TherapyProgress> {
  const empty: TherapyProgress = {
    patientId,
    poses: THERAPY_POSES.map((p) => ({
      poseKey: p.key,
      poseName: p.name,
      poseThai: p.thai,
      sessions: 0,
      scoredSessions: 0,
      averageAccuracy: 0,
      lastAccuracy: 0,
    })),
    overallAccuracy: 0,
    totalSessions: 0,
  };

  if (!isUuid(patientId)) return empty;

  const { data, error } = await getSupabase()
    .from("quest_completions")
    .select("pose_key, accuracy, completed_at")
    .eq("patient_id", patientId)
    .eq("source", "therapy")
    .eq("completed", true)
    .order("completed_at", { ascending: false })
    .returns<ProgressRow[]>();

  if (error) throw toError(error);

  const rows = data ?? [];
  let sum = 0;
  let count = 0;

  const poses = THERAPY_POSES.map((pose) => {
    const mine = rows.filter((r) => r.pose_key === pose.key);
    // ครั้งที่ประเมินไม่ได้ยังนับเป็น "ฝึกไปแล้ว" แต่ไม่ถ่วงค่าเฉลี่ย (accuracy เป็น null)
    const scored = mine.filter((r) => r.accuracy !== null);
    const total = scored.reduce((acc, r) => acc + (r.accuracy ?? 0), 0);

    sum += total;
    count += scored.length;

    return {
      poseKey: pose.key,
      poseName: pose.name,
      poseThai: pose.thai,
      sessions: mine.length,
      scoredSessions: scored.length,
      // ปัดเป็น int ตรงนี้ที่เดียว — ฝั่งเกมเอาไปโชว์ตรง ๆ ได้เลยไม่ต้องปัดซ้ำ
      averageAccuracy: scored.length ? Math.round(total / scored.length) : 0,
      // เรียงใหม่→เก่ามาแล้ว แถวแรกที่ประเมินได้คือครั้งล่าสุดที่มีคะแนน
      lastAccuracy: scored.length ? Math.round(scored[0].accuracy ?? 0) : 0,
    };
  });

  return {
    patientId,
    poses,
    overallAccuracy: count ? Math.round(sum / count) : 0,
    totalSessions: poses.reduce((acc, p) => acc + p.sessions, 0),
  };
}
