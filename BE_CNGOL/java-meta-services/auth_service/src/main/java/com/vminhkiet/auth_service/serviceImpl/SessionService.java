package com.vminhkiet.auth_service.serviceImpl;

import java.util.concurrent.TimeUnit;
import java.time.Instant;
import java.util.UUID;

import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.data.redis.core.RedisTemplate;
import org.springframework.stereotype.Service;

import com.vminhkiet.auth_service.config.JwtProvider;
import com.vminhkiet.auth_service.dto.AuthResponse;
import com.vminhkiet.auth_service.model.UserSession;

@Service
public class SessionService implements com.vminhkiet.auth_service.service.SessionService {
    @Autowired
    private RedisTemplate<String, Object> redisTemplate;

    @Autowired
    private JwtProvider jwtProvider;

    private static final long REFRESH_TTL_DAYS = 7;

    @Override
    public String login(Long userId, String roles) {
        String key = "refresh:" + userId;

        if(Boolean.TRUE.equals(redisTemplate.hasKey(key))) {
            throw new RuntimeException("User already logged in on another device");
        }
        
        String refreshToken = UUID.randomUUID().toString();
        UserSession session = new UserSession(refreshToken, roles, Instant.now());

        redisTemplate.opsForValue().set(key, session, REFRESH_TTL_DAYS, TimeUnit.DAYS);

        return refreshToken;
    }

    @Override
    public void logout(Long userId) {
        String key = "refresh:" + userId;

        redisTemplate.delete(key);
    }

    @Override
    public AuthResponse refreshAccessToken(Long userId, String refreshToken) {
        String key = "refresh:" + userId;

        UserSession userSession = (UserSession) redisTemplate.opsForValue().get(key);

        if(userSession == null || !userSession.getRefreshToken().equals(refreshToken)) {
            throw new RuntimeException("Invalid or expired refresh token.");
        }

        String newRefreshToken = UUID.randomUUID().toString();
        userSession.setRefreshToken(newRefreshToken);
        redisTemplate.opsForValue().set(key, userSession, REFRESH_TTL_DAYS, TimeUnit.DAYS);

        String jwt = jwtProvider.generateAccessToken(userId.toString(), userSession.getRoles());
        AuthResponse response = new AuthResponse();
        response.setJwt(jwt);
        response.setRefreshToken(newRefreshToken);

        return response;
    }
}
