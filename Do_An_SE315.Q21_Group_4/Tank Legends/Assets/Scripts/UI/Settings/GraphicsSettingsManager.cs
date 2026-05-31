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

        private void Start()
        {
            if (qualityDropdown != null)
            {
                qualityDropdown.onValueChanged.AddListener(OnQualityChanged);
                
                // Lấy index đã lưu (mặc định là 0 - Cao)
                int savedIndex = PlayerPrefs.GetInt("QualityIndex", 0);
                qualityDropdown.value = savedIndex;
                OnQualityChanged(savedIndex);
            }
            else
            {
                Debug.LogWarning("[GraphicsSettingsManager] Chưa gắn TMP_Dropdown!");
            }
        }

        public void OnQualityChanged(int index)
        {
            // Index 0: Cao (Scale 1.0, Bật Shadows, HDR, MSAA, PostProcessing)
            // Index 1: Trung bình (Scale 0.5, Bật Shadows, HDR, MSAA, PostProcessing)
            // Index 2: Thấp (Scale 0.5, Tắt Shadows, HDR, MSAA, Bloom, SSAO)
            
            float scale = 1.0f;
            bool highQuality = true;
            
            switch(index)
            {
                case 0: 
                    scale = 1.0f; 
                    highQuality = true;
                    break;
                case 1: 
                    scale = 0.5f; 
                    highQuality = true;
                    break;
                case 2: 
                    scale = 0.5f; 
                    highQuality = false;
                    break;
                default: 
                    scale = 1.0f; 
                    highQuality = true;
                    break;
            }
            
            ApplyGraphicsSettings(scale, highQuality);
            
            // Lưu lại cấu hình
            PlayerPrefs.SetInt("QualityIndex", index);
            PlayerPrefs.Save();
            
            Debug.Log($"[GraphicsSettings] Đã đổi chất lượng: Index {index}. Scale: {scale}, HighQualityFeatures: {highQuality}");
        }

        private void ApplyGraphicsSettings(float scale, bool highQualityFeatures)
        {
            UniversalRenderPipelineAsset urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            
            if (urpAsset != null)
            {
                urpAsset.renderScale = scale;
                
                // Tắt/Bật HDR & MSAA
                urpAsset.supportsHDR = highQualityFeatures;
                urpAsset.msaaSampleCount = highQualityFeatures ? 4 : 1; // 1 = Tắt MSAA
            }
            
            // Tắt/Bật Shadows hệ thống
            QualitySettings.shadows = highQualityFeatures ? UnityEngine.ShadowQuality.All : UnityEngine.ShadowQuality.Disable;

            // Tìm và tắt Bloom / SSAO trong Volume
            Volume[] volumes = FindObjectsOfType<Volume>();
            foreach (var vol in volumes)
            {
                if (vol.profile != null)
                {
                    // Tắt/Bật Bloom
                    if (vol.profile.TryGet(out Bloom bloom))
                    {
                        bloom.active = highQualityFeatures;
                    }
                    
                    // Thử tìm SSAO nếu nó được add như một Post-Processing effect
                    // (Lưu ý: Nếu SSAO nằm ở Renderer Feature, cách tắt tốt nhất là dùng nhiều URP Asset khác nhau)
                    // Hoặc bạn có thể Disable PostProcessing trên Camera nếu là mức Yếu
                }
            }
            
            // Vô hiệu hóa Post Processing trên Main Camera nếu ở mức Yếu
            if (Camera.main != null)
            {
                var cameraData = Camera.main.GetComponent<UniversalAdditionalCameraData>();
                if (cameraData != null)
                {
                    cameraData.renderPostProcessing = highQualityFeatures;
                }
            }
        }


    }
}
