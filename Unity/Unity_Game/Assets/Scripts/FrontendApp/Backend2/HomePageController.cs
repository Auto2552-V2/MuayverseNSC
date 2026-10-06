using UnityEngine;
using TMPro;

// แสดงชื่อผู้ใช้ที่ Username_Display บนหน้า Home
// อ่านค่าจาก PlayerPrefs ("userName") ที่ Signup/Login บันทึกไว้ตอนสำเร็จ
public class HomePageController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_Text usernameDisplay;      // ลาก Username_Display มาใส่

    [Header("Options")]
    [SerializeField] private string prefix = "";            // เช่น "Welcome, " (เว้นว่างได้)
    [SerializeField] private string fallbackName = "Guest"; // ใช้เมื่อไม่มีชื่อเก็บไว้

    // เรียกทุกครั้งที่หน้า Home ถูกเปิด -> ชื่ออัปเดตเสมอ
    private void OnEnable()
    {
        RefreshUsername();
    }

    public void RefreshUsername()
    {
        if (usernameDisplay == null) return;

        string name = PlayerPrefs.GetString("userName", "");
        if (string.IsNullOrEmpty(name)) name = fallbackName;

        usernameDisplay.text = prefix + name;
    }
}
