using UnityEngine;
using UnityEngine.Networking;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System.Text;

// DTOs
[System.Serializable]
public class SaveMatchRequest
{
    public long   matchId;
    public string opponentId;
    public string result;       // "WIN" | "LOSE" | "DRAW"
    public int    kills;
    public int    deaths;
    public int    durationSecs;
    public string mapName;
}

[System.Serializable]
public class MatchHistoryItem
{
    public long   matchId;
    public string opponentId;
    public string result;
    public int    kills;
    public int    deaths;
    public int    durationSecs;
    public string mapName;
    public string playedAt;
}

[System.Serializable]
public class MatchHistoryList { public List<MatchHistoryItem> items; }

[System.Serializable]
public class PlayerStats
{
    public int    totalMatches;
    public int    wins;
    public int    losses;
    public int    draws;
    public int    totalKills;
    public int    totalDeaths;
    public double winRate;
    public int    bestKillStreak;
}

public class MatchHistoryUIManager : MonoBehaviour
{
    [Header("API")]
    public string saveMatchUrl   = "http://localhost:8080/api/history/match";
    public string historyUrl     = "http://localhost:8080/api/history/me";
    public string statsUrl       = "http://localhost:8080/api/history/me/stats";
    public string leaderboardUrl = "http://localhost:8080/api/history/leaderboard";

    [Header("Stats UI")]
    public TextMeshProUGUI totalMatchesText;
    public TextMeshProUGUI winRateText;
    public TextMeshProUGUI totalKillsText;
    public TextMeshProUGUI bestStreakText;

    [Header("History List")]
    public Transform      historyContainer;   // Scroll View Content
    public GameObject     historyItemPrefab;  // Prefab co Text components

    private void Start()
    {
        // Load history when Lobby scene opens
        string jwt = PlayerPrefs.GetString("jwt", "");
        if (!string.IsNullOrEmpty(jwt))
        {
            StartCoroutine(LoadStats());
            StartCoroutine(LoadHistory());
        }
    }

    // ── Save match result (call this from GameManager when match ends) ─────────

    public static void SaveMatchResult(MonoBehaviour caller, long matchId,
        string opponentId, bool won, bool draw, int kills, int deaths, int durationSecs)
    {
        var mgr = FindObjectOfType<MatchHistoryUIManager>();
        if (mgr != null)
            caller.StartCoroutine(mgr.SaveMatchCoroutine(
                matchId, opponentId, won, draw, kills, deaths, durationSecs));
        else
            caller.StartCoroutine(StaticSave(matchId, opponentId, won, draw,
                kills, deaths, durationSecs));
    }

    public IEnumerator SaveMatchCoroutine(long matchId, string opponentId,
        bool won, bool draw, int kills, int deaths, int durationSecs)
    {
        yield return StaticSave(matchId, opponentId, won, draw,
            kills, deaths, durationSecs);
    }

    private static IEnumerator StaticSave(long matchId, string opponentId,
        bool won, bool draw, int kills, int deaths, int durationSecs)
    {
        string jwt = PlayerPrefs.GetString("jwt", "");
        if (string.IsNullOrEmpty(jwt)) yield break;

        string result = draw ? "DRAW" : (won ? "WIN" : "LOSE");
        var req = new SaveMatchRequest
        {
            matchId     = matchId,
            opponentId  = opponentId,
            result      = result,
            kills       = kills,
            deaths      = deaths,
            durationSecs = durationSecs,
            mapName     = "world"
        };

        string json = JsonUtility.ToJson(req);
        using var webReq = new UnityWebRequest("http://localhost:8080/api/history/match", "POST");
        webReq.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        webReq.downloadHandler = new DownloadHandlerBuffer();
        webReq.SetRequestHeader("Content-Type",  "application/json");
        webReq.SetRequestHeader("Authorization", "Bearer " + jwt);
        yield return webReq.SendWebRequest();

        if (webReq.result == UnityWebRequest.Result.Success)
            Debug.Log("[History] Match saved.");
        else
            Debug.LogWarning("[History] Save failed: " + webReq.error);
    }

    // ── Load player stats ─────────────────────────────────────────────────────

    private IEnumerator LoadStats()
    {
        string jwt = PlayerPrefs.GetString("jwt", "");
        using var req = UnityWebRequest.Get(statsUrl);
        req.SetRequestHeader("Authorization", "Bearer " + jwt);
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success) yield break;

        PlayerStats stats = JsonUtility.FromJson<PlayerStats>(req.downloadHandler.text);
        if (totalMatchesText) totalMatchesText.text = stats.totalMatches.ToString();
        if (winRateText)      winRateText.text      = $"{(stats.winRate * 100):F0}%";
        if (totalKillsText)   totalKillsText.text   = stats.totalKills.ToString();
        if (bestStreakText)   bestStreakText.text    = stats.bestKillStreak.ToString();

        // Debug log — xem trong Unity Console khi chua co UI
        Debug.Log($"[History] Stats: {stats.totalMatches} matches | " +
                  $"Win rate: {(stats.winRate * 100):F0}% | " +
                  $"Kills: {stats.totalKills} | Best streak: {stats.bestKillStreak}");
    }

    // ── Load match history list ───────────────────────────────────────────────

    private IEnumerator LoadHistory()
    {
        string jwt = PlayerPrefs.GetString("jwt", "");
        using var req = UnityWebRequest.Get(historyUrl);
        req.SetRequestHeader("Authorization", "Bearer " + jwt);
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success) yield break;

        // Debug log — in ra Console de test khi chua co UI
        string wrapped = "{\"items\":" + req.downloadHandler.text + "}";
        MatchHistoryList list = JsonUtility.FromJson<MatchHistoryList>(wrapped);
        Debug.Log($"[History] Loaded {list.items.Count} matches:");
        foreach (var item in list.items)
        {
            int min = item.durationSecs / 60, sec = item.durationSecs % 60;
            Debug.Log($"  [{item.result}] vs {item.opponentId} | " +
                      $"{item.kills}K/{item.deaths}D | {min}:{sec:00} | {item.mapName}");
        }

        if (historyContainer == null || historyItemPrefab == null) yield break;

        // Clear old items
        foreach (Transform child in historyContainer)
            Destroy(child.gameObject);

        foreach (var item in list.items)
        {
            GameObject go = Instantiate(historyItemPrefab, historyContainer);
            PopulateHistoryItem(go, item);
        }
    }

    private void PopulateHistoryItem(GameObject go, MatchHistoryItem item)
    {
        // Find text components by name — adjust to match your prefab
        var texts = go.GetComponentsInChildren<TextMeshProUGUI>();
        foreach (var t in texts)
        {
            switch (t.gameObject.name)
            {
                case "ResultText":
                    t.text  = item.result;
                    t.color = item.result == "WIN"
                        ? new Color(0.2f, 0.8f, 0.2f)
                        : item.result == "LOSE"
                            ? new Color(0.9f, 0.2f, 0.2f)
                            : Color.yellow;
                    break;
                case "OpponentText":
                    t.text = "vs " + (item.opponentId == "bot-1" ? "Bot" : item.opponentId);
                    break;
                case "KillsText":
                    t.text = $"{item.kills}K / {item.deaths}D";
                    break;
                case "DurationText":
                    int min = item.durationSecs / 60;
                    int sec = item.durationSecs % 60;
                    t.text = $"{min:00}:{sec:00}";
                    break;
                case "MapText":
                    t.text = item.mapName;
                    break;
            }
        }
    }

    // ── Public refresh (call after saving) ───────────────────────────────────

    public void RefreshHistory()
    {
        StartCoroutine(LoadStats());
        StartCoroutine(LoadHistory());
    }
}
