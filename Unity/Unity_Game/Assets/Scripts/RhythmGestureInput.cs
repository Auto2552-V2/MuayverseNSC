using UnityEngine;

/// <summary>
/// Rhythm-game input from DIRECTIONAL GESTURES (replaces the hand-cursor mechanic).
///
/// Each committed gesture from game_bridge.py --gesture (raise a straight arm and point it at a 3x3
/// direction) presses the matching grid slot for a short window, so a note in its hit window
/// registers. Two arms can hold two slots at once. Optionally previews the currently-aimed cell.
///
/// Setup (Rhythm.unity):
///   - PoseUdpReceiver in the scene (Port 5005, Bridge Host empty).
///   - RhythmInputGrid in the scene with an assigned Input Camera.
///   - This component (Grid/Receiver auto-found).
///   - REMOVE PoseHandCursors — this replaces it.
///   - Python: game_bridge.py --webcam --host 127.0.0.1 --gesture --stream-video --no-skeleton --mirror
/// </summary>
public class RhythmGestureInput : MonoBehaviour
{
    [SerializeField] private PoseUdpReceiver receiver;
    [SerializeField] private RhythmInputGrid grid;

    [Tooltip("How long a committed gesture holds its slot. Should exceed the note hit window so a " +
             "reach slightly before/after the beat still registers.")]
    [SerializeField] private float holdSeconds = 0.28f;

    [Tooltip("Hold the cell each arm is CURRENTLY aiming at (continuous). This is the natural model: " +
             "raise a straight arm, point at a direction, and that cell stays held while you hold the " +
             "pose - so raising higher moves to the top row live. Turn OFF for one-shot commit only.")]
    [SerializeField] private bool holdAimedPreview = true;

    // One committed hold per arm (left/right) so two arms can hold two slots.
    private int leftCell = -1, rightCell = -1;
    private float leftUntil, rightUntil;

    private void Awake()
    {
        if (receiver == null) receiver = FindFirstObjectByType<PoseUdpReceiver>();
        if (grid == null) grid = FindFirstObjectByType<RhythmInputGrid>();
    }

    private void OnEnable()
    {
        if (receiver != null) receiver.OnGestureCell += HandleGesture;
    }

    private void OnDisable()
    {
        if (receiver != null) receiver.OnGestureCell -= HandleGesture;
        if (grid != null) grid.SetHandHeldSlots(-1, -1);
    }

    private void HandleGesture(string arm, int cell, string quality)
    {
        if (arm == "left") { leftCell = cell; leftUntil = Time.time + holdSeconds; }
        else { rightCell = cell; rightUntil = Time.time + holdSeconds; }
    }

    private void Update()
    {
        if (grid == null) return;

        // Expire committed holds.
        if (leftCell >= 0 && Time.time >= leftUntil) leftCell = -1;
        if (rightCell >= 0 && Time.time >= rightUntil) rightCell = -1;

        int held0, held1;

        if (holdAimedPreview && receiver != null && receiver.LatestState != null
            && receiver.LatestState.pose_detected && receiver.LatestState.gesture != null)
        {
            // Continuous aim is primary: the cell the arm points at RIGHT NOW (updates as you raise).
            // Fall back to a just-committed cell only while the arm is between rest and re-aim.
            held0 = receiver.LatestState.gesture.left;
            held1 = receiver.LatestState.gesture.right;
            if (held0 < 0) held0 = leftCell;
            if (held1 < 0) held1 = rightCell;
        }
        else
        {
            held0 = leftCell;   // one-shot commit mode
            held1 = rightCell;
        }

        grid.SetHandHeldSlots(held0, held1);
    }
}
