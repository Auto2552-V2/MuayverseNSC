# MuayverseNSC — เกมชกมวยไทยด้วย AI Pose Detection

เกมชกมวยที่ผู้เล่นออกท่าจริงหน้ากล้อง ระบบ AI จำแนกท่าแล้วส่งผลไปยัง Unity
เพื่อตัดสินว่าตอบโต้ท่าของ Enemy ถูกต้องหรือไม่

**Build target: Android (APK)**

---

## สถาปัตยกรรมจริง

```
กล้อง → MediaPipe Pose → LSTM (TF.js)  ← ทั้งหมดนี้รันในเบราว์เซอร์
   ↓ Unity.call(json)
Unity (WebView ของ gree) → ตัดสิน → แสดงผล
```

**ทุกอย่างรันบนเครื่องผู้เล่น** ไม่มี Python server ไม่มีการส่งภาพออกนอกเครื่อง

> เอกสารรุ่นก่อนเขียนว่าใช้ Python WebSocket server แล้ว Unity ส่ง JPEG ไปให้
> — **เลิกแนวทางนั้นแล้ว** เพราะต้องมีคอมเปิดเซิร์ฟเวอร์ตลอดและใช้ได้แค่ใน LAN

### ช่องทางส่งข้อมูล (`transport` ใน pose-detector.js)

| โหมด | ใช้เมื่อ |
|---|---|
| `unity-webview` | **Android จริง** — gree ฉีด `window.Unity.call()` ให้ผ่าน `addJavascriptInterface` |
| `websocket` | **ทดสอบบนเดสก์ท็อป** — Chrome บนคอม → `ws://127.0.0.1:8787` → `PoseWebSocketServer.cs` |
| `sendmessage` | WebGL (ไม่ได้ใช้แล้ว แต่ยังรองรับ) |
| `auto` | เลือกจากสิ่งที่มีจริง: `unity-webview` → `sendmessage` → `websocket` |

บังคับทีละรันได้ด้วย query string โดยไม่ต้องแก้ไฟล์ — `game.html?transport=unity-webview`
หรือ `?transport=websocket` (ดู `transportFromUrl` ใน `pose-detector.js`)
ค่าที่พิมพ์ผิดจะเตือนแล้วใช้ค่าเดิมต่อ และปลายทางที่ pin ไว้แล้วไม่มีบริดจ์จริง
จะขึ้นสถานะบอกว่าท่าจะไม่ถูกส่งไปไหน ไม่เงียบหาย

**WebView ของ gree บน Windows เปิดกล้องไม่ได้** — `SetCameraAccess()` เป็น TODO ในปลั๊กอิน
ทดสอบบนเดสก์ท็อปจึงต้องใช้ WebSocket เท่านั้น

### สถานะ

| ส่วน | สถานะ |
|---|---|
| AI Detection (เทรน + TF.js + หน้าเว็บ) | เสร็จ ทดสอบแล้ว |
| Game logic (รอบ, ตัดสิน, combo, stun, HP) | เสร็จ ทดสอบด้วยปุ่มดีบักแล้ว |
| ทดสอบบนเดสก์ท็อปผ่าน WebSocket | พร้อม รอทดสอบ |
| ทดสอบบน Android จริง | **ยังไม่เคยลอง** — กล้องใน WebView ยังไม่พิสูจน์ |
| Deploy HTTPS | เตรียมไฟล์แล้ว (`npm run build:deploy`) ยังไม่ push |

---

## AI Detection

อยู่ที่ `AI_detection (Sparring)/` (แยกจากโปรเจกต์ Unity ที่ `Unity/Unity_Game/`)

ชื่อโฟลเดอร์มีเว้นวรรคและวงเล็บ — ใน terminal ต้องครอบด้วยเครื่องหมายคำพูดเสมอ
แต่**ห้ามเปลี่ยนชื่อ `web/` ข้างใน** — เทสต์ `build_deploy` และ URL ที่ Unity ใช้
(`http://localhost:8000/web/`) ผูกกับชื่อนี้ เคยเปลี่ยนเป็น `web (local)` แล้ว `npm test` พังทั้งชุด

### Model

- MediaPipe **Pose** เท่านั้น (ไม่ใช่ Holistic)
- ต้องใช้ **`pose_landmarker_full.task`** ไม่ใช่ `lite` — ข้อมูลเทรนเก็บด้วย
  `model_complexity=1` ซึ่งเทียบเท่า full ถ้าใช้ lite ความแม่นตกโดยไม่มี error บอก
- LSTM `(36, 60) → 6 คลาส` 45,670 params → TF.js 183 KB

**ลำดับคลาสไม่ใช่เรียงตามตัวอักษร:**

```
jab, cross, hook, uppercut, idle, block
```

`argmax` คืนเลข index ต้องอ่านลำดับจาก `tfjs_build/pose_action/meta.json` เท่านั้น
ถ้าเดาเป็นเรียงตามตัวอักษรจะได้ `block, cross, hook, idle, jab, uppercut` → แมปผิดทุกท่า

`block` ในที่นี้คือ **High Guard** (ยกแขนปิดหน้า)
`idle` ไม่เคยถูกส่งออกไปให้เกม เป็นแค่สถานะ "ยังไม่ออกท่า"

### Feature (ไม่ได้อยู่ในโมเดล ต้องทำเองก่อนป้อนเข้า)

`scripts/pose_features.py` คือต้นฉบับ `web/pose-features.js` คือ port ฝั่ง JS

```
landmark ดิบ 33 จุด × 4 = 132
  ↓ x *= aspect (แก้สัดส่วนภาพ)
  ↓ ยึดกลางสะโพก (23,24) เป็นจุด (0,0)
  ↓ หารด้วย |mid_shoulder - mid_hip|   ← ความยาวลำตัว ไม่ใช่ความกว้างไหล่
  ↓ เอาเฉพาะ 10 จุด [11,12,13,14,15,16,19,20,23,24] × 3 แกน = 30
  ↓ ต่อ velocity (diff ตามเวลา) อีก 30
= 60 features ต่อเฟรม × 36 เฟรม (~1.3 วินาที)
```

ป้อน landmark ดิบเข้าโมเดลตรง ๆ = มันตอบมั่วอย่างมั่นใจ ไม่ error
เขียนไว้สองที่จึงต้องมีเทสต์คุม — **แก้ไฟล์ไหนก็ต้องรัน `npm test` ใหม่**

### ⚠️ Mirror — จุดที่พลาดง่ายที่สุด

ตอนเก็บข้อมูลเทรนใช้ `cv2.flip(frame, 1)` **ก่อน** ส่งเข้า MediaPipe
ฝั่งเบราว์เซอร์จึงต้องพลิก **ภาพ** ก่อนเข้า MediaPipe เหมือนกัน (`_sourceFrame()`)

**ห้ามใช้วิธี `x → 1-x` เด็ดขาด** สองวิธีนี้ไม่เท่ากัน:

การพลิกภาพทำให้ MediaPipe **สลับ index ซ้าย/ขวา** ของ landmark ด้วย (11↔12, 15↔16, …)
เพราะมันตั้งชื่อตามกายวิภาคที่มันเห็น แปลว่าในข้อมูลเทรน index 15 ที่ชื่อ "wristL"
จริง ๆ บรรจุ**ข้อมือขวา**ของคน

ถ้าพลิกแค่พิกัด → ซ้ายขวาสลับกับที่เทรนมาทั้งหมด → `jab`/`cross` พังทันที
โดยที่โมเดลยังตอบด้วย confidence สูง

วัดจริงด้วย `scripts/probe_mirror.py` ทั้ง 4 คลิปใน `Video/` — พลิก x เฉย ๆ คลาด
0.055–0.088 ส่วนพลิก x พร้อมสลับ index คลาด 0.004–0.008 (ต่างกัน 7.7–15.6 เท่า)

> เอกสารรุ่นก่อนเขียนว่า "landmark ที่เข้า model ต้องเป็นค่าดิบไม่ mirror" — **ผิด**

### ตัวกรองก่อนส่งออก (3 ชั้น)

window เลื่อนทีละเฟรม หมัดเดียวจะถูกทายเป็น `jab` ติดกันหลายสิบเฟรม
ถ้าส่งทุกเฟรม Unity จะได้ jab 30 ครั้งจากหมัดเดียว

| ชั้น | ค่าเริ่มต้น | ทำอะไร |
|---|---|---|
| `threshold` | 0.7 | ความมั่นใจขั้นต่ำ |
| `consensus` | 3 เฟรม | ทายซ้ำกันกี่เฟรมติดก่อนเชื่อ (~100ms) |
| **lock** | `releaseFrames` 3 | ส่งแล้วล็อก จนเห็นว่าออกจากท่านั้นจริง |
| `emitCooldownMs` | 250 | เว้นระยะขั้นต่ำระหว่างสองท่า |

ชั้น lock ต่างจาก tutorial ที่ใช้ `word != sentence[-1]` ซึ่งทำให้ชกท่าเดิมสองครั้งติด
นับได้แค่ครั้งเดียว — ในเกมจริง jab สองหมัดต้องนับสองหมัด

> **ยังไม่ตรงกับเอกสารรุ่นก่อน** ที่ระบุว่าต้องเป็น event-triggered (ตรวจความเร็ว
> ข้อมือ → capture → predict ครั้งเดียว) ของจริงตอนนี้เป็น sliding window ทุกเฟรม
> ถ้าเล่นจริงแล้วรู้สึกว่าต้องค้างท่า ให้กลับมาพิจารณาเปลี่ยนเป็น event-triggered

### `minPresenceRatio` — อย่าปิด

โมเดล**ไม่มีคลาส "ไม่มีคน"** เฟรมที่ MediaPipe หาคนไม่เจอเป็นศูนย์ทั้งแถว
แล้วโมเดลตอบ `block` ด้วยความมั่นใจ **1.000**

ไม่กรอง = คนเล่นเดินออกจากกล้องแล้วได้ "บล็อคสำเร็จ" ฟรีไปเรื่อย ๆ

### ความแม่น

ข้อมูล `LSTM learn/MP_Data_Diag` — 4 session × 6 คลาส × 15 คลิป × 36 เฟรม
แบ่งตาม **session** ไม่ใช่สุ่มคลิป (คลิปใน session เดียวกันใช้แสง/มุม/ฟอร์มเดียวกัน
สุ่มแบ่งจะรั่วข้อมูลข้าม train/test)

| ชุด | accuracy |
|---|---|
| train (session 0,1 ×5 time shift) | 97.3% |
| validation (session 2) | 94.4% |
| **test (session 3 ไม่เคยเห็น)** | **93.3%** |

`hook` ต่ำสุด 88% สับสนกับ `jab`/`cross` — ตัวเลขนี้วัดจากคลิปที่ตัดท่าไว้แล้ว
ของจริงบน rolling buffer จะต่ำกว่า

### คำสั่งที่ใช้บ่อย

```powershell
cd "D:\MuayverseNSC\AI_detection (Sparring)"
npm test                 # 4 ชุด: feature parity / tfjs parity / pipeline / gating
npm run serve            # http://localhost:8000/web/
npm run mock-unity       # จำลอง Unity รับ WebSocket
npm run build:deploy     # ประกอบโฟลเดอร์อัป HTTPS

# เทรนใหม่
.venv\Scripts\python.exe scripts\train_pose_model.py
.venv\Scripts\python.exe scripts\export_reference.py pose_action.h5
.tfjsenv\Scripts\python.exe scripts\convert_to_tfjs.py pose_action.h5
.venv\Scripts\python.exe scripts\export_feature_fixtures.py
```

### หน้าเว็บมีสองหน้า

| ไฟล์ | ใช้ตอน |
|---|---|
| `web/index.html` | **พัฒนา** — มีปุ่มเริ่มกล้อง แถบความมั่นใจ log |
| `web/game.html` | **เล่นจริง** — เริ่มกล้องเอง ไม่มี UI โปร่งใส `?debug=1` เปิด overlay |

WebView ต้องชี้ไป **`game.html`** เพราะตอนซ่อน WebView ไม่มีใครกดปุ่มในหน้าทดสอบได้

---

## Game Logic (Unity)

### Counter Matrix

| Enemy | ผู้เล่นต้องทำ | ผล |
|---|---|---|
| `guard1` | `jab` เดี่ยว | Enemy −5 |
| `guard1` | `jab` → `cross` | Enemy −10 (PERFECT) |
| `guard2` | `hook` | Enemy −5 |
| `jab` | `block` | **ไม่มีใครเสีย HP** (กันสำเร็จ แต่ยังเป็น GOOD) |
| `cross` | `uppercut` | Enemy −5 |
| ท่าอื่น / ไม่ตอบ | — | ผู้เล่น −5 (MISS) |

ยืนยันแล้วว่า "Enemy ตั้งการ์ดแล้วผู้เล่นไม่ทำอะไรเลย" ก็นับเป็น MISS ผู้เล่นเสีย HP

### ท่าผิดไม่ปิดรอบ (`judgeOnFirstPoseOnly = false`)

ผู้เล่นลองได้เรื่อย ๆ จนหมด 4 วินาที ขอแค่มีท่าถูกโผล่มาสักครั้งก็ได้ damage
รอบ `guard1` ทำ `hook` → `jab` → `uppercut` = ได้ GOOD เพราะมี `jab` อยู่ในรอบ
MISS เกิดก็ต่อเมื่อหมดเวลาแล้ว**ไม่เคยเจอท่าถูกเลย**

เหตุผล: detection อ่านผิดเป็นครั้งคราว โหมดเข้ม (ตัดสินจากท่าแรกท่าเดียวตามสเปกเดิม)
จะลงโทษผู้เล่นจากความผิดพลาดของ AI ด้วย — สลับกลับได้ด้วยสวิตช์ใน Inspector

### ตีได้กี่หมัดต่อรอบ (`maxHitsPerRound`)

ค่าเริ่มต้น **1** = ตามสเปกเดิม ตีโดนแล้วปิดรอบ
ตั้ง **3** = รอบ `guard2` ทำ `hook` สามครั้งได้ Enemy −15
ตั้ง **0** = ไม่จำกัด

⚠️ กระทบสมดุลแรง ตัวกรองฝั่งเว็บปล่อยท่าได้เร็วสุดราว 3–4 ครั้งใน 4 วินาที
ถ้าไม่จำกัด ผู้เล่นรัวหมัดเดียวจะฆ่า Enemy (100 HP) ได้ในไม่กี่รอบ
stun ยังเกิดครั้งเดียวต่อรอบเหมือนเดิม

### Game Loop

```
สุ่มท่า Enemy (ไม่ซ้ำท่าเดิมติดกัน, ถ่วงน้ำหนักได้)
  ↓ วนเล่นท่านั้น guard=8 รอบ / jab,cross=4 รอบ
  ↓ แต่ละรอบ 4 วินาที: เล่น animation → เปิด window → ตัดสิน → damage → stun
  ↓ ครบรอบ สุ่มท่าใหม่
จบเมื่อ HP ฝ่ายใดฝ่ายหนึ่ง = 0
```

### Combo

- เฉพาะตอน `guard1` — detect `jab` แล้วรอ 1 วินาทีว่า `cross` ตามมาไหม
- ระหว่างรอ **ห้ามปิด window** และห้ามตัดสินซ้ำ
- `jab` ที่มาตอนเหลือเวลาไม่ถึง 1 วิ → รอเท่าที่เหลือ ไม่ลากข้ามรอบ
  (ถ้า `cross` มาทันในรอบนั้นยังนับเป็น combo — ตีความให้ผู้เล่นได้เปรียบ
  ถ้าต้องการตามสเปกเป๊ะว่าเป็น jab เดี่ยวเสมอ แก้ที่ `BattleJudge.ResolveCombo`)

### Stun

- Enemy เสีย HP → แทรก `enemystun` ทันที เล่น 1 รอบ แล้วกลับท่าเดิม
- **ไม่นับเป็นรอบ ไม่กินเวลา 4 วิ** รอบถัดไปเริ่มนับใหม่หลัง stun จบ
- คลิป `enemystun` ตั้ง **Loop Time = 1** จึงวนไม่จบเอง สคริปต์ต้องสั่งหยุดด้วย
  `stunDuration` (ค่าเริ่มต้น 2.22s = คลิป 3.33s ÷ Speed 1.5 ที่ตั้งใน State)
- ไม่ต้องตั้ง transition ใด ๆ ใน Animator — สคริปต์ยิง `CrossFadeInFixedTime`
  เข้า State ตรง ๆ ตามชื่อ

---

## โครงสร้าง Class

ทุกตัวอยู่ใน `Assets/Scripts/Sparring/` และใน `namespace Sparring`

| Class | หน้าที่ |
|---|---|
| `PoseReceiver` | รับ JSON เข้า `ConcurrentQueue` แกะใน `Update()` กรอง confidence แยก pose/status |
| `SparringEnemyController` | ถือนาฬิกาของเกม — สุ่มท่า นับรอบ เปิด/ปิด window จัดการ stun |
| `BattleJudge` | ตารางตัดสิน + **combo** คำนวณ damage |
| `SparringUIManager` | ข้อความบอกท่า/รอบ, เกรด, ตัวเลข HP, ผลแพ้ชนะ, เตือนเรื่องกล้อง |
| `PlayerPoseAnimator` | เล่น animation ฝั่งผู้เล่นตามท่าที่ตรวจจับได้ |
| `PoseWebViewHost` | เปิด WebView + ขอ permission กล้อง (ใช้บน Android) |
| `PoseWebSocketServer` | WS server ใน Unity สำหรับทดสอบบนเดสก์ท็อป (ไม่ติดไป build) |
| `DebugPoseInput` | จำลองท่าด้วยคีย์บอร์ด/ปุ่มบนจอ (ไม่ติดไป build) |

### ชื่อที่ห้ามใช้ซ้ำ

- **ห้ามตั้งชื่อ `UIManager`** — มีอยู่แล้วที่ `Scripts/FrontendApp/UI_script/UIManager.cs`
  (ตัวสลับ panel) อยู่ใน global namespace จะคอมไพล์ไม่ผ่านทั้งโปรเจกต์
- `EnemyController` ก็เลี่ยงไว้ เพราะมี `EnemyBrain` / `EnemyRandomAnimator` ของโหมดอื่นอยู่

### Event

```csharp
BattleJudge.OnRoundResult(string grade, int damage, bool isPlayerHit)
PoseReceiver.OnPose(string pose, float confidence)
PoseReceiver.OnDetectionStatus(string state, string detail)   // ready/nocamera/nopose/error
SparringEnemyController.OnPoseSelected / OnRoundBegin / OnWindowClosed / OnBattleEnd
```

### ข้อกำหนด

- WebSocket/WebView callback มาจาก thread อื่น → `ConcurrentQueue` + แกะใน `Update()`
  **ไม่มี `MainThreadDispatcher` ในโปรเจกต์** (เอกสารรุ่นก่อนเขียนว่ามี — ไม่มีจริง)
  รูปแบบเดียวกับที่ `PoseUdpReceiver.cs` ใช้อยู่แล้ว
- ค่าทั้งหมดปรับได้จาก Inspector: damage, จำนวนรอบ, ความยาว window, combo wait time

### Protocol

```json
{"type":"pose",   "pose":"jab", "confidence":0.92, "seq":7, "t":12345}
{"type":"status", "state":"nocamera", "detail":"NotAllowedError", "seq":8, "t":12400}
```

`seq` นับต่อเนื่องร่วมกันทั้งสองชนิด ใช้ตรวจว่ามีข้อความหายระหว่างทางไหม

---

## สภาพแวดล้อม

- Unity **6000.0.74f1** · Build target **Android** · Scene **`SparringMode`**
- Input: **New Input System เท่านั้น** (`activeInputHandler: 1`)
  → `Input.GetKeyDown` คอมไพล์ผ่านแต่ **ระเบิดตอนรัน** ต้องใช้ `Keyboard.current`
- UI: **TextMeshPro** (`TMP_Text`) — ยกเว้น `vs` ที่เป็น component อื่น
  ประกาศฟิลด์เป็น `UI.Text` จะลาก TMP ใส่ไม่ได้
- `runInBackground: 1` — เปิดไว้เพื่อให้ Unity ไม่หยุดตอนไปคลิก Chrome
- WebView: **gree/unity-webview** ที่ `Assets/Plugins/` (ติดตั้งจาก unitypackage)
- Scripting define Android: **`UNITYWEBVIEW_ANDROID_ENABLE_CAMERA`**
  ตัวนี้ทำให้ `UnityWebViewPostprocessBuild.cs` ใส่ `CAMERA` permission ให้ตอน build
  **ถ้าไม่มี กล้องจะไม่ทำงานบนมือถือ**

### Hierarchy ที่ใช้อยู่

```
Enemy_Guard1  [Health, SparringEnemyController]  (Animator อยู่ที่ลูก glb)
Player        [Health, PlayerPoseAnimator]
PoseReceiver  [PoseReceiver, PoseWebViewHost, PoseWebSocketServer, DebugPoseInput]
BattleJudge   [BattleJudge]
sparringUI    [SparringUIManager]
Canvas > PlayerHP, EnemyHP, vs, Alert > (text, pose)
```

`EnemyRandomAnimator` บน `Enemy_Guard1` **ต้องปิดไว้** ไม่งั้นแย่งสั่ง animation

### Animator

Enemy (`Sparring Mode/EnemyAnimator.controller`): `guard1` `guard2` `enemyjab`
`enemycross` `enemystun`

Player (`Animetion/PlayerControlller.controller`):

| ท่า | State | Speed |
|---|---|---|
| jab | `PunchL` | 1.5 |
| cross | `PunchR` | 1.5 |
| hook | `DodgingL` | 1.0 |
| uppercut | `ElbowR` | 1.5 |
| block | `block` | 1.0 |
| โดนตี | `Stun` | 1.2 |
| ว่าง | `Idel` | ลูป |

คลิปท่าต่อยทั้งหมด **ไม่ลูป** เล่นจบค้างเฟรมสุดท้าย ต้องมีคนพากลับ `Idel`
(`PlayerPoseAnimator` รอจน `normalizedTime >= 1` ไม่ใช้เวลาตายตัวเพราะ Speed ไม่เท่ากัน)

มี State ชื่อ `block` กับ `block 0` ซ้ำกัน — ใช้ `block` ลบอีกตัวทิ้งได้

---

## ข้อควรระวัง

### กล้อง

- บน Android **กล้องเปิดได้ทีละที่เดียว** — ถ้า Unity ใช้ `WebCamTexture`
  WebView จะเปิดกล้องไม่ได้ ตอนนี้ให้ **WebView เป็นเจ้าของกล้องแต่ผู้เดียว**
- `getUserMedia` ต้องการ **secure context** — `https://` หรือ `http://localhost` เท่านั้น
  `file://` ใช้ไม่ได้ ตอนพัฒนาใช้ `adb reverse tcp:8000 tcp:8000`
- gree **ทำ `onPermissionRequest` + grant `VIDEO_CAPTURE` มาให้แล้ว** ใน `.aar`
  (ตรวจจาก bytecode แล้ว ไม่ต้องแก้ Java) แต่ยังต้องขอ runtime permission
  ฝั่ง Unity ก่อนโหลดหน้าเว็บ — `PoseWebViewHost` ทำให้แล้ว
- URL ต้องมี **`/` ปิดท้าย** หรือชี้ไฟล์ตรง ๆ (path ภายในหน้าเว็บ resolve เทียบกับ
  `import.meta.url` แล้วจึงทนทั้งสองแบบ แต่ใส่ให้ครบชัดเจนกว่า)

### Dataset

- มุมกล้องตอน train ต้องใกล้เคียงมุมตอนใช้จริง (เก็บที่ 640×480 มุมเดียว)
- `Hook` กับ `Uppercut` สับสนกันง่ายในมุม 2D — ลองเพิ่ม joint angles ก่อนเพิ่มข้อมูล
- ท่าที่เก็บเป็นแบบครบวงจร: การ์ด → ออกหมัด → กลับการ์ด (ไม่ค้างท่า)

### Performance

- Latency ที่คาดหวัง 30–70ms (local) · เกมเป็น turn-based 4 วิ/รอบ จึงรับได้
- ยังไม่รู้ว่า MediaPipe `full` + LSTM ที่ ~30fps ไหวบนมือถือไหม
  ถ้าไม่ไหวต้องถอยไป `lite` ซึ่งแลกความแม่น (อาจต้องเก็บข้อมูลเทรนใหม่ด้วย lite)

---

## แผนงาน

```
[x] AI Detection — เทรน (93.3%), แปลง TF.js, หน้าเว็บ, เทสต์ 4 ชุด
[x] Unity Scene + Animation + UI
[x] Game logic — counter matrix, combo, stun, HP, UI
[x] ท่อส่งข้อมูล — unity-webview / websocket / sendmessage
[ ] เทสบน desktop (Unity Editor + Chrome ผ่าน WebSocket)   ← อยู่ตรงนี้
[ ] Build Android — พิสูจน์ว่ากล้องเปิดได้ใน WebView        ← ความเสี่ยงหลักที่เหลือ
[ ] Deploy HTTPS — host ไหนก็ได้ที่เป็น https:// (`npm run build:deploy` → `AI_detection (Sparring)/deploy/`)
    ไม่ใช้ team12.aiforthai.in.th / `ticta-deploy` แล้ว — แยกโปรเจกต์ออกมาตั้งแต่ 2026-10-05
[ ] วัดความแม่นตอนชกจริง + จูนตัวกรอง
```

**หลักการ:** เทสบน desktop ให้ครบก่อนย้ายลงมือถือ เพราะ debug ง่ายกว่ามาก
