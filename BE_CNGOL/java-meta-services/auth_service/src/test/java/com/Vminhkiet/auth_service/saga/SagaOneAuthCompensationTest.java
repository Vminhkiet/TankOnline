package com.Vminhkiet.auth_service.saga;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.vminhkiet.auth_service.kafka.UserProfileFailedKafkaListener;
import com.vminhkiet.auth_service.repository.UserRepository;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.extension.ExtendWith;
import org.mockito.Mock;
import org.mockito.junit.jupiter.MockitoExtension;

import java.util.Map;

import static org.mockito.Mockito.*;

/**
 * Saga 1 — User Registration (Choreography).
 * Kiểm tra auth_service: khi nhận user.profile.failed → xóa user mồ côi.
 */
@ExtendWith(MockitoExtension.class)
@DisplayName("Saga 1: User Registration – auth_service compensation (delete orphan user)")
class SagaOneAuthCompensationTest {

    @Mock UserRepository userRepository;

    private UserProfileFailedKafkaListener listener;
    private final ObjectMapper objectMapper = new ObjectMapper();

    @BeforeEach
    void setUp() {
        listener = new UserProfileFailedKafkaListener(userRepository, objectMapper);
    }

    @Test
    @DisplayName("Khi user tồn tại và profile tạo thất bại → xóa user (compensation)")
    void whenUserExistsAndProfileFailed_thenUserDeleted() throws Exception {
        String message = objectMapper.writeValueAsString(
                Map.of("userId", "42", "reason", "DB connection refused"));

        when(userRepository.existsById(42L)).thenReturn(true);

        listener.onUserProfileFailed(message);

        verify(userRepository).existsById(42L);
        verify(userRepository).deleteById(42L);
    }

    @Test
    @DisplayName("Khi user không tồn tại → không gọi deleteById (idempotent)")
    void whenUserNotFound_thenNoDeleteCalled() throws Exception {
        String message = objectMapper.writeValueAsString(
                Map.of("userId", "99", "reason", "already deleted"));

        when(userRepository.existsById(99L)).thenReturn(false);

        listener.onUserProfileFailed(message);

        verify(userRepository).existsById(99L);
        verify(userRepository, never()).deleteById(any());
    }

    @Test
    @DisplayName("Khi JSON không hợp lệ → không crash, không xóa gì")
    void whenInvalidJson_thenNoAction() {
        listener.onUserProfileFailed("not-json");

        verify(userRepository, never()).deleteById(any());
    }

    @Test
    @DisplayName("Khi userId không phải số → không crash")
    void whenUserIdIsNotNumeric_thenNoAction() throws Exception {
        String message = objectMapper.writeValueAsString(
                Map.of("userId", "abc", "reason", "bad id"));

        listener.onUserProfileFailed(message);

        verify(userRepository, never()).deleteById(any());
    }
}
