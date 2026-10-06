using UnityEngine;

// โมดูลเก็บค่าตั้งคา FOV effect — ใช้ซ้ำได้เหมือน CameraShakeProfile
[System.Serializable]
public class CameraFovProfile
{
    [SerializeField][Min(0f)] private float duration = 0.2f; // ระยะเวลา effect (วินาที)
    [SerializeField] private float fovDelta = -6f; // เปลี่ยน FOV จากค่าปกติ (ติดลบ = zoom เข้า, บวก = zoom ออก)
    [SerializeField] private AnimationCurve intensityCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.2f, 1f),
        new Keyframe(1f, 0f)
    ); // กราฟความแรงตามเวลา (0–1) — ค่า 1 = ถึง fovDelta เต็มที่

    public float Duration => duration;
    public float FovDelta => fovDelta;
    public AnimationCurve IntensityCurve => intensityCurve;

    public bool IsValid => duration > 0f && Mathf.Abs(fovDelta) > 0.01f;

    public void SetValues(float newDuration, float newFovDelta)
    {
        duration = Mathf.Max(0f, newDuration);
        fovDelta = newFovDelta;
    }
}
