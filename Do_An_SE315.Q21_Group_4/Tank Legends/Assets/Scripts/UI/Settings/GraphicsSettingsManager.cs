using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;
using TMPro;

namespace TankLegends.UI.Settings
{
    public class GraphicsSettingsManager : MonoBehaviour
    {
        [Header("UI References")]
        [Tooltip("Gắn TMP_Dropdown vào đây. Các tùy chọn nên là: 0 - Cao, 1 - Trung bình, 2 - Thấp")]
        public TMP_Dropdown qualityDropdown;

        [Header("URP Profiles")]
        [Tooltip("Kéo Assets/Profiles/Low.asset vào đây")]
        [SerializeField] private UniversalRenderPipelineAsset m_LowURPProfile;

        private UniversalRenderPipelineAsset m_DefaultURPAsset;

        private static GraphicsSettingsManager s_Instance;

        private void Awake()
        {
            if (s_Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            s_Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            m_DefaultURPAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

            if (qualityDropdown != null)
            {
                qualityDropdown.onValueChanged.AddListener(OnQualityChanged);

                int defaultIndex = GetDefaultQualityIndex();
                int savedIndex = PlayerPrefs.GetInt("QualityIndex", defaultIndex);
                qualityDropdown.value = savedIndex;
                OnQualityChanged(savedIndex);
            }
            else
            {
                Debug.LogWarning("[GraphicsSettingsManager] Chưa gắn TMP_Dropdown!");
            }
        }

        // RAM là cổng lọc đầu tiên — Exynos 850 / Helio G80 có 8 nhân nhưng GPU rất yếu.
        // cpuCount không dùng vì chip giá rẻ đã phổ cập 8 nhân, gây "dương tính giả".
        // GPU score chỉ tính khi RAM đủ; nghi ngờ → Thấp (thà mượt hơn là lag).
        private static int GetDefaultQualityIndex()
        {
            int  ramMB       = SystemInfo.systemMemorySize;
            int  shaderLevel = SystemInfo.graphicsShaderLevel;
            int  vramMB      = SystemInfo.graphicsMemorySize;
            bool supportsCS  = SystemInfo.supportsComputeShaders;

            // Bước 1 — RAM gate: < 6 GB → Thấp ngay, không cần hỏi thêm
            // Bắt A13 (4 GB), A03, Redmi A-series, v.v.
            if (ramMB < 6000) return 2;

            // Bước 2 — GPU score (chỉ chạy khi RAM >= 6 GB)
            int gpu = 0;
            if (shaderLevel >= 50) gpu += 2; // Vulkan / Metal compute-capable
            else if (shaderLevel >= 45) gpu += 1; // OpenGL ES 3.1+
            if (vramMB >= 2048) gpu++;           // shared VRAM đủ lớn
            if (supportsCS)     gpu++;           // compute shader = GPU thế hệ mới

            // Bước 3 — Phân loại; nghi ngờ → Thấp
            if (gpu < 2) return 2; // RAM ổn nhưng GPU yếu → Thấp
            if (gpu < 4) return 1; // Trung bình
            return 0;               // Cao: RAM lớn + GPU đủ mạnh
        }

        public void OnQualityChanged(int index)
        {
            switch (index)
            {
                case 0: ApplyGraphicsSettings(1.0f, true);  break; // Cao
                case 1: ApplyGraphicsSettings(0.5f, true);  break; // Trung bình
                case 2: ApplyGraphicsSettings(0.5f, false); break; // Thấp
                default: ApplyGraphicsSettings(1.0f, true); break;
            }

            PlayerPrefs.SetInt("QualityIndex", index);
            PlayerPrefs.Save();
            Debug.Log($"[GraphicsSettings] Quality index {index} — URP active: {GraphicsSettings.currentRenderPipeline?.name ?? "null"} | LowProfile: {(m_LowURPProfile != null ? m_LowURPProfile.name : "NOT ASSIGNED")}");
        }

        private void ApplyGraphicsSettings(float scale, bool highQuality)
        {
            // VSync & frame rate
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;

            // URP Asset swap
            // Thấp → dùng Low.asset (LDR đã cấu hình sẵn: renderScale 0.5, MSAA off, HDR off)
            //         LDR tước môi trường HDR khiến Bloom "chết đói" — không cần tắt thủ công
            // Cao/Trung → khôi phục default asset, chỉnh tay renderScale và HDR
            if (!highQuality && m_LowURPProfile != null)
            {
                QualitySettings.renderPipeline = m_LowURPProfile;
            }
            else
            {
                QualitySettings.renderPipeline = m_DefaultURPAsset;
                var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                if (urp != null)
                {
                    urp.renderScale     = scale;
                    urp.supportsHDR     = highQuality;
                    urp.msaaSampleCount = highQuality ? 4 : 1;
                }
            }

            // Shadows
            QualitySettings.shadows = highQuality ? UnityEngine.ShadowQuality.All : UnityEngine.ShadowQuality.Disable;

            // Physics — giảm solver iterations khi Thấp (16ms → ~8ms)
            Physics.defaultSolverIterations         = highQuality ? 6 : 2;
            Physics.defaultSolverVelocityIterations = 1;

            // Global Volumes: disable hoàn toàn khi Thấp để cắt toàn bộ Post-Processing loop
            foreach (Volume vol in FindObjectsOfType<Volume>())
            {
                if (vol != null && vol.isGlobal)
                    vol.enabled = highQuality;
            }

            // Bloom trong profile (dự phòng khi Volume vẫn còn active)
            SetBloomEnabled(highQuality);

            // Khoá kép Post-Processing: LDR (từ Low.asset) + tắt cờ camera
            // URP mới không có nút tổng tắt PP trong Asset nên cần cả hai lớp
            if (Camera.main != null)
            {
                var camData = Camera.main.GetComponent<UniversalAdditionalCameraData>();
                if (camData != null)
                    camData.renderPostProcessing = highQuality;
            }

            // Physics throttle: giảm BoxCast + Raycast từ 50Hz → 25Hz khi Thấp
            Complete.TankMovement.SetLowEndPhysicsMode(!highQuality);

            // GPU Instancing cho Thấp: gộp draw call
            if (!highQuality)
                ApplyGPUInstancing();
        }

        private void SetBloomEnabled(bool enabled)
        {
            foreach (var vol in FindObjectsOfType<Volume>())
            {
                if (vol == null) continue;
                VolumeProfile profile = vol.profile;
                if (profile == null) continue;
                if (profile.TryGet<Bloom>(out var bloom))
                    bloom.active = enabled;
            }
        }

        private void ApplyGPUInstancing()
        {
            int count = 0;
            foreach (var r in FindObjectsOfType<Renderer>())
            {
                foreach (var mat in r.sharedMaterials)
                {
                    if (mat != null && !mat.enableInstancing)
                    {
                        mat.enableInstancing = true;
                        count++;
                    }
                }
            }
            Debug.Log($"[GraphicsSettings] GPU Instancing bật cho {count} material.");
        }
    }
}
