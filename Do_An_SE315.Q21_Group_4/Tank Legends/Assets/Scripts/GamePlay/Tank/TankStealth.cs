using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace Complete
{
    /// <summary>
    /// Handles bush stealth mechanic for tanks.
    /// - Local tank: uses OnTriggerEnter/Exit to detect bush colliders (tag "Bush")
    ///   and sets _Dissolve = 0.5 (semi-transparent to the player).
    /// - Remote tank: reads InBush flag from server snapshot and sets _Dissolve = 1.0
    ///   (fully invisible to enemies).
    /// 
    /// Also hides the health UI (Canvas) of remote tanks when they are stealthed.
    /// Grace period prevents flickering when moving between adjacent bush colliders.
    /// </summary>
    public class TankStealth : MonoBehaviour
    {
        [Header("Dissolve Settings")]
        public float m_LocalDissolve = 0.5f;    // Value when local player is in bush
        public float m_RemoteDissolve = 1.0f;    // Value when enemy is in bush (invisible)
        public float m_LerpSpeed = 8f;           // Speed of dissolve transition
        public float m_GracePeriod = 0.2f;       // Delay before "exiting" bush (anti-flicker)

        /// <summary>
        /// Set by GameManager from server snapshot for remote tanks.
        /// </summary>
        [HideInInspector] public bool ServerInBush;
        [HideInInspector] public int ServerBushRegion;

        /// <summary>
        /// True if this is the local player's tank.
        /// </summary>
        [HideInInspector] public bool IsLocalTank;
        
        public static int s_LocalPlayerBushRegion;

        // ── Internal state ──────────────────────────────────────────────────
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

        /// <summary>
        /// Scans all child Renderers and caches those whose material has a _Dissolve property.
        /// Called once on spawn.
        /// </summary>
        private void CacheDissolveRenderers()
        {
            _dissolveRenderers.Clear();
            foreach (var rend in GetComponentsInChildren<Renderer>(true))
            {
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

            // Compute target dissolve value
            float targetDissolve = 0f;
            if (_inBush)
            {
                if (IsLocalTank || isSharedBush)
                    targetDissolve = m_LocalDissolve;
                else
                    targetDissolve = m_RemoteDissolve;
            }

            // Smoothly interpolate
            _currentDissolve = Mathf.Lerp(_currentDissolve, targetDissolve, Time.deltaTime * m_LerpSpeed);

            // Snap to zero when very close to avoid perpetual tiny values
            if (targetDissolve == 0f && _currentDissolve < 0.01f)
                _currentDissolve = 0f;

            // Apply to all dissolve-capable renderers
            ApplyDissolve(_currentDissolve);

            // Hide/show health bar UI for remote tanks when stealthed
            if (!IsLocalTank && _worldCanvas != null)
            {
                // Hide when dissolve is nearly full (enemy is invisible)
                _worldCanvas.enabled = _currentDissolve < 0.9f;
            }
        }

        private void ApplyDissolve(float value)
        {
            foreach (var rend in _dissolveRenderers)
            {
                if (rend == null) continue;
                rend.GetPropertyBlock(_mpb);
                _mpb.SetFloat(DissolveID, value);
                rend.SetPropertyBlock(_mpb);
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
            ApplyDissolve(0f);

            if (_worldCanvas != null)
                _worldCanvas.enabled = true;
        }
    }
}
