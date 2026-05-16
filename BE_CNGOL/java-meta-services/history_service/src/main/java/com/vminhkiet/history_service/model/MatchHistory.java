package com.vminhkiet.history_service.model;

import jakarta.persistence.*;
import lombok.Data;
import lombok.NoArgsConstructor;
import lombok.AllArgsConstructor;
import lombok.Builder;

import java.time.Instant;

@Entity
@Table(name = "match_history")
@Data
@NoArgsConstructor
@AllArgsConstructor
@Builder
public class MatchHistory {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(name = "match_id")
    private Long matchId;

    @Column(name = "player_id", nullable = false)
    private String playerId;

    @Column(name = "opponent_id")
    private String opponentId;

    @Enumerated(EnumType.STRING)
    @Column(nullable = false)
    private MatchResult result;       // WIN / LOSE / DRAW

    private int kills;
    private int deaths;

    @Column(name = "duration_secs")
    private int durationSecs;

    @Column(name = "map_name")
    private String mapName;

    @Column(name = "played_at")
    private Instant playedAt;

    public enum MatchResult { WIN, LOSE, DRAW }
}
