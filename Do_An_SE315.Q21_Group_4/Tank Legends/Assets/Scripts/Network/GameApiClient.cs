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
    public const string DefaultBaseUrl = "http://localhost:8080";
    public const string JwtKey = "jwt";
    public const string RefreshTokenKey = "refreshToken";
    public const string UsernameKey = "username";
    public const string EmailKey = "email";
    public const string BaseUrlKey = "api_base_url";

    public static string BaseUrl
    {
        get
        {
            string saved = PlayerPrefs.GetString(BaseUrlKey, DefaultBaseUrl);
            return string.IsNullOrWhiteSpace(saved) ? DefaultBaseUrl : saved.Trim().TrimEnd('/');
        }
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                PlayerPrefs.DeleteKey(BaseUrlKey);
            else
                PlayerPrefs.SetString(BaseUrlKey, value.Trim().TrimEnd('/'));
        }
    }

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
