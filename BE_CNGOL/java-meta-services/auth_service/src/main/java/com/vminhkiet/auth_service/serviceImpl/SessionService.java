package com.vminhkiet.auth_service.serviceImpl;

import java.util.concurrent.TimeUnit;
import java.time.Instant;
import java.util.UUID;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
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

    @Autowired
    private SessionInvalidationProducer sessionInvalidationProducer;

    private static final long REFRESH_TTL_DAYS = 7;
    private static final Logger logger = LoggerFactory.getLogger(SessionService.class);

    @Override
    public String login(Long userId, String roles) {
        String key = "refresh:" + userId;

        if(Boolean.TRUE.equals(redisTemplate.hasKey(key))) {
            // Có session cũ chưa logout -> gửi sự kiện kick user cũ trước khi ghi đè session mới
            try {
                sessionInvalidationProducer.publishDuplicateLoginKick(userId);
                logger.warn("Duplicate login detected for userId={}. Published session invalidation event (code=1003).", userId);
            } catch (Exception ex) {
                logger.error("Failed to publish session invalidation event for userId={}. Continue login flow.", userId, ex);
            }
            redisTemplate.delete(key);
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
