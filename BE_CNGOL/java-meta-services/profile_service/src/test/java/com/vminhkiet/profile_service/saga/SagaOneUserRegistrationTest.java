package com.vminhkiet.profile_service.saga;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.vminhkiet.profile_service.kafka.UserCreatedKafkaListener;
import com.vminhkiet.profile_service.service.ProfileService;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.extension.ExtendWith;
import org.mockito.ArgumentCaptor;
import org.mockito.Mock;
import org.mockito.junit.jupiter.MockitoExtension;
import org.springframework.kafka.core.KafkaTemplate;

import java.util.Map;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.ArgumentMatchers.eq;
import static org.mockito.Mockito.*;

/**
 * Saga 1 — User Registration (Choreography).
 * Kiểm tra profile_service: nếu createProfile lỗi → publish user.profile.failed.
 */
@ExtendWith(MockitoExtension.class)
@DisplayName("Saga 1: User Registration – profile_service compensation")
class SagaOneUserRegistrationTest {

    @Mock ProfileService profileService;
    @Mock KafkaTemplate<String, String> kafkaTemplate;

    private UserCreatedKafkaListener listener;
    private final ObjectMapper objectMapper = new ObjectMapper();

    @BeforeEach
    void setUp() {
        listener = new UserCreatedKafkaListener(profileService, objectMapper, kafkaTemplate);
    }

    @Test
    @DisplayName("Khi tạo profile thành công → KHÔNG publish user.profile.failed")
    void whenProfileCreatedSuccessfully_thenNoFailureEventPublished() throws Exception {
        String message = objectMapper.writeValueAsString(
                Map.of("userId", "42", "displayName", "alice"));

        listener.onUserCreated(message);

        verify(profileService).createProfile("42", "alice");
        verify(kafkaTemplate, never()).send(eq("user.profile.failed"), any(), any());
    }

    @Test
    @DisplayName("Khi createProfile ném exception → publish user.profile.failed với đúng userId")
    void whenProfileCreationFails_thenFailureEventPublished() throws Exception {
        String message = objectMapper.writeValueAsString(
                Map.of("userId", "99", "displayName", "bob"));

        doThrow(new RuntimeException("DB connection refused"))
                .when(profileService).createProfile(eq("99"), any());

        listener.onUserCreated(message);

        ArgumentCaptor<String> payloadCaptor = ArgumentCaptor.forClass(String.class);
        verify(kafkaTemplate).send(eq("user.profile.failed"), eq("99"), payloadCaptor.capture());

        @SuppressWarnings("unchecked")
        Map<String, Object> published = objectMapper.readValue(payloadCaptor.getValue(), Map.class);
        assertThat(published.get("userId")).isEqualTo("99");
        assertThat(published).containsKey("reason");
    }

    @Test
    @DisplayName("Khi message JSON bị lỗi → không crash, không publish event")
    void whenInvalidJson_thenNoEventPublished() {
        listener.onUserCreated("{ invalid json }");

        verify(kafkaTemplate, never()).send(any(), any(), any());
    }
}
