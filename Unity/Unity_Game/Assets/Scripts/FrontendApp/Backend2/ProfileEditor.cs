using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// กด Edit Profile -> เปิด popup แก้ข้อมูล -> กด Save -> อัปเดตทุก display
// ค่าถูกเก็บใน PlayerPrefs เปิดเกมใหม่ก็ยังอยู่
public class ProfileEditor : MonoBehaviour
{
    [Header("Backend")]
    [SerializeField] private AuthApi authApi;            // ตัวยิง API (ตัวเดียวกับหน้า Login)

    [Header("Buttons")]
    [SerializeField] private Button editProfileButton;   // Edit_Profile (button) — เปิด popup
    [SerializeField] private Button saveButton;          // ปุ่ม Save ใน popup
    [SerializeField] private Button cancelButton;        // ปุ่ม Cancel ใน popup (ไม่ใส่ก็ได้)

    [Header("Popup")]
    [SerializeField] private GameObject displayPanel;    // TabDisplay — ตัวโชว์ข้อมูล (โชว์ตลอด ยกเว้นตอนแก้)
    [SerializeField] private GameObject editPanel;       // Tab_Editor — ฟอร์มแก้ไข (ปิดไว้ตอนเริ่ม)
    // ช่องข้อมูลส่วนตัว (ชื่อจริง/วันเกิด/เพศ/อายุ/เบอร์/ที่อยู่/อาการ) ถูกถอดออก
    // พร้อมกับคอลัมน์ในฐานข้อมูล — ดู supabase/2026-10-05-drop-profile-fields.sql
    // ถ้าจะเอากลับต้องคืนคอลัมน์ก่อน ไม่งั้นกรอกแล้วหายทุกครั้งที่โหลดใหม่
    [SerializeField] private TMP_InputField usernameInput;
    [SerializeField] private TMP_InputField EmailInput;
    [SerializeField] private TMP_InputField PasswordInput;   // เว้นว่าง = ไม่เปลี่ยนรหัส

    [Header("Displays — ช่องที่ต้องเปลี่ยนตาม")]
    [SerializeField] private TMP_Text usernameDisplay;       // Username (หน้า Profile)
    [SerializeField] private TMP_Text homeUsernameDisplay;   // UserName_Display (หน้า Home)
    [SerializeField] private TMP_Text EmailDisplay;
    [SerializeField] private TMP_Text userIdDisplay;         // UserID (แสดงอย่างเดียว แก้ไม่ได้)
    [SerializeField] private TMP_Text statusText;            // แจ้งผล save/โหลด (ไม่ใส่ก็ได้)

    [Header("Options")]
    [SerializeField] private string homePrefix = "";     // ใส่ให้ตรงกับ HomePageController ถ้ามี prefix

    // ชื่อ key ทั้งหมดอยู่ที่ UserSession — ตรงนี้แค่ตั้งชื่อสั้นให้อ่านง่าย
    // อย่าประกาศ key เองซ้ำ ไม่งั้นล็อกอินบัญชีใหม่แล้วล้างไม่ครบ ข้อมูลคนเก่าจะค้าง
    private const string KeyUserId = UserSession.KeyUserId;
    private const string KeyPatientCode = UserSession.KeyPatientCode;
    private const string KeyName = UserSession.KeyName;
    private const string KeyEmail = UserSession.KeyEmail;

    // จับคู่ input + display + key ไว้ที่เดียว
    private class Field
    {
        public TMP_InputField input;
        public TMP_Text display;
        public string key;

        public Field(TMP_InputField input, TMP_Text display, string key)
        {
            this.input = input;
            this.display = display;
            this.key = key;
        }
    }

    private List<Field> fields;

    private void Awake()
    {
        // เพิ่มช่องใหม่ในอนาคต -> เติมอีก 1 บรรทัดตรงนี้พอ
        fields = new List<Field>
        {
            new Field(usernameInput,  usernameDisplay,  KeyName),
            new Field(EmailInput,     EmailDisplay,     KeyEmail),
        };

        if (editProfileButton != null) editProfileButton.onClick.AddListener(OpenPanel);
        if (saveButton != null) saveButton.onClick.AddListener(Save);
        if (cancelButton != null) cancelButton.onClick.AddListener(ClosePanel);

        // เริ่มต้น: โชว์ข้อมูล ซ่อนฟอร์มแก้ไข
        if (displayPanel != null) displayPanel.SetActive(true);
        if (editPanel != null) editPanel.SetActive(false);
    }

    // ต้องวาดใหม่ทุกครั้งที่ "บัญชีเปลี่ยน" ไม่ใช่แค่ตอน OnEnable
    //
    // เดิมพึ่ง OnEnable อย่างเดียว ซึ่งใช้ไม่ได้เลยกับที่ที่ component นี้แปะอยู่จริง:
    // Edit_MyProfile เปิดค้างตลอดทั้งซีน OnEnable จึงทำงานครั้งเดียวตอนซีนโหลด
    // ตอนนั้นยังไม่ได้ล็อกอิน PlayerPrefs ยังเป็นของ session ก่อนหน้า — พอสลับบัญชี
    // แล้วเปิดหน้านี้ ไม่มีอะไรไปสั่งวาดใหม่ ข้อมูลคนเก่าเลยค้างเต็มหน้าจอคนใหม่
    private void OnEnable()
    {
        UserSession.AccountChanged += ReloadForCurrentAccount;
        ReloadForCurrentAccount();
    }

    private void OnDisable()
    {
        UserSession.AccountChanged -= ReloadForCurrentAccount;
    }

    private void ReloadForCurrentAccount()
    {
        RefreshDisplays();   // แสดงค่าที่ cache ไว้ในเครื่องก่อน (เร็ว + ใช้ได้แม้เน็ตหลุด)
        LoadFromBackend();   // แล้วดึงค่าล่าสุดจาก backend มาทับ
    }

    // ดึง profile ล่าสุดจาก backend มาแสดง (ต้อง login มาก่อนถึงจะมี userId)
    private void LoadFromBackend()
    {
        string userId = PlayerPrefs.GetString(KeyUserId, "");
        if (authApi == null || string.IsNullOrEmpty(userId)) return;

        authApi.GetProfile(userId,
            onSuccess: data =>
            {
                ApplyProfile(data);   // เขียนลง cache + อัปเดต display
            },
            onError: msg =>
            {
                SetStatus("โหลดข้อมูลจากเซิร์ฟเวอร์ไม่ได้: " + msg);
            });
    }

    // เปิด popup พร้อมเติมค่าปัจจุบันลงในทุกช่องกรอก
    public void OpenPanel()
    {
        foreach (Field f in fields)
        {
            if (f.input != null)
                f.input.text = PlayerPrefs.GetString(f.key, "");
        }

        // สลับไปฟอร์มแก้ไข: ซ่อน display โชว์ editor
        if (displayPanel != null) displayPanel.SetActive(false);
        if (editPanel != null) editPanel.SetActive(true);
    }

    public void ClosePanel()
    {
        // กลับมาโชว์ข้อมูล: ปิด editor เปิด display คืน + รีเฟรชค่าล่าสุด
        if (editPanel != null) editPanel.SetActive(false);
        if (displayPanel != null) displayPanel.SetActive(true);
        RefreshDisplays();
    }

    // กด Save -> เก็บ cache ลงเครื่อง + ส่งขึ้น backend
    public void Save()
    {
        // 1) เก็บลงเครื่องก่อนเสมอ (ได้ผลทันที + ใช้ต่อได้แม้เน็ตหลุด)
        foreach (Field f in fields)
        {
            if (f.input == null) continue;
            PlayerPrefs.SetString(f.key, f.input.text.Trim());
        }
        PlayerPrefs.Save();

        RefreshDisplays();

        // 2) ส่งขึ้น backend
        string userId = PlayerPrefs.GetString(KeyUserId, "");
        if (authApi == null || string.IsNullOrEmpty(userId))
        {
            SetStatus("บันทึกในเครื่องแล้ว (ยังไม่ได้ login เลยไม่ได้ส่งขึ้นเซิร์ฟเวอร์)");
            ClosePanel();
            return;
        }

        SetStatus("กำลังบันทึก...");
        authApi.UpdateProfile(userId, BuildProfile(),
            onSuccess: data =>
            {
                ApplyProfile(data);   // sync ค่าที่ backend ตอบกลับ (email ที่ normalize แล้ว ฯลฯ)
                if (PasswordInput != null) PasswordInput.text = ""; // ล้างช่องรหัสหลังบันทึก
                SetStatus("บันทึกสำเร็จ");
                ClosePanel();
            },
            onError: msg =>
            {
                // เก็บในเครื่องไปแล้ว จึงแค่แจ้งเตือน ไม่ปิด popup เผื่อผู้ใช้แก้แล้วลองใหม่
                SetStatus("บันทึกขึ้นเซิร์ฟเวอร์ไม่สำเร็จ: " + msg);
            });
    }

    // รวบค่าจากช่องกรอกทั้งหมดเป็น ProfileData เพื่อส่งขึ้น backend
    private ProfileData BuildProfile()
    {
        return new ProfileData
        {
            name      = usernameInput  != null ? usernameInput.text.Trim()  : "",
            email     = EmailInput     != null ? EmailInput.text.Trim()     : "",
            // ว่าง = ไม่เปลี่ยนรหัส (backend จะข้ามให้)
            password  = PasswordInput  != null ? PasswordInput.text         : "",
        };
    }

    // เอา ProfileData จาก backend มาเขียนลง cache แล้วอัปเดต display
    private void ApplyProfile(ProfileData d)
    {
        if (d == null) return;

        // ทิ้งคำตอบที่มาช้าของบัญชีก่อนหน้า — ถ้าผู้ใช้สลับบัญชีตอน request ยังค้าง
        // อยู่ response เก่าจะวิ่งมาทับข้อมูลคนใหม่ กลายเป็นบั๊กเดิมแบบสุ่มเจอ
        if (!string.IsNullOrEmpty(d.id) && d.id != UserSession.UserId) return;

        PlayerPrefs.SetString(KeyName,  d.name ?? "");
        PlayerPrefs.SetString(KeyEmail, d.email ?? "");
        if (!string.IsNullOrEmpty(d.id)) PlayerPrefs.SetString(KeyUserId, d.id);
        if (!string.IsNullOrEmpty(d.patientCode))
            PlayerPrefs.SetString(KeyPatientCode, d.patientCode);
        PlayerPrefs.Save();

        RefreshDisplays();
    }

    // อ่านค่าจาก PlayerPrefs แล้วยิงลงทุก display
    private void RefreshDisplays()
    {
        foreach (Field f in fields)
        {
            if (f.display != null)
                f.display.text = PlayerPrefs.GetString(f.key, "");
        }

        // หน้า Home ใช้ชื่อเดียวกับ username แต่ใส่ prefix ได้
        // (set ได้แม้หน้า Home ถูกปิดอยู่)
        if (homeUsernameDisplay != null)
            homeUsernameDisplay.text = homePrefix + PlayerPrefs.GetString(KeyName, "");

        // UID แสดงอย่างเดียว — ใช้ patient_code ("0003") ไม่ใช่ uuid
        // uuid ยาว 36 ตัวอ่านไม่รู้เรื่อง และเป็นตัวเดียวกับที่ฝั่งหมอเห็นในเว็บ
        // ผู้ป่วยบอกเลขนี้กับหมอได้เลยเวลาต้องอ้างถึงตัวเอง
        if (userIdDisplay != null)
            userIdDisplay.text = PlayerPrefs.GetString(KeyPatientCode, "");
    }

    private void SetStatus(string message)
    {
        if (statusText != null) statusText.text = message;
    }
}
