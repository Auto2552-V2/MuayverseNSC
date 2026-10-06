using UnityEngine;
using System.Collections;

public class IFrameController : MonoBehaviour
{
    [Header("Player Reference")]
    [SerializeField] private Health playerHealth;

    [Header("Visual Feedback Settings")]
    [SerializeField] private bool useFlickerEffect = true;
    [SerializeField] private float flickerInterval = 0.1f;

    private Renderer[] characterRenderers;
    private Coroutine flickerCoroutine;

    void Awake()
    {
        InitializePlayerReferences();
    }

    private void InitializePlayerReferences()
    {
        if (playerHealth == null)
        {
            // พยายามหา Player จาก Tag หรือสคริปต์ PlayerMovement
            GameObject playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null)
            {
                playerHealth = playerObj.GetComponent<Health>();
            }
            else
            {
                PlayerMovement playerMovement = FindFirstObjectByType<PlayerMovement>();
                if (playerMovement != null)
                {
                    playerHealth = playerMovement.GetComponent<Health>();
                }
            }
        }

        if (playerHealth != null)
        {
            // ดึง Renderers ทั้งหมดของตัวละครผู้เล่น (รวมถึง GameObject ลูกๆ เช่น โมเดล 3D)
            characterRenderers = playerHealth.GetComponentsInChildren<Renderer>();
        }
        else
        {
            Debug.LogWarning("[IFrameController] ไม่พบข้อมูล Health ของผู้เล่น! กรุณาลากใส่ใน Inspector ของ GameController");
        }
    }

    /// <summary>
    /// กำหนดสถานะอมตะ (I-Frame) ของผู้เล่น
    /// </summary>
    public void SetInvincible(bool state)
    {
        // เผื่อกรณี Renderers ยังไม่ได้โหลด หรือตัวละครถูกสร้างขึ้นใหม่
        if (characterRenderers == null || characterRenderers.Length == 0 || playerHealth == null)
        {
            InitializePlayerReferences();
        }

        if (playerHealth != null)
        {
            playerHealth.IsInvincible = state;
        }

        if (state)
        {
            if (useFlickerEffect)
            {
                // หยุด Coroutine เดิมหากมีอยู่
                if (flickerCoroutine != null)
                {
                    StopCoroutine(flickerCoroutine);
                }
                flickerCoroutine = StartCoroutine(FlickerRoutine());
            }
        }
        else
        {
            if (flickerCoroutine != null)
            {
                StopCoroutine(flickerCoroutine);
                flickerCoroutine = null;
            }
            SetRenderersEnabled(true);
        }
    }

    private IEnumerator FlickerRoutine()
    {
        while (true)
        {
            SetRenderersEnabled(false);
            yield return new WaitForSeconds(flickerInterval);
            SetRenderersEnabled(true);
            yield return new WaitForSeconds(flickerInterval);
        }
    }

    private void SetRenderersEnabled(bool enabled)
    {
        if (characterRenderers == null) return;
        
        foreach (var r in characterRenderers)
        {
            if (r != null)
            {
                r.enabled = enabled;
            }
        }
    }
}
