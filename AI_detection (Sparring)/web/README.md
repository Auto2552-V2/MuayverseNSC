# AI detection — MediaPipe Pose + LSTM ในเบราว์เซอร์

ตรวจจับท่ามวย 5 ท่าจากกล้อง แล้วส่งให้ Unity เป็น `{ "pose": "jab", "confidence": 0.9213 }`

ทุกอย่างรันบนเครื่องคนเล่น ภาพจากกล้องไม่ออกจากเครื่อง เซิร์ฟเวอร์ไม่ต้องประมวลผลอะไร

---

## ลองใช้ทันที

```powershell
npm run serve          # เทอร์มินัลที่ 1 -> http://localhost:8000/web/
npm run mock-unity     # เทอร์มินัลที่ 2 -> ดูว่า Unity จะได้อะไร
```

เปิด <http://localhost:8000/web/> กดปุ่ม **เริ่มกล้อง** แล้วออกหมัด

ต้องเปิดผ่าน `http://localhost` — เปิดไฟล์ `index.html` ตรง ๆ จะใช้กล้องไม่ได้
(`getUserMedia` ต้องการ secure context ซึ่ง `file://` ไม่นับ)

หน้าทดสอบใช้ยืนยันสามเรื่องก่อนไปต่อฝั่งเกม:

1. โมเดลโหลดได้และ shape ตรงกับ `meta.json`
2. ท่าทั้ง 5 จำแนกถูกจริง (ดูแถบความมั่นใจด้านขวา)
3. ตัวกรองส่งท่าออก **ครั้งเดียวต่อหนึ่งหมัด** ไม่ใช่รัว ๆ หลายสิบครั้ง

---

## ไฟล์

| ไฟล์ | หน้าที่ |
|---|---|
| `pose-features.js` | สร้าง feature vector — port มาจาก `scripts/pose_features.py` ต้องตรงเป๊ะ |
| `pose-detector.js` | กล้อง + MediaPipe + LSTM + ตัวกรอง + ส่งออก (โหมดต่อสู้) |
| `index.html` | หน้าทดสอบ (ไม่ต้องมี Unity) |
| `game.html` | หน้าที่ WebView โหลด — เริ่มกล้องเอง ไม่มี UI |
| `models/` | โมเดล pose ของ MediaPipe |

โหมดฝึกซ้อม (Siamese, ไม่เกี่ยวกับ Unity) ย้ายไปอยู่ที่ `ExerciseMode/web/` แล้ว

## วิธีต่อเข้าโค้ดอื่น

```js
import { PoseDetector, transportFromUrl } from './pose-detector.js';

const detector = new PoseDetector({
  modelUrl: '../tfjs_build/pose_action/model/model.json',
  metaUrl: '../tfjs_build/pose_action/meta.json',
  poseModelUrl: 'models/pose_landmarker_full.task',
  transport: transportFromUrl('auto'),   // ?transport=... ทับค่านี้ได้
  wsUrl: 'ws://127.0.0.1:8787',
});

detector.onPose = ({ pose, confidence }) => { /* ... */ };
await detector.init();
await detector.start(videoElement);
```

ต้องโหลด `@tensorflow/tfjs` ไว้ที่ `globalThis.tf` ก่อนเรียก `init()`

### ช่องทางส่งข้อมูล

หน้าเดียวกันนี้ถูกเปิดได้สามที่ จึงมีปลายทางสามแบบ:

- `unity-webview` — **Android จริง** gree ฉีด `window.Unity.call()` ให้ผ่าน
  `addJavascriptInterface` เป็น JS bridge จริง ไม่ใช่ URL scheme
- `sendmessage` — Unity WebGL ในหน้าเดียวกัน ยิงตรง ไม่ต้องมี server
- `websocket` — ต่อไป Unity ที่เปิด WS server รออยู่ (ใช้ตอนทดสอบบนเดสก์ท็อป)
- `auto` — ค่าเริ่มต้น ดูว่า "มีอะไรให้ใช้" แล้วเลือกตามลำดับ
  `unity-webview` → `sendmessage` → `websocket`

`auto` ไม่ได้ไล่ลองต่อ แต่ตรวจว่ามีของจริงไหม จึงไม่เปิด WebSocket ทิ้งไว้เปล่า ๆ
ตอนอยู่ใน WebView (สำคัญบนมือถือ — WS ที่ต่อไม่ติดจะ retry กินแบตไปเรื่อย)

### บังคับปลายทางด้วย query string

```
game.html?transport=unity-webview    # บังคับทาง Android
game.html?transport=websocket        # บังคับทางเดสก์ท็อป
index.html?transport=websocket&debug # ใช้คู่กับ ?debug ได้
```

มีไว้เพื่อไม่ต้องแก้ไฟล์สลับไปมา ซึ่งพลาดง่าย (แก้แล้วลืมแก้กลับ แล้วไปงง
บนมือถือว่าทำไมไม่มีอะไรส่งออก) ค่าที่พิมพ์ผิดจะเตือนใน console แล้วใช้ค่าเดิมต่อ
ไม่ทำให้หน้าเว็บตาย

ปลายทางที่ถูก pin ไว้**ไม่มีตัวสำรอง** ถ้าบริดจ์ไม่มีจริงจะขึ้นสถานะบอกตรง ๆ ว่า
ท่าที่ตรวจได้จะไม่ถูกส่งไปไหน — เปิด `?transport=unity-webview` ใน Chrome บนคอม
แล้วไม่มีอะไรเกิดขึ้นคือเรื่องปกติ ไม่ใช่ของเสีย

**Unity WebGL ใช้ `System.Net.WebSockets` ไม่ได้** ถ้าจะ build เป็น WebGL ให้ใช้
`sendmessage` จะง่ายกว่ามาก (ไม่ต้องมี WS server, ไม่ต้องมี `.jslib` plugin)

---

## ค่าที่ปรับได้ (ปรับสดได้จากหน้าทดสอบ)

| ค่า | ค่าเริ่มต้น | ความหมาย |
|---|---|---|
| `threshold` | 0.7 | ความมั่นใจขั้นต่ำ |
| `consensus` | 3 | ต้องทายซ้ำกันกี่เฟรมติดก่อนเชื่อ |
| `releaseFrames` | 3 | ต้องหลุดจากท่าเดิมกี่เฟรมก่อนส่งท่าเดิมซ้ำได้ |
| `emitCooldownMs` | 250 | เว้นระยะขั้นต่ำระหว่างสองท่า |
| `minPresenceRatio` | 0.8 | ต้องเห็นคนกี่ % ของ window ถึงจะทำนาย |
| `mirror` | true | พลิก**ภาพ**ก่อนเข้า MediaPipe — ต้องตรงกับตอนเก็บข้อมูล (ดูข้อ 4) |

### ทำไมต้องมีตัวกรองสามชั้น

window เลื่อนทีละเฟรม **หมัดเดียวจะถูกทายเป็น `jab` ติดกันหลายสิบเฟรม**
ถ้าส่งออกทุกเฟรม Unity จะได้ jab 30 ครั้งจากหมัดเดียว

- `consensus` กันการสั่นช่วงท่ากำลังเปลี่ยน
- `threshold` กันท่าที่โมเดลเองก็ไม่มั่นใจ
- **lock** ส่งท่าหนึ่งแล้วล็อกไว้ จนเห็นว่าออกจากท่านั้นจริง ๆ `releaseFrames` เฟรม

ชั้น lock ต่างจาก notebook ที่ใช้ `word != sentence[-1]` ซึ่งทำให้ "ชกท่าเดิมสองครั้งติด"
นับได้แค่ครั้งเดียว — ในเกมจริง jab สองหมัดต้องนับสองหมัด

### `minPresenceRatio` — อย่าปิด

โมเดล **ไม่มีคลาส "ไม่มีคน"** เฟรมที่ MediaPipe หาคนไม่เจอจะเป็นศูนย์ทั้งแถว
แล้วโมเดลตอบ `block` ด้วยความมั่นใจ **1.000**

ถ้าไม่กรองตรงนี้ คนเล่นเดินออกจากกล้องจะได้ "บล็อคสำเร็จ" ฟรีไปเรื่อย ๆ
(`scripts/verify_pipeline.mjs` เป็นตัวที่จับเจอ และ `verify_gating.mjs` กันไม่ให้หลุดกลับมา)

---

## เส้นแบ่งที่ต้องไม่พลาด

### 1. ลำดับคลาสไม่ใช่เรียงตามตัวอักษร

```
jab, cross, hook, uppercut, idle, block
```

`argmax` คืนเลข index ลำดับจึงต้องอ่านจาก `meta.json` เท่านั้น เรียงตามตัวอักษรจะได้
`block, cross, hook, idle, jab, uppercut` → ทุกท่าแมปผิดหมด โดยไม่มี error บอก

### 2. `build_features` ไม่ได้อยู่ในโมเดล

โมเดลเห็นแต่ตัวเลขหลัง normalise (ปรับ aspect, ยึดกลางสะโพก, หารด้วยความยาวลำตัว,
ต่อ velocity) ถ้าป้อน landmark ดิบเข้าไป มันจะตอบมั่วอย่างมั่นใจ ไม่ error

เขียนไว้สองที่ (Python สำหรับเทรน, JS สำหรับเบราว์เซอร์) จึงต้องมีเทสต์คุม —
**แก้ไฟล์ไหนก็ต้องรัน `npm test` ใหม่ทุกครั้ง**

### 3. ต้องใช้ `pose_landmarker_full` ไม่ใช่ `lite`

ตอนเก็บข้อมูลเทรนใช้ `model_complexity=1` ซึ่งเทียบเท่า **full**
ใช้ `lite` ตำแหน่ง landmark จะคลาดจากที่โมเดลเคยเห็น ความแม่นตกโดยไม่มี error
(มี `WARN` ใน console ถ้าชี้ไป lite)

### 4. ต้องพลิก "ภาพ" ก่อนเข้า MediaPipe ห้ามพลิกพิกัดทีหลัง

ตอนเก็บข้อมูลใช้ `cv2.flip(frame, 1)` **ก่อน** ส่งเข้า MediaPipe
เบราว์เซอร์จึงต้องทำแบบเดียวกัน — `_sourceFrame()` วาดภาพลง canvas แบบพลิกแล้วส่ง canvas นั้นเข้า MediaPipe

**ห้ามเปลี่ยนไปใช้ `x → 1-x` เด็ดขาด** สองวิธีนี้ไม่เท่ากัน:

MediaPipe ตั้งชื่อ landmark ตามกายวิภาคที่มันเห็น พอภาพพลิก คนในภาพกลายเป็นภาพกระจก
มันจึง **สลับ index ซ้าย/ขวาให้เอง** (11↔12, 15↔16, …) ไม่ใช่แค่ย้ายตำแหน่ง `x`

แปลว่าในข้อมูลเทรน index 15 ที่ชื่อ "wristL" จริง ๆ บรรจุ**ข้อมือขวา**ของคน
ถ้าพลิกแค่พิกัด ช่องนั้นจะได้ข้อมือซ้ายจริง → **ซ้ายขวาสลับกับที่เทรนมาทั้งหมด**
และ `jab`/`cross` คือหมัดซ้าย/หมัดขวา จึงพังตรง ๆ ทั้งที่ยังตอบด้วยความมั่นใจสูง

วัดจริงด้วย `scripts/probe_mirror.py` ทั้ง 4 คลิปใน `Video/`:

| คลิป | พลิก x เฉย ๆ | พลิก x + สลับ index | ต่างกัน |
|---|---|---|---|
| JAB | 0.0876 | **0.0076** | 11.5× |
| CROSS | 0.0792 | **0.0067** | 11.8× |
| HOOK | 0.0653 | **0.0042** | 15.6× |
| UPPERCUT | 0.0549 | **0.0071** | 7.7× |

รันสคริปต์นี้ก่อนถ้ามีใครเสนอ "ลดรูป" ตรงนี้

### 4.1 ภาพที่โชว์ต้องเป็นภาพที่โมเดลเห็น

หน้าทดสอบโชว์ `detector.mirrorCanvas` ตรง ๆ **ไม่ใช่** `<video>` ที่พลิกด้วย CSS

ถ้าโชว์ `<video>` แล้วใส่ `transform: scaleX(-1)` ภาพจะพลิกสองรอบ (ที่ canvas ของ MediaPipe
รอบหนึ่ง ที่ CSS อีกรอบ) แต่ skeleton พลิกรอบเดียว → **โครงกระดูกไม่ทับตัวคน**
ซึ่งเป็นอาการที่ทำให้สืบหาสาเหตุผิดทาง

### 5. aspect ใช้ค่าจริงของกล้อง

ข้อมูลเทรนเก็บที่ 640×480 (4:3) แต่เบราว์เซอร์มักให้ 16:9 มาแทนที่ขอไป
โค้ดคำนวณ aspect จากขนาดภาพจริงให้เอง แต่ถ้ากล้องไม่ใช่ 4:3 จะมี `WARN`
และความแม่นอาจตกเพราะมุมกล้องต่างจากตอนเก็บข้อมูล

---

## เทสต์

```powershell
npm test
```

สี่ชั้น แต่ละชั้นคุมคนละเรื่อง:

| คำสั่ง | พิสูจน์อะไร |
|---|---|
| `npm run test:features` | feature ฝั่ง JS == ฝั่ง Python (ทน 1e-5, ของจริงได้ 7e-7) |
| `npm run compare` | โมเดล TF.js == โมเดล Keras |
| `npm run test:pipeline` | รวมสองชั้นบน บนคลิปจริง → ทายถูก 13/13 |
| `npm run test:gating` | ตัวกรองส่งออกถูกต้อง 11 กรณี |

ชั้นที่ 3 สำคัญที่สุด: ผ่านแล้วแปลว่าสิ่งที่ยังต่างจากเบราว์เซอร์ได้
เหลือแค่กล้องกับ MediaPipe เท่านั้น ไม่ใช่โค้ดเราเอง

---

## เทรนใหม่

```powershell
# 1. เทรน + save (notebook ไม่เคย save ตัวโมเดล)
.venv\Scripts\python.exe scripts\train_pose_model.py

# 2. แปลงเป็น TF.js  (ต้องใช้ .tfjsenv ดู scripts/README.md)
.venv\Scripts\python.exe scripts\export_reference.py pose_action.h5
.tfjsenv\Scripts\python.exe scripts\convert_to_tfjs.py pose_action.h5

# 3. สร้าง fixture ใหม่แล้วเทสต์ทั้งหมด
.venv\Scripts\python.exe scripts\export_feature_fixtures.py
npm test
```

### ผลล่าสุด

ข้อมูล `LSTM learn/MP_Data_Diag` — 4 session × 6 คลาส × 15 คลิป × 36 เฟรม

แบ่งตาม **session** ไม่ใช่สุ่มคลิป เพราะคลิปใน session เดียวกันใช้แสง มุมกล้อง
และฟอร์มเดียวกัน สุ่มแบ่งจะรั่วข้อมูลข้าม train/test แล้วได้ accuracy ที่เกมจริงทำไม่ได้

| ชุด | session | accuracy |
|---|---|---|
| train (×5 time shift) | 0, 1 | 97.3% |
| validation | 2 | 94.4% |
| **test (ไม่เคยเห็น)** | **3** | **93.3%** |

รายคลาสบน test: `block` 100%, `idle` 97%, `jab`/`uppercut` 93%, `cross` 90%, `hook` 88%
ที่สับสนกันคือ `jab`/`cross` → `hook` (ทิศหมัดใกล้กัน)

โมเดล 45,670 params → TF.js 183 KB (`model.json` 4.5 KB + weights 178 KB)
