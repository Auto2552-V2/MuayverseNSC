// Supabase client — server-side only.
//
// Uses the *service role* key, which bypasses Row Level Security. Every table in
// `supabase/schema.sql` has RLS on with no policies, so this key is the only way
// in. It must never reach the browser or a Unity WebGL build: no NEXT_PUBLIC_
// prefix, and this module is imported only from Route Handlers.
//
// Mirrors frontend/app/lib/supabase.ts — same project, same contract.

import { createClient, type SupabaseClient } from "@supabase/supabase-js";

// Thrown when the env vars are missing, so callers can say so instead of
// failing with a generic network error.
export const NOT_CONFIGURED = "SUPABASE_NOT_CONFIGURED";

// Thrown when the project is reachable but `supabase/schema.sql` was never run.
export const TABLE_MISSING = "SUPABASE_TABLE_MISSING";

let cached: SupabaseClient | null = null;

export function getSupabase(): SupabaseClient {
  if (cached) return cached;

  // Deliberately not NEXT_PUBLIC_ — that prefix inlines the value at build time
  // and ships it to the client. Both of these are runtime, server-only reads.
  const url = process.env.SUPABASE_URL;
  const key = process.env.SUPABASE_SERVICE_ROLE_KEY;
  if (!url || !key) throw new Error(NOT_CONFIGURED);

  cached = createClient(url, key, {
    auth: { persistSession: false, autoRefreshToken: false },
  });
  return cached;
}

export function isNotConfigured(e: unknown): boolean {
  return e instanceof Error && e.message === NOT_CONFIGURED;
}

export function isTableMissing(e: unknown): boolean {
  return e instanceof Error && e.message === TABLE_MISSING;
}

// Postgres unique-violation — email or username already registered.
export function isUniqueViolation(e: { code?: string } | null): boolean {
  return e?.code === "23505";
}

// ทุก id ในฐานข้อมูลเป็น uuid — ถ้าเทียบคอลัมน์ uuid กับสตริงที่ไม่ใช่ uuid
// Postgres จะโยน 22P02 (invalid input syntax) ซึ่งกลายเป็น 500 ทั้งที่ความจริง
// แค่ "ไม่พบ" เท่านั้น เกิดจริงมาแล้วตอนเกมยังถือ cuid เก่าของ Prisma ค้างใน
// PlayerPrefs แล้วยิง GET /api/users/cmr99b6xs...
const UUID_RE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isUuid(value: string): boolean {
  return UUID_RE.test(value.trim());
}

// Normalises a PostgREST error into an Error the route can branch on.
// PGRST205 = table missing from the schema cache, 42P01 = undefined table.
export function toError(e: { code?: string; message: string }): Error {
  if (e.code === "PGRST205" || e.code === "42P01") return new Error(TABLE_MISSING);
  return new Error(e.message);
}

// Every route ends with the same three failure modes; this keeps the wording
// (and the status codes Unity branches on) identical across all of them.
export function storageErrorResponse(e: unknown): Response {
  if (isNotConfigured(e)) {
    console.error("[supabase] SUPABASE_URL / SUPABASE_SERVICE_ROLE_KEY ยังไม่ได้ตั้ง");
    return Response.json({ error: "server is not configured" }, { status: 503 });
  }
  if (isTableMissing(e)) {
    console.error("[supabase] ยังไม่ได้รัน supabase/schema.sql");
    return Response.json({ error: "database is not initialised" }, { status: 503 });
  }
  console.error("[supabase]", e);
  return Response.json({ error: "internal error" }, { status: 500 });
}
