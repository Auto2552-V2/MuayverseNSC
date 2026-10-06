using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using TMPro;

// ---------------------------------------------------------------------------------------------
// Wire schema from realtime_feedback.py --session --unity (see that file's send_session()).
// ---------------------------------------------------------------------------------------------
[Serializable] public class SessionHeader { public string type; }

[Serializable] public class SessionPayload {
    public string kind;      // session_start | stage_start | progress | stage_done | session_complete
    public int idx;          // current stage number (1-based)
    public int n;            // total stages
    public int exercise;     // exercise id
    public int reps;
    public int target;
    public string name;      // Thai exercise name
    public string name_en;
    public string side;
    public string state;     // coach phase (ready/raising/...)
    public string hud;
    public string reason;    // done | skip | quit
    public bool completed;
}

[Serializable] public class SessionMessage {
    public string type; public int seq; public long t_ms; public SessionPayload payload;
}

[Serializable] public class CoachPayload { public string text; }

[Serializable] public class CoachMessage {
    public string type; public int seq; public long t_ms; public CoachPayload payload;
}

/// <summary>
/// Receives the guided therapy session (realtime_feedback.py --session --unity) over UDP and drives
/// the Session-scene UI: current exercise, rep counter, coach subtitles, and progress. Python owns
/// the camera + pose + LLM coach + Thai TTS audio; Unity shows the visuals and the spoken text.
///
///   python realtime_feedback.py --session --webcam --unity --stream-video --mirror --llm-feedback --llm-only
///
/// Pair with PoseVideoReceiver (port 5006) for the camera background. This binds the session port
/// (default 5005). One-way; Unity never sends back.
/// </summary>
public class SessionReceiver : MonoBehaviour
{
    [Header("Network")]
    [Tooltip("IP of the machine running the Python session (127.0.0.1 if same machine).")]
    [SerializeField] private string serverHost = "127.0.0.1";
    [SerializeField] private int port = 5005;

    [Header("UI (assign TMP texts; any may be left empty)")]
    [Tooltip("Current exercise, e.g. '[2/5] แขนซ้าย'.")]
    [SerializeField] private TMP_Text exerciseText;
    [Tooltip("Rep counter, e.g. '3 / 5'.")]
    [SerializeField] private TMP_Text repText;
    [Tooltip("Coach subtitle (every spoken line, incl. the LLM coach).")]
    [SerializeField] private TMP_Text subtitleText;
    [Tooltip("Session progress, e.g. '● ● ○ ○ ○'.")]
    [SerializeField] private TMP_Text progressText;

    [Header("Behaviour")]
    [Tooltip("Seconds a subtitle stays before clearing (0 = never clear).")]
    [SerializeField] private float subtitleHoldSeconds = 5f;

    // Live state other scripts can read.
    public int StageIndex { get; private set; }
    public int StageCount { get; private set; }
    public string ExerciseNameTh { get; private set; } = "";
    public int Reps { get; private set; }
    public int Target { get; private set; }
    public bool Completed { get; private set; }

    /// <summary>(kind, payload) for each session packet, on the main thread.</summary>
    public event Action<string, SessionPayload> OnSession;
    /// <summary>Each coach subtitle line, on the main thread.</summary>
    public event Action<string> OnCoachLine;

    private UdpClient udp;
    private Thread rxThread;
    private volatile bool running;
    private readonly ConcurrentQueue<string> inbox = new ConcurrentQueue<string>();
    private float subtitleClearAt;

    private IPEndPoint serverEndpoint;
    private float nextHello;
    private static readonly byte[] HelloBytes = Encoding.UTF8.GetBytes("{\"type\":\"hello\"}");

    private void Start()
    {
        // The Python session runs in serve mode (it binds the port). We connect TO it: an ephemeral
        // local socket that sends hello keepalives (so the session starts IN SYNC with Unity and
        // knows where to reply) and receives session/coach on the same socket.
        try
        {
            udp = new UdpClient();
            serverEndpoint = new IPEndPoint(IPAddress.Parse(serverHost), port);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Session] cannot open UDP socket: {e.Message}");
            return;
        }
        running = true;
        rxThread = new Thread(ReceiveLoop) { IsBackground = true };
        rxThread.Start();
        Debug.Log($"[Session] hello -> {serverHost}:{port} (waiting for the session to start)");
        if (progressText != null) progressText.text = "";
        if (exerciseText != null) exerciseText.text = "กำลังเชื่อมต่อ...";
    }

    // Keep the session registered; also this is the "Unity is ready" signal that starts the session.
    private void SendHelloIfDue()
    {
        if (udp == null || Time.unscaledTime < nextHello) return;
        nextHello = Time.unscaledTime + 1f;   // session drops us after ~10s of silence
        try { udp.Send(HelloBytes, HelloBytes.Length, serverEndpoint); }
        catch (SocketException e) { Debug.LogWarning($"[Session] hello failed: {e.Message}"); }
    }

    private void ReceiveLoop()
    {
        IPEndPoint any = new IPEndPoint(IPAddress.Any, 0);
        while (running)
        {
            try { inbox.Enqueue(Encoding.UTF8.GetString(udp.Receive(ref any))); }
            catch { /* closed on shutdown */ }
        }
    }

    private void Update()
    {
        SendHelloIfDue();

        while (inbox.TryDequeue(out string json))
        {
            SessionHeader head;
            try { head = JsonUtility.FromJson<SessionHeader>(json); }
            catch { continue; }
            if (head == null) continue;

            if (head.type == "session")
            {
                SessionMessage msg = JsonUtility.FromJson<SessionMessage>(json);
                if (msg?.payload != null) HandleSession(msg.payload);
            }
            else if (head.type == "coach")
            {
                CoachMessage msg = JsonUtility.FromJson<CoachMessage>(json);
                if (msg?.payload != null && !string.IsNullOrEmpty(msg.payload.text))
                    ShowSubtitle(msg.payload.text);
            }
        }

        if (subtitleHoldSeconds > 0f && subtitleText != null
            && subtitleClearAt > 0f && Time.time >= subtitleClearAt)
        {
            subtitleText.text = "";
            subtitleClearAt = 0f;
        }
    }

    private void HandleSession(SessionPayload p)
    {
        switch (p.kind)
        {
            case "session_start":
                StageCount = p.n; Target = p.target; Completed = false;
                UpdateProgress();
                break;
            case "stage_start":
                StageIndex = p.idx; StageCount = p.n; ExerciseNameTh = p.name;
                Reps = 0; Target = p.target;
                if (exerciseText != null) exerciseText.text = $"[{p.idx}/{p.n}] {p.name}";
                SetRepText();
                UpdateProgress();
                break;
            case "progress":
                StageIndex = p.idx; StageCount = p.n; Reps = p.reps; Target = p.target;
                SetRepText();
                break;
            case "stage_done":
                Reps = p.reps;
                SetRepText();
                UpdateProgress();
                break;
            case "session_complete":
                Completed = p.completed;
                if (exerciseText != null) exerciseText.text = p.completed ? "จบเซสชันแล้ว 🎉" : "จบเซสชัน";
                break;
        }
        OnSession?.Invoke(p.kind, p);
    }

    private void SetRepText()
    {
        if (repText != null) repText.text = $"{Reps} / {Target}";
    }

    private void UpdateProgress()
    {
        if (progressText == null || StageCount <= 0) return;
        var sb = new StringBuilder();
        for (int i = 1; i <= StageCount; i++)
            sb.Append(i < StageIndex ? "● " : (i == StageIndex ? "◉ " : "○ "));
        progressText.text = sb.ToString().TrimEnd();
    }

    private void ShowSubtitle(string text)
    {
        if (subtitleText != null)
        {
            subtitleText.text = text;
            subtitleClearAt = subtitleHoldSeconds > 0f ? Time.time + subtitleHoldSeconds : 0f;
        }
        OnCoachLine?.Invoke(text);
    }

    private void OnDestroy()
    {
        running = false;
        udp?.Close();
        udp = null;
    }
}
