package com.vminhkiet.history_service.dto;

import lombok.AllArgsConstructor;
import lombok.Builder;
import lombok.Data;
import lombok.NoArgsConstructor;

@Data
@Builder
@NoArgsConstructor
@AllArgsConstructor
public class LeaderboardEntryResponse {
    private int rank;
    private String playerId;
    private String username;
    private int totalKills;
    private int totalMatches;
    private int wins;
}
