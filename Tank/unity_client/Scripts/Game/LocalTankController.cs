// LocalTankController.cs
// Attach to LocalTankPrefab.
// Đọc input → gửi server, đồng thời predict vị trí locally để không bị lag.
using UnityEngine;
using TankNet;

public class LocalTankController : MonoBehaviour
{
    [Header("Movement (phải khớp server Tank.MAX_SPEED)")]
    public float MoveSpeed = 12f;
    public float TurnSpeed = 2f;         // rad/s

    private uint   _playerId;
    private float  _yaw;                 // predicted yaw (world radians)
    private Vector3 _predictedPos;       // client-side predicted position

    // Correction threshold: nếu server pos sai > 0.5 unit thì snap
    private const float CORRECTION_THRESHOLD = 0.5f;

    public void Init(uint playerId)
    {
        _playerId     = playerId;
        _predictedPos = transform.position;
        _yaw          = transform.eulerAngles.y * Mathf.Deg2Rad;
    }

    void Update()
    {
        float dt = Time.deltaTime;

        // ── 1. Đọc input ──────────────────────────────────────────────────────
        float horizontal = Input.GetAxisRaw("Horizontal"); // -1, 0, +1
        float vertical   = Input.GetAxisRaw("Vertical");
        bool  shoot      = Input.GetKeyDown(KeyCode.Space);

        int moveX = horizontal > 0.1f ? 1 : (horizontal < -0.1f ? -1 : 0);
        int moveZ = vertical   > 0.1f ? 1 : (vertical   < -0.1f ? -1 : 0);

        // ── 2. Gửi input lên server (TankNetClient handles 20Hz throttle) ─────
        TankNetClient.Instance.SetMove(moveX, moveZ);
        if (shoot) TankNetClient.Instance.RequestShoot();

        // ── 3. Client-side prediction (60Hz, mượt mà ngay cả khi server 20Hz) ─
        if (moveX != 0) _yaw += moveX * TurnSpeed * dt;

        Vector3 dir = new Vector3(Mathf.Sin(_yaw), 0f, Mathf.Cos(_yaw));
        _predictedPos += dir * moveZ * MoveSpeed * dt;

        // Apply predicted pos/rot
        transform.position   = _predictedPos;
        transform.rotation   = Quaternion.Euler(0, _yaw * Mathf.Rad2Deg, 0);
    }

    // ── Server correction ─────────────────────────────────────────────────────
    // Gọi mỗi khi snapshot về (20Hz). Snap nếu sai quá threshold.
    public void OnServerCorrection(TankState ts)
    {
        // Spawn local tank nếu chưa có
        if (!GameManager.Instance) return;
        Vector3 serverPos = new Vector3(ts.x, ts.y, ts.z);

        float error = Vector3.Distance(_predictedPos, serverPos);
        if (error > CORRECTION_THRESHOLD)
        {
            _predictedPos = serverPos;   // hard snap
            _yaw = ts.yaw;
            Debug.Log($"[Local] corrected: err={error:F2}");
        }

        // Xử lý chết
        if (!ts.IsAlive)
            Debug.Log("[Local] tank died — waiting for respawn");
    }
}
