package com.vminhkiet.profile_service.config;

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
        if (path.startsWith("/actuator") || path.startsWith("/internal")
                || path.startsWith("/api/profile/admin")
                || isPublicProfileView(path)) {
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

    // /api/profile/{userId} is public; /api/profile/me, /me/coins, /giftcode, /admin require auth
    private boolean isPublicProfileView(String path) {
        if (!path.startsWith("/api/profile/")) return false;
        String suffix = path.substring("/api/profile/".length());
        if (suffix.isEmpty()) return false;
        if (suffix.startsWith("me") || suffix.startsWith("admin") || suffix.startsWith("giftcode")) return false;
        return !suffix.contains("/");
    }
}
