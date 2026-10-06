-- ─────────────────────────────────────────────────────────────
--  ถอดช่องข้อมูลส่วนตัวของผู้ป่วยออกจากฐานข้อมูล
--
--  เหลือเฉพาะที่จำเป็นต่อการใช้งานจริง:
--    username / email / password_*  — ล็อกอิน
--    patient_code                   — UID ที่โชว์ในเกม
--    coins / stars / avatar_*       — ของสะสมกับหน้าตา
--    คอลัมน์ฝั่งหมอ (diagnosis, brunnstrom, clinical_symptoms, …) ไม่แตะ
--
--  ⚠ ลบแล้วข้อมูลเดิมในคอลัมน์เหล่านี้หายถาวร ย้อนกลับไม่ได้
--    ถ้ายังไม่แน่ใจ ให้ dump ตารางเก็บไว้ก่อน:
--      select id, full_name, birth_date_text, birth_date, age_text,
--             gender, phone, address, symptom from public.patients;
--
--  ต้อง deploy โค้ดที่แก้แล้วพร้อมกัน ไม่งั้นทั้งสองเว็บจะ query หาคอลัมน์ที่ไม่มี
--  (api/lib/patients.ts, frontend/app/lib/patient-db.ts, dashboard.ts)
--
--  วิธีใช้: Supabase Dashboard > SQL Editor > New query > วาง > Run (รันซ้ำได้)
-- ─────────────────────────────────────────────────────────────

alter table public.patients
  drop column if exists full_name,
  drop column if exists birth_date_text,
  drop column if exists birth_date,
  drop column if exists age_text,
  drop column if exists gender,
  drop column if exists phone,
  drop column if exists address,
  drop column if exists symptom;

-- email ไม่ได้ลบ — เป็นตัวระบุบัญชีตอนล็อกอิน (findCredentialsByEmail)
-- index ของมันจึงต้องอยู่ต่อ
-- symptom (ผู้ป่วยกรอกเอง) ต่างจาก clinical_symptoms (หมอกรอก) ซึ่งยังอยู่ครบ
