package com.vminhkiet.matchmaking_service.controller;

import java.util.Map;

import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.data.redis.core.RedisTemplate;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.security.core.Authentication;
import org.springframework.web.bind.annotation.DeleteMapping;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

import com.vminhkiet.matchmaking_service.model.Match;
import com.vminhkiet.matchmaking_service.service.MatchMakingService;

/**
 * Endpoint matchmaking — được gọi qua API Gateway sau khi JWT đã được xác thực.
 * Header X-User-Id được inject bởi GatewayHeaderAuthFilter từ JWT claims.
 *
 * Unity gọi:  POST http://localhost:8080/api/matchmaking/find
 *             Authorization: Bearer <jwt>
 * Response:   { matchId, serverHost, serverPort, players, createAt }
 */
@RestController
@RequestMapping("/api/matchmaking")
public class MatchmakingController {

    @Autowired
    private MatchMakingService matchMakingService;

    @Autowired
    private RedisTemplate<String, Object> redisTemplate;

    /**
     * Xếp hàng tìm trận và chờ tối đa 30s.
     * Khi đủ 2 người chơi, tạo match và trả địa chỉ UDP của Tank server.
     */
    @PostMapping("/find")
    public ResponseEntity<?> findMatch(Authentication authentication) {
        String userId = authentication.getName();
        try {
            Match match = matchMakingService.findOrCreateMatch(userId);
            return ResponseEntity.ok(match);
        } catch (RuntimeException e) {
            return ResponseEntity.status(HttpStatus.REQUEST_TIMEOUT)
                    .body(Map.of("error", e.getMessage()));
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            return ResponseEntity.status(HttpStatus.INTERNAL_SERVER_ERROR)
                    .body(Map.of("error", "Matchmaking bị gián đoạn"));
        }
    }

    /**
     * Kiểm tra trạng thái xếp hàng của người dùng hiện tại.
     * Trả về: waiting | matched | timeout | not_in_queue
     */
    @GetMapping("/status")
    public ResponseEntity<?> getStatus(Authentication authentication) {
        String userId = authentication.getName();
        Object status = redisTemplate.opsForValue()
                .get("matchmaking:player:" + userId + ":status");
        return ResponseEntity.ok(Map.of(
                "userId", userId,
                "status", status != null ? status : "not_in_queue"
        ));
    }

    /**
     * Hủy xếp hàng — đánh dấu trạng thái "cancelled".
     * Lưu ý: không xóa khỏi Redis list do race-condition; server bỏ qua player cancelled khi dequeue.
     */
    @DeleteMapping("/cancel")
    public ResponseEntity<?> cancel(Authentication authentication) {
        String userId = authentication.getName();
        redisTemplate.opsForValue()
                .set("matchmaking:player:" + userId + ":status", "cancelled");
        return ResponseEntity.ok(Map.of("message", "Đã hủy xếp hàng"));
    }
}
