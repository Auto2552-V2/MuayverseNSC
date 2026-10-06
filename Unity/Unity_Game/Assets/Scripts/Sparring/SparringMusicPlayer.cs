using System.Collections;
using UnityEngine;

namespace Sparring
{
    /// <summary>
    /// เพลงประกอบของโหมดซ้อม — ใส่ไฟล์แล้ววนลูปตลอดทั้งเกม
    ///
    /// ─── ทำไมไม่ใช้ AudioSource เปล่า ๆ ติ๊ก Loop เอา ───────────────────────────
    /// ทำแบบนั้นก็ได้เพลง แต่จะติดสามอย่าง:
    ///   · เพลงดังเต็มเสียงทันทีตอนเข้าเกม กระแทกหู — ตัวนี้ค่อย ๆ ดังขึ้น
    ///   · ตอนจบเกมเพลงยังดังคลอ กลบเสียงแพ้/ชนะ — ตัวนี้หรี่ลงให้
    ///   · ไม่มีที่ให้ปรับเสียงจากที่อื่น — ตัวนี้มี SetVolume() ให้เรียก
    ///
    /// ─── แยกจาก PunchSoundPlayer ───────────────────────────────────────────────
    /// ตัวนั้นเป็นเสียงสั้น ๆ ซ้อนกันหลายช่อง ส่วนเพลงเป็นเสียงยาวช่องเดียววนลูป
    /// คนละลักษณะกัน รวมกันจะทำให้ voice pool ของเสียงต่อยโดนเพลงกินช่องไปหนึ่ง
    /// </summary>
    public class SparringMusicPlayer : MonoBehaviour
    {
        [Header("เพลง")]
        [Tooltip("ไฟล์เพลงที่จะวนลูป เช่น เสียงปี่มวย")]
        [SerializeField] private AudioClip music;

        [Tooltip("ระดับเสียงเพลง — ตั้งต่ำกว่าเสียงต่อยไว้ ไม่งั้นเพลงจะกลบ")]
        [SerializeField][Range(0f, 1f)] private float volume = 0.4f;

        [Tooltip("เริ่มเล่นเองตอนเข้าเกม")]
        [SerializeField] private bool playOnStart = true;

        [Header("ค่อย ๆ ดัง / ค่อย ๆ เบา")]
        [Tooltip("ใช้เวลากี่วินาทีกว่าจะดังเต็มที่ — 0 = ดังเต็มทันที")]
        [SerializeField][Min(0f)] private float fadeInSeconds = 1.5f;

        [Tooltip("ตอนจบเกมหรี่เสียงลงกี่วินาที — 0 = ตัดทันที")]
        [SerializeField][Min(0f)] private float fadeOutSeconds = 1.5f;

        [Tooltip("ติ๊ก = พอจบเกมหรี่เพลงลงจนเงียบ ให้เสียงแพ้/ชนะเด่นขึ้น\n" +
                 "ไม่ติ๊ก = เล่นต่อไปเรื่อย ๆ")]
        [SerializeField] private bool fadeOutOnBattleEnd = true;

        [Header("ต่อสาย (เว้นว่างได้ จะหาให้เอง)")]
        [Tooltip("ใช้รู้ว่าเกมจบแล้ว เพื่อหรี่เพลงลง")]
        [SerializeField] private SparringEnemyController enemy;

        private AudioSource source;
        private Coroutine fading;

        private void Awake()
        {
            if (enemy == null) enemy = FindFirstObjectByType<SparringEnemyController>();

            // สร้าง AudioSource เองแทนที่จะบังคับให้ลากมาใส่ — ลดขั้นตอนตั้งค่า
            // และกันพลาดเรื่อง playOnAwake/loop ที่ลืมติ๊กแล้วงงว่าทำไมเพลงไม่วน
            source = GetComponent<AudioSource>();
            if (source == null) source = gameObject.AddComponent<AudioSource>();

            source.clip = music;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;    // 2D — เพลงต้องดังเท่ากันไม่ว่ากล้องอยู่ไหน
            source.volume = 0f;          // เริ่มที่เงียบเสมอ แล้วค่อยไล่ขึ้นใน Play()
        }

        private void OnEnable()
        {
            if (enemy != null) enemy.OnBattleEnd += HandleBattleEnd;
        }

        private void OnDisable()
        {
            if (enemy != null) enemy.OnBattleEnd -= HandleBattleEnd;
        }

        private void Start()
        {
            if (music == null)
            {
                Debug.LogWarning($"[{name}] ยังไม่ได้ใส่ไฟล์เพลงในช่อง Music", this);
                return;
            }
            if (playOnStart) Play();
        }

        /// <summary>เริ่มเล่นเพลง (ค่อย ๆ ดังขึ้นตาม Fade In Seconds)</summary>
        public void Play()
        {
            if (music == null) return;
            if (!source.isPlaying) source.Play();
            FadeTo(volume, fadeInSeconds);
        }

        /// <summary>หยุดเพลง (ค่อย ๆ เบาลงตาม Fade Out Seconds)</summary>
        public void Stop() => FadeTo(0f, fadeOutSeconds, stopAtEnd: true);

        /// <summary>ปรับระดับเสียงเป้าหมาย เรียกจากเมนูตั้งค่าได้</summary>
        public void SetVolume(float value)
        {
            volume = Mathf.Clamp01(value);
            FadeTo(volume, 0.2f);
        }

        private void HandleBattleEnd(bool playerWon)
        {
            if (fadeOutOnBattleEnd) Stop();
        }

        private void FadeTo(float target, float seconds, bool stopAtEnd = false)
        {
            // เฟดใหม่ทับเฟดเก่าเสมอ ไม่งั้นสองตัวจะแย่งกันเขียน volume แล้วเสียงสั่น
            if (fading != null) StopCoroutine(fading);
            fading = StartCoroutine(FadeRoutine(target, seconds, stopAtEnd));
        }

        private IEnumerator FadeRoutine(float target, float seconds, bool stopAtEnd)
        {
            float from = source.volume;

            if (seconds > 0f)
            {
                for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
                {
                    // unscaledDeltaTime เพราะเพลงต้องเฟดปกติแม้เกมจะ pause
                    // (Time.timeScale = 0 ตอนเปิดเมนู) ไม่งั้นเฟดจะค้างกลางทาง
                    source.volume = Mathf.Lerp(from, target, t / seconds);
                    yield return null;
                }
            }
            source.volume = target;

            if (stopAtEnd && Mathf.Approximately(target, 0f)) source.Stop();
            fading = null;
        }
    }
}
