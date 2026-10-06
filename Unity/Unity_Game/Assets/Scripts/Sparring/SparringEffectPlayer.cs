using System;
using System.Collections;
using UnityEngine;

namespace Sparring
{
    /// <summary>เอฟเฟกต์ของท่าหนึ่งท่า — ช่องเดียวกับที่ PlayerMovement ใช้ใน SampleScene</summary>
    [Serializable]
    public class PunchEffect
    {
        [Tooltip("ท่าที่จะให้เอฟเฟกต์นี้ทำงาน\n" +
                 "เว้นว่าง = ใช้กับทุกท่าที่ไม่มีเอฟเฟกต์เฉพาะของตัวเอง")]
        public string pose = "";

        [Tooltip("prefab เอฟเฟกต์ — ไม่ใส่ = ไม่มีอะไรเกิดขึ้น")]
        public GameObject prefab;

        [Tooltip("จุดที่จะให้เอฟเฟกต์โผล่ เช่น Transform ของหมัด\n" +
                 "เว้นว่าง = ใช้จุดสำรองของฝ่ายนั้น (ช่อง Fallback ด้านล่าง)")]
        public Transform spawnPoint;

        [Tooltip("ขยับจากจุดนั้นอีกเท่าไหร่ (พิกัดท้องถิ่นของจุด)")]
        public Vector3 localOffset;

        [Tooltip("หมุนเอฟเฟกต์ (องศา)")]
        public Vector3 rotationEuler;

        [Min(0.01f)] public float scale = 1f;

        [Tooltip("ความเร็วการเล่นของ particle/animator ในเอฟเฟกต์")]
        [Min(0.01f)] public float speed = 1f;

        [Tooltip("ลบทิ้งหลังผ่านไปกี่วินาที")]
        [Min(0.01f)] public float destroyDelay = 1.25f;

        [Tooltip("หน่วงกี่วินาทีก่อนเอฟเฟกต์โผล่\n" +
                 "ตั้งให้ตรงจังหวะที่หมัดกระทบจริงในคลิป ไม่ใช่ตอนเริ่มเหวี่ยง " +
                 "ไม่งั้นเอฟเฟกต์จะมาก่อนหมัดถึง")]
        [Min(0f)] public float delaySeconds = 0f;

        [Tooltip("ติ๊ก = ให้เอฟเฟกต์ติดไปกับจุดที่สร้าง (ขยับตามหมัด)\n" +
                 "ไม่ติ๊ก = ค้างอยู่กับที่ตรงจุดที่เกิด")]
        public bool parentToSpawnPoint;

        [Tooltip("เปิดลูก ๆ ของ prefab ที่ถูกปิดไว้ให้ด้วย")]
        public bool activateChildren = true;
    }

    /// <summary>
    /// เอฟเฟกต์ตอนต่อยของทั้งสองฝ่าย — ใช้ PunchEffectPlayer ตัวเดียวกับ SampleScene
    ///
    /// ─── ใช้ของเดิม ไม่เขียนระบบ particle ใหม่ ──────────────────────────────────
    /// PunchEffectPlayer.Play() จัดการเรื่องที่มองไม่เห็นไว้หมดแล้ว: เปิดลูกที่ถูกปิดไว้,
    /// Clear+Play ทุก ParticleSystem, Rebind Animator, ปรับ simulationSpeed, ตั้งเวลาลบทิ้ง
    /// เขียนเองก็ต้องไปเจอเคสเดิมทั้งหมด
    ///
    /// ─── รู้ได้ยังไงว่าท่าไหนต้องมีเอฟเฟกต์ ────────────────────────────────────
    /// ดูจากตารางเอาเอง — ท่าที่ไม่มีแถวในตาราง (เช่น guard1/guard2) ก็ไม่มีเอฟเฟกต์
    /// ไม่ต้องไปฮาร์ดโค้ดว่าท่าไหนเป็นหมัด
    /// </summary>
    public class SparringEffectPlayer : MonoBehaviour
    {
        [Header("ต่อสาย (เว้นว่างได้ จะหาให้เอง)")]
        [SerializeField] private PoseReceiver poseReceiver;
        [SerializeField] private SparringEnemyController enemy;

        [Header("ฝั่งผู้เล่น")]
        [Tooltip("จุดสำรองเมื่อแถวนั้นไม่ได้ใส่ Spawn Point — ปกติลาก Player ใส่")]
        [SerializeField] private Transform playerFallbackPoint;

        [SerializeField]
        private PunchEffect[] playerEffects =
        {
            new PunchEffect { pose = "jab" },
            new PunchEffect { pose = "cross" },
            new PunchEffect { pose = "hook" },
            new PunchEffect { pose = "uppercut" },
        };

        [Header("ฝั่ง Enemy")]
        [Tooltip("จุดสำรอง — ปกติลาก Enemy_Guard1 ใส่")]
        [SerializeField] private Transform enemyFallbackPoint;

        [Tooltip("ท่าที่ Enemy ออกหมัดตอนขึ้นรอบ — ใช้ id ใน EnemyPoseConfig\n" +
                 "ท่าการ์ดไม่ต้องใส่แถว")]
        [SerializeField]
        private PunchEffect[] enemyEffects =
        {
            new PunchEffect { pose = "jab" },
            new PunchEffect { pose = "cross" },
        };

        [Tooltip("เอฟเฟกต์ตอน Enemy โจมตีสำเร็จ (ผู้เล่นตอบผิด) — เล่นคู่กับท่า enemycross")]
        [SerializeField] private PunchEffect enemyAttackReactionEffect = new PunchEffect();

        [Header("ดีบัก")]
        [SerializeField] private bool logEffects = false;

        private void Awake()
        {
            if (poseReceiver == null) poseReceiver = FindFirstObjectByType<PoseReceiver>();
            if (enemy == null) enemy = FindFirstObjectByType<SparringEnemyController>();
        }

        private void OnEnable()
        {
            if (poseReceiver != null) poseReceiver.OnPose += HandlePlayerPose;
            if (enemy != null)
            {
                enemy.OnRoundBegin += HandleEnemyRound;
                enemy.OnEnemyAttack += HandleEnemyAttack;
            }
        }

        private void OnDisable()
        {
            if (poseReceiver != null) poseReceiver.OnPose -= HandlePlayerPose;
            if (enemy != null)
            {
                enemy.OnRoundBegin -= HandleEnemyRound;
                enemy.OnEnemyAttack -= HandleEnemyAttack;
            }
        }

        // ── ทางเข้า ───────────────────────────────────────────────────────────────

        private void HandlePlayerPose(string pose, float confidence)
        {
            Spawn(Find(playerEffects, pose), playerFallbackPoint, $"ผู้เล่น {pose}");
        }

        private void HandleEnemyRound(EnemyPoseConfig pose, int round, int total)
        {
            // ท่าที่ไม่มีแถวในตาราง (การ์ด) จะได้ null แล้วไม่เกิดอะไร
            Spawn(Find(enemyEffects, pose.id), enemyFallbackPoint, $"Enemy {pose.id}");
        }

        private void HandleEnemyAttack()
        {
            Spawn(enemyAttackReactionEffect, enemyFallbackPoint, "Enemy โจมตีสำเร็จ");
        }

        // ── เล่นเอฟเฟกต์ ──────────────────────────────────────────────────────────

        private void Spawn(PunchEffect fx, Transform fallback, string why)
        {
            if (fx == null || fx.prefab == null) return;      // ยังไม่ใส่ = เงียบ

            if (fx.delaySeconds > 0f) StartCoroutine(SpawnAfter(fx, fallback, why));
            else SpawnNow(fx, fallback, why);
        }

        private IEnumerator SpawnAfter(PunchEffect fx, Transform fallback, string why)
        {
            yield return new WaitForSeconds(fx.delaySeconds);
            SpawnNow(fx, fallback, why);
        }

        private void SpawnNow(PunchEffect fx, Transform fallback, string why)
        {
            if (logEffects) Debug.Log($"[Effect] {fx.prefab.name} — {why}");

            PunchEffectPlayer.Play(
                fx.prefab,
                fx.spawnPoint,
                fallback,
                fx.localOffset,
                fx.rotationEuler,
                fx.scale,
                fx.speed,
                fx.destroyDelay,
                fx.parentToSpawnPoint,
                fx.activateChildren,
                $"Sparring:{why}");
        }

        /// <summary>หาแถวของท่านี้ ไม่เจอก็ใช้แถวที่เว้น pose ว่างไว้เป็นตัวกลาง</summary>
        private static PunchEffect Find(PunchEffect[] table, string pose)
        {
            if (table == null) return null;

            PunchEffect wildcard = null;
            foreach (PunchEffect e in table)
            {
                if (e == null) continue;
                if (string.IsNullOrEmpty(e.pose)) { wildcard ??= e; continue; }
                if (string.Equals(e.pose, pose, StringComparison.OrdinalIgnoreCase)) return e;
            }
            return wildcard;
        }
    }
}
