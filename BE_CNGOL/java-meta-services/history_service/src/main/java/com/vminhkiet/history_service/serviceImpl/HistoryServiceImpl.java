package com.vminhkiet.history_service.serviceImpl;

import com.vminhkiet.history_service.dto.LeaderboardEntryResponse;
import com.vminhkiet.history_service.dto.MatchHistoryResponse;
import com.vminhkiet.history_service.dto.PlayerStatsResponse;
import com.vminhkiet.history_service.dto.SaveMatchRequest;
import com.vminhkiet.history_service.kafka.MatchResultKafkaListener;
import com.vminhkiet.history_service.model.MatchHistory;
import com.vminhkiet.history_service.model.MatchHistory.MatchResult;
import com.vminhkiet.history_service.repository.MatchHistoryRepository;
import com.vminhkiet.history_service.service.HistoryService;

import jakarta.annotation.PostConstruct;
import lombok.extern.slf4j.Slf4j;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.data.redis.core.StringRedisTemplate;
import org.springframework.data.redis.core.ZSetOperations;
import org.springframework.http.ResponseEntity;
import org.springframework.stereotype.Service;
import org.springframework.web.client.RestTemplate;

import java.time.Instant;
import java.util.*;
import java.util.stream.Collectors;

@Slf4j
@Service
public class HistoryServiceImpl implements HistoryService {

    @Autowired
    private MatchHistoryRepository repo;

    private final RestTemplate restTemplate = new RestTemplate();

    @Value("${auth.service.url:http://localhost:8082}")
    private String authServiceUrl;

    @Autowired
    private StringRedisTemplate redisTemplate;

    // Rebuild leaderboard ZSET from DB on startup (handles Redis restart / first boot)
    @PostConstruct
    public void rebuildLeaderboard() {
        try {
            Boolean hasData = redisTemplate.hasKey(MatchResultKafkaListener.LEADERBOARD_KEY);
            if (Boolean.TRUE.equals(hasData)) {
                log.info("Leaderboard ZSET already exists in Redis — skipping rebuild");
                return;
            }
            List<Object[]> rows = repo.findLeaderboard();
            if (rows.isEmpty()) return;
            for (Object[] row : rows) {
                String playerId  = (String) row[0];
                double totalKills = ((Number) row[1]).doubleValue();
                redisTemplate.opsForZSet().add(
                        MatchResultKafkaListener.LEADERBOARD_KEY, playerId, totalKills);
            }
            log.info("Rebuilt leaderboard ZSET from DB: {} players", rows.size());
        } catch (Exception e) {
            log.warn("Could not rebuild leaderboard ZSET from DB: {}", e.getMessage());
        }
    }

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
        // 1. Thử lấy top 10 từ Redis trước (nhanh)
        try {
            Set<ZSetOperations.TypedTuple<String>> top10 = redisTemplate.opsForZSet()
                    .reverseRangeWithScores(MatchResultKafkaListener.LEADERBOARD_KEY, 0, 9);

            if (top10 != null && !top10.isEmpty()) {
                List<LeaderboardEntryResponse> result = new ArrayList<>();
                int rank = 1;
                for (ZSetOperations.TypedTuple<String> t : top10) {
                    String playerId = t.getValue();
                    int totalKills = t.getScore() != null ? t.getScore().intValue() : 0;

                    // Bổ sung totalMatches và wins từ DB cho player này
                    List<MatchHistory> playerMatches = repo.findByPlayerIdOrderByPlayedAtDesc(playerId);
                    int totalMatches = playerMatches.size();
                    int wins = (int) playerMatches.stream()
                            .filter(m -> m.getResult() == MatchResult.WIN).count();

                    result.add(LeaderboardEntryResponse.builder()
                            .rank(rank++)
                            .playerId(playerId)
                            .username(fetchUsernameByPlayerId(playerId))
                            .totalKills(totalKills)
                            .totalMatches(totalMatches)
                            .wins(wins)
                            .build());
                }
                return result;
            }
        } catch (Exception e) {
            log.warn("Redis unavailable, falling back to DB for leaderboard: {}", e.getMessage());
        }

        // 2. Fallback: query DB trực tiếp
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
