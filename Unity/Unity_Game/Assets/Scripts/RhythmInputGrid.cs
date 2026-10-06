using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class RhythmInputGrid : MonoBehaviour
{
    public const int SlotCount = 9;

    [SerializeField] private GameObject[] slots = new GameObject[SlotCount];
    [SerializeField] private Camera inputCamera;
    [SerializeField] private LayerMask slotLayerMask = Physics.DefaultRaycastLayers;
    [SerializeField] private float raycastDistance = 100f;
    [SerializeField] private Color[] pressedColors = new Color[SlotCount]
    {
        Color.red,
        new Color(1f, 0.5f, 0f),
        Color.yellow,
        Color.green,
        Color.cyan,
        Color.blue,
        new Color(0.5f, 0f, 1f),
        Color.magenta,
        Color.white
    };
    [SerializeField] private float resetDelay = 0.08f;
    [SerializeField] private Color hitNeonColor = new Color(0.15f, 1f, 0.95f, 1f);
    [SerializeField] private Vector2 hitNeonThickness = new Vector2(5f, -5f);
    [SerializeField] private float hitNeonWorldWidth = 0.08f;
    [SerializeField] private float hitNeonWorldPadding = 0.08f;
    [SerializeField] private float hitNeonDurationSeconds = 0.16f;
    [SerializeField] private float hitNeonShimmerSpeed = 18f;
    [SerializeField, Range(0f, 1f)] private float hitNeonShimmerAmount = 0.45f;
    [SerializeField] private UnityEvent<int> onSlotPressed = new UnityEvent<int>();
    [SerializeField] private UnityEvent<int> onSlotHeld = new UnityEvent<int>();
    [SerializeField] private UnityEvent<int> onSlotReleased = new UnityEvent<int>();

    private readonly Color[] defaultColors = new Color[SlotCount];
    private readonly Outline[] hitNeonOutlines = new Outline[SlotCount];
    private readonly LineRenderer[] hitNeonLines = new LineRenderer[SlotCount];
    private readonly float[] hitNeonTimers = new float[SlotCount];
    private readonly System.Collections.Generic.List<RaycastResult> uiRaycastResults = new System.Collections.Generic.List<RaycastResult>();
    private Material hitNeonLineMaterial;
    private int activeSlotIndex = -1;

    // Slots currently held by the pose hand cursors (0-2). Separate from the mouse's activeSlotIndex
    // so both can work, and so two hands can hold two slots at once.
    private readonly System.Collections.Generic.HashSet<int> handHeldSlots = new System.Collections.Generic.HashSet<int>();
    private readonly System.Collections.Generic.HashSet<int> handHeldPrev = new System.Collections.Generic.HashSet<int>();

    public UnityEvent<int> OnSlotPressed => onSlotPressed;
    public UnityEvent<int> OnSlotHeld => onSlotHeld;
    public UnityEvent<int> OnSlotReleased => onSlotReleased;
    public int ActiveSlotIndex => activeSlotIndex;

    private void Awake()
    {
        EnsureInspectorArraySizes();
        AutoFillSlotsFromChildren();
        CacheDefaultColors();
    }

    private void Update()
    {
        UpdateMouseDragInput();
        UpdateHitNeonEffects();
    }

    private void OnDisable()
    {
        ReleaseActiveSlot();
    }

    private void UpdateMouseDragInput()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            ReleaseActiveSlot();
            return;
        }

        int hoveredSlotIndex = GetHoveredSlotIndex(mouse.position.ReadValue());
        if (hoveredSlotIndex != activeSlotIndex)
        {
            ReleaseActiveSlot();

            if (IsValidSlotIndex(hoveredSlotIndex))
            {
                activeSlotIndex = hoveredSlotIndex;
                PressSlot(activeSlotIndex);
            }
        }

        if (IsValidSlotIndex(activeSlotIndex))
        {
            HoldSlot(activeSlotIndex);
        }
    }

    private int GetHoveredSlotIndex(Vector2 screenPosition)
    {
        if (TryGetHoveredUiSlotIndex(screenPosition, out int uiSlotIndex))
        {
            return uiSlotIndex;
        }

        Camera rayCamera = inputCamera != null ? inputCamera : Camera.main;
        if (rayCamera == null)
        {
            return -1;
        }

        Ray ray = rayCamera.ScreenPointToRay(screenPosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, raycastDistance, slotLayerMask, QueryTriggerInteraction.Collide);
        int closestSlotIndex = -1;
        float closestDistance = float.PositiveInfinity;

        for (int hitIndex = 0; hitIndex < hits.Length; hitIndex++)
        {
            RaycastHit hit = hits[hitIndex];
            if (hit.distance >= closestDistance)
            {
                continue;
            }

            if (TryGetSlotIndex(hit.collider.gameObject, out int slotIndex))
            {
                closestSlotIndex = slotIndex;
                closestDistance = hit.distance;
            }
        }

        return closestSlotIndex;
    }

    private bool TryGetHoveredUiSlotIndex(Vector2 screenPosition, out int slotIndex)
    {
        slotIndex = -1;
        if (EventSystem.current == null)
        {
            return false;
        }

        PointerEventData pointerEventData = new PointerEventData(EventSystem.current)
        {
            position = screenPosition
        };

        uiRaycastResults.Clear();
        EventSystem.current.RaycastAll(pointerEventData, uiRaycastResults);

        for (int resultIndex = 0; resultIndex < uiRaycastResults.Count; resultIndex++)
        {
            GameObject hitObject = uiRaycastResults[resultIndex].gameObject;
            if (TryGetSlotIndex(hitObject, out slotIndex))
            {
                return true;
            }
        }

        return false;
    }

    private void ReleaseActiveSlot()
    {
        if (!IsValidSlotIndex(activeSlotIndex))
        {
            return;
        }

        int releasedSlotIndex = activeSlotIndex;
        activeSlotIndex = -1;
        ReleaseSlot(releasedSlotIndex);
    }

    public void PressSlot(int slotIndex)
    {
        if (!IsValidSlotIndex(slotIndex))
        {
            return;
        }

        SetSlotColor(slotIndex, pressedColors[slotIndex]);
        onSlotPressed.Invoke(slotIndex);
    }

    public void HoldSlot(int slotIndex)
    {
        if (!IsValidSlotIndex(slotIndex))
        {
            return;
        }

        onSlotHeld.Invoke(slotIndex);
    }

    public void ReleaseSlot(int slotIndex)
    {
        if (!IsValidSlotIndex(slotIndex))
        {
            return;
        }

        SetSlotColor(slotIndex, defaultColors[slotIndex]);
        onSlotReleased.Invoke(slotIndex);
    }

    public void PressSlot(GameObject slotObject)
    {
        int slotIndex = FindSlotIndex(slotObject);
        if (IsValidSlotIndex(slotIndex))
        {
            PressSlot(slotIndex);
        }
    }

    public void ReleaseSlot(GameObject slotObject)
    {
        int slotIndex = FindSlotIndex(slotObject);
        if (IsValidSlotIndex(slotIndex))
        {
            ReleaseSlot(slotIndex);
        }
    }

    public void HoldSlot(GameObject slotObject)
    {
        int slotIndex = FindSlotIndex(slotObject);
        if (IsValidSlotIndex(slotIndex))
        {
            HoldSlot(slotIndex);
        }
    }

    private void AutoFillSlotsFromChildren()
    {
        int childCount = Mathf.Min(transform.childCount, SlotCount);
        for (int childIndex = 0; childIndex < childCount; childIndex++)
        {
            GameObject child = transform.GetChild(childIndex).gameObject;
            if (!ContainsSlot(child, childIndex))
            {
                slots[childIndex] = child;
            }
        }
    }

    private void CacheDefaultColors()
    {
        for (int slotIndex = 0; slotIndex < SlotCount; slotIndex++)
        {
            defaultColors[slotIndex] = GetSlotColor(slots[slotIndex]);
        }
    }

    private int FindSlotIndex(GameObject slotObject)
    {
        if (slotObject == null)
        {
            return -1;
        }

        for (int slotIndex = 0; slotIndex < SlotCount; slotIndex++)
        {
            GameObject slot = slots[slotIndex];
            if (slot == null)
            {
                continue;
            }

            if (slot == slotObject || slotObject.transform.IsChildOf(slot.transform))
            {
                return slotIndex;
            }
        }

        return -1;
    }

    public bool TryGetSlotIndex(GameObject slotObject, out int slotIndex)
    {
        slotIndex = FindSlotIndex(slotObject);
        return IsValidSlotIndex(slotIndex);
    }

    public bool TryGetSlotTransform(int slotIndex, out Transform slotTransform)
    {
        slotTransform = null;
        if (!IsValidSlotIndex(slotIndex) || slots[slotIndex] == null)
        {
            return false;
        }

        slotTransform = slots[slotIndex].transform;
        return true;
    }

    public bool IsSlotHeld(int slotIndex)
    {
        return IsValidSlotIndex(slotIndex) && (slotIndex == activeSlotIndex || handHeldSlots.Contains(slotIndex));
    }

    /// <summary>Public wrapper so the pose hand cursors can resolve a screen point to a grid slot.</summary>
    public int GetSlotAtScreenPosition(Vector2 screenPosition)
    {
        return GetHoveredSlotIndex(screenPosition);
    }

    /// <summary>
    /// Set the slots held by the two pose hand cursors this frame (pass -1 for a hand that is off the
    /// grid). Fires press/hold/release just like the mouse so notes register and colours update.
    /// </summary>
    public void SetHandHeldSlots(int slotA, int slotB)
    {
        handHeldSlots.Clear();
        if (IsValidSlotIndex(slotA)) handHeldSlots.Add(slotA);
        if (IsValidSlotIndex(slotB)) handHeldSlots.Add(slotB);

        foreach (int slot in handHeldSlots)
        {
            if (!handHeldPrev.Contains(slot)) PressSlot(slot);
            HoldSlot(slot);
        }
        foreach (int slot in handHeldPrev)
        {
            if (!handHeldSlots.Contains(slot)) ReleaseSlot(slot);
        }

        handHeldPrev.Clear();
        handHeldPrev.UnionWith(handHeldSlots);
    }

    public void PlayHitNeon(int slotIndex)
    {
        if (!IsValidSlotIndex(slotIndex))
        {
            return;
        }

        bool playedEffect = false;
        Outline outline = GetOrCreateHitNeonOutline(slotIndex);
        if (outline != null)
        {
            outline.enabled = true;
            outline.effectColor = hitNeonColor;
            outline.effectDistance = hitNeonThickness;
            playedEffect = true;
        }

        LineRenderer lineRenderer = GetOrCreateHitNeonLine(slotIndex);
        if (lineRenderer != null)
        {
            PositionHitNeonLine(slotIndex, lineRenderer);
            lineRenderer.enabled = true;
            playedEffect = true;
        }

        if (playedEffect)
        {
            hitNeonTimers[slotIndex] = Mathf.Max(hitNeonTimers[slotIndex], hitNeonDurationSeconds);
        }
    }

    private void SetSlotColor(int slotIndex, Color color)
    {
        GameObject slot = slots[slotIndex];
        if (slot == null)
        {
            return;
        }

        if (slot.TryGetComponent(out Graphic graphic))
        {
            graphic.color = color;
            return;
        }

        if (slot.TryGetComponent(out SpriteRenderer spriteRenderer))
        {
            spriteRenderer.color = color;
            return;
        }

        if (slot.TryGetComponent(out Renderer renderer))
        {
            renderer.material.color = color;
        }
    }

    private void UpdateHitNeonEffects()
    {
        for (int slotIndex = 0; slotIndex < SlotCount; slotIndex++)
        {
            if (hitNeonTimers[slotIndex] <= 0f)
            {
                continue;
            }

            AnimateHitNeonEffect(slotIndex);
            hitNeonTimers[slotIndex] -= Time.deltaTime;
            if (hitNeonTimers[slotIndex] > 0f)
            {
                continue;
            }

            hitNeonTimers[slotIndex] = 0f;
            if (hitNeonOutlines[slotIndex] != null)
            {
                hitNeonOutlines[slotIndex].enabled = false;
            }

            if (hitNeonLines[slotIndex] != null)
            {
                hitNeonLines[slotIndex].enabled = false;
            }
        }
    }

    private void AnimateHitNeonEffect(int slotIndex)
    {
        float shimmer = 0.5f + 0.5f * Mathf.Sin(
            (Time.time * hitNeonShimmerSpeed) + slotIndex * 0.7f);
        float brightness = Mathf.Lerp(1f - hitNeonShimmerAmount, 1f, shimmer);
        Color shimmerColor = hitNeonColor;
        shimmerColor.a *= brightness;

        if (hitNeonOutlines[slotIndex] != null)
        {
            hitNeonOutlines[slotIndex].effectColor = shimmerColor;
            hitNeonOutlines[slotIndex].effectDistance = hitNeonThickness * brightness;
        }

        if (hitNeonLines[slotIndex] != null)
        {
            LineRenderer lineRenderer = hitNeonLines[slotIndex];
            lineRenderer.startColor = shimmerColor;
            lineRenderer.endColor = shimmerColor;
            lineRenderer.widthMultiplier = hitNeonWorldWidth * brightness;
        }
    }

    private Outline GetOrCreateHitNeonOutline(int slotIndex)
    {
        if (hitNeonOutlines[slotIndex] != null)
        {
            return hitNeonOutlines[slotIndex];
        }

        GameObject slot = slots[slotIndex];
        if (slot == null || !slot.TryGetComponent(out Graphic graphic))
        {
            return null;
        }

        Outline outline = slot.GetComponent<Outline>();
        if (outline == null)
        {
            outline = slot.AddComponent<Outline>();
        }

        outline.enabled = false;
        outline.useGraphicAlpha = false;
        outline.effectColor = hitNeonColor;
        outline.effectDistance = hitNeonThickness;
        hitNeonOutlines[slotIndex] = outline;
        return outline;
    }

    private LineRenderer GetOrCreateHitNeonLine(int slotIndex)
    {
        if (hitNeonLines[slotIndex] != null)
        {
            return hitNeonLines[slotIndex];
        }

        GameObject slot = slots[slotIndex];
        if (slot == null || GetSlotVisualRenderer(slot) == null)
        {
            return null;
        }

        GameObject lineObject = new GameObject("Hit Neon Border");
        lineObject.transform.SetParent(slot.transform, false);

        LineRenderer lineRenderer = lineObject.AddComponent<LineRenderer>();
        lineRenderer.enabled = false;
        lineRenderer.loop = true;
        lineRenderer.useWorldSpace = true;
        lineRenderer.positionCount = 4;
        lineRenderer.widthMultiplier = hitNeonWorldWidth;
        lineRenderer.numCornerVertices = 4;
        lineRenderer.numCapVertices = 4;
        lineRenderer.startColor = hitNeonColor;
        lineRenderer.endColor = hitNeonColor;
        lineRenderer.material = GetHitNeonLineMaterial();

        hitNeonLines[slotIndex] = lineRenderer;
        return lineRenderer;
    }

    private void PositionHitNeonLine(int slotIndex, LineRenderer lineRenderer)
    {
        Renderer slotRenderer = GetSlotVisualRenderer(slots[slotIndex]);
        if (slotRenderer == null)
        {
            return;
        }

        Transform visualTransform = slotRenderer.transform;
        Bounds localBounds = slotRenderer.localBounds;
        Vector3 localCenter = localBounds.center;
        Vector3 localExtents = localBounds.extents;
        Camera rayCamera = inputCamera != null ? inputCamera : Camera.main;

        Vector3 center = visualTransform.TransformPoint(localCenter);
        Vector3 cameraDirection = rayCamera != null
            ? rayCamera.transform.position - center
            : Vector3.forward;

        Vector3[] worldAxes =
        {
            visualTransform.right,
            visualTransform.up,
            visualTransform.forward
        };
        int faceAxis = GetLargestFacingAxis(worldAxes, cameraDirection);
        float faceSign = Vector3.Dot(worldAxes[faceAxis], cameraDirection) >= 0f ? 1f : -1f;
        Vector3 localNormal = GetAxisVector(faceAxis) * faceSign;
        Vector3 normal = worldAxes[faceAxis] * faceSign;

        int axisAIndex = (faceAxis + 1) % 3;
        int axisBIndex = (faceAxis + 2) % 3;
        Vector3 localAxisA = GetAxisVector(axisAIndex);
        Vector3 localAxisB = GetAxisVector(axisBIndex);
        float halfA = GetAxisValue(localExtents, axisAIndex);
        float halfB = GetAxisValue(localExtents, axisBIndex);
        Vector3 localFaceCenter = localCenter + localNormal * GetAxisValue(localExtents, faceAxis);

        lineRenderer.widthMultiplier = hitNeonWorldWidth;
        lineRenderer.startColor = hitNeonColor;
        lineRenderer.endColor = hitNeonColor;
        lineRenderer.SetPosition(0, visualTransform.TransformPoint(localFaceCenter - localAxisA * halfA - localAxisB * halfB) + normal * hitNeonWorldPadding);
        lineRenderer.SetPosition(1, visualTransform.TransformPoint(localFaceCenter + localAxisA * halfA - localAxisB * halfB) + normal * hitNeonWorldPadding);
        lineRenderer.SetPosition(2, visualTransform.TransformPoint(localFaceCenter + localAxisA * halfA + localAxisB * halfB) + normal * hitNeonWorldPadding);
        lineRenderer.SetPosition(3, visualTransform.TransformPoint(localFaceCenter - localAxisA * halfA + localAxisB * halfB) + normal * hitNeonWorldPadding);
    }

    private Material GetHitNeonLineMaterial()
    {
        if (hitNeonLineMaterial != null)
        {
            return hitNeonLineMaterial;
        }

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        hitNeonLineMaterial = new Material(shader);
        hitNeonLineMaterial.color = hitNeonColor;
        return hitNeonLineMaterial;
    }

    private static Renderer GetSlotVisualRenderer(GameObject slot)
    {
        if (slot == null)
        {
            return null;
        }

        Renderer[] renderers = slot.GetComponentsInChildren<Renderer>();
        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            Renderer renderer = renderers[rendererIndex];
            if (renderer is LineRenderer)
            {
                continue;
            }

            return renderer;
        }

        return null;
    }

    private static int GetLargestFacingAxis(Vector3[] axes, Vector3 direction)
    {
        if (direction.sqrMagnitude <= Mathf.Epsilon)
        {
            return 0;
        }

        direction.Normalize();
        int bestAxis = 0;
        float bestAlignment = Mathf.Abs(Vector3.Dot(axes[0], direction));
        for (int axisIndex = 1; axisIndex < axes.Length; axisIndex++)
        {
            float alignment = Mathf.Abs(Vector3.Dot(axes[axisIndex], direction));
            if (alignment > bestAlignment)
            {
                bestAxis = axisIndex;
                bestAlignment = alignment;
            }
        }

        return bestAxis;
    }

    private static Vector3 GetAxisVector(int axis)
    {
        switch (axis)
        {
            case 0:
                return Vector3.right;
            case 1:
                return Vector3.up;
            default:
                return Vector3.forward;
        }
    }

    private static float GetAxisValue(Vector3 vector, int axis)
    {
        switch (axis)
        {
            case 0:
                return vector.x;
            case 1:
                return vector.y;
            default:
                return vector.z;
        }
    }

    private static Color GetSlotColor(GameObject slot)
    {
        if (slot == null)
        {
            return Color.white;
        }

        if (slot.TryGetComponent(out Graphic graphic))
        {
            return graphic.color;
        }

        if (slot.TryGetComponent(out SpriteRenderer spriteRenderer))
        {
            return spriteRenderer.color;
        }

        if (slot.TryGetComponent(out Renderer renderer))
        {
            return renderer.sharedMaterial != null ? renderer.sharedMaterial.color : Color.white;
        }

        return Color.white;
    }

    private bool ContainsSlot(GameObject slotObject, int slotsToCheck)
    {
        for (int slotIndex = 0; slotIndex < slotsToCheck; slotIndex++)
        {
            if (slots[slotIndex] == slotObject)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsValidSlotIndex(int slotIndex)
    {
        return slotIndex >= 0 && slotIndex < SlotCount;
    }

    private void Reset()
    {
        EnsureInspectorArraySizes();
        AutoFillSlotsFromChildren();
    }

    private void OnValidate()
    {
        EnsureInspectorArraySizes();
        resetDelay = Mathf.Max(0f, resetDelay);
        raycastDistance = Mathf.Max(0f, raycastDistance);
        hitNeonWorldWidth = Mathf.Max(0.001f, hitNeonWorldWidth);
        hitNeonWorldPadding = Mathf.Max(0f, hitNeonWorldPadding);
        hitNeonDurationSeconds = Mathf.Max(0.01f, hitNeonDurationSeconds);
        hitNeonShimmerSpeed = Mathf.Max(0f, hitNeonShimmerSpeed);
        hitNeonShimmerAmount = Mathf.Clamp01(hitNeonShimmerAmount);
    }

    private void EnsureInspectorArraySizes()
    {
        if (slots == null || slots.Length != SlotCount)
        {
            GameObject[] resizedSlots = new GameObject[SlotCount];
            if (slots != null)
            {
                for (int i = 0; i < Mathf.Min(slots.Length, SlotCount); i++)
                {
                    resizedSlots[i] = slots[i];
                }
            }

            slots = resizedSlots;
        }

        if (pressedColors == null || pressedColors.Length != SlotCount)
        {
            Color[] resizedColors = new Color[SlotCount];
            for (int i = 0; i < SlotCount; i++)
            {
                resizedColors[i] = i < pressedColors?.Length ? pressedColors[i] : Color.white;
            }

            pressedColors = resizedColors;
        }
    }
}
