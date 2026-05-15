package com.vminhkiet.matchmaking_service.controller;

import java.util.List;
import java.util.Map;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.TimeUnit;

import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.data.redis.core.RedisTemplate;
import org.springframework.http.ResponseEntity;
import org.springframework.security.core.Authentication;
import org.springframework.web.bind.annotation.DeleteMapping;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

import com.vminhkiet.matchmaking_service.model.Match;
import com.vminhkiet.matchmaking_service.service.MatchMakingService;

@RestController
@RequestMapping("/api/matchmaking")
public class MatchmakingController {

    @Autowired
    private MatchMakingService matchMakingService;

    @Autowired
    private RedisTemplate<String, Object> redisTemplate;

    // Player 1 waiting slot
    private static volatile CompletableFuture<ResponseEntity<Map<String, Object>>> waitingPlayer = null;
    private static volatile String waitingUserId = null;

    @PostMapping("/find")
    public synchronized CompletableFuture<ResponseEntity<Map<String, Object>>> findMatch(
            Authentication authentication) {

        String userId = authentication != null ? authentication.getName() : "anonymous";

        CompletableFuture<ResponseEntity<Map<String, Object>>> future = new CompletableFuture<>();

        // Auto-complete with bot match after 30s if no opponent found
        future.completeOnTimeout(
            createMatchResponse(List.of(userId, "bot-1")),
            30, TimeUnit.SECONDS
        ).whenComplete((res, ex) -> {
            synchronized (MatchmakingController.class) {
                if (waitingPlayer == future) {
                    waitingPlayer = null;
                    waitingUserId = null;
                }
            }
        });

        if (waitingPlayer == null) {
            // First player — wait for opponent
            waitingPlayer = future;
            waitingUserId = userId;
        } else {
            // Second player — create match for both
            String opponent = waitingUserId;
            ResponseEntity<Map<String, Object>> resp = createMatchResponse(List.of(opponent, userId));

            waitingPlayer.complete(resp);
            future.complete(resp);
            waitingPlayer = null;
            waitingUserId = null;
        }

        return future;
    }

    @GetMapping("/status")
    public ResponseEntity<Map<String, Object>> getStatus(Authentication authentication) {
        String userId = authentication != null ? authentication.getName() : "anonymous";
        Object status = redisTemplate.opsForValue()
                .get("matchmaking:player:" + userId + ":status");
        return ResponseEntity.ok(Map.of(
                "userId",    userId,
                "status",    status != null ? status : "not_in_queue",
                "queueSize", waitingPlayer != null ? 1 : 0
        ));
    }

    @DeleteMapping("/cancel")
    public synchronized ResponseEntity<Map<String, Object>> cancel() {
        if (waitingPlayer != null) {
            waitingPlayer.cancel(true);
            waitingPlayer = null;
            waitingUserId = null;
        }
        return ResponseEntity.ok(Map.of("message", "Cancelled"));
    }

    // Calls service.createMatch() which notifies Tank server via HTTP port 9090
    private ResponseEntity<Map<String, Object>> createMatchResponse(List<String> players) {
        Match match = matchMakingService.createMatch(players);
        return ResponseEntity.ok(Map.of(
                "matchId",    match.getMatchId(),
                "serverHost", match.getServerHost(),
                "serverPort", match.getServerPort()
        ));
    }
}
