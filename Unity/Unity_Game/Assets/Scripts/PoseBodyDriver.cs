using UnityEngine;

/// <summary>
/// Maps the player's real body 1:1 onto the fighter, continuously (30 Hz), using the STATE channel
/// from game_bridge.py — separate from the discrete punch/elbow/kick EVENTS.
///
/// The mapping is a small signal-processing chain; every stage is a knob you can tune:
///
///   center_x in [-1,+1]              (where the player stands in frame)
///        |
///   1. DEAD ZONE     |x| &lt; d -> 0                 ignores standing jitter
///        |
///   2. TRANSFER f(x) responseCurve.Evaluate(x')    the "function graph": straight = 1:1,
///        |                                          ease-in = fine control near centre
///   3. GAIN          * maxLateralOffset             world units
///        |
///   4. LOW-PASS      SmoothDamp (critically damped) kills tracking noise without the
///        |                                          lag of a moving average
///        v
///   PlayerMovement.ExternalBodyOffset  (+ lean tilt)
///
/// We feed PlayerMovement's offset rather than moving a Transform because PlayerMovement re-locks
/// its own position to spawnPosition every frame; shifting that anchor makes lunges, dodges and
/// returns all follow the player automatically.
///
/// SmoothDamp keeps the velocity for us, i.e. d/dt of the mapped position — exposed as
/// <see cref="LateralVelocity"/> for dodge detection / impact scaling / camera shake.
/// </summary>
public class PoseBodyDriver : MonoBehaviour
{
    [Header("Source")]
    [SerializeField] private PoseUdpReceiver receiver;
    [Tooltip("Fighter to drive. Auto-found when left empty.")]
    [SerializeField] private PlayerMovement playerMovement;
    [Tooltip("Fallback when there is no PlayerMovement: Transform to move. Empty = this GameObject.")]
    [SerializeField] private Transform target;

    [Header("1. Dead zone")]
    [Tooltip("Ignore |center_x| below this so standing still doesn't jitter the fighter.")]
    [Range(0f, 0.5f)][SerializeField] private float deadZone = 0.08f;

    [Header("2. Transfer function f(x)")]
    [Tooltip("Input = |center_x| after dead zone (0..1). Output = 0..1 of max offset. " +
             "Straight line = 1:1. Ease-in = precise near centre. Ease-out = snappy.")]
    [SerializeField] private AnimationCurve responseCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Header("3. Gain")]
    [Tooltip("World units the fighter moves at full deflection.")]
    [SerializeField] private float maxLateralOffset = 2.0f;
    [Tooltip("Movement axis in world space. X = left/right.")]
    [SerializeField] private Vector3 lateralAxis = Vector3.right;
    [Tooltip("Also map center_y to vertical movement (crouch/stand). Usually off for a fighter.")]
    [SerializeField] private bool useVertical = false;
    [SerializeField] private float maxVerticalOffset = 0.5f;

    [Header("4. Smoothing (low-pass)")]
    [Tooltip("Seconds to reach the target. Higher = smoother but laggier. 0.08-0.15 feels good.")]
    [SerializeField] private float smoothTime = 0.12f;

    [Header("Lean -> tilt")]
    [SerializeField] private bool applyLeanTilt = true;
    [Tooltip("Degrees of fighter roll per degree of measured trunk lean.")]
    [SerializeField] private float leanTiltRatio = 0.8f;
    [SerializeField] private float maxLeanTilt = 25f;

    [Header("Safety")]
    [Tooltip("Freeze mapping while the pose is lost so the fighter doesn't snap to a stale value.")]
    [SerializeField] private bool holdOnTrackingLoss = true;
    [SerializeField] private bool logFirstState = true;

    private Vector3 basePosition;
    private Quaternion baseRotation;
    private Vector3 currentOffset;
    private Vector3 offsetVelocity;      // dx/dt maintained by SmoothDamp
    private float currentTilt;
    private float tiltVelocity;
    private bool loggedFirstState;

    /// <summary>Smoothed lateral offset in world units (the mapped position).</summary>
    public float LateralOffset => Vector3.Dot(currentOffset, lateralAxis.normalized);

    /// <summary>d/dt of the lateral offset — use for dodge detection / impact scaling.</summary>
    public float LateralVelocity => Vector3.Dot(offsetVelocity, lateralAxis.normalized);

    private void Awake()
    {
        if (receiver == null) receiver = FindFirstObjectByType<PoseUdpReceiver>();
        if (playerMovement == null) playerMovement = FindFirstObjectByType<PlayerMovement>();
        if (target == null) target = transform;

        basePosition = target.localPosition;
        baseRotation = target.localRotation;

        if (receiver == null)
        {
            Debug.LogError("[PoseBodyDriver] no PoseUdpReceiver in scene - body mapping disabled");
        }
    }

    private void Update()
    {
        if (receiver == null) return;

        PoseStatePayload state = receiver.LatestState;
        if (state == null || state.body == null) return;
        if (holdOnTrackingLoss && !state.pose_detected) return;

        if (logFirstState && !loggedFirstState)
        {
            loggedFirstState = true;
            Debug.Log($"[PoseBodyDriver] first body state received (center_x={state.body.center_x:F2}) " +
                      $"-> driving {(playerMovement != null ? "PlayerMovement offset" : "Transform")}");
        }

        // --- 1. dead zone + 2. transfer function + 3. gain ---
        float targetLateral = MapAxis(state.body.center_x) * maxLateralOffset;
        float targetVertical = useVertical ? MapAxis(state.body.center_y) * -maxVerticalOffset : 0f;

        Vector3 desired = lateralAxis.normalized * targetLateral + Vector3.up * targetVertical;

        // --- 4. low-pass: critically damped spring, keeps velocity for us ---
        currentOffset = Vector3.SmoothDamp(currentOffset, desired, ref offsetVelocity, smoothTime);

        float desiredTilt = applyLeanTilt
            ? Mathf.Clamp(state.body.lean * leanTiltRatio, -maxLeanTilt, maxLeanTilt)
            : 0f;
        currentTilt = Mathf.SmoothDamp(currentTilt, desiredTilt, ref tiltVelocity, smoothTime);

        if (playerMovement != null)
        {
            // Shift the fighter's home anchor; every lunge/dodge/return follows it automatically.
            playerMovement.ExternalBodyOffset = currentOffset;
            playerMovement.ExternalBodyTilt = currentTilt;
        }
        else if (target != null)
        {
            target.localPosition = basePosition + currentOffset;
            if (applyLeanTilt)
            {
                target.localRotation = baseRotation * Quaternion.AngleAxis(-currentTilt, Vector3.forward);
            }
        }
    }

    /// <summary>Dead zone, then the designer-drawn response curve, sign preserved.</summary>
    private float MapAxis(float raw)
    {
        float magnitude = Mathf.Abs(raw);
        if (magnitude <= deadZone) return 0f;

        // Rescale (deadZone..1) -> (0..1) so there is no jump at the edge of the dead zone.
        float normalized = Mathf.InverseLerp(deadZone, 1f, magnitude);
        float shaped = responseCurve.Evaluate(Mathf.Clamp01(normalized));
        return Mathf.Sign(raw) * shaped;
    }

    /// <summary>Treat the current position as the new neutral (e.g. after the player repositions).</summary>
    public void Recalibrate()
    {
        currentOffset = Vector3.zero;
        offsetVelocity = Vector3.zero;
        currentTilt = 0f;
        tiltVelocity = 0f;
        if (playerMovement != null)
        {
            playerMovement.ExternalBodyOffset = Vector3.zero;
            playerMovement.ExternalBodyTilt = 0f;
        }
    }

    private void OnValidate()
    {
        smoothTime = Mathf.Max(0.01f, smoothTime);
        if (lateralAxis == Vector3.zero) lateralAxis = Vector3.right;
    }
}
