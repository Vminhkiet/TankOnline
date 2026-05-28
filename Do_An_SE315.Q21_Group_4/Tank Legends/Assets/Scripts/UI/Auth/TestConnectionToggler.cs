using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Script test tạm thời để toggle cổng kết nối (Localhost <-> LAN IPv4) trên màn hình đăng nhập.
/// DỄ DÀNG XÓA FILE NÀY VÀ NÚT BẤM KHI GAME ĐÃ RELEASE.
/// </summary>
[RequireComponent(typeof(Button))]
public class TestConnectionToggler : MonoBehaviour
{
    [Header("UI Binding")]
    [SerializeField] private TMP_Text buttonLabelText;
    [SerializeField] private string prefixLabel = "Server: ";
    [SerializeField] private TMP_InputField manualIpInput; // Người test chuẩn bị sẵn

    private Button cachedButton;

    private void Awake()
    {
        cachedButton = GetComponent<Button>();
        cachedButton.onClick.AddListener(ToggleConnection);

        if (buttonLabelText == null)
            buttonLabelText = GetComponentInChildren<TMP_Text>();

        if (manualIpInput != null)
        {
            manualIpInput.text = GameApiClient.LanManualIp;
            manualIpInput.onValueChanged.AddListener(OnManualIpChanged);
        }
    }

    private void OnManualIpChanged(string newIp)
    {
        GameApiClient.LanManualIp = newIp;
        UpdateUI();
    }

    private bool isSearching = false;

    private void Start()
    {
        UpdateUI();
    }

    private void Update()
    {
        if (isSearching && !ServerDiscovery.IsSearching)
        {
            isSearching = false;
            UpdateUI();
            Debug.Log($"[Test Mode] Auto-discovery finished. Cổng kết nối hiện tại: {GameApiClient.BaseUrl}");
        }
    }

    private void ToggleConnection()
    {
        if (isSearching) return; // Đang tìm thì không cho ấn liên tục

        // Đọc mode hiện tại
        string currentMode = PlayerPrefs.GetString(GameApiClient.ConnectionModePrefKey, "lan_auto");
        
        // Đổi chế độ: lan_auto -> localhost -> lan_manual -> lan_auto
        string newMode;
        if (currentMode == "lan_auto")
            newMode = "localhost";
        else if (currentMode == "localhost")
            newMode = "lan_manual";
        else
            newMode = "lan_auto";
        
        // Lưu lại cấu hình mới
        PlayerPrefs.SetString(GameApiClient.ConnectionModePrefKey, newMode);
        PlayerPrefs.Save();

        if (newMode == "lan_auto")
        {
            isSearching = true;
            ServerDiscovery.DiscoverAsync();
        }

        // Cập nhật giao diện
        UpdateUI();

        // Ghi log để tester biết
        Debug.Log($"[Test Mode] Đã đổi mode thành: {newMode.ToUpper()}");
    }

    private void UpdateUI()
    {
        if (buttonLabelText == null)
            return;

        string currentMode = PlayerPrefs.GetString(GameApiClient.ConnectionModePrefKey, "lan_auto");
        
        if (manualIpInput != null)
        {
            manualIpInput.gameObject.SetActive(currentMode == "lan_manual");
        }

        if (isSearching)
        {
            buttonLabelText.text = $"{prefixLabel}TÌM SERVER...";
            buttonLabelText.color = Color.yellow;
            return;
        }

        string displayModeName = "";
        string currentUrl = GameApiClient.BaseUrl;
        
        if (currentMode == "lan_auto")
        {
            displayModeName = "LAN (AUTO)";
            if (currentUrl == "http://not-found")
            {
                currentUrl = "Không tìm thấy";
                buttonLabelText.color = Color.red;
            }
            else
            {
                buttonLabelText.color = Color.green;
            }
        }
        else if (currentMode == "lan_manual")
        {
            displayModeName = "LAN (MANUAL)";
            buttonLabelText.color = new Color(1f, 0.5f, 0f); // Màu cam
        }
        else
        {
            displayModeName = "LOCALHOST";
            buttonLabelText.color = Color.cyan;
        }
        
        buttonLabelText.text = $"{prefixLabel}{displayModeName}\n<size=70%>{currentUrl}</size>";
    }

    private void OnDestroy()
    {
        if (cachedButton != null)
            cachedButton.onClick.RemoveListener(ToggleConnection);
    }
}
