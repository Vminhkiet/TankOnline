package com.vminhkiet.auth_service.serviceImpl;

import java.time.Instant;

import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.kafka.core.KafkaTemplate;
import org.springframework.stereotype.Service;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.vminhkiet.auth_service.dto.SessionInvalidatedEvent;

@Service
public class SessionInvalidationProducer {

    private static final String TOPIC = "user.session.invalidated";
    private static final int DUPLICATE_LOGIN_CODE = 1003;
    private static final String DUPLICATE_LOGIN_MESSAGE = "Logged in from another device";
    
    private static final int BANNED_LOGIN_CODE = 1004;
    private static final String BANNED_LOGIN_MESSAGE = "Tài khoản của bạn đã bị cấm.";

    @Autowired
    private KafkaTemplate<String, String> kafkaTemplate;

    @Autowired
    private ObjectMapper objectMapper;

    public void publishDuplicateLoginKick(Long userId) {
        SessionInvalidatedEvent event = new SessionInvalidatedEvent(
                userId,
                DUPLICATE_LOGIN_CODE,
                DUPLICATE_LOGIN_MESSAGE,
                Instant.now());

        try {
            String payload = objectMapper.writeValueAsString(event);
            kafkaTemplate.send(TOPIC, String.valueOf(userId), payload);
        } catch (JsonProcessingException e) {
            throw new RuntimeException("Failed to serialize session invalidation event", e);
        }
    }

    public void publishBanKick(Long userId) {
        SessionInvalidatedEvent event = new SessionInvalidatedEvent(
                userId,
                BANNED_LOGIN_CODE,
                BANNED_LOGIN_MESSAGE,
                Instant.now());

        try {
            String payload = objectMapper.writeValueAsString(event);
            kafkaTemplate.send(TOPIC, String.valueOf(userId), payload);
        } catch (JsonProcessingException e) {
            throw new RuntimeException("Failed to serialize session invalidation event for ban", e);
        }
    }
}
