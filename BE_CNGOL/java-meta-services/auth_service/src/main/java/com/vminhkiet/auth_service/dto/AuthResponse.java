package com.vminhkiet.auth_service.dto;

import lombok.Data;

@Data
public class AuthResponse {
    private String jwt;
    private String refreshToken;
}
