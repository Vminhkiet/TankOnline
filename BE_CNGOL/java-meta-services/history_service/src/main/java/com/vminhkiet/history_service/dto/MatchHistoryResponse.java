package com.vminhkiet.history_service.dto;

import com.vminhkiet.history_service.model.MatchHistory;
import lombok.Data;

import java.time.Instant;

@Data
public class MatchHistoryResponse {
    private Long   id;
    private Long   matchId;
    private String opponentId;
    private String result;
    private int    kills;
    private int    deaths;
    private int    durationSecs;
    private String mapName;
    private Instant playedAt;

    public static MatchHistoryResponse from(MatchHistory h) {
        MatchHistoryResponse r = new MatchHistoryResponse();
        r.id          = h.getId();
        r.matchId     = h.getMatchId();
        r.opponentId  = h.getOpponentId();
        r.result      = h.getResult().name();
        r.kills       = h.getKills();
        r.deaths      = h.getDeaths();
        r.durationSecs = h.getDurationSecs();
        r.mapName     = h.getMapName();
        r.playedAt    = h.getPlayedAt();
        return r;
    }
}
