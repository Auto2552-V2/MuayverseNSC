# MuayverseNSC เทรนเนอร์มวยไทยโดยใช้ปัญญาประดิษฐ์

ผู้เล่นออกหมัดจริงหน้ากล้อง ระบบ AI อ่านท่าแล้วส่งเข้าเกม Unity เพื่อตัดสินว่าตอบโต้ท่าของคู่ชกถูกหรือไม่
มีสองโหมด

| โหมด | ทำอะไร | อยู่ที่ |
|---|---|---|
| **ต่อสู้ (Sparring)** | คู่ชกออกท่า ผู้เล่นต้องตอบโต้ให้ถูกภายในเวลา ชนะได้เหรียญและดาว | `Unity/` + `AI_detection (Sparring)/` |
| **ฝึกซ้อม (Exercise)** | ทำท่าตามต้นแบบ ระบบให้คะแนนความคล้าย | `ExerciseMode/` |

ภาพจากกล้องไม่ถูกส่งออกนอกเครื่อง — การจับท่าทั้งหมดรันในเบราว์เซอร์หรือในเครื่องผู้เล่น

---

## โครงสร้าง

```
MuayverseNSC/
├── Unity/Unity_Game/           เกม (Unity 6000.0.74f1) — หน้าหลัก + โหมดต่อสู้
├── AI_detection (Sparring)/    จับท่าสำหรับโหมดต่อสู้
│   ├── web/                      หน้าเว็บ MediaPipe + LSTM (TF.js)
│   ├── webcam/                   ตัวจับท่าแบบ Python (ใช้ทดสอบบนคอม)
│   ├── scripts/                  เทรน แปลงโมเดล เทสต์
│   └── LSTM learn/               notebook และข้อมูลเทรน
├── ExerciseMode/               โหมดฝึกซ้อม (เว็บ + โมเดล Siamese)
└── backend/
    ├── api/                      API ของเกม (Next.js) — ล็อกอิน เหรียญ ภารกิจ
    └── supabase/                 schema ฐานข้อมูลและ migration
```

---

## สิ่งที่ต้องติดตั้ง

| โปรแกรม | เวอร์ชัน | ใช้กับ |
|---|---|---|
| Unity | **6000.0.74f1** | เกม |
| Node.js | 20 ขึ้นไป | backend, หน้าเว็บ, เทสต์ |
| Python | **3.10** | Python client และการเทรน (ไม่ต้องใช้ถ้าเล่นผ่านหน้าเว็บ) |
| บัญชี Supabase | — | ฐานข้อมูลผู้เล่น |

---

## การตั้งค่าครั้งแรก

### 1. ฐานข้อมูล

สร้างโปรเจกต์ใน Supabase แล้วเปิด **SQL Editor** รันไฟล์ใน `backend/supabase/` ตามลำดับ

```
schema.sql
2026-08-16-therapy-session.sql
2026-10-04-minigame-reward.sql
2026-10-05-drop-profile-fields.sql
```

### 2. Backend

```powershell
cd backend\api
copy .env.example .env.local
npm install
```

แก้ `.env.local` ใส่ค่า `SUPABASE_URL` และ `SUPABASE_SERVICE_ROLE_KEY` จาก Supabase (Project Settings → API)

> ⚠️ `SUPABASE_SERVICE_ROLE_KEY` คือสิทธิ์ admin ของฐานข้อมูลทั้งหมด **ห้าม commit** —
> `.gitignore` กันไฟล์ `.env*` ไว้แล้ว แต่ควรเช็ก `git status` ทุกครั้งก่อน commit

### 3. หน้าเว็บจับท่า

```powershell
cd "AI_detection (Sparring)"
npm install
npm test
```

`npm test` ควรผ่านทั้ง 4 ชุด ถ้าไม่ผ่านอย่าเพิ่งเล่น — แปลว่าโมเดลหรือการคำนวณ feature ไม่ตรงกับตอนเทรน

ชื่อโฟลเดอร์มีเว้นวรรคและวงเล็บ เวลาพิมพ์ใน terminal **ต้องครอบด้วยเครื่องหมายคำพูดเสมอ**

### 4. Python (เฉพาะถ้าจะใช้ Python client)

```powershell
cd "AI_detection (Sparring)"
py -3.10 -m venv .venv
.venv\Scripts\python.exe -m pip install tensorflow==2.15.0 mediapipe==0.10.9 opencv-python numpy==1.26.4 websockets
```

---

## วิธีเล่นบนคอม

เปิด terminal ค้างไว้ตามขั้นตอน **ลำดับมีผล**

**① Backend** — ต้องเปิดก่อนเสมอ เพราะหน้าหลักของเกมบังคับให้ล็อกอิน

```powershell
cd backend\api
npm run dev
```

**② Unity** — เปิด `Unity/Unity_Game` เปิดซีน `FrontendAPP` แล้วกด Play

ล็อกอิน → กดเมนูมินิเกม → เข้าโหมดต่อสู้ รอ Console ขึ้น `[WS] รอรับที่ ws://127.0.0.1:8787`

**③ ตัวจับท่า** — เลือก**แบบใดแบบหนึ่ง**

<details open>
<summary><b>แบบ A: หน้าเว็บ</b> (ตัวเดียวกับที่จะใช้บนมือถือ)</summary>

```powershell
cd "AI_detection (Sparring)"
npm run serve
```

เปิด Chrome ไปที่ <http://localhost:8000/web/> แล้วกด **เริ่มกล้อง**

</details>

<details>
<summary><b>แบบ B: Python client</b></summary>

```powershell
cd "AI_detection (Sparring)"
.venv\Scripts\python.exe webcam\pose_ws_client.py --preview
```

`--preview` เปิดหน้าต่างกล้อง (กด `q` เพื่อออก) ถ้าไม่ใส่จะไม่มีหน้าต่าง แต่เร็วกว่าราวสองเท่า

</details>

### ท่าที่ใช้ตอบโต้

แต่ละรอบมีเวลา 3 วินาที **ทุกท่าที่ทำในรอบถูกนับ** — ถูกก็หักเลือดคู่ชก ผิดก็หักเลือดคุณ
ทันทีทีละท่า ชกถูกซ้ำได้หลายหมัดในรอบเดียว

| คู่ชกทำ | คุณต้องทำ | ผล |
|---|---|---|
| ตั้งการ์ด 1 | `jab` | คู่ชก −5 |
| ตั้งการ์ด 1 | `jab` → `cross` | คู่ชก −10 (PERFECT) |
| ตั้งการ์ด 2 | `hook` | คู่ชก −5 |
| ชก `jab` | `block` (ยกการ์ดปิดหน้า) | กันได้ ไม่มีใครเสียเลือด |
| ชก `cross` | `uppercut` | คู่ชก −5 |
| ท่าผิด / ไม่ตอบเลย | — | คุณ −1 ต่อครั้ง |

ชนะได้ **100 เหรียญ 3 ดาว** บันทึกลงฐานข้อมูลทันที

ตัวเลขทั้งหมดปรับได้ใน Inspector ดู [`Unity/README.md`](Unity/README.md) — ยกเว้นรางวัลที่กำหนดฝั่งเซิร์ฟเวอร์

### เคล็ดลับให้ระบบอ่านท่าแม่น

- ยืนให้กล้องเห็น**ตั้งแต่สะโพกขึ้นไป** — ระบบใช้สะโพกเป็นจุดอ้างอิง
- ออกหมัดให้ครบจังหวะ: การ์ด → ออกหมัด → กลับการ์ด อย่าค้างท่า
- แสงพอและพื้นหลังโล่งช่วยได้มาก

### ทดสอบเกมโดยไม่ใช้กล้อง

ในโหมดต่อสู้กดคีย์บอร์ดแทนได้ `J` jab · `C` cross · `H` hook · `U` uppercut · `B` block
(ต้องเปิด component `DebugPoseInput` บน GameObject `PoseReceiver`)

---

## โหมดฝึกซ้อม

```powershell
cd ExerciseMode
npm install
npm run serve -- 8001
```

เปิด <http://localhost:8001/web/exercise-play.html>

ใส่ `-- 8001` เพราะโหมดต่อสู้ใช้พอร์ต 8000 อยู่ ถ้าเปิดพร้อมกันพอร์ตจะชน
รายละเอียดเพิ่มเติมดู [`ExerciseMode/README.md`](ExerciseMode/README.md)

---

## แก้ปัญหาที่พบบ่อย

| อาการ | สาเหตุ / วิธีแก้ |
|---|---|
| Unity ขึ้น `[WS] เปิดพอร์ต 8787 ไม่ได้` | มีโปรแกรมอื่นจับพอร์ตอยู่ — มักเป็น `npm run mock-unity` ที่เปิดค้างไว้ ปิดก่อนแล้ว Play ใหม่ |
| ต่อยถูกแต่คู่ชกไม่เสียเลือด | ดู Console: `[Judge] เมิน … (หน้าต่างปิดแล้ว)` = ท่ามาช้าเกินรอบ ลองปิด `--preview` หรือเพิ่ม `Round Duration` |
| เหรียญ/ดาวไม่เพิ่มหลังชนะ | ยังไม่ได้ล็อกอิน หรือ backend ไม่ได้รัน — Console จะบอกสาเหตุ |
| ล็อกอินไม่ผ่าน | เช็กว่า `npm run dev` รันอยู่ และ `.env.local` ใส่ค่า Supabase ถูก |
| กดปุ่มเข้าโหมดแล้วขึ้น "ไม่มีซีนใน Build Settings" | File → Build Profiles → Scene List ต้องติ๊กทั้ง `FrontendAPP` และ `SparringMode` |
| หน้าเว็บขึ้นว่ากล้องไม่ทำงาน | ต้องเปิดผ่าน `http://localhost` ไม่ใช่เปิดไฟล์ตรง ๆ และให้สิทธิ์กล้องใน Chrome |
| `npm test` ไม่ผ่านหลังแก้โค้ด | การคำนวณ feature ฝั่ง JS กับ Python ไม่ตรงกันแล้ว — ต้องแก้ให้ตรงก่อนเล่น |

---

## สำหรับนักพัฒนา

| เรื่อง | ดูที่ |
|---|---|
| สถาปัตยกรรม การตัดสิน ข้อควรระวังทั้งหมด | [`CLAUDE.md`](CLAUDE.md) |
| หน้าเว็บจับท่า และการตั้งค่าตัวกรอง | [`AI_detection (Sparring)/web/README.md`](<AI_detection (Sparring)/web/README.md>) |
| แปลงโมเดลเป็น TF.js และตรวจว่าผลตรงต้นฉบับ | [`AI_detection (Sparring)/scripts/README.md`](<AI_detection (Sparring)/scripts/README.md>) |
| Python client | [`AI_detection (Sparring)/webcam/README.md`](<AI_detection (Sparring)/webcam/README.md>) |

**กฎสำคัญ:** แก้ไฟล์ใดใน `AI_detection (Sparring)/` ต้องรัน `npm test` ใหม่ทุกครั้ง
— ถ้าการคำนวณ feature คลาดจากตอนเทรนแม้นิดเดียว โมเดลจะตอบผิดอย่างมั่นใจโดยไม่มี error บอก
