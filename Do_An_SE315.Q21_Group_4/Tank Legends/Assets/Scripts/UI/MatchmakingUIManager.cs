using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;

[System.Serializable]
public class MatchmakingResponseData
{
    public uint matchId;
    public string serverHost;
    public int serverPort;
}

public class MatchmakingUIManager : MonoBehaviour
{
    [Header("UI Panels")]
    public GameObject findMatchPanel;
    public GameObject searchingPanel;

    [Header("UI Elements")]
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI timerText;

    [Header("API Settings")]
    public string matchmakingApiUrl = "http://localhost:8080/api/matchmaking/find";
    
    [Header("Scene Settings")]
    public string gameSceneName = "_Complete-Game";

    private bool isSearching = false;

    private void Start()
    {
        if (findMatchPanel != null) findMatchPanel.SetActive(false);
        if (statusText != null) statusText.text = "";
    }

    public void OnFindMatchButtonClicked()
    {
        if (isSearching) return;
        
        if (findMatchPanel != null) findMatchPanel.SetActive(true);
        StartCoroutine(FindMatchCoroutine());
        StartCoroutine(UpdateTimerCoroutine());
    }

    public void OnCancelMatchButtonClicked()
    {
        // Add functionality to cancel match search if supported by your API
        isSearching = false;
        if (findMatchPanel != null) findMatchPanel.SetActive(false);
        if (statusText != null) statusText.text = "Matchmaking cancelled.";
        if (timerText != null) timerText.text = "00:00";
    }

    private IEnumerator UpdateTimerCoroutine()
    {
        if (timerText == null) yield break;
        
        float elapsedTime = 0f;
        while (isSearching)
        {
            int minutes = Mathf.FloorToInt(elapsedTime / 60F);
            int seconds = Mathf.FloorToInt(elapsedTime - minutes * 60);
            timerText.text = string.Format("{0:00}:{1:00}", minutes, seconds);
            
            yield return new WaitForSeconds(1f);
            elapsedTime += 1f;
        }
    }

    private IEnumerator FindMatchCoroutine()
    {
        isSearching = true;
        
        if (statusText != null) statusText.text = "Finding match...";

        // Retrieve JWT token
        string jwt = PlayerPrefs.GetString("jwt", "");
        if (string.IsNullOrEmpty(jwt))
        {
            if (statusText != null) statusText.text = "Error: Not logged in.";
            isSearching = false;
            if (findMatchPanel != null) findMatchPanel.SetActive(false);
            yield break;
        }

        using (UnityWebRequest request = new UnityWebRequest(matchmakingApiUrl, "POST"))
        {
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + jwt);

            yield return request.SendWebRequest();

            if (!isSearching) yield break; // was cancelled

            if (request.result == UnityWebRequest.Result.Success)
            {
                if (statusText != null) statusText.text = "Match found!";
                
                try 
                {
                    MatchmakingResponseData response = JsonUtility.FromJson<MatchmakingResponseData>(request.downloadHandler.text);
                    
                    // Save to global state
                    GlobalMatchState.SetMatchInfo(response.matchId, response.serverHost, response.serverPort);
                    
                    Debug.Log($"Match Found! ID: {response.matchId}, Host: {response.serverHost}:{response.serverPort}");
                }
                catch (System.Exception e)
                {
                    Debug.LogError("Failed to parse matchmaking response: " + e.Message);
                    if (statusText != null) statusText.text = "Error parsing match data.";
                    isSearching = false;
                    if (findMatchPanel != null) findMatchPanel.SetActive(false);
                    yield break;
                }

                yield return new WaitForSeconds(0.5f);
                SceneManager.LoadScene(gameSceneName);
            }
            else
            {
                string errorMsg = "Matchmaking failed: " + request.error;
                Debug.LogError(errorMsg);
                if (statusText != null) statusText.text = errorMsg;
                isSearching = false;
                if (findMatchPanel != null) findMatchPanel.SetActive(false);
            }
        }
    }
}
