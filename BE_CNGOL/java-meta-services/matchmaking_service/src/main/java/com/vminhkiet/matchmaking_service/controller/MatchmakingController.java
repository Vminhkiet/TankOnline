package com.vminhkiet.matchmaking_service.controller;

import com.fasterxml.jackson.databind.ObjectMapper;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.http.ResponseEntity;
import org.springframework.kafka.core.KafkaTemplate;
import org.springframework.security.core.context.SecurityContextHolder;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

import java.util.List;
import java.util.Map;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.concurrent.atomic.AtomicReference;

@RestController
@RequestMapping("/api/matchmaking")
public class MatchmakingController {

    @Autowired
    private KafkaTemplate<String, String> kafkaTemplate;

    @Value("${game.server.host:127.0.0.1}")
    private String serverHost;

    @Value("${game.server.port:8080}")
    private int serverPort;

    @Value("${game.kafka.match-topic:match.create}")
    private String matchTopic;

    private record WaitingEntry(
        long userId,
        int playerId,
        CompletableFuture<ResponseEntity<Map<String, Object>>> future
    ) {}

    private final AtomicReference<WaitingEntry> waitingSlot  = new AtomicReference<>();
    private final AtomicInteger                 matchCounter  = new AtomicInteger(1000);
    private final AtomicInteger                 playerCounter = new AtomicInteger(1);
    private final ObjectMapper                  objectMapper  = new ObjectMapper();

    @PostMapping("/find")
    public CompletableFuture<ResponseEntity<Map<String, Object>>> findMatch() {
        var auth = SecurityContextHolder.getContext().getAuthentication();
        long userId = parseLong(auth != null ? (String) auth.getPrincipal() : null, 0L);

        CompletableFuture<ResponseEntity<Map<String, Object>>> myFuture = new CompletableFuture<>();
        int myPlayerId = playerCounter.getAndIncrement();
        WaitingEntry myEntry = new WaitingEntry(userId, myPlayerId, myFuture);

        while (true) {
            WaitingEntry existing = waitingSlot.get();

            if (existing == null) {
                if (waitingSlot.compareAndSet(null, myEntry)) {
                    return myFuture;
                }
            } else {
                if (waitingSlot.compareAndSet(existing, null)) {
                    int matchId = matchCounter.getAndIncrement();
                    publishMatch(matchId, existing.playerId(), existing.userId(), myPlayerId, userId);

                    existing.future().complete(ResponseEntity.ok(Map.of(
                        "matchId",    matchId,
                        "serverHost", serverHost,
                        "serverPort", serverPort,
                        "playerId",   existing.playerId()
                    )));
                    myFuture.complete(ResponseEntity.ok(Map.of(
                        "matchId",    matchId,
                        "serverHost", serverHost,
                        "serverPort", serverPort,
                        "playerId",   myPlayerId
                    )));
                    return myFuture;
                }
            }
        }
    }

    private void publishMatch(int matchId, int p1PlayerId, long p1UserId, int p2PlayerId, long p2UserId) {
        try {
            Map<String, Object> body = new java.util.LinkedHashMap<>();
            body.put("matchId",     matchId);
            body.put("mapName",     "world");
            body.put("maxDuration", 300);
            body.put("players",     List.of(p1PlayerId, p2PlayerId));
            body.put("userIds",     Map.of(
                String.valueOf(p1PlayerId), String.valueOf(p1UserId),
                String.valueOf(p2PlayerId), String.valueOf(p2UserId)
            ));
            String payload = objectMapper.writeValueAsString(body);
            kafkaTemplate.send(matchTopic, String.valueOf(matchId), payload);
        } catch (Exception e) {
            System.err.println("[MatchmakingController] Kafka publish failed for match "
                + matchId + ": " + e.getMessage());
        }
    }

    private static long parseLong(String s, long def) {
        try { return Long.parseLong(s); } catch (Exception e) { return def; }
    }
}
