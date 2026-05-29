using UnityEngine;
using UnityEngine.UI;
using TMPro;
using TankNet;

namespace Complete
{
    public class GameUIManager : MonoBehaviour
    {
        public static GameUIManager Instance { get; private set; }

        [Header("Tank HUD UI")]
        public TextMeshProUGUI m_AmmoText;
        public Image m_ReloadProgressImage;
        public Button m_ReloadButton;
        public Image specialAbilityIcon;

        [Header("Match Overlay UI")]
        public Text m_MessageText;
        public TextMeshProUGUI matchTimerText;
        public TextMeshProUGUI pingText;

        [Header("Match End UI")]
        public GameObject matchEndPanel;
        public TextMeshProUGUI matchEndResultText;
        public TextMeshProUGUI matchEndStatsText;
        public TextMeshProUGUI matchEndRpText;
        public TextMeshProUGUI matchEndCoinText;
        public MatchEndLeaderboardUIManager leaderboardUI;

        [Header("Match End Intermediate Screens")]
        public GameObject m_VictoryScreen;
        public GameObject m_YouDiedScreen;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
            }
            if (matchEndPanel != null) matchEndPanel.SetActive(false);

        }


        public void UpdateHUDForLocalTank(GameObject localTank)
        {
            if (specialAbilityIcon == null || localTank == null) return;
            var tankHealth = localTank.GetComponent<Complete.TankHealth>();
            if (tankHealth != null && tankHealth.m_Definition != null)
            {
                var icon = tankHealth.m_Definition.SpecialAbility.Icon;
                specialAbilityIcon.sprite = icon;
                specialAbilityIcon.enabled = (icon != null);
            }
            else
            {
                specialAbilityIcon.enabled = false;
            }
        }


        public void UpdateAmmoUI(int currentAmmo, int maxAmmo)
        {
            if (m_AmmoText != null)
            {
                m_AmmoText.text = $"{currentAmmo} / {maxAmmo}";
            }
        }

        public void UpdateReloadProgress(float progress)
        {
            if (m_ReloadProgressImage != null)
            {
                m_ReloadProgressImage.fillAmount = progress;
            }
        }

        public void SetMessageText(string message)
        {
            if (m_MessageText != null) m_MessageText.text = message;
        }

        public void SetMatchTimer(float serverTimeRemaining)
        {
            if (matchTimerText != null && serverTimeRemaining >= 0f)
            {
                int minutes = Mathf.FloorToInt(serverTimeRemaining / 60f);
                int seconds = Mathf.FloorToInt(serverTimeRemaining % 60f);
                matchTimerText.text = string.Format("{0:00}:{1:00}", minutes, seconds);
            }
        }

        public void SetPing(int pingTime)
        {
            if (pingText != null)
            {
                pingText.text = $"Ping: {pingTime} ms";
                pingText.color = pingTime < 100 ? Color.green : (pingTime < 200 ? Color.yellow : Color.red);
            }
        }

        public void SetPingOffline()
        {
            if (pingText != null)
            {
                pingText.text = "Ping: -- ms";
                pingText.color = Color.white;
            }
        }

        public void ShowVictoryScreen(bool show)
        {
            if (m_VictoryScreen != null) m_VictoryScreen.SetActive(show);
        }

        public void ShowYouDiedScreen(bool show)
        {
            if (m_YouDiedScreen != null) m_YouDiedScreen.SetActive(show);
        }

        public void ShowMatchEndPanel(bool won, bool draw, int myKills, int myDeaths, int durationSecs, MatchEndData end)
        {
            if (matchEndPanel != null) matchEndPanel.SetActive(true);

            string resultText = draw ? "DRAW!" : (won ? "YOU WIN!" : "YOU LOSE!");
            if (matchEndResultText != null) matchEndResultText.text = resultText;

            if (matchEndStatsText != null)
            {
                if (end != null)
                {
                    matchEndStatsText.text =
                        $"Kills: {myKills}   Deaths: {myDeaths}\n" +
                        $"Duration: {durationSecs / 60}:{durationSecs % 60:00}\n" +
                        $"Rank: #{end.Placement}";
                }
                else
                {
                    matchEndStatsText.text =
                        $"Kills: {myKills}   Deaths: {myDeaths}\n" +
                        $"Duration: {durationSecs / 60}:{durationSecs % 60:00}";
                }
            }

            if (end != null)
            {
                if (matchEndRpText != null) matchEndRpText.text = $"{(end.RpReward > 0 ? "+" : "")}{end.RpReward}";
                if (matchEndCoinText != null) matchEndCoinText.text = "+25";
                if (leaderboardUI != null) leaderboardUI.BuildLeaderboard(end.Players);
            }
            else
            {
                if (matchEndRpText != null) matchEndRpText.text = "+0";
                if (matchEndCoinText != null) matchEndCoinText.text = "+25";
            }
        }
        public void ShowEndMessage(bool isOnline, TankManager[] tanks, TankManager roundWinner, TankManager gameWinner)
        {
            if (m_MessageText == null) return;

            if (isOnline)
            {
                // In online mode, we act as a final leaderboard based on score and placement
                string message = "MATCH ENDED\n\nLEADERBOARD:\n\n";
                
                var sortedTanks = new System.Collections.Generic.List<TankManager>();
                for (int i = 0; i < tanks.Length; i++)
                {
                    if (tanks[i].m_Instance != null && tanks[i].m_Instance.activeSelf || tanks[i].m_Placement > 0)
                    {
                        sortedTanks.Add(tanks[i]);
                    }
                }
                
                sortedTanks.Sort((a, b) => {
                    int pCmp = a.m_Placement.CompareTo(b.m_Placement);
                    if (pCmp != 0) return pCmp;
                    return b.m_Score.CompareTo(a.m_Score); // Higher score wins tiebreaker
                });

                foreach (var t in sortedTanks)
                {
                    message += $"{t.m_ColoredPlayerText} - Rank #{t.m_Placement} - {t.m_Score} PTS\n";
                }
                m_MessageText.text = message;
                return;
            }

            // By default when a round ends there are no winners so the default end message is a draw.
            string offlineMessage = "DRAW!";

            // If there is a winner then change the message to reflect that.
            if (roundWinner != null)
                offlineMessage = roundWinner.m_ColoredPlayerText + " WINS THE ROUND!";

            // Add some line breaks after the initial message.
            offlineMessage += "\n\n\n\n";

            // Go through all the tanks and add each of their scores to the message.
            for (int i = 0; i < tanks.Length; i++)
            {
                offlineMessage += tanks[i].m_ColoredPlayerText + ": " + tanks[i].m_Wins + " WINS\n";
            }

            // If there is a game winner, change the entire message to reflect that.
            if (gameWinner != null)
                offlineMessage = gameWinner.m_ColoredPlayerText + " WINS THE GAME!";

            m_MessageText.text = offlineMessage;
        }
    }
}
