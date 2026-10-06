-- ═════════════════════════════════════════════════════════════
--  TICTA / MyRehab — Supabase schema (แหล่งความจริงที่เดียว)
--
--  วิธีใช้: Supabase Dashboard > SQL Editor > New query
--           วางทั้งไฟล์แล้วกด Run  (รันซ้ำได้ ไม่พัง)
--
--  ทุกตารางเปิด RLS แล้ว "ไม่" สร้าง policy เลย
--  => browser / anon key แตะไม่ได้เลยแม้แต่ read
--  => มีแต่ service_role key (ฝั่ง server เท่านั้น) ที่ bypass ได้
--  ห้ามเอา service_role key ไปไว้ใน Unity build หรือโค้ดฝั่ง client เด็ดขาด
-- ═════════════════════════════════════════════════════════════


-- ─────────────────────────────────────────────────────────────
--  ตัวช่วย: อัปเดต updated_at อัตโนมัติทุกครั้งที่ UPDATE
-- ─────────────────────────────────────────────────────────────
create or replace function public.touch_updated_at()
returns trigger
language plpgsql
as $$
begin
  new.updated_at = now();
  return new;
end;
$$;


-- ═════════════════════════════════════════════════════════════
--  1. แพทย์ — ใช้ล็อกอินเว็บหมอ (parkinweb)
-- ═════════════════════════════════════════════════════════════
create table if not exists public.doctors (
  id            uuid        primary key default gen_random_uuid(),
  full_name     text        not null,                    -- ชื่อจริง-นามสกุล
  email         text        not null unique,             -- เก็บเป็นตัวพิมพ์เล็กเสมอ
  doctor_id     text        not null,                    -- เลขใบประกอบวิชาชีพ
  password_salt text        not null,                    -- salt ของ scrypt
  password_hash text        not null,                    -- scrypt(password, salt) — ไม่เก็บรหัสจริง
  title         text        not null default 'Rehabilitation Physician',
  hospital      text        not null default '',
  avatar_color  text        not null default '#2563eb',
  created_at    timestamptz not null default now()
);

create index if not exists doctors_email_idx on public.doctors (email);


-- ═════════════════════════════════════════════════════════════
--  2. ผู้ป่วย — บัญชีที่สมัคร/ล็อกอินจากในเกม Unity
--
--  ตรงกับหน้า Profile_Page ในเกม 1:1
--  ช่องที่มาจากตอน signup: username, email, password
--  ที่เหลือกรอกทีหลังในหน้า Profile
-- ═════════════════════════════════════════════════════════════

-- UID ที่โชว์ในเกม (0001, 0002, ...) — uuid ยาวเกินกว่าจะให้คนอ่าน
create sequence if not exists public.patient_code_seq start 1;

create table if not exists public.patients (
  id            uuid        primary key default gen_random_uuid(),
  patient_code  text        not null unique
                  default lpad(nextval('public.patient_code_seq')::text, 4, '0'),

  -- ── มาจากตอน signup ──
  username      text        not null unique,          -- ชื่อผู้ใช้งาน (= ProfileData.name)
  email         text        not null unique,
  password_salt text        not null,                 -- scrypt เหมือนฝั่งหมอ
  password_hash text        not null,

  -- ── กรอกในหน้า Profile_Page (ผู้ป่วยกรอกเอง) ──
  full_name     text        not null default '',      -- ชื่อจริง

  -- ช่องวันเกิด/อายุ ในเกมเป็น TMP_InputField แบบข้อความอิสระ ผู้ใช้พิมพ์อะไรก็ได้
  -- ("12/08/2545", "20 ปี") จึงเก็บ 2 ชั้น:
  --   *_text = ตามที่พิมพ์จริง ส่งกลับไปโชว์ในเกมให้ตรงกับที่กรอก
  --   birth_date = ค่าที่ parse ได้ (parse ไม่ได้ = null) ฝั่งหมอใช้ตัวนี้คำนวณอายุ/เรียงลำดับ
  birth_date_text text      not null default '',
  birth_date      date,
  age_text        text      not null default '',

  gender        text        not null default '',      -- ข้อความอิสระ ("ชาย"/"male"/...) ฝั่งหมอค่อย normalize
  phone         text        not null default '',
  address       text        not null default '',
  symptom       text        not null default '',      -- อาการที่ป่วย (ข้อความอิสระ)
  avatar_url    text,                                 -- รูปโปรไฟล์ (Supabase Storage)
  avatar_color  text        not null default '#2563eb',

  -- ── ข้อมูลทางคลินิก (หมอกรอกจากเว็บ ผู้ป่วยแก้ไม่ได้) ──
  diagnosis         text     not null default '',
  brunnstrom        smallint not null default 0
                      check (brunnstrom between 0 and 6),   -- 0 = ยังไม่ประเมิน
  affected_side     text     check (affected_side is null or affected_side in ('left','right')),
  onset_date        date,                                   -- วันที่เกิด stroke
  clinical_symptoms text[]   not null default '{}',          -- อาการที่หมอบันทึก (คนละช่องกับ symptom)
  notes             text     not null default '',
  last_active_at    timestamptz,

  -- ── ยอดสะสมที่โชว์บนแถบบนสุดของเกม ──
  -- อย่า UPDATE ตรง ๆ ให้เรียก public.grant_reward() แทน จะได้มี ledger คู่กันเสมอ
  coins         integer     not null default 0 check (coins >= 0),
  stars         integer     not null default 0 check (stars >= 0),

  created_at    timestamptz not null default now(),
  updated_at    timestamptz not null default now()
);

create index if not exists patients_email_idx on public.patients (email);

drop trigger if exists patients_touch_updated_at on public.patients;
create trigger patients_touch_updated_at
  before update on public.patients
  for each row execute function public.touch_updated_at();


-- ═════════════════════════════════════════════════════════════
--  3. หมอคนไหนดูแลผู้ป่วยคนไหน
-- ═════════════════════════════════════════════════════════════
create table if not exists public.doctor_patients (
  doctor_id  uuid        not null references public.doctors(id)  on delete cascade,
  patient_id uuid        not null references public.patients(id) on delete cascade,
  created_at timestamptz not null default now(),
  primary key (doctor_id, patient_id)
);

create index if not exists doctor_patients_patient_idx
  on public.doctor_patients (patient_id);


-- ═════════════════════════════════════════════════════════════
--  4. Quest ที่หมอสั่ง — แทน .data/quests.json ของเดิม
--
--  QuestLoader.cs ในเกมยิง GET ?patient=<id>&day=<monday..sunday>
--
--  คอลัมน์ตั้งตาม type `Quest` ใน frontend/app/lib/quest.ts
--  ฝั่ง API จะแปลงให้ Unity อีกที เพราะ Unity รับเป็น string:
--     minutes 5      → time  "5 นาที"
--     difficulty easy → count "ง่าย"
-- ═════════════════════════════════════════════════════════════
create table if not exists public.quests (
  id           uuid        primary key default gen_random_uuid(),
  patient_id   uuid        not null references public.patients(id) on delete cascade,
  doctor_id    uuid        references public.doctors(id) on delete set null,
  day          text        not null
                 check (day in ('monday','tuesday','wednesday',
                                'thursday','friday','saturday','sunday')),
  pose_key     text        not null default '',   -- MoveKey เช่น shoulder_abduction
  pose_name    text        not null,              -- ชื่อท่าภาษาอังกฤษ → Unity PoseName
  pose_thai    text        not null default '',
  minutes      integer     not null default 0 check (minutes >= 0),
  difficulty   text        not null default 'easy'
                 check (difficulty in ('easy','medium','hard')),
  coin         integer     not null default 0 check (coin >= 0),
  star         integer     not null default 0 check (star >= 0),
  sort_order   integer     not null default 0,
  active       boolean     not null default true,
  created_at   timestamptz not null default now()
);

create index if not exists quests_patient_day_idx
  on public.quests (patient_id, day, sort_order);


-- ═════════════════════════════════════════════════════════════
--  5. ผู้ป่วยทำ quest เสร็จเมื่อไหร่ ได้อะไรบ้าง
--
--  เป็นทั้ง (ก) ข้อมูลป้อน ProgressChart ฝั่งหมอ
--         (ข) ที่มาของเหรียญ/ดาว
--  เก็บ pose_name ซ้ำไว้ด้วย เผื่อหมอลบ quest ทิ้งภายหลัง ประวัติจะได้ไม่กลายเป็นค่าว่าง
-- ═════════════════════════════════════════════════════════════
--  accuracy/completed ตั้งชื่อตาม type `SessionRecord` ที่ ProgressChart ใช้อยู่แล้ว
create table if not exists public.quest_completions (
  id           uuid        primary key default gen_random_uuid(),
  patient_id   uuid        not null references public.patients(id) on delete cascade,
  quest_id     uuid        references public.quests(id) on delete set null,
  pose_name    text        not null default '',

  -- 'quest'   = หมอสั่งไว้ในตาราง quests แล้วผู้ป่วยกดทำในเกม
  -- 'therapy' = เซสชันจากเว็บ AI-Rehab (quest_id เป็น null เสมอ เพราะหมอไม่ได้สั่ง)
  --             pose_key เป็นตัวบอกว่าท่าอะไร — ดู THERAPY_POSES ใน api/lib/therapy.ts
  pose_key     text        not null default '',
  source       text        not null default 'quest'
                 check (source in ('quest', 'therapy')),

  -- null = ฝึกจบจริงแต่ประเมินคุณภาพท่าไม่ได้ (โมเดลให้คะแนนโหลดไม่ขึ้น)
  -- คนละเรื่องกับ 0 ที่แปลว่าวัดได้จริงแล้วได้ศูนย์ — ค่าเฉลี่ยไม่นับแถวที่เป็น null
  accuracy     smallint    default 0 check (accuracy between 0 and 100),
  completed    boolean     not null default true,   -- false = เริ่มแล้วแต่ทำไม่ครบ
  reps_done    integer     not null default 0,
  duration_s   integer     not null default 0,
  coin_earned  integer     not null default 0,
  star_earned  integer     not null default 0,
  completed_at timestamptz not null default now(),

  -- วันที่ (ตามเวลาไทย) ที่เคลมรางวัล — ฝั่ง API เป็นคนใส่
  -- มีไว้ทำ unique index กันเคลมซ้ำ ทำเป็น expression index ตรง ๆ ไม่ได้เพราะ
  -- (completed_at at time zone 'Asia/Bangkok')::date เป็น STABLE ไม่ใช่ IMMUTABLE
  claim_day    date
);

create index if not exists quest_completions_patient_idx
  on public.quest_completions (patient_id, completed_at desc);

-- อัปเกรดฐานที่สร้างไว้ก่อนคอลัมน์นี้จะมี — ต้องมาก่อน create index ข้างล่าง
-- (create table if not exists ข้ามตารางเดิมไปเฉย ๆ ไม่เติมคอลัมน์ให้)
alter table public.quest_completions
  add column if not exists claim_day date;

-- quest หนึ่งอัน เคลมรางวัลได้วันละครั้ง — บังคับที่ฐานข้อมูล ไม่ใช่แค่เช็คในโค้ด
--
-- เช็คในโค้ดก่อน insert อย่างเดียวไม่พอ: สอง request ที่เข้ามาพร้อมกันจะเห็นว่า
-- "ยังไม่เคลม" ทั้งคู่แล้วได้เหรียญสองเท่า (กดรัว ๆ หรือยิงซ้ำจาก DevTools ก็ทำได้)
-- index นี้ทำให้ตัวที่สองชน 23505 แล้ว API แปลงเป็น "เคลมไปแล้ว"
create unique index if not exists quest_completions_one_claim_per_day
  on public.quest_completions (patient_id, quest_id, claim_day)
  where completed and claim_day is not null;

-- เซสชันกายภาพจากเว็บ AI-Rehab ก็ได้รางวัลวันละครั้งต่อท่าเหมือนกัน แต่ต้องมี
-- index ของตัวเอง เพราะ index ข้างบนคุมด้วย quest_id ซึ่งของ therapy เป็น null
-- และ Postgres ถือว่า null ไม่เท่ากับ null ในทุก unique index แถวพวกนี้จึงไม่มี
-- วันชนกันเองเลย
create unique index if not exists quest_completions_therapy_one_claim_per_day
  on public.quest_completions (patient_id, pose_key, claim_day)
  where source = 'therapy' and completed and claim_day is not null;

-- หน้า Dashboard ในเกมถามเป็นราย "ผู้ป่วย + ท่า" เสมอ (เฉลี่ยกี่ % ฝึกไปกี่ครั้ง)
create index if not exists quest_completions_patient_pose_idx
  on public.quest_completions (patient_id, pose_key, completed_at desc);


-- ═════════════════════════════════════════════════════════════
--  6. บัญชีเดินสะพัดของเหรียญ/ดาว
--
--  ทุกการเปลี่ยนแปลงยอดต้องมีบรรทัดที่นี่เสมอ ไม่งั้นตรวจย้อนหลังไม่ได้
--  ได้จาก quest = ค่าบวก / ซื้อของใน Store = ค่าลบ
-- ═════════════════════════════════════════════════════════════
create table if not exists public.reward_ledger (
  id         uuid        primary key default gen_random_uuid(),
  patient_id uuid        not null references public.patients(id) on delete cascade,
  coin_delta integer     not null default 0,
  star_delta integer     not null default 0,
  reason     text        not null check (reason in ('quest','store','adjust','signup','therapy')),
  ref_id     uuid,                                   -- quest_completions.id หรือ id ของสินค้า
  note       text        not null default '',
  created_at timestamptz not null default now()
);

create index if not exists reward_ledger_patient_idx
  on public.reward_ledger (patient_id, created_at desc);


-- ─────────────────────────────────────────────────────────────
--  ให้/หักเหรียญแบบ atomic — เขียน ledger + ขยับยอดในทีเดียว
--
--  เรียกจากฝั่ง server:
--    supabase.rpc('grant_reward', {
--      p_patient: id, p_coins: 10, p_stars: 1, p_reason: 'quest', p_ref: completionId
--    })
--
--  ถ้ายอดจะติดลบ (เหรียญไม่พอตอนซื้อของ) จะ raise แล้ว rollback ทั้งก้อน
--  ledger จะไม่ค้างเป็นขยะ
-- ─────────────────────────────────────────────────────────────
create or replace function public.grant_reward(
  p_patient uuid,
  p_coins   integer default 0,
  p_stars   integer default 0,
  p_reason  text    default 'adjust',
  p_ref     uuid    default null,
  p_note    text    default ''
)
returns public.patients
language plpgsql
as $$
declare
  result public.patients;
begin
  insert into public.reward_ledger (patient_id, coin_delta, star_delta, reason, ref_id, note)
  values (p_patient, p_coins, p_stars, p_reason, p_ref, p_note);

  update public.patients
     set coins = coins + p_coins,
         stars = stars + p_stars
   where id = p_patient
  returning * into result;

  if not found then
    raise exception 'PATIENT_NOT_FOUND';
  end if;

  return result;
exception
  -- check (coins >= 0) ของตาราง patients จะเด้งมาที่นี่
  when check_violation then
    raise exception 'INSUFFICIENT_BALANCE';
end;
$$;


-- ═════════════════════════════════════════════════════════════
--  ล็อกทุกตาราง — ไม่มี policy = เข้าได้เฉพาะ service_role
-- ═════════════════════════════════════════════════════════════
alter table public.doctors           enable row level security;
alter table public.patients          enable row level security;
alter table public.doctor_patients   enable row level security;
alter table public.quests            enable row level security;
alter table public.quest_completions enable row level security;
alter table public.reward_ledger     enable row level security;


-- ═════════════════════════════════════════════════════════════
--  ข้อมูลตัวอย่าง (seed) — ให้เว็บหมอไม่ว่างเปล่าตอนเปิดครั้งแรก
--
--  ผู้ป่วยสองคนนี้ล็อกอินเข้าเกมได้จริง รหัสผ่าน: patient123
--  hash เป็น scrypt(password, salt, 64) แบบเดียวกับที่ api/lib/password.ts ใช้
--
--  ก่อนส่งงานจริงควรลบทิ้ง:
--    delete from public.patients where email like '%@demo.local';
-- ═════════════════════════════════════════════════════════════
insert into public.patients
  (username, email, password_salt, password_hash,
   full_name, birth_date_text, birth_date, age_text, gender,
   phone, symptom, diagnosis, brunnstrom, affected_side, onset_date)
values
  ('somchai', 'somchai@demo.local',
   '99229cf56801f39bc2e971ba8b9f5478',
   'c3d566e8aea8d6473d649b5223f05054f166fb06cc4e1b82014be41412a5d0ea6998512b849a44d08144ade98911c5f83f72f31cdee65c8b171da6c81001262c',
   'สมชาย ใจดี', '12/08/2503', '1960-08-12', '65', 'ชาย',
   '081-234-5678', 'แขนขวาอ่อนแรง ยกไม่สุด',
   'Ischemic Stroke · อ่อนแรงซีกขวา', 3, 'right', '2026-02-14'),
  ('malee', 'malee@demo.local',
   '7361bab159cb73e9994d2aa5340f3183',
   '7783ac518478ca4b8a94308c945bd60ac1468385ec134cc7d0dcc6056b74a3906c2557e08bbc7e09d6d9c4f4d781fc4c070d69ff8031cf73ca38dc2eb9a815fd',
   'มาลี สุขใจ', '03/11/2508', '1965-11-03', '60', 'หญิง',
   '089-876-5432', 'ไหล่ซ้ายติด ยกแขนได้ไม่เกินระดับอก',
   'Hemorrhagic Stroke · อ่อนแรงซีกซ้าย', 2, 'left', '2026-04-02')
on conflict (email) do nothing;
