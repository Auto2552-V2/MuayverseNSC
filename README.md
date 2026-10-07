# MuayverseNSC เทรนเนอร์มวยไทยโดยใช้ปัญญาประดิษฐ์

ผู้เล่นออกหมัดจริงหน้ากล้อง ระบบ AI อ่านท่าแล้วส่งเข้าเกม Unity เพื่อตัดสินว่าอ่านเกมคู่ต่อสู้และโจมตีถูกต้องหรือไม่
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

| คู่ต่อสู้ | ท่าที่ถูกต้องของเรา | ผล |
|---|---|---|
| Guard (Open face) | `jab` | คู่ชก −1 |
| Guard (Open face) | `jab` → `cross` | คู่ชก −2 (PERFECT) |
| Guard | `hook` | คู่ชก −1 |
| Jab | `block` (ยกการ์ดปิดหน้า) | กันได้ ไม่มีใครเสียเลือด |
| Cross | `uppercut` | คู่ชก −1 |
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

## ความแม่นของ AI

โมเดล LSTM จำแนก 6 คลาส วัดจาก `pose_action.h5` ตัวที่ใช้งานอยู่จริง

| ชุดข้อมูล | ใช้ทำอะไร | จำนวน | ความแม่น |
|---|---|---|---|
| train | session 0, 1 (ขยายด้วยการเลื่อนเวลา 5 แบบ) | 900 | 97.3% |
| validation | session 2 — ใช้เลือกจุดหยุดเทรน | 90 | 94.4% |
| **test** | **session 3 — โมเดลไม่เคยเห็นเลย** | **90** | **93.3%** |

ข้อมูลอยู่ที่ `LSTM learn/MP_Data_Diag` — 4 session × 6 ท่า × 15 คลิป
แบ่งตาม **session** ไม่ใช่สุ่มคลิป เพราะคลิปใน session เดียวกันถ่ายด้วยแสง มุมกล้อง
และฟอร์มเดียวกัน ถ้าสุ่มแบ่งข้อมูลจะรั่วข้าม train/test และตัวเลขจะดูดีเกินจริง

### Confusion matrix — ชุด test

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="AI_detection%20(Sparring)/docs/confusion_test_dark.png">
  <img alt="Confusion matrix ชุด test: ถูก 84 จาก 90 คลิป (93.3%) โดย jab และ cross ถูกทายเป็น hook รวม 3 คลิป" src="AI_detection%20(Sparring)/docs/confusion_test_light.png" width="640">
</picture>

<details>
<summary>ดูเป็นตารางตัวเลข</summary>

แถว = ท่าที่ทำจริง · คอลัมน์ = ท่าที่โมเดลทาย

| จริง \ ทาย | jab | cross | hook | uppercut | idle | block |
|---|:-:|:-:|:-:|:-:|:-:|:-:|
| **jab** | **13** | 1 | 1 | · | · | · |
| **cross** | · | **13** | 2 | · | · | · |
| **hook** | · | · | **14** | 1 | · | · |
| **uppercut** | · | · | · | **14** | 1 | · |
| **idle** | · | · | · | · | **15** | · |
| **block** | · | · | · | · | · | **15** |

</details>

| ท่า | Recall | Precision |
|---|:-:|:-:|
| jab | 86.7% | 100% |
| cross | 86.7% | 92.9% |
| hook | 93.3% | **82.4%** |
| uppercut | 93.3% | 93.3% |
| idle | 100% | 93.8% |
| block | 100% | 100% |

**อ่านผลอย่างไร**

- ผิด 6 จาก 90 คลิป และ **ครึ่งหนึ่งคือ `jab`/`cross` ถูกทายเป็น `hook`** — hook จึงมี
  precision ต่ำสุด แปลว่าเวลาเกมได้รับ `hook` มีโอกาสราว 1 ใน 6 ที่ผู้เล่นทำท่าอื่น
- `block` กับ `idle` ถูกครบทุกคลิป — สองท่านี้เป็นท่าค้าง แยกออกจากหมัดได้ง่าย
- สาเหตุหลักคือกล้องมุมเดียวมองเห็นแค่ 2 มิติ หมัดตรงกับหมัดเหวี่ยงต่างกันที่ทิศ
  ของแขนซึ่งภาพด้านหน้าแยกได้ยาก

> ⚠️ ตัวเลขนี้วัดจาก**คลิปที่ตัดท่ามาแล้ว** ตอนเล่นจริงหน้าต่างเลื่อนต่อเนื่อง หมัดอาจถูก
> ตัดครึ่งที่ขอบหน้าต่าง ความแม่นจริงจึงต่ำกว่านี้ และยังไม่เคยวัดแยกไว้

<details>
<summary>Confusion matrix — ชุด validation (94.4%)</summary>

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="AI_detection%20(Sparring)/docs/confusion_val_dark.png">
  <img alt="Confusion matrix ชุด validation: ถูก 85 จาก 90 คลิป (94.4%)" src="AI_detection%20(Sparring)/docs/confusion_val_light.png" width="640">
</picture>

| จริง \ ทาย | jab | cross | hook | uppercut | idle | block |
|---|:-:|:-:|:-:|:-:|:-:|:-:|
| **jab** | **14** | 1 | · | · | · | · |
| **cross** | · | **14** | 1 | · | · | · |
| **hook** | · | · | **15** | · | · | · |
| **uppercut** | · | · | · | **14** | 1 | · |
| **idle** | · | · | · | · | **14** | 1 |
| **block** | 1 | · | · | · | · | **14** |

</details>

รูปสร้างจาก `scripts/plot_confusion.py` ซึ่งคำนวณใหม่จาก `pose_action.h5` ทุกครั้ง เทรนโมเดลใหม่แล้วรันสคริปต์นี้ซ้ำ รูปจะตรงกับโมเดลเสมอ ส่วนตัวเลขในตารางต้องแก้ตามเอง

`scripts/train_pose_model.py` พิมพ์ตารางชุดเดียวกันนี้ออกมาตอนเทรนจบ ถ้าเทรนใหม่ให้อัปเดตตัวเลขในส่วนนี้ด้วย

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
