using UnityEngine;

/// <summary>
/// ตรึงตำแหน่ง local ของกระดูกสะโพก (mixamorig:Hips) ไว้ทุกเฟรม
///
/// ทำไมต้องล็อกที่กระดูก ไม่ใช่ที่ GameObject:
/// ริกชุดนี้เป็น Humanoid (animationType: 3) เวลา Unity retarget อนิเมชัน มันจะเขียนตำแหน่งของ
/// Hips ใหม่ทุกเฟรมโดยอ้างจาก root ของตัวละคร ดังนั้นแม้จะล็อก transform ของตัวละครไว้แน่นแล้ว
/// (PlayerMovement.LateUpdate / CharacterPositionLock) ตัวโมเดลก็ยัง "ไถล" ออกไปได้
/// เพราะมันไถลอยู่ข้างในตัวเอง — ตัวเลขที่ขยับจึงไปโผล่ที่ mixamorig:Hips ไม่ใช่ที่ตัวละคร
///
/// ทางแก้ที่ต้นเหตุจริงๆ คือเปิด Bake Into Pose ของ Root Transform Position (XZ) ในคลิปทุกตัว
/// (ตอนนี้ m_LoopBlendPositionXZ: 0 = ปิดอยู่ทั้งหมด) ตัวนี้เป็นตัวปิดจ็อบฝั่งรันไทม์
/// สำหรับกรณีที่ไม่อยากไปยุ่งกับ import settings ของคลิป
///
/// ต้องเป็น LateUpdate เพราะลำดับของ Unity คือ Update -> Animator เขียนกระดูก -> LateUpdate
/// ค่าที่เซ็ตใน Update จะถูก Animator ทับทิ้งเสมอ
/// </summary>
// รันท้ายสุด กันสคริปต์อื่นที่ทำงานใน LateUpdate เหมือนกันมาทับทีหลัง
[DefaultExecutionOrder(200)]
public class HipsPositionLock : MonoBehaviour
{
    [Header("กระดูกที่จะล็อก")]
    [Tooltip("เว้นว่างไว้ได้ — จะหา Hips จาก Avatar ของ Animator ให้เอง " +
             "(อ่านจากริก ไม่ได้พึ่งชื่อ \"mixamorig:\" จึงไม่พังถ้าเปลี่ยนโมเดล)")]
    [SerializeField] private Transform hips;

    [Header("ล็อกแกนไหน")]
    [Tooltip("ซ้าย-ขวา — ต้นเหตุอาการไถลออกข้าง")]
    [SerializeField] private bool lockX = true;

    [Tooltip("ขึ้น-ลง — ติ๊กแล้วตัวจะนิ่งสนิทที่สุด แต่จะแข็งทื่อ เพราะการย่อ/ยืดตัว " +
             "กับจังหวะยุบตอนโดนต่อยจะหายไปด้วย ปกติปล่อยว่างไว้")]
    [SerializeField] private bool lockY = false;

    [Tooltip("หน้า-หลัง — ต้นเหตุอาการเดินหน้าเอง")]
    [SerializeField] private bool lockZ = true;

    [Header("เผื่อระยะขยับ")]
    [Tooltip("ระยะที่ยอมให้ Hips ขยับจากจุดตั้งต้นได้ (หน่วย local ของตัวละคร)\n" +
             "0 = ตรึงนิ่งสนิท | มากกว่า 0 = ขยับได้ในรัศมีนี้ แล้วค่อยถูกรีดกลับ " +
             "ช่วยให้ท่าหลบยังดูมีน้ำหนักอยู่แต่ไม่ไถลหนีจอ")]
    [SerializeField][Min(0f)] private float allowedDrift = 0f;

    [Header("ดีบัก")]
    [SerializeField] private bool logResolvedBone = true;

    // จุดตั้งต้นของ Hips ที่จับไว้ก่อน Animator เขียนเฟรมแรก = ท่า bind pose ที่ผูกมากับโมเดล
    private Vector3 basePosition;

    private void Start()
    {
        if (hips == null)
        {
            hips = ResolveHips();
        }

        if (hips == null)
        {
            Debug.LogError($"[HipsPositionLock] หากระดูก Hips บน {name} ไม่เจอ — ปิดการทำงานตัวเอง " +
                           "ลองลากกระดูกใส่ช่อง Hips เองใน Inspector");
            enabled = false;
            return;
        }

        basePosition = hips.localPosition;

        if (logResolvedBone)
        {
            Debug.Log($"[HipsPositionLock] ล็อก {hips.name} ของ {name} ไว้ที่ {basePosition} " +
                      $"(แกน X:{lockX} Y:{lockY} Z:{lockZ}, เผื่อระยะ {allowedDrift})");
        }
    }

    private void LateUpdate()
    {
        if (hips == null) return;

        Vector3 current = hips.localPosition;

        // แกนที่ไม่ได้ติ๊ก ปล่อยค่าจาก Animator ผ่านไปตามเดิม
        Vector3 target = new Vector3(
            lockX ? basePosition.x : current.x,
            lockY ? basePosition.y : current.y,
            lockZ ? basePosition.z : current.z);

        if (allowedDrift > 0f)
        {
            // รีดค่าเข้าหา target แต่ยอมให้ค้างห่างได้ไม่เกิน allowedDrift
            // ClampMagnitude ทำงานบนเวกเตอร์รวม จึงได้ขอบเขตเป็นทรงกลม ไม่ใช่กล่อง
            Vector3 drift = Vector3.ClampMagnitude(current - target, allowedDrift);
            target += drift;
        }

        hips.localPosition = target;
    }

    /// <summary>หา Hips จาก Avatar ของ Animator — แม่นกว่าเทียบชื่อ เพราะอ่านจากตัวริกโดยตรง</summary>
    private Transform ResolveHips()
    {
        Animator animator = GetComponentInChildren<Animator>();

        if (animator != null && animator.isHuman)
        {
            Transform bone = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (bone != null) return bone;
        }

        // ริกที่ไม่ใช่ Humanoid ยังพอถอยมาเทียบชื่อได้
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            if (child.name.EndsWith("Hips", System.StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }

        return null;
    }

    /// <summary>ยึดตำแหน่งปัจจุบันเป็นจุดตั้งต้นใหม่ (เช่นหลังเปลี่ยนท่ายืนตั้งต้น)</summary>
    public void Recapture()
    {
        if (hips == null) return;

        basePosition = hips.localPosition;
        Debug.Log($"[HipsPositionLock] ยึดจุดตั้งต้นใหม่ของ {hips.name} = {basePosition}");
    }
}
