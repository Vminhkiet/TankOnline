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

import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.atomic.AtomicInteger;

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

    private static final int MATCH_SIZE = 10;

    private final List<WaitingEntry>  lobby         = new ArrayList<>();
    private final AtomicInteger       matchCounter  = new AtomicInteger(1000);
    private final AtomicInteger       playerCounter = new AtomicInteger(1);
    private final ObjectMapper        objectMapper  = new ObjectMapper();

    @PostMapping("/find")
    public CompletableFuture<ResponseEntity<Map<String, Object>>> findMatch() {
        var auth = SecurityContextHolder.getContext().getAuthentication();
        long userId = parseLong(auth != null ? (String) auth.getPrincipal() : null, 0L);

        CompletableFuture<ResponseEntity<Map<String, Object>>> myFuture = new CompletableFuture<>();
        int myPlayerId = playerCounter.getAndIncrement();
        WaitingEntry myEntry = new WaitingEntry(userId, myPlayerId, myFuture);

        List<WaitingEntry> batch = null;
        synchronized (lobby) {
            lobby.add(myEntry);
            if (lobby.size() >= MATCH_SIZE) {
                batch = new ArrayList<>(lobby.subList(0, MATCH_SIZE));
                lobby.subList(0, MATCH_SIZE).clear();
            }
        }

        if (batch != null) {
            int matchId = matchCounter.getAndIncrement();
            publishMatch(matchId, batch);
            for (WaitingEntry e : batch) {
                e.future().complete(ResponseEntity.ok(Map.of(
                    "matchId",    matchId,
                    "serverHost", serverHost,
                    "serverPort", serverPort,
                    "playerId",   e.playerId()
                )));
            }
        }

        return myFuture;
    }

    private void publishMatch(int matchId, List<WaitingEntry> players) {
        try {
            List<Integer> playerIds = players.stream().map(WaitingEntry::playerId).toList();
            java.util.LinkedHashMap<String, String> userIdMap = new java.util.LinkedHashMap<>();
            for (WaitingEntry e : players)
                userIdMap.put(String.valueOf(e.playerId()), String.valueOf(e.userId()));

            Map<String, Object> body = new java.util.LinkedHashMap<>();
            body.put("matchId",     matchId);
            body.put("mapName",     "world");
            body.put("maxDuration", 300);
            body.put("players",     playerIds);
            body.put("userIds",     userIdMap);
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
