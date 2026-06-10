using UnityEngine;
using Complete; // For TankStatModifierSystem if needed

namespace Complete.Skills
{
    public class ShieldDomeSkill : SkillBase
    {
        public ShieldDomeSkill(SkillData data, GameObject owner) : base(data, owner) { }

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
            if (Data.vfxPrefab != null)
            {
                var ts = Owner.GetComponent<TankSkills>();
                Transform pivot = (ts != null && ts.m_SkillSpawnPoint != null) ? ts.m_SkillSpawnPoint : Owner.transform;

                bool shouldParent = (Data.targetingType == TargetingType.Self);
                Vector3 spawnLocation = (Data.targetingType == TargetingType.Self || Data.targetingType == TargetingType.Direction) ? pivot.position : targetPosition;

                // Spawn the visual dome at the target position, keeping original prefab rotation
                var vfx = UnityEngine.Object.Instantiate(Data.vfxPrefab, spawnLocation, Data.vfxPrefab.transform.rotation);
                
                if (shouldParent)
                {
                    vfx.transform.SetParent(pivot);
                    vfx.transform.localPosition = Vector3.zero;
                }

                // Scale it based on Data.radius normalized by vfxBaseSize
                float scaleFactor = Data.vfxBaseSize > 0f ? (Data.radius / Data.vfxBaseSize) : 1f;

                if (shouldParent)
                {
                    Vector3 pScale = pivot.lossyScale;
                    vfx.transform.localScale = new Vector3(
                        scaleFactor / pScale.x,
                        scaleFactor / pScale.y,
                        scaleFactor / pScale.z
                    );
                }
                else
                {
                    vfx.transform.localScale = Vector3.one * scaleFactor;
                }

                float destroyTime = Data.vfxDuration > 0f ? Data.vfxDuration : 3f;
                UnityEngine.Object.Destroy(vfx, destroyTime);
            }

            if (Data.castSound != null)
            {
                AudioSource.PlayClipAtPoint(Data.castSound, targetPosition);
            }
        }
    }
}
