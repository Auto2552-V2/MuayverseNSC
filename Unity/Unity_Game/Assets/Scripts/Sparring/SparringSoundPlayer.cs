using System;
using UnityEngine;

namespace Sparring
{
    /// <summary>เสียงของท่าหนึ่งท่า — ว่างได้ จะไปใช้เสียงกลางแทน</summary>
    [Serializable]
    public class PoseSound
    {
        [Tooltip("ชื่อท่า — ฝั่งผู้เล่นใช้ jab/cross/hook/uppercut/block " +
                 "ฝั่ง Enemy ใช้ id ใน EnemyPoseConfig (guard1/guard2/jab/cross)")]
        public string pose = "jab";

        [Tooltip("เว้นว่าง = ใช้เสียงกลางของฝั่งนั้นแทน")]
        public AudioClip clip;

        public PoseSound(string pose) { this.pose = pose; }
    }

    /// <summary>
    /// ใส่เสียงให้โหมดซ้อม — แค่ฟัง event ที่มีอยู่แล้ว ไม่แตะ game logic เลย
    ///
    /// ─── ใช้ PunchSoundPlayer ที่มีอยู่ ไม่สร้างระบบเสียงใหม่ ───────────────────
    /// ตัวนั้นแก้ปัญหาที่คิดไม่ถึงไว้แล้ว: มี AudioSource หลายช่องเพราะ pitch เป็นค่า
    /// ของ AudioSource ไม่ใช่ของเสียงแต่ละนัด ถ้าใช้ตัวเดียวแล้วสุ่ม pitch ตอนหมัดที่
    /// สองออก เสียงหมัดแรกที่ยังค้างอยู่จะเพี้ยนตามไปด้วย
    ///
    /// ต้องมี PunchSoundPlayer ในซีน (แนบบน Main Camera) — ซีนนี้ยังไม่มี
    ///
    /// ─── ช่องเสียงทั้งหมดเว้นว่างได้ ────────────────────────────────────────────
    /// ไม่ใส่ก็เงียบเฉย ๆ เกมเล่นได้ปกติ ใส่ทีละอันทดสอบไปทีละอันได้
    /// </summary>
    public class SparringSoundPlayer : MonoBehaviour
    {
        [Header("ต่อสาย (เว้นว่างได้ จะหาให้เอง)")]
        [SerializeField] private PoseReceiver poseReceiver;
        [SerializeField] private SparringEnemyController enemy;
        [SerializeField] private BattleJudge judge;

        [Header("เสียงผู้เล่นออกท่า")]
        [Tooltip("เสียงเฉพาะของแต่ละท่า — ท่าไหนไม่ใส่จะใช้เสียงกลางด้านล่าง")]
        [SerializeField]
        private PoseSound[] playerPoseSounds =
        {
            new PoseSound("jab"),
            new PoseSound("cross"),
            new PoseSound("hook"),
            new PoseSound("uppercut"),
            new PoseSound("block"),
        };

        [Tooltip("เสียงกลางของผู้เล่น ใช้กับท่าที่ไม่ได้ใส่เสียงเฉพาะ")]
        [SerializeField] private AudioClip playerDefaultSound;

        [Header("เสียง Enemy ออกท่า")]
        [Tooltip("เล่นตอน Enemy เริ่มรอบของท่าโจมตี — ท่าการ์ดปกติไม่ต้องใส่")]
        [SerializeField]
        private PoseSound[] enemyPoseSounds =
        {
            new PoseSound("jab"),
            new PoseSound("cross"),
        };

        [Tooltip("เสียงกลางของ Enemy")]
        [SerializeField] private AudioClip enemyDefaultSound;

        [Header("เสียงตอนโดน")]
        [Tooltip("Enemy เสีย HP")]
        [SerializeField] private AudioClip enemyHitSound;

        [Tooltip("ผู้เล่นเสีย HP")]
        [SerializeField] private AudioClip playerHitSound;

        [Tooltip("กันสำเร็จ — ไม่มีใครเสีย HP")]
        [SerializeField] private AudioClip blockSuccessSound;

        [Header("เสียงตามเกรด (เว้นว่างได้)")]
        [SerializeField] private AudioClip perfectSound;
        [SerializeField] private AudioClip missSound;

        [Header("เสียงจบเกม")]
        [SerializeField] private AudioClip winSound;
        [SerializeField] private AudioClip loseSound;

        [Header("ดีบัก")]
        [SerializeField] private bool logSounds = false;

        private bool warnedNoPlayer;

        private void Awake()
        {
            if (poseReceiver == null) poseReceiver = FindFirstObjectByType<PoseReceiver>();
            if (enemy == null) enemy = FindFirstObjectByType<SparringEnemyController>();
            if (judge == null) judge = FindFirstObjectByType<BattleJudge>();
        }

        private void OnEnable()
        {
            if (poseReceiver != null) poseReceiver.OnPose += HandlePlayerPose;
            if (enemy != null)
            {
                enemy.OnRoundBegin += HandleEnemyRound;
                enemy.OnBattleEnd += HandleBattleEnd;
            }
            if (judge != null) judge.OnRoundResult += HandleRoundResult;
        }

        private void OnDisable()
        {
            if (poseReceiver != null) poseReceiver.OnPose -= HandlePlayerPose;
            if (enemy != null)
            {
                enemy.OnRoundBegin -= HandleEnemyRound;
                enemy.OnBattleEnd -= HandleBattleEnd;
            }
            if (judge != null) judge.OnRoundResult -= HandleRoundResult;
        }

        private void Start()
        {
            // เตือนครั้งเดียวตอนเริ่ม ไม่ใช่ทุกหมัด — ไม่งั้น Console จะถูกถล่ม
            if (PunchSoundPlayer.Instance == null)
            {
                Debug.LogWarning($"[{name}] ไม่พบ PunchSoundPlayer ในซีน — จะไม่มีเสียงเลย\n" +
                                 "แนบสคริปต์ PunchSoundPlayer บน Main Camera ก่อน", this);
            }
        }

        // ── ผู้เล่นออกท่า ─────────────────────────────────────────────────────────

        private void HandlePlayerPose(string pose, float confidence)
        {
            Play(Lookup(playerPoseSounds, pose) ?? playerDefaultSound, $"ผู้เล่น {pose}");
        }

        // ── Enemy ออกท่า ──────────────────────────────────────────────────────────

        private void HandleEnemyRound(EnemyPoseConfig pose, int round, int total)
        {
            // ท่าที่ไม่ได้ใส่เสียงเฉพาะไว้ (เช่นการ์ด) ไม่ต้องมีเสียง จึงไม่ตกไปใช้เสียงกลาง
            // ถ้าอยากให้การ์ดมีเสียงด้วย เพิ่มแถวใน Enemy Pose Sounds เอง
            AudioClip clip = Lookup(enemyPoseSounds, pose.id);
            if (clip == null && HasEntry(enemyPoseSounds, pose.id)) clip = enemyDefaultSound;
            Play(clip, $"Enemy {pose.id} รอบ {round}");
        }

        // ── ผลการตัดสิน ───────────────────────────────────────────────────────────

        private void HandleRoundResult(string grade, int damage, bool isPlayerHit)
        {
            if (string.Equals(grade, "PERFECT", StringComparison.OrdinalIgnoreCase))
            {
                Play(perfectSound, "PERFECT");
            }
            else if (string.Equals(grade, "MISS", StringComparison.OrdinalIgnoreCase))
            {
                Play(missSound, "MISS");
            }

            if (isPlayerHit) Play(playerHitSound, "ผู้เล่นโดน");
            else if (damage > 0) Play(enemyHitSound, "Enemy โดน");
            else Play(blockSuccessSound, "กันสำเร็จ");   // ถูกแต่ไม่มีใครเสีย HP
        }

        private void HandleBattleEnd(bool playerWon)
        {
            Play(playerWon ? winSound : loseSound, playerWon ? "ชนะ" : "แพ้");
        }

        // ── เล่นเสียง ─────────────────────────────────────────────────────────────

        private void Play(AudioClip clip, string why)
        {
            if (clip == null) return;                       // ยังไม่ใส่ = เงียบ ไม่ใช่ error

            if (PunchSoundPlayer.Instance == null)
            {
                if (!warnedNoPlayer)
                {
                    warnedNoPlayer = true;
                    Debug.LogWarning($"[{name}] มีเสียงจะเล่น ({why}) แต่ไม่มี PunchSoundPlayer", this);
                }
                return;
            }

            if (logSounds) Debug.Log($"[Sound] {clip.name} — {why}");
            PunchSoundPlayer.PlayGlobal(clip, $"Sparring:{why}");
        }

        private static AudioClip Lookup(PoseSound[] table, string pose)
        {
            if (table == null) return null;
            foreach (PoseSound s in table)
            {
                if (s == null) continue;
                if (string.Equals(s.pose, pose, StringComparison.OrdinalIgnoreCase)) return s.clip;
            }
            return null;
        }

        private static bool HasEntry(PoseSound[] table, string pose)
        {
            if (table == null) return false;
            foreach (PoseSound s in table)
            {
                if (s != null && string.Equals(s.pose, pose, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
