package com.vminhkiet.auth_service.service;

import com.vminhkiet.auth_service.dto.AuthResponse;

public interface SessionService {
    String login(Long userId, String roles);
    void logout(Long userId);
    AuthResponse refreshAccessToken(Long userId, String refreshToken);
}
