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

        /// <summary>
        /// True if this is the local player's tank.
        /// </summary>
        [HideInInspector] public bool IsLocalTank;

        // ── Internal state ──────────────────────────────────────────────────
        private int _bushOverlapCount;            // Number of bush triggers currently overlapping
        private float _lastBushExitTime = -999f;  // Time when last bush trigger was exited
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
            Debug.Log($"[TankStealth] {gameObject.name}: found {_dissolveRenderers.Count} dissolve renderers, canvas={_worldCanvas != null}");
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

        // ── Trigger-based bush detection (works for local tank) ──────────

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Bush")) return;
            _bushOverlapCount++;
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag("Bush")) return;
            _bushOverlapCount = Mathf.Max(0, _bushOverlapCount - 1);
            if (_bushOverlapCount == 0)
                _lastBushExitTime = Time.time;
        }

        private void Update()
        {
            // Determine "in bush" state
            if (IsLocalTank)
            {
                // Local tank: use trigger overlap count + grace period
                if (_bushOverlapCount > 0)
                {
                    _inBush = true;
                }
                else if (_inBush && Time.time - _lastBushExitTime < m_GracePeriod)
                {
                    // Still within grace period — stay "in bush"
                }
                else
                {
                    _inBush = false;
                }
            }
            else
            {
                // Remote tank: trust the server flag
                _inBush = ServerInBush;
            }

            // Compute target dissolve value
            float targetDissolve = 0f;
            if (_inBush)
                targetDissolve = IsLocalTank ? m_LocalDissolve : m_RemoteDissolve;

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
            _bushOverlapCount = 0;
            _inBush = false;
            ApplyDissolve(0f);

            if (_worldCanvas != null)
                _worldCanvas.enabled = true;
        }
    }
}
