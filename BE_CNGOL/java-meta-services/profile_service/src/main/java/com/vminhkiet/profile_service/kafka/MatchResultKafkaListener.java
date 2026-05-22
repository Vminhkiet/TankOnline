package com.vminhkiet.profile_service.kafka;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.vminhkiet.profile_service.service.ProfileService;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.kafka.annotation.KafkaListener;
import org.springframework.stereotype.Component;

import java.util.Map;
import java.util.Set;

/**
 * Saga 3 — Match Result (Choreography).
 * Lắng nghe match.result, cộng coins thưởng cho người thắng và người thua/hòa.
 * Dùng group-id khác với history-service để nhận được bản sao độc lập của event.
 */
@Slf4j
@Component
@RequiredArgsConstructor
public class MatchResultKafkaListener {

    private static final long COINS_WIN         = 25L;
    private static final long COINS_DRAW        = 25L;
    private static final long COINS_LOSE        = 25L;

    private final ProfileService profileService;
    private final ObjectMapper objectMapper;

    @KafkaListener(topics = "match.result", groupId = "profile-service-match")
    public void onMatchResult(String message) {
        try {
            @SuppressWarnings("unchecked")
            Map<String, Object> event = objectMapper.readValue(message, Map.class);

            String outcome  = String.valueOf(event.getOrDefault("outcome", "draw"));
            long winnerId   = parseLong(event.get("winnerId"));
            long matchId    = parseLong(event.get("matchId"));

            @SuppressWarnings("unchecked")
            Map<String, String> userIds = (Map<String, String>) event.get("userIds");

            if (userIds == null || userIds.isEmpty()) {
                log.warn("[Saga-3] match.result matchId={} has no userIds, skipping coin reward", matchId);
                return;
            }

            Set<String> alreadyRewarded = new java.util.HashSet<>();

            @SuppressWarnings("unchecked")
            Map<String, Object> stats = (Map<String, Object>) event.get("stats");

            for (Map.Entry<String, String> entry : userIds.entrySet()) {
                long   playerId = parseLong(entry.getKey());
                String userId   = entry.getValue();

                if (userId == null || userId.isBlank() || alreadyRewarded.contains(userId)) continue;

                // 1. Add Coins
                long reward = resolveReward(outcome, playerId, winnerId);
                profileService.addCoins(userId, reward);
                
                // 2. Add RP
                int rpReward = 0;
                if (stats != null && stats.containsKey(entry.getKey())) {
                    @SuppressWarnings("unchecked")
                    Map<String, Object> playerStats = (Map<String, Object>) stats.get(entry.getKey());
                    if (playerStats != null && playerStats.containsKey("rp_reward")) {
                        rpReward = Integer.parseInt(String.valueOf(playerStats.get("rp_reward")));
                        profileService.addRp(userId, rpReward);
                    }
                }

                alreadyRewarded.add(userId);

                log.info("[Saga-3] Rewarded {} coins, {} RP to userId={} (matchId={}, outcome={}, playerId={})",
                        reward, rpReward, userId, matchId, outcome, playerId);
            }
        } catch (Exception e) {
            log.error("[Saga-3] Failed to process match.result: {}", e.getMessage());
        }
    }

    private long resolveReward(String outcome, long playerId, long winnerId) {
        return switch (outcome.toLowerCase()) {
            case "win"  -> playerId == winnerId ? COINS_WIN : COINS_LOSE;
            case "draw", "timeout" -> COINS_DRAW;
            default     -> COINS_LOSE;
        };
    }

    private long parseLong(Object value) {
        try { return Long.parseLong(String.valueOf(value)); }
        catch (Exception e) { return 0L; }
    }
}
