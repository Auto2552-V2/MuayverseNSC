using UnityEngine;

/// <summary>
/// เสียงต่อยกลางของสนาม — ใช้ร่วมกันทั้งผู้เล่น (PlayerMovement) และศัตรู (EnemyBrain)
///
/// แนบบน Main Camera ที่เดียวกับ CameraShake / CameraFovEffect แล้วใส่ไฟล์เสียงในช่อง
/// Punch Clips ครั้งเดียว จากนั้นทุกหมัดของทั้งสองฝ่ายจะมีเสียงเอง ไม่ต้องไปตั้งทีละท่า
///
/// ถ้าท่าไหนอยากได้เสียงเฉพาะตัว ค่อยไปใส่ช่อง Punch Sound ของท่านั้นทับ
/// (PlayerAttack.punchSound ในอาร์เรย์ attacks / EnemyBrain.punchSound)
/// </summary>
public class PunchSoundPlayer : MonoBehaviour
{
    public static PunchSoundPlayer Instance { get; private set; }

    [Header("เสียงต่อย")]
    [Tooltip("ใส่ไฟล์เสียงที่นี่ — ใส่ได้หลายไฟล์ จะสุ่มสลับให้ทุกครั้งที่ต่อย " +
             "เสียงจะได้ไม่ซ้ำเป๊ะจนน่าเบื่อ (ใส่ไฟล์เดียวก็ได้)")]
    [SerializeField] private AudioClip[] punchClips;

    [Header("ระดับเสียง")]
    [SerializeField][Range(0f, 1f)] private float volume = 1f;

    [Tooltip("สุ่มเปลี่ยนความสูงต่ำของเสียงบวก/ลบเท่านี้ กันเสียงซ้ำเป๊ะทุกหมัด — 0 = ปิด")]
    [SerializeField][Range(0f, 0.5f)] private float pitchJitter = 0.08f;

    [Header("ช่องเสียงพร้อมกัน")]
    [Tooltip("จำนวนเสียงที่ซ้อนกันได้ — ถ้าหมัดออกรัวกว่านี้ เสียงนัดเก่าจะถูกตัดกลางคัน")]
    [SerializeField][Min(1)] private int voiceCount = 4;

    [Header("ดีบัก")]
    [SerializeField] private bool logPunchSound = false;

    private AudioSource[] voices;
    private int nextVoice;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[PunchSoundPlayer] พบ PunchSoundPlayer ซ้ำ — ลบตัวที่ซ้ำออก");
            Destroy(this);
            return;
        }

        Instance = this;
        BuildVoices();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    // สร้าง AudioSource ไว้หลายตัวแทนที่จะใช้ตัวเดียว เพราะ pitch เป็นค่าของ AudioSource
    // ไม่ใช่ของเสียงแต่ละนัด — ถ้าใช้ตัวเดียวแล้วสุ่ม pitch ใหม่ตอนหมัดที่สองออก
    // เสียงหมัดแรกที่ยังเล่นค้างอยู่จะเพี้ยนตามไปด้วย
    private void BuildVoices()
    {
        voices = new AudioSource[Mathf.Max(1, voiceCount)];

        for (int i = 0; i < voices.Length; i++)
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            // 2D — เสียงหมัดต้องดังเท่ากันไม่ว่ากล้องจะอยู่ตรงไหนของสนาม
            source.spatialBlend = 0f;
            voices[i] = source;
        }
    }

    /// <summary>
    /// เล่นเสียงต่อย 1 นัด เรียกได้จากทุกที่โดยไม่ต้องถืออ้างอิง
    /// overrideClip = เสียงเฉพาะของท่านั้น เว้น null ไว้จะสุ่มจาก Punch Clips
    /// </summary>
    public static void PlayGlobal(AudioClip overrideClip = null, string debugOwner = "")
    {
        if (Instance == null)
        {
            // ไม่ใช่เรื่องคอขาดบาดตาย เกมเล่นต่อได้ แค่ไม่มีเสียง จึงเป็น warning ไม่ใช่ error
            Debug.LogWarning("[PunchSoundPlayer] ไม่พบ PunchSoundPlayer ในฉาก — " +
                             "แนบสคริปต์บน Main Camera แล้วใส่ไฟล์เสียงในช่อง Punch Clips");
            return;
        }

        Instance.Play(overrideClip, debugOwner);
    }

    public void Play(AudioClip overrideClip = null, string debugOwner = "")
    {
        AudioClip clip = overrideClip != null ? overrideClip : PickClip();

        // ยังไม่ได้ใส่ไฟล์เสียง — เงียบไปเฉยๆ ไม่ต้องถล่ม Console ทุกหมัด
        if (clip == null) return;

        if (voices == null || voices.Length == 0)
        {
            BuildVoices();
        }

        AudioSource source = TakeVoice();
        source.pitch = pitchJitter > 0f ? 1f + Random.Range(-pitchJitter, pitchJitter) : 1f;
        source.PlayOneShot(clip, volume);

        if (logPunchSound)
        {
            string owner = string.IsNullOrEmpty(debugOwner) ? "unknown" : debugOwner;
            Debug.Log($"[PunchSoundPlayer] {owner} เล่นเสียง {clip.name} " +
                      $"(pitch={source.pitch:F2}, volume={volume:F2})");
        }
    }

    // เลือกช่องที่ว่างก่อน ค่อยวนกลับมาทับช่องที่เก่าสุดเมื่อเต็มจริงๆ
    // ถ้าวน round-robin ตรงๆ ตั้งแต่แรก เสียงจะถูกตัดทิ้งทั้งที่ยังมีช่องว่างเหลือ
    private AudioSource TakeVoice()
    {
        for (int i = 0; i < voices.Length; i++)
        {
            if (!voices[i].isPlaying) return voices[i];
        }

        AudioSource source = voices[nextVoice];
        nextVoice = (nextVoice + 1) % voices.Length;
        return source;
    }

    private AudioClip PickClip()
    {
        if (punchClips == null || punchClips.Length == 0) return null;
        if (punchClips.Length == 1) return punchClips[0];

        return punchClips[Random.Range(0, punchClips.Length)];
    }
}
