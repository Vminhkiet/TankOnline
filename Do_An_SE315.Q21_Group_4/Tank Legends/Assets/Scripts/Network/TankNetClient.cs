// TankNetClient.cs — MonoBehaviour, attach to a persistent GameObject
// Usage:
//   TankNetClient.Instance.Connect("127.0.0.1", 8080, matchId: 42);
//   TankNetClient.Instance.SendMove(1, 0);    // forward
//   TankNetClient.Instance.SendShoot();
//   TankNetClient.Instance.OnSnapshot += HandleSnapshot;
using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

namespace TankNet
{
    public class SnapshotData
    {
        public ushort     ServerTick;
        public uint       LocalPlayerId;  // server-assigned ID for this client
        public TankState[]   Tanks;
        public BulletState[] Bullets;
    }

    public class TankNetClient : MonoBehaviour
    {
        public static TankNetClient Instance { get; private set; }

        public event Action<SnapshotData> OnSnapshot;

        [Header("Network")]
        public string ServerHost = "127.0.0.1";
        public int    ServerPort = 8080;
        public uint   MatchId    = 0;

        [Header("Input")]
        public float SendRateHz = 20f;

        private UdpClient  _udp;
        private IPEndPoint _server;
        private Thread     _recvThread;
        private bool       _running;
        private byte       _seq;

        // Pending input (set from main thread, sent on tick)
        private int _pendingMoveX;
        private int _pendingMoveZ;
        private bool _pendingShoot;

        // ── Unity lifecycle ───────────────────────────────────────────────────

        void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void OnDestroy() => Disconnect();

        // ── Public API ────────────────────────────────────────────────────────

        public void Connect(string host, int port, uint matchId)
        {
            ServerHost = host; ServerPort = port; MatchId = matchId;

            _server = new IPEndPoint(IPAddress.Parse(host), port);
            _udp    = new UdpClient();
            _udp.Client.ReceiveTimeout = 0; // non-blocking via thread

            _running = true;
            _recvThread = new Thread(RecvLoop) { IsBackground = true };
            _recvThread.Start();

            // Start send tick
            InvokeRepeating(nameof(SendTick), 0f, 1f / SendRateHz);
            Debug.Log($"[TankNet] connected → {host}:{port} matchId={matchId}");
        }

        public void Disconnect()
        {
            _running = false;
            CancelInvoke(nameof(SendTick));
            _udp?.Close();
            _recvThread?.Join(200);
        }

        // Call from player input (main thread safe — just sets flags)
        public void SetMove(int moveX, int moveZ)
        {
            _pendingMoveX = Mathf.Clamp(moveX, -1, 1);
            _pendingMoveZ = Mathf.Clamp(moveZ, -1, 1);
        }

        // Send input immediately from FixedUpdate — keeps server in sync with client prediction
        public void SendMoveNow(int moveX, int moveZ)
        {
            if (!_running) return;
            // Keep pending in sync so SendTick (heartbeat/shoot) doesn't override with (0,0)
            _pendingMoveX = Mathf.Clamp(moveX, -1, 1);
            _pendingMoveZ = Mathf.Clamp(moveZ, -1, 1);
            byte[] pkt = PacketBuilder.BuildMove(MatchId, _pendingMoveX, _pendingMoveZ, _seq++);
            try { _udp.Send(pkt, pkt.Length, _server); } catch { }
        }

        public void RequestShoot() => _pendingShoot = true;

        // ── Send tick (called by InvokeRepeating at 20 Hz) ───────────────────

        private void SendTick()
        {
            if (!_running) return;

            byte[] pkt = PacketBuilder.BuildMove(MatchId, _pendingMoveX, _pendingMoveZ, _seq++);
            _udp.Send(pkt, pkt.Length, _server);

            if (_pendingShoot)
            {
                byte[] shoot = PacketBuilder.BuildShoot(MatchId, _seq++);
                _udp.Send(shoot, shoot.Length, _server);
                _pendingShoot = false;
            }
        }

        // ── Receive loop (background thread) ─────────────────────────────────

        private void RecvLoop()
        {
            var from = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                try
                {
                    byte[] data = _udp.Receive(ref from);
                    if (data.Length < Marshal.SizeOf<SnapshotHeader>()) continue;

                    // S2C_SNAPSHOT uses raw binary header (not bit-packed)
                    var hdr = BytesToStruct<SnapshotHeader>(data, 0);
                    if ((Opcode)hdr.opcode != Opcode.S2C_SNAPSHOT) continue;
                    if (hdr.matchId != MatchId) continue;

                    var snap = ParseSnapshot(data, hdr);
                    // Marshal to main thread
                    UnityMainThread.Post(() => OnSnapshot?.Invoke(snap));
                }
                catch (SocketException) { /* timeout or closed */ }
                catch (Exception e) { Debug.LogWarning($"[TankNet] recv: {e.Message}"); }
            }
        }

        // ── Snapshot parsing ──────────────────────────────────────────────────

        private static SnapshotData ParseSnapshot(byte[] data, SnapshotHeader hdr)
        {
            int hdrSize    = Marshal.SizeOf<SnapshotHeader>();
            int tankSize   = Marshal.SizeOf<TankState>();
            int bulletSize = Marshal.SizeOf<BulletState>();

            var snap = new SnapshotData { ServerTick = hdr.serverTick, LocalPlayerId = hdr.localPlayerId };

            // body bắt đầu bằng uint16 tankCount (trùng với hdr.tankCount), skip nó
            int offset = hdrSize + 2;
            snap.Tanks = new TankState[hdr.tankCount];
            for (int i = 0; i < hdr.tankCount; i++)
            {
                snap.Tanks[i] = BytesToStruct<TankState>(data, offset);
                offset += tankSize;
            }

            if (offset + 2 > data.Length) { snap.Bullets = Array.Empty<BulletState>(); return snap; }
            ushort bulletCount = BitConverter.ToUInt16(data, offset); offset += 2;

            snap.Bullets = new BulletState[bulletCount];
            for (int i = 0; i < bulletCount; i++)
            {
                snap.Bullets[i] = BytesToStruct<BulletState>(data, offset);
                offset += bulletSize;
            }
            return snap;
        }

        private static T BytesToStruct<T>(byte[] data, int offset) where T : struct
        {
            int size = Marshal.SizeOf<T>();
            var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            try {
                return Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject() + offset);
            } finally { handle.Free(); }
        }
    }

    // Minimal Unity main-thread dispatcher
    public static class UnityMainThread
    {
        private static SynchronizationContext _ctx;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init() => _ctx = SynchronizationContext.Current;
        public static void Post(Action a) => _ctx?.Post(_ => a(), null);
    }
}
