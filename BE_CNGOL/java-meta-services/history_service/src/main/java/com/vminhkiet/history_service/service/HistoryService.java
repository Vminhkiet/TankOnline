package com.vminhkiet.history_service.service;

import com.vminhkiet.history_service.dto.MatchHistoryResponse;
import com.vminhkiet.history_service.dto.PlayerStatsResponse;
import com.vminhkiet.history_service.dto.SaveMatchRequest;

import java.util.List;
import java.util.Map;

public interface HistoryService {
    void             saveMatch(String playerId, SaveMatchRequest req);
    List<MatchHistoryResponse> getRecentMatches(String playerId);
    PlayerStatsResponse        getStats(String playerId);
    List<Map<String, Object>>  getLeaderboard();
}
