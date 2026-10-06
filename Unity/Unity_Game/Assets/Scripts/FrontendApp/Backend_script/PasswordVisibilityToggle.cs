using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ปุ่มรูปตา กดเปิด/ปิดการมองเห็นรหัสผ่าน
// - ปิด (default) -> แสดงเป็น ● (ContentType.Password)
// - เปิด           -> แสดงตัวอักษรจริง (ContentType.Standard)
public class PasswordVisibilityToggle : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TMP_InputField passwordInput;   // ลาก Password_input มาใส่
    [SerializeField] private Button toggleButton;            // ปุ่มรูปตา (ลากปุ่มมาใส่)

    [Header("Icon (optional)")]
    [SerializeField] private Image iconImage;               // รูปไอคอนบนปุ่ม (ไม่ใส่ก็ได้)
    [SerializeField] private Sprite eyeOpenSprite;          // ไอคอนตอน "เห็นรหัส"
    [SerializeField] private Sprite eyeClosedSprite;        // ไอคอนตอน "ซ่อนรหัส"

    private bool isVisible = false;

    private void Awake()
    {
        // เริ่มต้นให้ซ่อนรหัสเสมอ
        ApplyVisibility();

        if (toggleButton != null)
            toggleButton.onClick.AddListener(Toggle);
    }

    // สลับสถานะเปิด/ปิด
    public void Toggle()
    {
        isVisible = !isVisible;
        ApplyVisibility();
    }

    private void ApplyVisibility()
    {
        if (passwordInput != null)
        {
            passwordInput.contentType = isVisible
                ? TMP_InputField.ContentType.Standard
                : TMP_InputField.ContentType.Password;

            // บังคับให้ช่อง input วาดใหม่ ไม่งั้นตัวอักษรจะไม่อัปเดตทันที
            passwordInput.ForceLabelUpdate();
        }

        // สลับไอคอน (ถ้ามีตั้งค่าไว้)
        if (iconImage != null)
        {
            if (isVisible && eyeOpenSprite != null) iconImage.sprite = eyeOpenSprite;
            else if (!isVisible && eyeClosedSprite != null) iconImage.sprite = eyeClosedSprite;
        }
    }
}
