package com.vminhkiet.matchmaking_service.saga;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.vminhkiet.matchmaking_service.kafka.SessionInvalidatedKafkaListener;
import com.vminhkiet.matchmaking_service.model.WaitingEntry;
import com.vminhkiet.matchmaking_service.service.LobbyManager;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Nested;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.extension.ExtendWith;
import org.mockito.Mock;
import org.mockito.junit.jupiter.MockitoExtension;
import org.springframework.http.ResponseEntity;

import java.util.List;
import java.util.Map;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.ExecutionException;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;
import static org.mockito.ArgumentMatchers.anyLong;
import static org.mockito.Mockito.*;

/**
 * Saga 4 — Session Cleanup (Choreography).
 * Kiểm tra LobbyManager và SessionInvalidatedKafkaListener.
 */
@ExtendWith(MockitoExtension.class)
@DisplayName("Saga 4: Session Cleanup – xóa player khỏi lobby khi logout")
class SagaFourSessionCleanupTest {

    // ── LobbyManager (thật, không phải mock) ──────────────────────────────────
    private LobbyManager lobbyManager;

    // ── Dùng cho phần Listener tests ─────────────────────────────────────────
    @Mock LobbyManager lobbyManagerMock;

    private final ObjectMapper objectMapper = new ObjectMapper();

    @BeforeEach
    void setUp() {
        lobbyManager = new LobbyManager(); // real instance cho LobbyManager tests
    }

    // ═════════════════════════════════════════════════════════════════════════
    // LobbyManager tests
    // ═════════════════════════════════════════════════════════════════════════

    @Nested
    @DisplayName("LobbyManager — quản lý lobby")
    class LobbyManagerTests {

        @Test
        @DisplayName("Khi chỉ 1 player → chưa đủ, trả về null (chờ thêm)")
        void whenOnePlayerAdded_thenNoBatchFormed() {
            WaitingEntry e = new WaitingEntry(101L, 1, new CompletableFuture<>());

            assertThat(lobbyManager.addAndTryForm(e, 2)).isNull();
            assertThat(lobbyManager.size()).isEqualTo(1);
        }

        @Test
        @DisplayName("Khi 2 player → batch trả về, lobby rỗng")
        void whenTwoPlayersAdded_thenBatchFormedAndLobbyCleared() {
            WaitingEntry e1 = new WaitingEntry(101L, 1, new CompletableFuture<>());
            WaitingEntry e2 = new WaitingEntry(102L, 2, new CompletableFuture<>());

            assertThat(lobbyManager.addAndTryForm(e1, 2)).isNull();
            List<WaitingEntry> batch = lobbyManager.addAndTryForm(e2, 2);

            assertThat(batch).hasSize(2);
            assertThat(batch).extracting(WaitingEntry::userId).containsExactly(101L, 102L);
            assertThat(lobbyManager.size()).isEqualTo(0);
        }

        @Test
        @DisplayName("removePlayer → future completed exceptionally, lobby rỗng")
        void whenPlayerRemoved_thenFutureCompletedExceptionally() throws Exception {
            CompletableFuture<ResponseEntity<Map<String, Object>>> future = new CompletableFuture<>();
            lobbyManager.addAndTryForm(new WaitingEntry(101L, 1, future), 2);

            lobbyManager.removePlayer(101L);

            assertThat(future.isCompletedExceptionally()).isTrue();
            assertThat(lobbyManager.size()).isEqualTo(0);
            assertThatThrownBy(future::get)
                    .isInstanceOf(ExecutionException.class)
                    .hasMessageContaining("Session invalidated");
        }

        @Test
        @DisplayName("removePlayer userId không tồn tại → không crash, lobby không đổi")
        void whenRemoveNonExistentPlayer_thenNoEffect() {
            lobbyManager.addAndTryForm(new WaitingEntry(999L, 1, new CompletableFuture<>()), 2);

            lobbyManager.removePlayer(888L);

            assertThat(lobbyManager.size()).isEqualTo(1);
        }

        @Test
        @DisplayName("removePlayer chỉ xóa đúng player, không ảnh hưởng player khác")
        void whenRemoveOnePlayer_thenOtherPlayersRemain() {
            CompletableFuture<ResponseEntity<Map<String, Object>>> f1 = new CompletableFuture<>();
            CompletableFuture<ResponseEntity<Map<String, Object>>> f2 = new CompletableFuture<>();
            lobbyManager.addAndTryForm(new WaitingEntry(101L, 1, f1), 3);
            lobbyManager.addAndTryForm(new WaitingEntry(102L, 2, f2), 3);

            lobbyManager.removePlayer(101L);

            assertThat(lobbyManager.size()).isEqualTo(1);
            assertThat(f1.isCompletedExceptionally()).isTrue();
            assertThat(f2.isDone()).isFalse();
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // SessionInvalidatedKafkaListener tests
    // ═════════════════════════════════════════════════════════════════════════

    @Nested
    @DisplayName("SessionInvalidatedKafkaListener — nhận event và xóa player")
    class ListenerTests {

        private SessionInvalidatedKafkaListener listener;

        @BeforeEach
        void setUpListener() {
            listener = new SessionInvalidatedKafkaListener(lobbyManagerMock, objectMapper);
        }

        @Test
        @DisplayName("Khi nhận user.session.invalidated → gọi removePlayer với đúng userId")
        void whenSessionInvalidated_thenRemovePlayerCalled() throws Exception {
            String message = objectMapper.writeValueAsString(Map.of(
                    "userId", 42L, "code", 1003,
                    "message", "Logged in from another device"));

            listener.onSessionInvalidated(message);

            verify(lobbyManagerMock).removePlayer(42L);
        }

        @Test
        @DisplayName("JSON không hợp lệ → không crash, không gọi removePlayer")
        void whenInvalidJson_thenNoAction() {
            listener.onSessionInvalidated("{ bad json }");

            verify(lobbyManagerMock, never()).removePlayer(anyLong());
        }

        @Test
        @DisplayName("userId không phải số → không crash")
        void whenUserIdNonNumeric_thenNoAction() throws Exception {
            String message = objectMapper.writeValueAsString(
                    Map.of("userId", "not-a-number", "code", 1003));

            listener.onSessionInvalidated(message);

            verify(lobbyManagerMock, never()).removePlayer(anyLong());
        }
    }
}
