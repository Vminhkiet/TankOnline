using UnityEngine;
using TMPro;

public class MatchEndLeaderboardItem : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI rankText;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI rpText;
    [SerializeField] private TextMeshProUGUI killsText;

    public void Bind(int rank, string playerName, int rp, int kills)
    {
        if (rankText != null) rankText.text = rank.ToString();
        if (nameText != null) nameText.text = playerName;
        if (rpText != null) rpText.text = rp.ToString("N0") + " RP";
        if (killsText != null) killsText.text = kills + " Kills";
    }

    // Helper for async profile loading
    public void UpdateName(string newName)
    {
        if (nameText != null) nameText.text = newName;
    }
}
