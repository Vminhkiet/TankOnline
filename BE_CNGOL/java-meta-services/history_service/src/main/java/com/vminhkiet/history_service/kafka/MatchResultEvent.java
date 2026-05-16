package com.vminhkiet.history_service.kafka;

import lombok.Data;
import java.util.List;
import java.util.Map;

@Data
public class MatchResultEvent {
    private long matchId;
    private String outcome;       // "win" | "draw" | "timeout"
    private long winnerId;        // numeric playerId of winner (0 if draw/timeout)
    private float durationSecs;
    private Map<String, Integer> kills;    // playerId(str) -> kill count
    private Map<String, String> userIds;   // playerId(str) -> userId(str)
    private String mapName;
}
