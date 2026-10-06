using UnityEngine;
using UnityEngine.UI;

public class HealthBarUI : MonoBehaviour
{
    [Header("Target Settings")]
    [SerializeField] private Health targetHealth; // ดึงข้อมูลเลือดจาก Health Script
    
    [Header("UI Elements")]
    [SerializeField] private Image healthFillImage; // สำหรับแบบใช้ Image (Image Type = Filled)
    [SerializeField] private Slider healthSlider;     // สำหรับแบบใช้ Slider (สามารถเลือกใช้อย่างใดอย่างหนึ่งได้)

    private Camera mainCamera;

    void Start()
    {
        mainCamera = Camera.main;

        // ถ้าไม่ได้ลากใส่มา ให้ดึงจาก Parent อัตโนมัติ
        if (targetHealth == null)
        {
            targetHealth = GetComponentInParent<Health>();
        }

        if (targetHealth != null)
        {
            // รับฟังก์ชันเมื่อเลือดเปลี่ยนแปลง
            targetHealth.OnHealthChanged.AddListener(UpdateHealthBar);
            
            // อัปเดต UI ครั้งแรกตอนเริ่มเกม
            UpdateHealthBar(targetHealth.CurrentHealth);
        }
        else
        {
            Debug.LogError("ไม่พบสคริปต์ Health ใน Parent Object!");
        }
    }

    void LateUpdate()
    {
        // ทำ Billboard: บังคับให้ UI หันหน้ามาหากล้องของเกมเสมอ
        if (mainCamera != null)
        {
            transform.LookAt(transform.position + mainCamera.transform.rotation * Vector3.forward,
                             mainCamera.transform.rotation * Vector3.up);
        }
    }

    // ฟังก์ชันที่จะถูกเรียกผ่าน Event เพื่ออัปเดต UI เลือด
    public void UpdateHealthBar(float currentHealth)
    {
        if (targetHealth == null) return;

        float fillValue = targetHealth.HealthPercentage; // ดึงค่าเปอร์เซ็นต์เลือด (0 ถึง 1)

        if (healthFillImage != null)
        {
            healthFillImage.fillAmount = fillValue;
        }

        if (healthSlider != null)
        {
            healthSlider.value = fillValue;
        }
    }

    private void OnDestroy()
    {
        if (targetHealth != null)
        {
            targetHealth.OnHealthChanged.RemoveListener(UpdateHealthBar);
        }
    }
}
