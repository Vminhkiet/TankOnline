using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

public class AuthSessionRuntime : MonoBehaviour
{
    public static AuthSessionRuntime Instance { get; private set; }

    [Header("UI Runtime")]
    public GameObject warningPanel;
    public TextMeshProUGUI warningText;

    [Header("Auth Settings")]
    public string logoutApiPath = "/api/auth/logout";
    public string loginSceneName = "Authentication";
    public int defaultCountdownSeconds = 10;

    [Header("Session Polling")]
    public string refreshApiPath = "/api/auth/refresh";
    public float sessionPollIntervalSec = 30f;

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
        StartCoroutine(SessionPollLoop());
    }

    private IEnumerator SessionPollLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(sessionPollIntervalSec);

            if (_isHandlingForceLogout) continue;

            string jwt          = PlayerPrefs.GetString(GameApiClient.JwtKey, "");
            string refreshToken = PlayerPrefs.GetString(GameApiClient.RefreshTokenKey, "");
            if (string.IsNullOrEmpty(jwt) || string.IsNullOrEmpty(refreshToken)) continue;

            string body = $"{{\"refreshToken\":\"{refreshToken}\"}}";
            using (var req = GameApiClient.CreateRequest(refreshApiPath, "POST", body))
            {
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    // Cập nhật token mới để JWT không bao giờ hết hạn trong lúc chơi
                    string newJwt     = ExtractJsonField(req.downloadHandler.text, "jwt");
                    string newRefresh = ExtractJsonField(req.downloadHandler.text, "refreshToken");
                    if (!string.IsNullOrEmpty(newJwt))
                        PlayerPrefs.SetString(GameApiClient.JwtKey, newJwt);
                    if (!string.IsNullOrEmpty(newRefresh))
                        PlayerPrefs.SetString(GameApiClient.RefreshTokenKey, newRefresh);
                }
                else if (req.result != UnityWebRequest.Result.ConnectionError &&
                         req.result != UnityWebRequest.Result.DataProcessingError)
                {
                    // Server phản hồi lỗi (401/403/500) → session bị thu hồi do đăng nhập thiết bị khác
                    HandleForceLogout(1003, "Tài khoản đã đăng nhập ở nơi khác");
                }
            }
        }
    }

    private static string ExtractJsonField(string json, string key)
    {
        string search = $"\"{key}\":\"";
        int start = json.IndexOf(search, System.StringComparison.Ordinal);
        if (start < 0) return null;
        start += search.Length;
        int end = json.IndexOf('"', start);
        return end < 0 ? null : json.Substring(start, end - start);
    }

    private void OnApplicationQuit()
    {
        AuthenticationUIManager.LogoutSilently(this, GameApiClient.BuildUrl(logoutApiPath));
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

        AuthenticationUIManager.LogoutSilently(this, GameApiClient.BuildUrl(logoutApiPath));
        AuthenticationUIManager.ClearLocalAuth();

        yield return new WaitForSeconds(0.2f);

        if (!string.IsNullOrEmpty(loginSceneName))
            SceneManager.LoadScene(loginSceneName);

        _isHandlingForceLogout = false;
    }
}
