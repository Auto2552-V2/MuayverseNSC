using System;
using System.Collections.Generic;
using UnityEngine;

public enum RhythmNoteType
{
    Tap,
    Catch,
    Hold
}

[Serializable]
public class BeatmapNote
{
    [Range(0, RhythmInputGrid.SlotCount - 1)] public int slotIndex;
    [Min(0f)] public float startBeat;
    [Min(0f)] public float durationBeats;
    public RhythmNoteType noteType;

    public float EndBeat => startBeat + Mathf.Max(0f, durationBeats);
}

[CreateAssetMenu(fileName = "SongBeatmap", menuName = "TICTA/Rhythm/Song Beatmap")]
public class SongBeatmap : ScriptableObject
{
    [SerializeField] private AudioClip song;
    [SerializeField] private float bpm = 120f;
    [SerializeField] private float songOffsetSeconds;
    [SerializeField] private int snapDivision = 4;
    [SerializeField] private float noteSpeed = 5f;
    [SerializeField] private float spawnDistance = 20f;
    [SerializeField] private List<BeatmapNote> notes = new List<BeatmapNote>();

    public AudioClip Song => song;
    public float Bpm => Mathf.Max(1f, bpm);
    public float SongOffsetSeconds => songOffsetSeconds;
    public int SnapDivision => Mathf.Max(1, snapDivision);
    public float NoteSpeed => Mathf.Max(0.01f, noteSpeed);
    public float SpawnDistance => Mathf.Max(0f, spawnDistance);
    public IReadOnlyList<BeatmapNote> Notes => notes;
    public List<BeatmapNote> EditableNotes => notes;
    public float SecondsPerBeat => 60f / Bpm;
    public float SpawnLeadSeconds => SpawnDistance / NoteSpeed;
    public float SongLengthSeconds => song != null ? song.length : 0f;
    public float SongLengthBeats => SecondsToBeat(SongLengthSeconds);

    public float BeatToSeconds(float beat)
    {
        return songOffsetSeconds + beat * SecondsPerBeat;
    }

    public float SecondsToBeat(float seconds)
    {
        return (seconds - songOffsetSeconds) / SecondsPerBeat;
    }

    public float SnapBeat(float beat)
    {
        float step = 1f / SnapDivision;
        return Mathf.Max(0f, Mathf.Round(beat / step) * step);
    }

    public void SortNotes()
    {
        notes.Sort((left, right) =>
        {
            int beatComparison = left.startBeat.CompareTo(right.startBeat);
            return beatComparison != 0 ? beatComparison : left.slotIndex.CompareTo(right.slotIndex);
        });
    }

    public List<string> ValidateNotes()
    {
        List<string> issues = new List<string>();
        float lengthBeats = SongLengthBeats;

        for (int i = 0; i < notes.Count; i++)
        {
            BeatmapNote note = notes[i];
            if (note == null)
            {
                issues.Add($"Note {i} is null.");
                continue;
            }

            if (note.slotIndex < 0 || note.slotIndex >= RhythmInputGrid.SlotCount)
            {
                issues.Add($"Note {i} has invalid slot {note.slotIndex}.");
            }

            if (note.startBeat < 0f)
            {
                issues.Add($"Note {i} starts before beat 0.");
            }

            if (note.durationBeats < 0f)
            {
                issues.Add($"Note {i} has negative duration.");
            }

            if (song != null && note.startBeat > lengthBeats)
            {
                issues.Add($"Note {i} starts after the song ends.");
            }

            if (song != null && note.EndBeat > lengthBeats)
            {
                issues.Add($"Note {i} ends after the song ends.");
            }
        }

        return issues;
    }

    private void OnValidate()
    {
        bpm = Mathf.Max(1f, bpm);
        snapDivision = Mathf.Max(1, snapDivision);
        noteSpeed = Mathf.Max(0.01f, noteSpeed);
        spawnDistance = Mathf.Max(0f, spawnDistance);

        for (int i = 0; i < notes.Count; i++)
        {
            BeatmapNote note = notes[i];
            if (note == null)
            {
                continue;
            }

            note.slotIndex = Mathf.Clamp(note.slotIndex, 0, RhythmInputGrid.SlotCount - 1);
            note.startBeat = Mathf.Max(0f, note.startBeat);
            note.durationBeats = Mathf.Max(0f, note.durationBeats);
            if (note.noteType != RhythmNoteType.Hold)
            {
                note.durationBeats = 0f;
            }
        }
    }
}
