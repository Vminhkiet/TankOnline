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
    private const string LanUrl = "http://192.168.137.86:8080";
    public const string ConnectionModePrefKey = "test_connection_mode"; // "localhost" hoặc "lan"

    public static string BaseUrl
    {
        get
        {
            // Đọc mode từ PlayerPrefs, nếu chưa set thì mặc định là localhost
            string mode = PlayerPrefs.GetString(ConnectionModePrefKey, "localhost");
            if (mode == "lan")
                return LanUrl;
            return LocalhostUrl;
        }
    }
    #endregion

    public const string JwtKey          = "jwt";
    public const string RefreshTokenKey = "refreshToken";
    public const string UsernameKey     = "username";
    public const string EmailKey        = "email";

    public static string GetJwt() => PlayerPrefs.GetString(JwtKey, "");

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
