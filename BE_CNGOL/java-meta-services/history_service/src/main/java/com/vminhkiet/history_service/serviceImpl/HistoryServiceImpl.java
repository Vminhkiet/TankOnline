package com.vminhkiet.history_service.serviceImpl;

import com.vminhkiet.history_service.dto.LeaderboardEntryResponse;
import com.vminhkiet.history_service.dto.MatchHistoryResponse;
import com.vminhkiet.history_service.dto.PlayerStatsResponse;
import com.vminhkiet.history_service.dto.SaveMatchRequest;
import com.vminhkiet.history_service.model.MatchHistory;
import com.vminhkiet.history_service.model.MatchHistory.MatchResult;
import com.vminhkiet.history_service.repository.MatchHistoryRepository;
import com.vminhkiet.history_service.service.HistoryService;

import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.http.ResponseEntity;
import org.springframework.stereotype.Service;
import org.springframework.web.client.RestTemplate;

import java.time.Instant;
import java.util.*;
import java.util.stream.Collectors;

@Service
public class HistoryServiceImpl implements HistoryService {

    @Autowired
    private MatchHistoryRepository repo;

    private final RestTemplate restTemplate = new RestTemplate();

    @Value("${auth.service.url:http://localhost:8082}")
    private String authServiceUrl;

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
    public List<LeaderboardEntryResponse> getLeaderboard() {
        List<Object[]> rows = repo.findLeaderboard();
        List<LeaderboardEntryResponse> result = new ArrayList<>();

        for (int i = 0; i < rows.size(); i++) {
            Object[] row = rows.get(i);
            String playerId = String.valueOf(row[0]);

            int totalKills = row[1] instanceof Number ? ((Number) row[1]).intValue() : 0;
            int totalMatches = row[2] instanceof Number ? ((Number) row[2]).intValue() : 0;
            int wins = row[3] instanceof Number ? ((Number) row[3]).intValue() : 0;

            result.add(LeaderboardEntryResponse.builder()
                    .rank(i + 1)
                    .playerId(playerId)
                    .username(fetchUsernameByPlayerId(playerId))
                    .totalKills(totalKills)
                    .totalMatches(totalMatches)
                    .wins(wins)
                    .build());
        }

        return result;
    }

    private String fetchUsernameByPlayerId(String playerId) {
        try {
            String url = authServiceUrl + "/api/user/" + playerId;
            ResponseEntity<Map> response = restTemplate.getForEntity(url, Map.class);

            if (response.getBody() != null && response.getBody().get("username") != null) {
                return String.valueOf(response.getBody().get("username"));
            }
        } catch (Exception ignored) {
        }

        return "Player " + playerId;
    }
}
