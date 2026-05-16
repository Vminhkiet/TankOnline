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

import com.vminhkiet.matchmaking_service.model.Match;
import com.vminhkiet.matchmaking_service.service.MatchMakingService;

import java.util.List;
import java.util.Map;
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
        DeferredResult<ResponseEntity<Map<String, Object>>> result
    ) {}

    private final AtomicReference<WaitingEntry> waitingSlot  = new AtomicReference<>();
    private final AtomicInteger                 matchCounter  = new AtomicInteger(1000);
    private final AtomicInteger                 playerCounter = new AtomicInteger(1);
    private final ObjectMapper                  objectMapper  = new ObjectMapper();

    @PostMapping("/find")
    public DeferredResult<ResponseEntity<Map<String, Object>>> findMatch() {
        var auth = SecurityContextHolder.getContext().getAuthentication();
        long userId = parseLong(auth != null ? (String) auth.getPrincipal() : null, 0L);

        DeferredResult<ResponseEntity<Map<String, Object>>> myResult = new DeferredResult<>(60_000L);
        int myPlayerId = playerCounter.getAndIncrement();
        WaitingEntry myEntry = new WaitingEntry(userId, myPlayerId, myResult);

        myResult.onTimeout(() -> {
            waitingSlot.compareAndSet(myEntry, null);
            myResult.setErrorResult(
                ResponseEntity.status(408).body(Map.of("error", "Timeout waiting for another player"))
            );
        });

        // Spin until either registered as waiter or paired with existing waiter.
        while (true) {
            WaitingEntry existing = waitingSlot.get();

            if (existing == null) {
                if (waitingSlot.compareAndSet(null, myEntry)) {
                    // Registered as player 1 — response will arrive via player 2's thread.
                    return myResult;
                }
            } else {
                if (waitingSlot.compareAndSet(existing, null)) {
                    // Claimed existing waiter — we are player 2.
                    int matchId = matchCounter.getAndIncrement();
                    publishMatch(matchId, existing.playerId(), myPlayerId);

                    existing.result().setResult(ResponseEntity.ok(Map.of(
                        "matchId",    matchId,
                        "serverHost", serverHost,
                        "serverPort", serverPort,
                        "playerId",   existing.playerId()
                    )));
                    myResult.setResult(ResponseEntity.ok(Map.of(
                        "matchId",    matchId,
                        "serverHost", serverHost,
                        "serverPort", serverPort,
                        "playerId",   myPlayerId
                    )));
                    return myResult;
                }
            }
        }
    }

    private void publishMatch(int matchId, int p1PlayerId, int p2PlayerId) {
        try {
            String payload = objectMapper.writeValueAsString(Map.of(
                "matchId",     matchId,
                "mapName",     "world",
                "maxDuration", 300,
                "players",     List.of(p1PlayerId, p2PlayerId)
            ));
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
