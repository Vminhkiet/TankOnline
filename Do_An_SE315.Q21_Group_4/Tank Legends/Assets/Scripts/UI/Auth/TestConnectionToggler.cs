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

    private Button cachedButton;

    private void Awake()
    {
        cachedButton = GetComponent<Button>();
        cachedButton.onClick.AddListener(ToggleConnection);

        if (buttonLabelText == null)
            buttonLabelText = GetComponentInChildren<TMP_Text>();
    }

    private void Start()
    {
        UpdateUI();
    }

    private void ToggleConnection()
    {
        // Đọc mode hiện tại
        string currentMode = PlayerPrefs.GetString(GameApiClient.ConnectionModePrefKey, "localhost");
        
        // Đổi chế độ
        string newMode = (currentMode == "lan") ? "localhost" : "lan";
        
        // Lưu lại cấu hình mới
        PlayerPrefs.SetString(GameApiClient.ConnectionModePrefKey, newMode);
        PlayerPrefs.Save();

        // Cập nhật giao diện
        UpdateUI();

        // Ghi log để tester biết
        string currentUrl = GameApiClient.BaseUrl;
        Debug.Log($"[Test Mode] Đã đổi cổng kết nối thành: {newMode.ToUpper()} ({currentUrl})");
    }

    private void UpdateUI()
    {
        if (buttonLabelText == null)
            return;

        string currentMode = PlayerPrefs.GetString(GameApiClient.ConnectionModePrefKey, "localhost");
        string displayModeName = (currentMode == "lan") ? "LAN" : "LOCALHOST";
        string currentUrl = GameApiClient.BaseUrl;
        
        buttonLabelText.text = $"{prefixLabel}{displayModeName}\n({currentUrl})";
    }

    private void OnDestroy()
    {
        if (cachedButton != null)
            cachedButton.onClick.RemoveListener(ToggleConnection);
    }
}
