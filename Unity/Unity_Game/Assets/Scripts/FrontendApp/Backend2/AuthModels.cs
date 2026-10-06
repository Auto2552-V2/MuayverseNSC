// โครงสร้างข้อมูลที่ใช้แปลง JSON ไป-กลับกับ backend
// JsonUtility จับคู่ด้วย "ชื่อ field" ต้องตรงกับ key ใน JSON

[System.Serializable]
public class RegisterRequest
{
    public string name;
    public string email;
    public string password;
}

[System.Serializable]
public class LoginRequest
{
    public string email;
    public string password;
}

// ผลลัพธ์ที่ backend ส่งกลับเมื่อสำเร็จ: { id, patientCode, name, email, ... }
// (ยังมี field อื่นติดมาด้วย เช่น age — JsonUtility ทิ้ง key ที่ไม่ได้ประกาศ)
[System.Serializable]
public class AuthResponse
{
    public string id;          // uuid — ตัวที่ใช้ยิง API ทุกเส้น
    public string patientCode; // "0003" — ตัวที่เอาไปโชว์เป็น UID ในเกม
    public string name;
    public string email;

    // ยอดเหรียญ/ดาวตอนล็อกอิน — ต้องประกาศไว้ ไม่งั้น JsonUtility ทิ้งทั้งที่
    // เซิร์ฟเวอร์ส่งมาให้ แล้วแถบบนสุดจะขึ้น 0 ทุกครั้งที่ล็อกอินใหม่
    public int coins;
    public int stars;
}

// ผลลัพธ์เมื่อผิดพลาด: { error: "..." }
[System.Serializable]
public class ErrorResponse
{
    public string error;
}

// ข้อมูล profile ที่รับ-ส่งกับ backend (GET/PUT /api/users/:id)
// ชื่อ field ต้องตรงกับ key ใน JSON ของ backend เป๊ะๆ
[System.Serializable]
public class ProfileData
{
    public string id;
    // UID ที่โชว์ในหน้า Profile — เซิร์ฟเวอร์เป็นคนออกให้ตอนสมัคร แก้ไม่ได้
    // ส่งกลับไปตอน PUT ก็ไม่มีผล API ไม่รับ key นี้ (ดู FIELD_TO_COLUMN ใน patients.ts)
    public string patientCode;
    public string name;      // = Username
    public string email;

    // อ่านอย่างเดียว — ส่งกลับไปตอน PUT ก็ไม่มีผล เซิร์ฟเวอร์ไม่รับสอง key นี้
    // (ดู FIELD_TO_COLUMN ใน patients.ts) เหรียญเปลี่ยนผ่าน grant_reward() เท่านั้น
    public int coins;
    public int stars;

    // ใช้เฉพาะตอน "ส่ง" เพื่อเปลี่ยนรหัสผ่าน (ว่าง = ไม่เปลี่ยน)
    // ตอน GET กลับมา backend จะไม่ส่ง password มา ค่านี้เลยเป็นค่าว่างเสมอ
    public string password;
}
