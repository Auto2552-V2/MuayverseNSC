using System;
using UnityEngine;

namespace Sparring
{
    /// <summary>หนึ่งแถวในตารางตัดสิน — ปรับได้ทั้งหมดจาก Inspector</summary>
    [Serializable]
    public class CounterRule
    {
        [Tooltip("ท่าที่ Enemy กำลังเล่น — ต้องตรงกับ id ใน EnemyPoseConfig")]
        public string enemyPose = "guard1";

        [Tooltip("ท่าที่ผู้เล่นตอบ")]
        public string playerPose = "jab";

        [Tooltip("เว้นว่าง = ท่าเดี่ยวจบในตัว\n" +
                 "ใส่ชื่อท่า = ต้องมีท่านี้ตามมาถึงจะนับว่าเข้ากฎข้อนี้ (combo)\n" +
                 "กฎที่มี combo จะถูกเช็คก่อนกฎท่าเดี่ยวของคู่เดียวกันเสมอ")]
        public string comboNext = "";

        [Min(0)] public int damageToEnemy = 5;
        [Min(0)] public int damageToPlayer = 0;

        [Tooltip("PERFECT / GOOD / MISS — ส่งให้ UI เอาไปแสดง")]
        public string grade = "GOOD";
    }

    /// <summary>
    /// ตัดสินว่าท่าที่ผู้เล่นตอบถูกหรือผิด แล้วให้ damage ตามตาราง
    ///
    /// ─── ทำไม combo มาอยู่ที่นี่ ไม่ใช่ใน PoseReceiver ───────────────────────────
    /// การรอ cross ตามหลัง jab ต้องรู้สองอย่างที่ PoseReceiver ไม่มีทางรู้:
    ///   · ตอนนี้ Enemy เล่น guard1 อยู่ไหม (combo ใช้ได้เฉพาะตอนนั้น)
    ///   · เหลือเวลาในรอบพอจะรอครบ 1 วินาทีไหม
    /// ถ้าเอา combo ไปไว้ใน PoseReceiver ต้องเดินสายสถานะของ Enemy ย้อนกลับเข้าไป
    /// ซึ่งทำให้ตัวรับข้อมูลผูกกับ game logic โดยไม่จำเป็น
    /// PoseReceiver จึงทำแค่ รับ -> กรอง confidence -> ส่งต่อ
    ///
    /// ─── กฎที่ละเอียดกว่าที่เห็น ─────────────────────────────────────────────────
    ///   · ให้ damage ได้ครั้งเดียวต่อรอบ พอตัดสินแล้วปิดรับ ท่าหลังจากนั้นถูกเมิน
    ///   · ยกเว้น jab->cross ที่นับเป็นชุดเดียว ระหว่างรอ cross หน้าต่างยังไม่ปิด
    ///   · jab ที่มาตอนเหลือเวลาไม่ถึง 1 วิ จะรอเท่าที่เหลือ ไม่ลากข้ามรอบ
    ///   · ท่าผิดไม่ปิดรอบ (ดู judgeOnFirstPoseOnly) ผู้เล่นลองต่อได้จนหมดเวลา
    ///     — MISS เกิดก็ต่อเมื่อหมดเวลาแล้วไม่เคยเจอท่าถูกเลย
    /// </summary>
    public class BattleJudge : MonoBehaviour
    {
        [Header("ต่อสาย")]
        [SerializeField] private SparringEnemyController enemy;
        [SerializeField] private PoseReceiver poseReceiver;
        [SerializeField] private Health enemyHealth;
        [SerializeField] private Health playerHealth;

        [Header("ตารางตัดสิน")]
        [SerializeField]
        private CounterRule[] rules =
        {
            // combo ต้องอยู่ก่อนท่าเดี่ยวของคู่เดียวกัน เพื่อให้ถูกเจอก่อน
            new CounterRule { enemyPose = "guard1", playerPose = "jab", comboNext = "cross",
                              damageToEnemy = 10, damageToPlayer = 0, grade = "PERFECT" },
            new CounterRule { enemyPose = "guard1", playerPose = "jab", comboNext = "",
                              damageToEnemy = 5, damageToPlayer = 0, grade = "GOOD" },
            new CounterRule { enemyPose = "guard2", playerPose = "hook", comboNext = "",
                              damageToEnemy = 5, damageToPlayer = 0, grade = "GOOD" },
            // กันสำเร็จ: ไม่มีใครเสีย HP แต่ยังนับเป็น GOOD
            new CounterRule { enemyPose = "jab", playerPose = "block", comboNext = "",
                              damageToEnemy = 0, damageToPlayer = 0, grade = "GOOD" },
            new CounterRule { enemyPose = "cross", playerPose = "uppercut", comboNext = "",
                              damageToEnemy = 5, damageToPlayer = 0, grade = "GOOD" },
        };

        [Header("วิธีตัดสิน")]
        [Tooltip("ไม่ติ๊ก (แนะนำ) — ท่าผิดไม่ปิดรอบ ผู้เล่นลองต่อได้จนหมดเวลา\n" +
                 "   รอบ guard1 ทำ hook > jab > uppercut  ->  jab ถูก จึงได้ GOOD\n" +
                 "   ไม่มีท่าถูกเลยจนหมดเวลา  ->  MISS\n\n" +
                 "ติ๊ก — ตัดสินจากท่าแรกท่าเดียว ผิดแล้วจบรอบทันที (ตามสเปกเดิม)\n" +
                 "   รอบ guard1 ทำ hook > jab  ->  MISS ตั้งแต่ hook ส่วน jab ถูกเมิน\n\n" +
                 "ต่างกันมากตอนเล่นจริง เพราะ detection อาจอ่านท่าผิดเป็นครั้งคราว " +
                 "โหมดเข้มจะลงโทษผู้เล่นจากความผิดพลาดของ AI ด้วย")]
        [SerializeField] private bool judgeOnFirstPoseOnly = false;

        [Tooltip("ตีได้กี่หมัดต่อหนึ่งรอบ  (1 = ตามสเปกเดิม, 0 = ไม่จำกัด)\n\n" +
                 "ตั้ง 3 แล้วรอบ guard2 ทำ hook สามครั้ง = Enemy -15\n\n" +
                 "⚠ กระทบสมดุลแรง: ตัวกรองฝั่งเว็บปล่อยท่าได้เร็วสุดราว 3-4 ครั้งใน 4 วินาที " +
                 "ถ้าตั้งไม่จำกัด ผู้เล่นรัวหมัดเดียวจะฆ่า Enemy (100 HP) ได้ในไม่กี่รอบ\n" +
                 "stun ยังเกิดครั้งเดียวต่อรอบเหมือนเดิม (แทรกหลังรอบจบ)")]
        [SerializeField][Min(0)] private int maxHitsPerRound = 1;

        [Tooltip("ติ๊ก = นับทุกท่าที่ทำในรอบ ถูกก็หัก Enemy ผิดก็หักผู้เล่น ทันทีทีละท่า" + "\n" + "\n" +
                 "เช่น Enemy cross แล้วผู้เล่นทำ uppercut > cross > uppercut > uppercut" + "\n" +
                 "จะได้ Enemy -5 / ผู้เล่น -5 / Enemy -5 / Enemy -5" + "\n" + "\n" +
                 "ไม่ติ๊ก = โหมดผ่อนผัน ท่าผิดไม่เสียเลือด ขอแค่มีท่าถูกสักครั้งในรอบ" + "\n" + "\n" +
                 "โหมดนี้ต้องปิด End Round When Judged ที่ SparringEnemyController ด้วย " +
                 "ไม่งั้นรอบจะจบตั้งแต่ท่าแรกแล้วไม่มีท่าที่สองให้นับ" + "\n" +
                 "และควรตั้ง Max Hits Per Round = 0 ถ้าอยากให้ตีถูกได้ไม่จำกัด")]
        [SerializeField] private bool scoreEveryPose = false;

        [Header("ตอบผิด / ไม่ตอบ")]
        [Tooltip("ผู้เล่นเสีย HP เท่าไหร่เมื่อตอบผิดหรือไม่ตอบในเวลา")]
        [SerializeField][Min(0)] private int missDamageToPlayer = 5;
        [SerializeField] private string missGrade = "MISS";

        [Header("Combo")]
        [Tooltip("รอท่าที่สองนานกี่วินาทีหลังได้ท่าแรก — ครบแล้วตัดสินเป็นท่าเดี่ยว")]
        [SerializeField][Min(0.1f)] private float comboWaitTime = 1f;

        [Header("ดีบัก")]
        [SerializeField] private bool logJudgements = true;

        /// <summary>ผลของหนึ่งรอบ (เกรด, damage ที่เกิดขึ้น, ผู้เล่นเป็นฝ่ายโดนไหม)</summary>
        public event Action<string, int, bool> OnRoundResult;

        // สถานะของรอบปัจจุบัน
        private bool windowOpen;
        private bool judged;

        // สถานะการรอท่าที่สองของ combo
        private int wrongAttempts;      // นับเฉพาะโหมดผ่อนผัน ใช้บอกเหตุผลตอน MISS
        private int hitsThisRound;      // ตอบถูกไปกี่ครั้งในรอบนี้
        private bool waitingCombo;
        private CounterRule comboRule;      // กฎ combo ที่กำลังรอให้ครบ
        private CounterRule fallbackRule;   // กฎท่าเดี่ยว ใช้เมื่อรอไม่ครบ
        private float comboDeadline;

        private void OnEnable()
        {
            if (enemy == null || poseReceiver == null)
            {
                Debug.LogError($"[{name}] ต้องลาก SparringEnemyController และ PoseReceiver ใส่ให้ครบ", this);
                enabled = false;
                return;
            }

            // เตือนดัง ๆ ตั้งแต่เริ่ม ไม่ปล่อยให้ไปเจอตอนเล่นว่า "ตัดสินแล้วแต่เลือดไม่ลด"
            // ซึ่งดู log เผิน ๆ เหมือนทุกอย่างปกติเพราะบรรทัดตัดสินยังขึ้นครบ
            if (enemyHealth == null)
            {
                Debug.LogError($"[{name}] ช่อง Enemy Health ว่าง — ตีถูกก็ไม่มีอะไรเสียเลือด", this);
            }
            if (playerHealth == null)
            {
                Debug.LogError($"[{name}] ช่อง Player Health ว่าง — ตอบผิดก็ไม่มีอะไรเสียเลือด", this);
            }
            enemy.OnRoundBegin += HandleRoundBegin;
            enemy.OnWindowClosed += HandleWindowClosed;
            poseReceiver.OnPose += HandlePose;
        }

        private void OnDisable()
        {
            if (enemy != null)
            {
                enemy.OnRoundBegin -= HandleRoundBegin;
                enemy.OnWindowClosed -= HandleWindowClosed;
            }
            if (poseReceiver != null) poseReceiver.OnPose -= HandlePose;
        }

        private void Update()
        {
            // ครบเวลารอ cross แล้วแต่ไม่มา — ตัดสินเป็นท่าเดี่ยว
            if (waitingCombo && Time.time >= comboDeadline) ResolveCombo(followUpArrived: false);
        }

        // ── รอบใหม่ ───────────────────────────────────────────────────────────────

        private void HandleRoundBegin(EnemyPoseConfig pose, int round, int total)
        {
            windowOpen = true;
            judged = false;
            waitingCombo = false;
            comboRule = null;
            fallbackRule = null;
            wrongAttempts = 0;
            hitsThisRound = 0;
        }

        /// <summary>หมดเวลาแล้วยังไม่ได้ตัดสิน — ต้องจบให้ได้เดี๋ยวนี้</summary>
        private void HandleWindowClosed()
        {
            if (judged) return;

            // มี jab ค้างอยู่ รอ cross ไม่ทันหมดรอบ -> ตัดสินเป็นท่าเดี่ยวตามสเปก
            if (waitingCombo)
            {
                ResolveCombo(followUpArrived: false);
                CloseRound();
                return;
            }

            // โหมดนับทุกท่า: ทุกท่าถูกคิดเงินไปแล้วตอนที่มันเกิด ถ้ายังมาหัก MISS
            // ตอนหมดเวลาอีก ผู้เล่นที่ตอบผิดหนึ่งครั้งจะเสียเลือดสองเท่า
            if (scoreEveryPose && (hitsThisRound > 0 || wrongAttempts > 0))
            {
                CloseRound();
                return;
            }

            // ตีโดนไปแล้วในรอบนี้ แค่ยังเหลือโควตาหมัด — ไม่ใช่ MISS
            // (ถ้าไม่เช็คตรงนี้ ผู้เล่นที่ตีถูกแล้วจะโดนหักเลือดตอนหมดเวลาด้วย)
            if (hitsThisRound > 0)
            {
                CloseRound();
                return;
            }

            // หมดเวลาแล้วยังไม่เจอท่าถูก — ตอนนี้ค่อยนับเป็น MISS
            string why = wrongAttempts > 0
                ? $"ตอบผิด {wrongAttempts} ครั้ง ไม่มีท่าถูกเลย"
                : "ไม่ตอบในเวลา";
            Resolve(missGrade, 0, missDamageToPlayer, why);
        }

        // ── ได้ท่าจากผู้เล่น ──────────────────────────────────────────────────────

        private void HandlePose(string playerPose, float confidence)
        {
            if (!windowOpen || judged)
            {
                if (logJudgements) Debug.Log($"[Judge] เมิน {playerPose} (หน้าต่างปิดแล้ว)");
                return;
            }

            EnemyPoseConfig pose = enemy.CurrentPose;
            if (pose == null) return;

            // กำลังรอท่าที่สองของ combo อยู่
            if (waitingCombo)
            {
                if (string.Equals(playerPose, comboRule.comboNext, StringComparison.OrdinalIgnoreCase))
                {
                    ResolveCombo(followUpArrived: true);
                }
                else if (logJudgements)
                {
                    // ไม่ตัดสินผิดทันที เพราะยังอยู่ในชุดเดียวกัน ปล่อยให้รอจนครบเวลา
                    Debug.Log($"[Judge] ระหว่างรอ {comboRule.comboNext} ได้ {playerPose} — เมิน");
                }
                return;
            }

            // กฎที่มี combo ถูกเช็คก่อน ถ้าเจอให้เริ่มรอท่าที่สอง
            CounterRule combo = FindRule(pose.id, playerPose, wantCombo: true);
            if (combo != null)
            {
                comboRule = combo;
                fallbackRule = FindRule(pose.id, playerPose, wantCombo: false);
                waitingCombo = true;

                // รอได้ไม่เกินเวลาที่เหลือในรอบ — jab ที่มาตอนท้ายรอบจึงไม่ลากข้ามรอบ
                // (สเปก: jab ที่มาตอนเหลือน้อยกว่า 1 วิ ให้ตัดสินเป็น jab เดี่ยวเมื่อหมดรอบ)
                float wait = Mathf.Min(comboWaitTime, Mathf.Max(0f, enemy.TimeLeftInRound));
                comboDeadline = Time.time + wait;

                if (logJudgements)
                {
                    Debug.Log($"[Judge] ได้ {playerPose} — รอ {combo.comboNext} อีก {wait:F2}s " +
                              "(หน้าต่างยังไม่ปิด)");
                }
                return;
            }

            // ท่าเดี่ยวธรรมดา
            CounterRule rule = FindRule(pose.id, playerPose, wantCombo: false);
            if (rule != null)
            {
                Resolve(rule.grade, rule.damageToEnemy, rule.damageToPlayer,
                        $"{pose.id} + {playerPose}");
                return;
            }

            // ท่าผิด
            if (judgeOnFirstPoseOnly)
            {
                Resolve(missGrade, 0, missDamageToPlayer, $"{pose.id} + {playerPose} (ผิด)");
                return;
            }

            // โหมดนับทุกท่า: ผิดก็เสียเลือดเดี๋ยวนี้ แล้วเปิดรับท่าถัดไปต่อ
            // (Resolve จะไม่ปิดรอบให้เอง เพราะยังมีเวลาเหลือ — ดูเงื่อนไขใน Resolve)
            if (scoreEveryPose)
            {
                wrongAttempts++;
                Resolve(missGrade, 0, missDamageToPlayer, $"{pose.id} + {playerPose} (ผิด)");
                return;
            }

            // โหมดผ่อนผัน: ไม่ปิดรอบ ปล่อยให้ลองใหม่จนหมดเวลา
            // จะตัดสิน MISS ก็ต่อเมื่อหมดเวลาแล้วยังไม่เจอท่าถูกเลย (HandleWindowClosed)
            wrongAttempts++;
            if (logJudgements)
            {
                Debug.Log($"[Judge] {playerPose} ไม่ใช่คำตอบของ {pose.id} " +
                          $"— ยังเปิดรับอยู่ (ผิดมาแล้ว {wrongAttempts} ครั้ง)");
            }
        }

        /// <summary>
        /// ปิดเคสของ combo
        ///
        /// followUpArrived = true  -> ท่าที่สองมาครบ ใช้กฎ combo
        /// followUpArrived = false -> หมดเวลารอ ใช้กฎท่าเดี่ยวของท่าแรก
        /// </summary>
        private void ResolveCombo(bool followUpArrived)
        {
            waitingCombo = false;
            CounterRule rule = followUpArrived ? comboRule : fallbackRule;

            if (rule == null)
            {
                // ไม่มีกฎท่าเดี่ยวรองรับ = ท่าแรกนั้นผิดอยู่แล้วถ้าไม่ต่อ combo
                Resolve(missGrade, 0, missDamageToPlayer,
                        $"{comboRule?.playerPose} เดี่ยว (ไม่มีกฎรองรับ)");
                return;
            }

            string why = followUpArrived
                ? $"{rule.playerPose} -> {rule.comboNext} (combo)"
                : $"{rule.playerPose} เดี่ยว (รอ {comboRule?.comboNext} ไม่มา)";
            Resolve(rule.grade, rule.damageToEnemy, rule.damageToPlayer, why);
        }

        /// <summary>
        /// จุดเดียวที่ให้ damage — ทุกเส้นทางการตัดสินต้องผ่านที่นี่
        ///
        /// ปิดรอบหรือไม่ขึ้นกับ maxHitsPerRound: ถ้ายังตีได้อีกก็เปิดรับต่อ
        /// MISS ปิดรอบเสมอ เพราะไม่มีอะไรให้ลองต่อแล้ว (หมดเวลาถึงจะมา MISS)
        /// </summary>
        private void Resolve(string grade, int toEnemy, int toPlayer, string reason)
        {
            if (judged) return;
            waitingCombo = false;

            // นับเฉพาะ damage ที่ "ลงจริง" ไม่ใช่ที่ตารางบอก — ถ้าช่อง Health ว่าง
            // หรือฝ่ายนั้นอยู่ในสถานะอมตะ ตัวเลขที่โชว์ต้องสะท้อนของจริง
            int dealtToEnemy = 0, dealtToPlayer = 0;

            if (toEnemy > 0)
            {
                if (enemyHealth != null)
                {
                    float before = enemyHealth.CurrentHealth;
                    enemyHealth.TakeDamage(toEnemy);
                    dealtToEnemy = Mathf.RoundToInt(before - enemyHealth.CurrentHealth);
                    if (dealtToEnemy > 0) enemy.RequestStun();   // โดนตีแล้วต้องมึน
                    else if (logJudgements)
                    {
                        Debug.LogWarning($"[Judge] ตารางบอก -{toEnemy} แต่เลือด Enemy ไม่ลด " +
                                         "(ตายแล้ว หรือ IsInvincible อยู่?)");
                    }
                }
                else
                {
                    Debug.LogError($"[{name}] ตีโดน -{toEnemy} แต่ไม่ได้ลาก Enemy Health ใส่", this);
                }
            }

            if (toPlayer > 0)
            {
                if (playerHealth != null)
                {
                    float before = playerHealth.CurrentHealth;
                    playerHealth.TakeDamage(toPlayer);
                    dealtToPlayer = Mathf.RoundToInt(before - playerHealth.CurrentHealth);
                    // ผู้เล่นโดน = Enemy ได้โจมตี ให้เล่นท่าออกหมัดหลังรอบนี้จบ
                    if (dealtToPlayer > 0) enemy.RequestAttackReaction();
                }
                else
                {
                    Debug.LogError($"[{name}] ผู้เล่นควรเสีย -{toPlayer} แต่ไม่ได้ลาก Player Health ใส่", this);
                }
            }

            int shown = dealtToEnemy > 0 ? dealtToEnemy : dealtToPlayer;
            bool playerHit = dealtToPlayer > 0;
            bool wasMiss = string.Equals(grade, missGrade, StringComparison.OrdinalIgnoreCase);

            // ตอบถูก = นับเป็นหนึ่งหมัดของรอบนี้
            if (!wasMiss) hitsThisRound++;

            // ยังเหลือโควตาหมัดและยังไม่หมดเวลา -> เปิดรับต่อ ไม่ปิดรอบ
            //
            // โหมดนับทุกท่าต่างตรงที่ "ผิด" ก็ไม่ปิดรอบเหมือนกัน เพราะผู้เล่นเพิ่ง
            // เสียเลือดไปกับท่านั้นแล้ว การปิดรอบต่อจะเป็นการลงโทษซ้ำสอง
            bool quotaLeft = maxHitsPerRound <= 0 || hitsThisRound < maxHitsPerRound;
            bool timeLeft = enemy.TimeLeftInRound > 0f;
            bool canHitAgain = timeLeft && (scoreEveryPose
                ? (wasMiss || quotaLeft)
                : (!wasMiss && quotaLeft));

            if (logJudgements)
            {
                string more = canHitAgain
                    ? $"  (หมัดที่ {hitsThisRound} ยังตีต่อได้)"
                    : "";
                Debug.Log($"[Judge] {grade} — {reason} | Enemy -{dealtToEnemy} " +
                          $"ผู้เล่น -{dealtToPlayer}{more}");
            }
            OnRoundResult?.Invoke(grade, shown, playerHit);

            if (canHitAgain)
            {
                // เปิดรับหมัดถัดไป — ล้างสถานะ combo ให้เริ่มนับชุดใหม่ได้
                comboRule = null;
                fallbackRule = null;
                return;
            }

            CloseRound();
        }

        /// <summary>ปิดรับท่าของรอบนี้ — เรียกได้ซ้ำโดยไม่มีผลข้างเคียง</summary>
        private void CloseRound()
        {
            if (judged) return;
            judged = true;
            windowOpen = false;
            waitingCombo = false;
            enemy.NotifyJudged();
        }

        /// <summary>หากฎที่ตรงกับคู่ท่านี้ แยกตามว่าต้องการกฎแบบ combo หรือท่าเดี่ยว</summary>
        private CounterRule FindRule(string enemyPose, string playerPose, bool wantCombo)
        {
            if (rules == null) return null;
            foreach (CounterRule r in rules)
            {
                if (r == null) continue;
                bool isCombo = !string.IsNullOrEmpty(r.comboNext);
                if (isCombo != wantCombo) continue;
                if (!string.Equals(r.enemyPose, enemyPose, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(r.playerPose, playerPose, StringComparison.OrdinalIgnoreCase)) continue;
                return r;
            }
            return null;
        }
    }
}
