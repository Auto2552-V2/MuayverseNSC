using UnityEngine;
using System.Collections;

// แนบบน Main Camera แล้วเรียก CameraFovEffect.Instance.Play() จากสคริปต์อื่นได้
[RequireComponent(typeof(Camera))]
public class CameraFovEffect : MonoBehaviour
{
    public static CameraFovEffect Instance { get; private set; }

    [Header("Default Profile")]
    [SerializeField] private CameraFovProfile defaultProfile = new CameraFovProfile();

    private Camera targetCamera;
    private float baseFov;
    private Coroutine activeCoroutine;

    public float BaseFov => baseFov;
    public float CurrentFov => targetCamera != null ? targetCamera.fieldOfView : 0f;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[CameraFovEffect] พบ CameraFovEffect ซ้ำ — ลบตัวที่ซ้ำออก");
            Destroy(this);
            return;
        }

        Instance = this;
        targetCamera = GetComponent<Camera>();
        baseFov = targetCamera.fieldOfView;
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// เล่น FOV effect ด้วยโปรไฟล์เริ่มต้น
    /// </summary>
    public void Play()
    {
        Play(defaultProfile);
    }

    /// <summary>
    /// เล่น FOV effect ด้วยโปรไฟล์ที่กำหนด
    /// </summary>
    public void Play(CameraFovProfile profile)
    {
        if (profile == null || !profile.IsValid) return;
        Play(profile.Duration, profile.FovDelta, profile.IntensityCurve);
    }

    /// <summary>
    /// เล่น FOV effect ด้วยค่าที่กำหนดเอง
    /// </summary>
    public void Play(float duration, float fovDelta, AnimationCurve intensityCurve = null)
    {
        if (duration <= 0f || Mathf.Abs(fovDelta) <= 0.01f) return;

        if (activeCoroutine != null)
        {
            StopCoroutine(activeCoroutine);
            RestoreFov();
        }

        activeCoroutine = StartCoroutine(FovRoutine(duration, fovDelta, intensityCurve ?? AnimationCurve.Linear(0f, 0f, 1f, 1f)));
    }

    /// <summary>
    /// เรียกจากสคริปต์อื่นโดยไม่ต้องเก็บ reference
    /// </summary>
    public static void PlayGlobal(CameraFovProfile profile)
    {
        if (Instance == null)
        {
            Debug.LogWarning("[CameraFovEffect] ไม่พบ CameraFovEffect ในฉาก — แนบสคริปต์บน Main Camera");
            return;
        }

        Instance.Play(profile);
    }

    /// <summary>
    /// กำหนด FOV ฐานใหม่ (เช่น ตอนเปลี่ยนมุมมองกล้อง)
    /// </summary>
    public void SetBaseFov(float fov)
    {
        baseFov = Mathf.Clamp(fov, 1f, 179f);
        if (activeCoroutine == null)
        {
            targetCamera.fieldOfView = baseFov;
        }
    }

    /// <summary>
    /// คืน FOV กลับค่าปกติทันที
    /// </summary>
    public void RestoreFov()
    {
        if (targetCamera != null)
        {
            targetCamera.fieldOfView = baseFov;
        }
    }

    private IEnumerator FovRoutine(float duration, float fovDelta, AnimationCurve intensityCurve)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float intensity = intensityCurve.Evaluate(t);
            targetCamera.fieldOfView = baseFov + fovDelta * intensity;
            yield return null;
        }

        RestoreFov();
        activeCoroutine = null;
    }
}
