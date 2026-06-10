using UnityEngine;

namespace Complete.Skills
{
    /// <summary>
    /// Abstract base class for all skills. 
    /// Instantiated at runtime per player/skill slot.
    /// Handles cooldowns and provides separate entry points for Server (Logic) and Client (Visuals).
    /// </summary>
    public abstract class SkillBase
    {
        public SkillData Data { get; private set; }
        public float CurrentCooldown { get; private set; }
        
        protected GameObject Owner { get; private set; }
        protected GameObject m_ActiveChargeVfx;
        protected AudioSource m_ActiveChargeAudio;

        public SkillBase(SkillData data, GameObject owner)
        {
            Data = data;
            Owner = owner;
            CurrentCooldown = 0f;
        }

        public void TickCooldown(float dt)
        {
            if (CurrentCooldown > 0f)
            {
                CurrentCooldown -= dt;
                if (CurrentCooldown < 0f) CurrentCooldown = 0f;
            }
        }

        public bool CanCast()
        {
            return CurrentCooldown <= 0f;
        }

        public void StartCooldown()
        {
            CurrentCooldown = Data.cooldown;
        }

        /// <summary>
        /// Called when the skill begins its charging phase.
        /// Client spawns charging effects here.
        /// </summary>
        public virtual void ClientStartCharge(Vector3 targetPosition, Vector3 targetDirection)
        {
            if (Data.chargeVfxPrefab != null)
            {
                var ts = Owner.GetComponent<TankSkills>();
                Transform pivot = (ts != null && ts.m_SkillSpawnPoint != null) ? ts.m_SkillSpawnPoint : Owner.transform;

                bool shouldParent = true;
                Vector3 spawnLocation = pivot.position;

                m_ActiveChargeVfx = UnityEngine.Object.Instantiate(Data.chargeVfxPrefab, spawnLocation, Data.chargeVfxPrefab.transform.rotation);
                
                if (shouldParent)
                {
                    m_ActiveChargeVfx.transform.SetParent(pivot);
                    m_ActiveChargeVfx.transform.localPosition = Vector3.zero;
                    
                    // Inverse parent scale to maintain the exact visual size of the prefab
                    Vector3 prefabScale = Data.chargeVfxPrefab.transform.localScale;
                    Vector3 pScale = pivot.lossyScale;
                    m_ActiveChargeVfx.transform.localScale = new Vector3(
                        prefabScale.x / pScale.x,
                        prefabScale.y / pScale.y,
                        prefabScale.z / pScale.z
                    );
                }

                if (targetDirection != Vector3.zero && Data.targetingType != TargetingType.Self)
                {
                    float targetYaw = Mathf.Atan2(targetDirection.x, targetDirection.z) * Mathf.Rad2Deg;
                    Vector3 currentEuler = m_ActiveChargeVfx.transform.eulerAngles;
                    m_ActiveChargeVfx.transform.eulerAngles = new Vector3(currentEuler.x, targetYaw, currentEuler.z);
                }
            }

            if (Data.chargeSound != null)
            {
                // Create a temporary audio source to play and loop (or just play) the charge sound so it can be stopped
                m_ActiveChargeAudio = Owner.AddComponent<AudioSource>();
                m_ActiveChargeAudio.clip = Data.chargeSound;
                m_ActiveChargeAudio.spatialBlend = 1f; // 3D sound
                m_ActiveChargeAudio.Play();
            }
        }

        /// <summary>
        /// Called if the charge is interrupted or when it successfully finishes and executes.
        /// Cleans up charging VFX.
        /// </summary>
        public virtual void ClientCancelCharge()
        {
            if (m_ActiveChargeVfx != null)
            {
                UnityEngine.Object.Destroy(m_ActiveChargeVfx, Data.chargeVfxDestroyDelay);
                m_ActiveChargeVfx = null;
            }

            if (m_ActiveChargeAudio != null)
            {
                if (Data.chargeVfxDestroyDelay <= 0f)
                {
                    m_ActiveChargeAudio.Stop();
                }
                UnityEngine.Object.Destroy(m_ActiveChargeAudio, Data.chargeVfxDestroyDelay);
                m_ActiveChargeAudio = null;
            }
        }

        /// <summary>
        /// Authoritative gameplay logic (Damage, Movement, Buffs).
        /// Expected to be called by the Server or Host.
        /// </summary>
        /// <param name="targetPosition">Target position in world space.</param>
        /// <param name="targetDirection">Target direction in world space.</param>
        public abstract void ServerExecute(Vector3 targetPosition, Vector3 targetDirection);

        /// <summary>
        /// Client-side presentation logic (VFX, SFX, Animations).
        /// Expected to be called on all clients when the server confirms the cast.
        /// </summary>
        /// <param name="targetPosition">Target position in world space.</param>
        /// <param name="targetDirection">Target direction in world space.</param>
        public abstract void ClientExecute(Vector3 targetPosition, Vector3 targetDirection);
    }
}
