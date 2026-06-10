using System.Collections.Generic;
using UnityEngine;
using Complete; // For TankHealth

namespace Complete.Skills
{
    public class StatModifier
    {
        public string SourceId;
        public float Duration;
        public float SpeedMultiplier = 1f;
        public float DamageMultiplier = 1f;
        public float HpRegenPerSecond = 0f;

        public StatModifier(string sourceId, float duration)
        {
            SourceId = sourceId;
            Duration = duration;
        }
    }

    /// <summary>
    /// Attach this to the Tank to handle buffs and debuffs.
    /// This is used by the Server to track stats. Clients might use it for UI if synced.
    /// </summary>
    public class TankStatModifierSystem : MonoBehaviour
    {
        private List<StatModifier> m_Modifiers = new List<StatModifier>();
        private TankHealth m_TankHealth;

        private void Awake()
        {
            m_TankHealth = GetComponent<TankHealth>();
        }

        public void AddModifier(StatModifier mod)
        {
            // If modifier from same source exists, reset its duration
            var existing = m_Modifiers.Find(m => m.SourceId == mod.SourceId);
            if (existing != null)
            {
                existing.Duration = mod.Duration;
            }
            else
            {
                m_Modifiers.Add(mod);
            }
        }

        public void RemoveModifier(string sourceId)
        {
            m_Modifiers.RemoveAll(m => m.SourceId == sourceId);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            for (int i = m_Modifiers.Count - 1; i >= 0; i--)
            {
                var mod = m_Modifiers[i];
                
                // Handle HP Regen
                if (mod.HpRegenPerSecond > 0f && m_TankHealth != null)
                {
                    // For Server: actually apply healing.
                    // For Client: wait for Server snapshot.
                    // In a C++ server setup, the C++ server would actually do this. This is just the C# representation.
                    // m_TankHealth.TakeDamage(-mod.HpRegenPerSecond * dt); 
                }

                mod.Duration -= dt;
                if (mod.Duration <= 0f)
                {
                    m_Modifiers.RemoveAt(i);
                }
            }
        }

        public float GetSpeedMultiplier()
        {
            float mult = 1f;
            foreach (var mod in m_Modifiers)
                mult *= mod.SpeedMultiplier;
            return mult;
        }

        public float GetDamageMultiplier()
        {
            float mult = 1f;
            foreach (var mod in m_Modifiers)
                mult *= mod.DamageMultiplier;
            return mult;
        }
    }
}
