package com.vminhkiet.profile_service.saga;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.vminhkiet.profile_service.kafka.MatchResultKafkaListener;
import com.vminhkiet.profile_service.service.ProfileService;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.extension.ExtendWith;
import org.mockito.Mock;
import org.mockito.junit.jupiter.MockitoExtension;

import static org.mockito.Mockito.*;

/**
 * Saga 3 — Match Result Reward (Choreography).
 * Kiểm tra profile_service cộng đúng số coins tùy outcome.
 */
@ExtendWith(MockitoExtension.class)
@DisplayName("Saga 3: Match Result – coin reward cho người chơi")
class SagaThreeMatchResultTest {

    @Mock ProfileService profileService;

    private MatchResultKafkaListener listener;
    private final ObjectMapper objectMapper = new ObjectMapper();

    @BeforeEach
    void setUp() {
        listener = new MatchResultKafkaListener(profileService, objectMapper);
    }

    // ── Outcome: WIN ─────────────────────────────────────────────────────────

    @Test
    @DisplayName("Outcome=win → winner nhận 100 coins, loser nhận 10 coins")
    void whenOutcomeWin_thenWinnerGets100AndLoserGets10() {
        String msg = """
            {"matchId":1001,"outcome":"win","winnerId":1,"durationSecs":60.0,
             "kills":{"1":3,"2":0},"userIds":{"1":"user-alice","2":"user-bob"},"mapName":"world"}
            """;

        listener.onMatchResult(msg);

        verify(profileService).addCoins("user-alice", 100L);
        verify(profileService).addCoins("user-bob",  10L);
        verifyNoMoreInteractions(profileService);
    }

    // ── Outcome: DRAW ─────────────────────────────────────────────────────────

    @Test
    @DisplayName("Outcome=draw → tất cả người chơi nhận 20 coins")
    void whenOutcomeDraw_thenAllPlayersGet20Coins() {
        String msg = """
            {"matchId":1002,"outcome":"draw","winnerId":0,"durationSecs":300.0,
             "kills":{"1":2,"2":2},"userIds":{"1":"user-alice","2":"user-bob"},"mapName":"world"}
            """;

        listener.onMatchResult(msg);

        verify(profileService).addCoins("user-alice", 20L);
        verify(profileService).addCoins("user-bob",   20L);
    }

    // ── Outcome: TIMEOUT ─────────────────────────────────────────────────────

    @Test
    @DisplayName("Outcome=timeout → tất cả người chơi nhận 20 coins (như draw)")
    void whenOutcomeTimeout_thenAllPlayersGet20Coins() {
        String msg = """
            {"matchId":1003,"outcome":"timeout","winnerId":0,"durationSecs":300.0,
             "kills":{"1":0,"2":0},"userIds":{"1":"user-carol","2":"user-dave"},"mapName":"world"}
            """;

        listener.onMatchResult(msg);

        verify(profileService).addCoins("user-carol", 20L);
        verify(profileService).addCoins("user-dave",  20L);
    }

    // ── Edge cases ────────────────────────────────────────────────────────────

    @Test
    @DisplayName("Khi userIds null → không gọi addCoins, không crash")
    void whenUserIdsNull_thenNoCoinsAwarded() {
        String msg = """
            {"matchId":1004,"outcome":"win","winnerId":1,"durationSecs":10.0,
             "kills":{},"userIds":null,"mapName":"world"}
            """;

        listener.onMatchResult(msg);

        verifyNoInteractions(profileService);
    }

    @Test
    @DisplayName("Khi JSON không hợp lệ → không crash")
    void whenInvalidJson_thenNoAction() {
        listener.onMatchResult("{ bad json }");

        verifyNoInteractions(profileService);
    }

    @Test
    @DisplayName("Khi userId trùng nhau (map khác key, cùng userId) → chỉ cộng coins 1 lần")
    void whenDuplicateUserId_thenCoinsAwardedOnce() {
        // playerIds khác nhau nhưng userIds trùng (không nên xảy ra trong thực tế, nhưng an toàn)
        String msg = """
            {"matchId":1005,"outcome":"draw","winnerId":0,"durationSecs":30.0,
             "kills":{"1":1,"2":1},"userIds":{"1":"user-alice","2":"user-alice"},"mapName":"world"}
            """;

        listener.onMatchResult(msg);

        // user-alice chỉ được cộng 1 lần do alreadyRewarded set
        verify(profileService, times(1)).addCoins("user-alice", 20L);
    }
}
