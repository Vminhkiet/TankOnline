package com.vminhkiet.matchmaking_service.model;

import org.springframework.http.ResponseEntity;

import java.util.Map;
import java.util.concurrent.CompletableFuture;

public record WaitingEntry(
        long userId,
        CompletableFuture<ResponseEntity<Map<String, Object>>> future
) {}
