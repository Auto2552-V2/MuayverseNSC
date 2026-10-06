using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class SwapUIManager : MonoBehaviour
{
    [Header("UI Elements Setup (เรียงลำดับให้ตรงกัน)")]
    [SerializeField] private List<Button> menuButtons;      // ช่องสำหรับใส่ปุ่ม A, B, C, D
    [SerializeField] private List<GameObject> canvasPages; // ช่องสำหรับใส่หน้า Canvas E, F, G, H

    [Header("Startup")]
    // ปิดไว้เมื่อเกมต้องเริ่มที่หน้า Signin/Login
    // ถ้าเปิด จะเด้งไปหน้าแรกใน canvasPages ทันทีที่กด Play
    [SerializeField] private bool openFirstPageOnStart = false;

    void Start()
    {
        // วนลูปเพื่อผูกฟังก์ชันการคลิกให้กับทุกปุ่มตามลำดับอัตโนมัติ
        for (int i = 0; i < menuButtons.Count; i++)
        {
            int index = i; // บันทึก index ไว้สำหรับใช้งานใน Delegate
            if (menuButtons[i] != null)
            {
                menuButtons[i].onClick.AddListener(() => OnMenuButtonClicked(index));
            }
        }

        if (openFirstPageOnStart && canvasPages.Count > 0)
        {
            OnMenuButtonClicked(0);
        }
    }

    // ฟังก์ชันหลักในการเปิดหน้าจอที่เลือก และซ่อนหน้าจออื่นๆ
    // public เพื่อให้ LoginController/SignupController เรียกเปิดหน้า Home ได้หลังล็อกอินสำเร็จ
    public void OnMenuButtonClicked(int targetIndex)
    {
        for (int i = 0; i < canvasPages.Count; i++)
        {
            if (canvasPages[i] != null)
            {
                // ถ้า i ตรงกับปุ่มที่กดให้แสดงผล (true) ถ้าไม่ตรงให้ซ่อน (false)
                canvasPages[i].SetActive(i == targetIndex);
            }
        }
    }
}