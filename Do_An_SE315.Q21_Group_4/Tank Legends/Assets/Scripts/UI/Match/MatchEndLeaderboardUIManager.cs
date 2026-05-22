using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using TankNet;

public class MatchEndLeaderboardUIManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Transform container;
    
    [Header("Prefabs")]
    [SerializeField] private MatchEndLeaderboardItem rank1Prefab;
    [SerializeField] private MatchEndLeaderboardItem rank2Prefab;
    [SerializeField] private MatchEndLeaderboardItem rank3Prefab;
    [SerializeField] private MatchEndLeaderboardItem rankOtherPrefab;

    private void Start()
    {
        Clear();
    }

    public void BuildLeaderboard(MatchEndPlayer[] players)
    {
        Clear();

        Debug.Log($"[Leaderboard] BuildLeaderboard called with {players?.Length ?? 0} players.");

        if (players == null || players.Length == 0) return;

        for (int i = 0; i < players.Length; i++)
        {
            var p = players[i];
            int rank = i + 1;

            MatchEndLeaderboardItem prefab = rankOtherPrefab;
            if (rank == 1 && rank1Prefab != null) prefab = rank1Prefab;
            else if (rank == 2 && rank2Prefab != null) prefab = rank2Prefab;
            else if (rank == 3 && rank3Prefab != null) prefab = rank3Prefab;

            Debug.Log($"[Leaderboard] Rank {rank}: tankId={p.tankId}, userId={p.userId}, prefab null? {prefab == null}");

            if (prefab == null) continue;

            var item = Instantiate(prefab, container);
            
            // Initial bind with fallback name
            string fallbackName = $"Player {p.tankId}";
            item.Bind(rank, fallbackName, p.rpReward, p.kills);

            // Fetch actual Display Name asynchronously
            if (!string.IsNullOrEmpty(p.userId))
            {
                StartCoroutine(FetchProfileName(p.userId, item));
            }
        }
    }

    private void Clear()
    {
        if (container == null) return;
        foreach (Transform child in container)
        {
            Destroy(child.gameObject);
        }
    }

    private IEnumerator FetchProfileName(string userId, MatchEndLeaderboardItem itemUI)
    {
        string url = $"/api/user/{userId}";
        using var req = GameApiClient.CreateRequest(url, UnityWebRequest.kHttpVerbGET);
        GameApiClient.ApiCallResult result = default;
        yield return GameApiClient.Send(req, r => result = r);

        if (result.Success)
        {
            try
            {
                var userMe = JsonUtility.FromJson<UserMeResponseData>(result.Body);
                if (userMe != null && !string.IsNullOrWhiteSpace(userMe.username))
                {
                    if (itemUI != null) itemUI.UpdateName(userMe.username);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Leaderboard] Parse username failed for {userId}: {ex.Message}");
            }
        }
    }
}
