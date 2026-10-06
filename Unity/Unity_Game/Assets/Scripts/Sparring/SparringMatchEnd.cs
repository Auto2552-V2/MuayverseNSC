using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sparring
{
    /// <summary>
    /// จบแมตช์แล้วพากลับหน้าหลัก พร้อมจ่ายรางวัลถ้าชนะ
    ///
    /// ลำดับ: จบเกม -> ยิง API จ่ายรางวัล (ถ้าชนะ) -> รอให้อ่านข้อความแพ้ชนะ -> โหลดซีน
    ///
    /// ยิง API ทันทีไม่รอครบ 3 วินาที เพราะเน็ตช้าจะได้มีเวลาทำงานไปพร้อมกัน
    /// แต่ก็ไม่บล็อกการเปลี่ยนซีน — รางวัลถูกบันทึกฝั่งเซิร์ฟเวอร์แล้ว หน้าหลัก
    /// ดึงยอดใหม่เองอยู่แล้วตอนเปิด ต่อให้คำตอบมาช้ากว่าการเปลี่ยนซีนก็ไม่หาย
    ///
    /// ทำตามแบบ StageController.FinishMatchRoutine ของมินิเกมอีกตัวหนึ่ง
    /// (MinigameResult -> LoadScene -> TherapyReward.Consume ที่ปลายทาง)
    /// </summary>
    [RequireComponent(typeof(SparringEnemyController))]
    public class SparringMatchEnd : MonoBehaviour
    {
        [Header("กลับหน้าหลัก")]
        [Tooltip("รอกี่วินาทีหลังเกมจบ ก่อนเปลี่ยนซีน — ให้ผู้เล่นได้อ่าน YOU WIN / YOU LOSE")]
        [SerializeField][Min(0f)] private float returnDelay = 3f;

        [Tooltip("ชื่อซีนหน้าหลัก ต้องอยู่ใน Build Settings ไม่งั้นโหลดไม่ได้")]
        [SerializeField] private string returnSceneName = "FrontendAPP";

        [Header("รางวัล")]
        [Tooltip("ไม่ติ๊ก = ไม่ยิง API เลย ใช้ตอนทดสอบเกมโดยไม่มี backend")]
        [SerializeField] private bool grantReward = true;

        [Tooltip("ตัวยิง API — เว้นว่างได้ จะหาในซีนให้เอง")]
        [SerializeField] private MinigameApi api;

        [Tooltip("คีย์เกมที่ส่งให้เซิร์ฟเวอร์ ต้องตรงกับ REWARDS ใน " +
                 "api/app/minigame/complete/route.ts (จำนวนเหรียญกำหนดที่นั่น ไม่ใช่ที่นี่)")]
        [SerializeField] private string gameKey = "sparring";

        [Tooltip("ถ้า API ยังไม่ตอบตอนครบเวลา รออีกไม่เกินกี่วินาทีก่อนเปลี่ยนซีน\n\n" +
                 "มีไว้ให้หน้า Reward ได้ยอดจริงไปโชว์ทัน — เลยเวลานี้ก็ไปต่อ เหรียญ " +
                 "ถูกบันทึกฝั่งเซิร์ฟเวอร์แล้วไม่หาย แค่หน้าสรุปจะไม่ได้โชว์ยอด")]
        [SerializeField][Min(0f)] private float rewardWaitTimeout = 2f;

        private SparringEnemyController enemy;
        private bool finished;
        private bool rewardSettled;

        private void Awake()
        {
            enemy = GetComponent<SparringEnemyController>();
            if (api == null) api = FindFirstObjectByType<MinigameApi>();
        }

        private void OnEnable()
        {
            if (enemy != null) enemy.OnBattleEnd += HandleBattleEnd;
        }

        private void OnDisable()
        {
            if (enemy != null) enemy.OnBattleEnd -= HandleBattleEnd;
        }

        private void HandleBattleEnd(bool playerWon)
        {
            // OnBattleEnd ยิงซ้ำได้ถ้าเลือดสองฝ่ายหมดในเฟรมเดียวกัน
            // ปล่อยผ่านจะจ่ายรางวัลสองรอบและโหลดซีนซ้อนกัน
            if (finished) return;
            finished = true;

            MinigameResult.Set(playerWon);

            if (grantReward) SendResult(playerWon);

            StartCoroutine(ReturnRoutine());
        }

        private void SendResult(bool playerWon)
        {
            if (api == null)
            {
                Debug.LogWarning("[SparringMatchEnd] ไม่มี MinigameApi ในซีน — ไม่ได้บันทึกรางวัล");
                return;
            }

            api.Complete(gameKey, playerWon,
                result =>
                {
                    rewardSettled = true;
                    // ส่งต่อให้หน้า Reward ในซีนถัดไป — ตัวเลขต้องมาจากคำตอบของ
                    // เซิร์ฟเวอร์ ไม่ใช่ค่าที่วางไว้บนหน้าจอ ไม่งั้นจอกับฐานข้อมูลไม่ตรงกัน
                    MinigameResult.SetReward(result.coinEarned, result.starEarned,
                                             result.coins, result.stars);
                    // อัปเดต cache ด้วย แถบบนสุดของหน้าหลักจะได้ขึ้นยอดใหม่ทันที
                    // ที่กลับไปถึง ไม่ต้องรอ GET /api/users/:id อีกรอบ
                    UserSession.SetBalance(result.coins, result.stars);
                    Debug.Log(
                        $"[SparringMatchEnd] ได้ {result.coinEarned} เหรียญ {result.starEarned} ดาว " +
                        $"(ยอดรวม {result.coins} / {result.stars})");
                },
                // ล้มตรงนี้ไม่ควรขวางผู้เล่นกลับหน้าหลัก — อย่างแย่ที่สุดคือไม่ได้เหรียญรอบนี้
                error =>
                {
                    rewardSettled = true;
                    Debug.LogWarning($"[SparringMatchEnd] บันทึกรางวัลไม่ได้: {error}");
                });
        }

        private IEnumerator ReturnRoutine()
        {
            if (returnDelay > 0f) yield return new WaitForSeconds(returnDelay);

            // ยืดเวลาให้คำตอบตามมาทัน เฉพาะตอนที่ยังรออยู่จริง — ถ้าปิด grantReward
            // หรือไม่มี API ในซีน rewardSettled จะไม่มีวันเป็น true จึงต้องมีเพดาน
            if (grantReward && api != null)
            {
                float deadline = Time.time + rewardWaitTimeout;
                while (!rewardSettled && Time.time < deadline) yield return null;
            }

            // เช็คก่อนโหลดแบบเดียวกับ SceneLoadButton — LoadScene ที่หาซีนไม่เจอ
            // จะค้างอยู่เงียบ ๆ โดยไม่มีอะไรบอกว่าเพราะอะไร
            if (string.IsNullOrWhiteSpace(returnSceneName) ||
                !SceneLoadButton.IsInBuildSettings(returnSceneName))
            {
                Debug.LogError($"[SparringMatchEnd] ไม่มีซีน \"{returnSceneName}\" ใน Build Settings " +
                               "— กลับหน้าหลักไม่ได้");
                yield break;
            }

            SceneManager.LoadScene(returnSceneName);
        }
    }
}
