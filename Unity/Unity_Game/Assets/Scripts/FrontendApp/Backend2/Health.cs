using UnityEngine;
using UnityEngine.Events;

public class Health : MonoBehaviour
{
    [Header("Health Settings")]
    [SerializeField] private float maxHealth = 100f;
    private float currentHealth;
    private bool isDead = false;

    [Header("Invincibility Settings")]
    [SerializeField] private bool isInvincible = false;

    // Property สำหรับตรวจสอบหรือกำหนดสถานะอมตะ (I-Frame)
    public bool IsInvincible
    {
        get => isInvincible;
        set => isInvincible = value;
    }

    [Header("Events")]
    // คอยส่งสัญญาณให้ระบบอื่นๆ รับรู้ (เช่น เล่นอนิเมชั่นโดนโจมตี หรือ อัปเดตเลือดบน UI)
    public UnityEvent<float> OnHealthChanged; // แจ้งเมื่อเลือดปัจจุบันเปลี่ยน (ส่งค่าเลือดปัจจุบันไป)
    public UnityEvent<float> OnTakeDamage;    // แจ้งเมื่อโดนความเสียหาย (ส่งค่าความเสียหายไป)
    public UnityEvent OnDeath;                 // แจ้งเมื่อตัวละครตัวนี้ตาย

    // Properties สำหรับดึงค่าไปใช้งานในสคริปต์อื่น
    public float MaxHealth => maxHealth;
    public float CurrentHealth => currentHealth;
    public bool IsDead => isDead;
    public float HealthPercentage => maxHealth > 0f ? currentHealth / maxHealth : 0f;

    void Awake()
    {
        // กำหนดให้เลือดเริ่มต้นเต็มหลอดใน Awake เพื่อให้พร้อมใช้งานก่อน Start ของสคริปต์อื่น
        currentHealth = maxHealth;
    }

    /// <summary>
    /// รับความเสียหาย (ลด HP)
    /// </summary>
    /// <param name="damageAmount">จำนวนความเสียหาย</param>
    public void TakeDamage(float damageAmount)
    {
        if (isDead) return;

        if (isInvincible)
        {
            Debug.Log($"{gameObject.name} หลบได้ (อยู่ในสถานะอมตะ/I-Frame)!");
            return;
        }

        damageAmount = Mathf.Max(0f, damageAmount); // ป้องกันกรณีค่าติดลบ
        currentHealth -= damageAmount;
        currentHealth = Mathf.Max(0f, currentHealth); // ไม่ให้เลือดติดลบต่ำกว่า 0

        Debug.Log($"{gameObject.name} ได้รับความเสียหาย: {damageAmount} | เลือดคงเหลือ: {currentHealth}/{maxHealth}");

        OnHealthChanged?.Invoke(currentHealth);
        OnTakeDamage?.Invoke(damageAmount);

        // เช็คว่าเลือดหมดหรือไม่
        if (currentHealth <= 0f)
        {
            Die();
        }
    }

    /// <summary>
    /// ฟื้นฟูพลังชีวิต (เพิ่ม HP)
    /// </summary>
    /// <param name="healAmount">จำนวนที่ฟื้นฟู</param>
    public void Heal(float healAmount)
    {
        if (isDead) return;

        healAmount = Mathf.Max(0f, healAmount); // ป้องกันกรณีค่าติดลบ
        currentHealth += healAmount;
        currentHealth = Mathf.Min(currentHealth, maxHealth); // ไม่ให้เลือดเกินค่าสูงสุด

        Debug.Log($"{gameObject.name} ได้รับการฟื้นฟู: {healAmount} | เลือดคงเหลือ: {currentHealth}/{maxHealth}");

        OnHealthChanged?.Invoke(currentHealth);
    }

    /// <summary>
    /// ทำงานเมื่อตัวละครตาย
    /// </summary>
    private void Die()
    {
        isDead = true;
        Debug.Log($"{gameObject.name} ได้เสียชีวิตลงแล้ว!");
        
        // ส่งสัญญาณตายออกไป
        OnDeath?.Invoke();

        // สามารถเพิ่มโค้ดที่ต้องการให้ทำตอนตายตรงนี้ได้เลย เช่น:
        // - สั่งทำลายวัตถุ: Destroy(gameObject, 2f);
        // - เล่นอนิเมชั่นตาย: GetComponent<Animator>().SetTrigger("Die");
    }
}
