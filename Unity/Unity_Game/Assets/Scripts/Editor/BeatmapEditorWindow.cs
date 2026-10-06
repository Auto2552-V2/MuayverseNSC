using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class BeatmapEditorWindow : EditorWindow
{
    private const float LaneHeight = 34f;
    private const float HeaderWidth = 42f;
    private const float TimelinePadding = 8f;
    private const float MinPixelsPerBeat = 28f;
    private const float NoteHeight = 22f;
    private const float WaveformHeight = 112f;
    private const int WaveformResolution = 1400;

    private SongBeatmap beatmap;
    private SerializedObject serializedBeatmap;
    private Vector2 scrollPosition;
    private float pixelsPerBeat = 80f;
    private float playheadSeconds;
    private double previewDspStart;
    private float previewStartSeconds;
    private bool isPreviewPlaying;
    private bool isRecording;
    private int recordSlotIndex = -1;
    private float recordStartBeat;
    private RhythmNoteType recordDurationType = RhythmNoteType.Hold;
    private int liveRecordSlotIndex = -1;
    private float liveRecordStartBeat;
    private int selectedNoteIndex = -1;
    private int draggingNoteIndex = -1;
    private int resizingNoteIndex = -1;
    private Vector2 dragMouseOffset;
    private float dragOriginalBeat;
    private float resizeOriginalDuration;
    private AudioClip waveformClip;
    private float[] waveformPeaks;
    private bool waveformAvailable;
    private string waveformError;
    private bool waveformScrubbing;
    private bool resumeAfterWaveformScrub;
    private bool followPlayhead = true;
    private float timelineViewportWidth;
    private float timelineContentWidth;

    [MenuItem("TICTA/Rhythm/Beatmap Editor")]
    public static void Open()
    {
        GetWindow<BeatmapEditorWindow>("Beatmap Editor");
    }

    private void OnEnable()
    {
        EditorApplication.update += EditorUpdate;
    }

    private void OnDisable()
    {
        EditorApplication.update -= EditorUpdate;
        StopPreview();
    }

    private void OnGUI()
    {
        DrawToolbar();

        if (beatmap == null)
        {
            EditorGUILayout.HelpBox("Select or create a SongBeatmap asset to start mapping.", MessageType.Info);
            return;
        }

        EnsureSerializedBeatmap();
        serializedBeatmap.Update();

        DrawBeatmapFields();
        DrawTransport();
        DrawRecordControls();
        DrawTimeline();
        DrawSelectedNoteInspector();
        DrawValidationControls();

        serializedBeatmap.ApplyModifiedProperties();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        EditorGUI.BeginChangeCheck();
        beatmap = (SongBeatmap)EditorGUILayout.ObjectField(beatmap, typeof(SongBeatmap), false, GUILayout.MinWidth(240f));
        if (EditorGUI.EndChangeCheck())
        {
            serializedBeatmap = beatmap != null ? new SerializedObject(beatmap) : null;
            selectedNoteIndex = -1;
            StopPreview();
        }

        if (GUILayout.Button("Create", EditorStyles.toolbarButton, GUILayout.Width(64f)))
        {
            CreateBeatmapAsset();
        }

        GUILayout.FlexibleSpace();
        followPlayhead = GUILayout.Toggle(followPlayhead, "Follow", EditorStyles.toolbarButton, GUILayout.Width(58f));
        EditorGUILayout.LabelField("Zoom", GUILayout.Width(36f));
        pixelsPerBeat = GUILayout.HorizontalSlider(pixelsPerBeat, MinPixelsPerBeat, 180f, GUILayout.Width(120f));
        EditorGUILayout.EndHorizontal();
    }

    private void DrawBeatmapFields()
    {
        EditorGUILayout.PropertyField(serializedBeatmap.FindProperty("song"));
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(serializedBeatmap.FindProperty("bpm"));
        EditorGUILayout.PropertyField(serializedBeatmap.FindProperty("songOffsetSeconds"));
        EditorGUILayout.PropertyField(serializedBeatmap.FindProperty("snapDivision"));
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(serializedBeatmap.FindProperty("noteSpeed"));
        EditorGUILayout.PropertyField(serializedBeatmap.FindProperty("spawnDistance"));
        EditorGUILayout.EndHorizontal();
    }

    private void DrawTransport()
    {
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button(isPreviewPlaying ? "Stop" : "Play", GUILayout.Width(80f)))
        {
            if (isPreviewPlaying)
            {
                StopPreview();
                playheadSeconds = 0f;
            }
            else
            {
                PlayPreview(playheadSeconds);
            }
        }

        if (GUILayout.Button("Stop", GUILayout.Width(70f)))
        {
            StopPreview();
            playheadSeconds = 0f;
        }

        float songLength = Mathf.Max(0f, beatmap.SongLengthSeconds);
        EditorGUI.BeginChangeCheck();
        playheadSeconds = EditorGUILayout.Slider(playheadSeconds, 0f, Mathf.Max(0.01f, songLength));
        if (EditorGUI.EndChangeCheck())
        {
            if (isPreviewPlaying)
            {
                PlayPreview(playheadSeconds);
            }
        }

        EditorGUILayout.LabelField($"{playheadSeconds:0.00}s / Beat {beatmap.SecondsToBeat(playheadSeconds):0.##}", GUILayout.Width(150f));
        EditorGUILayout.EndHorizontal();
    }

    private void DrawRecordControls()
    {
        EditorGUILayout.BeginHorizontal();
        isRecording = GUILayout.Toggle(isRecording, "Record", "Button", GUILayout.Width(90f));
        EditorGUILayout.LabelField("Slot", GUILayout.Width(28f));
        int nextSlot = EditorGUILayout.IntSlider(recordSlotIndex < 0 ? 0 : recordSlotIndex, 0, RhythmInputGrid.SlotCount - 1);
        if (!isRecording)
        {
            recordSlotIndex = -1;
            StopLiveRecordNote();
        }

        if (GUILayout.Button("Tap", GUILayout.Width(60f)))
        {
            AddNote(nextSlot, beatmap.SnapBeat(beatmap.SecondsToBeat(playheadSeconds)), 0f, RhythmNoteType.Tap);
        }

        if (GUILayout.Button("Catch", GUILayout.Width(68f)))
        {
            AddNote(nextSlot, beatmap.SnapBeat(beatmap.SecondsToBeat(playheadSeconds)), 0f, RhythmNoteType.Catch);
        }

        if (recordSlotIndex < 0)
        {
            if (GUILayout.Button("Hold Start", GUILayout.Width(92f)))
            {
                recordSlotIndex = nextSlot;
                recordStartBeat = beatmap.SnapBeat(beatmap.SecondsToBeat(playheadSeconds));
                recordDurationType = RhythmNoteType.Hold;
            }
        }
        else if (GUILayout.Button("Hold End", GUILayout.Width(92f)))
        {
            float endBeat = beatmap.SnapBeat(beatmap.SecondsToBeat(playheadSeconds));
            AddNote(recordSlotIndex, recordStartBeat, Mathf.Max(1f / beatmap.SnapDivision, endBeat - recordStartBeat), recordDurationType);
            recordSlotIndex = -1;
        }

        EditorGUILayout.LabelField(recordSlotIndex >= 0
            ? $"Manual hold slot {recordSlotIndex}"
            : "In Play Mode, Record captures the hovered RhythmInputGrid slot automatically.");
        EditorGUILayout.EndHorizontal();
    }

    private void DrawTimeline()
    {
        float totalBeats = Mathf.Max(16f, beatmap.SongLengthBeats + 2f);
        float timelineHeight = 36f + RhythmInputGrid.SlotCount * LaneHeight + WaveformHeight;
        timelineViewportWidth = Mathf.Max(1f, position.width - 8f);
        Rect timelineRect = GUILayoutUtility.GetRect(0f, timelineHeight, GUILayout.ExpandWidth(true));
        timelineViewportWidth = timelineRect.width;
        timelineContentWidth = Mathf.Max(timelineViewportWidth,
            HeaderWidth + totalBeats * pixelsPerBeat + TimelinePadding * 2f);

        Rect contentRect = new Rect(0f, 0f, timelineContentWidth, timelineRect.height);
        scrollPosition = GUI.BeginScrollView(timelineRect, scrollPosition, contentRect);

        DrawBeatGrid(contentRect, totalBeats);
        DrawNotes();
        DrawWaveform(totalBeats);
        DrawPlayhead(contentRect);
        HandleTimelineEvents(contentRect);
        HandleWaveformEvents(totalBeats);

        GUI.EndScrollView();
    }

    private void DrawBeatGrid(Rect contentRect, float totalBeats)
    {
        Handles.BeginGUI();
        for (int lane = 0; lane < RhythmInputGrid.SlotCount; lane++)
        {
            float y = 28f + lane * LaneHeight;
            Rect laneRect = new Rect(0f, y, contentRect.width, LaneHeight);
            EditorGUI.DrawRect(laneRect, lane % 2 == 0 ? new Color(0.18f, 0.18f, 0.18f) : new Color(0.14f, 0.14f, 0.14f));
            GUI.Label(new Rect(4f, y + 8f, HeaderWidth - 8f, 20f), lane.ToString(), EditorStyles.boldLabel);
        }

        for (int beat = 0; beat <= Mathf.CeilToInt(totalBeats); beat++)
        {
            float x = BeatToX(beat);
            Handles.color = beat % 4 == 0 ? new Color(1f, 1f, 1f, 0.35f) : new Color(1f, 1f, 1f, 0.15f);
            Handles.DrawLine(new Vector3(x, 22f), new Vector3(x, 28f + RhythmInputGrid.SlotCount * LaneHeight));
            if (beat % 4 == 0)
            {
                GUI.Label(new Rect(x + 2f, 2f, 50f, 18f), beat.ToString());
            }
        }
        Handles.EndGUI();
    }

    private void DrawNotes()
    {
        List<BeatmapNote> notes = beatmap.EditableNotes;
        for (int i = 0; i < notes.Count; i++)
        {
            BeatmapNote note = notes[i];
            Rect rect = GetNoteRect(note);
            Color color = GetNoteColor(note.noteType);
            if (i == selectedNoteIndex)
            {
                color = Color.Lerp(color, Color.white, 0.35f);
            }

            EditorGUI.DrawRect(rect, color);
            GUI.Label(rect, GetNoteLabel(note), EditorStyles.centeredGreyMiniLabel);
        }
    }

    private void DrawPlayhead(Rect contentRect)
    {
        float beat = beatmap.SecondsToBeat(playheadSeconds);
        float x = BeatToX(beat);
        Handles.BeginGUI();
        Handles.color = Color.red;
        Handles.DrawLine(new Vector3(x, 20f), new Vector3(x, 28f + RhythmInputGrid.SlotCount * LaneHeight));
        float waveformTop = 32f + RhythmInputGrid.SlotCount * LaneHeight;
        Handles.DrawLine(new Vector3(x, waveformTop), new Vector3(x, waveformTop + WaveformHeight));
        Handles.EndGUI();
    }

    private void DrawWaveform(float totalBeats)
    {
        Rect waveformRect = GetWaveformRect(totalBeats);
        float waveformTop = waveformRect.y;
        float waveformWidth = waveformRect.width;
        EditorGUI.DrawRect(waveformRect, new Color(0.08f, 0.1f, 0.12f));
        GUI.Label(new Rect(4f, waveformTop + 5f, HeaderWidth - 8f, 18f), "Audio", EditorStyles.boldLabel);

        EnsureWaveformCache();
        Rect graphRect = new Rect(HeaderWidth + TimelinePadding, waveformTop + 20f,
            Mathf.Max(1f, waveformWidth - HeaderWidth - TimelinePadding * 2f), WaveformHeight - 30f);
        EditorGUI.DrawRect(graphRect, new Color(0.12f, 0.15f, 0.17f));

        if (waveformAvailable && waveformPeaks != null && waveformPeaks.Length > 1)
        {
            Handles.BeginGUI();
            Handles.color = new Color(0.25f, 0.8f, 0.95f, 0.9f);
            float halfHeight = graphRect.height * 0.5f;
            float centerY = graphRect.y + halfHeight;
            for (int i = 0; i < waveformPeaks.Length - 1; i++)
            {
                float x1 = graphRect.x + graphRect.width * i / (waveformPeaks.Length - 1f);
                float x2 = graphRect.x + graphRect.width * (i + 1) / (waveformPeaks.Length - 1f);
                float amplitude1 = waveformPeaks[i] * (halfHeight - 2f);
                float amplitude2 = waveformPeaks[i + 1] * (halfHeight - 2f);
                Handles.DrawLine(new Vector3(x1, centerY - amplitude1), new Vector3(x2, centerY - amplitude2));
                Handles.DrawLine(new Vector3(x1, centerY + amplitude1), new Vector3(x2, centerY + amplitude2));
            }
            Handles.color = new Color(1f, 1f, 1f, 0.15f);
            Handles.DrawLine(new Vector3(graphRect.x, centerY), new Vector3(graphRect.xMax, centerY));
            Handles.EndGUI();
        }
        else
        {
            GUI.Label(graphRect, string.IsNullOrEmpty(waveformError)
                ? "No audio clip selected"
                : waveformError, EditorStyles.centeredGreyMiniLabel);
        }
    }

    private Rect GetWaveformRect(float totalBeats)
    {
        float waveformTop = 32f + RhythmInputGrid.SlotCount * LaneHeight;
        float waveformWidth = HeaderWidth + TimelinePadding + totalBeats * pixelsPerBeat + TimelinePadding;
        return new Rect(0f, waveformTop, waveformWidth, WaveformHeight);
    }

    private void HandleWaveformEvents(float totalBeats)
    {
        Event current = Event.current;
        Rect waveformRect = GetWaveformRect(totalBeats);
        Rect graphRect = new Rect(waveformRect.x + HeaderWidth + TimelinePadding, waveformRect.y + 20f,
            Mathf.Max(1f, waveformRect.width - HeaderWidth - TimelinePadding * 2f), WaveformHeight - 30f);

        if (waveformScrubbing && current.type == EventType.MouseDrag && current.button == 0)
        {
            SetPlayheadFromWaveform(current.mousePosition.x);
            current.Use();
            return;
        }

        if (waveformScrubbing && current.type == EventType.MouseUp && current.button == 0)
        {
            SetPlayheadFromWaveform(current.mousePosition.x);
            waveformScrubbing = false;
            if (resumeAfterWaveformScrub)
                PlayPreview(playheadSeconds);
            resumeAfterWaveformScrub = false;
            current.Use();
            return;
        }

        if (!graphRect.Contains(current.mousePosition))
        {
            return;
        }

        if (current.type == EventType.MouseDown && current.button == 0)
        {
            waveformScrubbing = true;
            resumeAfterWaveformScrub = isPreviewPlaying;
            if (resumeAfterWaveformScrub)
                StopPreview();
            current.Use();
        }
    }

    private void SetPlayheadFromWaveform(float mouseX)
    {
        float beat = XToBeat(mouseX);
        playheadSeconds = Mathf.Clamp(beatmap.BeatToSeconds(beat), 0f, beatmap.SongLengthSeconds);
        Repaint();
    }

    private void EnsureWaveformCache()
    {
        AudioClip clip = beatmap != null ? beatmap.Song : null;
        if (clip == waveformClip)
        {
            return;
        }

        waveformClip = clip;
        waveformPeaks = null;
        waveformAvailable = false;
        waveformError = null;
        if (clip == null)
        {
            waveformError = "Assign an AudioClip above";
            return;
        }

        int resolution = Mathf.Clamp(Mathf.CeilToInt(clip.length / Mathf.Max(0.01f, clip.length) * WaveformResolution), 128, WaveformResolution);
        int samplesPerPeak = Mathf.Max(1, clip.samples / resolution);
        int channels = Mathf.Max(1, clip.channels);
        float[] buffer = new float[samplesPerPeak * channels];
        waveformPeaks = new float[resolution];

        try
        {
            for (int peakIndex = 0; peakIndex < waveformPeaks.Length; peakIndex++)
            {
                int sampleOffset = peakIndex * samplesPerPeak;
                int sampleCount = Mathf.Min(samplesPerPeak, clip.samples - sampleOffset);
                if (sampleCount <= 0 || !clip.GetData(buffer, sampleOffset))
                {
                    waveformPeaks = null;
                    waveformError = "Enable Decompress On Load or a readable import setting for waveform preview.";
                    return;
                }

                float peak = 0f;
                int values = sampleCount * channels;
                for (int i = 0; i < values; i++)
                {
                    peak = Mathf.Max(peak, Mathf.Abs(buffer[i]));
                }
                waveformPeaks[peakIndex] = peak;
            }
            waveformAvailable = true;
        }
        catch (System.Exception exception)
        {
            waveformPeaks = null;
            waveformError = "Waveform unavailable: " + exception.Message;
        }
    }

    private void HandleTimelineEvents(Rect contentRect)
    {
        Event current = Event.current;
        float noteAreaBottom = 28f + RhythmInputGrid.SlotCount * LaneHeight;
        if (!contentRect.Contains(current.mousePosition) || current.mousePosition.y > noteAreaBottom)
        {
            return;
        }

        if (current.type == EventType.MouseDown && current.button == 0)
        {
            int noteIndex = FindNoteAt(current.mousePosition);
            if (noteIndex >= 0)
            {
                SelectNote(noteIndex);
                Rect noteRect = GetNoteRect(beatmap.EditableNotes[noteIndex]);
                if (Mathf.Abs(current.mousePosition.x - noteRect.xMax) < 8f && beatmap.EditableNotes[noteIndex].noteType == RhythmNoteType.Hold)
                {
                    resizingNoteIndex = noteIndex;
                    resizeOriginalDuration = beatmap.EditableNotes[noteIndex].durationBeats;
                }
                else
                {
                    draggingNoteIndex = noteIndex;
                    dragOriginalBeat = beatmap.EditableNotes[noteIndex].startBeat;
                    dragMouseOffset = current.mousePosition - noteRect.position;
                }

                current.Use();
            }
            else
            {
                int lane = MouseToLane(current.mousePosition.y);
                if (lane >= 0)
                {
                    float beat = beatmap.SnapBeat(XToBeat(current.mousePosition.x));
                    AddNote(lane, beat, 0f, RhythmNoteType.Tap);
                    current.Use();
                }
            }
        }

        if (current.type == EventType.MouseDrag && current.button == 0)
        {
            if (draggingNoteIndex >= 0)
            {
                MoveSelectedNote(current.mousePosition);
                current.Use();
            }
            else if (resizingNoteIndex >= 0)
            {
                ResizeSelectedNote(current.mousePosition);
                current.Use();
            }
        }

        if (current.type == EventType.MouseUp)
        {
            draggingNoteIndex = -1;
            resizingNoteIndex = -1;
        }

        if (current.type == EventType.KeyDown && selectedNoteIndex >= 0)
        {
            if (current.keyCode == KeyCode.Delete || current.keyCode == KeyCode.Backspace)
            {
                DeleteSelectedNote();
                current.Use();
            }
            else if (current.control && current.keyCode == KeyCode.D)
            {
                DuplicateSelectedNote();
                current.Use();
            }
        }
    }

    private void DrawSelectedNoteInspector()
    {
        if (selectedNoteIndex < 0 || selectedNoteIndex >= beatmap.EditableNotes.Count)
        {
            return;
        }

        BeatmapNote note = beatmap.EditableNotes[selectedNoteIndex];
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Selected Note", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        note.slotIndex = EditorGUILayout.IntSlider("Slot", note.slotIndex, 0, RhythmInputGrid.SlotCount - 1);
        note.noteType = (RhythmNoteType)EditorGUILayout.EnumPopup("Type", note.noteType);
        note.startBeat = EditorGUILayout.FloatField("Start Beat", note.startBeat);
        note.durationBeats = note.noteType == RhythmNoteType.Hold ? EditorGUILayout.FloatField("Duration Beats", note.durationBeats) : 0f;
        if (EditorGUI.EndChangeCheck())
        {
            note.startBeat = beatmap.SnapBeat(note.startBeat);
            note.durationBeats = note.noteType == RhythmNoteType.Hold ? Mathf.Max(1f / beatmap.SnapDivision, beatmap.SnapBeat(note.durationBeats)) : 0f;
            MarkDirty();
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Duplicate"))
        {
            DuplicateSelectedNote();
        }

        if (GUILayout.Button("Delete"))
        {
            DeleteSelectedNote();
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawValidationControls()
    {
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Sort"))
        {
            Undo.RecordObject(beatmap, "Sort Beatmap Notes");
            beatmap.SortNotes();
            MarkDirty();
        }

        if (GUILayout.Button("Validate"))
        {
            List<string> issues = beatmap.ValidateNotes();
            string message = issues.Count == 0 ? "Beatmap is valid." : string.Join("\n", issues);
            EditorUtility.DisplayDialog("Beatmap Validation", message, "OK");
        }

        if (GUILayout.Button("Save Asset"))
        {
            EditorUtility.SetDirty(beatmap);
            AssetDatabase.SaveAssets();
        }
        EditorGUILayout.EndHorizontal();
    }

    private void EditorUpdate()
    {
        if (!isPreviewPlaying || beatmap == null || beatmap.Song == null)
        {
            return;
        }

        playheadSeconds = Mathf.Clamp(previewStartSeconds + (float)(EditorApplication.timeSinceStartup - previewDspStart), 0f, beatmap.Song.length);
        FollowPlayhead();
        if (isRecording)
        {
            UpdateLiveRecording();
        }

        if (playheadSeconds >= beatmap.Song.length)
        {
            StopLiveRecordNote();
            PausePreview();
        }

        Repaint();
    }

    private void FollowPlayhead()
    {
        if (!followPlayhead || timelineViewportWidth <= 1f || timelineContentWidth <= timelineViewportWidth)
        {
            return;
        }

        float playheadX = BeatToX(beatmap.SecondsToBeat(playheadSeconds));
        float leftMargin = 90f;
        float rightMargin = 140f;
        float visibleLeft = scrollPosition.x;
        float visibleRight = scrollPosition.x + timelineViewportWidth;
        if (playheadX > visibleRight - rightMargin)
        {
            scrollPosition.x = Mathf.Min(timelineContentWidth - timelineViewportWidth,
                playheadX - timelineViewportWidth + rightMargin);
        }
        else if (playheadX < visibleLeft + leftMargin)
        {
            scrollPosition.x = Mathf.Max(0f, playheadX - leftMargin);
        }
    }

    private void PlayPreview(float startSeconds)
    {
        if (beatmap == null || beatmap.Song == null)
        {
            return;
        }

        BeatmapEditorAudio.StopAll();
        playheadSeconds = Mathf.Clamp(startSeconds, 0f, beatmap.Song.length);
        int startSample = Mathf.Clamp(Mathf.RoundToInt(playheadSeconds * beatmap.Song.frequency), 0, beatmap.Song.samples - 1);
        BeatmapEditorAudio.Play(beatmap.Song, startSample);
        previewStartSeconds = playheadSeconds;
        previewDspStart = EditorApplication.timeSinceStartup;
        isPreviewPlaying = true;
    }

    private void PausePreview()
    {
        if (beatmap != null && beatmap.Song != null)
        {
            BeatmapEditorAudio.Stop(beatmap.Song);
        }

        isPreviewPlaying = false;
    }

    private void UpdateLiveRecording()
    {
        RhythmInputGrid grid = FindFirstObjectByType<RhythmInputGrid>();
        int hoveredSlot = grid != null ? grid.ActiveSlotIndex : -1;
        if (hoveredSlot == liveRecordSlotIndex)
        {
            return;
        }

        StopLiveRecordNote();

        if (hoveredSlot >= 0)
        {
            liveRecordSlotIndex = hoveredSlot;
            liveRecordStartBeat = beatmap.SnapBeat(beatmap.SecondsToBeat(playheadSeconds));
        }
    }

    private void StopLiveRecordNote()
    {
        if (liveRecordSlotIndex < 0 || beatmap == null)
        {
            liveRecordSlotIndex = -1;
            return;
        }

        float endBeat = beatmap.SnapBeat(beatmap.SecondsToBeat(playheadSeconds));
        float duration = Mathf.Max(0f, endBeat - liveRecordStartBeat);
        float minimumHoldBeats = 1f / beatmap.SnapDivision;
        RhythmNoteType noteType = duration >= minimumHoldBeats * 2f ? RhythmNoteType.Hold : RhythmNoteType.Tap;
        AddNote(liveRecordSlotIndex, liveRecordStartBeat, noteType == RhythmNoteType.Hold ? duration : 0f, noteType);
        liveRecordSlotIndex = -1;
    }

    private void StopPreview()
    {
        BeatmapEditorAudio.StopAll();
        isPreviewPlaying = false;
    }

    private void AddNote(int slotIndex, float startBeat, float durationBeats, RhythmNoteType noteType)
    {
        Undo.RecordObject(beatmap, "Add Beatmap Note");
        BeatmapNote note = new BeatmapNote
        {
            slotIndex = Mathf.Clamp(slotIndex, 0, RhythmInputGrid.SlotCount - 1),
            startBeat = beatmap.SnapBeat(startBeat),
            durationBeats = noteType == RhythmNoteType.Hold ? Mathf.Max(1f / beatmap.SnapDivision, beatmap.SnapBeat(durationBeats)) : 0f,
            noteType = noteType
        };

        beatmap.EditableNotes.Add(note);
        selectedNoteIndex = beatmap.EditableNotes.Count - 1;
        MarkDirty();
    }

    private void SelectNote(int noteIndex)
    {
        selectedNoteIndex = noteIndex;
        GUI.FocusControl(null);
        Repaint();
    }

    private void MoveSelectedNote(Vector2 mousePosition)
    {
        BeatmapNote note = beatmap.EditableNotes[draggingNoteIndex];
        Undo.RecordObject(beatmap, "Move Beatmap Note");
        note.startBeat = beatmap.SnapBeat(XToBeat(mousePosition.x - dragMouseOffset.x));
        note.slotIndex = Mathf.Clamp(MouseToLane(mousePosition.y), 0, RhythmInputGrid.SlotCount - 1);
        MarkDirty();
    }

    private void ResizeSelectedNote(Vector2 mousePosition)
    {
        BeatmapNote note = beatmap.EditableNotes[resizingNoteIndex];
        Undo.RecordObject(beatmap, "Resize Beatmap Note");
        float endBeat = beatmap.SnapBeat(XToBeat(mousePosition.x));
        note.durationBeats = Mathf.Max(1f / beatmap.SnapDivision, endBeat - note.startBeat);
        note.noteType = RhythmNoteType.Hold;
        MarkDirty();
    }

    private void DeleteSelectedNote()
    {
        if (selectedNoteIndex < 0 || selectedNoteIndex >= beatmap.EditableNotes.Count)
        {
            return;
        }

        Undo.RecordObject(beatmap, "Delete Beatmap Note");
        beatmap.EditableNotes.RemoveAt(selectedNoteIndex);
        selectedNoteIndex = Mathf.Min(selectedNoteIndex, beatmap.EditableNotes.Count - 1);
        MarkDirty();
    }

    private void DuplicateSelectedNote()
    {
        if (selectedNoteIndex < 0 || selectedNoteIndex >= beatmap.EditableNotes.Count)
        {
            return;
        }

        BeatmapNote source = beatmap.EditableNotes[selectedNoteIndex];
        Undo.RecordObject(beatmap, "Duplicate Beatmap Note");
        beatmap.EditableNotes.Add(new BeatmapNote
        {
            slotIndex = source.slotIndex,
            startBeat = beatmap.SnapBeat(source.startBeat + 1f / beatmap.SnapDivision),
            durationBeats = source.durationBeats,
            noteType = source.noteType
        });
        selectedNoteIndex = beatmap.EditableNotes.Count - 1;
        MarkDirty();
    }

    private int FindNoteAt(Vector2 mousePosition)
    {
        for (int i = beatmap.EditableNotes.Count - 1; i >= 0; i--)
        {
            if (GetNoteRect(beatmap.EditableNotes[i]).Contains(mousePosition))
            {
                return i;
            }
        }

        return -1;
    }

    private Rect GetNoteRect(BeatmapNote note)
    {
        float x = BeatToX(note.startBeat);
        float width = note.noteType == RhythmNoteType.Hold
            ? Mathf.Max(18f, note.durationBeats * pixelsPerBeat)
            : 18f;
        float y = 28f + note.slotIndex * LaneHeight + (LaneHeight - NoteHeight) * 0.5f;
        return new Rect(x, y, width, NoteHeight);
    }

    private static Color GetNoteColor(RhythmNoteType noteType)
    {
        switch (noteType)
        {
            case RhythmNoteType.Catch:
                return new Color(0.1f, 0.85f, 0.45f);
            case RhythmNoteType.Hold:
                return new Color(0.1f, 0.7f, 1f);
            default:
                return new Color(1f, 0.75f, 0.2f);
        }
    }

    private static string GetNoteLabel(BeatmapNote note)
    {
        switch (note.noteType)
        {
            case RhythmNoteType.Catch:
                return "C";
            case RhythmNoteType.Hold:
                return $"H {note.durationBeats:0.##}";
            default:
                return "T";
        }
    }

    private int MouseToLane(float mouseY)
    {
        return Mathf.FloorToInt((mouseY - 28f) / LaneHeight);
    }

    private float BeatToX(float beat)
    {
        return HeaderWidth + TimelinePadding + beat * pixelsPerBeat;
    }

    private float XToBeat(float x)
    {
        return Mathf.Max(0f, (x - HeaderWidth - TimelinePadding) / pixelsPerBeat);
    }

    private void CreateBeatmapAsset()
    {
        string path = EditorUtility.SaveFilePanelInProject("Create Song Beatmap", "SongBeatmap", "asset", "Choose where to save the beatmap.");
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        SongBeatmap asset = CreateInstance<SongBeatmap>();
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        beatmap = asset;
        serializedBeatmap = new SerializedObject(beatmap);
        Selection.activeObject = beatmap;
    }

    private void EnsureSerializedBeatmap()
    {
        if (serializedBeatmap == null || serializedBeatmap.targetObject != beatmap)
        {
            serializedBeatmap = new SerializedObject(beatmap);
        }
    }

    private void MarkDirty()
    {
        EditorUtility.SetDirty(beatmap);
        Repaint();
    }
}
