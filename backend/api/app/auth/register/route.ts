// POST /api/auth/register — สมัครสมาชิกจากหน้า Signin_Page ในเกม
// body: { name, email, password }
//
// เส้นทางไฟล์เป็น app/auth/register ไม่ใช่ app/api/auth/register เพราะ reverse
// proxy ของงานตัด /api ออกก่อนส่งเข้ามา ตอน dev มี rewrite ใน next.config.ts
// พา /api/* มาที่นี่ให้ URL ฝั่ง Unity เหมือนกันทั้งสองที่

import { createPatient, EmailTakenError, toProfile } from "@/lib/patients";
import { storageErrorResponse } from "@/lib/supabase";

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export async function POST(request: Request) {
  const body = await request.json().catch(() => null);
  if (!body) {
    return Response.json({ error: "invalid json body" }, { status: 400 });
  }

  const { name, email, password } = body as {
    name?: string;
    email?: string;
    password?: string;
  };

  if (!name || typeof name !== "string" || name.trim().length === 0) {
    return Response.json({ error: "name is required" }, { status: 400 });
  }

  const emailNormalized = (email ?? "").trim().toLowerCase();
  if (!EMAIL_RE.test(emailNormalized)) {
    return Response.json({ error: "email is invalid" }, { status: 400 });
  }

  if (!password || password.length < 8) {
    return Response.json(
      { error: "password must be at least 8 characters" },
      { status: 400 }
    );
  }

  try {
    const patient = await createPatient({
      username: name,
      email: emailNormalized,
      password,
    });
    return Response.json(toProfile(patient), { status: 201 });
  } catch (e) {
    if (e instanceof EmailTakenError) {
      return Response.json({ error: "email already registered" }, { status: 409 });
    }
    return storageErrorResponse(e);
  }
}
