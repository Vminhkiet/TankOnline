package com.vminhkiet.history_service.serviceImpl;

import com.vminhkiet.history_service.dto.MatchHistoryResponse;
import com.vminhkiet.history_service.dto.PlayerStatsResponse;
import com.vminhkiet.history_service.dto.SaveMatchRequest;
import com.vminhkiet.history_service.model.MatchHistory;
import com.vminhkiet.history_service.model.MatchHistory.MatchResult;
import com.vminhkiet.history_service.repository.MatchHistoryRepository;
import com.vminhkiet.history_service.service.HistoryService;

import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.stereotype.Service;

import java.time.Instant;
import java.util.*;
import java.util.stream.Collectors;

@Service
public class HistoryServiceImpl implements HistoryService {

    @Autowired
    private MatchHistoryRepository repo;

    @Override
    public void saveMatch(String playerId, SaveMatchRequest req) {
        // Tranh luu trung lap
        if (repo.existsByMatchIdAndPlayerId(req.getMatchId(), playerId)) return;

        MatchResult result = MatchResult.valueOf(req.getResult().toUpperCase());

        MatchHistory h = MatchHistory.builder()
                .matchId(req.getMatchId())
                .playerId(playerId)
                .opponentId(req.getOpponentId() != null ? req.getOpponentId() : "unknown")
                .result(result)
                .kills(req.getKills())
                .deaths(req.getDeaths())
                .durationSecs(req.getDurationSecs())
                .mapName(req.getMapName() != null ? req.getMapName() : "world")
                .playedAt(Instant.now())
                .build();

        repo.save(h);
    }

    @Override
    public List<MatchHistoryResponse> getRecentMatches(String playerId) {
        return repo.findTop10ByPlayerIdOrderByPlayedAtDesc(playerId)
                   .stream()
                   .map(MatchHistoryResponse::from)
                   .collect(Collectors.toList());
    }

    @Override
    public PlayerStatsResponse getStats(String playerId) {
        List<MatchHistory> all = repo.findByPlayerIdOrderByPlayedAtDesc(playerId);

        int wins   = 0, losses = 0, draws = 0;
        int kills  = 0, deaths = 0, bestStreak = 0;

        for (MatchHistory h : all) {
            switch (h.getResult()) {
                case WIN  -> wins++;
                case LOSE -> losses++;
                case DRAW -> draws++;
            }
            kills  += h.getKills();
            deaths += h.getDeaths();
            bestStreak = Math.max(bestStreak, h.getKills());
        }

        int total   = all.size();
        double rate = total > 0 ? (double) wins / total : 0.0;

        return new PlayerStatsResponse(playerId, total, wins, losses, draws,
                                       kills, deaths, rate, bestStreak);
    }

    @Override
    public List<Map<String, Object>> getLeaderboard() {
        return repo.findLeaderboard().stream().map(row -> {
            Map<String, Object> entry = new LinkedHashMap<>();
            entry.put("playerId",     row[0]);
            entry.put("totalKills",   row[1]);
            entry.put("totalMatches", row[2]);
            entry.put("wins",         row[3]);
            return entry;
        }).collect(Collectors.toList());
    }
}
