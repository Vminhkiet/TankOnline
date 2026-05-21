package com.vminhkiet.matchmaking_service.service;

import com.vminhkiet.matchmaking_service.model.WaitingEntry;
import lombok.extern.slf4j.Slf4j;
import org.springframework.stereotype.Component;

import java.util.ArrayList;
import java.util.List;

/**
 * Singleton lobby được chia sẻ giữa MatchmakingController và SessionInvalidatedKafkaListener.
 * Mọi truy cập đều synchronized để thread-safe.
 */
@Slf4j
@Component
public class LobbyManager {

    private final List<WaitingEntry> lobby = new ArrayList<>();

    /** Thêm player vào lobby, trả về batch nếu đủ MATCH_SIZE player. */
    public synchronized List<WaitingEntry> addAndTryForm(WaitingEntry entry, int matchSize) {
        // Xóa entry cũ của cùng userId (tìm trận lại sau khi hủy/khi cùng user gọi nhiều lần)
        // Complete future cũ để giải phóng HTTP thread đang chờ
        lobby.removeIf(existing -> {
            if (existing.userId() == entry.userId()) {
                existing.future().complete(
                        org.springframework.http.ResponseEntity.status(409)
                                .body(java.util.Map.of("error", "replaced_by_new_search")));
                return true;
            }
            return false;
        });
        lobby.add(entry);
        if (lobby.size() >= matchSize) {
            List<WaitingEntry> batch = new ArrayList<>(lobby.subList(0, matchSize));
            lobby.subList(0, matchSize).clear();
            return batch;
        }
        return null;
    }

    /**
     * Saga-4 Compensation: xóa player khỏi lobby khi session bị invalidate.
     */
    public synchronized void removePlayer(long userId) {
        boolean removed = lobby.removeIf(entry -> {
            if (entry.userId() == userId) {
                entry.future().completeExceptionally(
                        new RuntimeException("Session invalidated — bạn đã đăng nhập từ thiết bị khác"));
                return true;
            }
            return false;
        });
        if (removed) {
            log.info("[Saga-4] Removed userId={} from matchmaking lobby due to session invalidation", userId);
        }
    }

    /** Trả về số player đang chờ (dùng cho monitoring). */
    public synchronized int size() {
        return lobby.size();
    }
}
