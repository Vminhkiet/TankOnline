using UnityEngine;

public static class GlobalMatchState
{
    public static bool HasMatchInfo { get; set; } = false;
    public static uint MatchId { get; set; } = 0;
    public static string ServerHost { get; set; } = "";
    public static int ServerPort { get; set; } = 0;

    public static void SetMatchInfo(uint matchId, string host, int port)
    {
        MatchId = matchId;
        ServerHost = host;
        ServerPort = port;
        HasMatchInfo = true;
    }
    
    public static void Clear()
    {
        HasMatchInfo = false;
        MatchId = 0;
        ServerHost = "";
        ServerPort = 0;
    }
}
