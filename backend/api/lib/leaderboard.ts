// อันดับผู้เล่น — เรียงจากดาวมากไปน้อย ถ้าดาวเท่ากัน (รวมกรณี 0 ทั้งหมด)
// คนที่สมัครก่อนอยู่บน
//
// กติกาการเรียงถูกเขียนไว้ที่เดียวในไฟล์นี้ และถูกใช้ซ้ำทั้งตอนดึง "ท็อป N"
// และตอนหาอันดับของผู้เล่นคนเดียว ถ้าแยกกันเขียนสองที่แล้วหลุดไม่ตรงกันเมื่อไหร่
// ผู้เล่นจะเห็นตัวเองอยู่อันดับ 4 ในกล่อง Your_top ทั้งที่อยู่แถว #3 ของตาราง
//
// อ่านยอดจากคอลัมน์ patients.stars ไม่ใช่ SUM(reward_ledger.star_delta)
// เพราะ grant_reward() อัปเดตสองที่ใน transaction เดียวอยู่แล้ว (ดู schema.sql)
// การ SUM ทั้ง ledger ทุกครั้งที่เปิดหน้าอันดับจะช้าขึ้นเรื่อย ๆ ตามจำนวนครั้งที่เล่น

import { getSupabase, isUuid, toError } from "./supabase";

const TABLE = "patients";

// ไม่ดึง full_name มาเลย — กระดานอันดับโชว์ username อย่างเดียว
// ชื่อจริงเป็นข้อมูลส่วนตัวของผู้ป่วย ไม่ควรให้ผู้เล่นคนอื่นเห็น
const COLUMNS =
  "id, patient_code, username, avatar_url, avatar_color, stars, created_at";

type LeaderRow = {
  id: string;
  patient_code: string | null;
  username: string | null;
  avatar_url: string | null;
  avatar_color: string | null;
  stars: number | null;
  created_at: string;
};

// รูปนี้ตรงกับ class LeaderEntry ใน LeaderboardLoader.cs — JsonUtility จับคู่
// ด้วยชื่อ field เป๊ะ ๆ เปลี่ยนชื่อตรงนี้ต้องไปแก้ฝั่ง Unity ด้วย
export type LeaderEntry = {
  rank: number;
  patientId: string;
  patientCode: string;
  name: string;
  stars: number;
  avatarUrl: string;
  avatarColor: string;
};

// ค่าว่างสำหรับ me เมื่อยังไม่ล็อกอินหรือหาไม่เจอ
// ส่งเป็น object ที่ rank = 0 แทน null เพราะ JsonUtility ฝั่ง Unity อ่าน null
// เป็น object เปล่าอยู่ดี การส่งรูปเดียวกันเสมอทำให้ฝั่งเกมเช็คแค่ rank > 0 พอ
export const EMPTY_ENTRY: LeaderEntry = {
  rank: 0,
  patientId: "",
  patientCode: "",
  name: "",
  stars: 0,
  avatarUrl: "",
  avatarColor: "",
};

function toEntry(row: LeaderRow, rank: number): LeaderEntry {
  return {
    rank,
    patientId: row.id,
    patientCode: row.patient_code ?? "",
    // username เท่านั้น — ไม่ใช่ full_name
    name: (row.username ?? "").trim(),
    stars: row.stars ?? 0,
    avatarUrl: row.avatar_url ?? "",
    avatarColor: row.avatar_color || "#2563eb",
  };
}

// อันดับ 1..limit
export async function getTop(limit: number): Promise<LeaderEntry[]> {
  const { data, error } = await getSupabase()
    .from(TABLE)
    .select(COLUMNS)
    .order("stars", { ascending: false })
    .order("created_at", { ascending: true }) // ดาวเท่ากัน -> สมัครก่อนอยู่บน
    .limit(limit)
    .returns<LeaderRow[]>();

  if (error) throw toError(error);
  return (data ?? []).map((row, i) => toEntry(row, i + 1));
}

// อันดับของผู้เล่นคนเดียว
//
// นับว่ามีกี่คน "อยู่เหนือกว่า" แล้ว +1 แทนการดึงทั้งตารางมาไล่หาใน JS
// เงื่อนไข "เหนือกว่า" ต้องสะท้อน ORDER BY ข้างบนให้ตรงกันทุกตัว:
//   ดาวมากกว่า  หรือ  ดาวเท่ากันแต่ created_at น้อยกว่า (สมัครก่อน)
export async function getEntryFor(patientId: string): Promise<LeaderEntry | null> {
  // id ที่ไม่ใช่ uuid ทำให้ Postgres โยน 22P02 แล้วหน้าอันดับ 500 ทั้งหน้า
  // ทั้งที่ความจริงแค่ "ไม่พบผู้เล่นคนนี้"
  if (!isUuid(patientId)) return null;

  const sb = getSupabase();

  const { data: me, error } = await sb
    .from(TABLE)
    .select(COLUMNS)
    .eq("id", patientId)
    .maybeSingle<LeaderRow>();

  if (error) throw toError(error);
  if (!me) return null;

  const stars = me.stars ?? 0;

  const { count, error: countError } = await sb
    .from(TABLE)
    .select("id", { count: "exact", head: true })
    // created_at ใส่ double quote เพราะ timestamptz มี '+' และ ':' ซึ่ง PostgREST
    // จะแปลผิดถ้าปล่อยเปลือย
    .or(`stars.gt.${stars},and(stars.eq.${stars},created_at.lt."${me.created_at}")`);

  if (countError) throw toError(countError);

  return toEntry(me, (count ?? 0) + 1);
}
