# MuayverseNSC API

backend ของเกม — ล็อกอิน, โปรไฟล์, เหรียญ/ดาว, ภารกิจ, อันดับ และบันทึกผลการเล่น
เขียนด้วย Next.js (route handlers ล้วน ไม่มีหน้าเว็บ) เก็บข้อมูลใน Supabase

---

## เริ่มใช้งาน

### 1. ฐานข้อมูล

สร้างโปรเจกต์ใน Supabase → SQL Editor → รันไฟล์ใน [`../supabase/`](../supabase/) ตามลำดับ

```
schema.sql
2026-08-16-therapy-session.sql
2026-10-04-minigame-reward.sql
2026-10-05-drop-profile-fields.sql
```

ทุกไฟล์รันซ้ำได้ ส่วน `reset.sql` **ลบทุกอย่าง** ใช้เฉพาะตอนอยากเริ่มฐานข้อมูลใหม่หมด

### 2. ตั้งค่า

```powershell
copy .env.example .env.local
npm install
```

ใส่ค่าใน `.env.local` จาก Supabase → Project Settings → API

| ตัวแปร | จำเป็น | |
|---|---|---|
| `SUPABASE_URL` | ✅ | URL ของโปรเจกต์ |
| `SUPABASE_SERVICE_ROLE_KEY` | ✅ | **service role** ไม่ใช่ anon key |
| `AIFORTHAI_APIKEY` | — | ไม่มี route ไหนใช้แล้ว เว้นว่างได้ |

> ⚠️ service role key ข้าม Row Level Security ได้ทั้งหมด เทียบเท่าสิทธิ์ admin ของฐานข้อมูล
> **ห้าม commit `.env.local`** และห้ามใส่ key นี้ในเกมหรือหน้าเว็บที่ผู้เล่นเข้าถึงได้

### 3. รัน

```powershell
npm run dev        # http://localhost:3000
```

เปิด <http://localhost:3000/api/ready> — ได้ `200` แปลว่าต่อ Supabase ติดและตารางครบ
ได้ `503` ให้เช็ก `.env.local` และว่ารัน `schema.sql` แล้วหรือยัง

---

## Endpoint

route จริงอยู่ที่ระดับบนสุด (`app/auth/login/`) ส่วน `/api` ถูกตัดออกด้วย `rewrites` ใน
`next.config.ts` ตอน dev จึงเรียกแบบมี `/api` นำหน้าได้ตามปกติ — ตรงกับที่เกมเรียก

| Method | Path | ใช้ทำอะไร | ใครเรียก |
|---|---|---|---|
| POST | `/api/auth/register` | สมัคร `{ name, email, password }` | `AuthApi.cs` |
| POST | `/api/auth/login` | ล็อกอิน `{ email, password }` | `AuthApi.cs` |
| GET | `/api/users/:id` | โปรไฟล์ + ยอดเหรียญ/ดาว | `ProfileEditor`, `BalanceDisplay` |
| PUT | `/api/users/:id` | แก้ชื่อผู้ใช้ อีเมล รหัสผ่าน | `ProfileEditor` |
| GET | `/api/quests?patient=<id>[&day=monday]` | ภารกิจทั้งสัปดาห์ หรือเฉพาะวัน | `QuestLoader.cs` |
| POST | `/api/quests/:id/complete` | ทำภารกิจจบ จ่ายรางวัลวันละครั้ง | **ยังไม่มีใครเรียก** — เกมโหลดภารกิจมาโชว์แต่ยังไม่แจ้งตอนทำจบ |
| POST | `/api/minigame/complete` | จบมินิเกม `{ patientId, game, won }` | `MinigameApi.cs` |
| GET | `/api/leaderboard?limit=5[&patient=<id>]` | อันดับ + อันดับของตัวเอง | `LeaderboardLoader.cs` |
| GET | `/api/progress?patient=<id>` | ค่าเฉลี่ยความแม่นรายท่า | `ProgressOverviewLoader.cs` |
| POST | `/api/therapy-sessions` | บันทึกเซสชันกายภาพ | เว็บกายภาพ (ฝั่ง server) |
| GET | `/api/therapy-sessions/:id` | ผลของเซสชัน | `TherapyApi.cs` |
| GET | `/api/health` | liveness — ไม่แตะฐานข้อมูล | healthcheck |
| GET | `/api/ready` | readiness — แตะ Supabase จริง | ใช้ไล่ปัญหา |

error ทุกเส้นตอบเป็น `{ "error": "..." }`

---

## หลักที่ต้องรักษา

**จำนวนเหรียญกำหนดฝั่งเซิร์ฟเวอร์เท่านั้น** — เกมบอกได้แค่ว่า "ทำจบไหม / ชนะไหม"
ถ้ายอมให้ client ส่งจำนวนมา ใครแก้ request ก็ปั๊มเหรียญได้ไม่จำกัด

| ที่มา | รางวัลอยู่ที่ |
|---|---|
| ภารกิจ | แถวในตาราง `quests` (หมอเป็นคนตั้ง) |
| มินิเกม | `REWARDS` ใน `app/minigame/complete/route.ts` — ชนะ sparring ได้ 100 เหรียญ 3 ดาว |

เหรียญทุกเหรียญเดินผ่าน `grant_reward()` ใน Postgres ซึ่งเขียน ledger กับยอดคงเหลือ
ใน transaction เดียว — **อย่า `update patients set coins = …` ตรง ๆ** ไม่งั้นยอดกับประวัติจะไม่ตรงกัน

`PUT /api/users/:id` รับเฉพาะ key ที่อยู่ใน `FIELD_TO_COLUMN` (`lib/patients.ts`)
ส่ง `coins` หรือ `stars` มาจะถูกทิ้งเงียบ ๆ ตั้งใจให้เป็นแบบนั้น

**เพิ่มเหตุผลการจ่ายรางวัลใหม่ต้องแก้ CHECK constraint ด้วย** — `reward_ledger.reason`
จำกัดค่าไว้ ถ้าลืม `grant_reward()` จะ error แล้วถูกแปลงเป็น `INSUFFICIENT_BALANCE`
ซึ่งชวนหลงมาก ดูตัวอย่างใน `../supabase/2026-10-04-minigame-reward.sql`

---

## โครงสร้าง

```
app/          route handlers — หนึ่งโฟลเดอร์ต่อหนึ่ง endpoint
lib/
  supabase.ts     client + แปลง error เป็น response
  patients.ts     ผู้เล่น: อ่าน แก้ ล็อกอิน (คอลัมน์ที่อ่านได้อยู่ใน COLUMNS)
  password.ts     scrypt
  rewards.ts      จ่ายรางวัลภารกิจ, ยอดคงเหลือ
  quests.ts       ภารกิจรายสัปดาห์
  therapy.ts      เซสชันกายภาพ
  leaderboard.ts  การเรียงอันดับ
```
