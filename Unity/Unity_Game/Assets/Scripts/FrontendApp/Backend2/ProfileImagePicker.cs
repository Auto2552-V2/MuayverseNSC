using System.IO;
using UnityEngine;
using UnityEngine.UI;

// กดปุ่ม Edit Profile -> เลือกรูปจากเครื่อง -> แสดงเป็นรูปโปรไฟล์ (ทุกที่ที่ตั้งไว้)
// รูปถูกเซฟไว้ เปิดเกมใหม่ก็ยังอยู่
//
// เซฟ "แยกตามบัญชี" — ชื่อไฟล์ผูกกับ userId
// เดิมใช้ไฟล์ชื่อ profile.png ไฟล์เดียวทั้งเครื่อง ใครเลือกรูปทับล่าสุด บัญชีอื่น
// ที่ล็อกอินบนเครื่องเดียวกันก็เห็นรูปคนนั้น
//
// ข้อจำกัด: รูปอยู่ในเครื่องเท่านั้น ย้ายเครื่องแล้วไม่ตามไป และ WebGL ใช้
// WindowsFileDialog ไม่ได้ — ถ้าจะขึ้นเว็บจริงต้องเปลี่ยนไปเก็บที่ Supabase Storage
// (คอลัมน์ patients.avatar_url เตรียมไว้ให้แล้ว)
public class ProfileImagePicker : MonoBehaviour
{
    [Header("Button")]
    [SerializeField] private Button editProfileButton;   // ปุ่ม Edit_Profile

    [Header("Targets — รูปทุกช่องที่ต้องเปลี่ยนตาม")]
    [Tooltip("ลากช่องรูปทั้งหมดมาใส่ เช่น Edit_Profile_User (Photo) และ Profile_User (Photo) หน้า Home")]
    [SerializeField] private Image[] targetImages;

    [Header("ซ่อนเมื่อมีรูปโปรไฟล์แล้ว")]
    [Tooltip("ลาก Photo (1) หรือไอคอนกล้อง placeholder มาใส่ — จะหายทันทีที่ผู้ใช้เลือกรูป")]
    [SerializeField] private GameObject[] hideWhenPhotoSet;

    [Header("Options")]
    [SerializeField] private bool saveToDisk = true;     // จำรูปไว้หลังปิดเกม
    [Tooltip("รูปตอนที่บัญชีนี้ยังไม่เคยเลือกรูป (เว้นว่าง = ใช้รูปที่ตั้งไว้ในซีน)")]
    [SerializeField] private Sprite defaultAvatar;

    // รูปที่ตั้งไว้ในซีนตั้งแต่แรก ใช้คืนค่าเมื่อบัญชีใหม่ยังไม่มีรูปของตัวเอง
    private Sprite[] originalSprites;

    private string SavePath
    {
        get
        {
            string userId = UserSession.UserId;
            string name = string.IsNullOrEmpty(userId)
                ? "profile_guest.png"
                : $"profile_{Sanitize(userId)}.png";
            return Path.Combine(Application.persistentDataPath, name);
        }
    }

    // userId เป็น uuid อยู่แล้ว แต่กันไว้เผื่ออนาคตเปลี่ยนรูปแบบ id
    private static string Sanitize(string value)
    {
        foreach (char bad in Path.GetInvalidFileNameChars())
            value = value.Replace(bad, '_');
        return value;
    }

    private void Awake()
    {
        if (editProfileButton != null)
            editProfileButton.onClick.AddListener(OnEditProfileClicked);

        // เก็บรูปตั้งต้นไว้ก่อนที่จะมีใครไปทับ
        originalSprites = new Sprite[targetImages.Length];
        for (int i = 0; i < targetImages.Length; i++)
            originalSprites[i] = targetImages[i] != null ? targetImages[i].sprite : null;
    }

    // ต้องโหลดใหม่ทุกครั้งที่บัญชีเปลี่ยน ไม่ใช่ครั้งเดียวตอนซีนโหลด
    //
    // พึ่ง OnEnable อย่างเดียวไม่ได้ เพราะ component นี้อยู่บน Edit_MyProfile ที่เปิด
    // ค้างตลอดทั้งซีน มันเลยทำงานแค่รอบเดียวตอนเริ่มเกม แล้วรูปของบัญชีนั้นก็ค้าง
    // บน Image ยาวไปจนปิดเกม แม้จะสลับบัญชีไปแล้วก็ตาม
    private void OnEnable()
    {
        UserSession.AccountChanged += LoadSavedImage;
        LoadSavedImage();
    }

    private void OnDisable()
    {
        UserSession.AccountChanged -= LoadSavedImage;
    }

    public void OnEditProfileClicked()
    {
        string path = WindowsFileDialog.OpenImageFile();
        if (string.IsNullOrEmpty(path)) return;   // กดยกเลิก

        if (!File.Exists(path))
        {
            Debug.LogWarning($"ไม่พบไฟล์: {path}");
            return;
        }

        byte[] data = File.ReadAllBytes(path);
        ApplyImage(data);

        if (saveToDisk)
            File.WriteAllBytes(SavePath, data);
    }

    // โหลดรูปของ "บัญชีที่ล็อกอินอยู่ตอนนี้"
    private void LoadSavedImage()
    {
        string path = SavePath;
        if (saveToDisk && File.Exists(path))
        {
            ApplyImage(File.ReadAllBytes(path));
            return;
        }

        // บัญชีนี้ยังไม่เคยเลือกรูป — ต้องคืนค่าให้ชัดเจน
        // ถ้าไม่ทำ sprite ของบัญชีก่อนหน้าจะยังติดอยู่บน Image component
        ResetToDefault();
    }

    private void ResetToDefault()
    {
        for (int i = 0; i < targetImages.Length; i++)
        {
            Image img = targetImages[i];
            if (img == null) continue;

            img.sprite = defaultAvatar != null
                ? defaultAvatar
                : (originalSprites != null && i < originalSprites.Length ? originalSprites[i] : null);
        }

        SetPlaceholders(true);   // ยังไม่มีรูป -> โชว์ไอคอนกล้องกลับมา
    }

    // เปิด/ปิดไอคอน placeholder เช่น Photo (1)
    private void SetPlaceholders(bool visible)
    {
        if (hideWhenPhotoSet == null) return;

        foreach (GameObject obj in hideWhenPhotoSet)
        {
            if (obj != null) obj.SetActive(visible);
        }
    }

    // แปลง byte -> Texture -> ครอบเป็นจัตุรัส -> Sprite -> ใส่ทุก target
    private void ApplyImage(byte[] data)
    {
        Texture2D texture = new Texture2D(2, 2);
        if (!texture.LoadImage(data))
        {
            Debug.LogWarning("อ่านรูปไม่สำเร็จ (รองรับ .png / .jpg เท่านั้น)");
            return;
        }

        Texture2D square = CropToSquare(texture);
        Sprite sprite = Sprite.Create(
            square,
            new Rect(0, 0, square.width, square.height),
            new Vector2(0.5f, 0.5f)
        );

        foreach (Image img in targetImages)
        {
            if (img == null) continue;
            img.sprite = sprite;
            img.color = Color.white;   // กันกรณีสีถูกตั้งไว้จางๆ
            img.enabled = true;
        }

        SetPlaceholders(false);   // มีรูปแล้ว -> ซ่อนไอคอนกล้องทิ้ง
    }

    // ตัดรูปจากตรงกลางให้เป็นจัตุรัส -> ใส่ในวงกลมแล้วไม่ยืดเบี้ยว
    private Texture2D CropToSquare(Texture2D src)
    {
        int size = Mathf.Min(src.width, src.height);
        int x = (src.width - size) / 2;
        int y = (src.height - size) / 2;

        Texture2D result = new Texture2D(size, size, TextureFormat.RGBA32, false);
        result.SetPixels(src.GetPixels(x, y, size, size));
        result.Apply();
        return result;
    }
}
