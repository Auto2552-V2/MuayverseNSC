// GET  /api/users/:id — ดึง profile มาแสดงในหน้า Profile_Page
// PUT  /api/users/:id — บันทึกที่แก้จาก popup Edit Profile
//
// ProfileEditor.cs ฝั่ง Unity เรียกสองเส้นนี้ผ่าน AuthApi.GetProfile / UpdateProfile

import {
  EmailTakenError,
  getPatientById,
  NotFoundError,
  toProfile,
  updatePatientProfile,
} from "@/lib/patients";
import { storageErrorResponse } from "@/lib/supabase";

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export async function GET(
  _request: Request,
  { params }: { params: Promise<{ id: string }> }
) {
  const { id } = await params;

  try {
    const patient = await getPatientById(id);
    if (!patient) {
      return Response.json({ error: "user not found" }, { status: 404 });
    }
    return Response.json(toProfile(patient));
  } catch (e) {
    return storageErrorResponse(e);
  }
}

export async function PUT(
  request: Request,
  { params }: { params: Promise<{ id: string }> }
) {
  const { id } = await params;

  const body = await request.json().catch(() => null);
  if (!body || typeof body !== "object") {
    return Response.json({ error: "invalid json body" }, { status: 400 });
  }

  const patch = body as Record<string, unknown>;

  if (typeof patch.email === "string" && patch.email.trim()) {
    if (!EMAIL_RE.test(patch.email.trim().toLowerCase())) {
      return Response.json({ error: "email is invalid" }, { status: 400 });
    }
  }

  // ว่าง = ไม่เปลี่ยนรหัส (ProfileEditor ส่งช่องรหัสผ่านมาเสมอ ว่างบ้างไม่ว่างบ้าง)
  let newPassword: string | undefined;
  if (typeof patch.password === "string" && patch.password.length > 0) {
    if (patch.password.length < 8) {
      return Response.json(
        { error: "password must be at least 8 characters" },
        { status: 400 }
      );
    }
    newPassword = patch.password;
  }

  try {
    const patient = await updatePatientProfile(id, patch, newPassword);
    return Response.json(toProfile(patient));
  } catch (e) {
    if (e instanceof NotFoundError) {
      return Response.json({ error: "user not found" }, { status: 404 });
    }
    if (e instanceof EmailTakenError) {
      return Response.json({ error: "email already registered" }, { status: 409 });
    }
    return storageErrorResponse(e);
  }
}
