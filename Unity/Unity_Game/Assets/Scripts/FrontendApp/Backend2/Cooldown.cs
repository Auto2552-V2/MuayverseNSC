using UnityEngine;

[System.Serializable]
public class Cooldown
{
    [SerializeField] private float cooldownTime = 1f; // ระยะเวลาคูลดาวน์ (วินาที)
    private float nextReadyTime = 0f; // เวลาที่จะพร้อมใช้งานครั้งต่อไป

    public float CooldownTime => cooldownTime;

    // ตรวจสอบว่าพร้อมใช้งานหรือไม่ (คูลดาวน์เสร็จแล้วหรือยัง)
    public bool IsReady => Time.time >= nextReadyTime;

    // ส่งค่าระยะเวลาคูลดาวน์คงเหลือ (เป็นวินาที)
    public float TimeRemaining => Mathf.Max(0f, nextReadyTime - Time.time);

    // ส่งค่าอัตราส่วนคูลดาวน์คงเหลือ (0 ถึง 1 สำหรับเอาไปทำ UI Progress Bar: 1 = คูลดาวน์เต็ม, 0 = พร้อมใช้)
    public float Progress => cooldownTime > 0f ? Mathf.Clamp01(TimeRemaining / cooldownTime) : 0f;

    // เริ่มคูลดาวน์ทันที
    public void StartCooldown()
    {
        nextReadyTime = Time.time + cooldownTime;
    }

    // กำหนดเวลาคูลดาวน์ใหม่ผ่านโค้ด
    public void SetCooldownTime(float time)
    {
        cooldownTime = time;
    }
}
