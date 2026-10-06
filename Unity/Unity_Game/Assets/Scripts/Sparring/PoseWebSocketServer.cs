using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Sparring
{
    /// <summary>
    /// เปิด WebSocket server ใน Unity ให้หน้าเว็บ detection ต่อเข้ามา
    ///
    /// ─── มีไว้ทำไม ในเมื่อ Android ใช้ Unity.call ───────────────────────────────
    /// ไว้ทดสอบบนเดสก์ท็อปโดยไม่ต้องมีมือถือ:
    ///
    ///     Chrome บนคอม (กล้องจริง + โมเดลจริง)  --ws://127.0.0.1:8787-->  Unity Editor
    ///
    /// ทางเลือกอื่นใช้ไม่ได้: WebView ของ gree บน Windows ไม่ได้ implement
    /// SetCameraAccess() กล้องจึงเปิดไม่ได้ (ดู TODO ใน WebViewObject.cs)
    ///
    /// ของที่ต่างจากตอนเล่นจริงมีแค่ "ท่อ" เท่านั้น — ข้อความที่ไหลเข้า PoseReceiver
    /// เป็น JSON ชุดเดียวกันเป๊ะ ทุกอย่างหลังจากนั้น (กรอง, ตัดสิน, UI) เหมือนกันหมด
    ///
    /// ─── ทำไมเขียน RFC 6455 เอง ─────────────────────────────────────────────────
    /// .NET ไม่มี WebSocket server ที่ใช้ได้บน Unity โดยไม่ลง package เพิ่ม
    /// (HttpListener + WebSocket ใช้ไม่ได้บน Mono/IL2CPP หลายแพลตฟอร์ม)
    /// ส่วนที่ต้องทำจริง ๆ มีแค่ handshake กับการแกะ frame ซึ่งไม่ยาว
    ///
    /// ⚠ เป็นเครื่องมือพัฒนา ผูกกับ 127.0.0.1 เท่านั้น ไม่ควรติดไปกับ build จริง
    /// </summary>
    [RequireComponent(typeof(PoseReceiver))]
    public class PoseWebSocketServer : MonoBehaviour
    {
        [Header("เซิร์ฟเวอร์")]
        [Tooltip("พอร์ตที่รอรับ — ต้องตรงกับ wsUrl ฝั่งเว็บ (ค่าเริ่มต้น ws://127.0.0.1:8787)")]
        [SerializeField] private int port = 8787;

        [Tooltip("ปล่อยไม่ติ๊ก = ทำงานเฉพาะใน Editor\n" +
                 "ติ๊ก = ติดไปกับ build ด้วย (ปกติไม่ต้อง Android ใช้ Unity.call แทน)")]
        [SerializeField] private bool enabledInBuild = false;

        [SerializeField] private bool logEvents = true;

        public bool IsListening { get; private set; }
        public bool HasClient { get; private set; }
        public int MessagesReceived { get; private set; }

        private PoseReceiver receiver;
        private TcpListener listener;
        private Thread acceptThread;
        private volatile bool running;

        // Debug.Log แตะไม่ได้นอก main thread — เก็บข้อความไว้แล้วให้ Update() พิมพ์
        private readonly ConcurrentQueue<string> logs = new ConcurrentQueue<string>();

        private const string Guid6455 = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        private void Awake() => receiver = GetComponent<PoseReceiver>();

        private void OnEnable()
        {
            if (!Application.isEditor && !enabledInBuild) { enabled = false; return; }

            try
            {
                // ผูกกับ loopback เท่านั้น — เครื่องอื่นในวงแลนต่อเข้ามาไม่ได้
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
            }
            catch (SocketException e)
            {
                Debug.LogError($"[WS] เปิดพอร์ต {port} ไม่ได้: {e.Message} " +
                               "(มี mock_unity_ws.mjs หรือ Unity อีกตัวถืออยู่หรือเปล่า)");
                enabled = false;
                return;
            }

            running = true;
            IsListening = true;
            acceptThread = new Thread(AcceptLoop) { IsBackground = true };
            acceptThread.Start();
            Debug.Log($"[WS] รอรับที่ ws://127.0.0.1:{port} — เปิดหน้าเว็บใน Chrome ได้เลย");
        }

        private void OnDisable()
        {
            running = false;
            IsListening = false;
            HasClient = false;
            try { listener?.Stop(); } catch { }
            listener = null;
        }

        private void Update()
        {
            while (logs.TryDequeue(out string line)) Debug.Log(line);
        }

        // ── เธรดเบื้องหลัง ────────────────────────────────────────────────────────

        private void AcceptLoop()
        {
            while (running)
            {
                try
                {
                    TcpClient client = listener.AcceptTcpClient();
                    // รับทีละรายเพียงพอ — มีหน้าเว็บเดียวต่อเข้ามา
                    ServeClient(client);
                }
                catch
                {
                    // listener.Stop() ตอนปิด ทำให้ Accept โยน exception เป็นเรื่องปกติ
                    if (!running) return;
                }
            }
        }

        private void ServeClient(TcpClient client)
        {
            try
            {
                using (client)
                using (NetworkStream stream = client.GetStream())
                {
                    // จับมือไม่ผ่าน = มีคนยิง HTTP ธรรมดาเข้ามา ไม่ใช่การเชื่อมต่อที่หลุด
                    // จึงออกก่อนตั้ง HasClient เพื่อไม่ให้ log ขึ้นว่า "ตัดการเชื่อมต่อ" เปล่า ๆ
                    if (!Handshake(stream)) return;

                    HasClient = true;
                    logs.Enqueue("[WS] หน้าเว็บต่อเข้ามาแล้ว");

                    while (running && client.Connected)
                    {
                        if (!ReadFrame(stream, out string text, out bool close)) break;
                        if (close) break;
                        if (text == null) continue;      // ping/pong หรือ frame ที่ไม่สนใจ

                        MessagesReceived++;
                        // PoseReceiver ใช้ ConcurrentQueue ภายใน เรียกข้ามเธรดได้ปลอดภัย
                        receiver.OnPoseJson(text);
                    }
                }
            }
            catch (Exception e)
            {
                if (running) logs.Enqueue($"[WS] การเชื่อมต่อหลุด: {e.Message}");
            }
            finally
            {
                if (HasClient)
                {
                    HasClient = false;
                    logs.Enqueue("[WS] หน้าเว็บตัดการเชื่อมต่อ");
                }
            }
        }

        /// <summary>จับมือตาม RFC 6455 — ตอบ Sec-WebSocket-Accept ให้ถูกเท่านั้นก็พอ</summary>
        private bool Handshake(NetworkStream stream)
        {
            byte[] buf = new byte[4096];
            int n = stream.Read(buf, 0, buf.Length);
            if (n <= 0) return false;

            string req = Encoding.UTF8.GetString(buf, 0, n);
            string key = null;
            foreach (string line in req.Split('\n'))
            {
                if (line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
                {
                    key = line.Substring(line.IndexOf(':') + 1).Trim();
                    break;
                }
            }
            if (string.IsNullOrEmpty(key)) return false;

            string accept;
            using (var sha1 = SHA1.Create())
            {
                accept = Convert.ToBase64String(
                    sha1.ComputeHash(Encoding.UTF8.GetBytes(key + Guid6455)));
            }

            byte[] resp = Encoding.UTF8.GetBytes(
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Upgrade: websocket\r\nConnection: Upgrade\r\n" +
                $"Sec-WebSocket-Accept: {accept}\r\n\r\n");
            stream.Write(resp, 0, resp.Length);
            return true;
        }

        /// <summary>
        /// อ่าน frame หนึ่งอัน คืน false เมื่อสายขาด
        ///
        /// รองรับเฉพาะเท่าที่ต้องใช้: text frame ที่ไม่แตกเป็นชิ้น กับ close/ping
        /// ข้อความของเราสั้นมาก (~60 ไบต์) จึงไม่มีทางถูกแตกเป็นหลาย frame
        /// </summary>
        private bool ReadFrame(NetworkStream stream, out string text, out bool close)
        {
            text = null;
            close = false;

            byte[] head = new byte[2];
            if (!ReadExact(stream, head, 2)) return false;

            int opcode = head[0] & 0x0F;
            bool masked = (head[1] & 0x80) != 0;
            long len = head[1] & 0x7F;

            if (len == 126)
            {
                byte[] ext = new byte[2];
                if (!ReadExact(stream, ext, 2)) return false;
                len = (ext[0] << 8) | ext[1];
            }
            else if (len == 127)
            {
                byte[] ext = new byte[8];
                if (!ReadExact(stream, ext, 8)) return false;
                len = 0;
                for (int i = 0; i < 8; i++) len = (len << 8) | ext[i];
            }

            // กันหน่วยความจำระเบิดจาก frame ที่อ้างความยาวเกินจริง
            if (len < 0 || len > 1 << 20)
            {
                logs.Enqueue($"[WS] frame ยาวผิดปกติ ({len} ไบต์) — ตัดการเชื่อมต่อ");
                return false;
            }

            byte[] mask = new byte[4];
            if (masked && !ReadExact(stream, mask, 4)) return false;

            byte[] payload = new byte[len];
            if (len > 0 && !ReadExact(stream, payload, (int)len)) return false;
            if (masked)
            {
                for (int i = 0; i < payload.Length; i++) payload[i] ^= mask[i % 4];
            }

            switch (opcode)
            {
                case 0x1:                       // text
                    text = Encoding.UTF8.GetString(payload);
                    return true;
                case 0x8:                       // close
                    close = true;
                    return true;
                case 0x9:                       // ping -> ต้องตอบ pong ไม่งั้นเบราว์เซอร์ตัดสาย
                    SendFrame(stream, 0xA, payload);
                    return true;
                default:
                    return true;                // pong หรืออย่างอื่น — ข้ามไป
            }
        }

        /// <summary>
        /// อ่านให้ครบจำนวนไบต์ที่ขอ
        ///
        /// TCP ไม่รับประกันว่า Read ครั้งเดียวจะได้ครบ — ถ้าไม่วนอ่าน frame จะเพี้ยน
        /// เป็นครั้งคราวแบบสุ่ม ซึ่งเป็นบั๊กที่หาสาเหตุยากที่สุดประเภทหนึ่ง
        /// </summary>
        private static bool ReadExact(NetworkStream stream, byte[] buffer, int count)
        {
            int got = 0;
            while (got < count)
            {
                int n = stream.Read(buffer, got, count - got);
                if (n <= 0) return false;
                got += n;
            }
            return true;
        }

        private static void SendFrame(NetworkStream stream, int opcode, byte[] payload)
        {
            // เซิร์ฟเวอร์ห้าม mask และ payload ของเราสั้นเสมอ จึงใช้ header 2 ไบต์พอ
            if (payload.Length > 125) return;
            byte[] frame = new byte[2 + payload.Length];
            frame[0] = (byte)(0x80 | opcode);
            frame[1] = (byte)payload.Length;
            Array.Copy(payload, 0, frame, 2, payload.Length);
            stream.Write(frame, 0, frame.Length);
        }
    }
}
