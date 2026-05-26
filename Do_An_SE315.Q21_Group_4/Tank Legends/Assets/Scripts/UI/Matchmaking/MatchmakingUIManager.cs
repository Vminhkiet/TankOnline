using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;

[System.Serializable]
[UnityEngine.Scripting.Preserve]
public class MatchmakingResponseData
{
    public uint matchId;
    public string serverHost;
    public int serverPort;
    public uint playerId;
    public string token;
}

[System.Serializable]
[UnityEngine.Scripting.Preserve]
public class DeployedTankData
{
    public long itemId;
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
    [Tooltip("Path or full URL. Default: /api/matchmaking/find")]
    public string matchmakingPath = "/api/matchmaking/find";
    
    [Tooltip("Path or full URL. Default: /api/matchmaking/cancel")]
    public string cancelPath = "/api/matchmaking/cancel";
    
    [Header("Scene Settings")]
    public string gameSceneName = "_Complete-Game";

    private bool isSearching = false;
    private UnityWebRequest _activeRequest;  // Track the active HTTP request
    private Coroutine _findCoroutine;
    private Coroutine _timerCoroutine;

    private IEnumerator Start()
    {
        if (findMatchPanel != null) findMatchPanel.SetActive(false);
        if (statusText != null) statusText.text = "";

        // Chờ 1 frame để đảm bảo MainScreenButtonManager.Start() đã chạy xong và tắt các panel
        yield return null;

        // Tự động tìm trận nếu có yêu cầu từ màn hình kết thúc trận đấu trước đó
        if (GlobalMatchState.AutoMatchmake)
        {
            GlobalMatchState.AutoMatchmake = false; // Reset cờ
            if (findMatchPanel != null) findMatchPanel.SetActive(true); // Hiển thị bảng tìm trận
            OnFindMatchButtonClicked(); // Kích hoạt tiến trình tìm trận
        }
    }

    public void OnFindMatchButtonClicked()
    {
        if (isSearching) return;
        
        _findCoroutine  = StartCoroutine(FindMatchCoroutine());
        _timerCoroutine = StartCoroutine(UpdateTimerCoroutine());
    }

    public void OnCancelMatchButtonClicked()
    {
        isSearching = false;

        // Stop coroutines first so they don't touch the request after Abort
        if (_findCoroutine != null)  { StopCoroutine(_findCoroutine);  _findCoroutine = null; }
        if (_timerCoroutine != null) { StopCoroutine(_timerCoroutine); _timerCoroutine = null; }

        // Abort the pending HTTP request (Dispose is handled by the using block,
        // but since we stopped the coroutine, we need to dispose manually here)
        if (_activeRequest != null)
        {
            _activeRequest.Abort();
            _activeRequest.Dispose();
            _activeRequest = null;
        }

        if (findMatchPanel != null) findMatchPanel.SetActive(false);
        if (statusText != null) statusText.text = "Matchmaking cancelled.";
        if (timerText != null) timerText.text = "00:00";

        // Báo server xóa người chơi khỏi lobby
        StartCoroutine(SendCancelRequestCoroutine());
    }

    private IEnumerator SendCancelRequestCoroutine()
    {
        if (!GameApiClient.HasJwt()) yield break;

        using (UnityWebRequest request = GameApiClient.CreateRequest(cancelPath, "POST"))
        {
            GameApiClient.ApiCallResult result = default;
            yield return GameApiClient.Send(request, r => result = r);
            
            if (!result.Success)
            {
                Debug.LogWarning("[Matchmaking] Failed to notify server of cancellation: " + result.Error);
            }
        }
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

        if (!GameApiClient.HasJwt())
        {
            if (statusText != null) statusText.text = "Error: Not logged in.";
            isSearching = false;
            if (findMatchPanel != null) findMatchPanel.SetActive(false);
            yield break;
        }

        long deployedItemId = -1;
        string cachedPlayerId = PlayerPrefs.GetString("profile_player_id", "");
        using (UnityWebRequest req = GameApiClient.CreateRequest("/api/shop/deployed-tank", "GET"))
        {
            if (!string.IsNullOrEmpty(cachedPlayerId))
            {
                req.SetRequestHeader("X-Player-Id", cachedPlayerId);
            }
            
            GameApiClient.ApiCallResult res = default;
            yield return GameApiClient.Send(req, r => res = r);
            if (res.Success && !string.IsNullOrEmpty(res.Body))
            {
                try {
                    var node = JsonUtility.FromJson<DeployedTankData>(res.Body);
                    if (node != null) deployedItemId = node.itemId;
                } catch {}
            }
        }
        
        var tsm = FindObjectOfType<TankSelectionManager>(true);
        if (tsm != null)
        {
            var def = tsm.GetTankByItemId(deployedItemId);
            if (def != null) GlobalMatchState.LocalTankPrefab = def.GameplayPrefab;
            Debug.Log($"[Matchmaking] Deployed Tank ID: {deployedItemId}, Prefab: {(def != null ? def.GameplayPrefab.name : "null")}");
        }
        else
        {
            Debug.LogError("[Matchmaking] Could not find TankSelectionManager even in inactive objects!");
        }

        _activeRequest = GameApiClient.CreateRequest(matchmakingPath, "POST");
        var request = _activeRequest;

        using (request)
        {
            GameApiClient.ApiCallResult result = default;
            yield return GameApiClient.Send(request, r => result = r);

            _activeRequest = null; // Request finished, clear reference

            if (!isSearching) yield break; // was cancelled

            if (result.Success)
            {
                if (statusText != null) statusText.text = "Match found!";
                
                try 
                {
                    MatchmakingResponseData response = JsonUtility.FromJson<MatchmakingResponseData>(result.Body);
                    
                    GlobalMatchState.SetMatchInfo(response.matchId, response.serverHost, response.serverPort, response.playerId, response.token);
                    
                    Debug.Log($"Match Found! ID: {response.matchId}, Host: {response.serverHost}:{response.serverPort}, PlayerId: {response.playerId}");
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
                // Ignore 409 "replaced_by_new_search" — this is expected when user
                // cancels and immediately searches again
                if (result.Error != null && result.Error.Contains("409"))
                {
                    Debug.Log("[Matchmaking] Previous search replaced by new one (409) — ignoring.");
                    yield break;
                }

                string errorMsg = "Matchmaking failed: " + result.Error;
                Debug.LogError(errorMsg);
                if (statusText != null) statusText.text = errorMsg;
                isSearching = false;
                if (findMatchPanel != null) findMatchPanel.SetActive(false);
            }
        }
    }
}
