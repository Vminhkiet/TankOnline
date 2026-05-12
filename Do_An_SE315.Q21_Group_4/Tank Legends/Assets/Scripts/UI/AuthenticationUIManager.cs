using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;
using System.Text;

[System.Serializable]
public class LoginRequestData
{
    public string username;
    public string password;
}

[System.Serializable]
public class AuthResponseData
{
    public string jwt;
    public string refreshToken;
}

public class AuthenticationUIManager : MonoBehaviour
{
    [Header("UI Panels")]
    public GameObject loginPanel;
    public GameObject signupPanel;

    [Header("Input Fields")]
    public TMP_InputField usernameInput;
    public TMP_InputField passwordInput;
    public TextMeshProUGUI errorText;

    [Header("API Settings")]
    public string authApiUrl = "http://localhost:8081/api/auth/login";

    [Header("Scene Settings")]
    public string lobbySceneName = "Lobby";

    private bool isAuthenticating = false;

    private void Start()
    {
        ShowLoginPanel();
        if (errorText != null) errorText.text = "";
    }

    public void ShowLoginPanel()
    {
        if (loginPanel != null) loginPanel.SetActive(true);
        if (signupPanel != null) signupPanel.SetActive(false);
    }

    public void ShowSignupPanel()
    {
        if (loginPanel != null) loginPanel.SetActive(false);
        if (signupPanel != null) signupPanel.SetActive(true);
    }

    public void OnLoginButtonClicked()
    {
        if (isAuthenticating) return;

        if (string.IsNullOrEmpty(lobbySceneName))
        {
            Debug.LogWarning("Lobby scene name is empty. Please set lobbySceneName in the inspector.");
            return;
        }

        StartCoroutine(RealLoginCoroutine());
    }

    private IEnumerator RealLoginCoroutine()
    {
        if (usernameInput == null || passwordInput == null)
        {
            Debug.LogError("Please assign Username and Password InputFields in the Inspector!");
            yield break;
        }

        string username = usernameInput.text;
        string password = passwordInput.text;

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            ShowError("Please enter both username and password.");
            yield break;
        }

        isAuthenticating = true;
        ShowError("Connecting to server...");

        LoginRequestData requestData = new LoginRequestData
        {
            username = username,
            password = password
        };

        string jsonData = JsonUtility.ToJson(requestData);

        using (UnityWebRequest request = new UnityWebRequest(authApiUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                ShowError("Login Successful!");
                
                try 
                {
                    AuthResponseData response = JsonUtility.FromJson<AuthResponseData>(request.downloadHandler.text);
                    
                    // Save tokens for future API calls
                    if (!string.IsNullOrEmpty(response.jwt))
                    {
                        PlayerPrefs.SetString("jwt", response.jwt);
                        PlayerPrefs.SetString("refreshToken", response.refreshToken);
                        PlayerPrefs.Save();
                        Debug.Log("Tokens saved to PlayerPrefs.");
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError("Failed to parse response: " + e.Message);
                }

                yield return new WaitForSeconds(0.5f);
                SceneManager.LoadScene(lobbySceneName);
            }
            else
            {
                string errorMsg = "Login failed: ";
                if (request.responseCode == 401 || request.responseCode == 403 || request.responseCode == 400)
                {
                    errorMsg += "Invalid username or password.";
                }
                else
                {
                    errorMsg += request.error;
                }

                ShowError(errorMsg);
                isAuthenticating = false;
            }
        }
    }

    private void ShowError(string message)
    {
        if (errorText != null)
        {
            errorText.text = message;
        }
        else
        {
            Debug.Log("Auth Message: " + message);
        }
    }
}
