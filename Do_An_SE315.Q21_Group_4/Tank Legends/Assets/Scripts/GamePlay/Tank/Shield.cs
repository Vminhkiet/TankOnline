using UnityEngine;

namespace Complete
{
    public class Shield : MonoBehaviour
    {
        [Header("Shield Settings")]
        [Tooltip("Optional visual effect to play or toggle when blocking a shell.")]
        public GameObject m_BlockEffect;

        [Header("Shader Ripple Animation")]
        [Tooltip("Duration of the impact ripple animation.")]
        public float m_FadeDuration = 0.8f;
        [Tooltip("Starting radius of the impact ripple.")]
        public float m_StartImpactRadius = 0.0f;
        [Tooltip("Minimum radius to ensure small impacts are still visible.")]
        public float m_MinImpactRadius = 0.5f;
        [Tooltip("Ending radius of the impact ripple.")]
        public float m_EndImpactRadius = 3.0f;
        [Tooltip("Maximum strength of the impact visual.")]
        public float m_MaxImpactStrength = 1.0f;
        public uint OwnerId;

        private Material m_ShieldMaterial;
        private int m_ImpactPosID;
        private int m_ImpactRadiusID;
        private int m_ImpactStrengthID;
        private Coroutine m_ImpactCoroutine;

        public static System.Collections.Generic.List<Shield> ActiveShields = new System.Collections.Generic.List<Shield>();

        private void OnEnable()
        {
            ActiveShields.Add(this);
        }

        private void OnDisable()
        {
            ActiveShields.Remove(this);
        }

        public void DeactivateAfter(float delay)
        {
            StopAllCoroutines();
            StartCoroutine(DeactivateRoutine(delay));
        }

        private System.Collections.IEnumerator DeactivateRoutine(float delay)
        {
            yield return new WaitForSeconds(delay);
            gameObject.SetActive(false);
        }

        private void Awake()
        {
            Renderer rend = GetComponent<Renderer>();
            if (rend != null)
            {
                m_ShieldMaterial = rend.material;
            }

            // Ensure the collider is a trigger so it doesn't push tanks
            Collider coll = GetComponent<Collider>();
            if (coll != null)
            {
                coll.isTrigger = true;
            }

            // Resolve shader property IDs dynamically (supporting with or without underscore prefix)
            if (m_ShieldMaterial != null)
            {
                m_ImpactPosID = m_ShieldMaterial.HasProperty("_ImpactPosition") ? Shader.PropertyToID("_ImpactPosition") : Shader.PropertyToID("ImpactPosition");
                m_ImpactRadiusID = m_ShieldMaterial.HasProperty("_ImpactRadius") ? Shader.PropertyToID("_ImpactRadius") : Shader.PropertyToID("ImpactRadius");
                m_ImpactStrengthID = m_ShieldMaterial.HasProperty("_ImpactStrength") ? Shader.PropertyToID("_ImpactStrength") : Shader.PropertyToID("ImpactStrength");
            }
        }

        private void Start()
        {
            // Reset impact visual on start
            if (m_ShieldMaterial != null)
            {
                m_ShieldMaterial.SetFloat(m_ImpactStrengthID, 0f);
                m_ShieldMaterial.SetFloat(m_ImpactRadiusID, 0f);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            Collider shieldCollider = GetComponent<Collider>();
            if (shieldCollider == null) return;

            // Try to find the ShellExplosion component on the entering object
            ShellExplosion shell = other.GetComponent<ShellExplosion>();
            if (shell != null)
            {
                // Check if the shell's spawn position was inside the shield.
                // ClosestPoint returns the spawn position itself if it is inside the collider volume.
                Vector3 closestPointOnShield = shieldCollider.ClosestPoint(shell.m_SpawnPosition);
                float distanceToClosest = Vector3.Distance(closestPointOnShield, shell.m_SpawnPosition);

                // If the shell started inside the shield, it moves and exits normally
                if (distanceToClosest < 0.01f)
                {
                    return;
                }

                // For impact position, find the exact collision boundary point
                Vector3 hitPoint = shieldCollider.ClosestPoint(shell.transform.position);

                // If the shell was spawned outside the shield, block and explode it at the boundary
                Rigidbody shellRb = shell.GetComponent<Rigidbody>();
                bool isRemote = !shell.enabled;

                if (m_BlockEffect != null)
                {
                    // Use transform.forward since remote bullets have no Rigidbody velocity
                    Instantiate(m_BlockEffect, hitPoint, Quaternion.LookRotation(-shell.transform.forward));
                }

                // Trigger the shader impact visual ripple
                TriggerImpact(hitPoint);

                if (!isRemote)
                {
                    shell.Explode();
                }
                else
                {
                    // Hide the remote bullet so it doesn't visually clip through before the server destroys it
                    foreach (var rend in shell.GetComponentsInChildren<Renderer>())
                    {
                        rend.enabled = false;
                    }
                }
            }
            else
            {
                // For any other collision/impact (e.g., other tanks, physics objects), trigger the visual ripple
                Vector3 hitPoint = shieldCollider.ClosestPoint(other.transform.position);
                TriggerImpact(hitPoint);
            }
        }

        public void TriggerBlockEffect(Vector3 hitPoint, Vector3 hitDirection)
        {
            if (m_BlockEffect != null)
            {
                Instantiate(m_BlockEffect, hitPoint, Quaternion.LookRotation(hitDirection));
            }
            TriggerImpact(hitPoint);
        }

        public void TriggerImpact(Vector3 hitPoint)
        {
            if (m_ShieldMaterial == null) return;

            if (m_ImpactCoroutine != null)
            {
                StopCoroutine(m_ImpactCoroutine);
            }
            m_ImpactCoroutine = StartCoroutine(AnimateImpact(hitPoint));
        }

        private System.Collections.IEnumerator AnimateImpact(Vector3 hitPoint)
        {
            // Pass hit position to shader
            m_ShieldMaterial.SetVector(m_ImpactPosID, hitPoint);

            float elapsed = 0f;
            while (elapsed < m_FadeDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / m_FadeDuration;

                // Animate radius growing and strength fading out, clamped to minimum
                float radius = Mathf.Max(Mathf.Lerp(m_StartImpactRadius, m_EndImpactRadius, t), m_MinImpactRadius);
                float strength = Mathf.Lerp(m_MaxImpactStrength, 0f, t);

                m_ShieldMaterial.SetFloat(m_ImpactRadiusID, radius);
                m_ShieldMaterial.SetFloat(m_ImpactStrengthID, strength);

                yield return null;
            }

            // Clean up values at the end of the fade
            m_ShieldMaterial.SetFloat(m_ImpactStrengthID, 0f);
            m_ImpactCoroutine = null;
        }
    }
}
