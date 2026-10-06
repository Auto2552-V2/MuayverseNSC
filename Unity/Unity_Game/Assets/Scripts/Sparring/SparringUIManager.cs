using System;
using System.Collections;
using UnityEngine;
using TMPro;

namespace Sparring
{
    /// <summary>
    /// แสดงผลทุกอย่างบนจอ — ข้อความบอกท่า, รอบที่เท่าไหร่, เกรด, ตัวเลขเลือด, ผลแพ้ชนะ
    ///
    /// ─── ชื่อ class ─────────────────────────────────────────────────────────────
    /// ตั้งชื่อ SparringUIManager ไม่ใช่ UIManager เพราะโปรเจกต์นี้มี UIManager อยู่แล้ว
    /// (Assets/Scripts/FrontendApp/UI_script/UIManager.cs เป็นตัวสลับ panel) และมันอยู่
    /// ใน global namespace ถ้าตั้งชื่อซ้ำจะคอมไพล์ไม่ผ่านทั้งโปรเจกต์
    ///
    /// ─── ใช้ TMP_Text ไม่ใช่ UnityEngine.UI.Text ────────────────────────────────
    /// ข้อความใน Canvas ของซีนนี้เป็น TextMeshProUGUI ทั้งหมด (ยกเว้น "vs" ที่เป็น
    /// legacy Text) ถ้าประกาศฟิลด์เป็น Text จะลาก TMP ใส่ไม่ได้เลย ช่องจะไม่ยอมรับ
    ///
    /// ใช้ TMP_Text ซึ่งเป็น base class ครอบทั้ง TextMeshProUGUI (UI) และ
    /// TextMeshPro (world space) จึงรับได้ทั้งสองแบบ
    ///
    /// ─── ไม่มีข้อความก็ไม่พัง ───────────────────────────────────────────────────
    /// ทุกช่องเว้นว่างได้ เผื่อยังไม่ได้ทำ UI ครบ — เกมยังเล่นได้ แค่ไม่โชว์ส่วนนั้น
    /// </summary>
    public class SparringUIManager : MonoBehaviour
    {
        [Header("ต่อสาย")]
        [SerializeField] private SparringEnemyController enemy;
        [SerializeField] private BattleJudge judge;
        [SerializeField] private Health playerHealth;
        [SerializeField] private Health enemyHealth;

        [Header("บรรทัดบน — ท่าของ Enemy  (ช่อง Alert/text)")]
        [Tooltip("โชว์ชื่อท่าที่ Enemy กำลังเล่น เช่น ENEMY GUARD")]
        [SerializeField] private TMP_Text statusText;

        [Tooltip("รูปแบบ: {0}=ชื่อท่า {1}=รอบปัจจุบัน {2}=รอบทั้งหมด\n" +
                 "ค่าเริ่มต้นโชว์แค่ชื่อท่า — อยากได้เลขรอบด้วยให้ใส่ \"{0} - Round {1}/{2}\"")]
        [SerializeField] private string statusFormat = "{0}";

        [Header("บรรทัดล่าง — ท่าที่ผู้เล่นทำ  (ช่อง Alert/pose)")]
        [Tooltip("โชว์ท่าล่าสุดที่ตรวจจับได้จากผู้เล่น เช่น JAB\n" +
                 "โชว์ทุกท่าที่ตรวจเจอ ไม่สนว่าถูกหรือผิด — ผู้เล่นต้องเห็นว่าระบบอ่านท่าตนว่าอะไร")]
        [SerializeField] private TMP_Text playerPoseText;

        [Tooltip("ล้างข้อความท่าผู้เล่นหลังผ่านไปกี่วินาที — 0 = ค้างไว้จนกว่าจะมีท่าใหม่")]
        [SerializeField][Min(0f)] private float playerPoseHoldSeconds = 1.5f;

        [Tooltip("สีของท่าที่โมเดลเห็นแต่ยังไม่นับ (realtime) — จางกว่าท่าที่นับแล้ว")]
        [SerializeField] private Color liveColor = new Color(1f, 1f, 1f, 0.45f);

        [Tooltip("ท่า live หายไปแล้วรอกี่วินาทีถึงล้างข้อความ\n\n" +
                 "กันข้อความกะพริบ: ความมั่นใจของโมเดลแกว่งขึ้นลงรอบเส้น 0.35 ตลอดเวลา " +
                 "ถ้าล้างทันทีที่ต่ำกว่าเส้น ข้อความจะหาย ๆ โผล่ ๆ หลายครั้งต่อวินาที\n" +
                 "0 = ล้างทันที (จะกะพริบ)")]
        [SerializeField][Min(0f)] private float liveClearGraceSeconds = 0.4f;

        [Tooltip("ช่องบอกว่าผู้เล่นต้องทำท่าอะไร เช่น PUNCH! (JAB + CROSS)\n" +
                 "เว้นว่างได้ — ตอนนี้ Alert/pose ถูกใช้โชว์ท่าผู้เล่นแทนแล้ว")]
        [SerializeField] private TMP_Text promptText;

        [Header("ตัวเลขเลือด")]
        [Tooltip("ช่อง PlayerHP/Hp(text) — HealthBarUI ขยับแต่หลอด ไม่แตะตัวเลข")]
        [SerializeField] private TMP_Text playerHpText;
        [SerializeField] private TMP_Text enemyHpText;
        [SerializeField] private string hpFormat = "{0}/{1}";

        [Header("ผลการตัดสิน")]
        [Tooltip("ข้อความ PERFECT / GOOD / MISS — เว้นว่างได้ถ้ายังไม่ได้ทำ")]
        [SerializeField] private TMP_Text resultText;

        [Tooltip("โชว์ผลนานกี่วินาทีแล้วค่อยหาย")]
        [SerializeField][Min(0.1f)] private float resultHoldSeconds = 1.2f;

        [SerializeField] private Color perfectColor = new Color(1f, 0.84f, 0.2f);
        [SerializeField] private Color goodColor = new Color(0.35f, 0.9f, 0.45f);
        [SerializeField] private Color missColor = new Color(1f, 0.35f, 0.35f);

        [Header("ป้ายถูก/ผิด")]
        [Tooltip("โชว์เมื่อผู้เล่นตอบถูก (รวมกันสำเร็จที่ไม่มีใครเสีย HP ด้วย)")]
        [SerializeField] private GameObject correctFlash;

        [Tooltip("โชว์เมื่อผู้เล่นตอบผิด/ไม่ตอบ = Enemy ได้โจมตี")]
        [SerializeField] private GameObject incorrectFlash;

        [Tooltip("โชว์นานกี่วินาทีแล้วซ่อน")]
        [SerializeField][Min(0.1f)] private float flashSeconds = 1f;

        [Header("สถานะกล้อง / detection")]
        [Tooltip("ตัวส่งท่า — ใช้ฟังสถานะกล้อง เว้นว่างได้ จะหาให้เอง")]
        [SerializeField] private PoseReceiver poseReceiver;

        [Tooltip("ข้อความเตือนเรื่องกล้อง เว้นว่างได้ จะไปใช้ playerPoseText แทน\n" +
                 "จำเป็นตอนเล่นจริงเพราะ WebView ถูกซ่อน ผู้เล่นไม่มีทางรู้เลยว่ากล้องพัง")]
        [SerializeField] private TMP_Text detectionText;

        [SerializeField] private string noCameraMessage = "กล้องไม่ทำงาน";
        [SerializeField] private string noPoseMessage = "ยืนให้กล้องเห็นตัวเต็ม";

        [Header("นับถอยหลังก่อนเริ่ม")]
        [Tooltip("ช่องโชว์เลขนับถอยหลัง — เว้นว่างได้ จะไปใช้ช่องเดียวกับข้อความจบเกม\n" +
                 "(ช่องนั้นอยู่กลางจออยู่แล้ว จึงใช้ซ้ำได้โดยไม่ต้องสร้าง object ใหม่)")]
        [SerializeField] private TMP_Text countdownText;

        [Tooltip("ข้อความตอนนับถึงศูนย์ ก่อนรอบแรกจะเริ่ม")]
        [SerializeField] private string countdownGoMessage = "FIGHT!";

        [Tooltip("ค้างข้อความ FIGHT! ไว้กี่วินาทีก่อนล้างทิ้ง")]
        [SerializeField][Min(0f)] private float countdownGoHoldSeconds = 0.8f;

        [Header("จบเกม")]
        [Tooltip("ข้อความตอนจบ — เว้นว่างได้ จะไปโชว์ที่ statusText แทน")]
        [SerializeField] private TMP_Text endText;
        [SerializeField] private string winMessage = "YOU WIN!";
        [SerializeField] private string loseMessage = "YOU LOSE";

        [Tooltip("ติ๊ก = บันทึกผลลง MinigameResult เพื่อให้หน้า Reward ในซีนอื่นอ่านต่อ\n" +
                 "(กลไกเดิมของโปรเจกต์ ดู MinigameResult.cs)")]
        [SerializeField] private bool reportToMinigameResult = false;

        // เกรดที่ BattleJudge ส่งมาตอนผู้เล่นตอบผิด/ไม่ตอบ — ต้องตรงกับช่อง Miss Grade
        // ใน BattleJudge ถ้าไปเปลี่ยนที่นั่นต้องเปลี่ยนตรงนี้ด้วย
        private const string MissGrade = "MISS";

        private Coroutine resultRoutine;
        private Coroutine posePoseRoutine;
        private Coroutine flashRoutine;
        private Coroutine liveClearRoutine;
        private Coroutine countdownRoutine;

        private void Awake()
        {
            if (enemy == null) enemy = FindFirstObjectByType<SparringEnemyController>();
            if (judge == null) judge = FindFirstObjectByType<BattleJudge>();
            if (poseReceiver == null) poseReceiver = FindFirstObjectByType<PoseReceiver>();
        }

        private void OnEnable()
        {
            if (enemy != null)
            {
                enemy.OnPoseSelected += HandlePoseSelected;
                enemy.OnRoundBegin += HandleRoundBegin;
                enemy.OnBattleEnd += HandleBattleEnd;
                enemy.OnCountdown += HandleCountdown;
            }
            if (judge != null) judge.OnRoundResult += HandleRoundResult;
            if (poseReceiver != null)
            {
                poseReceiver.OnDetectionStatus += HandleDetectionStatus;
                poseReceiver.OnPose += HandlePlayerPose;
                poseReceiver.OnLivePose += HandleLivePose;
            }

            if (playerHealth != null) playerHealth.OnHealthChanged.AddListener(UpdatePlayerHp);
            if (enemyHealth != null) enemyHealth.OnHealthChanged.AddListener(UpdateEnemyHp);
        }

        /// <summary>
        /// วาดค่าเริ่มต้นลงจอ
        ///
        /// ต้องทำใน Start() ไม่ใช่ OnEnable() — Health ตั้งเลือดเต็มหลอดใน Awake()
        /// ซึ่งไม่รับประกันว่าจะทำงานก่อน OnEnable() ของสคริปต์นี้ ถ้าอ่านเร็วไป
        /// จะได้ CurrentHealth = 0 แล้วจอขึ้น 0/100 ตั้งแต่เริ่มเกม
        ///
        /// Unity รับประกันว่า Awake() ของทุกตัวจบก่อน Start() ตัวแรกเสมอ
        /// </summary>
        private void Start()
        {
            if (playerHealth != null) UpdatePlayerHp(playerHealth.CurrentHealth);
            if (enemyHealth != null) UpdateEnemyHp(enemyHealth.CurrentHealth);
            Show(resultText, "");
            Show(playerPoseText, "");

            // ซ่อน ไม่ใช่แค่ล้างข้อความ — ช่องนี้ถูกใช้ทั้งนับถอยหลังและผลแพ้ชนะ
            SetVisible(endText, false);
            SetVisible(countdownText, false);

            // ซ่อนป้ายทั้งสองไว้ก่อน — ถ้าวางไว้ในซีนแบบเปิดอยู่ มันจะบังจอตั้งแต่เริ่มเกม
            if (correctFlash != null) correctFlash.SetActive(false);
            if (incorrectFlash != null) incorrectFlash.SetActive(false);

            PaintCurrentPose();
        }

        /// <summary>
        /// วาดท่าปัจจุบันของ Enemy ลงจอโดยไม่รอ event
        ///
        /// จำเป็นเพราะ SparringEnemyController เริ่ม BattleLoop ใน OnEnable และ
        /// coroutine จะรันทันทีจนถึง yield แรก — แปลว่ามันสุ่มท่าและยิง OnPoseSelected
        /// ไปเรียบร้อยแล้วตั้งแต่ตอนนั้น
        ///
        /// ถ้า Unity เรียก OnEnable ของ Enemy ก่อนตัวนี้ (ลำดับไม่มีใครรับประกัน)
        /// UI จะพลาด event แรกไปเลย แล้วจอว่างจนกว่ารอบถัดไปจะเริ่ม
        /// </summary>
        private void PaintCurrentPose()
        {
            if (enemy == null || enemy.CurrentPose == null) return;

            Show(statusText, string.Format(statusFormat, enemy.CurrentPose.displayName,
                Mathf.Max(1, enemy.CurrentRound), Mathf.Max(1, enemy.TotalRounds)));
            Show(promptText, enemy.CurrentPose.prompt);
        }

        private void OnDisable()
        {
            if (enemy != null)
            {
                enemy.OnPoseSelected -= HandlePoseSelected;
                enemy.OnRoundBegin -= HandleRoundBegin;
                enemy.OnBattleEnd -= HandleBattleEnd;
                enemy.OnCountdown -= HandleCountdown;
            }
            if (judge != null) judge.OnRoundResult -= HandleRoundResult;
            if (poseReceiver != null)
            {
                poseReceiver.OnDetectionStatus -= HandleDetectionStatus;
                poseReceiver.OnPose -= HandlePlayerPose;
                poseReceiver.OnLivePose -= HandleLivePose;
            }

            if (playerHealth != null) playerHealth.OnHealthChanged.RemoveListener(UpdatePlayerHp);
            if (enemyHealth != null) enemyHealth.OnHealthChanged.RemoveListener(UpdateEnemyHp);
        }

        // ── ข้อความบอกท่า ─────────────────────────────────────────────────────────

        private void HandlePoseSelected(EnemyPoseConfig pose)
        {
            // ตอนเพิ่งสุ่มได้ยังไม่เข้ารอบแรก โชว์ชื่อท่าไว้ก่อนระหว่าง intro delay
            Show(statusText, pose.displayName);
            Show(promptText, pose.prompt);
        }

        private void HandleRoundBegin(EnemyPoseConfig pose, int round, int total)
        {
            Show(statusText, string.Format(statusFormat, pose.displayName, round, total));
            Show(promptText, pose.prompt);

            // ล้างท่าเก่าทิ้งเมื่อขึ้นรอบใหม่ ไม่งั้นผู้เล่นจะเข้าใจผิดว่าท่าของรอบก่อน
            // ยังนับอยู่ในรอบนี้
            ClearPlayerPose();
        }

        // ── ท่าที่ผู้เล่นทำ ────────────────────────────────────────────────────────

        /// <summary>
        /// โชว์ท่าล่าสุดที่ตรวจจับได้ — ไม่สนว่าถูกหรือผิด
        ///
        /// สำคัญกว่าที่คิด: เวลาเล่นแล้ว "ไม่เกิดอะไรขึ้น" ผู้เล่นแยกไม่ออกเลยว่า
        /// ระบบอ่านท่าไม่ออก หรืออ่านออกแต่เป็นท่าที่ตอบผิด ช่องนี้ตอบคำถามนั้น
        /// </summary>
        private void HandlePlayerPose(string pose, float confidence)
        {
            if (playerPoseText == null) return;

            CancelLiveClear();      // ท่าที่นับแล้วสำคัญกว่า live อย่าให้ของเก่ามาล้างทับ
            playerPoseText.color = Color.white;
            Show(playerPoseText, pose.ToUpperInvariant());

            if (posePoseRoutine != null) StopCoroutine(posePoseRoutine);
            if (playerPoseHoldSeconds > 0f)
            {
                posePoseRoutine = StartCoroutine(ClearPlayerPoseAfter(playerPoseHoldSeconds));
            }
        }

        /// <summary>
        /// ท่าที่โมเดลเห็น "ตอนนี้" — อัพเดตตลอดเวลาที่ผู้เล่นขยับ
        ///
        /// โชว์เป็นสีจาง ๆ เพื่อแยกจากท่าที่ "นับแล้ว" (สีขาว) ผู้เล่นจะได้รู้ว่า
        /// ระบบเห็นท่าตนอยู่ แต่ยังไม่มั่นใจพอจะนับเป็นคำตอบ
        ///
        /// ไม่เขียนทับท่าที่เพิ่งนับไป — ถ้าเพิ่งชกติดแล้วค่า live เปลี่ยนเป็นค่าว่าง
        /// ทันที ข้อความจะกระพริบหายซึ่งอ่านไม่ทัน
        /// </summary>
        private void HandleLivePose(string pose, float confidence)
        {
            if (playerPoseText == null) return;
            if (posePoseRoutine != null) return;      // ท่าที่นับแล้วยังโชว์ค้างอยู่

            if (!string.IsNullOrEmpty(pose))
            {
                CancelLiveClear();
                playerPoseText.color = liveColor;
                Show(playerPoseText, pose.ToUpperInvariant());
                return;
            }

            // โมเดลอ่านไม่ออกชั่วขณะ — รอก่อนค่อยล้าง ถ้าท่ากลับมาทันก็ไม่ต้องล้างเลย
            if (liveClearGraceSeconds <= 0f) { Show(playerPoseText, ""); return; }
            if (liveClearRoutine == null)
            {
                liveClearRoutine = StartCoroutine(ClearLiveAfterGrace());
            }
        }

        private IEnumerator ClearLiveAfterGrace()
        {
            yield return new WaitForSeconds(liveClearGraceSeconds);
            // ท่าที่นับแล้วอาจมาโชว์ระหว่างรอ — อย่าไปล้างทับของมัน
            if (posePoseRoutine == null) Show(playerPoseText, "");
            liveClearRoutine = null;
        }

        private void CancelLiveClear()
        {
            if (liveClearRoutine == null) return;
            StopCoroutine(liveClearRoutine);
            liveClearRoutine = null;
        }

        private IEnumerator ClearPlayerPoseAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            Show(playerPoseText, "");
            posePoseRoutine = null;
        }

        private void ClearPlayerPose()
        {
            if (posePoseRoutine != null) { StopCoroutine(posePoseRoutine); posePoseRoutine = null; }
            Show(playerPoseText, "");
        }

        // ── ผลการตัดสิน ───────────────────────────────────────────────────────────

        private void HandleRoundResult(string grade, int damage, bool isPlayerHit)
        {
            // ป้ายถูก/ผิดต้องทำงานแยกจากช่องข้อความ — ไม่งั้นถ้าไม่ได้ต่อ Result Text
            // ไว้ ป้ายก็จะไม่โชว์ไปด้วยทั้งที่ไม่เกี่ยวกัน
            HandleFlash(grade);

            if (resultText == null) return;

            // บอกให้ชัดว่าใครโดน ไม่ใช่โชว์แค่ตัวเลข — ผู้เล่นต้องรู้ทันทีว่าได้แต้มหรือเสียเลือด
            string line = grade;
            if (damage > 0) line += isPlayerHit ? $"  -{damage} YOU" : $"  -{damage} ENEMY";

            resultText.color = ColorFor(grade);
            Show(resultText, line);

            if (resultRoutine != null) StopCoroutine(resultRoutine);
            resultRoutine = StartCoroutine(ClearResultAfter(resultHoldSeconds));
        }

        /// <summary>
        /// โชว์ป้ายถูก/ผิด
        ///
        /// ตัดสินจากเกรด ไม่ใช่จาก damage — "กันสำเร็จ" ได้ GOOD แต่ไม่มีใครเสีย HP
        /// ถ้าดูจาก damage จะกลายเป็นผิดทั้งที่ผู้เล่นทำถูก
        /// </summary>
        private void HandleFlash(string grade)
        {
            bool miss = string.Equals(grade, MissGrade, StringComparison.OrdinalIgnoreCase);
            GameObject show = miss ? incorrectFlash : correctFlash;
            GameObject hide = miss ? correctFlash : incorrectFlash;

            if (hide != null) hide.SetActive(false);
            if (show == null) return;

            show.SetActive(true);

            // ป้ายใหม่มาทับป้ายเก่าเสมอ ไม่งั้น timer ของป้ายเก่าจะมาปิดป้ายใหม่กลางคัน
            if (flashRoutine != null) StopCoroutine(flashRoutine);
            flashRoutine = StartCoroutine(HideFlashAfter(show, flashSeconds));
        }

        private IEnumerator HideFlashAfter(GameObject go, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (go != null) go.SetActive(false);
            flashRoutine = null;
        }

        private Color ColorFor(string grade)
        {
            if (string.Equals(grade, "PERFECT", StringComparison.OrdinalIgnoreCase))
                return perfectColor;
            if (string.Equals(grade, "MISS", StringComparison.OrdinalIgnoreCase))
                return missColor;
            return goodColor;
        }

        private IEnumerator ClearResultAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            Show(resultText, "");
            resultRoutine = null;
        }

        // ── สถานะกล้อง ────────────────────────────────────────────────────────────

        /// <summary>
        /// โชว์ปัญหาเรื่องกล้องให้ผู้เล่นเห็น
        ///
        /// ไม่หยุดเกมให้ เพราะรอบยังเดินต่อและผู้เล่นจะโดน MISS ไปเรื่อย ๆ —
        /// แต่อย่างน้อยต้องบอกว่าทำไม ไม่ใช่ปล่อยให้เงียบแล้วเดาเอาเอง
        /// </summary>
        private void HandleDetectionStatus(string state, string detail)
        {
            // ไม่มีช่องแยก ก็ไปยืมช่องท่าผู้เล่น — ตอนกล้องพังมันว่างอยู่แล้ว
            TMP_Text target = detectionText != null ? detectionText : playerPoseText;
            if (target == null) return;

            switch (state)
            {
                case "nocamera":
                    target.color = missColor;
                    Show(target, noCameraMessage);
                    break;
                case "error":
                    target.color = missColor;
                    Show(target, $"{noCameraMessage} ({detail})");
                    break;
                case "nopose":
                    target.color = missColor;
                    Show(target, noPoseMessage);
                    break;
                case "ready":
                case "pose-ok":
                    // กลับมาปกติ — ล้างคำเตือนทิ้ง ท่าถัดไปที่ตรวจเจอจะมาเขียนทับเอง
                    target.color = Color.white;
                    Show(target, "");
                    break;
            }
        }

        // ── ตัวเลขเลือด ───────────────────────────────────────────────────────────

        private void UpdatePlayerHp(float current) => WriteHp(playerHpText, playerHealth, current);
        private void UpdateEnemyHp(float current) => WriteHp(enemyHpText, enemyHealth, current);

        private void WriteHp(TMP_Text target, Health health, float current)
        {
            if (target == null || health == null) return;
            // ปัดเป็นจำนวนเต็ม — เลือดเป็น float แต่ damage เป็นจำนวนเต็มทั้งหมด
            // ถ้าโชว์ทศนิยมจะเห็น 65.00000 ซึ่งไม่ได้ช่วยอะไร
            Show(target, string.Format(hpFormat,
                Mathf.RoundToInt(current), Mathf.RoundToInt(health.MaxHealth)));
        }

        // ── จบเกม ─────────────────────────────────────────────────────────────────

        private void HandleBattleEnd(bool playerWon)
        {
            string msg = playerWon ? winMessage : loseMessage;

            // ปิดนับถอยหลังก่อน ไม่งั้นมันแย่งช่องเดียวกันแล้วข้อความแพ้ชนะโดนทับ
            if (countdownRoutine != null) { StopCoroutine(countdownRoutine); countdownRoutine = null; }
            SetVisible(countdownText, false);

            if (endText != null)
            {
                // ต้องเปิด GameObject ด้วย ไม่ใช่เซ็ตแค่ .text — ช่องนี้ถูกปิดไว้ในซีน
                // (object ชื่อ lost/win) การเซ็ตข้อความลงของที่ปิดอยู่จึงไม่มีใครเห็น
                // ซึ่งเป็นเหตุผลที่ข้อความจบเกมไม่เคยขึ้นเลย
                SetVisible(endText, true);
                Show(endText, msg);
            }
            else Show(statusText, msg);     // ไม่มีช่องแยกก็ยัดลงบรรทัดบนแทน

            Show(promptText, "");
            if (resultRoutine != null) { StopCoroutine(resultRoutine); resultRoutine = null; }
            Show(resultText, "");

            // ป้ายรอบสุดท้ายอาจยังค้างอยู่ — เก็บทิ้งไม่ให้บังข้อความแพ้/ชนะ
            if (flashRoutine != null) { StopCoroutine(flashRoutine); flashRoutine = null; }
            if (correctFlash != null) correctFlash.SetActive(false);
            if (incorrectFlash != null) incorrectFlash.SetActive(false);

            if (reportToMinigameResult) MinigameResult.Set(playerWon);
        }

        // ── นับถอยหลังก่อนเริ่ม ───────────────────────────────────────────────────

        private void HandleCountdown(int secondsLeft)
        {
            TMP_Text target = countdownText != null ? countdownText : endText;
            if (target == null) target = statusText;
            if (target == null) return;

            if (secondsLeft > 0)
            {
                SetVisible(target, true);
                Show(target, secondsLeft.ToString());
                return;
            }

            Show(target, countdownGoMessage);
            if (countdownRoutine != null) StopCoroutine(countdownRoutine);
            countdownRoutine = StartCoroutine(ClearCountdown(target));
        }

        private IEnumerator ClearCountdown(TMP_Text target)
        {
            yield return new WaitForSeconds(countdownGoHoldSeconds);
            Show(target, "");
            // ซ่อนเฉพาะช่องที่ใช้ชั่วคราว — ถ้าไปตกที่ statusText อย่าปิดทิ้ง
            // เพราะมันเป็นบรรทัดบอกท่า Enemy ที่ต้องใช้ตลอดเกม
            if (target != statusText) SetVisible(target, false);
            countdownRoutine = null;
        }

        /// <summary>เขียนข้อความลง TMP_Text ที่อาจเป็น null ได้ — ช่องที่ไม่ได้ต่อก็แค่ข้ามไป</summary>
        private static void Show(TMP_Text target, string value)
        {
            if (target != null) target.text = value;
        }

        /// <summary>เปิด/ปิด GameObject ของช่องข้อความ — Show() เซ็ตแต่ตัวอักษร</summary>
        private static void SetVisible(TMP_Text target, bool visible)
        {
            if (target != null && target.gameObject.activeSelf != visible)
                target.gameObject.SetActive(visible);
        }
    }
}
