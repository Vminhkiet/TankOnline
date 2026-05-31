using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Shared HTTP client for API Gateway (JWT + base URL + JSON).
/// UI managers should only bind data — not duplicate UnityWebRequest setup.
/// </summary>
public static class GameApiClient
{
    #region TEST_CONNECTION_MODE (Dễ dàng xóa region này khi release game)
    // Cấu hình các cổng kết nối test
    private const string LocalhostUrl = "http://localhost:8080";
    public static string LanManualIp
    {
        get => PlayerPrefs.GetString("manual_ipv4_ip", "192.168.100.31");
        set { PlayerPrefs.SetString("manual_ipv4_ip", value); PlayerPrefs.Save(); }
    }
    public static string LanManualUrl => $"http://{LanManualIp}:8080";
    public const string ConnectionModePrefKey = "test_connection_mode"; // "localhost", "lan_manual", hoặc "lan_auto"

    private static string _cachedAutoUrl;

    public static string BaseUrl
    {
        get
        {
            // Đọc mode từ PlayerPrefs, nếu chưa set thì mặc định là lan_auto
            string mode = PlayerPrefs.GetString(ConnectionModePrefKey, "lan_auto");

            if (mode == "lan_auto")
            {
                // Dùng IP đã được ServerDiscovery tìm thấy
                if (!string.IsNullOrEmpty(ServerDiscovery.DiscoveredIp))
                {
                    _cachedAutoUrl = $"http://{ServerDiscovery.DiscoveredIp}:{ServerDiscovery.DiscoveredPort}";
                    return _cachedAutoUrl;
                }

                // Nếu chưa tìm thấy nhưng đã có cache từ lần trước
                if (!string.IsNullOrEmpty(_cachedAutoUrl))
                    return _cachedAutoUrl;

                // Fallback: UDP discovery chưa xong → dùng IP thủ công
                return LanManualUrl;
            }

            if (mode == "lan_manual")
                return LanManualUrl;

            return LocalhostUrl;
        }
    }
    #endregion

    public const string JwtKey          = "jwt";
    public const string RefreshTokenKey = "refreshToken";
    public const string UsernameKey     = "username";
    public const string EmailKey        = "email";

    private static string _jwt;
    private static string _refreshToken;

    public static string GetJwt() 
    {
        if (_jwt == null) _jwt = PlayerPrefs.GetString(JwtKey, "");
        return _jwt;
    }

    public static void SetJwt(string token)
    {
        _jwt = token;
        PlayerPrefs.SetString(JwtKey, token);
    }

    public static string GetRefreshToken()
    {
        if (_refreshToken == null) _refreshToken = PlayerPrefs.GetString(RefreshTokenKey, "");
        return _refreshToken;
    }

    public static void SetRefreshToken(string token)
    {
        _refreshToken = token;
        PlayerPrefs.SetString(RefreshTokenKey, token);
    }

    public static void ClearMemoryCache()
    {
        _jwt = null;
        _refreshToken = null;
    }

    public static bool HasJwt() => !string.IsNullOrEmpty(GetJwt());

    public static string BuildUrl(string pathOrFullUrl)
    {
        if (string.IsNullOrWhiteSpace(pathOrFullUrl))
            return BaseUrl;

        string trimmed = pathOrFullUrl.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        string path = trimmed.StartsWith("/") ? trimmed : "/" + trimmed;
        return BaseUrl + path;
    }

    public static UnityWebRequest CreateRequest(
        string pathOrFullUrl,
        string method,
        string jsonBody = null,
        string jwtOverride = null)
    {
        string jwt = jwtOverride ?? GetJwt();

        var req = new UnityWebRequest(BuildUrl(pathOrFullUrl), method)
        {
            downloadHandler = new DownloadHandlerBuffer()
        };

        req.SetRequestHeader("Accept", "application/json");
        if (!string.IsNullOrEmpty(jwt))
            req.SetRequestHeader("Authorization", "Bearer " + jwt);

        if (!string.IsNullOrEmpty(jsonBody))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
            req.SetRequestHeader("Content-Type", "application/json");
        }

        return req;
    }

    public static IEnumerator Send(UnityWebRequest request, Action<ApiCallResult> onComplete)
    {
        yield return request.SendWebRequest();

        bool success = request.result == UnityWebRequest.Result.Success;
        onComplete?.Invoke(new ApiCallResult
        {
            Success = success,
            StatusCode = (long)request.responseCode,
            Body = request.downloadHandler?.text ?? string.Empty,
            Error = success ? null : request.error
        });
    }

    public struct ApiCallResult
    {
        public bool Success;
        public long StatusCode;
        public string Body;
        public string Error;

        public string ErrorMessage =>
            Success ? null : $"HTTP {StatusCode}: {Error}\n{Body}";
    }
}
