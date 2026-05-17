using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MatchHistoryCardView : MonoBehaviour
{
    [Header("UI Refs")]
    [SerializeField] private Image resultImage;
    [SerializeField] private Image mapImage;
    [SerializeField] private TextMeshProUGUI opponentText;
    [SerializeField] private TextMeshProUGUI kdText;
    [SerializeField] private TextMeshProUGUI durationText;
    [SerializeField] private TextMeshProUGUI mapText;
    [SerializeField] private TextMeshProUGUI playedAtText;

    public void Bind(MatchHistoryItem item, Sprite resultSprite, Sprite mapSprite)
    {
        string resultKey = (item.result ?? string.Empty).Trim().ToUpperInvariant();

        if (resultImage != null && resultSprite != null)
            resultImage.sprite = resultSprite;

        if (mapImage != null && mapSprite != null)
            mapImage.sprite = mapSprite;

        if (opponentText != null)
            opponentText.text = (item.opponentId == "bot-1" ? "Bot" : item.opponentId);

        if (kdText != null)
            kdText.text = $"<color=green>{item.kills}</color> / <color=red>{item.deaths}</color>";

        if (durationText != null)
        {
            int min = item.durationSecs / 60;
            int sec = item.durationSecs % 60;
            durationText.text = $"{min:00}:{sec:00}";
        }

        if (mapText != null)
            mapText.text = item.mapName;

        if (playedAtText != null)
            playedAtText.text = FormatPlayedAt(item.playedAt);
    }

    private static string FormatPlayedAt(string playedAt)
    {
        if (string.IsNullOrWhiteSpace(playedAt)) return "-";
        if (DateTime.TryParse(playedAt, out var dt))
            return dt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
        return playedAt;
    }
}
