package com.vminhkiet.matchmaking_service.controller;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.vminhkiet.matchmaking_service.model.WaitingEntry;
import com.vminhkiet.matchmaking_service.service.LobbyManager;
import lombok.extern.slf4j.Slf4j;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.http.ResponseEntity;
import org.springframework.kafka.core.KafkaTemplate;
import org.springframework.security.core.context.SecurityContextHolder;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicInteger;
import org.springframework.http.HttpStatus;

@Slf4j
@RestController
@RequestMapping("/api/matchmaking")
public class MatchmakingController {

    @Autowired
    private KafkaTemplate<String, String> kafkaTemplate;

    @Autowired
    private LobbyManager lobbyManager;

    @Value("${game.server.host:127.0.0.1}")
    private String serverHost;

    @Value("${game.server.port:8080}")
    private int serverPort;

    @Value("${game.kafka.match-topic:match.create}")
    private String matchTopic;

    private static final int MATCH_SIZE = 2;

    private final AtomicInteger matchCounter  = new AtomicInteger(1000);
    private final AtomicInteger playerCounter = new AtomicInteger(1);
    private final ObjectMapper  objectMapper  = new ObjectMapper();
    private final Map<Integer, List<WaitingEntry>> pendingMatches = new ConcurrentHashMap<>();
    private final ScheduledExecutorService scheduler = Executors.newScheduledThreadPool(1);

    @PostMapping("/find")
    public CompletableFuture<ResponseEntity<Map<String, Object>>> findMatch() {
        var auth = SecurityContextHolder.getContext().getAuthentication();
        long userId = parseLong(auth != null ? (String) auth.getPrincipal() : null, 0L);

        CompletableFuture<ResponseEntity<Map<String, Object>>> myFuture = new CompletableFuture<>();
        int myPlayerId = playerCounter.getAndIncrement();
        WaitingEntry myEntry = new WaitingEntry(userId, myPlayerId, myFuture);

        List<WaitingEntry> batch = lobbyManager.addAndTryForm(myEntry, MATCH_SIZE);

        if (batch != null) {
            int matchId = matchCounter.getAndIncrement();
            pendingMatches.put(matchId, batch);
            publishMatch(matchId, batch);
            
            // Timeout after 5 seconds if game server doesn't respond
            scheduler.schedule(() -> {
                List<WaitingEntry> removed = pendingMatches.remove(matchId);
                if (removed != null) {
                    log.warn("[MatchmakingController] Match {} timed out waiting for game server ACK", matchId);
                    for (WaitingEntry e : removed) {
                        e.future().complete(ResponseEntity.status(HttpStatus.SERVICE_UNAVAILABLE)
                                .body(Map.of("error", "Máy chủ trò chơi hiện đang không khả dụng. Vui lòng thử lại sau.")));
                    }
                }
            }, 5, TimeUnit.SECONDS);
        }

        return myFuture;
    }

    private void publishMatch(int matchId, List<WaitingEntry> players) {
        try {
            List<Integer> playerIds = players.stream().map(WaitingEntry::playerId).toList();
            LinkedHashMap<String, String> userIdMap = new LinkedHashMap<>();
            for (WaitingEntry e : players)
                userIdMap.put(String.valueOf(e.playerId()), String.valueOf(e.userId()));

            LinkedHashMap<String, Object> body = new LinkedHashMap<>();
            body.put("matchId",     matchId);
            body.put("mapName",     "world");
            body.put("maxDuration", 300);
            body.put("players",     playerIds);
            body.put("userIds",     userIdMap);
            String payload = objectMapper.writeValueAsString(body);
            kafkaTemplate.send(matchTopic, String.valueOf(matchId), payload);
        } catch (Exception e) {
            log.error("[MatchmakingController] Kafka publish failed for matchId={}: {}", matchId, e.getMessage());
        }
    }

    @org.springframework.kafka.annotation.KafkaListener(topics = "match.ready", groupId = "matchmaking-service")
    public void onMatchReady(String payload) {
        try {
            var root = objectMapper.readTree(payload);
            if (root.has("matchId")) {
                int matchId = root.get("matchId").asInt();
                List<WaitingEntry> batch = pendingMatches.remove(matchId);
                if (batch != null) {
                    log.info("[MatchmakingController] Match {} is ready on game server!", matchId);
                    for (WaitingEntry e : batch) {
                        e.future().complete(ResponseEntity.ok(Map.of(
                                "matchId",    matchId,
                                "serverHost", serverHost,
                                "serverPort", serverPort,
                                "playerId",   e.playerId()
                        )));
                    }
                } else {
                    log.debug("[MatchmakingController] Received match.ready for match {}, but no pending entry found (timeout?)", matchId);
                }
            }
        } catch (Exception e) {
            log.error("[MatchmakingController] Error processing match.ready: {}", e.getMessage());
        }
    }

    private static long parseLong(String s, long def) {
        try { return Long.parseLong(s); } catch (Exception e) { return def; }
    }
}
