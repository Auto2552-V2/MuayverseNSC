// POST /api/minigame/complete — เกมแจ้งว่าเล่นมินิเกมจบแล้ว
//
// body: { patientId, game, won }
// ตอบ:  { completionId, coinEarned, starEarned, coins, stars }
//
// `coins`/`stars` ที่ตอบกลับคือยอดรวมล่าสุด เอาไปอัปเดตแถบบนสุดของเกมได้เลย
//
// จำนวนเหรียญกำหนดในไฟล์นี้เท่านั้น — body บอกได้แค่ว่า "เกมไหน ชนะไหม"
// ถ้ายอมให้ client ส่งตัวเลขมา ใครเปิด devtools ก็ปั๊มเหรียญได้ไม่จำกัด
// (หลักการเดียวกับ /api/quests/:id/complete ที่อ่านรางวัลจากแถว quests)
//
// ต่างจาก quest/therapy ตรงที่ **ไม่จำกัดวันละครั้ง** ชนะกี่รอบก็ได้เท่านั้น
// ดู supabase/2026-10-04-minigame-reward.sql ถ้าอยากเปลี่ยนเป็นจำกัด

import { getBalance } from "@/lib/rewards";
import { getSupabase, storageErrorResponse } from "@/lib/supabase";

// รางวัลต่อการชนะหนึ่งครั้ง — แพ้ไม่ได้อะไร แต่ยังบันทึกไว้ให้หมอเห็นว่าเล่นแล้ว
const REWARDS: Record<string, { coin: number; star: number; label: string }> = {
  sparring: { coin: 100, star: 3, label: "มินิเกมชกมวย" },
};

export async function POST(request: Request) {
  const body = await request.json().catch(() => null);
  if (!body || typeof body !== "object") {
    return Response.json({ error: "invalid json body" }, { status: 400 });
  }

  const { patientId, game, won } = body as {
    patientId?: string;
    game?: string;
    won?: unknown;
  };

  if (!patientId) {
    return Response.json({ error: "patientId is required" }, { status: 400 });
  }

  const reward = REWARDS[game ?? ""];
  if (!reward) {
    return Response.json(
      { error: `unknown game "${game}" (รู้จักแค่: ${Object.keys(REWARDS).join(", ")})` },
      { status: 400 }
    );
  }

  // ต้องเป็น true เป๊ะ ๆ — "won": "false" ที่เป็น string ต้องไม่กลายเป็นชนะ
  const playerWon = won === true;
  const coinEarned = playerWon ? reward.coin : 0;
  const starEarned = playerWon ? reward.star : 0;

  try {
    const sb = getSupabase();

    const { data: completion, error: insertError } = await sb
      .from("quest_completions")
      .insert({
        patient_id: patientId,
        quest_id: null,          // หมอไม่ได้สั่ง ผู้ป่วยกดเล่นเอง (เหมือน therapy)
        pose_name: reward.label,
        pose_key: game,
        source: "minigame",
        accuracy: null,          // มินิเกมไม่ได้ให้คะแนนคุณภาพท่า ไม่ใช่ได้ 0%
        completed: playerWon,
        reps_done: 0,
        duration_s: 0,
        coin_earned: coinEarned,
        star_earned: starEarned,
        claim_day: null,         // ไม่จำกัดวันละครั้ง จึงไม่ต้องกันชนด้วย claim_day
      })
      .select("id")
      .single<{ id: string }>();

    if (insertError) throw insertError;

    // ให้ผู้ป่วยขึ้นบนสุดของรายการ "เล่นล่าสุด" ฝั่งหมอ เหมือน quest ปกติ
    await sb
      .from("patients")
      .update({ last_active_at: new Date().toISOString() })
      .eq("id", patientId);

    if (coinEarned === 0 && starEarned === 0) {
      const balance = await getBalance(patientId);
      return Response.json(
        { completionId: completion.id, coinEarned, starEarned, ...balance },
        { status: 201 }
      );
    }

    // grant_reward() เขียน ledger + ขยับยอดใน transaction เดียว (ดู schema.sql)
    const { data: patient, error: rpcError } = await sb
      .rpc("grant_reward", {
        p_patient: patientId,
        p_coins: coinEarned,
        p_stars: starEarned,
        p_reason: "minigame",
        p_ref: completion.id,
        p_note: reward.label,
      })
      .single<{ coins: number; stars: number }>();

    if (rpcError) throw rpcError;

    return Response.json(
      {
        completionId: completion.id,
        coinEarned,
        starEarned,
        coins: patient.coins,
        stars: patient.stars,
      },
      { status: 201 }
    );
  } catch (e) {
    return storageErrorResponse(e);
  }
}
