package com.vminhkiet.history_service.service;

import com.vminhkiet.history_service.dto.LeaderboardEntryResponse;
import com.vminhkiet.history_service.dto.MatchHistoryResponse;
import com.vminhkiet.history_service.dto.PlayerStatsResponse;
import com.vminhkiet.history_service.dto.SaveMatchRequest;

import java.util.List;

public interface HistoryService {
    void             saveMatch(String playerId, SaveMatchRequest req);
    List<MatchHistoryResponse> getRecentMatches(String playerId);
    PlayerStatsResponse        getStats(String playerId);
    List<LeaderboardEntryResponse> getLeaderboard();
}
