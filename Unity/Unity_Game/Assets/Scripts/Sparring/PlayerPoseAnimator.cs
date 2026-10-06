using System;
using System.Collections;
using UnityEngine;

namespace Sparring
{
    /// <summary>จับคู่ท่าที่ AI ตรวจจับได้ กับ State ใน Animator ของผู้เล่น</summary>
    [Serializable]
    public class PoseAnimation
    {
        [Tooltip("ชื่อท่าที่ได้จาก detection — jab / cross / hook / uppercut / block")]
        public string pose = "jab";

        [Tooltip("ชื่อ State ใน PlayerControlller ต้องสะกดตรงเป๊ะ (ตัวพิมพ์เล็กใหญ่มีผล)")]
        public string animatorState = "PunchL";

        [Tooltip("ไม่ติ๊ก — เล่นจบหนึ่งรอบแล้วกลับท่ายืน (เหมาะกับหมัดที่ออกแล้วดึงกลับ)\n\n" +
                 "ติ๊ก — ค้างท่านี้ไว้ตราบที่ผู้เล่นยังทำอยู่ แล้วค่อยกลับท่ายืน\n" +
                 "ใช้กับ block ซึ่งเป็นการ์ดสูงที่ยกค้างไว้ได้นาน ถ้าไม่ติ๊ก ตัวละคร" +
                 "จะยกการ์ดแวบเดียวแล้วลดมือทั้งที่ผู้เล่นยังยกอยู่")]
        public bool holdWhileHeld = false;

        public PoseAnimation(string pose, string animatorState, bool holdWhileHeld = false)
        {
            this.pose = pose;
            this.animatorState = animatorState;
            this.holdWhileHeld = holdWhileHeld;
        }
    }

    /// <summary>
    /// เล่น animation ของผู้เล่นตามท่าที่ตรวจจับได้ แล้วกลับไปท่ายืนเอง
    ///
    /// ─── ทำไมต้องมีตัวนี้ ───────────────────────────────────────────────────────
    /// PoseReceiver แค่บอกว่า "ผู้เล่นออกท่าอะไร" ส่วน BattleJudge ตัดสินแพ้ชนะ
    /// ไม่มีใครรับผิดชอบเรื่องภาพของผู้เล่นเลย ตัวนี้เติมช่องว่างนั้น
    ///
    /// แยกจาก PlayerMovement ของเดิมโดยตั้งใจ — ตัวนั้นมีทั้ง cooldown, การเดิน,
    /// การคำนวณ damage, กล้องสั่น ซึ่งโหมดซ้อมไม่ใช้เลย เอามาใช้จะพ่วงของไม่ต้องการมาด้วย
    ///
    /// ─── กลับไปท่ายืนยังไง ──────────────────────────────────────────────────────
    /// คลิปท่าต่อยทั้งหมดตั้ง Loop Time = 0 พอเล่นจบมันจะค้างเฟรมสุดท้าย ต้องมีคนพากลับ
    /// ที่นี่รอจน normalizedTime >= 1 แล้วค่อย CrossFade กลับ Idel — ไม่ใช้เวลาตายตัว
    /// เพราะแต่ละ State ตั้ง Speed ไม่เท่ากัน (PunchL 1.5, DodgingL 1.0, Stun 1.2)
    /// และคลิปของ block ไม่ได้อยู่ในโฟลเดอร์เดียวกัน วัดความยาวล่วงหน้าไม่ได้
    /// </summary>
    public class PlayerPoseAnimator : MonoBehaviour
    {
        [Header("ต่อสาย")]
        [Tooltip("เว้นว่างได้ — จะไปหา Animator บนตัวเองหรือลูก ๆ ให้เอง")]
        [SerializeField] private Animator animator;

        [Tooltip("ตัวส่งท่าเข้ามา")]
        [SerializeField] private PoseReceiver poseReceiver;

        [Tooltip("เลือดของผู้เล่น — ใช้รู้ว่าโดนตีเมื่อไหร่เพื่อเล่นท่ามึนงง")]
        [SerializeField] private Health playerHealth;

        [SerializeField][Min(0)] private int layerIndex = 0;

        [Header("ท่า")]
        [SerializeField]
        private PoseAnimation[] poseAnimations =
        {
            new PoseAnimation("jab", "PunchL"),
            new PoseAnimation("cross", "PunchR"),
            new PoseAnimation("hook", "DodgingL"),
            new PoseAnimation("uppercut", "ElbowR"),
            new PoseAnimation("block", "block", holdWhileHeld: true),
        };

        [Tooltip("ท่ายืนที่กลับไปหลังเล่นท่าอื่นจบ (คลิปนี้ตั้ง Loop Time ไว้)")]
        [SerializeField] private string idleState = "Idel";

        [Tooltip("ท่าตอนโดนโจมตี — เล่นแทรกทันที ขัดท่าที่กำลังเล่นอยู่")]
        [SerializeField] private string hurtState = "Stun";

        [Header("จังหวะ")]
        [Tooltip("เวลาเกลี่ยรอยต่อระหว่างท่า (วินาที)")]
        [SerializeField][Min(0f)] private float crossFadeDuration = 0.08f;

        [Tooltip("กันค้าง: ถ้ารอให้ท่าจบนานเกินนี้ให้ตัดกลับ Idel เลย\n" +
                 "ปกติไม่ควรถึง มีไว้กันกรณี State ถูกตั้ง Loop Time ไว้โดยไม่ตั้งใจ")]
        [SerializeField][Min(0.5f)] private float maxHoldSeconds = 3f;

        [Header("ท่าที่ค้างได้")]
        [Tooltip("ต้องไม่เห็นท่านั้นนานกี่วินาที ถึงจะถือว่าผู้เล่นเลิกทำแล้ว\n" +
                 "เผื่อไว้เพราะ detection กะพริบเป็นครั้งคราว ถ้าปล่อยกลับท่ายืนทันที " +
                 "ที่อ่านพลาดเฟรมเดียว ตัวละครจะกระตุกขึ้นลง")]
        [SerializeField][Min(0.05f)] private float releaseGraceSeconds = 0.4f;

        [Tooltip("ค้างท่าได้นานสุดกี่วินาที กันค้างถาวรถ้าสัญญาณ live หายไป\n" +
                 "(เช่นตอนทดสอบด้วยปุ่มดีบักซึ่งไม่มีสัญญาณ live ส่งมา)")]
        [SerializeField][Min(1f)] private float maxSustainSeconds = 10f;

        [SerializeField] private bool logPlays = true;

        private Coroutine playing;
        private string heldPose;        // ท่าที่กำลังค้างอยู่ (null = ไม่ได้ค้างอะไร)
        private float lastHeldSeenAt;   // ครั้งสุดท้ายที่เห็นท่านั้นในสัญญาณ live

        private void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (poseReceiver == null) poseReceiver = FindFirstObjectByType<PoseReceiver>();
        }

        private void OnEnable()
        {
            if (!Validate()) { enabled = false; return; }

            poseReceiver.OnPose += HandlePose;
            poseReceiver.OnLivePose += HandleLivePose;
            Debug.Log($"[PlayerAnim] พร้อม — ฟังท่าจาก '{poseReceiver.name}' " +
                      $"บน Animator '{animator.name}'");
            // OnTakeDamage ส่งค่า damage มาด้วย แต่เราสนใจแค่ว่า "โดน" ไม่สนว่ากี่หน่วย
            if (playerHealth != null) playerHealth.OnTakeDamage.AddListener(HandleHurt);

            PlayState(idleState);
        }

        private void OnDisable()
        {
            if (poseReceiver != null)
            {
                poseReceiver.OnPose -= HandlePose;
                poseReceiver.OnLivePose -= HandleLivePose;
            }
            if (playerHealth != null) playerHealth.OnTakeDamage.RemoveListener(HandleHurt);
            StopAllCoroutines();
        }

        /// <summary>
        /// เช็คชื่อ State ทุกตัวตั้งแต่เริ่ม
        ///
        /// CrossFade เข้า State ที่ไม่มีอยู่จริงไม่ทำให้เกิด error ใด ๆ ตัวละครจะยืนนิ่ง
        /// เฉย ๆ แล้วไล่หาสาเหตุยากมาก — เช็คทีเดียวตรงนี้จบ
        /// </summary>
        private bool Validate()
        {
            if (animator == null)
            {
                Debug.LogError($"[{name}] ไม่เจอ Animator ของผู้เล่น", this);
                return false;
            }
            if (poseReceiver == null)
            {
                Debug.LogError($"[{name}] ไม่เจอ PoseReceiver", this);
                return false;
            }

            bool ok = true;
            foreach (PoseAnimation p in poseAnimations)
            {
                if (p == null || string.IsNullOrEmpty(p.animatorState)) continue;
                if (!HasState(p.animatorState))
                {
                    Debug.LogError($"[{name}] ไม่มี State '{p.animatorState}' ใน Animator " +
                                   $"(ท่า {p.pose})", this);
                    ok = false;
                }
            }
            foreach (string s in new[] { idleState, hurtState })
            {
                if (!string.IsNullOrEmpty(s) && !HasState(s))
                {
                    Debug.LogError($"[{name}] ไม่มี State '{s}' ใน Animator", this);
                    ok = false;
                }
            }
            return ok;
        }

        private bool HasState(string state) =>
            animator.HasState(layerIndex, Animator.StringToHash(state));

        // ── ท่าเข้ามา ─────────────────────────────────────────────────────────────

        private void HandlePose(string pose, float confidence)
        {
            PoseAnimation entry = Lookup(pose);
            string state = entry?.animatorState;
            if (string.IsNullOrEmpty(state))
            {
                // เตือนเป็น warning ไม่ใช่ log ธรรมดา — ท่าที่ตรวจเจอแล้วไม่มีใน
                // ตารางแปลว่าตั้งค่าไม่ครบ ผู้เล่นจะเห็นเป็น "ชกแล้วตัวไม่ขยับ"
                Debug.LogWarning($"[PlayerAnim] ไม่มีท่า '{pose}' ในตาราง Pose Animations " +
                                 "— ตัวละครจะไม่ขยับ");
                return;
            }
            // ท่าค้างได้ที่กำลังค้างอยู่แล้ว — ต่ออายุการค้าง ไม่เริ่มเล่นใหม่
            // (block ส่งซ้ำทุก 700ms ตราบที่ยังยกการ์ด ถ้าเล่นใหม่ทุกครั้งจะกระตุก)
            if (entry.holdWhileHeld && heldPose == pose)
            {
                lastHeldSeenAt = Time.time;
                return;
            }

            Play(state, $"ท่า {pose}", entry.holdWhileHeld ? pose : null);
        }

        /// <summary>โดนตี — แทรกท่ามึนงงทันที ขัดท่าที่กำลังเล่นอยู่</summary>
        private void HandleHurt(float damage)
        {
            if (string.IsNullOrEmpty(hurtState)) return;
            Play(hurtState, $"โดน {damage:F0}", null);
        }

        /// <summary>
        /// ท่าที่โมเดลเห็นอยู่ตอนนี้ — ใช้รู้ว่าผู้เล่น "ยังทำท่าค้างอยู่ไหม"
        ///
        /// ใช้สัญญาณ live ไม่ใช่ OnPose เพราะ OnPose มาเป็นจังหวะ ส่วนอันนี้สะท้อน
        /// สภาพปัจจุบันจริง ๆ จึงรู้ได้ทันทีที่ผู้เล่นลดการ์ดลง
        /// </summary>
        private void HandleLivePose(string pose, float confidence)
        {
            if (heldPose == null) return;
            if (string.Equals(pose, heldPose, StringComparison.OrdinalIgnoreCase))
            {
                lastHeldSeenAt = Time.time;
            }
        }

        private void Play(string state, string reason, string holdAs)
        {
            if (logPlays) Debug.Log($"[PlayerAnim] {state} ({reason})");

            // ท่าใหม่มาทับท่าเก่าเสมอ — ถ้าปล่อยให้ coroutine เก่าทำงานต่อ มันจะสั่ง
            // กลับ Idel ตอนท่าเก่าครบเวลา แล้วไปตัดท่าใหม่ที่กำลังเล่นอยู่ทิ้งกลางคัน
            if (playing != null) StopCoroutine(playing);

            heldPose = holdAs;
            lastHeldSeenAt = Time.time;

            playing = holdAs != null
                ? StartCoroutine(HoldWhileHeld(state))
                : StartCoroutine(PlayThenIdle(state));
        }

        /// <summary>
        /// ค้างท่าไว้จนกว่าผู้เล่นจะเลิกทำ
        ///
        /// ออกจากการค้างเมื่ออย่างใดอย่างหนึ่ง:
        ///   · ไม่เห็นท่านั้นนานเกิน releaseGraceSeconds (ผู้เล่นลดการ์ดแล้ว)
        ///   · ค้างนานเกิน maxSustainSeconds (กันค้างถาวรถ้าสัญญาณ live หายไป
        ///     เช่นตอนทดสอบด้วยปุ่มดีบักที่ไม่มี live ส่งมาเลย)
        /// </summary>
        private IEnumerator HoldWhileHeld(string state)
        {
            PlayState(state);
            float startedAt = Time.time;

            while (Time.time - lastHeldSeenAt < releaseGraceSeconds &&
                   Time.time - startedAt < maxSustainSeconds)
            {
                yield return null;
            }

            if (logPlays)
            {
                string why = Time.time - startedAt >= maxSustainSeconds
                    ? $"ครบ {maxSustainSeconds:F0}s" : "ผู้เล่นเลิกทำ";
                Debug.Log($"[PlayerAnim] เลิกค้าง {state} ({why})");
            }

            heldPose = null;
            PlayState(idleState);
            playing = null;
        }

        private IEnumerator PlayThenIdle(string state)
        {
            PlayState(state);

            int target = Animator.StringToHash(state);
            float giveUp = Time.time + maxHoldSeconds;

            // รอให้ transition เข้า State เป้าหมายจริง ๆ ก่อน
            // (ระหว่าง CrossFade ตัว current state ยังเป็นท่าเดิมอยู่)
            while (Time.time < giveUp)
            {
                AnimatorStateInfo si = animator.GetCurrentAnimatorStateInfo(layerIndex);
                if (si.shortNameHash == target && !animator.IsInTransition(layerIndex)) break;
                yield return null;
            }

            // แล้วรอจนเล่นจบหนึ่งรอบ
            while (Time.time < giveUp)
            {
                AnimatorStateInfo si = animator.GetCurrentAnimatorStateInfo(layerIndex);
                if (si.shortNameHash != target) yield break;   // มีท่าอื่นมาแทนแล้ว
                if (si.normalizedTime >= 1f) break;
                yield return null;
            }

            PlayState(idleState);
            playing = null;
        }

        private void PlayState(string state)
        {
            if (animator == null || string.IsNullOrEmpty(state)) return;
            animator.CrossFadeInFixedTime(state, crossFadeDuration, layerIndex);
        }

        private PoseAnimation Lookup(string pose)
        {
            if (poseAnimations == null) return null;
            foreach (PoseAnimation p in poseAnimations)
            {
                if (p == null) continue;
                if (string.Equals(p.pose, pose, StringComparison.OrdinalIgnoreCase)) return p;
            }
            return null;
        }
    }
}
