// Patient accounts — the game's users. Backed by `public.patients` in Supabase.
//
// The JSON shape returned by `toProfile` is a contract with Unity: JsonUtility
// matches by exact field name, and a mismatch fails *silently* with an empty
// value. Keep these keys in sync with `ProfileData` in
// TICTA_Game/Assets/Scripts/Backend2/AuthModels.cs — including `sympton`, which
// is misspelled on both sides on purpose. Renaming it means editing Unity too.

import {
  getSupabase,
  isUniqueViolation,
  isUuid,
  toError,
} from "./supabase";
import { hashPassword } from "./password";

const TABLE = "patients";

// Every read selects this set — keep in sync with PatientRow and toProfile.
// password_salt/password_hash are pulled only by findByEmail, never returned.
const COLUMNS = `
  id, patient_code, username, email, avatar_url, coins, stars
`;

export type PatientRow = {
  id: string;
  patient_code: string;
  username: string;
  email: string;
  avatar_url: string | null;
  coins: number;
  stars: number;
};

type CredentialRow = PatientRow & {
  password_salt: string;
  password_hash: string;
};

// ── ProfileData ⇄ column mapping ────────────────────────────────────────────
// Left side is what Unity sends/expects, right side is the column.
//
// ช่องข้อมูลส่วนตัว (ชื่อจริง/วันเกิด/เพศ/อายุ/เบอร์/ที่อยู่/อาการ) ถูกถอดออกจาก
// ฐานข้อมูลแล้ว เหลือแค่ชื่อผู้ใช้กับอีเมลที่จำเป็นต่อการล็อกอิน — ดู
// supabase/2026-10-05-drop-profile-fields.sql
const FIELD_TO_COLUMN = {
  name: "username",
  email: "email",
} as const;

export function toProfile(row: PatientRow) {
  return {
    id: row.id,
    patientCode: row.patient_code, // UID ที่โชว์ในเกม (0001) — ยังไม่มีใน ProfileData
    name: row.username,
    email: row.email,
    avatarUrl: row.avatar_url ?? "",
    coins: row.coins,
    stars: row.stars,
  };
}

// ── queries ────────────────────────────────────────────────────────────────

export async function findCredentialsByEmail(
  email: string
): Promise<CredentialRow | null> {
  const { data, error } = await getSupabase()
    .from(TABLE)
    .select(`${COLUMNS}, password_salt, password_hash`)
    .eq("email", email.trim().toLowerCase())
    .maybeSingle<CredentialRow>();
  if (error) throw toError(error);
  return data;
}

export async function getPatientById(id: string): Promise<PatientRow | null> {
  // id ที่ไม่ใช่ uuid = ไม่มีทางมีอยู่จริง ตอบ null ให้ route แปลงเป็น 404
  // ถ้าปล่อยผ่านไปถาม Postgres จะได้ 22P02 แล้วกลายเป็น 500
  if (!isUuid(id)) return null;

  const { data, error } = await getSupabase()
    .from(TABLE)
    .select(COLUMNS)
    .eq("id", id)
    .maybeSingle<PatientRow>();
  if (error) throw toError(error);
  return data;
}

export class EmailTakenError extends Error {
  constructor() {
    super("EMAIL_TAKEN");
  }
}

export async function createPatient(input: {
  username: string;
  email: string;
  password: string;
}): Promise<PatientRow> {
  const { salt, hash } = hashPassword(input.password);

  const { data, error } = await getSupabase()
    .from(TABLE)
    .insert({
      username: input.username.trim(),
      email: input.email.trim().toLowerCase(),
      password_salt: salt,
      password_hash: hash,
    })
    .select(COLUMNS)
    .single<PatientRow>();

  if (error) {
    if (isUniqueViolation(error)) throw new EmailTakenError();
    throw toError(error);
  }
  return data;
}

export class NotFoundError extends Error {
  constructor() {
    super("NOT_FOUND");
  }
}

// `patch` carries raw ProfileData keys straight off the request body. Only the
// keys in FIELD_TO_COLUMN are honoured — anything else (coins, stars,
// patient_code) is ignored, so the game cannot mint itself currency.
export async function updatePatientProfile(
  id: string,
  patch: Record<string, unknown>,
  newPassword?: string
): Promise<PatientRow> {
  if (!isUuid(id)) throw new NotFoundError();

  const row: Record<string, string> = {};

  for (const [field, column] of Object.entries(FIELD_TO_COLUMN)) {
    const value = patch[field];
    if (typeof value === "string") row[column] = value.trim();
  }

  if (row.email) row.email = row.email.toLowerCase();

  if (newPassword) {
    const { salt, hash } = hashPassword(newPassword);
    row.password_salt = salt;
    row.password_hash = hash;
  }

  const { data, error } = await getSupabase()
    .from(TABLE)
    .update(row)
    .eq("id", id)
    .select(COLUMNS)
    .maybeSingle<PatientRow>();

  if (error) {
    if (isUniqueViolation(error)) throw new EmailTakenError();
    throw toError(error);
  }
  if (!data) throw new NotFoundError();
  return data;
}
