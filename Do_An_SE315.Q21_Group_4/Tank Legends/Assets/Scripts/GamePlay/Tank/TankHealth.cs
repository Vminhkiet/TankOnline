using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

namespace Complete
{
    public class TankHealth : MonoBehaviour
    {
        [Header("Tank Definition Link")]
        [Tooltip("Optional TankDefinition ScriptableObject to dynamically override starting health and segmented bar.")]
        public TankDefinitionSO m_Definition;

        public float m_StartingHealth = 100f;               // The amount of health each tank starts with.
        public Color m_FullHealthColor = Color.green;       // The color the health bar will be when on full health.
        public Color m_ZeroHealthColor = Color.red;         // The color the health bar will be when on no health.
        public GameObject m_ExplosionPrefab;                // A prefab that will be instantiated in Awake, then used whenever the tank dies.
        
        [Header("Death Options")]
        public bool m_HasDieAnimation = false;              // If true, plays Animator death. If false, spawns busted tank.
        public GameObject m_BustedTankPrefab;               // Prefab of the busted/destroyed tank model.

        [Header("Segmented Health Bar")]
        [Tooltip("Drag an empty RectTransform from the Canvas here. Position and size it manually in the Scene view. Code will fill it with segments.")]
        public RectTransform m_SegmentBarContainer;
        [Tooltip("HP per segment (e.g. 100 means each segment = 100 HP).")]
        public float m_HpPerSegment = 100f;
        [Tooltip("Width of the divider lines between segments.")]
        public float m_DividerWidth = 1f;
        [Tooltip("Color of the divider lines between segments.")]
        public Color m_DividerColor = new Color(0.1f, 0.1f, 0.1f, 0.9f);
        [Tooltip("Color of the health bar background (empty segments).")]
        public Color m_BackgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.8f);
        [Tooltip("Color of the health bar border.")]
        public Color m_BorderColor = new Color(0.2f, 0.2f, 0.2f, 1f);
        [Tooltip("Width of the border around the health bar.")]
        public float m_BorderWidth = 1f;

        [Header("Floating Text")]
        [Tooltip("Prefab for floating text. Must contain a TextMeshProUGUI component.")]
        public GameObject m_FloatingTextPrefab;
        [Tooltip("Container for floating damage/heal numbers. If empty, falls back to the health bar container.")]
        public RectTransform m_FloatingTextContainer;
        
        private TankAnimation m_TankAnimation;      // Reference to the external TankAnimation component.
        
        private AudioSource m_ExplosionAudio;               // The audio source to play when the tank explodes.
        private ParticleSystem m_ExplosionParticles;        // The particle system the will play when the tank is destroyed.
        private float m_CurrentHealth;                      // How much health the tank currently has.
        private bool m_Dead;                                // Has the tank been reduced beyond zero health yet?

        // Segmented bar runtime data
        private int m_SegmentCount;
        private Image[] m_SegmentFills;
        private bool m_SegmentedBarBuilt = false;

        // Floating Text Object Pool
        private const int FLOATING_TEXT_POOL_SIZE = 5;
        private GameObject[] m_FloatingTextPool;
        private RectTransform[] m_FloatingTextRects;
        private TextMeshProUGUI[] m_FloatingTextTMPro;

        private void Awake ()
        {
            // Instantiate the explosion prefab and get a reference to the particle system on it.
            m_ExplosionParticles = Instantiate (m_ExplosionPrefab).GetComponent<ParticleSystem> ();

            // Get a reference to the audio source on the instantiated prefab.
            m_ExplosionAudio = m_ExplosionParticles.GetComponent<AudioSource> ();

            // Disable the prefab so it can be activated when it's required.
            m_ExplosionParticles.gameObject.SetActive (false);
            
            m_TankAnimation = GetComponent<TankAnimation>();

            // Initialize Floating Text Pool
            m_FloatingTextPool = new GameObject[FLOATING_TEXT_POOL_SIZE];
            m_FloatingTextRects = new RectTransform[FLOATING_TEXT_POOL_SIZE];
            m_FloatingTextTMPro = new TextMeshProUGUI[FLOATING_TEXT_POOL_SIZE];

            RectTransform container = m_FloatingTextContainer;
            if (container == null) container = m_SegmentBarContainer;

            if (container != null && m_FloatingTextPrefab != null)
            {
                for (int i = 0; i < FLOATING_TEXT_POOL_SIZE; i++)
                {
                    GameObject textGO = Instantiate(m_FloatingTextPrefab, container);
                    
                    RectTransform rt = textGO.GetComponent<RectTransform>();
                    // Reset transform in case the prefab was saved with a weird offset or scale
                    rt.localPosition = Vector3.zero;
                    rt.localRotation = Quaternion.identity;
                    rt.localScale = Vector3.one;

                    TextMeshProUGUI tmpro = textGO.GetComponent<TextMeshProUGUI>();

                    textGO.SetActive(false);

                    m_FloatingTextPool[i] = textGO;
                    m_FloatingTextRects[i] = rt;
                    m_FloatingTextTMPro[i] = tmpro;
                }
            }
        }


        private void OnEnable()
        {
            if (m_Definition != null)
            {
                m_StartingHealth = m_Definition.RealStats.MaxHealth;
            }

            // When the tank is enabled, reset the tank's health and whether or not it's dead.
            m_CurrentHealth = m_StartingHealth;
            m_Dead = false;

            // Build segmented bar on first enable (after m_StartingHealth is set)
            BuildSegmentedBar();

            // Update the health slider's value and color.
            SetHealthUI();
        }


        public void TakeDamage (float amount)
        {
            // Reduce current health by the amount of damage done.
            m_CurrentHealth -= amount;
            
            SpawnFloatingText(-amount);

            // Change the UI elements appropriately.
            SetHealthUI ();

            // If the current health is at or below zero and it has not yet been registered, call OnDeath.
            if (m_CurrentHealth <= 0f && !m_Dead)
            {
                OnDeath ();
            }
        }

        // Called by GameManager when a server snapshot arrives (online mode).
        // Syncs health bar and triggers death sequence if health reached zero.
        public void SyncFromServer(float serverHealth)
        {
            float delta = serverHealth - m_CurrentHealth;
            if (Mathf.Abs(delta) >= 1f)
            {
                SpawnFloatingText(delta);
            }

            m_CurrentHealth = serverHealth;
            SetHealthUI();
            if (m_CurrentHealth <= 0f && !m_Dead)
                OnDeath();
        }


        // ──────────────────────────────────────────────
        //  Segmented Health Bar — built at runtime
        // ──────────────────────────────────────────────

        private void BuildSegmentedBar()
        {
            // If no container is assigned, fall back to the original Slider
            if (m_SegmentBarContainer == null) return;

            // Only build once; on respawn just reset fills
            if (m_SegmentedBarBuilt && m_SegmentFills != null)
            {
                m_SegmentBarContainer.gameObject.SetActive(true);
                ResetSegmentFills();
                return;
            }

            // Destroy any previous children (in case of rebuild)
            for (int c = m_SegmentBarContainer.childCount - 1; c >= 0; c--)
                Destroy(m_SegmentBarContainer.GetChild(c).gameObject);

            // Calculate segment count
            m_SegmentCount = Mathf.Max(1, Mathf.CeilToInt(m_StartingHealth / m_HpPerSegment));

            // ── Border / outline (slightly larger than container) ──
            var borderGO = new GameObject("Border");
            borderGO.transform.SetParent(m_SegmentBarContainer, false);
            var borderRT = borderGO.AddComponent<RectTransform>();
            borderRT.anchorMin = Vector2.zero;
            borderRT.anchorMax = Vector2.one;
            borderRT.offsetMin = new Vector2(-m_BorderWidth, -m_BorderWidth);
            borderRT.offsetMax = new Vector2(m_BorderWidth, m_BorderWidth);
            var borderImg = borderGO.AddComponent<Image>();
            borderImg.color = m_BorderColor;

            // ── Background ──
            var bgGO = new GameObject("Background");
            bgGO.transform.SetParent(m_SegmentBarContainer, false);
            var bgRT = bgGO.AddComponent<RectTransform>();
            bgRT.anchorMin = Vector2.zero;
            bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;
            var bgImg = bgGO.AddComponent<Image>();
            bgImg.color = m_BackgroundColor;

            // ── Segment fills ──
            m_SegmentFills = new Image[m_SegmentCount];

            for (int i = 0; i < m_SegmentCount; i++)
            {
                // Container for each segment
                var segGO = new GameObject($"Segment_{i}");
                segGO.transform.SetParent(m_SegmentBarContainer, false);
                var segRT = segGO.AddComponent<RectTransform>();

                float xMin = (float)i / m_SegmentCount;
                float xMax = (float)(i + 1) / m_SegmentCount;
                segRT.anchorMin = new Vector2(xMin, 0f);
                segRT.anchorMax = new Vector2(xMax, 1f);
                segRT.offsetMin = Vector2.zero;
                segRT.offsetMax = Vector2.zero;

                // The fill image inside this segment
                var fillGO = new GameObject("Fill");
                fillGO.transform.SetParent(segGO.transform, false);
                var fillRT = fillGO.AddComponent<RectTransform>();
                fillRT.anchorMin = new Vector2(0f, 0f);
                fillRT.anchorMax = new Vector2(1f, 1f);
                fillRT.offsetMin = Vector2.zero;
                fillRT.offsetMax = Vector2.zero;

                var fillImg = fillGO.AddComponent<Image>();
                fillImg.color = m_FullHealthColor;
                m_SegmentFills[i] = fillImg;
            }

            // ── Divider lines between segments ──
            for (int i = 1; i < m_SegmentCount; i++)
            {
                var divGO = new GameObject($"Divider_{i}");
                divGO.transform.SetParent(m_SegmentBarContainer, false);
                var divRT = divGO.AddComponent<RectTransform>();

                float anchorX = (float)i / m_SegmentCount;
                divRT.anchorMin = new Vector2(anchorX, 0f);
                divRT.anchorMax = new Vector2(anchorX, 1f);
                divRT.pivot = new Vector2(0.5f, 0.5f);
                divRT.sizeDelta = new Vector2(m_DividerWidth, 0f);
                divRT.anchoredPosition = Vector2.zero;

                var divImg = divGO.AddComponent<Image>();
                divImg.color = m_DividerColor;
            }


            m_SegmentedBarBuilt = true;
        }

        private void ResetSegmentFills()
        {
            if (m_SegmentFills == null) return;
            for (int i = 0; i < m_SegmentFills.Length; i++)
            {
                if (m_SegmentFills[i] != null)
                {
                    var rt = m_SegmentFills[i].GetComponent<RectTransform>();
                    rt.anchorMax = new Vector2(1f, 1f);
                    m_SegmentFills[i].color = m_FullHealthColor;
                    m_SegmentFills[i].gameObject.SetActive(true);
                }
            }
        }

        private void SetHealthUI ()
        {
            if (m_SegmentFills != null)
                UpdateSegmentedBar();
        }

        private void UpdateSegmentedBar()
        {
            float healthClamped = Mathf.Clamp(m_CurrentHealth, 0f, m_StartingHealth);
            float healthRatio = healthClamped / m_StartingHealth;

            for (int i = 0; i < m_SegmentCount; i++)
            {
                if (m_SegmentFills[i] == null) continue;

                // HP range this segment covers
                float segStart = i * m_HpPerSegment;
                float segEnd   = (i + 1) * m_HpPerSegment;
                // Clamp last segment to actual max HP
                if (segEnd > m_StartingHealth) segEnd = m_StartingHealth;
                float segRange = segEnd - segStart;

                float segFill;
                if (healthClamped >= segEnd)
                    segFill = 1f; // fully filled
                else if (healthClamped <= segStart)
                    segFill = 0f; // fully empty
                else
                    segFill = (healthClamped - segStart) / segRange; // partially filled

                // Update fill by adjusting anchor
                var rt = m_SegmentFills[i].GetComponent<RectTransform>();
                rt.anchorMax = new Vector2(segFill, 1f);

                // Color: gradient based on overall health ratio (green → yellow → red)
                Color segColor = Color.Lerp(m_ZeroHealthColor, m_FullHealthColor, healthRatio);

                // Add per-segment brightness variation for visual depth
                float segRatio = (float)i / Mathf.Max(1, m_SegmentCount - 1);
                // Slight brightness ramp: leftmost segments a tiny bit darker
                float brightness = Mathf.Lerp(0.85f, 1f, segRatio);
                segColor *= brightness;
                segColor.a = 1f;

                m_SegmentFills[i].color = segColor;

                // Hide completely empty segments for cleaner look
                m_SegmentFills[i].gameObject.SetActive(segFill > 0.001f);
            }
        }


        private void OnDeath ()
        {
            // Set the flag so that this function is only called once.
            m_Dead = true;

            if (m_HasDieAnimation)
            {
                // Nếu có animation chết thì chỉ gọi PlayDie
                if (m_TankAnimation != null)
                {
                    m_TankAnimation.PlayDie();
                }

                // Vô hiệu hóa script di chuyển và bắn để xe tăng không thao tác được nữa khi đang chạy anim
                var movement = GetComponent<TankMovement>();
                if (movement != null) movement.enabled = false;
                
                var shooting = GetComponent<TankShooting>();
                if (shooting != null) shooting.enabled = false;
                
                // Ghi chú: Cần dùng Animation Event hoặc script khác để gọi gameObject.SetActive(false) khi anim chạy xong.
            }
            else
            {
                // Nếu không có animation, chạy hiệu ứng nổ như cũ
                m_ExplosionParticles.transform.position = transform.position;
                m_ExplosionParticles.gameObject.SetActive (true);
                m_ExplosionParticles.Play ();
                m_ExplosionAudio.Play();

                // Spawn mô hình xe tăng vỡ vụn tại vị trí hiện tại
                if (m_BustedTankPrefab != null)
                {
                    Instantiate(m_BustedTankPrefab, transform.position, transform.rotation);
                }

                // Tắt xe tăng ngay lập tức
                gameObject.SetActive (false);
            }
        }

        // ──────────────────────────────────────────────
        //  Floating Text Logic (Object Pool)
        // ──────────────────────────────────────────────
        private void SpawnFloatingText(float amount)
        {
            if (m_FloatingTextPool == null || m_FloatingTextPrefab == null || !gameObject.activeInHierarchy || m_Dead) return;

            // Find an inactive object in the pool
            int poolIndex = -1;
            for (int i = 0; i < FLOATING_TEXT_POOL_SIZE; i++)
            {
                if (m_FloatingTextPool[i] != null && !m_FloatingTextPool[i].activeSelf)
                {
                    poolIndex = i;
                    break;
                }
            }

            if (poolIndex == -1) return; // Pool full, just ignore

            GameObject textGO = m_FloatingTextPool[poolIndex];
            RectTransform rt = m_FloatingTextRects[poolIndex];
            TextMeshProUGUI tmpro = m_FloatingTextTMPro[poolIndex];

            // Randomize X slightly based on the text's own width so it scales correctly
            float randomX = UnityEngine.Random.Range(-rt.rect.width * 0.25f, rt.rect.width * 0.25f);
            rt.localPosition = new Vector3(randomX, 0f, 0f); // Use localPosition to ensure it's relative to container's pivot

            tmpro.text = amount > 0 ? $"+{Mathf.RoundToInt(amount)}" : $"{Mathf.RoundToInt(amount)}";
            tmpro.color = amount > 0 ? Color.green : Color.red;
            
            textGO.SetActive(true);
            StartCoroutine(AnimateFloatingText(textGO, rt, tmpro));
        }

        private IEnumerator AnimateFloatingText(GameObject go, RectTransform rt, TextMeshProUGUI tmpro)
        {
            float duration = 1.0f;
            float elapsed = 0f;
            
            // Use world position to avoid being affected by parent's rotation (like if Canvas is flipped)
            Vector3 startPos = rt.position;
            
            // Calculate distance in world space. Ensure it moves at least 1.5 world units.
            float worldFloatDist = Mathf.Max(rt.rect.height * 1.5f * rt.lossyScale.y, 1.5f);
            Vector3 endPos = startPos + Vector3.up * worldFloatDist; 
            
            Color startColor = tmpro.color;
            Color endColor = new Color(startColor.r, startColor.g, startColor.b, 0f);

            while (elapsed < duration)
            {
                if (go == null || !go.activeSelf) yield break;
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                
                float easeT = 1f - Mathf.Pow(1f - t, 3f); // easeOutCubic
                
                rt.position = Vector3.Lerp(startPos, endPos, easeT);
                tmpro.color = Color.Lerp(startColor, endColor, easeT);
                
                yield return null;
            }
            
            if (go != null) go.SetActive(false); // Return to pool
        }
    }
}