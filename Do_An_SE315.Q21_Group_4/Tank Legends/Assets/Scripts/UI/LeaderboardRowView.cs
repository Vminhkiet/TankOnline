using UnityEngine;
using TMPro;

public class LeaderboardRowView : MonoBehaviour
{
    public TMP_Text rankText;
    public TMP_Text usernameText;
    public TMP_Text totalKillsText;
    public TMP_Text totalMatchesText;
    public TMP_Text winsText;

    public void Bind(LeaderboardEntryData entry, int fallbackRank)
    {
        int rank = entry != null && entry.rank > 0 ? entry.rank : fallbackRank;
        string username = entry != null && !string.IsNullOrWhiteSpace(entry.username) ? entry.username : "-";
        string kills = entry != null ? entry.totalKills.ToString() : "0";
        string matches = entry != null ? entry.totalMatches.ToString() : "0";
        string wins = entry != null ? entry.wins.ToString() : "0";

        if (rankText != null) rankText.text = "#" + rank;
        if (usernameText != null) usernameText.text = username;
        if (totalKillsText != null) totalKillsText.text = kills;
        if (totalMatchesText != null) totalMatchesText.text = matches;
        if (winsText != null) winsText.text = wins;
    }
}
