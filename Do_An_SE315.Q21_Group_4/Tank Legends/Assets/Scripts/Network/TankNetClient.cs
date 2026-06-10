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
        public ItemState[]   Items;
    }

    public class TankNetClient : MonoBehaviour
    {
        public static TankNetClient Instance { get; private set; }

        public event Action<SnapshotData> OnSnapshot;
        public event Action<MatchEndData> OnMatchEnd;
        public event Action<EventShootPacket> OnEventShoot;
        public event Action<EventSkillCastPacket> OnEventSkillCast;
        public event Action<ushort, string, uint> OnForceLogout; // (code, message, disconnectAfterMs)
        public event Action<PacketSpawnItem> OnItemSpawn;
        public event Action<PacketDespawnItem> OnItemDespawn;

        [Header("Network")]
        public string ServerHost = "127.0.0.1";
        public int    ServerPort = 8080;
        public uint   MatchId    = 0;
        public uint   PlayerId   = 0;

        [Header("Input")]
        public float SendRateHz = 20f;
        public int PingMs { get; private set; } = -1;

        private float _lastPingTime;

        private UdpClient  _udp;
        private IPEndPoint _server;
        private Thread     _recvThread;
        private bool       _running;
        private byte       _seq;

        // Pending input        // State variables sent every tick
        private int _pendingMoveX = 0;
        private int _pendingMoveZ = 0;
        private float _pendingTurretYaw = 0f;
        private bool _pendingShoot = false;
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

            // Sử dụng địa chỉ IP theo cấu hình của TestConnectionToggler (thông qua GameApiClient)
            try
            {
                var uri = new System.Uri(GameApiClient.BaseUrl);
                host = uri.Host;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[TankNet] Could not parse GameApiClient.BaseUrl: {ex.Message}");
            }

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

            // Send C2S_LOGIN with tank type index
            int typeIndex = 0;
            var gm = FindObjectOfType<Complete.GameManager>();
            if (gm != null && gm.m_TankPrefabMappings != null && gm.m_TankPrefab != null)
            {
                foreach (var mapping in gm.m_TankPrefabMappings)
                {
                    if (mapping.prefab == gm.m_TankPrefab)
                    {
                        typeIndex = mapping.typeIndex;
                        break;
                    }
                }
            }
            byte[] loginPkt = PacketBuilder.BuildLogin(MatchId, typeIndex, PlayerId);
            try { _udp.Send(loginPkt, loginPkt.Length, _server); } catch { }

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

        private float _pendingHullYaw;
        private bool _pendingReload;

        // Call from player input (main thread safe — just sets flags)
        public void SetMove(int moveX, int moveZ, float turretYaw = 0f, float hullYaw = 0f, bool reload = false)
        {
            _pendingMoveX = Mathf.Clamp(moveX, -1, 1);
            _pendingMoveZ = Mathf.Clamp(moveZ, -1, 1);
            _pendingTurretYaw = turretYaw;
            _pendingHullYaw = hullYaw;
            if (reload) _pendingReload = true; // latch until sent
        }

        // Send input immediately from FixedUpdate — keeps server in sync with client prediction
        public void SendMoveNow(int moveX, int moveZ, float turretYaw = 0f, float hullYaw = 0f, bool reload = false)
        {
            if (!_running) return;
            // Keep pending in sync so SendTick (heartbeat/shoot) doesn't override with (0,0)
            _pendingMoveX = Mathf.Clamp(moveX, -1, 1);
            _pendingMoveZ = Mathf.Clamp(moveZ, -1, 1);
            _pendingTurretYaw = turretYaw;
            _pendingHullYaw = hullYaw;
            if (reload) _pendingReload = true;
            
            byte[] pkt = PacketBuilder.BuildMove(MatchId, _pendingMoveX, _pendingMoveZ, _pendingTurretYaw, _pendingHullYaw, _pendingReload, PlayerId, _seq++);
            _pendingReload = false; // consume
            try { _udp.Send(pkt, pkt.Length, _server); } catch { }
        }

        public void RequestShoot(float force, float turretYaw, byte barrelCount)
        {
            if (!_running) return;
            // Send immediately so server bullet spawns in sync with local prediction shell
            byte[] pkt = PacketBuilder.BuildShoot(MatchId, (int)force, turretYaw, barrelCount, PlayerId, _seq++);
            try { _udp.Send(pkt, pkt.Length, _server); } catch { }
            // Do NOT set _pendingShoot — SendTick would double-send and fire a second bullet
        }

        public void SendCastSkill(string skillName, Vector3 target, Vector3 dir, bool isCharging = false)
        {
            if (!_running) return;
            byte[] pkt = PacketBuilder.BuildCastSkill(MatchId, skillName, target, dir, isCharging, PlayerId);
            try { _udp.Send(pkt, pkt.Length, _server); } catch { }
        }

        // ── Send tick (called by InvokeRepeating at 20 Hz) ───────────────────

        private void SendTick()
        {
            if (!_running) return;

            try 
            {
                if (Time.time - _lastPingTime > 1f)
                {
                    _lastPingTime = Time.time;
                    uint clientTime = (uint)System.Environment.TickCount;
                    byte[] pingPkt = PacketBuilder.BuildPing(MatchId, clientTime, PlayerId);
                    try { _udp.Send(pingPkt, pingPkt.Length, _server); } catch { }
                }

                byte[] pkt = PacketBuilder.BuildMove(MatchId, _pendingMoveX, _pendingMoveZ, _pendingTurretYaw, _pendingHullYaw, _pendingReload, PlayerId, _seq++);
                _pendingReload = false; // consume
                int sent = _udp.Send(pkt, pkt.Length, _server);
                
             /*   if (_seq % 20 == 0) // Log once per second
                    Debug.Log($"[TankNet] Sent {sent} bytes UDP to {_server.Address}:{_server.Port} for Match {MatchId}");*/

                if (_pendingShoot)
                {
                    // Fallback using 0 yaw if pending shoot is somehow used (it shouldn't be used typically)
                    byte[] shoot = PacketBuilder.BuildShoot(MatchId, (int)_pendingShootForce, 0f, 1, PlayerId, _seq++);
                    try { _udp.Send(shoot, shoot.Length, _server); } catch { }
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



                    if ((Opcode)opcode == Opcode.S2C_PONG)
                    {
                        if (data.Length < Marshal.SizeOf<PongHeader>()) continue;
                        var hdr = BytesToStruct<PongHeader>(data, 0);
                        if (hdr.matchId != MatchId) continue;

                        uint nowMs = (uint)System.Environment.TickCount;
                        PingMs = (int)(nowMs - hdr.clientTimeMs);
                        continue;
                    }

                    if ((Opcode)opcode == Opcode.S2C_EVENT_SPAWN_ITEM)
                    {
                        if (data.Length < Marshal.SizeOf<PacketSpawnItem>()) continue;
                        var pkt = BytesToStruct<PacketSpawnItem>(data, 0);
                        if (pkt.matchId != MatchId) continue;
                        UnityMainThread.Post(() => OnItemSpawn?.Invoke(pkt));
                        continue;
                    }

                    if ((Opcode)opcode == Opcode.S2C_EVENT_DESPAWN_ITEM)
                    {
                        if (data.Length < Marshal.SizeOf<PacketDespawnItem>()) continue;
                        var pkt = BytesToStruct<PacketDespawnItem>(data, 0);
                        if (pkt.matchId != MatchId) continue;
                        UnityMainThread.Post(() => OnItemDespawn?.Invoke(pkt));
                        continue;
                    }

                    if ((Opcode)opcode == Opcode.S2C_EVENT_SHOOT)
                    {
                        if (data.Length < Marshal.SizeOf<EventShootPacket>()) continue;
                        var pkt = BytesToStruct<EventShootPacket>(data, 0);
                        if (pkt.matchId != MatchId) continue;

                        UnityMainThread.Post(() => OnEventShoot?.Invoke(pkt));
                        continue;
                    }

                    if ((Opcode)opcode == Opcode.S2C_EVENT_SKILL_CAST)
                    {
                        if (data.Length < Marshal.SizeOf<EventSkillCastPacket>()) continue;
                        var pkt = BytesToStruct<EventSkillCastPacket>(data, 0);
                        if (pkt.matchId != MatchId) continue;

                        UnityMainThread.Post(() => OnEventSkillCast?.Invoke(pkt));
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

            if (offset + 2 > data.Length) { snap.Items = Array.Empty<ItemState>(); return snap; }
            ushort itemCount = BitConverter.ToUInt16(data, offset); offset += 2;

            int itemSize = Marshal.SizeOf<ItemState>();
            snap.Items = new ItemState[itemCount];
            for (int i = 0; i < itemCount; i++)
            {
                snap.Items[i] = BytesToStruct<ItemState>(data, offset);
                offset += itemSize;
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
