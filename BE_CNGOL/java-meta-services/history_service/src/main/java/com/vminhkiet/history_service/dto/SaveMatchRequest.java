package com.vminhkiet.history_service.dto;

import lombok.Data;

// Called by Unity client after match ends
@Data
public class SaveMatchRequest {
    private long   matchId;
    private String opponentId;    // opponent userId or "bot-1"
    private String result;        // "WIN" | "LOSE" | "DRAW"
    private int    kills;
    private int    deaths;
    private int    durationSecs;
    private String mapName;
}
