using UnityEngine;
using UnityEngine.Networking;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

// DTOs
[System.Serializable]
public class SaveMatchRequest
{
    public long matchId;
    public string opponentId;
    public string result; // "WIN" | "LOSE" | "DRAW"
    public int kills;
    public int deaths;
    public int durationSecs;
    public string mapName;
}

[System.Serializable]
public class MatchHistoryItem
{
    public long matchId;
    public string opponentId;
    public string result;
    public int kills;
    public int deaths;
    public int durationSecs;
    public string mapName;
    public string playedAt;
}

[System.Serializable]
public class MatchHistoryList
{
    public List<MatchHistoryItem> items;
}

[System.Serializable]
public class PlayerStats
{
    public int totalMatches;
    public int wins;
    public int losses;
    public int draws;
    public int totalKills;
    public int totalDeaths;
    public double winRate;
    public int bestKillStreak;
}

public class MatchHistoryUIManager : MonoBehaviour
{
    private const string MatchPath = "/api/history/match";
    private const string HistoryPath = "/api/history/me";
    private const string StatsPath = "/api/history/me/stats";

    [Header("API")]
    [SerializeField] private string apiBaseUrl = "http://localhost:8080";

    [Header("Stats UI")]
    public TextMeshProUGUI totalMatchesText;
    public TextMeshProUGUI winRateText;
    public TextMeshProUGUI totalKillsText;
    public TextMeshProUGUI bestStreakText;

    [Header("History List")]
    public Transform historyContainer;
    public GameObject historyItemPrefab;

    private readonly Dictionary<string, Sprite> _resultSprites = new Dictionary<string, Sprite>();
    private readonly Dictionary<string, Sprite> _mapSprites = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

    private void Awake()
    {
        LoadResultSprites();
        LoadMapSprites();
    }

    private void Start()
    {
        string jwt = PlayerPrefs.GetString("jwt", "");
        if (string.IsNullOrEmpty(jwt))
        {
            Debug.LogWarning("[History] Start skipped load: jwt is empty.");
            return;
        }

        StartCoroutine(LoadStats());
        StartCoroutine(LoadHistory());
    }

    public static void SaveMatchResult(MonoBehaviour caller, long matchId, string opponentId, bool won, bool draw, int kills, int deaths, int durationSecs)
    {
        var mgr = FindObjectOfType<MatchHistoryUIManager>();
        if (mgr == null)
        {
            Debug.LogWarning("[History] SaveMatchResult skipped: MatchHistoryUIManager not found in scene.");
            return;
        }

        caller.StartCoroutine(mgr.SaveMatchCoroutine(matchId, opponentId, won, draw, kills, deaths, durationSecs));
    }

    public IEnumerator SaveMatchCoroutine(long matchId, string opponentId, bool won, bool draw, int kills, int deaths, int durationSecs)
    {
        string jwt = PlayerPrefs.GetString("jwt", "");
        if (string.IsNullOrEmpty(jwt))
        {
            Debug.LogWarning("[History] SaveMatch skipped: jwt is empty.");
            yield break;
        }

        string url = BuildApiUrl(MatchPath);
        string result = draw ? "DRAW" : (won ? "WIN" : "LOSE");

        var reqBody = new SaveMatchRequest
        {
            matchId = matchId,
            opponentId = opponentId,
            result = result,
            kills = kills,
            deaths = deaths,
            durationSecs = durationSecs,
            mapName = "world"
        };

        string json = JsonUtility.ToJson(reqBody);

        using var req = CreateAuthorizedRequest(url, "POST", jwt, json);
        Debug.Log($"[History] SaveMatch request | url={url}");
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[History] SaveMatch failed | url={url} | status={(long)req.responseCode} | error={req.error} | body={req.downloadHandler?.text}");
            yield break;
        }

        Debug.Log($"[History] SaveMatch success | status={(long)req.responseCode} | body={req.downloadHandler?.text}");
    }

    private IEnumerator LoadStats()
    {
        string jwt = PlayerPrefs.GetString("jwt", "");
        if (string.IsNullOrEmpty(jwt))
        {
            Debug.LogWarning("[History] LoadStats skipped: jwt is empty.");
            yield break;
        }

        string url = BuildApiUrl(StatsPath);
        using var req = CreateAuthorizedRequest(url, UnityWebRequest.kHttpVerbGET, jwt);
        Debug.Log($"[History] LoadStats request | url={url}");

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[History] LoadStats failed | url={url} | status={(long)req.responseCode} | error={req.error} | body={req.downloadHandler?.text}");
            yield break;
        }

        Debug.Log($"[History] LoadStats success | status={(long)req.responseCode} | body={req.downloadHandler?.text}");

        PlayerStats stats;
        try
        {
            stats = JsonUtility.FromJson<PlayerStats>(req.downloadHandler.text);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[History] LoadStats parse failed | ex={ex.Message} | body={req.downloadHandler?.text}");
            yield break;
        }

        if (stats == null)
        {
            Debug.LogWarning("[History] LoadStats parsed null stats.");
            yield break;
        }

        if (totalMatchesText) totalMatchesText.text = stats.totalMatches.ToString();
        if (winRateText) winRateText.text = $"{(stats.winRate * 100):F0}%";
        if (totalKillsText) totalKillsText.text = stats.totalKills.ToString();
        if (bestStreakText) bestStreakText.text = stats.bestKillStreak.ToString();
    }

    private IEnumerator LoadHistory()
    {
        string jwt = PlayerPrefs.GetString("jwt", "");
        if (string.IsNullOrEmpty(jwt))
        {
            Debug.LogWarning("[History] LoadHistory skipped: jwt is empty.");
            yield break;
        }

        string url = BuildApiUrl(HistoryPath);
        using var req = CreateAuthorizedRequest(url, UnityWebRequest.kHttpVerbGET, jwt);
        Debug.Log($"[History] LoadHistory request | url={url}");

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[History] LoadHistory failed | url={url} | status={(long)req.responseCode} | error={req.error} | body={req.downloadHandler?.text}");
            yield break;
        }

        Debug.Log($"[History] LoadHistory success | status={(long)req.responseCode} | body={req.downloadHandler?.text}");

        string wrapped = "{\"items\":" + req.downloadHandler.text + "}";
        MatchHistoryList list;
        try
        {
            list = JsonUtility.FromJson<MatchHistoryList>(wrapped);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[History] LoadHistory parse failed | ex={ex.Message} | body={req.downloadHandler?.text}");
            yield break;
        }

        if (list == null || list.items == null)
        {
            Debug.LogWarning("[History] LoadHistory parsed null/empty list.");
            yield break;
        }

        if (historyContainer == null || historyItemPrefab == null)
        {
            Debug.LogWarning("[History] Missing historyContainer or historyItemPrefab.");
            yield break;
        }

        foreach (Transform child in historyContainer)
            Destroy(child.gameObject);

        int instantiatedCount = 0;
        foreach (var item in list.items)
        {
            GameObject go = Instantiate(historyItemPrefab, historyContainer);
            PopulateHistoryItem(go, item);
            instantiatedCount++;
        }

        Debug.Log($"[History] Instantiated {instantiatedCount} history cards into container '{historyContainer.name}'.");
    }

    private void PopulateHistoryItem(GameObject go, MatchHistoryItem item)
    {
        var view = go.GetComponent<MatchHistoryCardView>();
        if (view == null)
        {
            Debug.LogWarning("[History] Missing MatchHistoryCardView on history item prefab instance.");
            return;
        }

        string resultKey = (item.result ?? string.Empty).Trim().ToUpperInvariant();
        _resultSprites.TryGetValue(resultKey, out var resultSprite);

        string mapKey = (item.mapName ?? string.Empty).Trim();
        _mapSprites.TryGetValue(mapKey, out var mapSprite);

        view.Bind(item, resultSprite, mapSprite);
    }

    private void LoadResultSprites()
    {
        _resultSprites.Clear();

        Sprite[] sprites = Resources.LoadAll<Sprite>("Sprites/UI");
        foreach (var s in sprites)
        {
            if (s.name.Equals("Win", StringComparison.OrdinalIgnoreCase))
                _resultSprites["WIN"] = s;
            else if (s.name.Equals("Lose", StringComparison.OrdinalIgnoreCase))
                _resultSprites["LOSE"] = s;
            else if (s.name.Equals("Draw", StringComparison.OrdinalIgnoreCase))
                _resultSprites["DRAW"] = s;
        }

        if (!_resultSprites.ContainsKey("WIN") || !_resultSprites.ContainsKey("LOSE") || !_resultSprites.ContainsKey("DRAW"))
        {
            Debug.LogWarning("[History] Missing one or more result sprites in Resources/Sprites/UI (Win/Lose/Draw).");
        }
    }

    private void LoadMapSprites()
    {
        _mapSprites.Clear();

        Sprite[] sprites = Resources.LoadAll<Sprite>("Sprites/Maps");
        foreach (var s in sprites)
        {
            if (!_mapSprites.ContainsKey(s.name))
                _mapSprites[s.name] = s;
        }

        if (_mapSprites.Count == 0)
        {
            Debug.LogWarning("[History] No map sprites found in Resources/Sprites/Maps.");
        }
    }

    private string BuildApiUrl(string path)
    {
        string baseUrl = (apiBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(baseUrl))
            baseUrl = "http://localhost:8080";

        string normalizedPath = path.StartsWith("/") ? path : "/" + path;
        return baseUrl + normalizedPath;
    }

    private static UnityWebRequest CreateAuthorizedRequest(string url, string method, string jwt, string jsonBody = null)
    {
        UnityWebRequest req = new UnityWebRequest(url, method)
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

    public void RefreshHistory()
    {
        StartCoroutine(LoadStats());
        StartCoroutine(LoadHistory());
    }
}
