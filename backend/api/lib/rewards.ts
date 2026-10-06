// บันทึกผลการทำ quest + จ่ายเหรียญ/ดาว
//
// กติกาสำคัญ: **จำนวนเหรียญมาจากแถว quests ในฐานข้อมูลเท่านั้น** ไม่เคยอ่านจาก
// body ที่เกมส่งมา ไม่งั้นใครก็ยิง POST ขอเหรียญเท่าไหร่ก็ได้ — WebGL build เปิด
// DevTools ดู request แล้วยิงซ้ำเองได้ทันที

import { getSupabase, toError } from "./supabase";
import type { QuestRow } from "./quests";

const OFFSET_MS = 7 * 60 * 60 * 1000; // UTC+7

// "วันนี้" ตามเวลาไทย เป็น YYYY-MM-DD — เก็บลงคอลัมน์ claim_day
// ซึ่งมี unique index (patient_id, quest_id, claim_day) คุมอยู่ ดู schema.sql
export function bangkokToday(now: Date = new Date()): string {
  return new Date(now.getTime() + OFFSET_MS).toISOString().slice(0, 10);
}

// เที่ยงคืนของ "วันนี้" ตามเวลาไทย คืนเป็น ISO ที่เป็น UTC
export function bangkokDayStartUtc(now: Date = new Date()): string {
  const shifted = new Date(now.getTime() + OFFSET_MS);
  const midnightUtc = Date.UTC(
    shifted.getUTCFullYear(),
    shifted.getUTCMonth(),
    shifted.getUTCDate()
  );
  return new Date(midnightUtc - OFFSET_MS).toISOString();
}

// เคลม quest นี้ของวันนี้ไปแล้ว — โยนจาก recordCompletion เมื่อชน unique index
export class AlreadyClaimedError extends Error {
  constructor() {
    super("ALREADY_CLAIMED");
  }
}

export async function hasClaimedToday(
  patientId: string,
  questId: string
): Promise<boolean> {
  const { data, error } = await getSupabase()
    .from("quest_completions")
    .select("id")
    .eq("patient_id", patientId)
    .eq("quest_id", questId)
    .eq("completed", true)
    .gte("completed_at", bangkokDayStartUtc())
    .limit(1);

  if (error) throw toError(error);
  return (data?.length ?? 0) > 0;
}

export type CompletionInput = {
  accuracy: number; // 0-100
  repsDone: number;
  durationS: number;
  completed: boolean;
};

export type CompletionResult = {
  completionId: string;
  coinEarned: number;
  starEarned: number;
  coins: number; // ยอดรวมหลังจ่ายแล้ว
  stars: number;
};

// บันทึก 1 ครั้งที่เล่นจบ แล้วจ่ายรางวัลถ้าทำครบ
export async function recordCompletion(
  quest: QuestRow,
  input: CompletionInput
): Promise<CompletionResult> {
  const sb = getSupabase();

  // ทำไม่ครบก็ยังบันทึกไว้ หมอจะได้เห็นว่าพยายามแล้ว แค่ไม่จ่ายเหรียญ
  const coinEarned = input.completed ? quest.coin : 0;
  const starEarned = input.completed ? quest.star : 0;

  const { data: completion, error: insertError } = await sb
    .from("quest_completions")
    .insert({
      patient_id: quest.patient_id,
      quest_id: quest.id,
      pose_name: quest.pose_name,
      accuracy: input.accuracy,
      completed: input.completed,
      reps_done: input.repsDone,
      duration_s: input.durationS,
      coin_earned: coinEarned,
      star_earned: starEarned,
      // ใส่เฉพาะครั้งที่ทำครบ — unique index ครอบเฉพาะแถวที่ completed
      // ทำไม่ครบยังบันทึกได้ไม่จำกัดครั้ง (หมอจะได้เห็นว่าพยายามกี่รอบ)
      claim_day: input.completed ? bangkokToday() : null,
    })
    .select("id")
    .single<{ id: string }>();

  if (insertError) {
    // 23505 = ชน unique index กันเคลมซ้ำ แปลว่ามี request คู่แข่งชิงบันทึกไปแล้ว
    if (insertError.code === "23505") throw new AlreadyClaimedError();
    throw toError(insertError);
  }

  // ให้ผู้ป่วยขึ้นบนสุดของรายการ "เล่นล่าสุด" ฝั่งหมอ
  await sb
    .from("patients")
    .update({ last_active_at: new Date().toISOString() })
    .eq("id", quest.patient_id);

  if (coinEarned === 0 && starEarned === 0) {
    const balance = await getBalance(quest.patient_id);
    return { completionId: completion.id, coinEarned, starEarned, ...balance };
  }

  // grant_reward() เขียน ledger + ขยับยอดใน transaction เดียว (ดู schema.sql)
  const { data: patient, error: rpcError } = await sb
    .rpc("grant_reward", {
      p_patient: quest.patient_id,
      p_coins: coinEarned,
      p_stars: starEarned,
      p_reason: "quest",
      p_ref: completion.id,
      p_note: quest.pose_name,
    })
    .single<{ coins: number; stars: number }>();

  if (rpcError) throw toError(rpcError);

  return {
    completionId: completion.id,
    coinEarned,
    starEarned,
    coins: patient.coins,
    stars: patient.stars,
  };
}

export async function getBalance(
  patientId: string
): Promise<{ coins: number; stars: number }> {
  const { data, error } = await getSupabase()
    .from("patients")
    .select("coins, stars")
    .eq("id", patientId)
    .maybeSingle<{ coins: number; stars: number }>();

  if (error) throw toError(error);
  return data ?? { coins: 0, stars: 0 };
}
