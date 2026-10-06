using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Receives the annotated camera frame (JPEG over UDP) from game_bridge.py --stream-video and shows
/// it as a background texture. Unity cannot run MediaPipe/OpenCV itself, so Python owns the camera +
/// pose and sends a finished picture; this component just blits it.
///
/// Setup (see the header comment in the scene notes):
///   1. Canvas (Screen Space - Camera or Overlay), a full-rect RawImage as the BACK layer.
///   2. Put this component on it (or anywhere) and assign that RawImage to `targetImage`.
///   3. game_bridge.py --webcam --host 127.0.0.1 --stream-video --mirror
///
/// Runs the socket on a background thread; only the newest frame is kept (older ones dropped) so
/// the display never accumulates lag.
/// </summary>
public class PoseVideoReceiver : MonoBehaviour
{
    [Header("Network")]
    [Tooltip("Must match game_bridge.py --video-port (default 5006). Different from the move port 5005.")]
    [SerializeField] private int videoPort = 5006;

    [Header("Display")]
    [Tooltip("Full-screen RawImage that shows the camera. Leave empty to use a RawImage on this object.")]
    [SerializeField] private RawImage targetImage;
    [Tooltip("Also copy the texture here (e.g. a material) if you want the feed elsewhere.")]
    [SerializeField] private Renderer targetRenderer;

    private UdpClient udp;
    private Thread rxThread;
    private volatile bool running;

    // Hold only the latest frame; the main thread swaps it in each Update.
    private byte[] pendingFrame;
    private readonly object frameLock = new object();

    private Texture2D texture;

    public bool HasSignal { get; private set; }

    private void Start()
    {
        if (targetImage == null)
        {
            targetImage = GetComponent<RawImage>();
        }

        texture = new Texture2D(2, 2, TextureFormat.RGB24, false);

        try
        {
            udp = new UdpClient(videoPort);
        }
        catch (SocketException e)
        {
            Debug.LogError($"[PoseVideo] cannot open UDP {videoPort}: {e.Message}");
            return;
        }

        running = true;
        rxThread = new Thread(ReceiveLoop) { IsBackground = true };
        rxThread.Start();
        Debug.Log($"[PoseVideo] listening on :{videoPort}");
    }

    private void ReceiveLoop()
    {
        IPEndPoint any = new IPEndPoint(IPAddress.Any, 0);
        while (running)
        {
            try
            {
                byte[] data = udp.Receive(ref any);      // one datagram = one whole JPEG
                lock (frameLock)
                {
                    pendingFrame = data;                 // keep only the newest
                }
            }
            catch
            {
                // socket closed on shutdown, or a transient error; loop exits via `running`
            }
        }
    }

    private void Update()
    {
        byte[] frame = null;
        lock (frameLock)
        {
            if (pendingFrame != null)
            {
                frame = pendingFrame;
                pendingFrame = null;
            }
        }

        if (frame == null) return;

        // ImageConversion.LoadImage resizes the texture to the JPEG's dimensions automatically.
        if (texture.LoadImage(frame))
        {
            HasSignal = true;
            if (targetImage != null && targetImage.texture != texture)
            {
                targetImage.texture = texture;
            }
            if (targetRenderer != null)
            {
                targetRenderer.material.mainTexture = texture;
            }
        }
    }

    private void OnDestroy()
    {
        running = false;
        udp?.Close();
        udp = null;
    }
}
