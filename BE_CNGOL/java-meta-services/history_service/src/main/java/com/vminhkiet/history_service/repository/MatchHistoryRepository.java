package com.vminhkiet.history_service.repository;

import com.vminhkiet.history_service.model.MatchHistory;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;
import org.springframework.stereotype.Repository;

import java.util.List;

@Repository
public interface MatchHistoryRepository extends JpaRepository<MatchHistory, Long> {

    // 10 tran gan nhat cua player
    List<MatchHistory> findTop10ByPlayerIdOrderByPlayedAtDesc(String playerId);

    // Tat ca tran cua player (cho thong ke)
    List<MatchHistory> findByPlayerIdOrderByPlayedAtDesc(String playerId);

    // Top kills cho leaderboard
    @Query("""
        SELECT h.playerId, SUM(h.kills) as totalKills, COUNT(h) as totalMatches,
               SUM(CASE WHEN h.result = 'WIN' THEN 1 ELSE 0 END) as wins
        FROM MatchHistory h
        GROUP BY h.playerId
        ORDER BY totalKills DESC
        LIMIT 10
        """)
    List<Object[]> findLeaderboard();

    // Kiem tra trung lap (tranh luu 2 lan cho cung match)
    boolean existsByMatchIdAndPlayerId(Long matchId, String playerId);
}
