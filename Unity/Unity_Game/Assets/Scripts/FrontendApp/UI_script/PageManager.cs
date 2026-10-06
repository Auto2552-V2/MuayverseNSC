using UnityEngine;
using System.Collections.Generic;

// ตัวจัดการหน้าเดียวจบทั้งแอป: เปิดหน้าที่เลือก ปิดหน้าอื่น
// พร้อมสลับไอคอนแถบเมนูให้เข้มที่หน้าปัจจุบัน และซ่อนแถบเมนูในหน้าที่ไม่ต้องการ
// แปะไว้บน GameObject ที่ active ตลอด (เช่น _UIController)
//
// วิธีผูกปุ่ม — ช่องเดียวจบ ไม่ต้องแตะโค้ด:
//   เลือกปุ่ม -> Inspector -> On Click () -> กด +
//   -> ลาก _UIController ใส่ช่อง Object
//   -> เลือก PageManager > ShowPage (GameObject)
//   -> ลาก GameObject ของหน้าปลายทางใส่ช่องว่างข้างล่าง จบ
public class PageManager : MonoBehaviour
{
    // จับคู่หน้ากับไอคอนสีเข้มไว้ในแถวเดียวกัน
    // อยู่ติดกันแบบนี้เลยไม่มีทางจับคู่หลุดเหมือนการใช้สอง list แยกที่ต้องเรียง index ให้ตรงกัน
    [System.Serializable]
    public class Tab
    {
        public GameObject page;        // หน้าของแท็บนี้ เช่น Home_Page
        public GameObject activeIcon;  // ไอคอนสีเข้มของแท็บนี้ (ตัวที่อยู่ใน Button (Onclick))
    }

    [Header("ทุกหน้าที่สลับกันได้ (ใส่ Signin/Login ด้วย)")]
    [SerializeField] private List<GameObject> pages;

    [Header("แท็บล่าง — จับคู่หน้ากับไอคอนสีเข้มของมัน")]
    [SerializeField] private List<Tab> tabs;

    [Header("หน้าที่เปิดตอนเริ่มเกม (เว้นว่าง = ใช้ค่าที่จัดไว้ในซีน)")]
    [SerializeField] private GameObject startPage;

    [Header("แถบเมนูล่าง (เว้นว่างได้ถ้าไม่ใช้)")]
    [SerializeField] private GameObject tabBar;

    [Tooltip("หน้าที่ต้องซ่อนแถบเมนู เช่น Signin_Page, Login_Page")]
    [SerializeField] private List<GameObject> hideTabBarOn;

    private void Start()
    {
        if (startPage != null) ShowPage(startPage);
    }

    // ผูกกับ OnClick ของปุ่ม แล้วลากหน้าปลายทางใส่ช่อง argument
    public void ShowPage(GameObject target)
    {
        // เปิดหน้าที่เลือก ปิดที่เหลือ
        for (int i = 0; i < pages.Count; i++)
        {
            if (pages[i] != null)
                pages[i].SetActive(pages[i] == target);
        }

        // ไอคอนสีเข้มโชว์เฉพาะแท็บที่ตรงกับหน้าปัจจุบัน
        // ถ้าเข้าหน้าที่ไม่มีแท็บ (เช่น Game_Page) จะดับหมดทุกอันเอง
        for (int i = 0; i < tabs.Count; i++)
        {
            if (tabs[i] != null && tabs[i].activeIcon != null)
                tabs[i].activeIcon.SetActive(tabs[i].page == target);
        }

        // แถบเมนูจัดการตรงนี้เลย ไม่ต้องมีสคริปต์แยกคอยเช็คทุกเฟรม
        if (tabBar != null)
            tabBar.SetActive(!hideTabBarOn.Contains(target));
    }
}
