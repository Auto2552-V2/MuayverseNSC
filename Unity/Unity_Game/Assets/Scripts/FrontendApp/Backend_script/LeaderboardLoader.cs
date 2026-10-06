using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

// เติมหน้า Leaderboard_Page จาก GET /api/leaderboard
//
//   No1..No5   -> topSlots  (อันดับ 1-5 ของทุกคน)
//   Your_top   -> mySlot    (อันดับของผู้ใช้ที่ล็อกอินอยู่)
//
// การเรียงอันดับทำที่ฝั่ง server ทั้งหมด (ดาวมาก -> น้อย, เท่ากันเอาคนสมัครก่อน)
// ฝั่งนี้แค่เอาผลมาแปะ ไม่ต้องเรียงซ้ำ ไม่งั้นสองฝั่งจะเพี้ยนจากกันได้
//
// URL ผ่าน ApiConfig เสมอ — WebGL ยิง "/api/..." แบบ same-origin บนเซิร์ฟเวอร์
// ของงาน ส่วนใน Editor ใช้ baseUrl ที่ตั้งใน Inspector
public class LeaderboardLoader : MonoBehaviour
{
    // หนึ่งแถวในตาราง — ช่องไหนไม่มีก็เว้นว่างได้ (เช่นเลขอันดับที่พิมพ์ตายไว้ในซีนแล้ว)
    [Serializable]
    public class Slot
    {
        public GameObject root;      // ทั้งแถว ปิดเมื่อยังไม่มีคนในอันดับนี้
        public TMP_Text rankText;    // "#1"
        public TMP_Text nameText;
        public TMP_Text starText;
        public Image avatar;         // รูปโปรไฟล์ (โหลดจาก avatarUrl ถ้ามี)
    }

    [Header("Backend")]
    [Tooltip("ใช้เฉพาะใน Editor — WebGL ยิง /api/ ตรงจาก origin เดียวกัน")]
    [SerializeField] private string baseUrl = "http://localhost:3000";

    [Header("อันดับ 1-5 — เรียงให้ตรงกับ No1..No5")]
    [SerializeField] private Slot[] topSlots = new Slot[5];

    [Header("กล่องของผู้ใช้เอง (Your_top)")]
    [SerializeField] private Slot mySlot;

    [Tooltip("ข้อความอันดับตอนยังไม่ล็อกอินหรือหาอันดับไม่เจอ")]
    [SerializeField] private string unrankedText = "-";

    // โหลดใหม่ทุกครั้งที่เปิดหน้า ไม่ใช่ครั้งเดียวตอนซีนโหลด
    // ดาวเปลี่ยนทุกครั้งที่เล่นจบ ถ้าโหลดครั้งเดียวจะเห็นของเก่าค้าง
    private void OnEnable()
    {
        Refresh();
    }

    // ผูกกับปุ่ม refresh ได้ถ้าต้องการ
    public void Refresh()
    {
        if (!isActiveAndEnabled) return;
        StopAllCoroutines();
        StartCoroutine(Load());
    }

    private IEnumerator Load()
    {
        string path = $"/api/leaderboard?limit={Mathf.Max(1, topSlots.Length)}";

        string patientId = UserSession.UserId;
        if (!string.IsNullOrEmpty(patientId))
            path += "&patient=" + UnityWebRequest.EscapeURL(patientId);

        // กัน cache ของเบราว์เซอร์ตอนเป็น WebGL — อันดับต้องสดเสมอ
        path += "&_=" + DateTime.Now.Ticks;

        using (UnityWebRequest req = UnityWebRequest.Get(ApiConfig.Url(baseUrl, path)))
        {
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"โหลดอันดับไม่สำเร็จ: {req.error}");
                yield break;
            }

            LeaderboardResponse res =
                JsonUtility.FromJson<LeaderboardResponse>(req.downloadHandler.text);

            if (res == null)
            {
                Debug.LogWarning("อ่าน JSON อันดับไม่ได้");
                yield break;
            }

            ApplyTop(res.top);
            ApplyMe(res.me);
        }
    }

    private void ApplyTop(LeaderEntry[] top)
    {
        for (int i = 0; i < topSlots.Length; i++)
        {
            bool has = top != null && i < top.Length;
            Fill(topSlots[i], has ? top[i] : null, hideWhenEmpty: true);
        }
    }

    private void ApplyMe(LeaderEntry me)
    {
        // rank = 0 คือ server หาไม่เจอ (ยังไม่ล็อกอิน / id ไม่ตรง)
        // กล่อง Your_top ไม่ซ่อน เพราะมันเป็นส่วนหนึ่งของหน้า ไม่ใช่แถวในตาราง
        bool ranked = me != null && me.rank > 0;
        Fill(mySlot, ranked ? me : null, hideWhenEmpty: false);

        if (!ranked && mySlot != null)
        {
            if (mySlot.rankText != null) mySlot.rankText.text = unrankedText;
            if (mySlot.starText != null) mySlot.starText.text = "0";
            if (mySlot.nameText != null)
                mySlot.nameText.text = PlayerPrefs.GetString(UserSession.KeyName, "");
        }
    }

    private void Fill(Slot slot, LeaderEntry entry, bool hideWhenEmpty)
    {
        if (slot == null) return;

        bool has = entry != null;
        if (slot.root != null && hideWhenEmpty) slot.root.SetActive(has);
        if (!has) return;

        if (slot.rankText != null) slot.rankText.text = "#" + entry.rank;
        if (slot.nameText != null) slot.nameText.text = entry.name;
        if (slot.starText != null) slot.starText.text = entry.stars.ToString();

        // ยังไม่มีรูปก็ปล่อยรูป default ที่ตั้งไว้ในซีนไว้เหมือนเดิม
        // อย่าเคลียร์ sprite ทิ้ง ไม่งั้นจะได้กล่องขาวแทนที่จะเป็นรูปคนสีเทา
        if (slot.avatar != null && !string.IsNullOrEmpty(entry.avatarUrl))
            StartCoroutine(LoadAvatar(slot.avatar, entry.avatarUrl));
    }

    private IEnumerator LoadAvatar(Image target, string url)
    {
        using (UnityWebRequest req = UnityWebRequestTexture.GetTexture(url))
        {
            yield return req.SendWebRequest();

            // รูปโหลดไม่ขึ้นไม่ใช่เรื่องคอขาดบาดตาย ปล่อยรูป default ไว้เงียบ ๆ
            if (req.result != UnityWebRequest.Result.Success) yield break;
            if (target == null) yield break;   // เปลี่ยนหน้าไปแล้วระหว่างรอโหลด

            Texture2D tex = DownloadHandlerTexture.GetContent(req);
            target.sprite = Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f)
            );
            target.color = Color.white;
        }
    }
}

// ── รูป JSON ที่ /api/leaderboard ส่งกลับ ────────────────────────────────
// ชื่อ field ต้องตรงกับ LeaderEntry ใน api/lib/leaderboard.ts เป๊ะ ๆ
// JsonUtility จับคู่ด้วยชื่อ ถ้าไม่ตรงจะได้ค่าว่างแบบเงียบ ๆ ไม่มี error

[Serializable]
public class LeaderEntry
{
    public int rank;
    public string patientId;
    public string patientCode;
    public string name;
    public int stars;
    public string avatarUrl;
    public string avatarColor;
}

[Serializable]
public class LeaderboardResponse
{
    public LeaderEntry[] top;
    public LeaderEntry me;
}
