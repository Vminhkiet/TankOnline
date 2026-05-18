package com.vminhkiet.api_gateway.filter;

import io.jsonwebtoken.Claims;
import io.jsonwebtoken.Jwts;
import io.jsonwebtoken.security.Keys;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.cloud.gateway.filter.GatewayFilterChain;
import org.springframework.cloud.gateway.filter.GlobalFilter;
import org.springframework.core.Ordered;
import org.springframework.http.HttpStatus;
import org.springframework.http.HttpMethod;
import org.springframework.http.server.reactive.ServerHttpRequest;
import org.springframework.stereotype.Component;
import org.springframework.web.server.ServerWebExchange;
import reactor.core.publisher.Mono;

import javax.crypto.SecretKey;
import java.util.List;

@Component
public class JwtAuthenticationFilter implements GlobalFilter, Ordered {

    @Value("${jwt.secret-key}")
    private String secretKey;

    private static final List<String> PUBLIC_ENDPOINTS = List.of(
            "/api/auth/",
            "/api/user/",
            "/api/history/leaderboard"
    );

    // Endpoints accessible without JWT but still forward X-User-Id when JWT is present
    private static final List<String> OPTIONAL_AUTH_ENDPOINTS = List.of(
            "/api/shop/items",
            "/api/shop/items/"
    );

    @Override
    public Mono<Void> filter(ServerWebExchange exchange, GatewayFilterChain chain) {
        String path = exchange.getRequest().getURI().getPath();

        if (exchange.getRequest().getMethod() == HttpMethod.OPTIONS) {
            return chain.filter(exchange);
        }

        boolean isPublic = isPublicEndpoint(path);
        boolean isOptionalAuth = isOptionalAuthEndpoint(path) || isPublicProfileView(path);

        String authHeader = exchange.getRequest().getHeaders().getFirst("Authorization");

        if (authHeader == null || !authHeader.startsWith("Bearer ")) {
            if (isPublic || isOptionalAuth) {
                return chain.filter(exchange);
            }
            exchange.getResponse().setStatusCode(HttpStatus.UNAUTHORIZED);
            return exchange.getResponse().setComplete();
        }

        String token = authHeader.substring(7);

        try {
            SecretKey key = Keys.hmacShaKeyFor(secretKey.getBytes());
            Claims claims = Jwts.parserBuilder()
                    .setSigningKey(key)
                    .build()
                    .parseClaimsJws(token)
                    .getBody();

            String userId = String.valueOf(claims.get("userId"));
            String authorities = String.valueOf(claims.get("authorities"));

            ServerHttpRequest mutatedRequest = exchange.getRequest().mutate()
                    .header("X-User-Id", userId)
                    .header("X-User-Roles", authorities)
                    .build();

            return chain.filter(exchange.mutate().request(mutatedRequest).build());

        } catch (Exception e) {
            if (isPublic || isOptionalAuth) {
                return chain.filter(exchange);
            }
            exchange.getResponse().setStatusCode(HttpStatus.UNAUTHORIZED);
            return exchange.getResponse().setComplete();
        }
    }

    private boolean isPublicEndpoint(String path) {
        return PUBLIC_ENDPOINTS.stream().anyMatch(path::startsWith);
    }

    private boolean isOptionalAuthEndpoint(String path) {
        return OPTIONAL_AUTH_ENDPOINTS.stream().anyMatch(path::startsWith);
    }

    // GET /api/profile/{userId} is public (view other player's profile)
    // but /api/profile/me, /api/profile/me/coins, /api/profile/giftcode, /api/profile/admin require auth
    private boolean isPublicProfileView(String path) {
        if (!path.startsWith("/api/profile/")) return false;
        String suffix = path.substring("/api/profile/".length());
        if (suffix.isEmpty()) return false;
        if (suffix.startsWith("me") || suffix.startsWith("admin") || suffix.startsWith("giftcode")) return false;
        return !suffix.contains("/");
    }

    @Override
    public int getOrder() {
        return -1;
    }
}
