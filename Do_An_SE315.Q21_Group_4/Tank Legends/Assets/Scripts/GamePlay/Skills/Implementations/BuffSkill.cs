using UnityEngine;

namespace Complete.Skills
{
    public class BuffSkill : SkillBase
    {
        public BuffSkill(SkillData data, GameObject owner) : base(data, owner) { }

        public override void ServerExecute(Vector3 targetPosition, Vector3 targetDirection)
        {
            var modifierSys = Owner.GetComponent<TankStatModifierSystem>();
            if (modifierSys != null)
            {
                // Increase damage by 15%, Regenerate 5% max HP per second, Duration 8 seconds
                // Requires TankHealth to know Max HP, assuming 100 for this example or fetch it
                float maxHp = 100f;
                var tankHealth = Owner.GetComponent<TankHealth>();
                if (tankHealth != null) maxHp = tankHealth.m_StartingHealth;

                var buff = new StatModifier("BuffSkill", 8f)
                {
                    DamageMultiplier = 1.15f,
                    HpRegenPerSecond = maxHp * 0.05f
                };
                modifierSys.AddModifier(buff);
            }
        }

        public override void ClientExecute(Vector3 targetPosition, Vector3 targetDirection)
        {
            // Visuals
            if (Data.vfxPrefab != null)
            {
                // Attach aura to the tank
                var vfx = Object.Instantiate(Data.vfxPrefab, Owner.transform);
                vfx.transform.localPosition = Vector3.zero;
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
