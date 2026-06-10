using UnityEngine;

namespace Complete.Skills
{
    public class DashSkill : SkillBase
    {
        public float DashDistance = 10f;
        
        public DashSkill(SkillData data, GameObject owner) : base(data, owner) { }

        public override void ServerExecute(Vector3 targetPosition, Vector3 targetDirection)
        {
            // Authoritative server moves the player
            var rb = Owner.GetComponent<Rigidbody>();
            if (rb != null)
            {
                // Simple teleport for dash logic. 
                // A better approach would be applying velocity or using a Server-side lerp.
                Vector3 newPos = rb.position + targetDirection.normalized * DashDistance;
                rb.MovePosition(newPos);
            }
        }

        public override void ClientExecute(Vector3 targetPosition, Vector3 targetDirection)
        {
            // Visuals
            if (Data.vfxPrefab != null)
            {
                // Spawn trail or dust particles
                var vfx = Object.Instantiate(Data.vfxPrefab, Owner.transform.position, Owner.transform.rotation);
                float destroyTime = Data.vfxDuration > 0f ? Data.vfxDuration : 3f;
                Object.Destroy(vfx, destroyTime);
            }

            if (Data.castSound != null)
            {
                AudioSource.PlayClipAtPoint(Data.castSound, Owner.transform.position);
            }
        }
    }
}
