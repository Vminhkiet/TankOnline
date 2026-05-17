package com.vminhkiet.auth_service.dto;

import java.time.Instant;

import lombok.AllArgsConstructor;
import lombok.Data;
import lombok.NoArgsConstructor;

@Data
@NoArgsConstructor
@AllArgsConstructor
public class SessionInvalidatedEvent {
    private Long userId;
    private int code;
    private String message;
    private Instant timestamp;
}
