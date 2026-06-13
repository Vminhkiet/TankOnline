using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace Complete
{
    /// <summary>
    /// Handles bush stealth mechanic and 12m vision logic for tanks.
    /// - Local tank: uses OnTriggerEnter/Exit to detect bush colliders (tag "Bush")
    ///   and sets _Dissolve = 0.5 (semi-transparent to the player).
    /// - Remote tank: reads InBush flag from server snapshot and sets _Dissolve = 1.0
    ///   (fully invisible to enemies).
    /// 
    /// Also hides the health UI (Canvas) of remote tanks when they are stealthed or beyond 12m.
    /// </summary>
    public class TankStealth : MonoBehaviour
    {
        [Header("Dissolve Settings")]
        public float m_LocalDissolve = 0.5f;    // Value when local player is in bush
        public float m_RemoteDissolve = 1.0f;    // Value when enemy is in bush (invisible)
        public float m_LerpSpeed = 8f;           // Speed of dissolve transition
        public float m_GracePeriod = 0.2f;       // Delay before "exiting" bush (anti-flicker)

        [Header("Minimap & Vision")]
        public float m_VisionRadius = 12f;
        public SpriteRenderer m_MinimapDot;
        public Color m_LocalMinimapColor = Color.blue;
        public Color m_EnemyMinimapColor = Color.red;

        /// <summary>
        /// Set by GameManager from server snapshot for remote tanks.
        /// </summary>
        [HideInInspector] public bool ServerInBush;
        [HideInInspector] public int ServerBushRegion;
        [HideInInspector] public bool ServerRevealedOnMap;

        /// <summary>
        /// True if this is the local player's tank.
        /// </summary>
        [HideInInspector] public bool IsLocalTank;
        
        public static int s_LocalPlayerBushRegion;
        public static Transform s_LocalTankTransform;

        // Internal state
        private bool _inBush;                     // Computed state: currently considered "in bush"
        private float _currentDissolve;           // Current dissolve value being applied

        private static readonly int DissolveID = Shader.PropertyToID("_Dissolve");

        // Renderers whose materials support the _Dissolve property
        private readonly List<Renderer> _dissolveRenderers = new();
        private MaterialPropertyBlock _mpb;

        // UI elements to hide when stealthed (health bar, etc.)
        private Canvas _worldCanvas;

        private void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            CacheDissolveRenderers();
            CacheWorldCanvas();
        }

        private void Start()
        {
            if (IsLocalTank)
            {
                s_LocalTankTransform = transform;
            }
        }

        private void OnDestroy()
        {
            if (IsLocalTank && s_LocalTankTransform == transform)
            {
                s_LocalTankTransform = null;
            }
        }

        /// <summary>
        /// Scans all child Renderers and caches those whose material has a _Dissolve property.
        /// Called once on spawn.
        /// </summary>
        private void CacheDissolveRenderers()
        {
            _dissolveRenderers.Clear();
            foreach (var rend in GetComponentsInChildren<Renderer>(true))
            {
                // Skip the minimap dot sprite
                if (rend == m_MinimapDot) continue;

                // Check each material on the renderer
                foreach (var mat in rend.sharedMaterials)
                {
                    if (mat != null && mat.HasProperty(DissolveID))
                    {
                        _dissolveRenderers.Add(rend);
                        break; // One match per renderer is enough
                    }
                }
            }
        }

        /// <summary>
        /// Finds the WorldSpace Canvas (health bar UI) attached to this tank.
        /// </summary>
        private void CacheWorldCanvas()
        {
            // Health bar Canvas is usually a child with RenderMode.WorldSpace
            foreach (var canvas in GetComponentsInChildren<Canvas>(true))
            {
                if (canvas.renderMode == RenderMode.WorldSpace)
                {
                    _worldCanvas = canvas;
                    break;
                }
            }
        }

        private void Update()
        {
            bool isSharedBush = false;

            // Determine "in bush" state purely from server
            _inBush = ServerInBush;

            if (IsLocalTank)
            {
                s_LocalPlayerBushRegion = ServerBushRegion;
            }
            else
            {
                // Bypass full stealth if sharing the same BushRegion
                if (_inBush && ServerBushRegion != 0 && ServerBushRegion == s_LocalPlayerBushRegion)
                {
                    isSharedBush = true;
                }
            }

            // --- 1. MINIMAP LOGIC ---
            if (m_MinimapDot != null)
            {
                if (IsLocalTank)
                {
                    m_MinimapDot.color = m_LocalMinimapColor;
                    m_MinimapDot.enabled = true;
                }
                else
                {
                    m_MinimapDot.color = m_EnemyMinimapColor;
                    
                    float distance = s_LocalTankTransform != null ? Vector3.Distance(transform.position, s_LocalTankTransform.position) : float.MaxValue;
                    bool visibleOnScreen = (distance <= m_VisionRadius) && (!_inBush || isSharedBush);
                    
                    // Show on minimap if visible on screen OR revealed by combat anywhere
                    m_MinimapDot.enabled = visibleOnScreen || ServerRevealedOnMap;
                }
            }

            // --- 2. SCREEN VISIBILITY LOGIC (Dissolve & Hide Renderers) ---
            float targetDissolve = 0f;
            bool isHiddenByDistance = false;

            if (!IsLocalTank)
            {
                float distance = s_LocalTankTransform != null ? Vector3.Distance(transform.position, s_LocalTankTransform.position) : float.MaxValue;
                if (distance > m_VisionRadius)
                {
                    isHiddenByDistance = true; // Enemy is beyond 12m, hide completely!
                }
                else if (_inBush && !isSharedBush)
                {
                    targetDissolve = m_RemoteDissolve; // In bush, fully invisible
                }
            }
            else if (_inBush)
            {
                targetDissolve = m_LocalDissolve; // Local player in bush
            }

            // Smoothly interpolate dissolve (only applies when not hidden by distance)
            _currentDissolve = Mathf.Lerp(_currentDissolve, targetDissolve, Time.deltaTime * m_LerpSpeed);
            if (targetDissolve == 0f && _currentDissolve < 0.01f)
                _currentDissolve = 0f;

            // Apply visibility to all renderers
            foreach (var rend in _dissolveRenderers)
            {
                if (rend == null) continue;

                if (isHiddenByDistance)
                {
                    rend.enabled = false;
                }
                else
                {
                    rend.enabled = true;
                    rend.GetPropertyBlock(_mpb);
                    _mpb.SetFloat(DissolveID, _currentDissolve);
                    rend.SetPropertyBlock(_mpb);
                }
            }

            // Hide/show health bar UI for remote tanks
            if (!IsLocalTank && _worldCanvas != null)
            {
                if (isHiddenByDistance)
                {
                    _worldCanvas.enabled = false;
                }
                else
                {
                    // Hide when dissolve is nearly full (enemy is invisible in bush)
                    _worldCanvas.enabled = _currentDissolve < 0.9f;
                }
            }
        }

        /// <summary>
        /// Force reset dissolve to 0 (e.g. on death/respawn).
        /// </summary>
        public void ResetDissolve()
        {
            _currentDissolve = 0f;
            _inBush = false;
            ServerInBush = false;
            ServerBushRegion = 0;
            ServerRevealedOnMap = false;
            
            foreach (var rend in _dissolveRenderers)
            {
                if (rend != null)
                {
                    rend.enabled = true;
                    rend.GetPropertyBlock(_mpb);
                    _mpb.SetFloat(DissolveID, 0f);
                    rend.SetPropertyBlock(_mpb);
                }
            }

            if (_worldCanvas != null)
                _worldCanvas.enabled = true;
        }
    }
}
