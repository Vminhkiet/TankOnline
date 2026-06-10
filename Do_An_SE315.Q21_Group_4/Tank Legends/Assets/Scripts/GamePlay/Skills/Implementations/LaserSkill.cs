using UnityEngine;

namespace Complete.Skills
{
    public class LaserSkill : SkillBase
    {
        public LaserSkill(SkillData data, GameObject owner) : base(data, owner) { }

        public override void ServerExecute(Vector3 targetPosition, Vector3 targetDirection)
        {
            // Authoritative Raycast to detect enemies
            Ray ray = new Ray(Owner.transform.position + Vector3.up * 1f, targetDirection.normalized);
            
            // Using RaycastAll to hit multiple enemies
            RaycastHit[] hits = Physics.RaycastAll(ray, Data.length);
            
            foreach (var hit in hits)
            {
                if (hit.collider.gameObject == Owner) continue;

                var tankHealth = hit.collider.GetComponentInParent<TankHealth>();
                if (tankHealth != null)
                {
                    // Apply Damage
                    // If we had a StatModifierSystem on the caster, we could apply damage multipliers here.
                    float baseDamage = 50f;
                    float finalDamage = baseDamage;
                    
                    var modSys = Owner.GetComponent<TankStatModifierSystem>();
                    if (modSys != null) finalDamage *= modSys.GetDamageMultiplier();

                    // In a true C++ server, the C++ code performs the Raycast.
                    // tankHealth.TakeDamage(finalDamage);
                }
            }
        }

        public override void ClientExecute(Vector3 targetPosition, Vector3 targetDirection)
        {
            // Visuals
            if (Data.vfxPrefab != null)
            {
                var ts = Owner.GetComponent<TankSkills>();
                Transform pivot = (ts != null && ts.m_SkillSpawnPoint != null) ? ts.m_SkillSpawnPoint : Owner.transform;

                bool shouldParent = (Data.targetingType == TargetingType.Self);
                Vector3 spawnLocation = pivot.position; // Laser always originates from pivot

                // Instantiate laser beam prefab and orient it
                var vfx = UnityEngine.Object.Instantiate(Data.vfxPrefab, spawnLocation, Data.vfxPrefab.transform.rotation);
                
                if (shouldParent)
                {
                    vfx.transform.SetParent(pivot);
                    vfx.transform.localPosition = Vector3.zero;
                }

                if (targetDirection != Vector3.zero)
                {
                    float targetYaw = Mathf.Atan2(targetDirection.x, targetDirection.z) * Mathf.Rad2Deg;
                    Vector3 currentEuler = vfx.transform.eulerAngles;
                    vfx.transform.eulerAngles = new Vector3(currentEuler.x, targetYaw, currentEuler.z);
                }
                
                // Assuming the laser VFX is designed to be scaled on the Z axis to match length
                float scaleFactor = Data.vfxBaseSize > 0f ? (Data.length / Data.vfxBaseSize) : 1f;
                Vector3 prefabScale = Data.vfxPrefab.transform.localScale;
                Vector3 pScale = shouldParent ? pivot.lossyScale : Vector3.one;

                vfx.transform.localScale = new Vector3(
                    prefabScale.x / pScale.x,
                    prefabScale.y / pScale.y,
                    scaleFactor / pScale.z
                );

                float destroyTime = Data.vfxDuration > 0f ? Data.vfxDuration : 3f;
                UnityEngine.Object.Destroy(vfx, destroyTime);
            }

            if (Data.castSound != null)
            {
                AudioSource.PlayClipAtPoint(Data.castSound, Owner.transform.position);
            }
        }
    }
}
