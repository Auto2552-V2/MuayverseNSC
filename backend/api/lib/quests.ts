// Quest schedule — ตารางท่ากายภาพที่หมอสั่งให้ผู้ป่วยแต่ละคนทำในแต่ละวัน
// เก็บที่ `public.quests` (เดิมอยู่ใน frontend/.data/quests.json ซึ่งหายทุกครั้งที่ redeploy)

import { getSupabase, isUuid, toError } from "./supabase";

const TABLE = "quests";

const COLUMNS = `
  id, patient_id, day, pose_key, pose_name, pose_thai,
  start_time, end_time, reps, minutes, difficulty, coin, star, sort_order
`;

export const DAYS = [
  "monday",
  "tuesday",
  "wednesday",
  "thursday",
  "friday",
  "saturday",
  "sunday",
] as const;

export type Day = (typeof DAYS)[number];

export function isDay(v: string): v is Day {
  return (DAYS as readonly string[]).includes(v);
}

// ตัวย่อวันที่การ์ด quest ฝั่ง Unity เอาไปโชว์ใน `Day (text)`
// ต้องตรงกับ DAYS ใน frontend/app/lib/quest.ts (field `short`)
const DAY_SHORT: Record<Day, string> = {
  monday: "จ.",
  tuesday: "อ.",
  wednesday: "พ.",
  thursday: "พฤ.",
  friday: "ศ.",
  saturday: "ส.",
  sunday: "อา.",
};

const DAY_INDEX: Record<Day, number> = Object.fromEntries(
  DAYS.map((d, i) => [d, i])
) as Record<Day, number>;

export type Difficulty = "easy" | "medium" | "hard";

// quest รุ่นเก่าเท่านั้น — ของใหม่หมอสั่งเป็นช่วงเวลา + จำนวนครั้งแทน
const DIFFICULTY_LABEL: Record<Difficulty, string> = {
  easy: "ง่าย",
  medium: "กลาง",
  hard: "ยาก",
};

export type QuestRow = {
  id: string;
  patient_id: string;
  day: Day;
  pose_key: string;
  pose_name: string;
  pose_thai: string;
  start_time: string | null; // "17:00:00" — ช่วงเวลาที่หมอกำหนด
  end_time: string | null;
  reps: number | null; // จำนวนครั้ง
  minutes: number | null; // legacy (quest ที่สั่งไว้ก่อนเปลี่ยนระบบ)
  difficulty: Difficulty | null; // legacy
  coin: number;
  star: number;
  sort_order: number;
};

// Postgres `time` มาเป็น "17:00:00" — การ์ดในเกมโชว์ 17.00-19.00
function timeRange(start: string | null, end: string | null): string {
  if (!start || !end) return "";
  const fmt = (t: string) => t.slice(0, 5).replace(":", ".");
  return `${fmt(start)}-${fmt(end)}`;
}

// รูปที่ QuestLoader.cs กิน — `time` กับ `count` เป็น string เพราะฝั่ง Unity
// ประกาศไว้แบบนั้น (ดู class Quest ใน QuestLoader.cs)
//
// `id` ไม่มีใน class Quest ฝั่ง Unity ตอนนี้ JsonUtility จะข้ามไปเฉย ๆ
// แต่ต้องเพิ่มเข้าไปถ้าจะใช้ POST /quests/:id/complete
export function toUnity(q: QuestRow) {
  const range = timeRange(q.start_time, q.end_time);
  return {
    id: q.id,
    day: q.day,
    dayShort: DAY_SHORT[q.day] ?? q.day,
    poseName: q.pose_name,
    poseThai: q.pose_thai,
    poseKey: q.pose_key,
    // ถ้าเป็น quest รุ่นเก่าที่ยังไม่มีช่วงเวลา ให้โชว์ "N นาที" แบบเดิมไปก่อน
    time: range || (q.minutes != null ? `${q.minutes} นาที` : ""),
    startTime: q.start_time ? q.start_time.slice(0, 5) : "",
    endTime: q.end_time ? q.end_time.slice(0, 5) : "",
    count:
      q.reps && q.reps > 0
        ? String(q.reps)
        : q.difficulty
          ? (DIFFICULTY_LABEL[q.difficulty] ?? q.difficulty)
          : "",
    reps: q.reps ?? 0,
    coin: q.coin,
    star: q.star,
  };
}

// เรียงทั้งสัปดาห์ให้เป็นลำดับเดียวกับที่ Unity เติมลง Quest_1, Quest_2, …
// จันทร์มาก่อนอังคารเสมอ ไม่ใช่ลำดับที่หมอกดเพิ่มในเว็บ
export function byWeekOrder(rows: QuestRow[]): QuestRow[] {
  return [...rows].sort(
    (a, b) => DAY_INDEX[a.day] - DAY_INDEX[b.day] || a.sort_order - b.sort_order
  );
}

export async function getQuestsForPatient(
  patientId: string,
  day?: Day
): Promise<QuestRow[]> {
  if (!isUuid(patientId)) return [];   // ดูเหตุผลใน supabase.ts (isUuid)

  let query = getSupabase()
    .from(TABLE)
    .select(COLUMNS)
    .eq("patient_id", patientId)
    .eq("active", true)
    // created_at เป็นตัวตัดสินรอง เพราะ sort_order default เป็น 0 เท่ากันหมด
    // ถ้าไม่ใส่ ลำดับการ์ดในวันเดียวกันจะสลับไปมาทุกครั้งที่ Unity poll
    .order("sort_order", { ascending: true })
    .order("created_at", { ascending: true });

  if (day) query = query.eq("day", day);

  const { data, error } = await query.returns<QuestRow[]>();
  if (error) throw toError(error);
  return data ?? [];
}

export async function getQuestById(id: string): Promise<QuestRow | null> {
  if (!isUuid(id)) return null;

  const { data, error } = await getSupabase()
    .from(TABLE)
    .select(COLUMNS)
    .eq("id", id)
    .maybeSingle<QuestRow>();
  if (error) throw toError(error);
  return data;
}
