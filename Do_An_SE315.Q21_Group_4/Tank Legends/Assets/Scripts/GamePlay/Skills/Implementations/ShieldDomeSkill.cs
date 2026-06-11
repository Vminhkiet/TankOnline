using UnityEngine;
using Complete; // For TankStatModifierSystem if needed

namespace Complete.Skills
{
    public class ShieldDomeSkill : SkillBase
    {
        private GameObject[] m_ShieldPool;
        private int m_PoolIndex = 0;

        public ShieldDomeSkill(SkillData data, GameObject owner) : base(data, owner) 
        { 
            if (Data.vfxPrefab != null && owner != null)
            {
                m_ShieldPool = new GameObject[2];
                for (int i = 0; i < 2; i++)
                {
                    m_ShieldPool[i] = UnityEngine.Object.Instantiate(Data.vfxPrefab);
                    
                    // Scale it based on Data.radius
                    float scaleFactor = Data.vfxBaseSize > 0f ? (Data.radius / Data.vfxBaseSize) : 1f;
                    m_ShieldPool[i].transform.localScale = Vector3.one * scaleFactor;
                    
                    m_ShieldPool[i].SetActive(false);
                }
            }
        }

        public override void ServerExecute(Vector3 targetPosition, Vector3 targetDirection)
        {
            // Server spawns the authoritative dome logic
            // In a real authoritative setup, the C++ server creates the region. 
            // Here, we can instantiate a C# prefab with a trigger collider if we were doing it in Unity.
            // But since C++ handles it, we might just send a packet, or if this is the C# representation:
            if (Data.vfxPrefab != null)
            {
                // We assume there's a "Server Shield Dome" prefab that has logic to block projectiles and slow enemies.
                // For simplicity here we just show the structure.
                // GameObject serverDome = Object.Instantiate(ServerDomePrefab, targetPosition, Quaternion.identity);
                // Object.Destroy(serverDome, 5f);
            }
        }

        public override void ClientExecute(Vector3 targetPosition, Vector3 targetDirection)
        {
            if (m_ShieldPool != null && m_ShieldPool.Length > 0)
            {
                GameObject vfx = m_ShieldPool[m_PoolIndex];
                m_PoolIndex = (m_PoolIndex + 1) % m_ShieldPool.Length;

                var ts = Owner.GetComponent<TankSkills>();
                Transform pivot = (ts != null && ts.m_SkillSpawnPoint != null) ? ts.m_SkillSpawnPoint : Owner.transform;

                Vector3 spawnLocation = (Data.targetingType == TargetingType.Self || Data.targetingType == TargetingType.Direction) ? pivot.position : targetPosition;

                // Reset position/rotation just in case
                vfx.transform.position = spawnLocation;
                vfx.transform.rotation = Data.vfxPrefab.transform.rotation;
                
                // Toggle active to trigger OnEnable
                vfx.SetActive(false);
                vfx.SetActive(true);

                float destroyTime = Data.vfxDuration > 0f ? Data.vfxDuration : 3f;
                var shieldScript = vfx.GetComponent<Complete.Shield>();
                if (shieldScript != null)
                {
                    shieldScript.DeactivateAfter(destroyTime);
                    if (ts != null) { shieldScript.OwnerId = ts.OwnerId; }
                }
            }

            if (Data.castSound != null)
            {
                AudioSource.PlayClipAtPoint(Data.castSound, targetPosition);
            }
        }
    }
}
