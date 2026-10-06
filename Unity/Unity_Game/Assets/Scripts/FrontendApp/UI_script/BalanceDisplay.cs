using TMPro;
using UnityEngine;
using UnityEngine.UI;

// โชว์ยอดเหรียญ/ดาวของผู้ใช้บนแถบบนสุด
//
// เดิมไม่มีใครโหลดยอดมาแสดงเลย ตัวเลขบนจอจึงเป็นค่าที่พิมพ์ทิ้งไว้ในซีน (0)
// ทั้งที่ฐานข้อมูลเก็บยอดจริงไว้ครบ
//
// ที่มาของเลขมีสองทาง เรียงตามความสด:
//   1. cache ใน UserSession — ขึ้นทันทีไม่ต้องรอเน็ต (อาจเก่าไปนิด)
//   2. GET /api/users/:id — ยอดจริง มาทับ cache เมื่อคำตอบถึง
//
// แปะไว้บน GameObject ที่ active ตลอด (เช่น _UIController) แล้วลากช่อง text
// ของทุกหน้าที่โชว์ยอดใส่ list — หน้าที่ปิดอยู่ก็เขียนได้ ไม่ต้องรอให้เปิด
public class BalanceDisplay : MonoBehaviour
{
    [Header("ช่องตัวเลข — ใส่ได้หลายช่อง (คนละหน้ากัน)")]
    [Tooltip("ช่องที่โชว์จำนวนเหรียญ เช่น CoinAmount ของแถบบนสุด")]
    [SerializeField] private TMP_Text[] coinTexts;

    [Tooltip("ช่องที่โชว์จำนวนดาว เช่น StarAmount")]
    [SerializeField] private TMP_Text[] starTexts;

    [Header("หลอดความคืบหน้าดาว")]
    [Tooltip("หลอดที่เดินตามจำนวนดาว เช่น Slider ในการ์ดโปรไฟล์ — เว้นว่างได้")]
    [SerializeField] private Slider[] starSliders;

    [Tooltip("ดาวกี่ดวงหลอดถึงเต็ม — ต้องตรงกับป้าย \"/20\" ที่วางไว้ข้างหลอด\n" +
             "ถ้าแก้ตรงนี้อย่าลืมแก้ข้อความ star_text ในซีนด้วย ไม่งั้นหลอดกับป้ายจะคนละเรื่อง")]
    [SerializeField][Min(1)] private int starsPerBar = 20;

    [Tooltip("ติ๊ก = ครบ 20 แล้วหลอดวนกลับไปเริ่มใหม่ (ดาวสะสมไปเรื่อย ๆ)\n" +
             "ไม่ติ๊ก = หลอดเต็มแล้วค้างเต็มตลอด (เป้าหมายครั้งเดียวจบ)")]
    [SerializeField] private bool barWrapsAtTarget = true;

    [Header("Backend")]
    [Tooltip("ตัวยิง API — เว้นว่างได้ จะหาในซีนให้เอง")]
    [SerializeField] private AuthApi authApi;

    private void Awake()
    {
        if (authApi == null) authApi = FindFirstObjectByType<AuthApi>();
    }

    private void OnEnable()
    {
        // สลับบัญชีแล้วต้องวาดใหม่ ไม่งั้นยอดของคนก่อนค้างอยู่บนจอของคนใหม่
        UserSession.AccountChanged += HandleAccountChanged;
        UserSession.BalanceChanged += Paint;

        Paint();
        Refresh();
    }

    private void OnDisable()
    {
        UserSession.AccountChanged -= HandleAccountChanged;
        UserSession.BalanceChanged -= Paint;
    }

    private void HandleAccountChanged()
    {
        Paint();      // cache ของบัญชีใหม่ (หรือ 0 ถ้า logout)
        Refresh();    // แล้วตามด้วยยอดจริงจากเซิร์ฟเวอร์
    }

    /// <summary>ดึงยอดจริงจาก backend — เรียกได้ทุกเมื่อ เช่นหลังได้รางวัล</summary>
    public void Refresh()
    {
        if (!UserSession.IsLoggedIn || authApi == null) return;

        authApi.GetProfile(UserSession.UserId,
            // SetBalance ยิง BalanceChanged ต่อให้เอง จึงไม่ต้องเรียก Paint ตรงนี้
            profile => UserSession.SetBalance(profile.coins, profile.stars),
            // ล้มแล้วปล่อยให้ cache ที่วาดไปแล้วค้างไว้ ดีกว่าล้างจอเป็น 0
            // ทั้งที่ผู้ใช้มีเหรียญจริง — แค่ตอนนี้ยืนยันกับเซิร์ฟเวอร์ไม่ได้
            error => Debug.LogWarning($"[BalanceDisplay] ดึงยอดไม่ได้: {error}"));
    }

    private void Paint()
    {
        string coins = UserSession.Coins.ToString();
        string stars = UserSession.Stars.ToString();

        if (coinTexts != null)
            foreach (TMP_Text t in coinTexts) if (t != null) t.text = coins;

        if (starTexts != null)
            foreach (TMP_Text t in starTexts) if (t != null) t.text = stars;

        PaintBars();
    }

    private void PaintBars()
    {
        if (starSliders == null || starSliders.Length == 0) return;

        int total = UserSession.Stars;

        // ดาวสะสมไปเรื่อย ๆ ไม่มีเพดาน ถ้าหาร 20 ตรง ๆ พอเกิน 20 หลอดจะเต็มค้าง
        // แล้วไม่ขยับอีกเลยตลอดเกม — จึงวนรอบใหม่ทุกครั้งที่ครบเป้า
        int progress = barWrapsAtTarget ? total % starsPerBar : Mathf.Min(total, starsPerBar);
        float fill = (float)progress / starsPerBar;

        // ครบเป้าพอดีให้โชว์เต็มหลอด ไม่ใช่ว่างเปล่า — 20 % 20 = 0 ซึ่งดูเหมือน
        // ยังไม่ได้อะไรเลยทั้งที่เพิ่งทำครบ
        if (barWrapsAtTarget && total > 0 && progress == 0) fill = 1f;

        foreach (Slider bar in starSliders)
            if (bar != null) bar.normalizedValue = fill;
    }
}
