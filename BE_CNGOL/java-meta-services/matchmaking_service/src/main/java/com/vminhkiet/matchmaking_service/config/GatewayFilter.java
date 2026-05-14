package com.vminhkiet.matchmaking_service.config;

import jakarta.servlet.*;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.servlet.http.HttpServletResponse;
import org.springframework.stereotype.Component;

import java.io.IOException;

@Component
public class GatewayFilter implements Filter {

    @Override
    public void doFilter(ServletRequest request, ServletResponse response, FilterChain chain)
            throws IOException, ServletException {
        HttpServletRequest req = (HttpServletRequest) request;
        String gatewayToken = req.getHeader("X-Gateway-Origin");

        if (gatewayToken == null || !gatewayToken.equals("MySecretKey123")) {
            HttpServletResponse res = (HttpServletResponse) response;
            res.setStatus(HttpServletResponse.SC_FORBIDDEN);
            res.setCharacterEncoding("UTF-8");
            res.setContentType("application/json; charset=UTF-8");
            res.getWriter().write("{\"error\": \"Phải truy cập qua API Gateway\"}");
            return;
        }

        chain.doFilter(request, response);
    }
}
