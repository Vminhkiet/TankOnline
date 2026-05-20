package com.vminhkiet.matchmaking_service.controller;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.vminhkiet.matchmaking_service.model.WaitingEntry;
import com.vminhkiet.matchmaking_service.service.LobbyManager;
import lombok.extern.slf4j.Slf4j;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.data.redis.core.RedisTemplate;
import org.springframework.http.ResponseEntity;
import org.springframework.kafka.core.KafkaTemplate;
import org.springframework.security.core.context.SecurityContextHolder;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

import java.time.Duration;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.UUID;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.atomic.AtomicInteger;

@Slf4j
@RestController
@RequestMapping("/api/matchmaking")
public class MatchmakingController {

    @Autowired
    private KafkaTemplate<String, String> kafkaTemplate;

    @Autowired
    private LobbyManager lobbyManager;

    @Autowired
    private RedisTemplate<String, Object> redisTemplate;

    @Value("${tank.server.host:127.0.0.1}")
    private String serverHost;

    @Value("${tank.server.udp-port:8080}")
    private int serverPort;

    @Value("${game.kafka.match-topic:match.create}")
    private String matchTopic;

    private static final int MATCH_SIZE = 2;

    private final AtomicInteger matchCounter = new AtomicInteger(1000);
    private final ObjectMapper  objectMapper = new ObjectMapper();

    // Redis key prefixes
    private static final String R_SEARCHING = "matchmaking:searching:";  // TTL 120s
    private static final String R_TOKEN     = "matchmaking:token:";       // TTL 120s
    private static final String R_MATCH     = "matchmaking:match:";       // TTL 600s

    @PostMapping("/find")
    public CompletableFuture<ResponseEntity<Map<String, Object>>> findMatch() {
        var auth = SecurityContextHolder.getContext().getAuthentication();
        long userId = parseLong(auth != null ? (String) auth.getPrincipal() : null, 0L);

        // Redis: đánh dấu user đang tìm trận (overwrite — cùng user tìm lại thì cập nhật TTL)
        redisTemplate.opsForValue().set(R_SEARCHING + userId, "searching", Duration.ofSeconds(120));
        log.info("[Redis] SET {} = searching", R_SEARCHING + userId);

        CompletableFuture<ResponseEntity<Map<String, Object>>> myFuture = new CompletableFuture<>();
        WaitingEntry myEntry = new WaitingEntry(userId, myFuture);

        List<WaitingEntry> batch = lobbyManager.addAndTryForm(myEntry, MATCH_SIZE);

        if (batch != null) {
            int matchId = matchCounter.getAndIncrement();

            // Assign per-match slot IDs: 1, 2, ... (luôn trong range [1..255] của packet format)
            // tokenMap và userIdMap đều key theo slotId (String)
            LinkedHashMap<String, String> tokenMap  = new LinkedHashMap<>();
            LinkedHashMap<String, String> userIdMap = new LinkedHashMap<>();
            List<Integer> slotIds = new java.util.ArrayList<>();

            for (int i = 0; i < batch.size(); i++) {
                int slotId = i + 1;
                WaitingEntry e = batch.get(i);
                slotIds.add(slotId);
                tokenMap.put(String.valueOf(slotId), generateToken());
                userIdMap.put(String.valueOf(slotId), String.valueOf(e.userId()));
            }

            // Redis: xóa searching, lưu token và match info
            for (int i = 0; i < batch.size(); i++) {
                int slotId = i + 1;
                WaitingEntry e = batch.get(i);
                redisTemplate.delete(R_SEARCHING + e.userId());
                String token = tokenMap.get(String.valueOf(slotId));
                redisTemplate.opsForValue().set(
                    R_TOKEN + token,
                    matchId + ":" + slotId,
                    Duration.ofSeconds(120));
                log.info("[Redis] SET {}{}  =  {}:{}", R_TOKEN, token, matchId, slotId);
            }
            redisTemplate.opsForValue().set(
                R_MATCH + matchId,
                batch.stream().map(e -> String.valueOf(e.userId())).reduce((a, b) -> a + "," + b).orElse(""),
                Duration.ofSeconds(600));
            log.info("[Redis] SET {}{}  =  match active", R_MATCH, matchId);

            publishMatch(matchId, slotIds, userIdMap, tokenMap);

            for (int i = 0; i < batch.size(); i++) {
                int slotId = i + 1;
                WaitingEntry e = batch.get(i);
                e.future().complete(ResponseEntity.ok(Map.of(
                        "matchId",    matchId,
                        "serverHost", serverHost,
                        "serverPort", serverPort,
                        "playerId",   slotId,
                        "token",      tokenMap.get(String.valueOf(slotId))
                )));
            }
        }

        return myFuture;
    }

    private void publishMatch(int matchId, List<Integer> slotIds,
                              LinkedHashMap<String, String> userIdMap,
                              LinkedHashMap<String, String> tokenMap) {
        try {
            LinkedHashMap<String, Object> body = new LinkedHashMap<>();
            body.put("matchId",     matchId);
            body.put("mapName",     "world");
            body.put("maxDuration", 300);
            body.put("players",     slotIds);
            body.put("userIds",     userIdMap);
            body.put("tokens",      tokenMap);
            String payload = objectMapper.writeValueAsString(body);
            kafkaTemplate.send(matchTopic, String.valueOf(matchId), payload);
            log.info("[Matchmaking] match {} published → players={} tokens={}",
                     matchId, slotIds, tokenMap.keySet());
        } catch (Exception e) {
            log.error("[MatchmakingController] Kafka publish failed for matchId={}: {}", matchId, e.getMessage());
        }
    }

    private static String generateToken() {
        return UUID.randomUUID().toString().replace("-", "");
    }

    private static long parseLong(String s, long def) {
        try { return Long.parseLong(s); } catch (Exception e) { return def; }
    }
}
