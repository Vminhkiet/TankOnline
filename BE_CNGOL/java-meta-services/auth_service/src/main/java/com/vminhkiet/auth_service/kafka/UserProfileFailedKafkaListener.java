package com.vminhkiet.auth_service.kafka;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.vminhkiet.auth_service.repository.UserRepository;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.kafka.annotation.KafkaListener;
import org.springframework.stereotype.Component;

import java.util.Map;

/**
 * Saga 1 — User Registration (Choreography).
 * Compensation step: nếu profile_service không tạo được profile,
 * auth_service xóa user vừa tạo để tránh tài khoản "mồ côi".
 */
@Slf4j
@Component
@RequiredArgsConstructor
public class UserProfileFailedKafkaListener {

    private final UserRepository userRepository;
    private final ObjectMapper objectMapper;

    @KafkaListener(topics = "user.profile.failed", groupId = "auth-service-saga")
    public void onUserProfileFailed(String message) {
        try {
            @SuppressWarnings("unchecked")
            Map<String, Object> event = objectMapper.readValue(message, Map.class);
            String userIdStr = String.valueOf(event.get("userId"));
            Long userId = Long.parseLong(userIdStr);

            if (userRepository.existsById(userId)) {
                userRepository.deleteById(userId);
                log.warn("[Saga-1 Compensation] Deleted orphan user userId={} — profile creation failed", userId);
            } else {
                log.info("[Saga-1 Compensation] User userId={} not found, nothing to delete", userId);
            }
        } catch (Exception e) {
            log.error("[Saga-1 Compensation] Failed to process user.profile.failed: {}", e.getMessage());
        }
    }
}
