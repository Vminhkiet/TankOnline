// GameManager.cs
// Attach to a persistent GameObject in scene.
// Call GameManager.Instance.JoinMatch(...) sau khi lobby trả về matchId.
using System.Collections.Generic;
using UnityEngine;
using TankNet;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Server")]
    public string ServerHost = "127.0.0.1";
    public int    ServerPort = 8080;

    [Header("Prefabs")]
    public GameObject LocalTankPrefab;   // tank của mình (có camera, input)
    public GameObject RemoteTankPrefab;  // tank của người khác (chỉ interpolate)

    [Header("My Player")]
    public uint MyPlayerId;   // set từ lobby trước khi JoinMatch

    // Tracks spawned tank objects: tankId → controller
    private readonly Dictionary<uint, RemoteTankController> _remoteTanks = new();
    private LocalTankController _localTank;
    private uint _matchId;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ── Gọi sau khi lobby xác nhận match ─────────────────────────────────────
    public void JoinMatch(uint matchId, string host = null, int port = 0)
    {
        _matchId = matchId;

        TankNetClient.Instance.OnSnapshot += HandleSnapshot;
        TankNetClient.Instance.Connect(
            host ?? ServerHost,
            port > 0 ? port : ServerPort,
            matchId);

        Debug.Log($"[GameManager] joined match {matchId}");
    }

    void OnDestroy()
    {
        if (TankNetClient.Instance != null)
            TankNetClient.Instance.OnSnapshot -= HandleSnapshot;
    }

    // ── Nhận snapshot từ server (20 Hz) ──────────────────────────────────────
    private void HandleSnapshot(SnapshotData snap)
    {
        foreach (var ts in snap.Tanks)
        {
            if (ts.tankId == MyPlayerId)
            {
                // Local tank: chỉ correct nếu sai quá 0.5 unit
                if (_localTank != null)
                    _localTank.OnServerCorrection(ts);
            }
            else
            {
                GetOrSpawnRemote(ts.tankId).PushSnapshot(ts, snap.ServerTick);
            }
        }

        // Despawn remote tanks không còn trong snapshot (đã chết hoặc rời trận)
        var activeIds = new HashSet<uint>();
        foreach (var ts in snap.Tanks) activeIds.Add(ts.tankId);
        foreach (var id in new List<uint>(_remoteTanks.Keys))
            if (!activeIds.Contains(id)) DespawnRemote(id);
    }

    // ── Spawn local tank lần đầu khi server confirm ──────────────────────────
    public void SpawnLocalTank(Vector3 pos)
    {
        if (_localTank != null) return;
        var go = Instantiate(LocalTankPrefab, pos, Quaternion.identity);
        _localTank = go.GetComponent<LocalTankController>();
        _localTank.Init(MyPlayerId);
        Debug.Log($"[GameManager] local tank spawned at {pos}");
    }

    private RemoteTankController GetOrSpawnRemote(uint tankId)
    {
        if (_remoteTanks.TryGetValue(tankId, out var ctrl)) return ctrl;

        var go = Instantiate(RemoteTankPrefab, Vector3.zero, Quaternion.identity);
        ctrl = go.GetComponent<RemoteTankController>();
        ctrl.Init(tankId);
        _remoteTanks[tankId] = ctrl;
        return ctrl;
    }

    private void DespawnRemote(uint tankId)
    {
        if (!_remoteTanks.TryGetValue(tankId, out var ctrl)) return;
        Destroy(ctrl.gameObject);
        _remoteTanks.Remove(tankId);
    }
}
