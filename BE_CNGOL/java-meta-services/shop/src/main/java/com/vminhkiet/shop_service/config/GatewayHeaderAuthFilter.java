package com.vminhkiet.shop_service.config;

import jakarta.servlet.FilterChain;
import jakarta.servlet.ServletException;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.servlet.http.HttpServletResponse;
import org.springframework.security.authentication.UsernamePasswordAuthenticationToken;
import org.springframework.security.core.authority.AuthorityUtils;
import org.springframework.security.core.context.SecurityContextHolder;
import org.springframework.web.filter.OncePerRequestFilter;

import java.io.IOException;
import java.util.List;

public class GatewayHeaderAuthFilter extends OncePerRequestFilter {

    @Override
    protected void doFilterInternal(HttpServletRequest request,
                                    HttpServletResponse response,
                                    FilterChain filterChain) throws ServletException, IOException {
        String userId = request.getHeader("X-User-Id");
        String roles  = request.getHeader("X-User-Roles");

        String path = request.getRequestURI();
        String gatewayOrigin = request.getHeader("X-Gateway-Origin");

        if (path.startsWith("/actuator") || isPublicShopPath(request.getMethod(), path)
                || isAdminViaGateway(path, gatewayOrigin)) {
            filterChain.doFilter(request, response);
            return;
        }

        if (userId == null || userId.isBlank() || "null".equals(userId)) {
            response.setStatus(HttpServletResponse.SC_UNAUTHORIZED);
            response.setContentType("application/json");
            response.getWriter().write("{\"error\":\"Unauthorized: missing X-User-Id header\"}");
            return;
        }

        var auth = new UsernamePasswordAuthenticationToken(
                userId,
                null,
                roles != null ? AuthorityUtils.commaSeparatedStringToAuthorityList(roles) : List.of()
        );
        SecurityContextHolder.getContext().setAuthentication(auth);

        filterChain.doFilter(request, response);
    }

    // GET /api/shop/items, /api/shop/items/version, /api/shop/items/category/*, /api/shop/items/{id}
    private boolean isPublicShopPath(String method, String path) {
        if (!"GET".equalsIgnoreCase(method)) return false;
        return path.equals("/api/shop/items")
                || path.startsWith("/api/shop/items/");
    }

    // Admin endpoints cho phép nếu request đến từ gateway (có header X-Gateway-Origin)
    private boolean isAdminViaGateway(String path, String gatewayOrigin) {
        return path.startsWith("/api/shop/admin/")
                && "MySecretKey123".equals(gatewayOrigin);
    }
}
