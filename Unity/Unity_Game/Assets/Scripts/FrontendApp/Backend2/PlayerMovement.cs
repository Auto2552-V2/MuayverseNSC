using UnityEngine;
using UnityEngine.InputSystem; // นำเข้า New Input System
using System.Collections; // สำหรับใช้งาน Coroutine

public enum PlayerState
{
    Idle,
    Lunge,
    Punch,
    Return,
    Dodge,
    Stun
}

// โครงสร้างข้อมูลสำหรับท่าโจมตีหรือการกระทำแต่ละท่า
[System.Serializable]
public struct PlayerAttack
{
    public string attackName; // ชื่อท่าอ้างอิง เช่น Punch, Kick, Heavy Punch

    [Header("Input Bindings")]
    public Key keyboardKey; // ปุ่มกดบนคีย์บอร์ด (เช่น Digit1, Digit2, Space)
    public bool useMouseLeftClick; // ถ้าตั้งเป็น true จะกดใช้ด้วยการคลิกซ้าย แทนที่จะเป็นปุ่มคีย์บอร์ด

    [Header("Animation Settings")]
    public string triggerName; // ชื่อ Trigger ใน Animator
    public string animStateName; // ชื่อ State ของท่านี้ใน Animator Controller

    [Header("Combat Settings")]
    public Cooldown cooldown; // คลาสสำหรับเก็บระบบคูลดาวน์แยกกันของแต่ละท่า
    public float damage; // พลังโจมตี
    public float damageDelay; // ดีเลย์ก่อนพลังชีวิตศัตรูลดลง (ให้ตรงกับจังหวะหมัด/เท้าโดนตัว)

    [Header("Punch Effect Settings")]
    public GameObject punchEffectPrefab;
    public Transform punchEffectPoint;
    public Vector3 punchEffectLocalOffset;
    public Vector3 punchEffectRotationEuler;
    [Min(0.000001f)] public float punchEffectScale;
    [Min(0.01f)] public float punchEffectSpeed;
    [Min(0.01f)] public float punchEffectDestroyDelay;
    public bool parentPunchEffectToPoint;
    public bool activatePunchEffectChildren;

    [Header("Punch Sound Settings")]
    [Tooltip("เสียงเฉพาะของท่านี้ — เว้นว่างไว้จะใช้เสียงกลางจาก PunchSoundPlayer บน Main Camera")]
    public AudioClip punchSound;

    [Header("Dodge Settings")]
    public bool isDodge; // ระบุว่าเป็นท่าหลบหรือไม่ (จะได้รับ I-Frame อมตะระหว่างทำท่านี้)
    public bool dodgeBackward; // หาก true จะหลบไปข้างหลัง, false = forward (เพิ่ม forward lunge)
    public bool dodgeLeft; // จะหลบไปด้านซ้าย
    public bool dodgeRight; // จะหลบไปด้านขวา
    public float dodgeDistance; // ระยะการเคลื่อนที่ไปข้างหน้า/หลัง (หน่วย Unity)
    public float dodgeLateralDistance; // ระยะการเคลื่อนที่ด้านข้าง (หน่วย Unity)
    public float dodgeForwardLunge; // ระยะการพุ่งไปข้างหน้าเพิ่มเติม (0 = ไม่มี)
    public float dodgeDuration; // เวลาที่ใช้สไลด์หลบ (วินาที)
}

public class PlayerMovement : MonoBehaviour
{
    private Animator animator;
    
    [Header("State Settings")]
    [SerializeField] private PlayerState currentState = PlayerState.Idle;

    [Header("Stun Settings")]
    [SerializeField][Min(0f)] private float stunDuration = 1.0f;
    [SerializeField] private string stunTriggerName = "Stun";
    
    // ตัวแปรล็อกพิกัดเริ่มต้น เพื่อแก้ไขปัญหาตัวละครตำแหน่งไม่นิ่ง หรือเลื่อนไหล (Drift) เองจากอนิเมชั่น
    // จุดยืนดั้งเดิมที่จับตอน Start (ค่าคงที่ ไม่เปลี่ยน)
    private Vector3 spawnAnchor;
    private Quaternion spawnRotationAnchor;

    /// <summary>ระยะเลื่อนจากภายนอก (PoseBodyDriver) ให้ตัวละครขยับตามตัวผู้เล่นจริง — world space</summary>
    public Vector3 ExternalBodyOffset { get; set; }

    /// <summary>องศาเอียงลำตัวจากภายนอก (PoseBodyDriver)</summary>
    public float ExternalBodyTilt { get; set; }

    [Header("ล็อกจุดยืน")]
    [Tooltip("ตรึงตัวละครไว้ที่จุดยืนตั้งต้นแบบตายตัว — ตัดอิทธิพลของ PoseBodyDriver ที่เลื่อนตัวละคร " +
             "ซ้าย-ขวาตามตัวคนเล่นหน้ากล้องออกไปด้วย\n" +
             "ติ๊กออกเมื่อไหร่ ตัวละครจะกลับมาขยับตามคนเล่นเหมือนเดิม")]
    [SerializeField] private bool lockToSpawnPoint = true;

    [Header("ล็อคหันหน้าเข้าหาศัตรู")]
    [Tooltip("หันหน้าเข้าหาศัตรูทุกเฟรมในทุกสเตท (lock-on) — ไม่ใช่แค่ตอน Idle\n" +
             "ติ๊กออกเมื่อไหร่ ตัวละครจะกลับไปหันตามองศาตั้งต้นที่จับไว้ตอนเริ่มเกมเหมือนเดิม")]
    [SerializeField] private bool faceEnemy = true;

    [Tooltip("ความเร็วหมุน (องศา/วินาที) — 0 = หันทันทีแบบ snap\n" +
             "ค่าเดียวกับ turnSpeed ของ EnemyBrain คือ 360")]
    [SerializeField][Min(0f)] private float turnSpeed = 720f;

    // จุดอ้างอิงที่ "ขยับได้" — โค้ดเดิมทุกจุดอ่านตัวนี้เหมือนเดิม ทั้ง lunge/dodge/return
    // จึงตามตัวผู้เล่นไปเองโดยไม่ต้องแก้ coroutine ใดๆ
    //
    // ติ๊ก lockToSpawnPoint = ไม่บวก offset จากกล้องเลย จุดยืนจึงนิ่งสนิทตลอดเกม
    private Vector3 spawnPosition =>
        lockToSpawnPoint ? spawnAnchor : spawnAnchor + ExternalBodyOffset;

    // องศาที่ตัวละคร "ควรหัน" ณ เฟรมนี้ (ปลายทาง ไม่ใช่ค่าที่ใส่ลง transform ตรงๆ —
    // TurnTowards เป็นคนค่อยๆ หมุนเข้าหาค่านี้ตาม turnSpeed)
    //
    // เปิด faceEnemy = ฐานมาจากทิศของศัตรู, ปิด = ฐานคือองศาตั้งต้นตอน Start เหมือนเดิม
    private Quaternion spawnRotation
    {
        get
        {
            Quaternion baseRotation = TryGetEnemyFacing(out Quaternion facing)
                ? facing
                : spawnRotationAnchor;

            return lockToSpawnPoint
                ? baseRotation
                : baseRotation * Quaternion.AngleAxis(-ExternalBodyTilt, Vector3.forward);
        }
    }

    // ตัวแปรเก็บ Collider ของตัวผู้เล่นเอง
    private Collider playerCollider;
    private CharacterController playerCC;

    [Header("Lunge & Return Settings")]
    [SerializeField] private float stoppingOffset = 0.3f; // ระยะห่างจากขอบ Collider ของศัตรูที่จะไปหยุดยืน
    [SerializeField] private float lungeDuration = 0.15f; // เวลาพุ่งขาไป (วินาที)
    [SerializeField] private float returnDuration = 0.15f; // เวลาพุ่งกลับมาที่เดิม (วินาที)
    [SerializeField] private float dodgeReturnDuration = 0.4f; // เวลาเดินทางกลับหลังทำท่าหลบ (ปรับแยกต่างหากจาก returnDuration ปกติ)

    [Header("Attacks Settings")]
    [SerializeField] private PlayerAttack[] attacks; // รายการท่าโจมตีทั้งหมด (เพิ่มลดจำนวนท่าได้อิสระจาก Inspector)

    [Header("Camera Feedback")]
    [SerializeField] private CameraShakeProfile damageShakeProfile = new CameraShakeProfile();
    [SerializeField] private bool logDamageShakeEvents = true;
    [SerializeField] private CameraFovProfile dodgeFovProfile = new CameraFovProfile(); // FOV zoom-out ระหว่าง Dodge
    [SerializeField] private CameraFovProfile punchFovProfile = new CameraFovProfile();  // FOV ตอนออกท่าโจมตี

    private Health health;
    private IFrameController iframeController;
    private StageController stage;
    private Coroutine stunCoroutine;
    private Transform cachedEnemy;

    // Property เพื่อให้ภายนอกเช็คสถานะของผู้เล่นได้
    public PlayerState CurrentState => currentState;

    void OnValidate()
    {
        if (attacks == null) return;

        for (int i = 0; i < attacks.Length; i++)
        {
            PlayerAttack attack = attacks[i];
            attack.punchEffectScale = Mathf.Max(0.000001f, attack.punchEffectScale);
            attack.punchEffectSpeed = attack.punchEffectSpeed > 0f ? attack.punchEffectSpeed : 1f;
            attack.punchEffectDestroyDelay = attack.punchEffectDestroyDelay > 0f ? attack.punchEffectDestroyDelay : 1.25f;
            attacks[i] = attack;
        }
    }

    void Awake()
    {
        health = GetComponent<Health>();
        iframeController = FindFirstObjectByType<IFrameController>();
        stage = StageController.GetOrCreateActiveStage();
        stage.RegisterPlayer(this);
    }

    void OnEnable()
    {
        if (health != null)
        {
            health.OnTakeDamage.AddListener(HandleTakeDamage);
        }
    }

    void OnDisable()
    {
        if (health != null)
        {
            health.OnTakeDamage.RemoveListener(HandleTakeDamage);
        }
    }

    void Start()
    {
        animator = GetComponent<Animator>();

        if (animator == null)
        {
            Debug.LogError("ไม่พบ Animator Component!");
        }
        else
        {
            // ปิด root motion ให้ชัดเจนตั้งแต่ต้น — คลิปชุดนี้ (Idel/Punch/Dodging) เป็น Humanoid
            // ที่ไม่ได้ bake ตำแหน่ง XZ เข้า pose (m_LoopBlendPositionXZ: 0) Animator จึงเอา
            // ระยะเดินในคลิปมาลากตัวละครออกจากจุดยืนเองทุกเฟรม = อาการ "เดินเอง"
            animator.applyRootMotion = false;
        }

        // บันทึกตำแหน่งและองศาการหันตั้งต้นตอนเริ่มเกมไว้ เพื่อใช้ล็อคพิกัดให้มั่นคง 100%
        spawnAnchor = transform.position;
        spawnRotationAnchor = transform.rotation;

        // ดึงคอมโพเนนต์ Collider หรือ CharacterController ของผู้เล่นเองมาเก็บไว้
        playerCollider = GetComponent<Collider>();
        playerCC = GetComponent<CharacterController>();

        // เริ่มต้นด้วย Idle
        TransitionToState(PlayerState.Idle);
    }

    void Update()
    {
        if (animator == null) return;

        // แยกโค้ดทำงานและเงื่อนไขตาม State ปัจจุบัน
        switch (currentState)
        {
            case PlayerState.Idle:
                UpdateIdleState();
                break;
            case PlayerState.Lunge:
            case PlayerState.Punch:
            case PlayerState.Return:
            case PlayerState.Dodge:
                // ในสเตทเคลื่อนไหวเหล่านี้ จะมี Coroutine รับหน้าที่จัดการอยู่ ไม่ต้องทำอะไรใน Update
                break;
            case PlayerState.Stun:
                UpdateStunState();
                break;
        }
    }

    // ล็อกพิกัดซ้ำอีกรอบหลัง Animator เขียน Transform เสร็จ (เหตุผลเดียวกับ CharacterPositionLock)
    // ลำดับของ Unity คือ Update -> Animator -> LateUpdate ค่าที่เซ็ตไว้ใน UpdateIdleState จึงถูก
    // ทับได้เสมอถ้ามีอะไรมาขยับ Transform ทีหลัง ตัวนี้เป็นด่านสุดท้ายที่การันตีว่ายืนนิ่งจริง
    //
    // แตะเฉพาะสเตทที่ต้องยืนอยู่กับที่ — Lunge/Punch/Return/Dodge มี coroutine คุมตำแหน่งอยู่
    // ถ้าล็อกด้วยจะพุ่ง/หลบไม่ออกเลย
    void LateUpdate()
    {
        // หันหน้าเข้าหาศัตรูก่อน และทำ "ทุกสเตท" รวมทั้ง Lunge/Punch/Return/Dodge ที่ coroutine
        // คุมตำแหน่งอยู่ — ตัวนี้แตะแค่ rotation ไม่แตะ position จึงไม่ไปขวางการพุ่ง/หลบ
        // และเป็นด่านสุดท้ายที่ทับค่าที่ coroutine เซ็ตไว้ จึงการันตีว่าหันเข้าหาศัตรูจริงตลอด
        if (faceEnemy)
        {
            TurnTowards(spawnRotation);
        }

        if (currentState != PlayerState.Idle && currentState != PlayerState.Stun) return;

        transform.position = spawnPosition;

        // ปิด faceEnemy = กลับไปตรึงองศาตั้งต้นแบบเดิมเป๊ะๆ
        if (!faceEnemy)
        {
            transform.rotation = spawnRotation;
        }
    }

    /// <summary>
    /// องศาหันหน้าเข้าหาศัตรู — แบนแกน Y ไว้ ไม่ให้ตัวละครก้ม/เงยตามความสูงของศัตรู
    /// คืน false เมื่อปิด faceEnemy, หาศัตรูไม่เจอ, หรือยืนทับกันพอดี (ไม่มีทิศให้หัน)
    /// </summary>
    private bool TryGetEnemyFacing(out Quaternion rotation)
    {
        rotation = spawnRotationAnchor;
        if (!faceEnemy) return false;

        Transform enemy = FindEnemy();
        if (enemy == null) return false;

        Vector3 direction = enemy.position - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) return false;

        rotation = Quaternion.LookRotation(direction.normalized);
        return true;
    }

    /// <summary>ค่อยๆ หมุนเข้าหา target ตาม turnSpeed — turnSpeed = 0 คือ snap ทันที</summary>
    private void TurnTowards(Quaternion target)
    {
        transform.rotation = turnSpeed > 0f
            ? Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Time.deltaTime)
            : target;
    }

    /// <summary>
    /// ตรึงตัวละครไว้ที่จุดยืนตั้งต้น — เว้น rotation ไว้ให้ LateUpdate จัดการเมื่อเปิด faceEnemy
    /// ไม่งั้นการ snap องศารายเฟรมตรงนี้จะลบล้างการหมุนแบบ smooth ตาม turnSpeed ทิ้งทั้งหมด
    /// </summary>
    private void HoldSpawnPose()
    {
        transform.position = spawnPosition;

        if (!faceEnemy)
        {
            transform.rotation = spawnRotation;
        }
    }

    // ฟังก์ชันหลักในการสลับสเตทของผู้เล่น
    public void TransitionToState(PlayerState newState)
    {
        ExitState(currentState);
        currentState = newState;
        EnterState(currentState);
    }

    private void EnterState(PlayerState state)
    {
        Debug.Log($"[PlayerMovement] เข้าสู่สถานะ: {state}");
        switch (state)
        {
            case PlayerState.Idle:
                // ล็อคพิกัดตำแหน่งตัวผู้เล่นให้อยู่จุดเริ่มต้นเสมอ ป้องกันการขยับสไลด์หลุดจากแอนิเมชัน
                HoldSpawnPose();
                break;
            case PlayerState.Lunge:
                break;
            case PlayerState.Punch:
                break;
            case PlayerState.Return:
                break;
            case PlayerState.Dodge:
                break;
            case PlayerState.Stun:
                HoldSpawnPose();
                ResetAttackTriggers();
                if (animator != null && !string.IsNullOrEmpty(stunTriggerName))
                {
                    animator.SetTrigger(stunTriggerName);
                }
                stunCoroutine = StartCoroutine(StunTimerCoroutine());
                break;
        }
    }

    private void ExitState(PlayerState state)
    {
        switch (state)
        {
            case PlayerState.Idle:
                break;
            case PlayerState.Lunge:
                break;
            case PlayerState.Punch:
                break;
            case PlayerState.Return:
                break;
            case PlayerState.Dodge:
                break;
            case PlayerState.Stun:
                if (stunCoroutine != null)
                {
                    StopCoroutine(stunCoroutine);
                    stunCoroutine = null;
                }
                if (animator != null && !string.IsNullOrEmpty(stunTriggerName))
                {
                    animator.ResetTrigger(stunTriggerName);
                }
                break;
        }
    }

    // ทำงานเฉพาะเมื่ออยู่ในสถานะ Idle เท่านั้น
    private void UpdateIdleState()
    {
        // ยืนล็อคตำแหน่งอยู่กับที่
        HoldSpawnPose();

        if (attacks == null || attacks.Length == 0) return;

        // วนลูปเช็คอินพุตของทุกท่าในอาร์เรย์
        for (int i = 0; i < attacks.Length; i++)
        {
            if (CheckAttackInput(attacks[i]))
            {
                ExecuteAttack(attacks[i]);
                break; // กดออกท่าได้ทีละ 1 ท่าใน 1 เฟรม
            }
        }
    }

    private void UpdateStunState()
    {
        HoldSpawnPose();
    }

    // ฟังก์ชันช่วยดักจับว่ามีการกดอินพุตของท่าโจมตีนั้นๆ หรือไม่
    /// <summary>
    /// สั่งออกท่าจากภายนอก (PoseUdpReceiver ที่รับท่าจากกล้อง) โดยอ้างอิงจาก attackName
    /// เงื่อนไขเหมือนกดปุ่มทุกอย่าง คือต้องอยู่ Idle และ cooldown พร้อม — คืน true เมื่อออกท่าได้จริง
    /// </summary>
    public bool TryExecuteAttackByName(string attackName)
    {
        if (string.IsNullOrEmpty(attackName) || attacks == null || attacks.Length == 0)
        {
            return false;
        }

        if (currentState != PlayerState.Idle)
        {
            return false;
        }

        for (int i = 0; i < attacks.Length; i++)
        {
            if (!string.Equals(attacks[i].attackName, attackName, System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!attacks[i].cooldown.IsReady)
            {
                return false;
            }

            ExecuteAttack(attacks[i]);
            return true;
        }

        Debug.LogWarning($"[PlayerMovement] ไม่พบท่าชื่อ '{attackName}' ใน attacks[]");
        return false;
    }

    private bool CheckAttackInput(PlayerAttack attack)
    {
        if (attack.useMouseLeftClick)
        {
            return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        }
        else
        {
            return Keyboard.current != null && Keyboard.current[attack.keyboardKey].wasPressedThisFrame;
        }
    }

    // ฟังก์ชันจัดการออกท่าโจมตี
    private void ExecuteAttack(PlayerAttack attack)
    {
        if (attack.cooldown.IsReady)
        {
            Transform targetEnemy = FindEnemy();

            if (attack.isDodge)
            {
                Debug.Log($"[Debug] ออกท่าหลบ: {attack.attackName}");
                StartCoroutine(DodgeCoroutine(targetEnemy, attack));
            }
            else
            {
                // ค้นหาศัตรูตัวเดียวในฉาก
                if (targetEnemy != null)
                {
                    float dist = Vector3.Distance(spawnPosition, targetEnemy.position);
                    Debug.Log($"[Debug] พบศัตรู: {targetEnemy.name} | ระยะห่างจริง: {dist:F2} | ออกท่า: {attack.attackName}");
                    
                    // เริ่มคอรันทีนการพุ่งโจมตีโดยส่งโครงสร้างข้อมูลท่านี้เข้าไปประมวลผล
                    StartCoroutine(LungeAndReturnCoroutine(targetEnemy, attack));
                }
                else
                {
                    // หากไม่พบศัตรู ให้ปล่อยท่าโจมตีอยู่กับที่ตามปกติ
                    TransitionToState(PlayerState.Punch);
                    animator.SetTrigger(attack.triggerName);
                    attack.cooldown.StartCooldown();
                    
                    StartCoroutine(StationaryPunchCoroutine(attack));
                    Debug.LogError($"[Debug] ไม่พบศัตรูในฉาก! (ปล่อยท่า {attack.attackName} อยู่กับที่)");
                }
            }
        }
        else
        {
            Debug.Log($"ท่า {attack.attackName} ติดคูลดาวน์เหลืออีก: {attack.cooldown.TimeRemaining:F2} วินาที");
        }
    }

    /// <summary>
    /// พุ่งไปหาศัตรู -> เล่นอนิเมชั่นท่าโจมตี -> สไลด์ถอยหลังกลับมาที่เดิม
    /// </summary>
    private IEnumerator LungeAndReturnCoroutine(Transform target, PlayerAttack attack)
    {
        // 1. เริ่มพุ่ง (Lunge)
        TransitionToState(PlayerState.Lunge);

        // หาจุดขอบ Collider ของศัตรูที่ใกล้ผู้เล่นที่สุด
        Collider enemyCollider = target.GetComponent<Collider>();
        Vector3 direction = (target.position - spawnPosition).normalized;
        Vector3 stoppingPosition;

        // คำนวณขนาดความกว้าง (รัศมี) ของตัวผู้เล่นเอง
        float playerSizeOffset = 0f;
        if (playerCollider != null)
        {
            if (playerCollider is CapsuleCollider capsule)
            {
                playerSizeOffset = capsule.radius * Mathf.Max(transform.localScale.x, transform.localScale.z);
            }
            else if (playerCollider is SphereCollider sphere)
            {
                playerSizeOffset = sphere.radius * Mathf.Max(transform.localScale.x, transform.localScale.y, transform.localScale.z);
            }
            else if (playerCollider is BoxCollider box)
            {
                // หาระยะขอบกล่องตามทิศทางที่พุ่งไป
                Vector3 localDir = transform.InverseTransformDirection(direction);
                Vector3 halfSize = box.size * 0.5f;
                playerSizeOffset = Mathf.Abs(localDir.x * halfSize.x * transform.localScale.x) + 
                                   Mathf.Abs(localDir.y * halfSize.y * transform.localScale.y) + 
                                   Mathf.Abs(localDir.z * halfSize.z * transform.localScale.z);
            }
            else
            {
                // สำหรับ Collider ทั่วไป
                Vector3 testPoint = transform.position + direction * 10f;
                Vector3 closestPointOnSelf = playerCollider.ClosestPoint(testPoint);
                playerSizeOffset = Vector3.Distance(transform.position, closestPointOnSelf);
            }
        }
        else if (playerCC != null)
        {
            playerSizeOffset = playerCC.radius * Mathf.Max(transform.localScale.x, transform.localScale.z);
        }

        // รวมระยะหยุดห่างจากขอบ = รัศมีผู้เล่น + ระยะ Offset ที่ตั้งไว้
        float totalBuffer = playerSizeOffset + stoppingOffset;

        if (enemyCollider != null)
        {
            // ClosestPoint จะคืนจุดบนผิว Collider ที่ใกล้ผู้เล่นที่สุด
            Vector3 closestPoint = enemyCollider.ClosestPoint(spawnPosition);
            closestPoint.y = spawnPosition.y; // ล็อคแกน Y

            // หยุดห่างจากขอบ Collider ของศัตรู เท่ากับขนาดตัวผู้เล่น + offset
            stoppingPosition = closestPoint - (direction * totalBuffer);

            float distToStop = Vector3.Distance(spawnPosition, stoppingPosition);
            Debug.Log($"[Debug] ขอบศัตรู: {closestPoint} | รัศมีผู้เล่น: {playerSizeOffset:F2} | หยุดที่: {stoppingPosition} | ระยะพุ่ง: {distToStop:F2}");

            // ถ้าจุดหยุดอยู่หลังผู้เล่น (ใกล้เกินไปแล้ว) ให้ยืนอยู่กับที่
            if (Vector3.Dot(stoppingPosition - spawnPosition, direction) <= 0)
            {
                stoppingPosition = spawnPosition;
                Debug.Log("[Debug] ผู้เล่นอยู่ใกล้ศัตรูมากพอแล้ว -> ชกอยู่กับที่");
            }
        }
        else
        {
            // ถ้าไม่มี Collider ศัตรู ให้พุ่งไปหาตำแหน่งศัตรูแล้วหยุดห่างตาม totalBuffer
            Vector3 targetPos = target.position;
            targetPos.y = spawnPosition.y;
            stoppingPosition = targetPos - (direction * totalBuffer);
            Debug.LogWarning("[Debug] ศัตรูไม่มี Collider! ใช้ตำแหน่ง transform แทน");
        }
        stoppingPosition.y = spawnPosition.y; // ล็อคระดับความสูงแกน Y

        // หมุนตัวและพุ่งไปหาศัตรู (Lunge Move)
        // แบนแกน Y ก่อนหมุน ไม่ให้ตัวละครก้ม/เงยตามความสูงของศัตรู — direction ตัวเดิมยังไม่แบน
        // เพราะยังต้องใช้คำนวณจุดหยุดอยู่ข้างบน
        Vector3 facingDirection = direction;
        facingDirection.y = 0f;
        if (facingDirection.sqrMagnitude > 0.0001f && stoppingPosition != spawnPosition)
        {
            transform.rotation = Quaternion.LookRotation(facingDirection.normalized);
        }

        float elapsed = 0f;
        while (elapsed < lungeDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / lungeDuration;
            Vector3 currentPos = Vector3.Lerp(spawnPosition, stoppingPosition, t);
            currentPos.y = spawnPosition.y; // ป้องกันบิดเบี้ยวแกน Y
            transform.position = currentPos;
            yield return null;
        }
        transform.position = stoppingPosition;

        // รอ 1 เฟรมเพื่อให้ Unity อัพเดตตำแหน่งบนหน้าจอก่อนจะเริ่มทำท่า
        yield return null;

        // 2. ทรานซิชันเข้าสู่สเตทโจมตี (Punch)
        TransitionToState(PlayerState.Punch);
        CameraFovEffect.PlayGlobal(punchFovProfile); // เล่น FOV effect ตอนออกหมัด
        animator.SetTrigger(attack.triggerName);
        attack.cooldown.StartCooldown();

        // รอ 1 เฟรมให้ trigger ถูก consume แล้วรีเซ็ตทิ้ง ป้องกัน trigger ค้างทำให้โจมตีซ้ำ
        yield return null;
        animator.ResetTrigger(attack.triggerName);

        // รอให้ Animator เข้าสู่ state การทำงานของท่านั้นๆ ก่อน
        while (!animator.GetCurrentAnimatorStateInfo(0).IsName(attack.animStateName))
        {
            yield return null;
        }

        // รอตามเวลาดีเลย์เพื่อให้มือ/เท้าเอื้อมไปถึงตัวศัตรู แล้วค่อยทำความเสียหาย
        yield return new WaitForSeconds(attack.damageDelay);
        PlayPunchEffect(attack);

        Health enemyHealth = target.GetComponent<Health>();
        if (enemyHealth != null)
        {
            enemyHealth.TakeDamage(attack.damage);
            PlayDamageCameraShake(attack.damage, $"hit {target.name} with {attack.attackName}");
        }
        else
        {
            Debug.LogWarning($"[Debug] ไม่พบสคริปต์ Health บน {target.name} จึงสร้างความเสียหายไม่ได้!");
        }

        // รอเวลาที่เหลือของอนิเมชั่นสกิลจนกว่าจะเล่นจบแล้วเปลี่ยนไปสเตท Return
        while (animator.GetCurrentAnimatorStateInfo(0).IsName(attack.animStateName))
        {
            yield return null;
        }

        // 3. ทรานซิชันเข้าสู่สเตทเดินทางกลับ (Return)
        TransitionToState(PlayerState.Return);
        elapsed = 0f;
        Vector3 reachedPosition = transform.position;
        reachedPosition.y = spawnPosition.y;

        while (elapsed < returnDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / returnDuration;
            Vector3 currentPos = Vector3.Lerp(reachedPosition, spawnPosition, t);
            currentPos.y = spawnPosition.y; // ล็อคแกน Y
            transform.position = currentPos;
            yield return null;
        }

        // 4. สิ้นสุดการทำงาน กลับสู่สเตท Idle
        TransitionToState(PlayerState.Idle);
    }

    /// <summary>
    /// คอรันทีนควบคุมท่าหลบ: สไลด์ถอยหลังพร้อมเปิด I-Frame อมตะ -> เล่นอนิเมชันหลบ -> สไลด์กลับมาที่เดิม
    /// </summary>
    private IEnumerator DodgeCoroutine(Transform target, PlayerAttack attack)
    {
        // 1. เข้าสู่สถานะ Dodge
        TransitionToState(PlayerState.Dodge);

        // เปิดใช้งาน I-Frame (อมตะ)
        if (iframeController != null)
        {
            iframeController.SetInvincible(true);
        }
        else if (health != null)
        {
            health.IsInvincible = true;
        }

        // เล่น FOV zoom-out ตอนเริ่ม Dodge
        CameraFovEffect.PlayGlobal(dodgeFovProfile);

        animator.SetTrigger(attack.triggerName);
        attack.cooldown.StartCooldown();

        // รอ 1 เฟรมให้ trigger ถูก consume แล้วรีเซ็ตทิ้ง ป้องกัน trigger ค้าง
        yield return null;
        animator.ResetTrigger(attack.triggerName);

        // คำนวณทิศทางหันหน้าเข้าหาศัตรู
        Vector3 direction = transform.forward;
        if (target != null)
        {
            direction = (target.position - spawnPosition).normalized;
        }
        direction.y = 0; // ล็อคแกน Y

        if (direction != Vector3.zero)
        {
            transform.rotation = Quaternion.LookRotation(direction);
        }

        // Compute dodge target based on configured directions
        Vector3 dodgeTargetPosition = spawnPosition;
        Vector3 offset = Vector3.zero;

        // Forward/backward distance
        if (attack.dodgeDistance > 0f)
        {
            // Use backward if dodgeBackward is true, otherwise forward
            if (attack.dodgeBackward)
                offset -= direction * attack.dodgeDistance;
            else
                offset += direction * attack.dodgeDistance;
        }

        // Additional forward lunge
        if (attack.dodgeForwardLunge > 0f)
        {
            offset += direction * attack.dodgeForwardLunge;
        }

        // Lateral movement
        if (attack.dodgeLateralDistance > 0f)
        {
            if (attack.dodgeLeft)
                offset -= transform.right * attack.dodgeLateralDistance;
            else if (attack.dodgeRight)
                offset += transform.right * attack.dodgeLateralDistance;
        }

        // Apply offset
        dodgeTargetPosition += offset;
        dodgeTargetPosition.y = spawnPosition.y;

        // สไลด์หลบไปยังพิกัด dodgeTargetPosition (ใช้ Lerp ให้เคลื่อนที่ smooth แทนการวาปทันที)
        float elapsed = 0f;
        Vector3 startPos = transform.position;
        if (attack.dodgeDuration > 0f)
        {
            while (elapsed < attack.dodgeDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / attack.dodgeDuration;
                Vector3 currentPos = Vector3.Lerp(startPos, dodgeTargetPosition, t);
                currentPos.y = spawnPosition.y;
                transform.position = currentPos;
                yield return null;
            }
            // snap ให้ตรงพิกัดเป๊ะหลัง loop จบ
            transform.position = dodgeTargetPosition;
        }
        else
        {
            // ไม่มี duration → วาปทันที (fallback)
            transform.position = dodgeTargetPosition;
        }

        // รอจนกว่า Animator จะเข้าสู่ state อนิเมชันหลบ
        while (!animator.GetCurrentAnimatorStateInfo(0).IsName(attack.animStateName))
        {
            yield return null;
        }

        // รอจนกว่าจะอนิเมชันหลบจะเล่นเสร็จสิ้น (ออกจาก state)
        while (animator.GetCurrentAnimatorStateInfo(0).IsName(attack.animStateName))
        {
            yield return null;
        }

        // 3. ทรานซิชันเข้าสู่สเตทเดินทางกลับ (Return)
        TransitionToState(PlayerState.Return);
        elapsed = 0f;
        Vector3 reachedPosition = transform.position;
        reachedPosition.y = spawnPosition.y;

        // ใช้ dodgeReturnDuration แยกจาก returnDuration ปกติ เพื่อปรับความเร็วกลับหลัง dodge ได้อิสระ
        float activeReturnDuration = dodgeReturnDuration > 0f ? dodgeReturnDuration : returnDuration;
        while (elapsed < activeReturnDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / activeReturnDuration;
            Vector3 currentPos = Vector3.Lerp(reachedPosition, spawnPosition, t);
            currentPos.y = spawnPosition.y; // ล็อคแกน Y
            transform.position = currentPos;
            yield return null;
        }
        transform.position = spawnPosition;

        // ปิดการใช้งาน I-Frame (อมตะ) เมื่อทำท่าหลบและกลับมาเสร็จสมบูรณ์
        if (iframeController != null)
        {
            iframeController.SetInvincible(false);
        }
        else if (health != null)
        {
            health.IsInvincible = false;
        }

        // 4. สิ้นสุดการทำงาน กลับสู่สเตท Idle
        TransitionToState(PlayerState.Idle);
    }

    /// <summary>
    /// คอรันทีนจัดการสเตทโจมตีกรณีไม่มีศัตรู (ออกท่าอยู่กับที่) จนอนิเมชั่นเสร็จแล้วจึงกลับสู่ Idle
    /// </summary>
    private IEnumerator StationaryPunchCoroutine(PlayerAttack attack)
    {
        yield return null;
        animator.ResetTrigger(attack.triggerName);

        // รอเข้าสู่ state การทำดาเมจของท่านั้นๆ
        while (!animator.GetCurrentAnimatorStateInfo(0).IsName(attack.animStateName))
        {
            yield return null;
        }

        if (attack.damageDelay > 0f)
        {
            yield return new WaitForSeconds(attack.damageDelay);
        }

        PlayPunchEffect(attack);

        // รอจนเล่นจบแอนิเมชัน
        while (animator.GetCurrentAnimatorStateInfo(0).IsName(attack.animStateName))
        {
            yield return null;
        }

        // พลิกกลับมาเป็น Idle
        TransitionToState(PlayerState.Idle);
    }

    private void PlayPunchEffect(PlayerAttack attack)
    {
        // เสียงแยกจาก effect โดยตั้งใจ — PunchEffectPlayer.Play จะ return ทันทีถ้าไม่ได้ใส่
        // prefab เอาไว้ ถ้าเอาเสียงไปฝากไว้ในนั้นจะกลายเป็นว่าต้องมี effect ก่อนจึงจะมีเสียง
        PunchSoundPlayer.PlayGlobal(attack.punchSound, $"PlayerMovement:{attack.attackName}");

        PunchEffectPlayer.Play(
            attack.punchEffectPrefab,
            attack.punchEffectPoint,
            transform,
            attack.punchEffectLocalOffset,
            attack.punchEffectRotationEuler,
            attack.punchEffectScale,
            attack.punchEffectSpeed,
            attack.punchEffectDestroyDelay > 0f ? attack.punchEffectDestroyDelay : 1.25f,
            attack.parentPunchEffectToPoint,
            attack.activatePunchEffectChildren,
            $"PlayerMovement:{attack.attackName}");
    }

    private void HandleTakeDamage(float damageAmount)
    {
        PlayDamageCameraShake(damageAmount, "player took damage");

        if (health != null && health.CurrentHealth <= 0f)
        {
            return;
        }

        StopAllCoroutines();
        stunCoroutine = null;
        TransitionToState(PlayerState.Stun);
    }

    private IEnumerator StunTimerCoroutine()
    {
        if (stunDuration > 0f)
        {
            yield return new WaitForSeconds(stunDuration);
        }
        else
        {
            yield return null;
        }

        stunCoroutine = null;
        if (health == null || !health.IsDead)
        {
            TransitionToState(PlayerState.Idle);
        }
    }

    private void ResetAttackTriggers()
    {
        if (animator == null || attacks == null) return;

        for (int i = 0; i < attacks.Length; i++)
        {
            string triggerName = attacks[i].triggerName;
            if (!string.IsNullOrEmpty(triggerName))
            {
                animator.ResetTrigger(triggerName);
            }
        }
    }

    private void PlayDamageCameraShake(float damageAmount, string context)
    {
        if (damageShakeProfile == null || !damageShakeProfile.IsValid)
        {
            Debug.LogWarning($"[PlayerMovement] Cannot play camera shake after {context}; damageShakeProfile is invalid.");
            return;
        }

        if (CameraShake.Instance == null)
        {
            Debug.LogWarning($"[PlayerMovement] Cannot play camera shake after {context}; no CameraShake instance was found.");
            return;
        }

        CameraShake.Instance.Shake(damageShakeProfile);

        if (logDamageShakeEvents)
        {
            Debug.Log($"[PlayerMovement] Camera shake after {context}: damage={damageAmount}, duration={damageShakeProfile.Duration:F2}, magnitude={damageShakeProfile.Magnitude:F2}, rotation={damageShakeProfile.RotationMagnitude:F2}");
        }
    }

    /// <summary>
    /// ค้นหาศัตรูที่มี Tag ว่า "Enemy" ในฉาก
    /// </summary>
    private Transform FindEnemy()
    {
        if (stage != null && stage.EnemyTransform != null)
        {
            return stage.EnemyTransform;
        }

        // แคชผลของ FindWithTag เพราะ lock-on เรียกเมธอดนี้ทุกเฟรมจาก LateUpdate
        // Unity ให้ == null เป็นจริงเมื่ออ็อบเจกต์ถูกทำลาย แคชจึงหลุดเองเมื่อศัตรูหายไปจากฉาก
        if (cachedEnemy != null)
        {
            return cachedEnemy;
        }

        GameObject enemy = GameObject.FindWithTag("Enemy");
        cachedEnemy = enemy != null ? enemy.transform : null;
        return cachedEnemy;
    }
}
