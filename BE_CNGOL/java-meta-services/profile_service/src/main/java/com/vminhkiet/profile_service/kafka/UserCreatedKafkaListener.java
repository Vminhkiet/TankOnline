package com.vminhkiet.profile_service.kafka;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.vminhkiet.profile_service.service.ProfileService;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.kafka.annotation.KafkaListener;
import org.springframework.stereotype.Component;

import java.util.Map;

@Slf4j
@Component
@RequiredArgsConstructor
public class UserCreatedKafkaListener {

    private final ProfileService profileService;
    private final ObjectMapper objectMapper;

    @KafkaListener(topics = "user.created", groupId = "profile-service")
    public void onUserCreated(String message) {
        try {
            Map<String, Object> event = objectMapper.readValue(message, Map.class);
            String userId      = String.valueOf(event.get("userId"));
            String displayName = String.valueOf(event.getOrDefault("displayName", "Player_" + userId));

            profileService.createProfile(userId, displayName);
            log.info("Profile created for userId={}", userId);
        } catch (Exception e) {
            log.error("Failed to process user.created event: {}", e.getMessage());
        }
    }
}
