using UnityEditor;
using UnityEngine;
using System.IO;

public class DebugTankSpeed
{
    [MenuItem("Tools/Debug Tank Speed")]
    public static void DebugSpeed()
    {
        string logPath = "D:/CodeProject/SE315.Q21/tank_speeds.txt";
        using (StreamWriter writer = new StreamWriter(logPath))
        {
            string[] guids = AssetDatabase.FindAssets("t:TankDefinitionSO");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                TankDefinitionSO def = AssetDatabase.LoadAssetAtPath<TankDefinitionSO>(path);
                if (def != null && def.GameplayPrefab != null)
                {
                    var mov = def.GameplayPrefab.GetComponent<Complete.TankMovement>();
                    if (mov != null)
                    {
                        writer.WriteLine($"[{def.TankName}] m_Speed = {mov.m_Speed}, m_TurnSpeed = {mov.m_TurnSpeed}");
                    }
                }
            }
        }
        Debug.Log("Wrote to " + logPath);
    }
}
