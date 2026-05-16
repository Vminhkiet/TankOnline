package com.vminhkiet.history_service.dto;

import lombok.AllArgsConstructor;
import lombok.Data;

@Data
@AllArgsConstructor
public class PlayerStatsResponse {
    private String playerId;
    private int    totalMatches;
    private int    wins;
    private int    losses;
    private int    draws;
    private int    totalKills;
    private int    totalDeaths;
    private double winRate;       // 0.0 - 1.0
    private int    bestKillStreak;
}
