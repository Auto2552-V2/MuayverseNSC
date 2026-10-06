using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the rhythm game with the player's two arms as pointers (from game_bridge.py hands data).
///
/// The cursor is driven by ARM AIM (the shoulder->wrist direction), NOT the raw wrist pixel. So:
///   - reaching the arm UP moves the cursor to the top row,
///   - reaching OUT to the side moves it to that column,
///   - a FULL reach lands on the EDGE of the play area instead of flying off-screen,
///   - it works wherever the player stands (measured relative to the shoulder).
///
/// This is the therapeutic bit: hitting the outer cells requires actually extending the arm in that
/// direction. `aimRange` sets how far a reach must go to hit the edge — lower it for a patient with
/// limited range so a smaller reach still spans the grid. VALIDATE WITH A PHYSICAL THERAPIST.
///
/// Setup:
///   - PoseUdpReceiver + RhythmInputGrid in the scene.
///   - Optional: a full-screen RectTransform as `playArea` (else the whole screen is used), and two
///     UI Images as cursor visuals.
///   - Python: game_bridge.py --webcam --host 127.0.0.1 --stream-video --no-skeleton --mirror
/// </summary>
public class PoseHandCursors : MonoBehaviour
{
    [Header("Source / target")]
    [SerializeField] private PoseUdpReceiver receiver;
    [SerializeField] private RhythmInputGrid grid;

    [Header("Play area")]
    [Tooltip("Rect the cursors move within. Leave empty to use the whole screen (with margin).")]
    [SerializeField] private RectTransform playArea;
    [Tooltip("Screen margin in pixels when no playArea is set.")]
    [SerializeField] private float screenMargin = 80f;

    [Header("Aim mapping")]
    [Tooltip("Arm reach (torso-lengths) that maps to the edge of the play area. ~1.0 = a full " +
             "straight-arm reach hits the edge. Lower = smaller reach spans the grid (easier).")]
    [Range(0.4f, 1.5f)][SerializeField] private float aimRange = 0.95f;
    [Tooltip("Recentre the aim so a comfortable resting reach sits at the grid centre. " +
             "ay offset > 0 assumes the arm rests slightly downward.")]
    [SerializeField] private Vector2 aimCenter = new Vector2(0f, 0.15f);

    [Header("Cursor visuals (optional)")]
    [SerializeField] private RectTransform leftCursor;
    [SerializeField] private RectTransform rightCursor;
    [SerializeField] private bool hideCursorsWhenNoPose = true;
    [SerializeField] private Color reachingColor = new Color(0.2f, 1f, 0.4f, 0.85f);
    [SerializeField] private Color tuckedColor = new Color(1f, 0.7f, 0.2f, 0.55f);

    [Header("Therapy - extension gate")]
    [Tooltip("Require a minimum reach for a slot to count, so a tucked/bent arm doesn't score. " +
             "This is reach (|aim|), not raw elbow angle, so the centre cell stays reachable.")]
    [SerializeField] private bool requireReach = true;
    [Tooltip("Minimum reach (torso-lengths) to register a hit. ~0.5 keeps the centre reachable " +
             "while rejecting a fully tucked arm. Raise for a stricter extension demand.")]
    [Range(0.2f, 1.2f)][SerializeField] private float leftMinReach = 0.5f;
    [Range(0.2f, 1.2f)][SerializeField] private float rightMinReach = 0.5f;

    [Header("Tuning")]
    [SerializeField] private float smoothTime = 0.06f;

    private Vector2 leftScreen, rightScreen, leftVel, rightVel;
    private bool haveLeft, haveRight;
    private Image leftImage, rightImage;

    private void Awake()
    {
        if (receiver == null) receiver = FindFirstObjectByType<PoseUdpReceiver>();
        if (grid == null) grid = FindFirstObjectByType<RhythmInputGrid>();
        if (leftCursor != null) leftImage = leftCursor.GetComponent<Image>();
        if (rightCursor != null) rightImage = rightCursor.GetComponent<Image>();
    }

    private void Update()
    {
        if (receiver == null || grid == null) return;

        PoseStatePayload state = receiver.LatestState;
        bool tracked = state != null && state.pose_detected && state.hands != null;

        if (!tracked)
        {
            grid.SetHandHeldSlots(-1, -1);
            SetCursorVisible(leftCursor, false);
            SetCursorVisible(rightCursor, false);
            return;
        }

        Rect region = GetPlayRect();

        int leftSlot = ResolveHand(state.hands.left, leftMinReach, region,
                                   ref leftScreen, ref leftVel, ref haveLeft, leftCursor, leftImage);
        int rightSlot = ResolveHand(state.hands.right, rightMinReach, region,
                                    ref rightScreen, ref rightVel, ref haveRight, rightCursor, rightImage);

        grid.SetHandHeldSlots(leftSlot, rightSlot);
    }

    /// <summary>Map one arm's aim to a smoothed cursor inside the play area and return the slot it holds.</summary>
    private int ResolveHand(PoseHandPoint hand, float minReach, Rect region,
                            ref Vector2 screen, ref Vector2 vel, ref bool have, RectTransform cursor, Image image)
    {
        if (hand == null)
        {
            SetCursorVisible(cursor, false);
            return -1;
        }

        // aim (torso-lengths) -> 0..1 across the play area. aimRange sets how far a reach hits the edge.
        float u = 0.5f + 0.5f * Mathf.Clamp((hand.ax - aimCenter.x) / aimRange, -1f, 1f);
        float v = 0.5f + 0.5f * Mathf.Clamp((hand.ay - aimCenter.y) / aimRange, -1f, 1f); // ay up -> v up
        Vector2 rawScreen = new Vector2(
            Mathf.Lerp(region.xMin, region.xMax, u),
            Mathf.Lerp(region.yMin, region.yMax, v));   // screen y is up, v up -> top of area

        if (!have) { screen = rawScreen; have = true; }
        else { screen = Vector2.SmoothDamp(screen, rawScreen, ref vel, smoothTime); }

        bool reaching = !requireReach || hand.reach >= minReach;

        if (cursor != null)
        {
            cursor.position = screen;
            SetCursorVisible(cursor, true);
            if (image != null) image.color = reaching ? reachingColor : tuckedColor;
        }

        if (!reaching) return -1;                       // arm tucked/bent -> extend it to score
        return grid.GetSlotAtScreenPosition(screen);
    }

    private Rect GetPlayRect()
    {
        if (playArea != null)
        {
            Vector3[] c = new Vector3[4];
            playArea.GetWorldCorners(c);
            // corners are in world space; for an Overlay canvas that's already screen space.
            float xMin = Mathf.Min(c[0].x, c[2].x), xMax = Mathf.Max(c[0].x, c[2].x);
            float yMin = Mathf.Min(c[0].y, c[2].y), yMax = Mathf.Max(c[0].y, c[2].y);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }
        return Rect.MinMaxRect(screenMargin, screenMargin,
                               Screen.width - screenMargin, Screen.height - screenMargin);
    }

    private void SetCursorVisible(RectTransform cursor, bool visible)
    {
        if (cursor == null) return;
        bool show = visible || !hideCursorsWhenNoPose;
        if (cursor.gameObject.activeSelf != show)
        {
            cursor.gameObject.SetActive(show);
        }
    }
}
