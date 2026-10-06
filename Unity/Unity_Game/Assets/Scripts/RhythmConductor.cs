using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class RhythmConductor : MonoBehaviour
{
    [SerializeField] private SongBeatmap beatmap;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private bool playOnStart = true;
    [SerializeField] private double scheduleLeadSeconds = 0.1d;

    private double songDspStartTime;
    private double pausedSongTime;
    private bool isPlaying;
    private bool isPaused;

    public SongBeatmap Beatmap => beatmap;
    public AudioSource AudioSource => audioSource;
    public bool IsPlaying => isPlaying && !isPaused;
    public float SongTimeSeconds
    {
        get
        {
            if (isPaused)
            {
                return (float)pausedSongTime;
            }

            if (!isPlaying)
            {
                return 0f;
            }

            return Mathf.Max(0f, (float)(AudioSettings.dspTime - songDspStartTime));
        }
    }

    public float SongBeat => beatmap != null ? beatmap.SecondsToBeat(SongTimeSeconds) : 0f;

    private void Awake()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        ApplyBeatmapClip();
    }

    private void Start()
    {
        if (playOnStart)
        {
            Play();
        }
    }

    private void Update()
    {
        if (isPlaying && !isPaused && beatmap != null && beatmap.Song != null && SongTimeSeconds >= beatmap.Song.length)
        {
            Stop();
        }
    }

    public void Play()
    {
        if (beatmap == null || beatmap.Song == null || audioSource == null)
        {
            return;
        }

        ApplyBeatmapClip();
        pausedSongTime = 0d;
        ScheduleFromTime(0d);
    }

    public void Pause()
    {
        if (!isPlaying || isPaused || audioSource == null)
        {
            return;
        }

        pausedSongTime = SongTimeSeconds;
        audioSource.Pause();
        isPaused = true;
    }

    public void Resume()
    {
        if (!isPlaying || !isPaused)
        {
            return;
        }

        ScheduleFromTime(pausedSongTime);
    }

    public void Stop()
    {
        if (audioSource != null)
        {
            audioSource.Stop();
        }

        isPlaying = false;
        isPaused = false;
        pausedSongTime = 0d;
        songDspStartTime = 0d;
    }

    public void Seek(float songTimeSeconds)
    {
        if (beatmap == null || beatmap.Song == null || audioSource == null)
        {
            return;
        }

        double clampedTime = Mathf.Clamp(songTimeSeconds, 0f, beatmap.Song.length);
        if (isPlaying && !isPaused)
        {
            ScheduleFromTime(clampedTime);
            return;
        }

        pausedSongTime = clampedTime;
        audioSource.time = (float)clampedTime;
        isPlaying = true;
        isPaused = true;
    }

    private void ScheduleFromTime(double songTime)
    {
        ApplyBeatmapClip();

        audioSource.Stop();
        audioSource.time = (float)songTime;
        double dspStart = AudioSettings.dspTime + Mathf.Max(0f, (float)scheduleLeadSeconds);
        audioSource.PlayScheduled(dspStart);
        songDspStartTime = dspStart - songTime;
        pausedSongTime = songTime;
        isPlaying = true;
        isPaused = false;
    }

    private void ApplyBeatmapClip()
    {
        if (audioSource != null && beatmap != null)
        {
            audioSource.clip = beatmap.Song;
        }
    }

    private void Reset()
    {
        audioSource = GetComponent<AudioSource>();
        playOnStart = true;
    }

    private void OnValidate()
    {
        scheduleLeadSeconds = Mathf.Max(0f, (float)scheduleLeadSeconds);
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }
    }
}
