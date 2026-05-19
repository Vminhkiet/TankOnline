using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Script test tạm thời để click đăng nhập nhanh bằng tài khoản test (player1, player2...).
/// DỄ DÀNG XÓA FILE NÀY VÀ CÁC NÚT ĐĂNG NHẬP NHANH KHI GAME ĐÃ RELEASE.
/// </summary>
[RequireComponent(typeof(Button))]
public class QuickTestLoginButton : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private AuthenticationUIManager authManager;

    [Header("Credentials")]
    [SerializeField] private string testUsername = "player1";
    [SerializeField] private string testPassword = "123456";

    private Button cachedButton;

    private void Awake()
    {
        cachedButton = GetComponent<Button>();
        cachedButton.onClick.AddListener(OnQuickLoginClick);

        if (authManager == null)
            authManager = FindObjectOfType<AuthenticationUIManager>();
    }

    private void OnQuickLoginClick()
    {
        if (authManager != null)
        {
            Debug.Log($"[Quick Login] Đang đăng nhập nhanh bằng tài khoản: {testUsername}");
            authManager.QuickLogin(testUsername, testPassword);
        }
        else
        {
            Debug.LogError("[Quick Login] Không tìm thấy AuthenticationUIManager trong Scene để thực hiện đăng nhập!");
        }
    }

    private void OnDestroy()
    {
        if (cachedButton != null)
            cachedButton.onClick.RemoveListener(OnQuickLoginClick);
    }
}
