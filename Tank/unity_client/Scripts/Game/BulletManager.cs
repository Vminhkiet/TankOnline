// BulletManager.cs — Spawn/despawn bullet visuals từ snapshot
// Attach to scene singleton. Bullet chỉ là visual, physics do server tính.
using System.Collections.Generic;
using UnityEngine;
using TankNet;

public class BulletManager : MonoBehaviour
{
    public static BulletManager Instance { get; private set; }

    [Header("Prefab")]
    public GameObject BulletPrefab;

    private readonly Dictionary<uint, Transform> _bullets = new();

    void Awake()
    {
        Instance = this;
        TankNetClient.Instance.OnSnapshot += OnSnapshot;
    }

    void OnDestroy() => TankNetClient.Instance.OnSnapshot -= OnSnapshot;

    private void OnSnapshot(SnapshotData snap)
    {
        // Update / spawn bullets
        var activeIds = new HashSet<uint>();
        foreach (var bs in snap.Bullets)
        {
            activeIds.Add(bs.bulletId);
            Vector3 pos = new Vector3(bs.x, bs.y, bs.z);

            if (!_bullets.TryGetValue(bs.bulletId, out var tr))
            {
                var go = Instantiate(BulletPrefab, pos, Quaternion.identity);
                tr = go.transform;
                _bullets[bs.bulletId] = tr;
            }
            else
            {
                tr.position = pos;
            }
        }

        // Despawn bullets no longer in snapshot
        foreach (var id in new List<uint>(_bullets.Keys))
        {
            if (!activeIds.Contains(id))
            {
                Destroy(_bullets[id].gameObject);
                _bullets.Remove(id);
            }
        }
    }
}
