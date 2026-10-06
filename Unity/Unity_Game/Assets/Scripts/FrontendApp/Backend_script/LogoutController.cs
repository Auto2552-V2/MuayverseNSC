using UnityEngine;
using UnityEngine.UI;

// ปุ่ม Logout ในหน้า Profile_Page: ล้าง session -> กลับไปหน้า Signin
//
// วิธีใช้ (เลือกทางใดทางหนึ่ง):
//   ก. แปะ component นี้บน "Logout (button)" เอง แล้วเว้นช่อง Logout Button ไว้
//      มันจะหา Button บนตัวเองอัตโนมัติ
//   ข. แปะบน GameObject ไหนก็ได้ที่ active อยู่ แล้วลากปุ่มใส่ช่อง Logout Button
//
// ช่อง Signin Page ต้องลากใส่เสมอ ส่วน Page Manager เว้นว่างได้ (จะหาในซีนเอง)
public class LogoutController : MonoBehaviour
{
    [Header("ปุ่ม (เว้นว่าง = ใช้ Button บน GameObject นี้)")]
    [SerializeField] private Button logoutButton;

    [Header("Navigation")]
    [Tooltip("เว้นว่างได้ — จะหา PageManager ในซีนให้เอง")]
    [SerializeField] private PageManager pageManager;
    [Tooltip("หน้าที่จะไปหลัง logout (ลาก Signin_Page ใส่)")]
    [SerializeField] private GameObject signinPage;

    [Header("หน้าที่ต้องปิดถ้าไม่ได้ใช้ PageManager")]
    [Tooltip("ใช้เฉพาะตอนหา PageManager ไม่เจอ — ลาก Profile_Page ใส่")]
    [SerializeField] private GameObject profilePage;

    private void Awake()
    {
        if (logoutButton == null) logoutButton = GetComponent<Button>();
        if (logoutButton != null) logoutButton.onClick.AddListener(Logout);
    }

    public void Logout()
    {
        // ล้างก่อนเปลี่ยนหน้าเสมอ — ถ้าเปลี่ยนหน้าก่อนแล้วล้างทีหลัง
        // ProfileEditor.OnEnable ของหน้าถัดไปอาจอ่านค่าเก่าไปแล้ว
        UserSession.Clear();

        GoToSignin();
    }

    private void GoToSignin()
    {
        if (signinPage == null)
        {
            Debug.LogWarning("LogoutController: ยังไม่ได้ลาก Signin_Page ใส่ — ล้าง session แล้วแต่ไม่ได้เปลี่ยนหน้า");
            return;
        }

        // ผ่าน PageManager เสมอถ้าหาได้ เพราะมันเป็นคนสั่งซ่อนแถบเมนูล่าง
        // ถ้าสลับหน้าด้วย SetActive ตรง ๆ แถบเมนูจะค้างโชว์อยู่บนหน้า Signin
        PageManager manager = pageManager != null
            ? pageManager
            : FindFirstObjectByType<PageManager>();

        if (manager != null)
        {
            manager.ShowPage(signinPage);
            return;
        }

        // สำรองสุดท้าย: ไม่มี PageManager ในซีนเลย
        if (profilePage != null) profilePage.SetActive(false);
        signinPage.SetActive(true);
    }
}
