using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

[DefaultExecutionOrder(-100)]
public class StageController : MonoBehaviour
{
    /// <summary>จะทำอะไรต่อเมื่อแมตช์จบ</summary>
    public enum MatchEndAction
    {
        /// <summary>เด้ง alert บนหน้าเว็บ แล้วพาออกจากเกมเมื่อกด OK — ไม่ต้องมีซีนอื่นใน Build</summary>
        AlertAndLeaveWeb,

        /// <summary>โหลดซีนหน้า Reward (ต้องติ๊กซีนนั้นไว้ใน Build Settings ด้วย)</summary>
        LoadResultScene,
    }
    private static StageController activeStage;

    [Header("Player Stage")]
    [SerializeField] private PlayerMovement player;
    [SerializeField] private Transform playerSpawnPoint;

    [Header("Enemy Stage")]
    [SerializeField] private EnemyBrain enemy;
    [SerializeField] private Transform enemySpawnPoint;

    [Header("จบแมตช์")]
    [Tooltip("Alert And Leave Web = เด้งกล่องข้อความบนหน้าเว็บ กด OK แล้วออกจากเกมเลย " +
             "(build แค่ SampleScene ตัวเดียวก็ใช้ได้)\n" +
             "Load Result Scene = พฤติกรรมเดิม โหลดซีนหน้า Reward ต่อ")]
    [SerializeField] private MatchEndAction endAction = MatchEndAction.AlertAndLeaveWeb;

    [Tooltip("หน่วงก่อนตัดออกจากสนาม ให้เห็นจังหวะน็อกก่อน (วินาที)")]
    [SerializeField][Min(0f)] private float resultDelay = 2f;

    [Header("โหมด Alert And Leave Web")]
    [Tooltip("ข้อความในกล่อง alert เมื่อผู้เล่นชนะ")]
    [SerializeField] private string winMessage = "คุณชนะ! เก่งมาก";

    [Tooltip("ข้อความในกล่อง alert เมื่อผู้เล่นแพ้")]
    [SerializeField] private string loseMessage = "คุณแพ้แล้ว ลองอีกครั้งนะ";

    [Tooltip("path ที่จะไปหลังผู้เล่นกด OK ต้องขึ้นต้นด้วย / (เช่น /rehab)\n" +
             "เว้นว่าง = ถอยกลับหน้าเดิมที่พาเข้าเกมมา ซึ่งปกติเป็นสิ่งที่ต้องการ")]
    [SerializeField] private string exitPath = "";

    [Header("โหมด Load Result Scene")]
    [Tooltip("ซีนที่มีหน้า Reward_Page อยู่ — ต้องอยู่ใน Build Settings")]
    [SerializeField] private string resultSceneName = "FrontendAPP";

    private Health playerHealth;
    private Health enemyHealth;
    private bool matchOver;

#if UNITY_WEBGL && !UNITY_EDITOR
    // นิยามอยู่ใน Assets/Plugins/WebGL/SiteNavigation.jslib
    [DllImport("__Internal")]
    private static extern void TictaAlertAndLeave(string message, string url);
#endif

    public static StageController ActiveStage => activeStage;
    public PlayerMovement Player => player;
    public EnemyBrain Enemy => enemy;
    public Transform PlayerTransform => player != null ? player.transform : null;
    public Transform EnemyTransform => enemy != null ? enemy.transform : null;

    public static StageController GetOrCreateActiveStage()
    {
        if (activeStage != null)
        {
            return activeStage;
        }

        activeStage = FindFirstObjectByType<StageController>();
        if (activeStage != null)
        {
            return activeStage;
        }

        GameObject stageObject = new GameObject("StageController");
        activeStage = stageObject.AddComponent<StageController>();
        return activeStage;
    }

    private void Awake()
    {
        if (activeStage != null && activeStage != this)
        {
            Debug.LogWarning("[StageController] More than one StageController found. Using the first active stage.");
            return;
        }

        activeStage = this;
        ResolveStageActors();
        ApplySpawnPoints();
    }

    // ผูกอีเวนต์ตายใน Start ไม่ใช่ Awake — PlayerMovement/EnemyBrain เพิ่งมา Register ตัวเอง
    // ตอน Awake ของมัน กว่าจะรู้ว่าใครเป็นใครครบก็ต้องรอถึง Start
    private void Start()
    {
        if (activeStage != this) return;

        playerHealth = FindHealth(PlayerTransform, "ผู้เล่น");
        enemyHealth = FindHealth(EnemyTransform, "ศัตรู");

        if (playerHealth != null) playerHealth.OnDeath.AddListener(HandlePlayerDeath);
        if (enemyHealth != null) enemyHealth.OnDeath.AddListener(HandleEnemyDeath);
    }

    private void OnDestroy()
    {
        if (playerHealth != null) playerHealth.OnDeath.RemoveListener(HandlePlayerDeath);
        if (enemyHealth != null) enemyHealth.OnDeath.RemoveListener(HandleEnemyDeath);

        if (activeStage == this)
        {
            activeStage = null;
        }
    }

    private Health FindHealth(Transform actor, string label)
    {
        if (actor == null)
        {
            Debug.LogWarning($"[StageController] ไม่พบ{label}ในสนาม — จบแมตช์แล้วจะไม่พาไปหน้ารางวัล");
            return null;
        }

        Health actorHealth = actor.GetComponent<Health>();
        if (actorHealth == null)
        {
            Debug.LogWarning($"[StageController] {actor.name} ไม่มี Health — จบแมตช์แล้วจะไม่พาไปหน้ารางวัล");
        }
        return actorHealth;
    }

    // แยกเป็นเมธอดจริง ไม่ใช้ lambda เพราะต้องถอด listener คืนตอน OnDestroy ให้ได้
    private void HandlePlayerDeath() => EndMatch(false);
    private void HandleEnemyDeath() => EndMatch(true);

    // เลือดหมดพร้อมกันทั้งคู่ในเฟรมเดียวเป็นไปได้ (หมัดแลกหมัด) matchOver กันไม่ให้
    // สั่งโหลดซีนซ้อนกันสองรอบ ผลที่ค้างไว้จึงเป็นของฝ่ายที่ตายก่อนเสมอ
    private void EndMatch(bool playerWon)
    {
        if (matchOver) return;
        matchOver = true;

        Debug.Log($"[StageController] จบแมตช์: ผู้เล่น{(playerWon ? "ชนะ" : "แพ้")}");
        MinigameResult.Set(playerWon);
        StartCoroutine(FinishMatchRoutine(playerWon));
    }

    private IEnumerator FinishMatchRoutine(bool playerWon)
    {
        if (resultDelay > 0f)
        {
            yield return new WaitForSeconds(resultDelay);
        }

        if (endAction == MatchEndAction.AlertAndLeaveWeb)
        {
            AlertAndLeaveWeb(playerWon);
            yield break;
        }

        // เช็คก่อนโหลดแบบเดียวกับ SceneLoadButton — LoadScene ที่หาซีนไม่เจอจะค้างอยู่
        // ในสนามเงียบ ๆ โดยไม่มีอะไรบอกว่าเพราะอะไร
        if (string.IsNullOrWhiteSpace(resultSceneName) ||
            !SceneLoadButton.IsInBuildSettings(resultSceneName))
        {
            Debug.LogError($"[StageController] ไม่มีซีน \"{resultSceneName}\" ใน Build Settings — " +
                           "พาไปหน้ารางวัลไม่ได้ (ถ้า build มาแค่ SampleScene ให้ตั้ง " +
                           "End Action เป็น Alert And Leave Web)");
            yield break;
        }

        SceneManager.LoadScene(resultSceneName);
    }

    // แจ้งผลบนหน้าเว็บแล้วพาออกจากเกม — alert() บล็อกเธรดหลักไว้เอง จึงไม่ต้องรอ callback
    // กลับมาฝั่ง Unity การเปลี่ยน location เกิดขึ้นหลังผู้เล่นกด OK แล้วเสมอ
    private void AlertAndLeaveWeb(bool playerWon)
    {
        string message = playerWon ? winMessage : loseMessage;
        string url = (exitPath ?? "").Trim();

#if UNITY_WEBGL && !UNITY_EDITOR
        TictaAlertAndLeave(message, url);
#else
        // Editor/PC ไม่มี alert ของเบราว์เซอร์ และไม่มีหน้าเว็บให้ออกไป — log ให้เห็นว่า
        // ตอน build เป็น WebGL จริงผู้เล่นจะเจออะไร
        Debug.Log($"[StageController] จบแมตช์ (โหมด Alert And Leave Web): \"{message}\" " +
                  $"-> {(string.IsNullOrEmpty(url) ? "ถอยกลับหน้าเดิมที่พาเข้าเกมมา" : url)}");
#endif
    }

    public void RegisterPlayer(PlayerMovement stagePlayer)
    {
        if (stagePlayer == null) return;
        player = stagePlayer;
        ApplyPlayerSpawnPoint();
    }

    public void RegisterEnemy(EnemyBrain stageEnemy)
    {
        if (stageEnemy == null) return;
        enemy = stageEnemy;
        ApplyEnemySpawnPoint();
    }

    private void ResolveStageActors()
    {
        if (player == null)
        {
            player = FindFirstObjectByType<PlayerMovement>();
        }

        if (enemy == null)
        {
            enemy = FindFirstObjectByType<EnemyBrain>();
        }
    }

    private void ApplySpawnPoints()
    {
        ApplyPlayerSpawnPoint();
        ApplyEnemySpawnPoint();
    }

    private void ApplyPlayerSpawnPoint()
    {
        if (player == null || playerSpawnPoint == null) return;

        player.transform.SetPositionAndRotation(playerSpawnPoint.position, playerSpawnPoint.rotation);
    }

    private void ApplyEnemySpawnPoint()
    {
        if (enemy == null || enemySpawnPoint == null) return;

        enemy.transform.SetPositionAndRotation(enemySpawnPoint.position, enemySpawnPoint.rotation);
    }
}
