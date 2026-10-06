// GET /api/leaderboard?limit=5&patient=<patientId>
//
//   top → อันดับ 1..limit  (เติมช่อง No1..No5 ในหน้า Leaderboard_Page)
//   me  → อันดับของ patient ที่ส่งมา (เติมกล่อง Your_top) — rank = 0 คือไม่พบ
//
// ทั้งสองก้อนใช้กติกาเรียงชุดเดียวกันจาก lib/leaderboard.ts
// ไม่ส่ง patient มาก็ได้ จะได้แค่ top (เช่นเปิดดูอันดับตอนยังไม่ล็อกอิน)

import { EMPTY_ENTRY, getEntryFor, getTop } from "@/lib/leaderboard";
import { storageErrorResponse } from "@/lib/supabase";

export const dynamic = "force-dynamic";

const DEFAULT_LIMIT = 5;
const MAX_LIMIT = 50; // กันยิง ?limit=100000 ลากทั้งตารางออกมา

export async function GET(request: Request) {
  const url = new URL(request.url);
  const patientId = url.searchParams.get("patient")?.trim() ?? "";

  const raw = Number(url.searchParams.get("limit"));
  const limit =
    Number.isFinite(raw) && raw > 0
      ? Math.min(Math.trunc(raw), MAX_LIMIT)
      : DEFAULT_LIMIT;

  try {
    // สองคิวรีนี้ไม่ได้พึ่งผลของกัน ยิงพร้อมกันได้
    const [top, me] = await Promise.all([
      getTop(limit),
      patientId ? getEntryFor(patientId) : Promise.resolve(null),
    ]);

    return Response.json({ top, me: me ?? EMPTY_ENTRY });
  } catch (e) {
    return storageErrorResponse(e);
  }
}
