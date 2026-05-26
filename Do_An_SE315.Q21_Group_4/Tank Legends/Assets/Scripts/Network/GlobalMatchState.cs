using UnityEngine;

public static class GlobalMatchState
{
    public static bool HasMatchInfo { get; set; } = false;
    public static uint MatchId { get; set; } = 0;
    public static string ServerHost { get; set; } = "";
    public static int ServerPort { get; set; } = 0;
    public static bool AutoMatchmake { get; set; } = false; // Tự động tìm trận sau khi kết thúc trận cũ
    public static uint PlayerId { get; set; } = 0;
    public static string Token { get; set; } = "";
    public static GameObject LocalTankPrefab { get; set; } = null;

    public static void SetMatchInfo(uint matchId, string host, int port, uint playerId = 0, string token = "")
    {
        MatchId = matchId;
        ServerHost = host;
        ServerPort = port;
        PlayerId = playerId;
        Token = token;
        HasMatchInfo = true;
    }

    public static void Clear()
    {
        HasMatchInfo = false;
        MatchId = 0;
        ServerHost = "";
        ServerPort = 0;
        PlayerId = 0;
        Token = "";
        LocalTankPrefab = null;
    }
}
