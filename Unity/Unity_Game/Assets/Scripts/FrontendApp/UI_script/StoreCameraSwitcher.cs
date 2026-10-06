using UnityEngine;

// สลับกล้องตามสถานะของหน้า Store_Page
// เปิด Store_Page -> ใช้ storeCamera / ปิด Store_Page -> ใช้ mainCamera
// แปะไว้บน GameObject ที่ active ตลอด (เช่น _UIController) ไม่ใช่บน Store_Page เอง
// เพราะถ้าแปะบนหน้าที่ถูกปิด สคริปต์จะหยุดทำงานและสลับกล้องกลับไม่ได้
public class StoreCameraSwitcher : MonoBehaviour
{
    [Header("Page")]
    [SerializeField] private GameObject storePage;      // ลาก Store_Page มาใส่

    [Header("Cameras")]
    [SerializeField] private Camera mainCamera;         // ลาก Main Camera มาใส่
    [SerializeField] private Camera storeCamera;        // ลาก Store Camera มาใส่

    private bool? lastState; // null = ยังไม่เคยตั้งค่า จะได้บังคับ apply ครั้งแรกเสมอ

    void LateUpdate()
    {
        bool storeOpen = storePage != null && storePage.activeInHierarchy;

        if (lastState == storeOpen) return;
        lastState = storeOpen;

        if (mainCamera != null) mainCamera.gameObject.SetActive(!storeOpen);
        if (storeCamera != null) storeCamera.gameObject.SetActive(storeOpen);
    }
}
