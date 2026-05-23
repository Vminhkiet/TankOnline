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
using System.Text;
using System.Threading;
using UnityEngine;

namespace TankNet
{
    public class SnapshotData
    {
        public ushort     ServerTick;
        public uint       LocalPlayerId;  // server-assigned ID for this client
        public float TimeRemaining;
        public TankState[]   Tanks;
        public BulletState[] Bullets;
    }

    public class TankNetClient : MonoBehaviour
    {
        public static TankNetClient Instance { get; private set; }

        public event Action<SnapshotData> OnSnapshot;
        public event Action<MatchEndData> OnMatchEnd;
        public event Action<ushort, string, uint> OnForceLogout; // (code, message, disconnectAfterMs)

        [Header("Network")]
        public string ServerHost = "127.0.0.1";
        public int    ServerPort = 8080;
        public uint   MatchId    = 0;
        public uint   PlayerId   = 0;

        [Header("Input")]
        public float SendRateHz = 20f;

        private UdpClient  _udp;
        private IPEndPoint _server;
        private Thread     _recvThread;
        private bool       _running;
        private byte       _seq;

        // Pending input (set from main thread, sent on tick)
        private int   _pendingMoveX;
        private int   _pendingMoveZ;
        private bool  _pendingShoot;
        private float _pendingShootForce = 20f;

        // ── Unity lifecycle ───────────────────────────────────────────────────

        void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void OnDestroy() => Disconnect();

        // ── Public API ────────────────────────────────────────────────────────

        public bool IsConnected => _running;

        public void Connect(string host, int port, uint matchId, uint playerId = 0)
        {
            if (_running)
            {
                if (MatchId == matchId && ServerHost == host && ServerPort == port && PlayerId == playerId)
                    return;
                Disconnect();
            }

#if UNITY_EDITOR || UNITY_STANDALONE_WIN
            if (host.StartsWith("10.") || host.StartsWith("192.168.") || host.StartsWith("172."))
            {
                host = "127.0.0.1";
            }
#endif
            if (host == "auto" || host.ToLower() == "localhost")
            {
                host = "127.0.0.1";
            }

            ServerHost = host; ServerPort = port; MatchId = matchId; PlayerId = playerId;

            IPAddress ipAddr;
            if (!IPAddress.TryParse(host, out ipAddr))
            {
                try
                {
                    var addresses = System.Net.Dns.GetHostAddresses(host);
                    if (addresses.Length > 0)
                    {
                        ipAddr = addresses[0];
                    }
                    else
                    {
                        Debug.LogError($"[TankNet] Could not resolve host: {host}");
                        return;
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[TankNet] DNS resolve error for {host}: {ex.Message}");
                    return;
                }
            }

            _server = new IPEndPoint(ipAddr, port);
            _udp    = new UdpClient();
            _udp.Client.ReceiveTimeout = 0; // non-blocking via thread

            _running = true;
            _recvThread = new Thread(RecvLoop) { IsBackground = true };
            _recvThread.Start();

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
            byte[] pkt = PacketBuilder.BuildMove(MatchId, _pendingMoveX, _pendingMoveZ, PlayerId, _seq++);
            try { _udp.Send(pkt, pkt.Length, _server); } catch { }
        }

        public void RequestShoot(float force = 20f)
        {
            if (!_running) return;
            // Send immediately so server bullet spawns in sync with local prediction shell
            byte[] pkt = PacketBuilder.BuildShoot(MatchId, (int)force, PlayerId, _seq++);
            try { _udp.Send(pkt, pkt.Length, _server); } catch { }
            // Do NOT set _pendingShoot — SendTick would double-send and fire a second bullet
        }

        // ── Send tick (called by InvokeRepeating at 20 Hz) ───────────────────

        private void SendTick()
        {
            if (!_running) return;

            try 
            {
                byte[] pkt = PacketBuilder.BuildMove(MatchId, _pendingMoveX, _pendingMoveZ, PlayerId, _seq++);
                int sent = _udp.Send(pkt, pkt.Length, _server);
                
             /*   if (_seq % 20 == 0) // Log once per second
                    Debug.Log($"[TankNet] Sent {sent} bytes UDP to {_server.Address}:{_server.Port} for Match {MatchId}");*/

                if (_pendingShoot)
                {
                    byte[] shoot = PacketBuilder.BuildShoot(MatchId, (int)_pendingShootForce, PlayerId, _seq++);
                    _udp.Send(shoot, shoot.Length, _server);
                    _pendingShoot = false;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[TankNet] SendTick Exception: {e.Message}");
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
                    if (data.Length < 6) continue; // at least matchId + opcode

                    ushort opcode = BitConverter.ToUInt16(data, 4);

                    if ((Opcode)opcode == Opcode.S2C_SNAPSHOT)
                    {
                        if (data.Length < Marshal.SizeOf<SnapshotHeader>()) continue;

                        var hdr = BytesToStruct<SnapshotHeader>(data, 0);
                        if (hdr.matchId != MatchId) continue;

                        var snap = ParseSnapshot(data, hdr);
                        UnityMainThread.Post(() => OnSnapshot?.Invoke(snap));
                        continue;
                    }

                    if ((Opcode)opcode == Opcode.S2C_MATCH_END)
                    {
                        if (data.Length < Marshal.SizeOf<MatchEndHeader>()) continue;
                        var hdr = BytesToStruct<MatchEndHeader>(data, 0);
                        if (hdr.matchId != MatchId) continue;

                        var end = new MatchEndData
                        {
                            MatchId      = hdr.matchId,
                            Outcome      = hdr.outcome,
                            WinnerId     = hdr.winnerId,
                            DurationSecs = hdr.durationSecs,
                            MyKills      = hdr.myKills,
                            RpReward     = hdr.rpReward,
                            Placement    = hdr.placement,
                            Players      = new MatchEndPlayer[hdr.playerCount]
                        };

                        int offset = Marshal.SizeOf<MatchEndHeader>();
                        int playerSize = Marshal.SizeOf<MatchEndPlayer>();
                        
                        Debug.Log($"[TankNet] S2C_MATCH_END: pktLength={data.Length}, hdrSize={offset}, playerSize={playerSize}, playerCount={hdr.playerCount}");

                        for (int i = 0; i < hdr.playerCount; i++)
                        {
                            if (offset + playerSize > data.Length) 
                            {
                                Debug.LogWarning($"[TankNet] Not enough data for player {i}. offset={offset}, len={data.Length}");
                                break;
                            }
                            end.Players[i] = BytesToStruct<MatchEndPlayer>(data, offset);
                            offset += playerSize;
                        }

                        UnityMainThread.Post(() => OnMatchEnd?.Invoke(end));
                        continue;
                    }

                    if ((Opcode)opcode == Opcode.S2C_FORCE_LOGOUT)
                    {
                        if (data.Length < Marshal.SizeOf<ForceLogoutHeader>()) continue;
                        var hdr = BytesToStruct<ForceLogoutHeader>(data, 0);
                        if (hdr.matchId != MatchId) continue;

                        int headerSize = Marshal.SizeOf<ForceLogoutHeader>();
                        int msgLen = Math.Min(hdr.messageLen, (ushort)Math.Max(0, data.Length - headerSize));
                        string reason = msgLen > 0
                            ? Encoding.UTF8.GetString(data, headerSize, msgLen)
                            : "Logged in from another device";

                        UnityMainThread.Post(() => OnForceLogout?.Invoke(hdr.code, reason, hdr.disconnectAfterMs));
                        continue;
                    }
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

            var snap = new SnapshotData { ServerTick = hdr.serverTick, LocalPlayerId = hdr.localPlayerId, TimeRemaining = hdr.timeRemainingTenths / 10f};

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
