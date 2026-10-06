using UnityEngine;
using System.Collections;

/// <summary>
/// สุ่มท่าให้ศัตรูในโหมดซ้อม (Sparring Mode) — สุ่มเลือกท่าจากรายชื่อ State ใน Animator
/// Controller แล้วค้างท่านั้นไว้สักพัก จากนั้นสุ่มใหม่ วนไปเรื่อย ๆ
///
/// ท่าที่สุ่มได้ซ้ำกันได้ ไม่มีการกันไม่ให้ออกท่าเดิม
///
/// แต่ละท่าตั้งน้ำหนักได้ว่าให้ออกบ่อยแค่ไหน — ค่าเริ่มต้นตั้งท่าตั้งการ์ด (guard1/guard2)
/// ไว้ที่ 2 ส่วนท่าออกหมัด (enemyjab/enemycross) ไว้ที่ 1 การ์ดจึงออกบ่อยเป็นสองเท่า
///
/// ไม่ต้องตั้ง Parameter หรือลาก Transition ใน Animator เลย สคริปต์ยิง CrossFade เข้า
/// State ตรง ๆ ตามชื่อ ดังนั้นหน้าต่าง Animator จะมี State ลอย ๆ ไม่ต่อเส้นกันก็ใช้ได้
///
/// แนบบนตัวศัตรู (เช่น Enemy_Guard1) แล้วใส่ชื่อ State ให้ตรงกับที่เห็นในหน้าต่าง Animator
/// </summary>
public class EnemyRandomAnimator : MonoBehaviour
{
    /// <summary>ท่าหนึ่งท่าในตารางสุ่ม — ชื่อ State คู่กับน้ำหนักความถี่</summary>
    [System.Serializable]
    public class RandomPose
    {
        [Tooltip("ชื่อ State ใน Animator Controller — ต้องสะกดตรงกับที่เห็นในกล่องสี่เหลี่ยม " +
                 "ของหน้าต่าง Animator เป๊ะ ๆ (ตัวพิมพ์เล็กใหญ่มีผล)")]
        public string stateName;

        [Tooltip("ยิ่งมากยิ่งออกบ่อย เทียบกันเป็นสัดส่วนตรง ๆ\n" +
                 "เช่น 2 จะออกบ่อยเป็นสองเท่าของท่าที่ตั้ง 1\n" +
                 "ตั้ง 0 = ปิดท่านั้นไว้ชั่วคราว ไม่ต้องลบทิ้ง")]
        [Min(0f)] public float weight = 1f;

        [Tooltip("เริ่มเล่นท่านี้ที่เฟรมไหน 0 คือเริ่มตั้งแต่ต้นคลิปตามปกติ " +
                 "ใช้เมื่อช่วงต้นคลิปเป็นส่วนที่ไม่อยากให้เห็น เช่น enemystun ที่ท่าจริงเริ่มตอนเฟรม 110 " +
                 "ถ้าคลิปตั้ง Loop Time ไว้ พอวนรอบสองจะกลับไปเริ่มที่เฟรม 0 เอง " +
                 "ค่านี้มีผลเฉพาะตอนเข้าท่าครั้งแรก")]
        [Min(0f)] public float startFrame = 0f;

        public RandomPose(string stateName, float weight, float startFrame = 0f)
        {
            this.stateName = stateName;
            this.weight = weight;
            this.startFrame = startFrame;
        }
    }

    [Header("ตัวที่จะสั่ง")]
    [Tooltip("เว้นว่างได้ — จะไปหา Animator บนตัวเองหรือลูก ๆ ให้เอง")]
    [SerializeField] private Animator animator;

    [Tooltip("เลเยอร์ใน Animator ที่จะสั่ง ปกติคือ 0 (Base Layer)")]
    [SerializeField][Min(0)] private int layerIndex = 0;

    [Header("ท่าที่จะสุ่ม")]
    [Tooltip("ตารางสุ่ม — ช่อง Weight คุมว่าท่าไหนออกบ่อยกว่ากัน")]
    [SerializeField]
    private RandomPose[] poses =
    {
        new RandomPose("guard1", 2f),
        new RandomPose("guard2", 2f),
        new RandomPose("enemyjab", 1f),
        new RandomPose("enemycross", 1f),
    };

    [Header("จังหวะเปลี่ยนท่า")]
    [Tooltip("ค้างท่าไว้อย่างน้อยกี่วินาทีก่อนสุ่มท่าถัดไป")]
    [SerializeField][Min(0.05f)] private float minHoldDuration = 1f;

    [Tooltip("ค้างท่าไว้อย่างมากกี่วินาที — ตั้งเท่ากับค่าต่ำสุดถ้าอยากให้เปลี่ยนตรงเวลาเป๊ะ")]
    [SerializeField][Min(0.05f)] private float maxHoldDuration = 2.5f;

    [Tooltip("เวลาเกลี่ยรอยต่อระหว่างท่า (วินาที) — 0 = ตัดเปลี่ยนทันทีแบบกระตุก")]
    [SerializeField][Min(0f)] private float crossFadeDuration = 0.2f;

    [Tooltip("เฟรมเรตของคลิปอนิเมชัน ใช้แปลงช่อง Start Frame เป็นวินาที — ดูได้มุมขวาบน " +
             "ของหน้าต่าง Animation ตอนเลือกคลิป ปกติโปรเจกต์นี้คือ 60")]
    [SerializeField][Min(1f)] private float clipFrameRate = 60f;

    [Header("พฤติกรรมตอนสุ่มได้ท่าเดิม")]
    [Tooltip("ติ๊ก = สุ่มได้ท่าเดิมแล้วเริ่มเล่นท่านั้นใหม่ตั้งแต่ต้น (จะเห็นเป็นสะดุดนิดนึง)\n" +
             "ไม่ติ๊ก = ปล่อยให้ท่าเดิมเล่นต่อเนื่องไป แค่ค้างนานขึ้นอีกหนึ่งรอบ (แนะนำ)")]
    [SerializeField] private bool restartOnSamePick = false;

    [Header("ดีบัก")]
    [Tooltip("เปิดแล้วจะพิมพ์ชื่อท่าที่สุ่มได้ พร้อมเปอร์เซ็นต์โอกาสของแต่ละท่าลง Console")]
    [SerializeField] private bool logPicks = false;

    private int[] stateHashes;
    private float[] cumulativeWeights; // ผลรวมสะสม ใช้สุ่มแบบถ่วงน้ำหนักด้วยการไล่หาช่อง
    private float totalWeight;
    private int currentIndex = -1;

    private void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
    }

    private void OnEnable()
    {
        if (!BuildLookupTables()) return;
        currentIndex = -1;
        StartCoroutine(RandomPoseLoop());
    }

    private void OnDisable()
    {
        StopAllCoroutines();
    }

    /// <summary>
    /// แปลงชื่อ State เป็น hash และสร้างตารางน้ำหนักสะสมไว้ล่วงหน้า พร้อมเช็กว่าทุกชื่อ
    /// มีอยู่จริงใน Controller — เช็กตรงนี้ทีเดียวตอนเริ่ม จะได้ไม่ต้องไปงงทีหลังว่า
    /// ทำไมศัตรูไม่ขยับ
    /// </summary>
    private bool BuildLookupTables()
    {
        if (animator == null)
        {
            Debug.LogError($"[EnemyRandomAnimator] {name}: ไม่เจอ Animator — ลากใส่ช่อง Animator ด้วย", this);
            return false;
        }

        if (animator.runtimeAnimatorController == null)
        {
            Debug.LogError($"[EnemyRandomAnimator] {name}: Animator ยังไม่ได้ใส่ Controller", this);
            return false;
        }

        if (poses == null || poses.Length == 0)
        {
            Debug.LogError($"[EnemyRandomAnimator] {name}: ยังไม่ได้ใส่ท่าในตาราง Poses", this);
            return false;
        }

        if (layerIndex >= animator.layerCount)
        {
            Debug.LogError($"[EnemyRandomAnimator] {name}: Layer Index {layerIndex} เกินจำนวนเลเยอร์ " +
                           $"({animator.layerCount} เลเยอร์)", this);
            return false;
        }

        stateHashes = new int[poses.Length];
        cumulativeWeights = new float[poses.Length];
        totalWeight = 0f;

        for (int i = 0; i < poses.Length; i++)
        {
            stateHashes[i] = Animator.StringToHash(poses[i].stateName);

            if (!animator.HasState(layerIndex, stateHashes[i]))
            {
                Debug.LogError($"[EnemyRandomAnimator] {name}: ไม่มี State ชื่อ \"{poses[i].stateName}\" " +
                               $"ในเลเยอร์ {layerIndex} — เช็กตัวสะกดกับหน้าต่าง Animator อีกที", this);
                return false;
            }

            totalWeight += Mathf.Max(0f, poses[i].weight);
            cumulativeWeights[i] = totalWeight;
        }

        if (totalWeight <= 0f)
        {
            Debug.LogError($"[EnemyRandomAnimator] {name}: น้ำหนักรวมเป็น 0 — ต้องมีอย่างน้อยหนึ่งท่า " +
                           "ที่ Weight มากกว่า 0 ไม่งั้นไม่มีอะไรให้สุ่ม", this);
            return false;
        }

        if (logPicks)
        {
            var report = new System.Text.StringBuilder($"[EnemyRandomAnimator] {name}: โอกาสออกของแต่ละท่า");
            for (int i = 0; i < poses.Length; i++)
            {
                float chance = Mathf.Max(0f, poses[i].weight) / totalWeight * 100f;
                report.Append($"\n  {poses[i].stateName} = {chance:0.#}%");
            }
            Debug.Log(report.ToString(), this);
        }

        return true;
    }

    /// <summary>
    /// สุ่มแบบถ่วงน้ำหนัก — ทอยเลขในช่วง 0 ถึงน้ำหนักรวม แล้วดูว่าตกลงช่องไหน
    /// ท่าที่น้ำหนักมากกินช่องกว้างกว่า จึงถูกเลือกบ่อยกว่าตามสัดส่วน
    /// </summary>
    private int PickWeightedIndex()
    {
        float roll = Random.Range(0f, totalWeight);
        for (int i = 0; i < cumulativeWeights.Length; i++)
        {
            if (roll < cumulativeWeights[i]) return i;
        }

        // ตกมาถึงตรงนี้ได้เฉพาะตอน roll ชนขอบบนพอดีจากความคลาดเคลื่อนของทศนิยม
        // คืนช่องสุดท้ายที่น้ำหนักไม่ใช่ 0 กันไม่ให้ไปโผล่ท่าที่ถูกปิดไว้
        for (int i = cumulativeWeights.Length - 1; i >= 0; i--)
        {
            if (poses[i].weight > 0f) return i;
        }

        return 0;
    }

    private IEnumerator RandomPoseLoop()
    {
        while (true)
        {
            // ไม่กันท่าซ้ำ — ท่าเดิมมีสิทธิ์ออกติดกันได้ตามโอกาสของมันตามปกติ
            int pick = PickWeightedIndex();

            if (pick != currentIndex || restartOnSamePick)
            {
                // CrossFadeInFixedTime คิดเวลาเกลี่ยเป็นวินาทีจริง ไม่ใช่สัดส่วนความยาวคลิป
                // ท่าสั้นท่ายาวจึงเกลี่ยเท่ากันหมด
                // พารามิเตอร์ตัวท้ายคือจุดเริ่มเล่นของท่าปลายทาง คิดเป็นวินาที จึงต้อง
                // แปลงจากเฟรมที่ตั้งไว้ด้วยเฟรมเรตของคลิปก่อน
                float startOffset = poses[pick].startFrame / clipFrameRate;
                animator.CrossFadeInFixedTime(stateHashes[pick], crossFadeDuration, layerIndex, startOffset);
                currentIndex = pick;

                if (logPicks) Debug.Log($"[EnemyRandomAnimator] {name}: เปลี่ยนเป็น \"{poses[pick].stateName}\"", this);
            }
            else if (logPicks)
            {
                Debug.Log($"[EnemyRandomAnimator] {name}: สุ่มได้ \"{poses[pick].stateName}\" ซ้ำ — เล่นต่อเนื่อง", this);
            }

            float hold = Random.Range(minHoldDuration, Mathf.Max(minHoldDuration, maxHoldDuration));
            yield return new WaitForSeconds(hold);
        }
    }
}
