package com.vminhkiet.auth_service.model;

import java.time.Instant;

import lombok.AllArgsConstructor;
import lombok.Data;
import lombok.NoArgsConstructor;


@Data
@NoArgsConstructor
@AllArgsConstructor
public class UserSession {
    private String refreshToken;
    private String roles; 
    private Instant loginTime;
}
