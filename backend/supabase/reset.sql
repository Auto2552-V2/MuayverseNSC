-- ═════════════════════════════════════════════════════════════
--  RESET — ลบตารางฝั่งผู้ป่วยทั้งหมดแล้วสร้างใหม่
--
--  ⚠️  ลบข้อมูลถาวร กู้ไม่ได้
--
--  ใช้เมื่อ schema.sql ถูกแก้แล้วต้องการให้ตารางตรงกับไฟล์ล่าสุด
--  เพราะ `create table if not exists` จะข้ามตารางที่มีอยู่แล้วเฉย ๆ
--  ไม่เพิ่มคอลัมน์ใหม่ให้ อาการที่เจอคือ API ตอบ 500 พร้อม log ประมาณ
--    column patients.age_text does not exist
--
--  วิธีใช้: Supabase > SQL Editor > New query
--    1. วางไฟล์นี้ทั้งไฟล์ → Run
--    2. วาง schema.sql ทั้งไฟล์ → Run
--
--  ตาราง doctors ไม่ถูกแตะ — บัญชีหมอที่สมัครไว้แล้วยังอยู่ครบ
-- ═════════════════════════════════════════════════════════════

drop table if exists public.reward_ledger     cascade;
drop table if exists public.quest_completions cascade;
drop table if exists public.quests            cascade;
drop table if exists public.doctor_patients   cascade;
drop table if exists public.patients          cascade;

drop function if exists public.grant_reward(uuid, integer, integer, text, uuid, text);
drop sequence if exists public.patient_code_seq;

-- เสร็จแล้วรัน schema.sql ต่อได้เลย
