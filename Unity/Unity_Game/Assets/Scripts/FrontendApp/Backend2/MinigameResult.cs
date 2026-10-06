// ผลแพ้/ชนะของมินิเกม (SampleScene) ที่ต้องข้ามซีนไปโชว์บนหน้า Reward ซึ่งอยู่ในซีน FrontendAPP
//
// ใช้ static ธรรมดาพอ ไม่ต้องพึ่ง PlayerPrefs เพราะ SceneManager.LoadScene สลับซีนอยู่ใน
// process เดิม (WebGL ก็ไม่ได้รีโหลดหน้าเว็บ) ค่าจึงอยู่ครบถึงปลายทางแน่นอน
//
// และตั้งใจให้ "อ่านแล้วหาย" (Consume) — ไม่งั้นผู้เล่นเข้า ๆ ออก ๆ หน้าหลักทีไร
// หน้ารางวัลของแมตช์เก่าจะเด้งขึ้นมาซ้ำทุกครั้ง
public static class MinigameResult
{
    private static bool hasPending;
    private static bool pendingWon;

    // รางวัลที่เซิร์ฟเวอร์จ่ายจริงสำหรับแมตช์นี้ — แยกจาก hasPending เพราะคำตอบ
    // จาก API อาจมาช้ากว่าการจบแมตช์ (หรือไม่มาเลยถ้าเน็ตล่ม) หน้า Reward จึง
    // ต้องแยกได้ว่า "ยังไม่รู้ยอด" กับ "รู้แล้วว่าได้ศูนย์"
    private static bool hasReward;
    private static int rewardCoinEarned, rewardStarEarned, rewardCoins, rewardStars;

    /// <summary>บันทึกผลแมตช์ที่เพิ่งจบ ก่อนสั่งโหลดซีนหน้า Reward</summary>
    public static void Set(bool won)
    {
        hasPending = true;
        pendingWon = won;
    }

    /// <summary>
    /// บันทึกรางวัลที่เซิร์ฟเวอร์จ่ายจริง — เรียกตอน API ตอบกลับ
    ///
    /// ตัวเลขพวกนี้ต้องมาจากคำตอบของเซิร์ฟเวอร์เท่านั้น ห้ามให้ฝั่งเกมคำนวณเอง
    /// ไม่งั้นจอจะโชว์เลขที่ไม่ตรงกับยอดจริงในฐานข้อมูล
    /// </summary>
    public static void SetReward(int coinEarned, int starEarned, int coins, int stars)
    {
        hasReward = true;
        rewardCoinEarned = coinEarned;
        rewardStarEarned = starEarned;
        rewardCoins = coins;
        rewardStars = stars;
    }

    /// <summary>อ่านผลที่ค้างอยู่แล้วล้างทิ้ง — คืน false เมื่อไม่มีผลรออยู่</summary>
    public static bool Consume(out bool won)
    {
        won = pendingWon;
        bool had = hasPending;

        hasPending = false;
        pendingWon = false;
        return had;
    }

    /// <summary>
    /// อ่านรางวัลที่ค้างอยู่แล้วล้างทิ้ง — คืน false เมื่อยังไม่รู้ยอด
    /// (API ยังไม่ตอบ, ยิงไม่สำเร็จ, หรือปิด grantReward ไว้)
    /// </summary>
    public static bool ConsumeReward(out int coinEarned, out int starEarned,
                                     out int coins, out int stars)
    {
        coinEarned = rewardCoinEarned;
        starEarned = rewardStarEarned;
        coins = rewardCoins;
        stars = rewardStars;

        bool had = hasReward;
        hasReward = false;
        rewardCoinEarned = rewardStarEarned = rewardCoins = rewardStars = 0;
        return had;
    }
}
