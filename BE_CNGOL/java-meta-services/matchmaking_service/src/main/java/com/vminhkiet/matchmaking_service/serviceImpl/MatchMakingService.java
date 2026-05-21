package com.vminhkiet.matchmaking_service.serviceImpl;

import java.time.Instant;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.Objects;
import java.util.stream.Collectors;


import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.data.redis.core.RedisTemplate;
import org.springframework.kafka.core.KafkaTemplate;
import org.springframework.stereotype.Service;
import com.fasterxml.jackson.databind.ObjectMapper;

import com.vminhkiet.matchmaking_service.model.Match;
import com.vminhkiet.matchmaking_service.model.Player;

@Service
public class MatchMakingService implements com.vminhkiet.matchmaking_service.service.MatchMakingService {

    private static final Logger log = LoggerFactory.getLogger(MatchMakingService.class);

    // Queue lưu userId dạng String — đơn giản, so sánh chính xác trong Redis
    private static final String QUEUE_KEY           = "matchmaking:queue";
    private static final long   WAIT_FOR_OPPONENT_MS = 10_000L;
    private static final long   POLL_MS              = 500L;

    @Autowired
    private RedisTemplate<String, Object> redisTemplate;

    @Autowired
    private KafkaTemplate<String, String> kafkaTemplate;

    private static final String MATCH_CREATE_TOPIC = "match.create";

    @Value("${tank.server.host:127.0.0.1}")
    private String tankHost;

    @Value("${tank.server.udp-port:8080}")
    private int tankUdpPort;

    // ── Helpers status ────────────────────────────────────────────────────────

    private String statusKey(String userId) {
        return "matchmaking:player:" + userId + ":status";
    }

    private String getStatus(String userId) {
        return Objects.toString(redisTemplate.opsForValue().get(statusKey(userId)), "");
    }

    private void setStatus(String userId, String status) {
        redisTemplate.opsForValue().set(statusKey(userId), status);
    }

    // ── Interface compat — không dùng trong findOrCreateMatch ─────────────────

    @Override
    public void enqueuePlayer(Player player) {
        redisTemplate.opsForList().rightPush(QUEUE_KEY, player.getId());
        setStatus(player.getId(), "waiting");
    }

    @Override
    public Player dequeuePlayer() {
        Object raw = redisTemplate.opsForList().leftPop(QUEUE_KEY);
        return raw != null ? new Player(raw.toString(), 0, "default") : null;
    }

    // ── Match creation ────────────────────────────────────────────────────────

    @Override
    public Match createMatch(List<String> players) {
        Long matchId = redisTemplate.opsForValue().increment("matchmaking:counter", 1);
        if (matchId == null) matchId = 1L;

        Match match = new Match(matchId, players, Instant.now(), tankHost, tankUdpPort);
        redisTemplate.opsForHash().put("matchmaking:match", String.valueOf(matchId), match);
        players.forEach(id -> setStatus(id, "matched"));

        notifyTankServer(matchId, players);
        return match;
    }

    // ── Main entry ────────────────────────────────────────────────────────────

    @Override
    public Match findOrCreateMatch(String userId) throws InterruptedException {
        // Xóa mọi entry cũ của userId khỏi queue trước khi thêm lại
        // (tránh A vs A khi cùng user tìm trận nhiều lần liên tiếp)
        redisTemplate.opsForList().remove(QUEUE_KEY, 0, userId);
        setStatus(userId, "waiting");
        redisTemplate.opsForList().rightPush(QUEUE_KEY, userId);

        long deadline = System.currentTimeMillis() + WAIT_FOR_OPPONENT_MS;
        while (System.currentTimeMillis() < deadline) {
            Long size = redisTemplate.opsForList().size(QUEUE_KEY);
            if (size != null && size >= 2) {
                Match match = tryFormMatch(userId);
                if (match != null) return match;
            }
            Thread.sleep(POLL_MS);
        }

        // Timeout: đánh dấu để stale entry bị bỏ qua khi bị dequeue sau này
        setStatus(userId, "bot_pending");
        log.info("No opponent for {} after {}ms — bot match", userId, WAIT_FOR_OPPONENT_MS);
        return createMatch(List.of(userId, "bot-1"));
    }

    // ── Tìm 2 player "waiting" từ queue, bỏ qua stale entries ───────────────

    private Match tryFormMatch(String requestingUserId) {
        List<String> validPlayers = new ArrayList<>();

        // Duyệt tối đa 10 entry để lọc bỏ stale
        for (int attempt = 0; attempt < 10 && validPlayers.size() < 2; attempt++) {
            Object raw = redisTemplate.opsForList().leftPop(QUEUE_KEY);
            if (raw == null) break;

            String pid = raw.toString();
            if ("waiting".equals(getStatus(pid)) && !validPlayers.contains(pid)) {
                validPlayers.add(pid);
            } else {
                log.debug("Skipped stale queue entry: {} (status={})", pid, getStatus(pid));
            }
        }

        if (validPlayers.size() == 2) {
            log.info("Match formed: {} vs {}", validPlayers.get(0), validPlayers.get(1));
            return createMatch(validPlayers);
        }

        // Không đủ 2 player hợp lệ — đẩy lại vào đầu queue
        for (int i = validPlayers.size() - 1; i >= 0; i--) {
            redisTemplate.opsForList().leftPush(QUEUE_KEY, validPlayers.get(i));
        }
        return null;
    }

    // ── Notify Tank server via Kafka match.create ─────────────────────────────

    private void notifyTankServer(long matchId, List<String> playerStrIds) {
        try {
            // userId từ auth service là username string (vd: "player1"), không phải số nguyên.
            // Integer.parseInt("player1") → NumberFormatException → bị filter → players=[] rỗng
            // → server nhận empty playerIds → resolvePlayer luôn fail → không spawn tank.
            //
            // Fix: dùng sequential slot ID [1, 2, ..., N] cho game server.
            // Server chỉ cần int để quản lý session trong match, không cần trùng với DB userId.
            // Client nhận localPlayerId từ snapshot header → tự cập nhật m_MyPlayerId đúng.
            // userIds map (slot→username) được gửi kèm để server đính kèm vào match.result.
            List<Integer> playerIntIds = new ArrayList<>();
            Map<String, String> userIdMap = new HashMap<>();
            for (int i = 0; i < playerStrIds.size(); i++) {
                int slotId = i + 1;          // slot 0 → playerId 1, slot 1 → playerId 2, ...
                playerIntIds.add(slotId);
                userIdMap.put(String.valueOf(slotId), playerStrIds.get(i));
            }

            Map<String, Object> bodyMap = new HashMap<>();
            bodyMap.put("matchId",     (int) matchId);
            bodyMap.put("players",     playerIntIds);
            bodyMap.put("userIds",     userIdMap);   // {1:"player1", 2:"player2"} cho match.result
            bodyMap.put("mapName",     "world");
            bodyMap.put("maxDuration", 180);

            String json = new ObjectMapper().writeValueAsString(bodyMap);
            kafkaTemplate.send(MATCH_CREATE_TOPIC, json);
            log.info("Published match.create matchId={} players={} userIds={}", matchId, playerIntIds, userIdMap);
        } catch (Exception e) {
            log.warn("Failed to publish match.create to Kafka: {}", e.getMessage());
        }
    }
}
