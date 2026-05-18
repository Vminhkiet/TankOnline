package com.monitoring.model;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;
import com.fasterxml.jackson.annotation.JsonProperty;
import lombok.Data;

/**
 * JSON payload từ Kafka topic "game.perf".
 * Published bởi tank_metrics_agent.py sau khi parse dòng [Task] từ server.log.
 *
 * Format: {"matchId":1,"bulletUs":0,"physicsUs":420,"snapUs":35,"timestamp":1234567890.0}
 */
@Data
@JsonIgnoreProperties(ignoreUnknown = true)
public class GamePerfEvent {

    @JsonProperty("matchId")
    private int matchId;

    /** Thời gian xử lý bullet collision trung bình trong 600 ticks (µs) */
    @JsonProperty("bulletUs")
    private long bulletUs;

    /** Thời gian physics update + collision detection trung bình (µs) */
    @JsonProperty("physicsUs")
    private long physicsUs;

    /** Thời gian broadcast snapshot trung bình (µs) */
    @JsonProperty("snapUs")
    private long snapUs;

    @JsonProperty("timestamp")
    private double timestamp;
}
