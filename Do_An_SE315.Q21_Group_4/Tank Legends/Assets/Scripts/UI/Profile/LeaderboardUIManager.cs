using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public class LeaderboardEntryData
{
    public int rank;
    public string playerId;
    public string username;
    public int totalKills;
    public int totalMatches;
    public int wins;
}

[Serializable]
public class LeaderboardEntryListWrapper
{
    public List<LeaderboardEntryData> items;
}

public class LeaderboardUIManager : MonoBehaviour
{
private const string LeaderboardPath = "/api/history/leaderboard";



    [Header("Top 1-4 Dedicated Prefabs")]
    [SerializeField] private Transform top1To4Container;
    [SerializeField] private GameObject top1Prefab;
    [SerializeField] private GameObject top2Prefab;
    [SerializeField] private GameObject top3Prefab;
    [SerializeField] private GameObject top4Prefab;

    [Header("Top 5-10 Shared Prefabs")]
    [SerializeField] private Transform top5To10Container;
    [SerializeField] private GameObject sharedRowPrefab;

    [Header("Optional Status")]
    [SerializeField] private TMP_Text statusText;

    private readonly List<GameObject> spawnedTopRows = new List<GameObject>();
    private readonly List<GameObject> spawnedSharedRows = new List<GameObject>();


    private void Start()
    {
        if (top1To4Container != null)
        {
            for (int i = top1To4Container.childCount - 1; i >= 0; i--)
                Destroy(top1To4Container.GetChild(i).gameObject);
        }
        if (top5To10Container != null)
        {
            for (int i = top5To10Container.childCount - 1; i >= 0; i--)
                Destroy(top5To10Container.GetChild(i).gameObject);
        }
    }

    private void OnEnable()
    {
        StartCoroutine(LoadLeaderboardCoroutine());
    }

    public void RefreshLeaderboard()
    {
        StartCoroutine(LoadLeaderboardCoroutine());
    }

    private IEnumerator LoadLeaderboardCoroutine()
    {
        ClearAllRows();
        SetStatus("Loading leaderboard...");

        string url = GameApiClient.BuildUrl(LeaderboardPath);
        Debug.Log($"[Leaderboard] Loading from {url}");

        using (UnityWebRequest req = GameApiClient.CreateRequest(LeaderboardPath, UnityWebRequest.kHttpVerbGET))
        {
            GameApiClient.ApiCallResult result = default;
            yield return GameApiClient.Send(req, r => result = r);

            if (!result.Success)
            {
                Debug.LogError($"[Leaderboard] Load failed | status={result.StatusCode} | error={result.ErrorMessage}");
                SetStatus("Load leaderboard failed: " + result.ErrorMessage);
                yield break;
            }

            Debug.Log($"[Leaderboard] Load success | status={result.StatusCode} | body={result.Body}");

            List<LeaderboardEntryData> entries = ParseEntries(result.Body);
            if (entries == null)
            {
                Debug.LogWarning("[Leaderboard] ParseEntries returned null.");
                SetStatus("Invalid leaderboard response format.");
                yield break;
            }

            Debug.Log($"[Leaderboard] Parsed {entries.Count} entries.");

            BindTopRows(entries);
            BindSharedRows(entries);

            SetStatus(entries.Count == 0 ? "No leaderboard data." : string.Empty);
        }
    }

    private List<LeaderboardEntryData> ParseEntries(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            return new List<LeaderboardEntryData>();

        try
        {
            string wrapped = "{\"items\":" + rawJson + "}";
            LeaderboardEntryListWrapper wrapper = JsonUtility.FromJson<LeaderboardEntryListWrapper>(wrapped);
            return wrapper?.items ?? new List<LeaderboardEntryData>();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[Leaderboard] Parse failed: " + ex.Message);
            return null;
        }
    }

    private void BindTopRows(List<LeaderboardEntryData> entries)
    {
        if (top1To4Container == null)
            return;

        var prefabs = new[] { top1Prefab, top2Prefab, top3Prefab, top4Prefab };
        for (int i = 0; i < prefabs.Length; i++)
        {
            if (prefabs[i] == null)
                continue;

            // Chỉ spawn row khi có dữ liệu thực tế cho hạng này
            LeaderboardEntryData entry = GetEntry(entries, i);
            if (entry == null)
                continue;

            GameObject rowGO = Instantiate(prefabs[i], top1To4Container);
            spawnedTopRows.Add(rowGO);

            LeaderboardRowView rowView = rowGO.GetComponent<LeaderboardRowView>();
            if (rowView != null)
            {
                rowView.Bind(entry, i + 1);
            }
            else
            {
                Debug.LogWarning($"[Leaderboard] Prefab top{i + 1} is missing LeaderboardRowView component!");
            }
        }
    }

    private void BindSharedRows(List<LeaderboardEntryData> entries)
    {
        if (top5To10Container == null || sharedRowPrefab == null)
            return;

        for (int i = 4; i < Mathf.Min(entries.Count, 10); i++)
        {
            GameObject rowGO = Instantiate(sharedRowPrefab, top5To10Container);
            spawnedSharedRows.Add(rowGO);

            LeaderboardRowView rowView = rowGO.GetComponent<LeaderboardRowView>();
            if (rowView != null)
            {
                rowView.Bind(entries[i], i + 1);
            }
            else
            {
                Debug.LogWarning("[Leaderboard] Shared row prefab is missing LeaderboardRowView component!");
            }
        }
    }

    private LeaderboardEntryData GetEntry(List<LeaderboardEntryData> entries, int index)
    {
        if (entries == null || index < 0 || index >= entries.Count)
            return null;
        return entries[index];
    }

    private void ClearAllRows()
    {
        // Xóa các row đã spawn lần trước
        for (int i = 0; i < spawnedTopRows.Count; i++)
        {
            if (spawnedTopRows[i] != null)
                Destroy(spawnedTopRows[i]);
        }
        spawnedTopRows.Clear();

        for (int i = 0; i < spawnedSharedRows.Count; i++)
        {
            if (spawnedSharedRows[i] != null)
                Destroy(spawnedSharedRows[i]);
        }
        spawnedSharedRows.Clear();

        // Xóa luôn tất cả children có sẵn trong container (placeholder từ Editor)
        if (top1To4Container != null)
        {
            for (int i = top1To4Container.childCount - 1; i >= 0; i--)
                Destroy(top1To4Container.GetChild(i).gameObject);
        }
        if (top5To10Container != null)
        {
            for (int i = top5To10Container.childCount - 1; i >= 0; i--)
                Destroy(top5To10Container.GetChild(i).gameObject);
        }
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
    }
}
