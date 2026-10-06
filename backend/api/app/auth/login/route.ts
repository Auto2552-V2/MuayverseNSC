// POST /api/auth/login — เข้าสู่ระบบจากหน้า Login_Page ในเกม
// body: { email, password }

import { findCredentialsByEmail, toProfile } from "@/lib/patients";
import { verifyPassword } from "@/lib/password";
import { storageErrorResponse } from "@/lib/supabase";

export async function POST(request: Request) {
  const body = await request.json().catch(() => null);
  if (!body) {
    return Response.json({ error: "invalid json body" }, { status: 400 });
  }

  const { email, password } = body as { email?: string; password?: string };

  const emailNormalized = (email ?? "").trim().toLowerCase();
  if (!emailNormalized || !password) {
    return Response.json(
      { error: "email and password are required" },
      { status: 400 }
    );
  }

  // ข้อความเดียวกันทั้งกรณีไม่มี user และรหัสผิด เพื่อไม่ให้เดาได้ว่าอีเมลนี้สมัครไว้ไหม
  const invalid = () =>
    Response.json({ error: "email or password is incorrect" }, { status: 401 });

  try {
    const patient = await findCredentialsByEmail(emailNormalized);
    if (!patient) return invalid();

    const ok = verifyPassword(
      password,
      patient.password_salt,
      patient.password_hash
    );
    if (!ok) return invalid();

    // toProfile หยิบเฉพาะคอลัมน์สาธารณะ salt/hash ไม่หลุดออกไป
    return Response.json(toProfile(patient));
  } catch (e) {
    return storageErrorResponse(e);
  }
}
