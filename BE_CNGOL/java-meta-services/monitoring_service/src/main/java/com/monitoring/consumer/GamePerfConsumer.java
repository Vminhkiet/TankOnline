package com.monitoring.consumer;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.monitoring.model.GamePerfEvent;
import com.monitoring.model.TaskStats;
import io.micrometer.core.instrument.Gauge;
import io.micrometer.core.instrument.MeterRegistry;
import lombok.extern.slf4j.Slf4j;
import org.springframework.kafka.annotation.KafkaListener;
import org.springframework.stereotype.Service;

import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;

/**
 * Consumes "game.perf" Kafka topic.
 * Mỗi message = 1 match đã aggregate 600 ticks → update Micrometer Gauge.
 *
 * Prometheus scrape /actuator/prometheus sẽ expose:
 *   game_task_bullet_us{match_id="1"}
 *   game_task_physics_us{match_id="1"}
 *   game_task_snap_us{match_id="1"}
 */
@Slf4j
@Service
public class GamePerfConsumer {

    private final ObjectMapper objectMapper = new ObjectMapper();
    private final MeterRegistry meterRegistry;

    // matchId → TaskStats (thread-safe, Gauge registered per matchId)
    private final ConcurrentHashMap<Integer, TaskStats> taskStatsMap = new ConcurrentHashMap<>();

    // Track which matchIds already have Gauges registered (register once, update via AtomicLong)
    private final ConcurrentHashMap<Integer, Boolean> registeredGauges = new ConcurrentHashMap<>();

    public GamePerfConsumer(MeterRegistry meterRegistry) {
        this.meterRegistry = meterRegistry;
    }

    @KafkaListener(topics = "game.perf", groupId = "monitoring-game-perf")
    public void consume(String message) {
        try {
            GamePerfEvent event = objectMapper.readValue(message, GamePerfEvent.class);
            int matchId = event.getMatchId();

            // Get or create TaskStats for this match
            TaskStats stats = taskStatsMap.computeIfAbsent(matchId, TaskStats::new);
            stats.update(event.getBulletUs(), event.getPhysicsUs(), event.getSnapUs());

            // Register Prometheus Gauges once per matchId
            registeredGauges.computeIfAbsent(matchId, id -> {
                String matchLabel = String.valueOf(id);

                Gauge.builder("game_task_bullet_us", stats, s -> s.getBulletUs())
                        .description("Bullet collision processing µs (avg over 600 ticks)")
                        .tag("match_id", matchLabel)
                        .register(meterRegistry);

                Gauge.builder("game_task_physics_us", stats, s -> s.getPhysicsUs())
                        .description("Physics update + collision detection µs (avg over 600 ticks)")
                        .tag("match_id", matchLabel)
                        .register(meterRegistry);

                Gauge.builder("game_task_snap_us", stats, s -> s.getSnapUs())
                        .description("Snapshot broadcast µs (avg over 600 ticks)")
                        .tag("match_id", matchLabel)
                        .register(meterRegistry);

                log.info("Registered Prometheus Gauges for match_id={}", id);
                return Boolean.TRUE;
            });

            // Evict stale matches (finished matches stop sending events)
            taskStatsMap.entrySet().removeIf(e -> e.getValue().isStale());

            log.debug("game.perf: match={} bullet={}µs physics={}µs snap={}µs",
                    matchId, event.getBulletUs(), event.getPhysicsUs(), event.getSnapUs());

        } catch (Exception e) {
            log.warn("Failed to parse game.perf message: {} — {}", message, e.getMessage());
        }
    }

    /** REST endpoint helper — trả về toàn bộ stats hiện tại. */
    public Map<Integer, TaskStats> getAllStats() {
        return taskStatsMap;
    }
}
