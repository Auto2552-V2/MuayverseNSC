using UnityEngine;

// ล็อกตำแหน่ง/การหมุนของ Main_Character ไว้ตลอด ไม่ให้ root motion จาก PlayerControlller ลากออกไป
// ต้องเป็น LateUpdate เพราะ Animator เขียนค่า Transform หลัง Update จบ
public class CharacterPositionLock : MonoBehaviour
{
    [SerializeField] private Vector3 lockedPosition = new Vector3(666.9781f, 10f, 443.8806f);
    [SerializeField] private Vector3 lockedEulerAngles = new Vector3(0f, 64.523f, 0f);

    void LateUpdate()
    {
        transform.position = lockedPosition;
        transform.rotation = Quaternion.Euler(lockedEulerAngles);
    }
}
