package com.vminhkiet.matchmaking_service.config;

import jakarta.servlet.FilterChain;
import jakarta.servlet.ServletException;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.servlet.http.HttpServletResponse;
import org.springframework.security.authentication.UsernamePasswordAuthenticationToken;
import org.springframework.security.core.authority.AuthorityUtils;
import org.springframework.security.core.context.SecurityContextHolder;
import org.springframework.stereotype.Component;
import org.springframework.web.filter.OncePerRequestFilter;

import java.io.IOException;
import java.util.List;

@Component
public class GatewayHeaderAuthFilter extends OncePerRequestFilter {

    @Override
    protected void doFilterInternal(HttpServletRequest request,
                                    HttpServletResponse response,
                                    FilterChain filterChain) throws ServletException, IOException {
        String userId = request.getHeader("X-User-Id");
        String roles  = request.getHeader("X-User-Roles");

        if (userId != null && !userId.isBlank()) {
            var auth = new UsernamePasswordAuthenticationToken(
                    userId,
                    null,
                    roles != null ? AuthorityUtils.commaSeparatedStringToAuthorityList(roles) : List.of()
            );
            SecurityContextHolder.getContext().setAuthentication(auth);
        }

        filterChain.doFilter(request, response);
    }

    @Override
    protected boolean shouldNotFilterAsyncDispatch() {
        // Must return false so that the filter runs again when DeferredResult wakes up,
        // otherwise SecurityContextHolder is empty and Spring Security denies the async dispatch.
        return false;
    }
}
