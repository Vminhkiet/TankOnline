package com.vminhkiet.matchmaking_service.controller;

import java.util.Map;
import java.util.HashMap;

import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;
import org.springframework.web.context.request.async.DeferredResult;

@RestController
@RequestMapping("/api/matchmaking")
public class MatchmakingController {

    private static DeferredResult<ResponseEntity<Map<String, Object>>> waitingPlayer = null;

    @PostMapping("/find")
    public synchronized DeferredResult<ResponseEntity<Map<String, Object>>> findMatch() {
        // Tạo một HTTP Long-polling request với timeout 60 giây
        DeferredResult<ResponseEntity<Map<String, Object>>> result = new DeferredResult<>(60000L);

        // Xử lý khi người chơi chờ quá lâu (60s)
        result.onTimeout(() -> {
            Map<String, Object> timeoutResponse = new HashMap<>();
            timeoutResponse.put("error", "Timeout waiting for another player");
            result.setErrorResult(ResponseEntity.status(408).body(timeoutResponse));
            if (waitingPlayer == result) {
                waitingPlayer = null;
            }
        });

        if (waitingPlayer == null) {
            // Bạn là người đầu tiên tìm trận -> Lưu lại và Bắt đầu chờ
            waitingPlayer = result;
            System.out.println("Player 1 is waiting for a match...");
        } else {
            // Bạn là người thứ hai -> Đã đủ 2 người, tạo trận!
            System.out.println("Player 2 joined! Match found. Sending response to both players...");
            
            Map<String, Object> response = new HashMap<>();
            // Trả về Match ID và thông tin Dedicated Server C++
            response.put("matchId", 1);
            response.put("serverHost", "127.0.0.1");
            response.put("serverPort", 8080);
            
            ResponseEntity<Map<String, Object>> okResp = ResponseEntity.ok(response);
            
            // Trả kết quả cho người thứ nhất (thoát khỏi trạng thái chờ)
            waitingPlayer.setResult(okResp);
            // Trả kết quả cho người thứ hai
            result.setResult(okResp);
            
            // Reset hàng đợi
            waitingPlayer = null;
        }

        return result;
    }
}
