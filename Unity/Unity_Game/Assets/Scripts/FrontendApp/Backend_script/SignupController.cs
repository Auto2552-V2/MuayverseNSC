using UnityEngine;
using UnityEngine.UI;
using TMPro;

// คุมหน้า Signin_Page: ตรวจข้อมูล -> ยิง register -> ไปหน้า Home
// ** ใส่ component นี้แทน CreateAccountValidator เพื่อไม่ให้ปุ่มถูกผูก 2 ที่ **
public class SignupController : MonoBehaviour
{
    [Header("API")]
    [SerializeField] private AuthApi authApi;

    [Header("Inputs")]
    [SerializeField] private TMP_InputField nameInput;
    [SerializeField] private TMP_InputField emailInput;
    [SerializeField] private TMP_InputField passwordInput;

    [Header("UI")]
    [SerializeField] private TMP_Text alertText;            // AlertPassword (ใช้โชว์ error ทุกแบบ)
    [SerializeField] private Button createAccountButton;

    [Header("Navigation")]
    [SerializeField] private PageManager pageManager;       // ลาก _UIController ใส่ (แนะนำ)
    [SerializeField] private GameObject signinPage;         // หน้านี้ (ปิดเมื่อสำเร็จ)
    [SerializeField] private GameObject homePage;           // ไปหน้านี้เมื่อสำเร็จ

    [Header("Rules")]
    [SerializeField] private int minPasswordLength = 8;

    private void Awake()
    {
        HideAlert();
        if (createAccountButton != null)
            createAccountButton.onClick.AddListener(OnCreateAccountClicked);
    }

    public void OnCreateAccountClicked()
    {
        string name = nameInput != null ? nameInput.text.Trim() : "";
        string email = emailInput != null ? emailInput.text.Trim() : "";
        string password = passwordInput != null ? passwordInput.text : "";

        // --- ตรวจฝั่ง client ก่อน (ลดการยิงไป server โดยไม่จำเป็น) ---
        if (string.IsNullOrEmpty(name)) { ShowAlert("กรุณากรอกชื่อ"); return; }
        if (!IsValidEmail(email)) { ShowAlert("อีเมลไม่ถูกต้อง"); return; }
        if (password.Length < minPasswordLength)
        {
            ShowAlert($"รหัสผ่านต้องมีอย่างน้อย {minPasswordLength} ตัวอักษร");
            return;
        }

        HideAlert();
        SetInteractable(false); // กันกดซ้ำระหว่างรอ server

        authApi.Register(name, email, password,
            onSuccess: res =>
            {
                SetInteractable(true);
                SaveSession(res);
                GoToHome();
            },
            onError: msg =>
            {
                SetInteractable(true);
                ShowAlert(msg); // เช่น "email already registered"
            });
    }

    private void SaveSession(AuthResponse res)
    {
        // BeginSession ล้าง cache ของบัญชีก่อนหน้าให้ก่อนเสมอ
        // เขียน PlayerPrefs เองตรงนี้ไม่ได้ เพราะจะทับแค่ 3 key แล้วอายุ/ชื่อจริง/
        // เบอร์/ที่อยู่/อาการ ของคนเก่าจะค้างไปโผล่ในหน้า Profile ของคนใหม่
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
        if (signinPage != null) signinPage.SetActive(false);
        if (homePage != null) homePage.SetActive(true);
    }

    private bool IsValidEmail(string email)
    {
        return !string.IsNullOrEmpty(email)
            && System.Text.RegularExpressions.Regex.IsMatch(email, @"^[^\s@]+@[^\s@]+\.[^\s@]+$");
    }

    private void SetInteractable(bool value)
    {
        if (createAccountButton != null) createAccountButton.interactable = value;
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
