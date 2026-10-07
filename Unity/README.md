# Unity — ตัวเกม

โปรเจกต์อยู่ที่ `Unity/Unity_Game/` เปิดด้วย **Unity 6000.0.74f1** (เวอร์ชันอื่นอาจแปลงไฟล์ซีนจนเปิดกลับไม่ได้)

| ซีน | Build index | ทำอะไร |
|---|---|---|
| `FrontendAPP` | 0 | หน้าหลัก — ล็อกอิน, ภารกิจ, อันดับ, โปรไฟล์, เมนูเลือกโหมด |
| `SparringMode` | 1 | โหมดต่อสู้ |

`SampleScene` กับ `Rhythm` อยู่ใน Build Settings แต่ปิดไว้

---

## เปิดเล่นครั้งแรก

1. รัน backend ก่อน (`backend/api` → `npm run dev`) — หน้าหลักบังคับล็อกอิน
2. เปิดซีน **`FrontendAPP`** แล้วกด Play — **อย่ากด Play ที่ `SparringMode` ตรง ๆ**
   ไม่งั้นจะไม่ได้ล็อกอิน และชนะแล้วไม่ได้เหรียญ
3. ล็อกอิน → เมนูมินิเกม → โหมดต่อสู้
4. เปิดตัวจับท่า (ดู [README หน้าแรก](../README.md#วิธีเล่นบนคอม)) หรือใช้คีย์บอร์ด

---

## โหมดต่อสู้ — GameObject หลัก

```
Enemy_Guard1   SparringEnemyController, SparringMatchEnd, Health
Player         PlayerPoseAnimator, Health
PoseReceiver   PoseReceiver, PoseWebSocketServer, PoseWebViewHost, DebugPoseInput
BattleJudge    BattleJudge
sparringUI     SparringUIManager
```

### ท่ามาจากไหน

`PoseReceiver` รับท่าได้สามทาง เปิดทางที่ใช้ ปิดทางที่ไม่ใช้

| Component | ใช้ตอน | สถานะในซีน |
|---|---|---|
| `PoseWebSocketServer` | เล่นบนคอม — รอที่ `ws://127.0.0.1:8787` | เปิด |
| `PoseWebViewHost` | Android — เปิดหน้าเว็บจับท่าใน WebView | ปิด |
| `DebugPoseInput` | ทดสอบด้วยคีย์บอร์ด `J C H U B` | ปิด |

`PoseWebSocketServer` ปิดตัวเองอัตโนมัติเมื่อ build ไปมือถือ (`Enabled In Build` = ปิด)

---

## ค่าที่ปรับได้ใน Inspector

### `SparringEnemyController` (บน `Enemy_Guard1`)

| ช่อง | ตอนนี้ | |
|---|---|---|
| `Round Duration` | 3 | วินาทีต่อรอบ — เวลาที่ผู้เล่นมีให้ตอบ |
| `Start Countdown Seconds` | 5 | นับถอยหลังก่อนรอบแรก ให้ผู้เล่นถอยไปยืนให้กล้องเห็น |
| `Pose Intro Delay` | 1 | หน่วงก่อนรอบแรกของท่าใหม่ |
| `Poses` → `Rounds` | 3 | แต่ละท่าของคู่ชกเล่นกี่รอบ ก่อนสุ่มท่าใหม่ |
| `Poses` → `Rest Animator State` | ว่าง | ใส่ชื่อ State = เล่นคลิปครั้งเดียวต่อรอบ ไม่วนซ้ำ |

**ถ้าคู่ชกออกท่าซ้ำหลายครั้งในรอบเดียว** — คลิปทุกตัวตั้ง Loop Time ไว้ จะวนเองจนหมดรอบ
ใส่ `Rest Animator State` ของท่า `jab`/`cross` เป็น `guard1` เพื่อให้ออกหมัดครั้งเดียวแล้วกลับการ์ด

### `BattleJudge`

| ช่อง | ตอนนี้ | |
|---|---|---|
| `Counter Rules` | — | ตารางท่าตอบโต้และดาเมจ |
| `Score Every Pose` | ✅ | ทุกท่าในรอบถูกนับ ถูกหักคู่ชก ผิดหักผู้เล่น ทีละท่า |
| `Max Hits Per Round` | 0 | 0 = ตีได้ไม่จำกัดต่อรอบ |
| `Miss Damage To Player` | 1 | ผู้เล่นเสียกี่เลือดต่อท่าผิด |
| `Combo Wait Time` | 1 | รอ `cross` หลัง `jab` กี่วินาทีเพื่อนับ combo |
| `Judge On First Pose Only` | ⬜ | ติ๊ก = ตัดสินจากท่าแรกท่าเดียว (โหมดเข้ม) |

### `SparringMatchEnd` (บน `Enemy_Guard1`)

| ช่อง | ตอนนี้ | |
|---|---|---|
| `Return Delay` | 3 | วินาทีที่โชว์ YOU WIN / YOU LOSE ก่อนกลับหน้าหลัก |
| `Grant Reward` | ✅ | ปิดได้ตอนทดสอบโดยไม่มี backend |

จำนวนเหรียญ**ไม่ได้**ตั้งที่นี่ — อยู่ฝั่งเซิร์ฟเวอร์ ดู [`backend/api/README.md`](../backend/api/README.md)

---

## สคริปต์

```
Assets/Scripts/
├── Sparring/          โหมดต่อสู้ (namespace Sparring)
│   ├── SparringEnemyController   นาฬิกาของเกม: สุ่มท่า นับรอบ นับถอยหลัง
│   ├── BattleJudge               ตารางตัดสิน + combo + ดาเมจ
│   ├── PoseReceiver              รับ JSON จากตัวจับท่า แยก pose/status
│   ├── SparringUIManager         ข้อความ, HP, ผลแพ้ชนะ
│   ├── SparringMatchEnd          จบเกม → จ่ายรางวัล → กลับหน้าหลัก
│   └── PoseWebSocketServer / PoseWebViewHost / DebugPoseInput
└── FrontendApp/       หน้าหลัก
    ├── Backend_script/   AuthApi, MinigameApi, TherapyApi, LeaderboardLoader, ApiConfig
    ├── Backend2/         UserSession, ProfileEditor, MinigameResult, AuthModels
    └── UI_script/        PageManager, SceneLoadButton, BalanceDisplay, TherapyReward
```

URL ของ backend ตั้งที่ช่อง `Base Url` ของ `AuthApi` / `TherapyApi` / `MinigameApi` (ค่าเริ่มต้น `http://localhost:3000`)

รายละเอียดเต็มดู [`CLAUDE.md`](../CLAUDE.md)
