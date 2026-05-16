package com.vminhkiet.history_service.controller;

import com.vminhkiet.history_service.dto.MatchHistoryResponse;
import com.vminhkiet.history_service.dto.PlayerStatsResponse;
import com.vminhkiet.history_service.dto.SaveMatchRequest;
import com.vminhkiet.history_service.service.HistoryService;

import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.http.ResponseEntity;
import org.springframework.security.core.Authentication;
import org.springframework.web.bind.annotation.*;

import java.util.List;
import java.util.Map;

@RestController
@RequestMapping("/api/history")
public class HistoryController {

    @Autowired
    private HistoryService historyService;

    /**
     * Unity goi sau khi tran ket thuc.
     * POST /api/history/match
     * Body: { matchId, opponentId, result, kills, deaths, durationSecs, mapName }
     */
    @PostMapping("/match")
    public ResponseEntity<Map<String, String>> saveMatch(
            Authentication authentication,
            @RequestBody SaveMatchRequest req) {

        String playerId = authentication.getName();
        historyService.saveMatch(playerId, req);
        return ResponseEntity.ok(Map.of("status", "saved"));
    }

    /**
     * Lay 10 tran gan nhat cua player hien tai.
     * GET /api/history/me
     */
    @GetMapping("/me")
    public ResponseEntity<List<MatchHistoryResponse>> getMyHistory(Authentication authentication) {
        String playerId = authentication.getName();
        return ResponseEntity.ok(historyService.getRecentMatches(playerId));
    }

    /**
     * Lay thong ke tong hop: tong tran, ti le thang, tong kills.
     * GET /api/history/me/stats
     */
    @GetMapping("/me/stats")
    public ResponseEntity<PlayerStatsResponse> getMyStats(Authentication authentication) {
        String playerId = authentication.getName();
        return ResponseEntity.ok(historyService.getStats(playerId));
    }

    /**
     * Leaderboard top 10 players theo tong kills.
     * GET /api/history/leaderboard
     */
    @GetMapping("/leaderboard")
    public ResponseEntity<List<Map<String, Object>>> getLeaderboard() {
        return ResponseEntity.ok(historyService.getLeaderboard());
    }

    /**
     * Admin: Lay lich su tran dau cua bat ky player nao.
     * GET /api/history/player/{playerId}
     */
    @GetMapping("/player/{playerId}")
    public ResponseEntity<List<MatchHistoryResponse>> getPlayerHistory(@PathVariable String playerId) {
        return ResponseEntity.ok(historyService.getRecentMatches(playerId));
    }

    /**
     * Admin: Lay thong ke cua bat ky player nao.
     * GET /api/history/player/{playerId}/stats
     */
    @GetMapping("/player/{playerId}/stats")
    public ResponseEntity<PlayerStatsResponse> getPlayerStats(@PathVariable String playerId) {
        return ResponseEntity.ok(historyService.getStats(playerId));
    }
}
