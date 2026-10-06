using UnityEngine;
using UnityEngine.Serialization;
using System.Collections;

// กำหนด State ของ AI
public enum EnemyState
{
    Idle,
    Charge,
    Punch,
    Stun
}

public class EnemyBrain : MonoBehaviour
{
    [Header("State Settings")]
    [SerializeField] private EnemyState currentState = EnemyState.Idle;

    [Header("Stun Settings")]
    [SerializeField] private float stunDuration = 1.0f; // ระยะเวลาที่ศัตรูจะติดมึนงง (วินาที)
    [SerializeField] private string stunTriggerName = "Stun"; // ชื่อพารามิเตอร์ใน Animator สำหรับท่าโดนดาเมจ/มึนงง

    [Header("Animator Parameter Names")]
    [SerializeField] private string chargeTriggerName = "Charge"; // ชื่อ Trigger ใน Animator สำหรับท่าชาร์จพลัง
    [SerializeField] private string punchTriggerName = "Punch"; // ชื่อ Trigger ใน Animator สำหรับท่าพุ่งชก
    [SerializeField] private string chargeAnimStateName = "Charge"; // ชื่อ State ของท่าชาร์จใน Animator Controller
    [SerializeField] private string punchAnimStateName = "PunchR"; // ชื่อ State ของท่าต่อยใน Animator Controller

    [Header("Attack & Charge Settings")]
    [SerializeField][Min(0.01f)] private float chargeDuration = 1.5f; // ระยะเวลาชาร์จก่อนพุ่ง (วินาที)
    [SerializeField][Min(0.01f)] private float chargeAnimSpeed = 1f; // ตัวคูณความเร็วอนิเมชันชาร์จ (ปรับท่าให้เร็ว/ช้าลง)
    [SerializeField] private bool autoSyncChargeAnimSpeed = true; // คำนวณความเร็วอนิเมให้จบ 1 รอบพอดีกับ chargeDuration
    [SerializeField] private float attackDamage = 10f; // พลังโจมตี
    [SerializeField] private float damageDelay = 0.15f; // ดีเลย์ชกโดนตัว (วินาที)
    [SerializeField, FormerlySerializedAs("attackCooldownDuration")][Min(0f)] private float attackCooldownMinDuration = 3f; // เวลาคูลดาวน์ต่ำสุดก่อนโจมตีรอบถัดไป (วินาที)
    [SerializeField][Min(0f)] private float attackCooldownMaxDuration = 3f; // เวลาคูลดาวน์สูงสุดก่อนโจมตีรอบถัดไป (วินาที)
    [SerializeField] private float detectionRange = 15f; // ระยะตรวจจับผู้เล่น (หน่วย Unity)

    [Header("Charge Effect Settings")]
    [SerializeField] private bool enableChargeEffect = true;
    [SerializeField] private GameObject chargeEffectPrefab;
    [SerializeField] private Vector3 chargeEffectLocalOffset = Vector3.zero;
    [SerializeField][Min(0.000001f)] private float chargeEffectScale = 1f;
    [SerializeField][Min(0f)] private float chargeEffectStopDelay = 1.25f;
    [SerializeField] private bool activateChargeEffectChildren = true;
    [SerializeField] private bool logChargeEffectDebug = true;

    [Header("Punch Effect Settings")]
    [SerializeField] private GameObject punchEffectPrefab;
    [SerializeField] private Transform punchEffectPoint;
    [SerializeField] private Vector3 punchEffectLocalOffset = Vector3.zero;
    [SerializeField] private Vector3 punchEffectRotationEuler = Vector3.zero;
    [SerializeField][Min(0.000001f)] private float punchEffectScale = 1f;
    [SerializeField][Min(0.01f)] private float punchEffectSpeed = 1f;
    [SerializeField][Min(0.01f)] private float punchEffectDestroyDelay = 1.25f;
    [SerializeField] private bool parentPunchEffectToPoint;
    [SerializeField] private bool activatePunchEffectChildren = true;
    [SerializeField] private bool logPunchEffectDebug = true;

    [Header("Punch Sound Settings")]
    [Tooltip("เสียงเฉพาะของศัตรู — เว้นว่างไว้จะใช้เสียงกลางจาก PunchSoundPlayer บน Main Camera")]
    [SerializeField] private AudioClip punchSound;

    [Header("Lunge Settings")]
    [SerializeField] private float stoppingOffset = 0.3f; // ระยะหยุดห่างจากขอบตัวผู้เล่น
    [SerializeField] private float turnSpeed = 360f; // ความเร็วหมุนหันหน้าหาผู้เล่น (องศา/วินาที)
    [SerializeField] private float lungeDuration = 0.2f; // ความเร็วพุ่งชก (วินาที)
    [SerializeField] private float returnDuration = 0.2f; // ความเร็วพุ่งกลับ (วินาที)

    private Animator animator;
    private Health health;
    private Rigidbody rb;
    private StageController stage;
    private PlayerMovement playerMovement; // อ้างอิงเพื่อตรวจ state ของผู้เล่น
    
    // สำหรับล็อคพิกัดยืนของศัตรูไม่ให้เลื่อนไหล
    private Vector3 spawnPosition;
    private Quaternion spawnRotation;

    // สำหรับเช็คและประมวลผลอินพุตของตัวผู้เล่นเองเพื่อหาขนาด
    private Collider enemyCollider;

    private Coroutine activeBehaviorCoroutine;
    private float nextAttackTime = 0f; // เก็บเวลาที่จะสามารถเริ่มโจมตีรอบถัดไปได้
    private bool wasKinematic; // เก็บค่า isKinematic เดิมก่อนล็อค
    private GameObject activeChargeEffect;
    private ParticleSystem[] activeChargeParticles;
    private Animator[] activeChargeEffectAnimators;

    // Property เพื่อให้ภายนอกตรวจสอบ State ปัจจุบันได้
    public EnemyState CurrentState => currentState;

    void OnValidate()
    {
        attackCooldownMinDuration = Mathf.Max(0f, attackCooldownMinDuration);
        attackCooldownMaxDuration = Mathf.Max(attackCooldownMinDuration, attackCooldownMaxDuration);
        chargeEffectScale = Mathf.Max(0.000001f, chargeEffectScale);
        chargeEffectStopDelay = Mathf.Max(0f, chargeEffectStopDelay);
        punchEffectScale = Mathf.Max(0.000001f, punchEffectScale);
        punchEffectSpeed = Mathf.Max(0.01f, punchEffectSpeed);
        punchEffectDestroyDelay = Mathf.Max(0.01f, punchEffectDestroyDelay);
    }

    private void ScheduleNextAttack()
    {
        nextAttackTime = Time.time + GetRandomAttackCooldownDuration();
    }

    private float GetRandomAttackCooldownDuration()
    {
        float minDuration = Mathf.Max(0f, attackCooldownMinDuration);
        float maxDuration = Mathf.Max(minDuration, attackCooldownMaxDuration);
        return Random.Range(minDuration, maxDuration);
    }

    void Awake()
    {
        // ค้นหา Animator จากตัวเอง หรือจาก child objects (โมเดล 3D มักอยู่เป็นลูก)
        animator = GetComponentInChildren<Animator>();
        health = GetComponent<Health>();
        enemyCollider = GetComponent<Collider>();
        rb = GetComponent<Rigidbody>();
        stage = StageController.GetOrCreateActiveStage();
        stage.RegisterEnemy(this);
        playerMovement = ResolvePlayerMovement(); // หา PlayerMovement ในฉาก

        if (animator != null)
        {
            Debug.Log($"[EnemyBrain] พบ Animator บน '{animator.gameObject.name}', Controller: {animator.runtimeAnimatorController}");
        }
        else
        {
            Debug.LogError("[EnemyBrain] ไม่พบ Animator component ทั้งบนตัวเองและ child objects! Trigger จะไม่ทำงาน");
        }
    }

    void OnEnable()
    {
        if (health != null)
        {
            // รับอีเวนต์เมื่อโดนดาเมจ เพื่อเปลี่ยนเข้าสู่สถานะ Stun
            health.OnTakeDamage.AddListener(HandleTakeDamage);
        }
    }

    void OnDisable()
    {
        if (health != null)
        {
            health.OnTakeDamage.RemoveListener(HandleTakeDamage);
        }

        StopChargeEffect(true);
    }

    void Start()
    {
        // บันทึกตำแหน่งและองศาการหันตั้งต้นตอนเริ่มเกมไว้
        spawnPosition = transform.position;
        spawnRotation = transform.rotation;

        // ปิด root motion ด้วยเหตุผลเดียวกับฝั่งผู้เล่น — คลิปชุดนี้ไม่ได้ bake ตำแหน่ง XZ
        // เข้า pose ถ้าเปิดค้างไว้ Animator จะลากศัตรูออกจากจุดยืนในช่วง Charge/Punch
        // ซึ่งเป็นจังหวะที่ไม่มีใครล็อกพิกัดให้
        if (animator != null)
        {
            animator.applyRootMotion = false;
        }

        // เริ่มนับคูลดาวน์ตั้งแต่ต้นเกม เพื่อไม่ให้โจมตีทันที
        ScheduleNextAttack();

        // เริ่มต้นให้อยู่ในสถานะ Idle
        TransitionToState(EnemyState.Idle);
    }

    void Update()
    {
        // อัปเดตการทำงาน (Behavior) ในแต่ละเฟรมตามสถานะปัจจุบัน
        switch (currentState)
        {
            case EnemyState.Idle:
                UpdateIdle();
                break;
            case EnemyState.Charge:
            case EnemyState.Punch:
            case EnemyState.Stun:
                // ในสเตทเหล่านี้จะมีคอรันทีนเฉพาะทางจัดการอยู่ ไม่ต้องทำอะไรเพิ่มใน Update
                break;
        }
    }


    // ล็อกพิกัดซ้ำหลัง Animator เขียน Transform เสร็จ — ที่ล็อกไว้ใน Update/coroutine เป็นการ
    // เซ็ต "ก่อน" Animator ทำงาน จึงถูกทับได้เสมอ (เหตุผลเดียวกับ CharacterPositionLock)
    void LateUpdate()
    {
        if (currentState == EnemyState.Idle)
        {
            transform.position = spawnPosition;
            transform.rotation = spawnRotation;
            return;
        }

        // Charge/Stun ก็ยืนอยู่กับที่ แต่ทิศทางปล่อยให้ SmoothLookAtPlayer คุมเอง ไม่ต้องล็อก
        if (currentState == EnemyState.Charge || currentState == EnemyState.Stun)
        {
            transform.position = spawnPosition;
        }
    }

    // ฟังก์ชันสำหรับสลับเปลี่ยนสถานะของ AI
    public void TransitionToState(EnemyState newState)
    {
        // ออกจากสถานะเก่า (Exit State)
        ExitState(currentState);

        // เข้าสู่สถานะใหม่
        currentState = newState;
        EnterState(currentState);
    }

    // สิ่งที่ทำเมื่อเริ่มต้นเข้าสู่สถานะใหม่
    private void EnterState(EnemyState state)
    {
        Debug.Log($"[EnemyBrain] {gameObject.name} เข้าสู่สถานะ: {state}");

        switch (state)
        {
            case EnemyState.Idle:
                // ยืนล็อกพิกัดตำแหน่งเริ่มต้น
                transform.position = spawnPosition;
                transform.rotation = spawnRotation;
                break;

            case EnemyState.Charge:
                // เล่นแอนิเมชันเตรียมชาร์จพลัง (ความเร็วตั้งใน ChargeBehaviorCoroutine)
                if (animator != null)
                {
                    animator.ResetTrigger(punchTriggerName);
                    animator.SetTrigger(chargeTriggerName);
                }

                StartChargeEffect();
                
                // เริ่มคอรันทีนชาร์จพลังเตรียมโจมตี
                activeBehaviorCoroutine = StartCoroutine(ChargeBehaviorCoroutine());
                break;

            case EnemyState.Punch:
                // ล็อคฟิสิกส์กันถูกผลักขณะพุ่งชก
                FreezePhysics();
                // ค้นหาผู้เล่นเพื่อพุ่งชน
                Transform player = FindPlayer();
                if (player != null)
                {
                    activeBehaviorCoroutine = StartCoroutine(LungeAttackCoroutine(player));
                }
                else
                {
                    Debug.LogWarning("[EnemyBrain] หาตัวผู้เล่นไม่เจอ! ยกเลิกการโจมตี");
                    TransitionToState(EnemyState.Idle);
                }
                break;

            case EnemyState.Stun:
                // เล่นแอนิเมชันมึนงง
                if (animator != null)
                {
                    animator.SetTrigger(stunTriggerName);
                }

                // สั่งให้นับเวลาถอยหลังเพื่อหลุดพ้นจากสถานะ Stun
                activeBehaviorCoroutine = StartCoroutine(StunTimerCoroutine());
                break;
        }
    }

    // พฤติกรรมที่ประมวลผลต่อเนื่องใน Update
    private void UpdateIdle()
    {
        // ยืนล็อคพิกัดตำแหน่งเริ่มต้น
        transform.position = spawnPosition;
        transform.rotation = spawnRotation;

        // ถ้ายังไม่หมดคูลดาวน์ -> รอต่อ
        if (Time.time < nextAttackTime) return;

        // หาผู้เล่นจาก Tag "Player"
        Transform player = FindPlayer();
        if (player == null) return;

        Health playerHealth = player.GetComponent<Health>();
        if (playerHealth != null && !playerHealth.IsDead)
        {
            // เจอผู้เล่นและยังมีชีวิตอยู่ -> เริ่มชาร์จเตรียมโจมตี
            TransitionToState(EnemyState.Charge);
        }
    }

    // สิ่งที่ทำก่อนจะย้ายออกจากสถานะเดิม
    private void ExitState(EnemyState state)
    {
        // ยุติการทำงานของพฤติกรรมในสถานะเก่าทันที เพื่อให้สถานะใหม่ขึ้นมาทำงานได้อย่างสมบูรณ์
        if (activeBehaviorCoroutine != null)
        {
            StopCoroutine(activeBehaviorCoroutine);
            activeBehaviorCoroutine = null;
        }

        switch (state)
        {
            case EnemyState.Idle:
                break;
            case EnemyState.Charge:
                StopChargeEffect();

                // คืนค่าความเร็ว Animator กลับเป็นปกติ
                if (animator != null)
                {
                    animator.speed = 1.0f;
                }
                UnfreezePhysics();
                break;
            case EnemyState.Punch:
                UnfreezePhysics();
                break;
            case EnemyState.Stun:
                // รีเซ็ต Trigger ในกรณีติด Stun แล้วฟื้น เพื่อไม่ให้ Trigger ค้างไปยิงซ้ำ
                if (animator != null)
                {
                    animator.ResetTrigger(stunTriggerName);
                }
                break;
        }
    }

    // เมื่อได้รับความเสียหายจาก Health.cs
    private void HandleTakeDamage(float damageAmount)
    {
        // หากศัตรูกำลังชาร์จ (Charge) หรือกำลังพุ่งโจมตี (Punch) จะมีสถานะป้องกันการโดนขัดจังหวะ (ขัดไม่ได้)
        if (currentState == EnemyState.Charge || currentState == EnemyState.Punch)
        {
            Debug.Log($"[EnemyBrain] {gameObject.name} โดนดาเมจแต่ไม่ติด Stun เนื่องจากอยู่ในสถานะ: {currentState}");
            return;
        }

        // ถ้าอยู่นอกเหนือนั้น (เช่น Idle) โดนดาเมจแล้วจะเปลี่ยนเป็นสถานะ Stun ทันที
        TransitionToState(EnemyState.Stun);
    }

    // ล็อค Rigidbody กันถูกผลัก (เรียกตอนเริ่ม Charge/Punch)
    private void FreezePhysics()
    {
        if (rb != null)
        {
            wasKinematic = rb.isKinematic;
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    // ปลดล็อค Rigidbody กลับสู่ปกติ
    private void UnfreezePhysics()
    {
        if (rb != null)
        {
            rb.isKinematic = wasKinematic;
        }
    }

    // หมุนหันหน้าหาผู้เล่นแบบ smooth ทุกเฟรม (ใช้ turnSpeed องศา/วินาที)
    private void SmoothLookAtPlayer()
    {
        Transform player = FindPlayer();
        if (player == null) return;

        Vector3 targetPos = player.position;
        targetPos.y = transform.position.y; // แบนแกน Y
        Vector3 dir = (targetPos - transform.position).normalized;
        
        if (dir == Vector3.zero) return;

        Quaternion targetRot = Quaternion.LookRotation(dir);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, targetRot, turnSpeed * Time.deltaTime
        );
    }

    // คอร์รูทีนชาร์จพลัง — ล็อคตำแหน่งทุกเฟรมตลอดช่วงชาร์จ
    private IEnumerator ChargeBehaviorCoroutine()
    {
        FreezePhysics();

        Debug.Log($"[EnemyBrain] เริ่มรัน ChargeBehaviorCoroutine. chargeDuration={chargeDuration}s, autoSync={autoSyncChargeAnimSpeed}, animSpeed={chargeAnimSpeed}");

        // รอ 1 เฟรมให้ trigger ใน Animator ทำงาน
        yield return null;

        bool chargeAnimStarted = false;

        if (animator != null && !string.IsNullOrEmpty(chargeAnimStateName))
        {
            // รอจนกระทั่ง Animator เริ่มทำการทรานซิชัน หรือเข้าสู่ state การชาร์จโดยตรง
            float elapsed = 0f;
            while (!animator.GetCurrentAnimatorStateInfo(0).IsName(chargeAnimStateName) &&
                   !animator.GetNextAnimatorStateInfo(0).IsName(chargeAnimStateName))
            {
                transform.position = spawnPosition;
                SmoothLookAtPlayer();
                elapsed += Time.deltaTime;
                if (elapsed > 1.0f)
                {
                    var curState = animator.GetCurrentAnimatorStateInfo(0);
                    Debug.LogWarning($"[EnemyBrain] รอเริ่มทรานซิชันเข้าสู่ Charge นานเกิน 1 วินาที! CurrentStateHash={curState.shortNameHash}");
                    break;
                }
                yield return null;
            }

            chargeAnimStarted = animator.GetCurrentAnimatorStateInfo(0).IsName(chargeAnimStateName)
                             || animator.GetNextAnimatorStateInfo(0).IsName(chargeAnimStateName);

            if (chargeAnimStarted)
            {
                ApplyChargeAnimSpeed();
            }
            else
            {
                Debug.LogWarning("[EnemyBrain] ไม่พบสเตท Charge ใน Animator — ใช้ chargeDuration อย่างเดียว");
            }
        }
        else
        {
            Debug.Log("[EnemyBrain] ไม่ได้ระบุ Animator หรือ chargeAnimStateName ว่างเปล่า -> ใช้ chargeDuration อย่างเดียว");
        }

        // รอตาม chargeDuration ที่ตั้งไว้ (ควบคุมเวลาชาร์จหลัก)
        // *** ตรวจ Rising Edge: Player เริ่ม Dodge ใหม่ระหว่างชาร์จเท่านั้น (ไม่นับถ้า Dodge มาก่อนหน้า) ***
        float chargeElapsed = 0f;
        bool dodgeInterrupted = false;

        // บันทึก state ก่อนเข้าลูป เพื่อตรวจการเปลี่ยนแปลง (rising edge)
        PlayerMovement targetPlayerMovement = ResolvePlayerMovement();
        bool wasInDodge = targetPlayerMovement != null && targetPlayerMovement.CurrentState == PlayerState.Dodge;

        while (chargeElapsed < chargeDuration)
        {
            transform.position = spawnPosition;
            SmoothLookAtPlayer();

            targetPlayerMovement = ResolvePlayerMovement();
            bool isInDodge = targetPlayerMovement != null && targetPlayerMovement.CurrentState == PlayerState.Dodge;

            // ตรวจ rising edge: Player เพิ่ง *เข้า* Dodge (จาก non-Dodge → Dodge) ระหว่างชาร์จ
            if (!wasInDodge && isInDodge)
            {
                Debug.Log("[EnemyBrain] Player เริ่ม Dodge ใหม่ระหว่างชาร์จ → Interrupt และ Punch ทันที!");
                dodgeInterrupted = true;
                break;
            }

            wasInDodge = isInDodge;
            chargeElapsed += Time.deltaTime;
            yield return null;
        }

        // ชาร์จเสร็จ (ปกติ หรือ interrupt จาก Dodge) — เปลี่ยนไปลุยสเตทออกหมัด
        if (dodgeInterrupted)
        {
            Debug.Log("[EnemyBrain] Punch ถูก Trigger จากการ Dodge ของ Player!");
        }
        activeBehaviorCoroutine = null;
        TransitionToState(EnemyState.Punch);
    }

    private void StartChargeEffect()
    {
        if (!enableChargeEffect)
        {
            return;
        }

        if (chargeEffectPrefab == null)
        {
            if (logChargeEffectDebug)
            {
                Debug.LogWarning("[EnemyBrain] Charge effect ไม่เล่น เพราะยังไม่ได้ใส่ chargeEffectPrefab ใน Inspector");
            }
            return;
        }

        StopChargeEffect(true);

        activeChargeEffect = Instantiate(chargeEffectPrefab, transform);
        activeChargeEffect.name = $"{chargeEffectPrefab.name} (Enemy Charge)";
        activeChargeEffect.transform.localPosition = chargeEffectLocalOffset;
        activeChargeEffect.transform.localRotation = Quaternion.identity;
        activeChargeEffect.transform.localScale = chargeEffectPrefab.transform.localScale * chargeEffectScale;

        if (activateChargeEffectChildren)
        {
            SetActiveRecursively(activeChargeEffect.transform, true);
        }

        activeChargeParticles = activeChargeEffect.GetComponentsInChildren<ParticleSystem>(true);
        activeChargeEffectAnimators = activeChargeEffect.GetComponentsInChildren<Animator>(true);
        activeChargeEffect.SetActive(true);

        foreach (ParticleSystem particle in activeChargeParticles)
        {
            if (particle == null) continue;

            GameObject particleObject = particle.gameObject;
            if (!particleObject.activeSelf)
            {
                particleObject.SetActive(true);
            }

            particle.Clear(true);
            particle.Play(true);
        }

        foreach (Animator effectAnimator in activeChargeEffectAnimators)
        {
            if (effectAnimator == null) continue;

            if (!effectAnimator.gameObject.activeSelf)
            {
                effectAnimator.gameObject.SetActive(true);
            }

            effectAnimator.enabled = true;
            effectAnimator.Rebind();
            effectAnimator.Update(0f);
        }

        activeChargeEffect.BroadcastMessage("Play", SendMessageOptions.DontRequireReceiver);

        if (logChargeEffectDebug)
        {
            Debug.Log($"[EnemyBrain] เล่น Charge effect: {activeChargeEffect.name} | ParticleSystems={activeChargeParticles.Length}, Animators={activeChargeEffectAnimators.Length}");
        }
    }

    private void StopChargeEffect(bool immediate = false)
    {
        if (activeChargeEffect == null) return;

        GameObject effectToStop = activeChargeEffect;
        ParticleSystem[] particlesToStop = activeChargeParticles;
        Animator[] animatorsToStop = activeChargeEffectAnimators;

        activeChargeEffect = null;
        activeChargeParticles = null;
        activeChargeEffectAnimators = null;

        if (immediate)
        {
            if (Application.isPlaying)
            {
                Destroy(effectToStop);
            }
            else
            {
                DestroyImmediate(effectToStop);
            }
            return;
        }

        effectToStop.transform.SetParent(null, true);

        if (particlesToStop != null)
        {
            foreach (ParticleSystem particle in particlesToStop)
            {
                if (particle != null)
                {
                    particle.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }
        }

        if (animatorsToStop != null)
        {
            foreach (Animator effectAnimator in animatorsToStop)
            {
                if (effectAnimator != null)
                {
                    effectAnimator.enabled = false;
                }
            }
        }

        effectToStop.BroadcastMessage("Stop", SendMessageOptions.DontRequireReceiver);

        Destroy(effectToStop, chargeEffectStopDelay);
    }

    private void SetActiveRecursively(Transform target, bool isActive)
    {
        target.gameObject.SetActive(isActive);

        foreach (Transform child in target)
        {
            SetActiveRecursively(child, isActive);
        }
    }

    private void PlayPunchEffect()
    {
        // เสียงแยกจาก effect โดยตั้งใจ — PunchEffectPlayer.Play จะ return ทันทีถ้าไม่ได้ใส่
        // prefab เอาไว้ ถ้าเอาเสียงไปฝากไว้ในนั้นจะกลายเป็นว่าต้องมี effect ก่อนจึงจะมีเสียง
        PunchSoundPlayer.PlayGlobal(punchSound, "EnemyBrain");

        PunchEffectPlayer.Play(
            punchEffectPrefab,
            punchEffectPoint,
            transform,
            punchEffectLocalOffset,
            punchEffectRotationEuler,
            punchEffectScale,
            punchEffectSpeed,
            punchEffectDestroyDelay,
            parentPunchEffectToPoint,
            activatePunchEffectChildren,
            logPunchEffectDebug ? "EnemyBrain" : "");
    }

    // ตั้งความเร็ว Animator ให้ท่าชาร์จสอดคล้องกับ chargeDuration
    private void ApplyChargeAnimSpeed()
    {
        if (animator == null) return;

        float appliedSpeed = chargeAnimSpeed;

        if (autoSyncChargeAnimSpeed)
        {
            var stateInfo = animator.GetCurrentAnimatorStateInfo(0);
            if (!stateInfo.IsName(chargeAnimStateName))
            {
                stateInfo = animator.GetNextAnimatorStateInfo(0);
            }

            // stateInfo.length = ความยาวคลิป ÷ speed ของ state ใน Animator Controller
            if (stateInfo.length > 0.001f)
            {
                appliedSpeed = (stateInfo.length / chargeDuration) * chargeAnimSpeed;
            }
        }

        animator.speed = appliedSpeed;
        Debug.Log($"[EnemyBrain] Charge anim speed = {appliedSpeed:F2} (duration={chargeDuration}s, multiplier={chargeAnimSpeed})");
    }

    // ฟังก์ชันช่วยหาจุดที่ใกล้ที่สุดบน Collider อย่างปลอดภัย (รองรับ CharacterController และป้องกันการโยน Exception)
    private Vector3 GetClosestPoint(Collider col, Vector3 toPosition)
    {
        if (col == null) return toPosition;

        // หากเป็น CharacterController (ซึ่งมักไม่มีการรองรับ ClosestPoint ดั้งเดิมที่ดีพอในบางเวอร์ชัน)
        if (col is CharacterController cc)
        {
            Vector3 center = cc.transform.TransformPoint(cc.center);
            float halfHeight = Mathf.Max(0f, (cc.height * 0.5f) - cc.radius);
            Vector3 top = center + Vector3.up * halfHeight;
            Vector3 bottom = center + Vector3.down * halfHeight;
            
            Vector3 dir = top - bottom;
            float length = dir.magnitude;
            if (length < 0.0001f) return center;
            
            dir.Normalize();
            float t = Vector3.Dot(toPosition - bottom, dir);
            t = Mathf.Clamp(t, 0f, length);
            Vector3 closestPointOnSegment = bottom + dir * t;
            
            Vector3 toClosest = toPosition - closestPointOnSegment;
            float dist = toClosest.magnitude;
            if (dist > 0.0001f)
            {
                return closestPointOnSegment + (toClosest / dist) * (cc.radius * Mathf.Max(cc.transform.localScale.x, cc.transform.localScale.z));
            }
            return closestPointOnSegment;
        }

        // สำหรับ Collider ทั่วไป
        try
        {
            return col.ClosestPoint(toPosition);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[EnemyBrain] ClosestPoint ล้มเหลวสำหรับ {col.name}: {e.Message} (จะสลับไปใช้ Bounds แทน)");
            return col.bounds.ClosestPoint(toPosition);
        }
    }

    // คอรันทีนการพุ่งโจมตีผู้เล่น (โลจิกเหมือนกับ Player)
    private IEnumerator LungeAttackCoroutine(Transform target)
    {
        // ยิง Punch trigger ทันทีที่เข้า Punch — ให้ Animator เปลี่ยนจาก Charge ไป PunchR พร้อมกับพุ่ง
        if (animator != null)
        {
            animator.SetTrigger(punchTriggerName);
        }

        yield return null; // รอ 1 เฟรมให้ trigger ใน Animator ทำงาน

        // คำนวณทิศทางและระยะหยุด
        Collider playerCollider = target.GetComponent<Collider>();
        CharacterController playerCC = target.GetComponent<CharacterController>();
        // คำนวณทิศทางและระยะหยุด (แบนบนแกน XZ เพื่อป้องกันความต่างระดับความสูงแกน Y)
        Vector3 targetPos = target.position;
        targetPos.y = spawnPosition.y;
        Vector3 direction = (targetPos - spawnPosition).normalized;
        Vector3 stoppingPosition;

        // คำนวณขนาดความกว้าง (รัศมี) ของตัวศัตรูเอง
        float selfSizeOffset = 0f;
        if (enemyCollider != null)
        {
            if (enemyCollider is CapsuleCollider capsule)
                selfSizeOffset = capsule.radius * Mathf.Max(transform.localScale.x, transform.localScale.z);
            else if (enemyCollider is SphereCollider sphere)
                selfSizeOffset = sphere.radius * Mathf.Max(transform.localScale.x, transform.localScale.y, transform.localScale.z);
            else if (enemyCollider is BoxCollider box)
            {
                Vector3 localDir = transform.InverseTransformDirection(direction);
                Vector3 halfSize = box.size * 0.5f;
                selfSizeOffset = Mathf.Abs(localDir.x * halfSize.x * transform.localScale.x) +
                                 Mathf.Abs(localDir.z * halfSize.z * transform.localScale.z);
            }
        }

        // คำนวณขนาดความกว้าง (รัศมี) ของผู้เล่น
        float playerSizeOffset = 0f;
        if (playerCollider != null)
        {
            if (playerCollider is CapsuleCollider playerCapsule)
                playerSizeOffset = playerCapsule.radius * Mathf.Max(target.localScale.x, target.localScale.z);
            else if (playerCollider is SphereCollider playerSphere)
                playerSizeOffset = playerSphere.radius * Mathf.Max(target.localScale.x, target.localScale.y, target.localScale.z);
            else if (playerCollider is BoxCollider playerBox)
            {
                Vector3 localDir = target.InverseTransformDirection(-direction);
                Vector3 halfSize = playerBox.size * 0.5f;
                playerSizeOffset = Mathf.Abs(localDir.x * halfSize.x * target.localScale.x) +
                                   Mathf.Abs(localDir.z * halfSize.z * target.localScale.z);
            }
        }
        else if (playerCC != null)
        {
            playerSizeOffset = playerCC.radius * Mathf.Max(target.localScale.x, target.localScale.z);
        }

        if (playerCollider != null)
        {
            // หาขอบตัวผู้เล่นที่ใกล้ศัตรูที่สุดแบบปลอดภัย
            Vector3 closestPoint = GetClosestPoint(playerCollider, spawnPosition);
            closestPoint.y = spawnPosition.y;

            // ในเมื่อได้จุดผิวของผู้เล่น (closestPoint) แล้ว ระยะหยุดถอยห่างออกมาจะเท่ากับ (ขนาดตัวศัตรู + offset)
            float totalBuffer = selfSizeOffset + stoppingOffset;
            stoppingPosition = closestPoint - (direction * totalBuffer);
        }
        else
        {
            // Fallback ในกรณีไม่มี Collider ใช้จุดศูนย์กลางผู้เล่น แล้วลบขนาดรัศมีผู้เล่นรวมไปด้วย
            float totalBuffer = selfSizeOffset + playerSizeOffset + stoppingOffset;
            stoppingPosition = targetPos - (direction * totalBuffer);
        }
        stoppingPosition.y = spawnPosition.y;

        // เช็คกันพุ่งเลยหัว (ถ้าจุดหยุดอยู่หลังตัวศัตรู = ใกล้เกินไปแล้ว)
        if (Vector3.Dot(stoppingPosition - spawnPosition, direction) <= 0)
        {
            stoppingPosition = spawnPosition;
        }

        // หันหน้ามองผู้เล่นตรงๆ ก่อนเริ่มพุ่ง (ระหว่าง Charge หมุนหาผู้เล่นอยู่แล้ว)
        if (direction != Vector3.zero && stoppingPosition != spawnPosition)
        {
            transform.rotation = Quaternion.LookRotation(direction);
        }

        // 1. พุ่งไปหาผู้เล่น (Lunge)
        float elapsed = 0f;
        while (elapsed < lungeDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / lungeDuration;
            Vector3 currentPos = Vector3.Lerp(spawnPosition, stoppingPosition, t);
            currentPos.y = spawnPosition.y;
            transform.position = currentPos;
            yield return null;
        }
        transform.position = stoppingPosition;

        yield return null; // รอ 1 เฟรมให้ภาพ Render สนิท

        // 2. รอให้ Animator เข้าสู่ท่าต่อย (trigger ถูกยิงตอนเริ่ม Punch แล้ว)
        if (animator != null && !string.IsNullOrEmpty(punchAnimStateName))
        {
            // รอจนกระทั่ง Animator เริ่มทำการทรานซิชัน หรือเข้าสู่ state การต่อยโดยตรง
            float punchElapsed = 0f;
            while (!animator.GetCurrentAnimatorStateInfo(0).IsName(punchAnimStateName) && 
                   !animator.GetNextAnimatorStateInfo(0).IsName(punchAnimStateName))
            {
                punchElapsed += Time.deltaTime;
                if (punchElapsed > 1.0f) // ตัวช่วยหลุดกรณีฉุกเฉิน (1 วินาที)
                {
                    Debug.LogWarning("[EnemyBrain] รอเริ่มทรานซิชันเข้าสู่ PunchR นานเกิน 1 วินาที!");
                    break;
                }
                yield return null;
            }

            // เมื่อเริ่มทรานซิชันแล้ว ค่อยรีเซ็ต trigger เพื่อป้องกันปัญหากลืนทริกเกอร์เร็วเกินไป
            animator.ResetTrigger(punchTriggerName);
            Debug.Log($"[EnemyBrain] รีเซ็ต Punch trigger: {punchTriggerName}");

            // รอตามเวลาดีเลย์เพื่อให้หมัดเอื้อมไปถึงตัวผู้เล่น แล้วค่อยทำความเสียหาย
            yield return new WaitForSeconds(damageDelay);
            PlayPunchEffect();

            Health playerHealth = target.GetComponent<Health>();
            if (playerHealth != null)
            {
                playerHealth.TakeDamage(attackDamage);
            }

            // รอจนกว่าแอนิเมชันชกจะเล่นจบอย่างสมบูรณ์ (หรือความคืบหน้าครบ 99%)
            punchElapsed = 0f;
            while (true)
            {
                var stateInfo = animator.GetCurrentAnimatorStateInfo(0);
                var nextStateInfo = animator.GetNextAnimatorStateInfo(0);

                float progress = 0f;
                bool inPunchState = false;

                if (stateInfo.IsName(punchAnimStateName))
                {
                    progress = stateInfo.normalizedTime;
                    inPunchState = true;
                }
                else if (nextStateInfo.IsName(punchAnimStateName))
                {
                    progress = nextStateInfo.normalizedTime;
                    inPunchState = true;
                }

                // ถ้าไม่ได้อยู่ในสเตท Punch หรือความคืบหน้าของเวลารันอนิเมชันครบ 99% แล้ว ให้จบการรอ
                if (!inPunchState || progress >= 0.99f)
                {
                    break;
                }

                punchElapsed += Time.deltaTime;
                if (punchElapsed > 3.0f) // ตัวช่วยหลุดกรณีฉุกเฉิน (3 วินาที)
                {
                    Debug.LogWarning("[EnemyBrain] รอเล่นอนิเมชัน PunchR ครบรอบนานเกิน 3 วินาที!");
                    break;
                }
                yield return null;
            }
        }
        else
        {
            // Fallback กรณีไม่มีชื่ออนิเมชันให้ตรวจจับ - ใช้เวลาดีเลย์แบบคงที่
            yield return null;
            if (animator != null)
            {
                animator.ResetTrigger(punchTriggerName);
            }

            yield return new WaitForSeconds(damageDelay);
            PlayPunchEffect();

            Health playerHealth = target.GetComponent<Health>();
            if (playerHealth != null)
            {
                playerHealth.TakeDamage(attackDamage);
            }

            // รอจังหวะท่าชกค้างสักพักก่อนถอยกลับ
            yield return new WaitForSeconds(0.2f);
        }

        // 3. สไลด์ถอยกลับจุดเดิม
        elapsed = 0f;
        Vector3 reachedPosition = transform.position;
        reachedPosition.y = spawnPosition.y;

        while (elapsed < returnDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / returnDuration;
            Vector3 currentPos = Vector3.Lerp(reachedPosition, spawnPosition, t);
            currentPos.y = spawnPosition.y;
            transform.position = currentPos;
            yield return null;
        }

        // เซ็ตเวลาคูลดาวน์ก่อนจะโจมตีรอบใหม่ได้
        ScheduleNextAttack();

        // กลับไปตั้งหลักที่ Idle
        activeBehaviorCoroutine = null;
        TransitionToState(EnemyState.Idle);
    }

    // คอร์รูทีนสำหรับนับเวลาถอยหลังการหายมึนงง (Stun)
    private IEnumerator StunTimerCoroutine()
    {
        yield return new WaitForSeconds(stunDuration);

        // เมื่อฟื้นตัว ถ้ายังไม่ตาย ให้กลับไปเริ่มลูปที่ Idle
        if (health != null && !health.IsDead)
        {
            // ตั้งเวลาให้ศัตรูยังไม่โจมตีทันทีหลังหายมึน โดยใช้ช่วงสุ่มเดียวกับคูลดาวน์โจมตี
            ScheduleNextAttack();
            activeBehaviorCoroutine = null;
            TransitionToState(EnemyState.Idle);
        }
    }

    // ฟังก์ชันค้นหาผู้เล่นที่มี Tag ว่า "Player"
    private PlayerMovement ResolvePlayerMovement()
    {
        if (stage != null && stage.Player != null)
        {
            playerMovement = stage.Player;
            return playerMovement;
        }

        if (playerMovement == null)
        {
            playerMovement = FindFirstObjectByType<PlayerMovement>();
        }

        return playerMovement;
    }

    private Transform FindPlayer()
    {
        PlayerMovement resolvedPlayer = ResolvePlayerMovement();
        if (resolvedPlayer != null)
        {
            return resolvedPlayer.transform;
        }

        if (stage != null && stage.PlayerTransform != null)
        {
            return stage.PlayerTransform;
        }

        GameObject player = GameObject.FindWithTag("Player");
        return player != null ? player.transform : null;
    }
}
