-- ─────────────────────────────────────────────────────────────
--  รางวัลจากมินิเกมชกมวย (ซีน SparringMode)
--
--  ไม่สร้างตารางใหม่ — ใช้ quest_completions เดิมด้วยเหตุผลเดียวกับเซสชันกายภาพ:
--    · เว็บหมออ่านประวัติจากตารางนี้อยู่แล้ว มินิเกมจึงโผล่ในประวัติทันที
--    · เหรียญ/ดาวเดินผ่าน grant_reward() ตัวเดียวกัน มี ledger ครบ
--
--  ต่างจาก quest และ therapy ตรงที่ **ไม่จำกัดวันละครั้ง** — ชนะกี่รอบก็ได้เท่านั้น
--  จึงไม่มี unique index ของ source นี้ (ดูหมายเหตุท้ายไฟล์)
--
--  จำนวนเหรียญกำหนดฝั่งเซิร์ฟเวอร์ใน app/minigame/complete/route.ts เท่านั้น
--  เกมส่งมาได้แค่ว่า "ชนะไหม" — ถ้าให้ client บอกจำนวนเอง ใครก็ปั๊มเหรียญได้
--
--  วิธีใช้: Supabase Dashboard > SQL Editor > New query > วาง > Run (รันซ้ำได้)
-- ─────────────────────────────────────────────────────────────

-- 'minigame' = ผู้ป่วยเล่นมินิเกมในแอปจนชนะ (quest_id เป็น null เหมือน therapy)
alter table public.quest_completions
  drop constraint if exists quest_completions_source_check;
alter table public.quest_completions
  add constraint quest_completions_source_check
  check (source in ('quest', 'therapy', 'minigame'));

-- reason ต้องเพิ่มคู่กันเสมอ ไม่งั้น grant_reward() จะ raise check_violation
-- แล้วถูก exception handler ใน schema.sql แปลงเป็น INSUFFICIENT_BALANCE
-- ซึ่งหลอกมาก (เลือดเต็มอยู่แต่บอกว่าเหรียญไม่พอ)
alter table public.reward_ledger
  drop constraint if exists reward_ledger_reason_check;
alter table public.reward_ledger
  add constraint reward_ledger_reason_check
  check (reason in ('quest', 'store', 'adjust', 'signup', 'therapy', 'minigame'));

-- ประวัติมินิเกมของผู้ป่วยคนหนึ่ง เรียงใหม่ไปเก่า
create index if not exists quest_completions_minigame_idx
  on public.quest_completions (patient_id, completed_at desc)
  where source = 'minigame';

-- ─────────────────────────────────────────────────────────────
--  ถ้าภายหลังอยากจำกัดเป็นวันละครั้ง ให้รันบรรทัดนี้เพิ่ม แล้วฝั่ง route
--  จับ AlreadyClaimedError เหมือน quest (ตอนนี้ตั้งใจให้ไม่จำกัด)
--
--  create unique index if not exists quest_completions_minigame_one_claim_per_day
--    on public.quest_completions (patient_id, pose_key, claim_day)
--    where source = 'minigame' and completed and claim_day is not null;
-- ─────────────────────────────────────────────────────────────
