using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

// สั่งหน้าเว็บเปิดกล้องตอนเข้าซีนมินิเกม และปิดตอนออก
//
// แปะไว้บน GameObject ตัวใดตัวหนึ่งใน SampleScene ที่ active ตลอด (เช่น PoseReceiver เลยก็ได้)
//
// ─── ทำไมต้องให้ Unity เป็นคนสั่ง ────────────────────────────────────────────────────
// ถ้าหน้าเว็บเปิดกล้องเองตั้งแต่โหลด เบราว์เซอร์จะเด้งขอสิทธิ์ใส่ผู้เล่นทั้งที่ยังอยู่หน้า
// ล็อกอิน คนส่วนใหญ่กดปฏิเสธ แล้วพอถึงตอนจะเล่นจริงขอใหม่ไม่ได้ ต้องไปแก้ที่ตั้งค่า
// เบราว์เซอร์เอง — ขอตอนกดเข้าเกมจึงเป็นจังหวะที่ผู้เล่นเข้าใจว่าขอไปทำไม
//
// ─── บนเดสก์ท็อป/Editor ไม่ทำอะไร ──────────────────────────────────────────────────
// ที่นั่นกล้องเป็นหน้าที่ของ game_bridge.py ซึ่งยิงเข้ามาทาง UDP ตามเดิม
// สคริปต์นี้จึงว่างเปล่าเมื่อไม่ได้ build เป็น WebGL

public class PoseCameraStarter : MonoBehaviour
{
#if UNITY_WEBGL && !UNITY_EDITOR
    // นิยามอยู่ใน Assets/Plugins/WebGL/SiteNavigation.jslib
    [DllImport("__Internal")] private static extern void TictaStartPoseCamera();
    [DllImport("__Internal")] private static extern void TictaStopPoseCamera();
#endif

    [Tooltip("ปิดกล้องเมื่อออกจากซีนนี้ — ปิดไว้ถ้าอยากให้กล้องค้างข้ามซีน")]
    [SerializeField] private bool stopOnLeave = true;

    private void Start()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        TictaStartPoseCamera();
#else
        Debug.Log("[PoseCameraStarter] ไม่ใช่ WebGL — กล้องเป็นหน้าที่ของ game_bridge.py (UDP)");
#endif
    }

    private void OnDestroy()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (stopOnLeave)
        {
            // ไม่ปิดแล้วไฟกล้องจะค้างติดหลังกลับไปหน้าแอป ผู้เล่นเห็นแล้วตกใจ
            TictaStopPoseCamera();
        }
#endif
    }
}
