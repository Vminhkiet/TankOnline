package com.monitoring.controller;

import lombok.extern.slf4j.Slf4j;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RequestHeader;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;
import org.springframework.web.client.RestTemplate;

import java.util.HashMap;
import java.util.Map;

@RestController
@RequestMapping("/api/tank")
@Slf4j
public class TankMetricsController {

    private static final String TANK_METRICS_URL = "http://localhost:9100/metrics";
    private final RestTemplate restTemplate = new RestTemplate();

    @GetMapping("/metrics")
    @SuppressWarnings("unchecked")
    public ResponseEntity<Map<String, Object>> getMetrics(
            @RequestHeader(value = "X-User-Roles", required = false) String roles) {

        try {
            Map<String, Object> metrics = restTemplate.getForObject(TANK_METRICS_URL, Map.class);
            if (metrics != null) {
                metrics.put("status", "online");
            }
            return ResponseEntity.ok(metrics);
        } catch (Exception e) {
            log.warn("Tank Server unreachable at {}: {}", TANK_METRICS_URL, e.getMessage());
            Map<String, Object> offline = new HashMap<>();
            offline.put("status", "offline");
            offline.put("message", "Tank Server chưa chạy hoặc chưa expose HTTP metrics port 9100.");
            return ResponseEntity.ok(offline);
        }
    }

    @GetMapping("/health")
    public ResponseEntity<Map<String, Object>> getHealth(
            @RequestHeader(value = "X-User-Roles", required = false) String roles) {

        Map<String, Object> result = new HashMap<>();
        result.put("service", "tank-server");
        try {
            restTemplate.getForObject(TANK_METRICS_URL, String.class);
            result.put("status", "UP");
        } catch (Exception e) {
            result.put("status", "DOWN");
            result.put("reason", e.getMessage());
        }
        return ResponseEntity.ok(result);
    }

    private boolean isAdmin(String roles) {
        return roles != null && roles.contains("ROLE_ADMIN");
    }
}
