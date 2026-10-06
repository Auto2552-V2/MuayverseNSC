using System;
using UnityEngine;

// ที่เก็บ session + ข้อมูล profile ที่ cache ไว้ในเครื่อง (PlayerPrefs)
//
// รวม key ทุกตัวไว้ที่เดียว เพราะเคยมีบั๊กจากการที่แต่ละสคริปต์รู้จัก key คนละชุด:
// ตอนล็อกอินบัญชีใหม่ มีแค่ userId/userName/userEmail ที่ถูกทับ ส่วนอายุ ชื่อจริง
// เบอร์โทร ที่อยู่ อาการ ของ "บัญชีก่อนหน้า" ยังค้างอยู่ แล้วไปโผล่ในหน้า Profile
// ของคนใหม่ — ข้อมูลส่วนตัวรั่วข้ามบัญชีบนเครื่องเดียวกัน
//
// กติกา: เข้าสู่ระบบ/สมัคร ให้เรียก BeginSession() เสมอ อย่าเขียน PlayerPrefs เอง
public static class UserSession
{
    // ── ตัวระบุ session ──
    // KeyUserId เป็น uuid ใช้ยิง API ทุกเส้น ส่วน KeyPatientCode ("0003") มีไว้โชว์
    // อย่างเดียว อย่าเอาไปแทนกัน — /api/users/0003 หา uuid ไม่เจอ ได้ 404 กลับมา
    public const string KeyUserId = "userId";
    public const string KeyPatientCode = "userPatientCode";
    public const string KeyName = "userName";
    public const string KeyEmail = "userEmail";


    // ── ยอดเหรียญ/ดาว ──
    // cache ไว้ให้แถบบนสุดมีเลขโชว์ทันทีที่เปิดหน้า ไม่ต้องรอเน็ต แล้วค่อยถูก
    // ทับด้วยยอดจริงเมื่อ API ตอบ — ยอดที่เชื่อถือได้อยู่ที่ฐานข้อมูลเสมอ
    public const string KeyCoins = "userCoins";
    public const string KeyStars = "userStars";

    // ทุก key ที่เป็น "ข้อมูลของผู้ใช้คนนั้น" — ต้องล้างเมื่อเปลี่ยนบัญชี
    private static readonly string[] ProfileKeys =
    {
        KeyPatientCode, KeyName, KeyEmail, KeyCoins, KeyStars,
    };

    // ยิงทุกครั้งที่ "บัญชีที่ล็อกอินอยู่" เปลี่ยน — login / สมัคร / logout
    //
    // จำเป็นเพราะจอที่โชว์ข้อมูลผู้ใช้ (ProfileEditor, ProfileImagePicker) แปะอยู่บน
    // Edit_MyProfile ซึ่งเปิดค้างตลอดทั้งซีน OnEnable ของมันจึงทำงานครั้งเดียวตอน
    // ซีนโหลด ซึ่งเป็นตอนที่ยังไม่ได้ล็อกอิน พอสลับบัญชีทีหลังไม่มีอะไรไปสั่งให้
    // วาดใหม่ ข้อมูลกับรูปของคนก่อนหน้าเลยค้างบนจอของคนใหม่ ทั้งที่ PlayerPrefs
    // ถูกล้างไปแล้วเรียบร้อย
    //
    // ผูก event ใน OnEnable และถอดใน OnDisable เสมอ — นี่เป็น static event
    // ถ้าไม่ถอด ตัวที่ถูกทำลายไปแล้วจะยังถูกเรียกอยู่
    public static event Action AccountChanged;

    /// <summary>
    /// ยอดเหรียญ/ดาวเปลี่ยน — แถบบนสุดทุกหน้าฟังตัวนี้เพื่อวาดใหม่
    ///
    /// แยกจาก AccountChanged เพราะยอดเปลี่ยนบ่อยกว่ามาก (ทุกครั้งที่ได้รางวัล)
    /// และไม่ได้แปลว่าสลับบัญชี คนที่ฟังจึงไม่ต้องไปโหลดข้อมูลอื่นใหม่ทั้งชุด
    /// </summary>
    public static event Action BalanceChanged;

    public static string UserId => PlayerPrefs.GetString(KeyUserId, "");

    public static bool IsLoggedIn => !string.IsNullOrEmpty(UserId);

    // เรียกทันทีที่ล็อกอิน/สมัครสำเร็จ
    //
    // ล้าง cache ของคนเก่าทิ้งก่อนเสมอ ไม่ใช่แค่ตอนที่ id ไม่ตรง เพราะถ้า id
    // เดิมแต่ข้อมูลฝั่งเซิร์ฟเวอร์ถูกแก้จากที่อื่น ค่าเก่าก็ไม่ควรค้างอยู่ดี
    // เดี๋ยว ProfileEditor จะดึงของจริงมาเติมให้เองตอนเปิดหน้า Profile
    public static void BeginSession(AuthResponse res)
    {
        if (res == null) return;

        ClearKeys();
        PlayerPrefs.SetString(KeyUserId, res.id ?? "");
        PlayerPrefs.SetString(KeyPatientCode, res.patientCode ?? "");
        PlayerPrefs.SetString(KeyName, res.name ?? "");
        PlayerPrefs.SetString(KeyEmail, res.email ?? "");
        PlayerPrefs.SetInt(KeyCoins, res.coins);
        PlayerPrefs.SetInt(KeyStars, res.stars);
        PlayerPrefs.Save();

        // ยิงหลังเขียน key ครบแล้ว คนที่ฟังอยู่จะได้อ่านเจอบัญชีใหม่ ไม่ใช่ค่าว่างกลางคัน
        AccountChanged?.Invoke();
    }

    public static int Coins => PlayerPrefs.GetInt(KeyCoins, 0);
    public static int Stars => PlayerPrefs.GetInt(KeyStars, 0);

    /// <summary>
    /// เขียนยอดล่าสุดที่เซิร์ฟเวอร์ตอบกลับมา แล้วบอกให้ทุกจอวาดใหม่
    ///
    /// ห้ามให้ฝั่งเกมบวกเลขเอง — ยอดต้องมาจากคำตอบของ API เท่านั้น ไม่งั้นจอกับ
    /// ฐานข้อมูลจะค่อย ๆ เพี้ยนจากกันโดยไม่มีใครรู้
    /// </summary>
    public static void SetBalance(int coins, int stars)
    {
        PlayerPrefs.SetInt(KeyCoins, coins);
        PlayerPrefs.SetInt(KeyStars, stars);
        PlayerPrefs.Save();
        BalanceChanged?.Invoke();
    }

    // ออกจากระบบ — ต้องไม่เหลือร่องรอยของคนก่อนไว้ให้คนถัดไปที่ใช้เครื่องนี้เห็น
    public static void Clear()
    {
        ClearKeys();
        AccountChanged?.Invoke();
    }

    // ล้าง key อย่างเดียวไม่ยิง event — BeginSession ใช้ตัวนี้เพื่อไม่ให้ยิงสองรอบ
    // (รอบแรกตอนล้างจะเป็นสถานะ "ไม่มีใครล็อกอิน" ซึ่งไม่ใช่ความจริงสักนิด)
    private static void ClearKeys()
    {
        PlayerPrefs.DeleteKey(KeyUserId);
        foreach (string key in ProfileKeys)
            PlayerPrefs.DeleteKey(key);
        PlayerPrefs.Save();
    }
}
