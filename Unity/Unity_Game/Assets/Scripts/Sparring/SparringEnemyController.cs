using System;
using System.Collections;
using UnityEngine;

namespace Sparring
{
    /// <summary>ท่าหนึ่งท่าของ Enemy พร้อมทุกอย่างที่เกี่ยวกับท่านั้น ปรับได้จาก Inspector</summary>
    [Serializable]
    public class EnemyPoseConfig
    {
        [Tooltip("ชื่อที่ BattleJudge ใช้อ้างอิงในตารางตัดสิน — guard1 / guard2 / jab / cross")]
        public string id = "guard1";

        [Tooltip("ชื่อ State ใน Animator Controller ต้องสะกดตรงเป๊ะ (ตัวพิมพ์เล็กใหญ่มีผล)")]
        public string animatorState = "guard1";

        [Tooltip("ท่านี้วนกี่รอบก่อนสุ่มท่าใหม่ — การ์ด 8 รอบ ออกหมัด 4 รอบ")]
        [Min(1)] public int rounds = 8;

        [Tooltip("เริ่มเล่นที่เฟรมไหน — ข้ามช่วงต้นคลิปที่ไม่อยากให้เห็น\n" +
                 "0 = เริ่มต้นคลิปตามปกติ (และใช้ CrossFade เกลี่ยรอยต่อให้ด้วย)\n" +
                 "มากกว่า 0 = ตัดเข้าเฟรมนั้นทันที ไม่เกลี่ย เพราะระบุจุดเริ่มกับ " +
                 "CrossFade พร้อมกันไม่ได้")]
        [Min(0f)] public float startFrame = 0f;

        [Tooltip("คลิปนี้ยาวกี่เฟรม ใช้แปลง Start Frame เป็นสัดส่วน\n" +
                 "guard1/guard2 = 50 · enemyjab/enemycross = 100  (ที่ 60fps)")]
        [Min(1f)] public float clipFrames = 100f;

        [Tooltip("ยิ่งมากยิ่งถูกสุ่มบ่อย เทียบกันเป็นสัดส่วน ตั้ง 0 = ปิดท่านั้นชั่วคราว")]
        [Min(0f)] public float weight = 1f;

        [Tooltip("เว้นว่าง = ห้ามซ้ำเฉพาะ \"ท่าเดียวกัน\" เท่านั้น\n\n" +
                 "ใส่ชื่อกลุ่ม = ห้ามสุ่มท่าที่อยู่กลุ่มเดียวกันติดกันด้วย\n" +
                 "ใช้เมื่อหลายท่าหน้าตาคล้ายกันจนผู้เล่นแยกไม่ออก เช่น guard1 กับ guard2 " +
                 "ต่างกันแค่ช่องเปิด ถ้าออกติดกันจะดูเป็นช่วงตั้งการ์ดยาว 16 รอบรวด " +
                 "(เกิดขึ้น 17% ของการสุ่ม) ใส่ \"guard\" ทั้งคู่จะสลับกับท่าออกหมัดเสมอ")]
        public string group = "";

        [Tooltip("ข้อความบอกผู้เล่นว่าท่านี้ต้องตอบด้วยอะไร เช่น PUNCH! (JAB + CROSS)")]
        public string prompt = "PUNCH!";

        [Tooltip("ชื่อท่าที่เอาไปโชว์บนจอ เช่น ENEMY GUARD")]
        public string displayName = "ENEMY GUARD";

        [Tooltip("เว้นว่าง = ปล่อยคลิปวนไปจนหมดรอบ (เหมาะกับท่าการ์ดที่คลิปสั้นและวนแล้วดูปกติ)\n\n" +
                 "ใส่ชื่อ State = เล่นท่านี้ \"ครั้งเดียว\" แล้วสลับไป State ที่ใส่ไว้จนหมดรอบ\n" +
                 "ใช้กับท่าโจมตี: คลิป enemyjab ยาว 1.67 วิ แต่รอบยาว 4 วิ ถ้าปล่อยวน " +
                 "ศัตรูจะต่อย 2-3 ครั้งในรอบเดียว ซึ่งไม่ตรงกับที่ตั้งจำนวนรอบไว้\n" +
                 "ปกติใส่ guard1")]
        public string restAnimatorState = "";
    }

    /// <summary>
    /// หัวใจของโหมดซ้อม — ถือ "นาฬิกา" ของเกมทั้งหมดไว้ที่เดียว
    ///
    /// สุ่มท่า -> วนรอบ -> เปิดหน้าต่างรับท่า -> ปิด -> แทรก stun ถ้าโดนตี -> รอบถัดไป
    ///
    /// ─── ทำไมต้องรวมนาฬิกาไว้ที่เดียว ───────────────────────────────────────────
    /// ถ้าให้ BattleJudge หรือ UI จับเวลาเองด้วย มันจะเพี้ยนจากกันทันทีที่มี stun มาคั่น
    /// (stun ไม่กินเวลาของรอบ แต่กินเวลาจริง) ที่นี่จึงเป็นคนบอกทุกคนว่าตอนนี้รอบไหน
    /// เปิดรับท่าอยู่ไหม ผ่าน event — คนอื่นมีหน้าที่ฟังอย่างเดียว ไม่ต้องนับเอง
    ///
    /// ─── ลำดับของหนึ่งรอบ ───────────────────────────────────────────────────────
    ///   1. เล่น animation ท่าปัจจุบัน
    ///   2. ยิง OnRoundBegin -> BattleJudge เปิดรับท่า, UI อัพเดทข้อความ
    ///   3. รอจนหมด 4 วินาที หรือจนกว่า BattleJudge จะบอกว่าตัดสินแล้ว
    ///   4. ถ้ายังไม่ตัดสิน ยิง OnWindowClosed -> BattleJudge ตัดสินตอนนี้ (MISS / jab เดี่ยว)
    ///   5. ถ้า Enemy โดนตี เล่น stun ให้จบก่อน แล้วค่อยขึ้นรอบใหม่
    ///
    /// ไม่ต้องตั้ง Parameter หรือลาก Transition ใน Animator — สคริปต์ยิง CrossFade เข้า
    /// State ตรง ๆ ตามชื่อ (วิธีเดียวกับ EnemyRandomAnimator ที่โปรเจกต์นี้ใช้อยู่)
    /// </summary>
    public class SparringEnemyController : MonoBehaviour
    {
        [Header("ตัวที่จะสั่ง")]
        [Tooltip("เว้นว่างได้ — จะไปหา Animator บนตัวเองหรือลูก ๆ ให้เอง")]
        [SerializeField] private Animator animator;

        [Tooltip("เลเยอร์ใน Animator ที่จะสั่ง ปกติคือ 0 (Base Layer)")]
        [SerializeField][Min(0)] private int layerIndex = 0;

        [Header("เลือดทั้งสองฝ่าย")]
        [Tooltip("เลือดของ Enemy — ใช้เช็คว่าแพ้/ชนะแล้วหรือยัง")]
        [SerializeField] private Health enemyHealth;
        [Tooltip("เลือดของผู้เล่น")]
        [SerializeField] private Health playerHealth;

        [Header("ท่าของ Enemy")]
        [SerializeField]
        private EnemyPoseConfig[] poses =
        {
            // clipFrames = ความยาวคลิป × 60fps  (guard 0.83s = 50 · หมัด 1.67s = 100)
            new EnemyPoseConfig { id = "guard1", animatorState = "guard1", rounds = 8,
                                  weight = 1f, displayName = "ENEMY GUARD",
                                  prompt = "PUNCH! (JAB + CROSS)", clipFrames = 50f },
            new EnemyPoseConfig { id = "guard2", animatorState = "guard2", rounds = 8,
                                  weight = 1f, displayName = "ENEMY GUARD (OPEN)",
                                  prompt = "HOOK!", clipFrames = 50f },
            // ท่าโจมตีตั้ง restAnimatorState ไว้ ไม่งั้นคลิปจะวนซ้ำในรอบเดียว
            new EnemyPoseConfig { id = "jab", animatorState = "enemyjab", rounds = 4,
                                  weight = 1f, displayName = "ENEMY JAB",
                                  prompt = "BLOCK!", restAnimatorState = "guard1",
                                  clipFrames = 100f },
            new EnemyPoseConfig { id = "cross", animatorState = "enemycross", rounds = 4,
                                  weight = 1f, displayName = "ENEMY CROSS",
                                  prompt = "UPPERCUT!", restAnimatorState = "guard1",
                                  startFrame = 20f, clipFrames = 100f },
        };

        [Header("จังหวะ")]
        [Tooltip("ความยาวของหนึ่งรอบ (วินาที) — เวลาที่ผู้เล่นมีให้ตอบ")]
        [SerializeField][Min(0.5f)] private float roundDuration = 4f;

        [Tooltip("หน่วงก่อนเริ่มรอบแรกของท่าใหม่ ให้ผู้เล่นได้อ่านข้อความก่อน")]
        [SerializeField][Min(0f)] private float poseIntroDelay = 1f;

        [Tooltip("นับถอยหลังกี่วินาทีก่อนรอบแรกจะเริ่ม — 0 = เริ่มทันที\n\n" +
                 "ผู้เล่นต้องถอยไปยืนให้กล้องเห็นตัวเต็มก่อน ซึ่งทำไม่ทันถ้าเกมเริ่มนับรอบ " +
                 "ตั้งแต่วินาทีแรก และรอบแรกจะกลายเป็น MISS ฟรีทุกครั้ง")]
        [SerializeField][Min(0f)] private float startCountdownSeconds = 5f;

        [Tooltip("เวลาเกลี่ยรอยต่อระหว่างท่า (วินาที) 0 = ตัดเปลี่ยนทันทีแบบกระตุก")]
        [SerializeField][Min(0f)] private float crossFadeDuration = 0.15f;

        [Tooltip("ติ๊ก = ตัดสินปุ๊บจบรอบทันที ไม่ต้องรอให้ครบ 4 วิ (ตอบสนองไวกว่า)\n" +
                 "ไม่ติ๊ก = รอให้ครบ 4 วิเสมอ จังหวะเกมจะสม่ำเสมอกว่าแต่มีเวลาตายหลังตอบ")]
        [SerializeField] private bool endRoundWhenJudged = true;

        [Header("Stun — Enemy โดนตี")]
        [Tooltip("ชื่อ State ของท่ามึนงงใน Animator")]
        [SerializeField] private string stunState = "enemystun";

        [Tooltip("เริ่มเล่นที่เฟรมไหน — ข้ามช่วงต้นคลิปที่ไม่อยากให้เห็น\n" +
                 "คลิป enemystun ยาว 200 เฟรม ท่าจริงเริ่มราวเฟรม 110")]
        [SerializeField][Min(0f)] private float stunStartFrame = 110f;

        [Tooltip("คลิปนี้ยาวกี่เฟรมทั้งหมด ใช้แปลงเลขเฟรมเป็นสัดส่วน (enemystun = 200)")]
        [SerializeField][Min(1f)] private float stunClipFrames = 200f;

        [Tooltip("เล่นนานกี่วินาที\n" +
                 "เฟรม 110 ถึงท้ายคลิป = 90 เฟรม ที่ 60fps = 1.5s หารด้วย Speed 1.5 ของ State = 1.0s\n" +
                 "คลิปตั้ง Loop Time ไว้ สคริปต์จึงต้องสั่งหยุดเอง ไม่งั้นวนไม่จบ")]
        [SerializeField][Min(0.1f)] private float stunDuration = 1.0f;

        [Header("Enemy โจมตีสำเร็จ")]
        [Tooltip("เล่นเมื่อผู้เล่นตอบผิด/ไม่ตอบ แล้ว Enemy ได้โจมตี\n" +
                 "เว้นว่าง = ไม่เล่นอะไร (พฤติกรรมเดิม)")]
        [SerializeField] private string attackState = "enemycross";

        [Tooltip("เริ่มเล่นที่เฟรมไหน")]
        [SerializeField][Min(0f)] private float attackStartFrame = 0f;

        [Tooltip("คลิปนี้ยาวกี่เฟรมทั้งหมด (enemycross = 100)")]
        [SerializeField][Min(1f)] private float attackClipFrames = 100f;

        [Tooltip("เล่นนานกี่วินาที — enemycross 100 เฟรม @60fps Speed 1.0 = 1.67s")]
        [SerializeField][Min(0.1f)] private float attackDuration = 1.67f;

        [Header("ดีบัก")]
        [SerializeField] private bool logRounds = true;

        // ── Event ให้คนอื่นฟัง ────────────────────────────────────────────────────

        /// <summary>สุ่มท่าใหม่ได้แล้ว (ท่า) — UI เอาไปขึ้นข้อความ</summary>
        public event Action<EnemyPoseConfig> OnPoseSelected;

        /// <summary>รอบใหม่เริ่ม เปิดรับท่าได้ (ท่า, รอบที่ 1-based, รอบทั้งหมด)</summary>
        public event Action<EnemyPoseConfig, int, int> OnRoundBegin;

        /// <summary>หมดเวลาโดยยังไม่มีการตัดสิน — BattleJudge ต้องตัดสินเดี๋ยวนี้</summary>
        public event Action OnWindowClosed;

        /// <summary>Enemy เริ่มเล่นท่ามึนงง</summary>
        public event Action OnStunBegin;

        /// <summary>Enemy เริ่มเล่นท่าโจมตีสำเร็จ (ผู้เล่นตอบผิด) — ใช้จับจังหวะใส่เอฟเฟกต์</summary>
        public event Action OnEnemyAttack;

        /// <summary>จบการต่อสู้ (true = ผู้เล่นชนะ)</summary>
        public event Action<bool> OnBattleEnd;

        /// <summary>
        /// นับถอยหลังก่อนเริ่มเกม — วินาทีที่เหลือ, 0 = เริ่มแล้ว
        ///
        /// ส่งเป็นจำนวนเต็มเพราะ UI แค่ขึ้นเลขโต้ง ๆ ไม่ได้ทำหลอดเวลา
        /// </summary>
        public event Action<int> OnCountdown;

        // ── สถานะที่คนอื่นอ่านได้ ─────────────────────────────────────────────────

        public EnemyPoseConfig CurrentPose { get; private set; }
        public int CurrentRound { get; private set; }
        public int TotalRounds { get; private set; }
        public bool BattleOver { get; private set; }

        /// <summary>เวลาที่เหลือในรอบนี้ (วินาที) — BattleJudge ใช้ตัดสินใจเรื่อง combo</summary>
        public float TimeLeftInRound { get; private set; }

        private int lastPoseIndex = -1;
        private bool judgedThisRound;
        private bool stunRequested;
        private bool attackRequested;
        private Coroutine restRoutine;

        // ──────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }

        private void OnEnable()
        {
            if (!Validate()) return;

            if (enemyHealth != null) enemyHealth.OnDeath.AddListener(HandleEnemyDeath);
            if (playerHealth != null) playerHealth.OnDeath.AddListener(HandlePlayerDeath);

            StartCoroutine(BattleLoop());
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            if (enemyHealth != null) enemyHealth.OnDeath.RemoveListener(HandleEnemyDeath);
            if (playerHealth != null) playerHealth.OnDeath.RemoveListener(HandlePlayerDeath);
        }

        /// <summary>
        /// เช็คทุกอย่างทีเดียวตอนเริ่ม แล้วบอกให้ชัดว่าอะไรขาด
        ///
        /// ถ้าไม่เช็คตรงนี้ อาการที่เจอคือ "ศัตรูไม่ขยับ" เฉย ๆ โดยไม่มี error
        /// เพราะ CrossFade เข้า State ที่ไม่มีอยู่จริงไม่ทำให้เกิด error ใด ๆ
        /// </summary>
        private bool Validate()
        {
            if (animator == null)
            {
                Debug.LogError($"[{name}] ไม่เจอ Animator — ลากใส่ช่อง Animator ด้วย", this);
                return false;
            }
            if (poses == null || poses.Length == 0)
            {
                Debug.LogError($"[{name}] ยังไม่ได้ตั้งท่าของ Enemy เลย", this);
                return false;
            }

            bool ok = true;
            foreach (EnemyPoseConfig p in poses)
            {
                if (!animator.HasState(layerIndex, Animator.StringToHash(p.animatorState)))
                {
                    Debug.LogError($"[{name}] ไม่มี State ชื่อ '{p.animatorState}' ใน Animator " +
                                   $"layer {layerIndex} (ท่า {p.id})", this);
                    ok = false;
                }
                if (!string.IsNullOrEmpty(p.restAnimatorState) &&
                    !animator.HasState(layerIndex, Animator.StringToHash(p.restAnimatorState)))
                {
                    Debug.LogError($"[{name}] ไม่มี State ชื่อ '{p.restAnimatorState}' ที่ตั้งเป็น " +
                                   $"ท่าพักของ {p.id}", this);
                    ok = false;
                }
            }
            foreach (string s in new[] { stunState, attackState })
            {
                if (string.IsNullOrEmpty(s)) continue;
                if (!animator.HasState(layerIndex, Animator.StringToHash(s)))
                {
                    Debug.LogError($"[{name}] ไม่มี State ชื่อ '{s}' ใน Animator — " +
                                   "ลากคลิปเข้า Controller ก่อน", this);
                    ok = false;
                }
            }

            if (enemyHealth == null || playerHealth == null)
            {
                Debug.LogError($"[{name}] ต้องลาก Health ของทั้งสองฝ่ายใส่ให้ครบ", this);
                ok = false;
            }

            float total = 0f;
            foreach (EnemyPoseConfig p in poses) total += Mathf.Max(0f, p.weight);
            if (total <= 0f)
            {
                Debug.LogError($"[{name}] น้ำหนักของทุกท่าเป็น 0 — สุ่มไม่ได้", this);
                ok = false;
            }
            return ok;
        }

        // ── ลูปหลัก ───────────────────────────────────────────────────────────────

        private IEnumerator BattleLoop()
        {
            yield return StartCountdown();

            while (!BattleOver)
            {
                EnemyPoseConfig pose = PickPose();
                CurrentPose = pose;
                TotalRounds = pose.rounds;

                if (logRounds) Debug.Log($"[Enemy] ท่าใหม่: {pose.id} ({pose.rounds} รอบ)");
                OnPoseSelected?.Invoke(pose);

                PlayPose(pose);

                if (poseIntroDelay > 0f) yield return new WaitForSeconds(poseIntroDelay);

                for (int round = 1; round <= pose.rounds && !BattleOver; round++)
                {
                    yield return RunRound(pose, round);
                }
            }
        }

        /// <summary>
        /// นับถอยหลังก่อนรอบแรก
        ///
        /// รอหนึ่งเฟรมก่อนยิง event ตัวแรก — OnEnable() ของสคริปต์นี้อาจทำงานก่อน
        /// ของ SparringUIManager ซึ่งเป็นตัวที่สมัครรับ event ถ้ายิงทันทีเลข 5
        /// จะหายไปเฉย ๆ บนจอ ส่วนเลขที่เหลือมาปกติ (เป็นบั๊กที่หายากมากเวลาเจอ)
        /// </summary>
        private IEnumerator StartCountdown()
        {
            yield return null;

            int left = Mathf.CeilToInt(startCountdownSeconds);
            while (left > 0 && !BattleOver)
            {
                OnCountdown?.Invoke(left);
                yield return new WaitForSeconds(1f);
                left--;
            }

            OnCountdown?.Invoke(0);
        }

        private IEnumerator RunRound(EnemyPoseConfig pose, int round)
        {
            CurrentRound = round;
            judgedThisRound = false;
            stunRequested = false;
            attackRequested = false;

            // เล่นท่าใหม่ทุกต้นรอบ เพื่อให้ animation เริ่มพร้อมกับหน้าต่างรับท่าเสมอ
            // (ถ้าปล่อยให้วนต่อจากรอบก่อน จังหวะของท่าจะเลื่อนไปเรื่อย ๆ จนไม่ตรงกับเวลาตอบ)
            PlayPose(pose);

            // ท่าโจมตีเล่นครั้งเดียวแล้วกลับไปตั้งการ์ดรอจนหมดรอบ
            if (!string.IsNullOrEmpty(pose.restAnimatorState))
            {
                restRoutine = StartCoroutine(RestAfterOneCycle(pose));
            }

            if (logRounds) Debug.Log($"[Enemy] {pose.id} รอบ {round}/{pose.rounds}");
            OnRoundBegin?.Invoke(pose, round, pose.rounds);

            TimeLeftInRound = roundDuration;
            while (TimeLeftInRound > 0f && !BattleOver)
            {
                if (judgedThisRound && endRoundWhenJudged) break;
                TimeLeftInRound -= Time.deltaTime;
                yield return null;
            }
            TimeLeftInRound = 0f;

            StopRestRoutine();
            if (BattleOver) yield break;

            // หมดเวลาโดยยังไม่ตัดสิน — ให้ BattleJudge ตัดสินเดี๋ยวนี้
            // (เคส MISS หรือ jab ที่มาตอนท้ายรอบแล้วรอ cross ไม่ทัน)
            if (!judgedThisRound) OnWindowClosed?.Invoke();

            // ตัดสินแล้วอาจทำให้เกิดการแทรก animation — Enemy ออกหมัด หรือ Enemy โดนตี
            if (!BattleOver) yield return PlayPendingInserts();
        }

        /// <summary>
        /// เล่นท่าโจมตีให้ครบ "หนึ่งรอบคลิป" แล้วสลับไปท่าพัก
        ///
        /// คลิปทุกตัวตั้ง Loop Time ไว้ ถ้าปล่อยไว้มันจะวนจนหมดรอบ 4 วินาที
        /// (enemyjab ยาว 1.67 วิ -> ต่อย 2-3 ครั้งในรอบเดียว) ซึ่งไม่ตรงกับ
        /// จำนวนรอบที่ตั้งไว้ และผู้เล่นจะงงว่าควรตอบหมัดไหน
        ///
        /// วัดความยาวจาก Animator ตอนรัน ไม่ใช้ค่าตายตัว เพราะแต่ละ State
        /// ตั้ง Speed ไม่เท่ากัน และคลิปอาจถูกเปลี่ยนทีหลัง
        /// </summary>
        private IEnumerator RestAfterOneCycle(EnemyPoseConfig pose)
        {
            int target = Animator.StringToHash(pose.animatorState);
            float giveUp = Time.time + roundDuration;

            while (Time.time < giveUp)
            {
                AnimatorStateInfo si = animator.GetCurrentAnimatorStateInfo(layerIndex);
                if (si.shortNameHash == target && !animator.IsInTransition(layerIndex)) break;
                yield return null;
            }
            while (Time.time < giveUp)
            {
                AnimatorStateInfo si = animator.GetCurrentAnimatorStateInfo(layerIndex);
                if (si.shortNameHash != target) yield break;   // มีท่าอื่นมาแทรกแล้ว (stun)
                if (si.normalizedTime >= 1f) break;
                yield return null;
            }

            if (!BattleOver) PlayState(pose.restAnimatorState);
            restRoutine = null;
        }

        private void StopRestRoutine()
        {
            if (restRoutine != null) { StopCoroutine(restRoutine); restRoutine = null; }
        }

        /// <summary>
        /// แทรก animation หนึ่งตัวให้จบ แล้วกลับไปท่าเดิม
        ///
        /// ใช้ WaitForSeconds ไม่ใช่รอให้ animation จบเอง เพราะคลิปพวกนี้ตั้ง
        /// Loop Time ไว้ มันจึงวนไม่มีวันจบ ต้องมีคนสั่งหยุด
        ///
        /// เริ่มด้วย Animator.Play ไม่ใช่ CrossFade เพราะต้องระบุจุดเริ่มเป็นเฟรมได้
        /// (CrossFade ไม่มีพารามิเตอร์นั้นแบบที่คุมง่าย) และการตัดทันทีก็เหมาะกับ
        /// จังหวะกระแทกอยู่แล้ว ไม่ต้องเกลี่ย
        /// </summary>
        private IEnumerator PlayInsert(string state, float startFrame, float clipFrames,
                                       float duration, string label)
        {
            if (string.IsNullOrEmpty(state)) yield break;

            float normalized = clipFrames > 0f ? Mathf.Clamp01(startFrame / clipFrames) : 0f;
            if (logRounds)
            {
                Debug.Log($"[Enemy] {label} {duration:F2}s (เริ่มเฟรม {startFrame:F0}/{clipFrames:F0})");
            }
            animator.Play(Animator.StringToHash(state), layerIndex, normalized);

            yield return new WaitForSeconds(duration);

            // กลับไปท่าเดิม รอบถัดไปเริ่มนับใหม่ตั้งแต่ต้น — การแทรกจึงไม่กินเวลาของรอบ
            if (!BattleOver && CurrentPose != null) PlayPose(CurrentPose);
        }

        private IEnumerator PlayPendingInserts()
        {
            // Enemy ออกหมัดก่อน แล้วค่อยถึงคิวโดนตี — ตามลำดับเหตุการณ์จริงในรอบ
            if (attackRequested)
            {
                attackRequested = false;
                OnEnemyAttack?.Invoke();
                yield return PlayInsert(attackState, attackStartFrame, attackClipFrames,
                                        attackDuration, "โจมตี");
            }
            if (stunRequested && !BattleOver)
            {
                stunRequested = false;
                OnStunBegin?.Invoke();
                yield return PlayInsert(stunState, stunStartFrame, stunClipFrames,
                                        stunDuration, "stun");
            }
        }

        /// <summary>
        /// เล่น State หนึ่งตัว เริ่มที่เฟรมที่กำหนดได้
        ///
        /// startFrame = 0 ใช้ CrossFade เพื่อเกลี่ยรอยต่อให้ดูลื่น
        /// startFrame > 0 ต้องใช้ Animator.Play เพราะ CrossFade ระบุจุดเริ่มไม่ได้
        /// — ตัดเข้าทันทีไม่เกลี่ย ซึ่งก็สมเหตุผลเพราะคนตั้งค่านี้ตั้งใจข้ามต้นคลิปอยู่แล้ว
        /// </summary>
        private void PlayState(string stateName, float startFrame = 0f, float clipFrames = 0f)
        {
            if (animator == null || string.IsNullOrEmpty(stateName)) return;

            if (startFrame > 0f && clipFrames > 0f)
            {
                animator.Play(Animator.StringToHash(stateName), layerIndex,
                              Mathf.Clamp01(startFrame / clipFrames));
                return;
            }
            animator.CrossFadeInFixedTime(stateName, crossFadeDuration, layerIndex);
        }

        /// <summary>เล่นท่าประจำรอบ โดยเคารพ Start Frame ที่ตั้งไว้ของท่านั้น</summary>
        private void PlayPose(EnemyPoseConfig pose)
        {
            if (pose == null) return;
            PlayState(pose.animatorState, pose.startFrame, pose.clipFrames);
        }

        /// <summary>
        /// สุ่มท่าถัดไปแบบถ่วงน้ำหนัก โดยไม่ซ้ำท่าเดิม (และไม่ซ้ำกลุ่มเดิม ถ้าตั้ง group ไว้)
        ///
        /// กันซ้ำด้วยการ "ตัดตัวเลือกออกก่อนสุ่ม" ไม่ใช่สุ่มใหม่จนกว่าจะไม่ซ้ำ —
        /// วิธีหลังอาจวนไม่จบถ้าเหลือท่าเดียวที่เลือกได้
        /// </summary>
        private EnemyPoseConfig PickPose()
        {
            EnemyPoseConfig picked = PickExcluding(blockSameGroup: true);

            // ตั้งกลุ่มจนไม่เหลือตัวเลือกเลย (เช่นทุกท่าอยู่กลุ่มเดียวกัน)
            // ยอมผ่อนเป็นห้ามซ้ำแค่ท่าเดียวกัน ดีกว่าค้างไปเลย
            if (picked == null) picked = PickExcluding(blockSameGroup: false);

            // ยังไม่ได้อีก = มีท่าที่น้ำหนักไม่เป็นศูนย์อยู่ตัวเดียว ยอมให้ซ้ำ
            if (picked == null)
            {
                lastPoseIndex = Mathf.Clamp(lastPoseIndex, 0, poses.Length - 1);
                return poses[lastPoseIndex];
            }
            return picked;
        }

        private EnemyPoseConfig PickExcluding(bool blockSameGroup)
        {
            string lastGroup = lastPoseIndex >= 0 ? poses[lastPoseIndex].group : "";
            bool groupMatters = blockSameGroup && !string.IsNullOrEmpty(lastGroup);

            float total = 0f;
            for (int i = 0; i < poses.Length; i++)
            {
                if (!Eligible(i, lastGroup, groupMatters)) continue;
                total += Mathf.Max(0f, poses[i].weight);
            }
            if (total <= 0f) return null;

            float roll = UnityEngine.Random.Range(0f, total);
            for (int i = 0; i < poses.Length; i++)
            {
                if (!Eligible(i, lastGroup, groupMatters)) continue;
                roll -= Mathf.Max(0f, poses[i].weight);
                if (roll <= 0f)
                {
                    lastPoseIndex = i;
                    return poses[i];
                }
            }

            // ตกมาถึงตรงนี้ได้จากความคลาดของ float เท่านั้น — หยิบตัวสุดท้ายที่ใช้ได้
            for (int i = poses.Length - 1; i >= 0; i--)
            {
                if (Eligible(i, lastGroup, groupMatters) && poses[i].weight > 0f)
                {
                    lastPoseIndex = i;
                    return poses[i];
                }
            }
            return null;
        }

        private bool Eligible(int i, string lastGroup, bool groupMatters)
        {
            if (i == lastPoseIndex) return false;
            if (groupMatters &&
                string.Equals(poses[i].group, lastGroup, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            return true;
        }

        // ── ทางเข้าสำหรับ BattleJudge ────────────────────────────────────────────

        /// <summary>
        /// บอกว่ารอบนี้ตัดสินไปแล้ว — หน้าต่างรับท่าปิด ท่าที่มาหลังจากนี้ถูกเมิน
        /// (ตัว BattleJudge เป็นคนเมินเอง ที่นี่แค่ใช้ตัดสินใจว่าจะจบรอบเร็วไหม)
        /// </summary>
        public void NotifyJudged() => judgedThisRound = true;

        /// <summary>
        /// Enemy โดนตี — ขอให้แทรก stun หลังรอบนี้จบ
        ///
        /// ไม่เล่น stun ทันทีที่เรียก เพราะ BattleJudge เรียกมาจากกลางลูปของรอบ
        /// การไปหยุด coroutine ตรงนั้นจะทำให้ลำดับ event เพี้ยน
        /// </summary>
        public void RequestStun() => stunRequested = true;

        /// <summary>
        /// ผู้เล่นตอบผิดหรือไม่ตอบ — Enemy ได้โจมตี ขอให้เล่นท่าออกหมัดหลังรอบนี้จบ
        ///
        /// เหตุผลเดียวกับ RequestStun: เรียกมาจากกลางลูปของรอบ ถ้าไปหยุด coroutine
        /// ตรงนั้นลำดับ event จะเพี้ยน
        /// </summary>
        public void RequestAttackReaction() => attackRequested = true;

        /// <summary>true เมื่อยังเหลือเวลาพอให้รอ cross ตามมาหลัง jab</summary>
        public bool HasTimeFor(float seconds) => TimeLeftInRound >= seconds;

        // ── จบเกม ─────────────────────────────────────────────────────────────────

        private void HandleEnemyDeath() => EndBattle(playerWon: true);
        private void HandlePlayerDeath() => EndBattle(playerWon: false);

        private void EndBattle(bool playerWon)
        {
            if (BattleOver) return;
            BattleOver = true;
            StopAllCoroutines();

            if (logRounds) Debug.Log($"[Enemy] จบการต่อสู้ — ผู้เล่น{(playerWon ? "ชนะ" : "แพ้")}");
            OnBattleEnd?.Invoke(playerWon);
        }
    }
}
