package com.vminhkiet.shop_service.config; // Đảm bảo đúng package của bạn

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
        HttpServletResponse res = (HttpServletResponse) response;

        String path = req.getRequestURI();
        if (req.getMethod().equalsIgnoreCase("OPTIONS") || path.startsWith("/actuator")) {
            chain.doFilter(request, response);
            return;
        }

        // Kiểm tra request phải đến từ API Gateway
        String gatewayToken = req.getHeader("X-Gateway-Origin");
        if (gatewayToken == null || !gatewayToken.equals("MySecretKey123")) {
            res.setStatus(HttpServletResponse.SC_FORBIDDEN);
            res.setContentType("text/plain; charset=UTF-8");
            res.getWriter().write("Bạn phải truy cập qua API Gateway!");
            return;
        }

        // Public GET endpoints do not require a logged-in user
        if (isPublicShopPath(req.getMethod(), path)) {
            chain.doFilter(request, response);
            return;
        }

        // Kiểm tra user đã đăng nhập (gateway thêm header này sau khi validate JWT)
        String userId = req.getHeader("X-User-Id");
        if (userId == null || userId.isBlank() || "null".equals(userId)) {
            res.setStatus(HttpServletResponse.SC_UNAUTHORIZED);
            res.setContentType("application/json;charset=UTF-8");
            res.getWriter().write("{\"error\":\"Unauthorized\"}");
            return;
        }

        chain.doFilter(request, response);
    }

    private boolean isPublicShopPath(String method, String path) {
        if (!"GET".equalsIgnoreCase(method)) return false;
        return path.equals("/api/shop/items")
                || path.startsWith("/api/shop/items/");
    }
}