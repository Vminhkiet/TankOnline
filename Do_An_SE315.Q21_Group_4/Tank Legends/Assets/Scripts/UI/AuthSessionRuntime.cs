using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class AuthSessionRuntime : MonoBehaviour
{
    public static AuthSessionRuntime Instance { get; private set; }

    [Header("UI Runtime")]
    public GameObject warningPanel;
    public TextMeshProUGUI warningText;

    [Header("Auth Settings")]
    public string logoutApiUrl = "http://localhost:8080/api/auth/logout";
    public string loginSceneName = "Authentication";
    public int defaultCountdownSeconds = 10;

    private bool _isHandlingForceLogout;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnApplicationQuit()
    {
        AuthenticationUIManager.LogoutSilently(this, logoutApiUrl);
        AuthenticationUIManager.ClearLocalAuth();
    }

    public void HandleForceLogout(int code, string message, int countdownSec = -1)
    {
        if (_isHandlingForceLogout) return;
        int seconds = countdownSec > 0 ? countdownSec : defaultCountdownSeconds;
        StartCoroutine(ForceLogoutFlow(code, message, seconds));
    }

    public void TriggerDuplicateLoginKickTest()
    {
        HandleForceLogout(1003, "Tài khoản đã đăng nhập ở nơi khác", defaultCountdownSeconds);
    }

    private IEnumerator ForceLogoutFlow(int code, string message, int countdownSeconds)
    {
        _isHandlingForceLogout = true;

        if (warningPanel != null)
            warningPanel.SetActive(true);

        for (int remain = countdownSeconds; remain > 0; remain--)
        {
            if (warningText != null)
                warningText.text = $"{message}\n(Mã: {code})\nThoát sau {remain}s...";
            yield return new WaitForSeconds(1f);
        }

        AuthenticationUIManager.LogoutSilently(this, logoutApiUrl);
        AuthenticationUIManager.ClearLocalAuth();

        yield return new WaitForSeconds(0.2f);

        if (!string.IsNullOrEmpty(loginSceneName))
            SceneManager.LoadScene(loginSceneName);

        _isHandlingForceLogout = false;
    }
}
