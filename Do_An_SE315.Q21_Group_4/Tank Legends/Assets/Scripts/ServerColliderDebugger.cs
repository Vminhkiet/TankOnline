using UnityEngine;

/// Vẽ collider server (OBB tank + sphere bullet) lên Scene/Game view để debug.
/// Attach vào bất kỳ GameObject nào trong scene. Kích hoạt Gizmos trong Game view để thấy.
[ExecuteAlways]
public class ServerColliderDebugger : MonoBehaviour
{
    [Header("Hiển thị")]
    public bool showTanks   = true;
    public bool showBullets = true;

    [Header("Màu")]
    public Color tankColor   = new Color(0f, 1f, 0f, 1f);
    public Color bulletColor = new Color(1f, 0.4f, 0f, 1f);

    [Header("Tank OBB (half-extents – khớp server TankConfig)")]
    public float extentX = 0.9f;
    public float extentY = 1.0f;
    public float extentZ = 1.2f;

    [Header("Bullet Sphere (radius – khớp server BulletConfig)")]
    public float bulletRadius = 0.25f;

    void OnDrawGizmos()
    {
        if (showTanks)   DrawTanks();
        if (showBullets) DrawBullets();
    }

    void DrawTanks()
    {
        Gizmos.color = tankColor;
        foreach (GameObject tank in GameObject.FindGameObjectsWithTag("Tank"))
        {
            // Server đặt center = position + (0, extentY, 0), xoay theo yaw
            Vector3 center = tank.transform.position + Vector3.up * extentY;
            Quaternion rot = Quaternion.Euler(0f, tank.transform.eulerAngles.y, 0f);

            Gizmos.matrix = Matrix4x4.TRS(center, rot, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(extentX * 2f, extentY * 2f, extentZ * 2f));
            Gizmos.matrix = Matrix4x4.identity;
        }
    }

    void DrawBullets()
    {
        Gizmos.color = bulletColor;
        foreach (GameObject shell in GameObject.FindGameObjectsWithTag("Shell"))
            Gizmos.DrawWireSphere(shell.transform.position, bulletRadius);
    }
}
