using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;
using System.Text;

// ── DTOs khớp với Auth Service ────────────────────────────────────────────────

[System.Serializable]
public class LoginRequestData
{
    public string username;
    public string password;
}

[System.Serializable]
public class SignUpRequestData
{
    public string username;
    public string password;
    public string email;
}

[System.Serializable]
public class AuthResponseData
{
    public string jwt;
    public string refreshToken;
}

// ─────────────────────────────────────────────────────────────────────────────

public class AuthenticationUIManager : MonoBehaviour
{
    [Header("UI Panels")]
    public GameObject loginPanel;
    public GameObject signupPanel;

    [Header("Login Fields")]
    public TMP_InputField usernameInput;
    public TMP_InputField passwordInput;
    public TextMeshProUGUI errorText;

    [Header("Sign Up Fields")]
    public TMP_InputField signupUsernameInput;
    public TMP_InputField signupEmailInput;
    public TMP_InputField signupPasswordInput;
    public TMP_InputField signupConfirmPasswordInput;
    public TextMeshProUGUI signupErrorText;

    [Header("API Settings")]
    public string loginApiUrl  = "http://localhost:8080/api/auth/login";
    public string signupApiUrl = "http://localhost:8080/api/auth/signup";
    public string logoutApiUrl = "http://localhost:8080/api/auth/logout";

    [Header("Scene Settings")]
    public string lobbySceneName = "Lobby";
    public string loginSceneName = "Authentication";

    private bool isBusy = false;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Start()
    {
        ShowLoginPanel();
        ClearErrors();
    }

    // ── Panel switching ───────────────────────────────────────────────────────

    public void ShowLoginPanel()
    {
        if (loginPanel  != null) loginPanel.SetActive(true);
        if (signupPanel != null) signupPanel.SetActive(false);
        ClearErrors();
    }

    public void ShowSignupPanel()
    {
        if (loginPanel  != null) loginPanel.SetActive(false);
        if (signupPanel != null) signupPanel.SetActive(true);
        ClearErrors();
    }

    // ── LOGIN ─────────────────────────────────────────────────────────────────

    public void OnLoginButtonClicked()
    {
        if (isBusy) return;
        if (string.IsNullOrEmpty(lobbySceneName))
        {
            ShowLoginError("Lobby scene chưa được cấu hình.");
            return;
        }
        StartCoroutine(LoginCoroutine());
    }

    private IEnumerator LoginCoroutine()
    {
        if (usernameInput == null || passwordInput == null)
        {
            ShowLoginError("Chưa gán Input Fields trong Inspector!");
            yield break;
        }

        string username = usernameInput.text.Trim();
        string password = passwordInput.text;

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            ShowLoginError("Vui lòng nhập đầy đủ tài khoản và mật khẩu.");
            yield break;
        }

        isBusy = true;
        ShowLoginError("Đang kết nối...");

        string json = JsonUtility.ToJson(new LoginRequestData { username = username, password = password });

        using (UnityWebRequest req = new UnityWebRequest(loginApiUrl, "POST"))
        {
            req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            // yield KHÔNG được nằm trong try/catch — đặt ngoài
            yield return req.SendWebRequest();

            bool loginOk = false;

            if (req.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    AuthResponseData res = JsonUtility.FromJson<AuthResponseData>(req.downloadHandler.text);
                    if (!string.IsNullOrEmpty(res.jwt))
                    {
                        PlayerPrefs.SetString("jwt",          res.jwt);
                        PlayerPrefs.SetString("refreshToken", res.refreshToken);
                        PlayerPrefs.SetString("username",     username);
                        PlayerPrefs.Save();
                        loginOk = true;
                    }
                    ShowLoginError("Đăng nhập thành công!");
                }
                catch (System.Exception e)
                {
                    ShowLoginError("Lỗi parse response: " + e.Message);
                    isBusy = false;
                }
            }
            else
            {
                if (req.responseCode == 400 || req.responseCode == 401 || req.responseCode == 403)
                    ShowLoginError("Sai tài khoản hoặc mật khẩu.");
                else
                    ShowLoginError("Lỗi kết nối: " + req.error);
                isBusy = false;
            }

            // yield nằm ngoài try/catch
            if (loginOk)
            {
                yield return new WaitForSeconds(0.5f);
                SceneManager.LoadScene(lobbySceneName);
            }
        }
    }

    // ── SIGN UP ───────────────────────────────────────────────────────────────

    public void OnSignupButtonClicked()
    {
        if (isBusy) return;
        StartCoroutine(SignupCoroutine());
    }

    private IEnumerator SignupCoroutine()
    {
        if (signupUsernameInput == null || signupPasswordInput == null || signupEmailInput == null)
        {
            ShowSignupError("Chưa gán Signup Input Fields trong Inspector!");
            yield break;
        }

        string username = signupUsernameInput.text.Trim();
        string email    = signupEmailInput.text.Trim();
        string password = signupPasswordInput.text;
        string confirm  = signupConfirmPasswordInput != null ? signupConfirmPasswordInput.text : password;

        if (string.IsNullOrEmpty(username))
        { ShowSignupError("Tên đăng nhập không được để trống."); yield break; }

        if (string.IsNullOrEmpty(email) || !email.Contains("@"))
        { ShowSignupError("Email không hợp lệ."); yield break; }

        if (string.IsNullOrEmpty(password) || password.Length < 6)
        { ShowSignupError("Mật khẩu phải có ít nhất 6 ký tự."); yield break; }

        if (password != confirm)
        { ShowSignupError("Mật khẩu xác nhận không khớp."); yield break; }

        isBusy = true;
        ShowSignupError("Đang đăng ký...");

        string json = JsonUtility.ToJson(new SignUpRequestData
        {
            username = username,
            email    = email,
            password = password
        });

        using (UnityWebRequest req = new UnityWebRequest(signupApiUrl, "POST"))
        {
            req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

            bool goLobby    = false;
            bool goLogin    = false;

            if (req.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    AuthResponseData res = JsonUtility.FromJson<AuthResponseData>(req.downloadHandler.text);
                    if (!string.IsNullOrEmpty(res.jwt))
                    {
                        PlayerPrefs.SetString("jwt",          res.jwt);
                        PlayerPrefs.SetString("refreshToken", res.refreshToken);
                        PlayerPrefs.SetString("username",     username);
                        PlayerPrefs.Save();
                        ShowSignupError("Đăng ký thành công!");
                        goLobby = true;
                    }
                    else
                    {
                        ShowSignupError("Đăng ký thành công! Vui lòng đăng nhập.");
                        goLogin = true;
                    }
                }
                catch (System.Exception e)
                {
                    ShowSignupError("Lỗi parse response: " + e.Message);
                    isBusy = false;
                }
            }
            else
            {
                if (req.responseCode == 409)
                    ShowSignupError("Tên đăng nhập hoặc email đã tồn tại.");
                else if (req.responseCode == 400)
                    ShowSignupError("Thông tin đăng ký không hợp lệ.");
                else
                    ShowSignupError("Lỗi kết nối: " + req.error);
                isBusy = false;
            }

            // yield nằm ngoài try/catch
            if (goLobby)
            {
                yield return new WaitForSeconds(0.5f);
                SceneManager.LoadScene(lobbySceneName);
            }
            else if (goLogin)
            {
                yield return new WaitForSeconds(1f);
                ShowLoginPanel();
                isBusy = false;
            }
        }
    }

    // ── LOGOUT ────────────────────────────────────────────────────────────────

    public void OnLogoutButtonClicked()
    {
        if (isBusy) return;
        StartCoroutine(LogoutCoroutine());
    }

    private IEnumerator LogoutCoroutine()
    {
        isBusy = true;

        yield return LogoutRequestCoroutine(logoutApiUrl);

        ClearLocalAuth();

        isBusy = false;

        if (!string.IsNullOrEmpty(loginSceneName))
            SceneManager.LoadScene(loginSceneName);
        else
            ShowLoginPanel();
    }

    // ── Static helper — gọi từ Lobby / GameManager ────────────────────────────

    public static void Logout(MonoBehaviour caller, string logoutUrl, string loginScene)
    {
        caller.StartCoroutine(StaticLogoutCoroutine(logoutUrl, loginScene));
    }

    public static void LogoutSilently(MonoBehaviour caller, string logoutUrl)
    {
        caller.StartCoroutine(LogoutRequestCoroutine(logoutUrl));
    }

    private static IEnumerator StaticLogoutCoroutine(string logoutUrl, string loginScene)
    {
        yield return LogoutRequestCoroutine(logoutUrl);
        ClearLocalAuth();
        SceneManager.LoadScene(loginScene);
    }

    private static IEnumerator LogoutRequestCoroutine(string logoutUrl)
    {
        string jwt = PlayerPrefs.GetString("jwt", "");
        if (string.IsNullOrEmpty(jwt)) yield break;

        using (UnityWebRequest req = new UnityWebRequest(logoutUrl, "POST"))
        {
            req.uploadHandler   = new UploadHandlerRaw(new byte[0]);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = 2;
            req.SetRequestHeader("Content-Type",  "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + jwt);
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
                Debug.LogWarning("[Auth] Logout server error (ignored): " + req.error);
        }
    }

    public static void ClearLocalAuth()
    {
        PlayerPrefs.DeleteKey("jwt");
        PlayerPrefs.DeleteKey("refreshToken");
        PlayerPrefs.DeleteKey("username");
        PlayerPrefs.Save();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ClearErrors()
    {
        if (errorText       != null) errorText.text       = "";
        if (signupErrorText != null) signupErrorText.text = "";
    }

    private void ShowLoginError(string msg)
    {
        if (errorText != null) errorText.text = msg;
        else Debug.Log("[Auth] " + msg);
    }

    private void ShowSignupError(string msg)
    {
        if (signupErrorText != null) signupErrorText.text = msg;
        else if (errorText  != null) errorText.text = msg;
        else Debug.Log("[Auth] " + msg);
    }
}
