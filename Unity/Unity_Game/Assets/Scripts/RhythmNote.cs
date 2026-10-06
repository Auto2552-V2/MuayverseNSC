using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(Rigidbody))]
public class RhythmNote : MonoBehaviour
{
    [SerializeField] private RhythmInputGrid inputGrid;
    [SerializeField] private RhythmConductor conductor;
    [SerializeField] private Transform targetSlot;
    [SerializeField] private int slotIndex;
    [SerializeField] private RhythmNoteType noteType = RhythmNoteType.Tap;
    [SerializeField] private float hitTimeSeconds;
    [SerializeField] private float durationSeconds;
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private Vector3 approachDirection = Vector3.forward;
    [SerializeField] private float hitWindowSeconds = 0.18f;
    [SerializeField] private float missWindowSeconds = 0.35f;
    [SerializeField] private RhythmScoreManager scoreManager;

    private float fallbackStartTime;
    private bool holdStarted;
    private bool isFinished;
    private Vector3 baseScale;
    private float holdVisualLength;
    private float nextHoldComboTime;
    private int awardedHoldScore;

    private float EndTimeSeconds => hitTimeSeconds + Mathf.Max(0f, durationSeconds);
    private bool UsesConductor => conductor != null;
    private float SongTimeSeconds => UsesConductor ? conductor.SongTimeSeconds : Time.time - fallbackStartTime;

    public int SlotIndex => slotIndex;
    public float HitTimeSeconds => hitTimeSeconds;
    public float DurationSeconds => durationSeconds;
    public RhythmNoteType NoteType => noteType;

    private void Awake()
    {
        baseScale = transform.localScale;

        if (inputGrid == null)
        {
            inputGrid = FindFirstObjectByType<RhythmInputGrid>();
        }

        if (scoreManager == null)
        {
            scoreManager = RhythmScoreManager.Instance;
        }

        Collider noteCollider = GetComponent<Collider>();
        noteCollider.isTrigger = true;

        Rigidbody noteRigidbody = GetComponent<Rigidbody>();
        noteRigidbody.isKinematic = true;
        noteRigidbody.useGravity = false;
    }

    private void OnEnable()
    {
        fallbackStartTime = Time.time;
        if (inputGrid != null)
        {
            inputGrid.OnSlotHeld.AddListener(HandleSlotHeld);
        }

        if (scoreManager == null)
        {
            scoreManager = RhythmScoreManager.Instance;
        }
    }

    private void OnDisable()
    {
        if (inputGrid != null)
        {
            inputGrid.OnSlotHeld.RemoveListener(HandleSlotHeld);
        }
    }

    private void Update()
    {
        UpdatePosition();
        UpdateHitState();
    }

    public void Initialize(
        RhythmInputGrid grid,
        RhythmConductor songConductor,
        Transform slotTarget,
        int targetSlotIndex,
        RhythmNoteType targetNoteType,
        float targetHitTimeSeconds,
        float targetDurationSeconds,
        float speed,
        Vector3 movementDirection,
        float hitWindow)
    {
        inputGrid = grid;
        conductor = songConductor;
        targetSlot = slotTarget;
        slotIndex = targetSlotIndex;
        noteType = targetNoteType;
        hitTimeSeconds = targetHitTimeSeconds;
        durationSeconds = Mathf.Max(0f, targetDurationSeconds);
        moveSpeed = Mathf.Max(0.01f, speed);
        approachDirection = movementDirection == Vector3.zero ? Vector3.forward : movementDirection.normalized;
        hitWindowSeconds = Mathf.Max(0.01f, hitWindow);
        fallbackStartTime = Time.time;
        holdStarted = false;
        isFinished = false;
        nextHoldComboTime = 0f;
        awardedHoldScore = 0;
        holdVisualLength = GetHoldVisibleLength(SongTimeSeconds);
        ApplyHoldVisualLength(holdVisualLength);
        UpdatePosition();
    }

    private void UpdatePosition()
    {
        if (targetSlot == null || approachDirection == Vector3.zero)
        {
            return;
        }

        float songTime = SongTimeSeconds;
        float secondsUntilHit = hitTimeSeconds - songTime;
        float approachOffset = secondsUntilHit * moveSpeed;

        if (noteType == RhythmNoteType.Hold)
        {
            holdVisualLength = GetHoldVisibleLength(songTime);
            ApplyHoldVisualLength(holdVisualLength);
            approachOffset = Mathf.Max(0f, approachOffset);
        }

        float centerOffset = noteType == RhythmNoteType.Hold ? holdVisualLength * 0.5f : 0f;
        transform.position = targetSlot.position - approachDirection.normalized * (approachOffset + centerOffset);
    }

    private void UpdateHitState()
    {
        if (isFinished || inputGrid == null)
        {
            return;
        }

        float songTime = SongTimeSeconds;
        bool slotHeld = inputGrid.IsSlotHeld(slotIndex);

        if (noteType == RhythmNoteType.Tap || noteType == RhythmNoteType.Catch)
        {
            if (slotHeld && IsWithinWindow(songTime, hitTimeSeconds))
            {
                PlaySlotHitNeon();
                FinishNote(true);
                return;
            }

            if (songTime > hitTimeSeconds + missWindowSeconds)
            {
                FinishNote(false);
            }

            return;
        }

        UpdateHoldState(songTime, slotHeld);
    }

    private void UpdateHoldState(float songTime, bool slotHeld)
    {
        if (!holdStarted)
        {
            if (slotHeld && IsWithinWindow(songTime, hitTimeSeconds))
            {
                holdStarted = true;
                nextHoldComboTime = hitTimeSeconds + GetHoldTickInterval();
                PlaySlotHitNeon();
            }
            else if (songTime > hitTimeSeconds + missWindowSeconds)
            {
                FinishNote(false);
            }

            return;
        }

        if (!slotHeld && songTime < EndTimeSeconds)
        {
            FinishNote(false);
            return;
        }

        PlaySlotHitNeon();
        RegisterHoldScore(songTime);
        RegisterHoldComboTicks(songTime);

        if (songTime >= EndTimeSeconds)
        {
            FinishNote(true, false);
        }
    }

    private void RegisterHoldScore(float songTime)
    {
        if (scoreManager == null)
        {
            scoreManager = RhythmScoreManager.Instance;
        }

        int targetScore = Mathf.RoundToInt(durationSeconds * scoreManager.HoldScorePerSecond);
        if (targetScore <= 0 || durationSeconds <= 0f)
        {
            return;
        }

        float cappedSongTime = Mathf.Min(songTime, EndTimeSeconds);
        float progress = Mathf.InverseLerp(hitTimeSeconds, EndTimeSeconds, cappedSongTime);
        int targetScoreAtProgress = Mathf.FloorToInt(targetScore * progress);
        if (songTime >= EndTimeSeconds)
        {
            targetScoreAtProgress = targetScore;
        }

        int scoreToAward = targetScoreAtProgress - awardedHoldScore;
        if (scoreToAward <= 0)
        {
            return;
        }

        awardedHoldScore += scoreToAward;
        scoreManager.RegisterHoldScore(scoreToAward);
    }

    private void RegisterHoldComboTicks(float songTime)
    {
        if (scoreManager == null)
        {
            scoreManager = RhythmScoreManager.Instance;
        }

        float interval = GetHoldTickInterval();
        float cappedSongTime = Mathf.Min(songTime, EndTimeSeconds);
        while (nextHoldComboTime <= cappedSongTime)
        {
            scoreManager.RegisterHoldTick();
            nextHoldComboTime += interval;
        }
    }

    private float GetHoldTickInterval()
    {
        if (scoreManager == null)
        {
            scoreManager = RhythmScoreManager.Instance;
        }

        return Mathf.Max(0.01f, scoreManager.HoldTickIntervalSeconds);
    }

    private float GetHoldVisibleLength(float songTime)
    {
        if (noteType != RhythmNoteType.Hold)
        {
            return 0f;
        }

        if (songTime < hitTimeSeconds)
        {
            return durationSeconds * moveSpeed;
        }

        return Mathf.Max(0f, EndTimeSeconds - songTime) * moveSpeed;
    }

    private void ApplyHoldVisualLength(float visibleLength)
    {
        if (baseScale == Vector3.zero)
        {
            baseScale = transform.localScale;
        }

        transform.localScale = baseScale;

        if (noteType != RhythmNoteType.Hold || visibleLength <= 0f)
        {
            return;
        }

        Vector3 absoluteDirection = new Vector3(
            Mathf.Abs(approachDirection.x),
            Mathf.Abs(approachDirection.y),
            Mathf.Abs(approachDirection.z));

        Vector3 scale = baseScale;
        if (absoluteDirection.x >= absoluteDirection.y && absoluteDirection.x >= absoluteDirection.z)
        {
            scale.x = Mathf.Max(0.01f, baseScale.x + visibleLength);
        }
        else if (absoluteDirection.y >= absoluteDirection.x && absoluteDirection.y >= absoluteDirection.z)
        {
            scale.y = Mathf.Max(0.01f, baseScale.y + visibleLength);
        }
        else
        {
            scale.z = Mathf.Max(0.01f, baseScale.z + visibleLength);
        }

        transform.localScale = scale;
    }

    private void HandleSlotHeld(int heldSlotIndex)
    {
        if (heldSlotIndex == slotIndex)
        {
            UpdateHitState();
        }
    }

    private bool IsWithinWindow(float songTime, float targetTime)
    {
        return Mathf.Abs(songTime - targetTime) <= hitWindowSeconds;
    }

    private void PlaySlotHitNeon()
    {
        if (inputGrid != null)
        {
            inputGrid.PlayHitNeon(slotIndex);
        }
    }

    private void FinishNote(bool wasHit, bool awardScore = true)
    {
        if (isFinished)
        {
            return;
        }

        isFinished = true;
        if (scoreManager == null)
        {
            scoreManager = RhythmScoreManager.Instance;
        }

        if (wasHit && awardScore)
        {
            scoreManager.RegisterHit(noteType);
        }
        else if (!wasHit)
        {
            scoreManager.RegisterMiss();
        }

        Destroy(gameObject);
    }

    private void Reset()
    {
        Collider noteCollider = GetComponent<Collider>();
        noteCollider.isTrigger = true;

        Rigidbody noteRigidbody = GetComponent<Rigidbody>();
        noteRigidbody.isKinematic = true;
        noteRigidbody.useGravity = false;
    }

    private void OnValidate()
    {
        slotIndex = Mathf.Clamp(slotIndex, 0, RhythmInputGrid.SlotCount - 1);
        durationSeconds = Mathf.Max(0f, durationSeconds);
        moveSpeed = Mathf.Max(0.01f, moveSpeed);
        hitWindowSeconds = Mathf.Max(0.01f, hitWindowSeconds);
        missWindowSeconds = Mathf.Max(hitWindowSeconds, missWindowSeconds);
    }
}
