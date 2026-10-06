using UnityEngine;
using System.Collections;

// แนบบน Main Camera แล้วเรียก CameraShake.Instance.Shake() จากสคริปต์อื่นได้
public class CameraShake : MonoBehaviour
{
    public static CameraShake Instance { get; private set; }

    [Header("Default Profile")]
    [SerializeField] private CameraShakeProfile defaultProfile = new CameraShakeProfile();

    [Header("Shake Settings")]
    [SerializeField][Min(1f)] private float frequency = 25f; // ความถี่ Perlin noise (ยิ่งสูงยิ่งสั่นเร็ว)

    private Transform camTransform;
    private Vector3 originalLocalPosition;
    private Quaternion originalLocalRotation;
    private Coroutine activeShakeCoroutine;
    private float noiseSeed;
    private float activeMagnitude;
    private float activeRotationMagnitude;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[CameraShake] พบ CameraShake ซ้ำ — ลบตัวที่ซ้ำออก");
            Destroy(this);
            return;
        }

        Instance = this;
        camTransform = transform;
        CacheOriginalTransform();
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    void OnDisable()
    {
        if (activeShakeCoroutine != null)
        {
            StopCoroutine(activeShakeCoroutine);
            activeShakeCoroutine = null;
            ResetTransform();
        }

        activeMagnitude = 0f;
        activeRotationMagnitude = 0f;
    }

    void CacheOriginalTransform()
    {
        originalLocalPosition = camTransform.localPosition;
        originalLocalRotation = camTransform.localRotation;
    }

    /// <summary>
    /// สั่นกล้องด้วยโปรไฟล์เริ่มต้น
    /// </summary>
    public void Shake()
    {
        Shake(defaultProfile);
    }

    /// <summary>
    /// สั่นกล้องด้วยโปรไฟล์ที่กำหนด
    /// </summary>
    public void Shake(CameraShakeProfile profile)
    {
        if (profile == null || !profile.IsValid) return;
        Shake(profile.Duration, profile.Magnitude, profile.RotationMagnitude);
    }

    /// <summary>
    /// สั่นกล้องด้วยค่าที่กำหนดเอง
    /// </summary>
    public void Shake(float duration, float magnitude, float rotationMagnitude = 0f)
    {
        if (duration <= 0f || (magnitude <= 0f && rotationMagnitude <= 0f)) return;

        magnitude = Mathf.Max(0f, magnitude);
        rotationMagnitude = Mathf.Max(0f, rotationMagnitude);

        // ถ้ากำลังสั่นอยู่แล้ว ให้เมินเฉพาะการสั่นใหม่ที่อ่อนกว่า
        if (activeShakeCoroutine != null && IsIncomingShakeWeaker(magnitude, rotationMagnitude))
        {
            return;
        }

        if (activeShakeCoroutine != null)
        {
            StopCoroutine(activeShakeCoroutine);
            ResetTransform();
        }

        CacheOriginalTransform();
        activeMagnitude = magnitude;
        activeRotationMagnitude = rotationMagnitude;
        noiseSeed = Random.Range(0f, 100f);
        activeShakeCoroutine = StartCoroutine(ShakeRoutine(duration, magnitude, rotationMagnitude));
    }

    /// <summary>
    /// เรียกจากสคริปต์อื่นโดยไม่ต้องเก็บ reference — หา Instance อัตโนมัติ
    /// </summary>
    public static void ShakeGlobal(CameraShakeProfile profile)
    {
        if (Instance == null)
        {
            Debug.LogWarning("[CameraShake] ไม่พบ CameraShake ในฉาก — แนบสคริปต์บน Main Camera");
            return;
        }

        Instance.Shake(profile);
    }

    private IEnumerator ShakeRoutine(float duration, float magnitude, float rotationMagnitude)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float damp = 1f - Mathf.Clamp01(elapsed / duration);

            float offsetX = (Mathf.PerlinNoise(noiseSeed, Time.time * frequency) * 2f - 1f) * magnitude * damp;
            float offsetY = (Mathf.PerlinNoise(noiseSeed + 1f, Time.time * frequency) * 2f - 1f) * magnitude * damp;
            camTransform.localPosition = originalLocalPosition + new Vector3(offsetX, offsetY, 0f);

            if (rotationMagnitude > 0f)
            {
                float roll = (Mathf.PerlinNoise(noiseSeed + 2f, Time.time * frequency) * 2f - 1f) * rotationMagnitude * damp;
                camTransform.localRotation = originalLocalRotation * Quaternion.Euler(0f, 0f, roll);
            }

            yield return null;
        }

        ResetTransform();
        activeShakeCoroutine = null;
        activeMagnitude = 0f;
        activeRotationMagnitude = 0f;
    }

    private bool IsIncomingShakeWeaker(float magnitude, float rotationMagnitude)
    {
        return magnitude <= activeMagnitude &&
               rotationMagnitude <= activeRotationMagnitude &&
               (magnitude < activeMagnitude || rotationMagnitude < activeRotationMagnitude);
    }

    private void ResetTransform()
    {
        if (camTransform == null) return;

        camTransform.localPosition = originalLocalPosition;
        camTransform.localRotation = originalLocalRotation;
    }
}
