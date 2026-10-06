using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// เติมกล่อง "Progress overview" ในหน้า Dashboard ด้วยผลการฝึกจริงของผู้ป่วย
//
// ยิงถาม GET /api/progress?patient=<id> แล้วเติมลงแถวตาม index:
//
//   rows[0] = Shoulder mobility Left    (shoulder_abduction_left)
//   rows[1] = Shoulder mobility Right   (shoulder_abduction_right)
//   rows[2] = Shoulder mobility Combo   (shoulder_abduction_combo)
//
// ลำดับนี้ถูกกำหนดโดย THERAPY_POSES ใน ticta-deploy/api/lib/therapy.ts — ฝั่งนั้น
// ตอบมาเป็นอาร์เรย์เรียงคงที่และครบ 3 ท่าเสมอ ต่อให้ยังไม่เคยฝึกเลย (ได้ 0%)
// เหตุผลเดียวกับ `schedule` ของ /api/quests: JsonUtility อ่าน object ที่ key
// ไม่ตายตัวไม่ได้ เลยต้องส่งมาเป็น array ที่ index มีความหมายแน่นอน
//
// ─── ตัวเลขมาจากไหน ───────────────────────────────────────────────────
//
//   เปอร์เซ็นต์ของท่าหนึ่ง = ผลรวม accuracy ของท่านั้น ÷ จำนวนครั้งที่ฝึกท่านั้น
//
// ฝั่ง API ปัดเป็นจำนวนเต็มมาให้แล้ว ที่นี่จึงไม่ปัดซ้ำ (ปัดสองต่อทำให้เลขเพี้ยนได้)
// ครั้งที่ฝึกจบแต่โมเดลให้คะแนนไม่ทำงานจะนับเป็น "จำนวนครั้ง" แต่ไม่ถ่วงค่าเฉลี่ย
//
// ─── วิธีติดตั้ง ─────────────────────────────────────────────────────
//
//   แปะบน Dashboard_Page (หรือ object "Progress overview" ก็ได้) แล้วลาก Slider
//   กับ accuracy (text) ของ left / right / combo ใส่ตามลำดับ
//
//   แปะบนหน้าที่ถูก SetActive สลับไปมาได้เลย — OnEnable/OnDisable คุม coroutine
//   ให้อยู่แล้ว เข้าหน้าทีก็ยิงถามใหม่ที ออกจากหน้าแล้วหยุด (แบบเดียวกับ QuestLoader)
public class ProgressOverviewLoader : MonoBehaviour
{
    // ช่องหนึ่งชุด = หนึ่งแถวในกล่อง Progress overview
    // ช่องไหนไม่มีใน UI เว้นว่างได้ เช็ค null ทุกตัวก่อนเขียน
    [System.Serializable]
    public class Row
    {
        [Tooltip("แถบเลื่อนของแถวนี้ (object ชื่อ Slider)")]
        public Slider slider;

        [Tooltip("ตัวเลขเปอร์เซ็นต์ (object ชื่อ accuracy) — เติม % ให้เอง")]
        public TMP_Text percentText;

        [Tooltip("จำนวนครั้งที่ฝึกท่านี้ เช่น '5 ครั้ง' (เว้นว่างได้)")]
        public TMP_Text sessionsText;
    }

    [Header("Backend")]
    [Tooltip("ใช้เฉพาะตอนรันใน Editor/PC — ตัวเดียวกับที่ AuthApi/QuestLoader ใช้ " +
             "(dev คือพอร์ต 3000 = service api) WebGL build ไม่สนใจค่านี้")]
    [SerializeField] private TherapyApi api;

    [Tooltip("เว้นว่าง = ใช้ id ของคนที่ล็อกอินอยู่ (UserSession.UserId) " +
             "ใส่เองได้เฉพาะตอนเทสต์ ต้องเป็น uuid จริงจากตาราง patients")]
    [SerializeField] private string patientIdOverride = "";

    [Tooltip("ยิงถามซ้ำทุกกี่วินาที — 0 = ถามครั้งเดียวตอนเปิดหน้า\n" +
             "ค่าเริ่มต้นเผื่อไว้สำหรับกรณีเปิดหน้านี้ค้างไว้ระหว่างที่มีการฝึกจากอุปกรณ์อื่น")]
    [SerializeField] private float pollInterval = 5f;

    [Header("แถว (เรียง ซ้าย → ขวา → สองข้าง)")]
    [SerializeField] private Row[] rows = new Row[3];

    [Header("สรุปรวม (เว้นว่างได้)")]
    [Tooltip("ค่าเฉลี่ยรวมทุกท่า")]
    [SerializeField] private TMP_Text overallText;

    [Tooltip("จำนวนครั้งที่ฝึกทั้งหมด")]
    [SerializeField] private TMP_Text totalSessionsText;

    [Header("การเคลื่อนไหว")]
    [Tooltip("ความเร็วที่แถบไหลไปหาค่าใหม่ (หน่วยต่อวินาที, 1 = เต็มแถบใน 1 วินาที)\n" +
             "0 = กระโดดไปเลยไม่ต้องไหล")]
    [SerializeField] private float fillSpeed = 1.5f;

    // ค่าเป้าหมายของแต่ละแถบ 0-1 — Update ค่อยไล่ค่าจริงเข้าหาทีละเฟรม
    private float[] targets;

    private string PatientId =>
        string.IsNullOrEmpty(patientIdOverride) ? UserSession.UserId : patientIdOverride;

    private void Awake()
    {
        targets = new float[rows.Length];
        if (api == null) api = FindAnyObjectByType<TherapyApi>();
    }

    private void OnEnable()
    {
        // เปิดหน้ามาให้เป็นศูนย์ไว้ก่อน กันตัวเลขของผู้ป่วยคนก่อนค้างให้เห็นระหว่างรอ
        // response (สลับบัญชีบนเครื่องเดียวกันเกิดขึ้นจริงในงานนี้)
        ResetRows();
        StartCoroutine(Loop());
    }

    // หน้านี้ถูกเปิด/ปิดด้วย SetActive ทุกครั้งที่สลับหน้า ถ้าไม่หยุด coroutine
    // Loop() จะซ้อนกันเพิ่มขึ้นทุกรอบที่กลับเข้าหน้านี้ แล้วยิง request ถี่ขึ้นเรื่อย ๆ
    private void OnDisable()
    {
        StopAllCoroutines();
    }

    private IEnumerator Loop()
    {
        while (true)
        {
            Fetch();
            if (pollInterval <= 0f) yield break;
            yield return new WaitForSeconds(pollInterval);
        }
    }

    private void Fetch()
    {
        string patientId = PatientId;

        // ยังไม่ล็อกอิน — ไม่มีอะไรให้ดึง อย่ายิง request รัว ๆ ทิ้ง
        if (string.IsNullOrEmpty(patientId)) return;
        if (api == null)
        {
            Debug.LogWarning("[ProgressOverview] ยังไม่ได้ลาก TherapyApi ใส่ช่อง Api");
            return;
        }

        api.GetProgress(patientId, Render, error =>
        {
            // เน็ตสะดุดชั่วคราว — คงเลขเดิมไว้ดีกว่าล้างจอให้กะพริบ
            Debug.LogWarning($"[ProgressOverview] ดึงความคืบหน้าไม่ได้: {error}");
        });
    }

    private void Render(TherapyProgressResult data)
    {
        if (data == null || data.poses == null) return;

        for (int i = 0; i < rows.Length; i++)
        {
            Row row = rows[i];
            if (row == null) continue;

            // มีแถวใน UI มากกว่าท่าที่ API รู้จัก — ปล่อยแถวเกินไว้ที่ศูนย์
            if (i >= data.poses.Length)
            {
                SetRow(i, row, 0, 0, 0);
                continue;
            }

            PoseProgress pose = data.poses[i];
            SetRow(i, row, pose.averageAccuracy, pose.sessions, pose.scoredSessions);
        }

        if (overallText != null) overallText.text = data.overallAccuracy + "%";
        if (totalSessionsText != null) totalSessionsText.text = data.totalSessions + " ครั้ง";
    }

    private void SetRow(int index, Row row, int percent, int sessions, int scoredSessions)
    {
        if (row.percentText != null)
        {
            // ฝึกไปแล้วแต่ประเมินไม่ได้สักครั้ง (โมเดลให้คะแนนโหลดไม่ขึ้น) — โชว์ 0%
            // จะกลายเป็นการบอกว่า "ทำได้แย่มาก" ทั้งที่ไม่เคยมีใครวัดได้เลย
            // ยังไม่เคยฝึกเลยต่างหากที่ 0% ถูกต้อง เพราะยังไม่มีอะไรให้แสดง
            bool unmeasured = sessions > 0 && scoredSessions == 0;
            // เลขบนจอเป็นจำนวนเต็มเสมอ — ค่าที่ API ส่งมาปัดมาแล้ว ไม่ปัดซ้ำ
            row.percentText.text = unmeasured ? "—" : percent + "%";
        }
        if (row.sessionsText != null) row.sessionsText.text = sessions + " ครั้ง";

        if (index < targets.Length) targets[index] = Mathf.Clamp01(percent / 100f);

        // ไม่ต้องรอไหล ถ้าตั้งความเร็วเป็น 0 หรือแถบยังไม่เคยมีค่า
        if (fillSpeed <= 0f && row.slider != null)
            row.slider.normalizedValue = Mathf.Clamp01(percent / 100f);
    }

    private void ResetRows()
    {
        for (int i = 0; i < rows.Length; i++)
        {
            if (rows[i] == null) continue;
            if (i < targets.Length) targets[i] = 0f;
            if (rows[i].slider != null) rows[i].slider.normalizedValue = 0f;
            if (rows[i].percentText != null) rows[i].percentText.text = "0%";
            if (rows[i].sessionsText != null) rows[i].sessionsText.text = "0 ครั้ง";
        }
        if (overallText != null) overallText.text = "0%";
        if (totalSessionsText != null) totalSessionsText.text = "0 ครั้ง";
    }

    // ไล่แถบเข้าหาค่าเป้าหมายทีละเฟรม ให้เห็นว่ามัน "ขยับ" ตอนเปิดหน้ามา
    // ไม่ใช่โผล่มาเต็มแถบตั้งแต่เฟรมแรกจนไม่รู้ว่าอันไหนเปลี่ยนไปบ้าง
    private void Update()
    {
        if (fillSpeed <= 0f) return;

        for (int i = 0; i < rows.Length && i < targets.Length; i++)
        {
            Slider slider = rows[i]?.slider;
            if (slider == null) continue;

            float current = slider.normalizedValue;
            if (Mathf.Approximately(current, targets[i])) continue;

            slider.normalizedValue =
                Mathf.MoveTowards(current, targets[i], fillSpeed * Time.unscaledDeltaTime);
        }
    }
}
