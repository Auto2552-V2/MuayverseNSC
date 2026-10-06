using System.Collections.Generic;
using UnityEngine;

public class RhythmNoteSpawner : MonoBehaviour
{
    [SerializeField] private RhythmConductor conductor;
    [SerializeField] private RhythmInputGrid inputGrid;
    [SerializeField] private RhythmNote notePrefab;
    [SerializeField] private Transform noteParent;
    [SerializeField] private Vector3 approachDirection = Vector3.forward;
    [SerializeField] private float hitWindowSeconds = 0.18f;
    [SerializeField] private bool clearOnStop = true;

    private readonly List<RhythmNote> spawnedNotes = new List<RhythmNote>();
    private int nextNoteIndex;
    private SongBeatmap activeBeatmap;

    private void Awake()
    {
        if (conductor == null)
        {
            conductor = FindFirstObjectByType<RhythmConductor>();
        }

        if (inputGrid == null)
        {
            inputGrid = FindFirstObjectByType<RhythmInputGrid>();
        }
    }

    private void OnEnable()
    {
        ResetSpawner();
    }

    private void Update()
    {
        if (conductor == null || conductor.Beatmap == null || notePrefab == null || inputGrid == null)
        {
            return;
        }

        if (activeBeatmap != conductor.Beatmap)
        {
            ResetSpawner();
        }

        if (!conductor.IsPlaying)
        {
            if (clearOnStop && conductor.SongTimeSeconds <= 0f)
            {
                ClearSpawnedNotes();
                nextNoteIndex = 0;
            }

            return;
        }

        SpawnDueNotes(conductor.SongTimeSeconds);
    }

    public void ResetSpawner()
    {
        activeBeatmap = conductor != null ? conductor.Beatmap : null;
        nextNoteIndex = 0;
        ClearSpawnedNotes();

        if (activeBeatmap != null)
        {
            activeBeatmap.SortNotes();
        }
    }

    public void SeekToCurrentSongTime()
    {
        if (conductor == null || conductor.Beatmap == null)
        {
            ResetSpawner();
            return;
        }

        ClearSpawnedNotes();
        activeBeatmap = conductor.Beatmap;
        activeBeatmap.SortNotes();
        float songTime = conductor.SongTimeSeconds;
        nextNoteIndex = 0;

        while (nextNoteIndex < activeBeatmap.Notes.Count)
        {
            BeatmapNote note = activeBeatmap.Notes[nextNoteIndex];
            float hitTime = activeBeatmap.BeatToSeconds(note.startBeat);
            if (hitTime + GetNoteDurationSeconds(note) >= songTime)
            {
                break;
            }

            nextNoteIndex++;
        }

        SpawnDueNotes(songTime);
    }

    private void SpawnDueNotes(float songTime)
    {
        float leadSeconds = activeBeatmap.SpawnLeadSeconds;
        while (nextNoteIndex < activeBeatmap.Notes.Count)
        {
            BeatmapNote note = activeBeatmap.Notes[nextNoteIndex];
            float hitTime = activeBeatmap.BeatToSeconds(note.startBeat);
            if (hitTime - leadSeconds > songTime)
            {
                break;
            }

            SpawnNote(note, hitTime);
            nextNoteIndex++;
        }
    }

    private void SpawnNote(BeatmapNote beatmapNote, float hitTime)
    {
        if (beatmapNote == null || !inputGrid.TryGetSlotTransform(beatmapNote.slotIndex, out Transform targetSlot))
        {
            return;
        }

        RhythmNote spawnedNote = Instantiate(notePrefab, noteParent != null ? noteParent : transform);
        spawnedNote.Initialize(
            inputGrid,
            conductor,
            targetSlot,
            beatmapNote.slotIndex,
            beatmapNote.noteType,
            hitTime,
            GetNoteDurationSeconds(beatmapNote),
            activeBeatmap.NoteSpeed,
            approachDirection,
            hitWindowSeconds);

        spawnedNotes.Add(spawnedNote);
    }

    private float GetNoteDurationSeconds(BeatmapNote note)
    {
        if (note == null || note.noteType != RhythmNoteType.Hold)
        {
            return 0f;
        }

        return Mathf.Max(0f, note.durationBeats) * activeBeatmap.SecondsPerBeat;
    }

    private void ClearSpawnedNotes()
    {
        for (int i = spawnedNotes.Count - 1; i >= 0; i--)
        {
            RhythmNote note = spawnedNotes[i];
            if (note != null)
            {
                Destroy(note.gameObject);
            }
        }

        spawnedNotes.Clear();
    }

    private void OnValidate()
    {
        hitWindowSeconds = Mathf.Max(0.01f, hitWindowSeconds);
        if (approachDirection == Vector3.zero)
        {
            approachDirection = Vector3.forward;
        }
    }
}
