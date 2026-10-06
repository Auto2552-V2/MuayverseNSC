-- ─────────────────────────────────────────────────────────────
--  บันทึกเซสชันกายภาพจากเว็บ AI-Rehab (/mode/session-left ฯลฯ)
--
--  ไม่สร้างตารางใหม่ — ใช้ quest_completions เดิม เพราะ:
--    · เว็บหมออ่านประวัติ/กราฟจากตารางนี้อยู่แล้ว (patient-db.ts, dashboard.ts)
--      เซสชันกายภาพจึงโผล่ในกราฟผู้ป่วยทันทีโดยไม่ต้องแก้ฝั่งหมอเลย
--    · เหรียญ/ดาวเดินผ่าน grant_reward() ตัวเดียวกัน มี ledger ครบเหมือนกัน
--
--  ต่างกันแค่ quest_id เป็น null (หมอไม่ได้สั่ง ผู้ป่วยกดเล่นเอง) จึงต้องมี
--  pose_key ไว้บอกว่าเป็นท่าอะไร และ source ไว้แยกว่ามาจากไหน
--
--  วิธีใช้: Supabase Dashboard > SQL Editor > New query > วาง > Run (รันซ้ำได้)
-- ─────────────────────────────────────────────────────────────

alter table public.quest_completions
  add column if not exists pose_key text not null default '',
  add column if not exists source   text not null default 'quest';

-- accuracy เป็น null ได้แล้ว = "ฝึกจบจริง แต่ประเมินคุณภาพท่าไม่ได้"
--
-- เกิดตอนโมเดลให้คะแนน (public/models/scorer_ex1.json) โหลดไม่ขึ้น ซึ่งเป็นคนละ
-- เรื่องกับได้ 0% แต่ถ้าเก็บเป็น 0 ค่าเฉลี่ยในหน้า Dashboard จะถูกถ่วงลงทันที
-- ทั้งที่ไม่เคยมีใครวัดได้ว่าแย่ — เก็บเป็น null แล้วไม่นับเข้าค่าเฉลี่ยแทน
-- (จำนวนครั้งที่ฝึกยังนับตามปกติ เพราะเขาฝึกจริง)
--
-- check (accuracy between 0 and 100) เดิมไม่ต้องแตะ — CHECK ที่ได้ผลเป็น null
-- ถือว่าผ่านตามมาตรฐาน SQL และฝั่งเว็บหมอประกาศ type เป็น `number | null` อยู่แล้ว
alter table public.quest_completions alter column accuracy drop not null;

-- 'quest'   = หมอสั่งไว้ในตาราง quests แล้วผู้ป่วยกดทำในเกม
-- 'therapy' = เซสชันจากเว็บ AI-Rehab (quest_id เป็น null เสมอ)
alter table public.quest_completions
  drop constraint if exists quest_completions_source_check;
alter table public.quest_completions
  add constraint quest_completions_source_check
  check (source in ('quest', 'therapy'));

-- ท่าหนึ่งท่า เคลมรางวัลได้วันละครั้ง — บังคับที่ฐานข้อมูลเหมือนฝั่ง quest
--
-- ต้องมี index ของตัวเองแยกจาก quest_completions_one_claim_per_day เพราะ
-- index นั้นคุมด้วย quest_id ซึ่งของ therapy เป็น null — Postgres ถือว่า null
-- ไม่เท่ากับ null ในทุก unique index แถว therapy จึงไม่มีวันชนกันเองเลย
create unique index if not exists quest_completions_therapy_one_claim_per_day
  on public.quest_completions (patient_id, pose_key, claim_day)
  where source = 'therapy' and completed and claim_day is not null;

-- หน้า Dashboard ในเกมถามว่า "ท่านี้เฉลี่ยกี่เปอร์เซ็นต์ ฝึกไปกี่ครั้ง"
-- เป็น query แบบ patient + pose เสมอ
create index if not exists quest_completions_patient_pose_idx
  on public.quest_completions (patient_id, pose_key, completed_at desc);

-- reward_ledger.reason เดิมมีแค่ quest/store/adjust/signup
-- ถ้าไม่เพิ่ม grant_reward() จะ raise check_violation แล้วถูกแปลงเป็น
-- INSUFFICIENT_BALANCE (ดู exception handler ใน schema.sql) ซึ่งหลอกมาก
alter table public.reward_ledger
  drop constraint if exists reward_ledger_reason_check;
alter table public.reward_ledger
  add constraint reward_ledger_reason_check
  check (reason in ('quest', 'store', 'adjust', 'signup', 'therapy'));
