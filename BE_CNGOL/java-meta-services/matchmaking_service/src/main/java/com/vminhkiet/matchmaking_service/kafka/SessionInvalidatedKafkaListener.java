package com.vminhkiet.matchmaking_service.kafka;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.vminhkiet.matchmaking_service.service.LobbyManager;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.kafka.annotation.KafkaListener;
import org.springframework.stereotype.Component;

import java.util.Map;

/**
 * Saga 4 — Session Cleanup (Choreography).
 * Khi auth_service phát hiện đăng nhập trùng và invalidate session,
 * matchmaking_service xóa player đó khỏi lobby để tránh player zombie chờ mãi.
 */
@Slf4j
@Component
@RequiredArgsConstructor
public class SessionInvalidatedKafkaListener {

    private final LobbyManager lobbyManager;
    private final ObjectMapper objectMapper;

    @KafkaListener(topics = "user.session.invalidated", groupId = "matchmaking-service-saga")
    public void onSessionInvalidated(String message) {
        try {
            @SuppressWarnings("unchecked")
            Map<String, Object> event = objectMapper.readValue(message, Map.class);
            long userId = Long.parseLong(String.valueOf(event.get("userId")));
            lobbyManager.removePlayer(userId);
        } catch (Exception e) {
            log.error("[Saga-4] Failed to process user.session.invalidated: {}", e.getMessage());
        }
    }
}
