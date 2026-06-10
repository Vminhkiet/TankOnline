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

                float buffDuration = Data.duration > 0f ? Data.duration : 8f;
                // Nhập 20 nghĩa là +20% sát thương (1.2x)
                float dmgMult = (Data.parameters != null && Data.parameters.Length > 0 && Data.parameters[0] > 0f) ? (1f + Data.parameters[0] / 100f) : 1.15f;
                // Nhập 5 nghĩa là 5% máu mỗi giây (0.05)
                float hpRegenPct = (Data.parameters != null && Data.parameters.Length > 1 && Data.parameters[1] > 0f) ? (Data.parameters[1] / 100f) : 0.05f;

                var buff = new StatModifier("BuffSkill", buffDuration)
                {
                    DamageMultiplier = dmgMult,
                    HpRegenPerSecond = maxHp * hpRegenPct
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
