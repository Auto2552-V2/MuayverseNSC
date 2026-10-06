using UnityEngine;

// โมดูลเก็บค่าตั้งคามกล้องสั่น — ใช้ซ้ำได้เหมือน Cooldown (ลากใส่ Inspector หรืออ้างอิงจากสคริปต์อื่น)
[System.Serializable]
public class CameraShakeProfile
{
    [SerializeField][Min(0f)] private float duration = 0.25f; // ระยะเวลาสั่น (วินาที)
    [SerializeField][Min(0f)] private float magnitude = 0.5f; // ความแรงการสั่น (หน่วย Unity)
    [SerializeField][Min(0f)] private float rotationMagnitude = 4f; // ความแรงการหมุนกล้อง (องศา) — 0 = ไม่หมุน

    public float Duration => duration;
    public float Magnitude => magnitude;
    public float RotationMagnitude => rotationMagnitude;

    public bool IsValid => duration > 0f && (magnitude > 0f || rotationMagnitude > 0f);

    public void SetValues(float newDuration, float newMagnitude, float newRotationMagnitude = 0f)
    {
        duration = Mathf.Max(0f, newDuration);
        magnitude = Mathf.Max(0f, newMagnitude);
        rotationMagnitude = Mathf.Max(0f, newRotationMagnitude);
    }
}
