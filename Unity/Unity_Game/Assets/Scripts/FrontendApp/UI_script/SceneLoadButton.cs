using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// ปุ่มที่พาไปอีกซีนในเกมเดียวกัน — ใช้กับ Minigame_Mode (ไป SampleScene)
//
// ต่างจาก TherapyLink / DoctorPortalLink ตรงที่สองตัวนั้นพา "ออกจากเกม" ไปหน้าเว็บ
// ส่วนตัวนี้อยู่ในเกมเดิม แค่สลับซีน
//
// ซีนปลายทางต้องอยู่ใน Build Settings ไม่งั้นจะ error ตอนกด ไม่ใช่ตอน build
// (File > Build Profiles > Scene List — ต้องติ๊กเปิดด้วย ซีนที่อยู่ในลิสต์แต่ไม่ติ๊ก
//  ถือว่าโหลดไม่ได้เหมือนกัน)
//
// UserSession เก็บใน PlayerPrefs การสลับซีนจึงไม่หลุดล็อกอิน

[RequireComponent(typeof(Button))]
public class SceneLoadButton : MonoBehaviour
{
    [Header("ซีนปลายทาง")]
    [Tooltip("ชื่อซีนตามที่อยู่ใน Build Settings ไม่ต้องใส่ .unity และไม่ต้องใส่ path")]
    [SerializeField] private string sceneName = "SampleScene";

    // ผูก listener เองตั้งแต่ Awake แบบเดียวกับ DoctorPortalLink/TherapyLink
    // ห้ามเพิ่มใน On Click () ของ Inspector ซ้ำอีก จะโหลดสองรอบ
    private void Awake()
    {
        GetComponent<Button>().onClick.AddListener(Load);
    }

    public void Load()
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogWarning("[SceneLoadButton] ช่อง Scene Name ว่าง");
            return;
        }

        // เช็คก่อนโหลด เพราะ LoadScene ที่หาซีนไม่เจอจะโยน error ที่อ่านไม่รู้เรื่อง
        // ("Scene couldn't be loaded because it isn't added to the build settings")
        // แล้วเกมค้างอยู่หน้าเดิมเฉย ๆ โดยไม่มีอะไรบอกว่าเพราะอะไร
        if (!IsInBuildSettings(sceneName))
        {
            Debug.LogError($"[SceneLoadButton] ไม่มีซีน \"{sceneName}\" ใน Build Settings — " +
                           "เพิ่มที่ File > Build Profiles > Scene List ก่อน");
            return;
        }

        SceneManager.LoadScene(sceneName);
    }

    /// <summary>
    /// ซีนนี้อยู่ใน Build Settings และเปิดใช้งานอยู่ไหม
    ///
    /// ไม่ใช้ Application.CanStreamedLevelBeLoaded() ทั้งที่สั้นกว่า เพราะมันเป็น API
    /// ตกค้างจากยุค streamed Web Player และตอบ false ได้ทั้งที่ซีนอยู่ในลิสต์จริง
    /// เมื่อแพลตฟอร์มที่ active เป็น Web — อาการคือกดปุ่มแล้วขึ้น error ว่าไม่มีซีน
    /// ทั้งที่เพิ่งเพิ่มเข้าไปเรียบร้อย
    ///
    /// การไล่ดู build index ตรง ๆ แบบนี้คือสิ่งที่ LoadScene(string) ใช้จริง
    /// จึงตอบตรงกับผลลัพธ์เสมอไม่ว่าแพลตฟอร์มไหน
    /// </summary>
    public static bool IsInBuildSettings(string name)
    {
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            if (string.IsNullOrEmpty(path)) continue;

            // path เป็น "Assets/Scenes/SparringMode.unity" ส่วน name เป็นชื่อเปล่า
            if (string.Equals(Path.GetFileNameWithoutExtension(path), name,
                              StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
