package com.vminhkiet.history_service.kafka;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.vminhkiet.history_service.model.MatchHistory;
import com.vminhkiet.history_service.model.MatchHistory.MatchResult;
import com.vminhkiet.history_service.repository.MatchHistoryRepository;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.data.redis.core.StringRedisTemplate;
import org.springframework.kafka.annotation.KafkaListener;
import org.springframework.stereotype.Component;

import java.time.Instant;
import java.util.Map;

@Slf4j
@Component
@RequiredArgsConstructor
public class MatchResultKafkaListener {

    public static final String LEADERBOARD_KEY = "leaderboard:kills";

    private final MatchHistoryRepository repo;
    private final ObjectMapper objectMapper;
    private final StringRedisTemplate redisTemplate;

    @KafkaListener(topics = "match.result", groupId = "history-service")
    public void onMatchResult(String message) {
        try {
            MatchResultEvent event = objectMapper.readValue(message, MatchResultEvent.class);
            if ("cheat_void".equalsIgnoreCase(event.getOutcome())) {
                log.warn("Match {} cancelled due to cheat — skipping history save", event.getMatchId());
                return;
            }
            saveForAllPlayers(event);
        } catch (Exception e) {
            log.error("Failed to process match.result event: {}", e.getMessage());
        }
    }

    private void saveForAllPlayers(MatchResultEvent event) {
        if (event.getUserIds() == null || event.getUserIds().isEmpty()) return;

        Map<String, String> userIds  = event.getUserIds();
        Map<String, Integer> kills   = event.getKills();
        Map<String, Integer> deaths  = event.getDeaths();

        String[] playerKeys = userIds.keySet().toArray(new String[0]);

        for (int i = 0; i < playerKeys.length; i++) {
            String pidStr  = playerKeys[i];
            String userId  = userIds.get(pidStr);
            long   pid     = Long.parseLong(pidStr);

            // opponent is the other player (if only 2 players)
            String opponentUserId = "unknown";
            if (playerKeys.length == 2) {
                String oppPidStr = playerKeys[i == 0 ? 1 : 0];
                opponentUserId = userIds.getOrDefault(oppPidStr, "unknown");
            }

            if (repo.existsByMatchIdAndPlayerId(event.getMatchId(), userId)) continue;

            MatchResult result = resolveResult(event, pid);
            int killCount  = kills  != null ? kills.getOrDefault(pidStr, 0) : 0;
            int deathCount = deaths != null ? deaths.getOrDefault(pidStr, 0) : 0;

            MatchHistory h = MatchHistory.builder()
                    .matchId(event.getMatchId())
                    .playerId(userId)
                    .opponentId(opponentUserId)
                    .result(result)
                    .kills(killCount)
                    .deaths(deathCount)
                    .durationSecs((int) event.getDurationSecs())
                    .mapName(event.getMapName() != null ? event.getMapName() : "world")
                    .playedAt(Instant.now())
                    .build();

            repo.save(h);
            // Update leaderboard ZSET — O(log N), no DB query needed on read
            if (killCount > 0) {
                redisTemplate.opsForZSet().incrementScore(LEADERBOARD_KEY, userId, killCount);
            } else {
                // Ensure player exists in ZSET even with 0 kills (adds if absent)
                redisTemplate.opsForZSet().addIfAbsent(LEADERBOARD_KEY, userId, 0);
            }
            log.info("Saved match history: matchId={} userId={} result={} kills={}", event.getMatchId(), userId, result, killCount);
        }
    }

    private MatchResult resolveResult(MatchResultEvent event, long playerId) {
        String outcome = event.getOutcome();
        if ("win".equalsIgnoreCase(outcome)) {
            return playerId == event.getWinnerId() ? MatchResult.WIN : MatchResult.LOSE;
        }
        return MatchResult.DRAW;
    }
}
