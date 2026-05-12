// RemoteTankController.cs
// Attach to RemoteTankPrefab.
// Buffer snapshots → render tại (serverTime - 100ms) → interpolate mượt.
using System.Collections.Generic;
using UnityEngine;
using TankNet;

public class RemoteTankController : MonoBehaviour
{
    // ── Snapshot buffer ───────────────────────────────────────────────────────
    private struct Frame
    {
        public float   Time;    // local Time.time khi nhận
        public Vector3 Pos;
        public float   Yaw;
        public bool    IsAlive;
    }

    private readonly Queue<Frame> _buf = new();
    private const int   MAX_BUF     = 16;
    private const float INTERP_DELAY = 0.1f;  // 100ms interpolation buffer

    private uint  _tankId;
    private float _healthFill = 1f;

    [Header("Health Bar (optional)")]
    public UnityEngine.UI.Image HealthBar;

    public void Init(uint tankId) => _tankId = tankId;

    // ── Gọi từ GameManager khi snapshot về (20Hz, background thread → main) ──
    public void PushSnapshot(TankState ts, ushort serverTick)
    {
        var frame = new Frame
        {
            Time    = Time.time,
            Pos     = new Vector3(ts.x, ts.y, ts.z),
            Yaw     = ts.yaw * Mathf.Rad2Deg,
            IsAlive = ts.IsAlive,
        };

        _buf.Enqueue(frame);
        if (_buf.Count > MAX_BUF) _buf.Dequeue();  // drop stale

        // Update health bar
        _healthFill = Mathf.Clamp01(ts.health / 100f);
        if (HealthBar) HealthBar.fillAmount = _healthFill;
    }

    void Update()
    {
        if (_buf.Count < 2) return;

        // Render tại (now - delay): luôn có ít nhất 2 frames để lerp
        float renderTime = Time.time - INTERP_DELAY;

        // Tìm 2 frame kẹp renderTime
        Frame from = default, to = default;
        bool found = false;

        var frames = _buf.ToArray();
        for (int i = 0; i < frames.Length - 1; i++)
        {
            if (frames[i].Time <= renderTime && renderTime <= frames[i + 1].Time)
            {
                from = frames[i];
                to   = frames[i + 1];
                found = true;
                break;
            }
        }

        if (!found)
        {
            // renderTime vượt quá buffer → dùng frame mới nhất (extrapolate-safe)
            to = frames[^1];
            transform.position = to.Pos;
            transform.rotation = Quaternion.Euler(0, to.Yaw, 0);
            return;
        }

        float span = to.Time - from.Time;
        float t    = span > 0.0001f ? (renderTime - from.Time) / span : 1f;

        transform.position = Vector3.Lerp(from.Pos, to.Pos, t);
        transform.rotation = Quaternion.Slerp(
            Quaternion.Euler(0, from.Yaw, 0),
            Quaternion.Euler(0, to.Yaw,   0), t);

        // Ẩn nếu chết
        gameObject.SetActive(to.IsAlive);
    }
}
