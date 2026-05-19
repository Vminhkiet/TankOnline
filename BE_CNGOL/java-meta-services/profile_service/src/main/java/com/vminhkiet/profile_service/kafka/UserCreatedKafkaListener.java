package com.vminhkiet.profile_service.kafka;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.vminhkiet.profile_service.service.ProfileService;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.kafka.annotation.KafkaListener;
import org.springframework.kafka.core.KafkaTemplate;
import org.springframework.stereotype.Component;

import java.util.Map;

@Slf4j
@Component
@RequiredArgsConstructor
public class UserCreatedKafkaListener {

    private final ProfileService profileService;
    private final ObjectMapper objectMapper;
    private final KafkaTemplate<String, String> kafkaTemplate;

    @KafkaListener(topics = "user.created", groupId = "profile-service")
    public void onUserCreated(String message) {
        String userId = null;
        try {
            Map<String, Object> event = objectMapper.readValue(message, Map.class);
            userId      = String.valueOf(event.get("userId"));
            String displayName = String.valueOf(event.getOrDefault("displayName", "Player_" + userId));

            profileService.createProfile(userId, displayName);
            log.info("[Saga-1] Profile created for userId={}", userId);
        } catch (Exception e) {
            log.error("[Saga-1] Failed to create profile for userId={}: {}", userId, e.getMessage());
            // Compensation: thông báo cho auth_service xóa user
            if (userId != null) {
                publishProfileFailed(userId, e.getMessage());
            }
        }
    }

    private void publishProfileFailed(String userId, String reason) {
        try {
            String payload = objectMapper.writeValueAsString(Map.of(
                    "userId", userId,
                    "reason", reason != null ? reason : "unknown"
            ));
            kafkaTemplate.send("user.profile.failed", userId, payload);
            log.warn("[Saga-1] Published user.profile.failed for userId={}", userId);
        } catch (Exception ex) {
            log.error("[Saga-1] Could not publish user.profile.failed for userId={}: {}", userId, ex.getMessage());
        }
    }
}
