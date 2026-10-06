using UnityEngine;
using UnityEngine.UI;

public class UIController : MonoBehaviour
{
    [Header("UI Pages")]
    public GameObject pageA; // ลาก GameObject หน้า A มาใส่
    public GameObject pageB; // ลาก GameObject หน้า B มาใส่

    [Header("UI Buttons")]
    public Button buttonC;   // ลาก "ปุ่ม C" (ที่อยู่ในหน้า A) มาใส่
    public Button buttonD;   // ลาก "ปุ่ม D" (ที่อยู่ในหน้า B) มาใส่

    void Start()
    {
        // เมื่อคลิกปุ่ม C ให้ทำงานที่ฟังก์ชัน ClickButtonC
        if (buttonC != null)
        {
            buttonC.onClick.AddListener(ClickButtonC);
        }

        // เมื่อคลิกปุ่ม D ให้ทำงานที่ฟังก์ชัน ClickButtonD
        if (buttonD != null)
        {
            buttonD.onClick.AddListener(ClickButtonD);
        }
    }

    // ฟังก์ชันเมื่อกดปุ่ม C (ซ่อนหน้า A -> แสดงหน้า B)
    void ClickButtonC()
    {
        if (pageA != null) pageA.SetActive(false);
        if (pageB != null) pageB.SetActive(true);
    }

    // ฟังก์ชันเมื่อกดปุ่ม D (ซ่อนหน้า B -> แสดงหน้า A)
    void ClickButtonD()
    {
        if (pageB != null) pageB.SetActive(false);
        if (pageA != null) pageA.SetActive(true);
    }
}