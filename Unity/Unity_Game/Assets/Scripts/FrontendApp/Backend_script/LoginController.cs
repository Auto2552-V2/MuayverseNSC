using UnityEngine;
using UnityEngine.UI;
using TMPro;

// คุมหน้า Login_Page: ยิง login -> ไปหน้า Home
public class LoginController : MonoBehaviour
{
    [Header("API")]
    [SerializeField] private AuthApi authApi;

    [Header("Inputs")]
    [SerializeField] private TMP_InputField emailInput;
    [SerializeField] private TMP_InputField passwordInput;

    [Header("UI")]
    [SerializeField] private TMP_Text alertText;            // ข้อความ error
    [SerializeField] private Button loginButton;

    [Header("Navigation")]
    [SerializeField] private PageManager pageManager;       // ลาก _UIController ใส่ (แนะนำ)
    [SerializeField] private GameObject loginPage;          // หน้านี้ (ปิดเมื่อสำเร็จ)
    [SerializeField] private GameObject homePage;           // ไปหน้านี้เมื่อสำเร็จ

    private void Awake()
    {
        HideAlert();
        if (loginButton != null)
            loginButton.onClick.AddListener(OnLoginClicked);
    }

    public void OnLoginClicked()
    {
        string email = emailInput != null ? emailInput.text.Trim() : "";
        string password = passwordInput != null ? passwordInput.text : "";

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            ShowAlert("กรุณากรอกอีเมลและรหัสผ่าน");
            return;
        }

        HideAlert();
        SetInteractable(false);

        authApi.Login(email, password,
            onSuccess: res =>
            {
                SetInteractable(true);
                SaveSession(res);
                GoToHome();
            },
            onError: msg =>
            {
                SetInteractable(true);
                ShowAlert(msg); // เช่น "email or password is incorrect"
            });
    }

    private void SaveSession(AuthResponse res)
    {
        // BeginSession ล้าง cache ของบัญชีก่อนหน้าให้ก่อนเสมอ — ดูเหตุผลใน UserSession.cs
        UserSession.BeginSession(res);
    }

    private void GoToHome()
    {
        // ผ่าน PageManager เพื่อให้แถบเมนูล่างกลับมาโชว์ด้วย
        if (pageManager != null && homePage != null)
        {
            pageManager.ShowPage(homePage);
            return;
        }

        // สำรอง: ถ้ายังไม่ได้ลาก PageManager ใส่ ก็ยังเข้าหน้า Home ได้เหมือนเดิม
        if (loginPage != null) loginPage.SetActive(false);
        if (homePage != null) homePage.SetActive(true);
    }

    private void SetInteractable(bool value)
    {
        if (loginButton != null) loginButton.interactable = value;
    }

    private void ShowAlert(string message)
    {
        if (alertText == null) return;
        alertText.text = message;
        alertText.gameObject.SetActive(true);
    }

    private void HideAlert()
    {
        if (alertText != null) alertText.gameObject.SetActive(false);
    }
}
