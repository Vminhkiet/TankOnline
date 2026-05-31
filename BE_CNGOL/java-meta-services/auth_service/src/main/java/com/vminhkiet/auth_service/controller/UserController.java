package com.vminhkiet.auth_service.controller;

import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.security.core.Authentication;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;
import org.springframework.web.bind.annotation.PutMapping;
import org.springframework.web.bind.annotation.PathVariable;

import com.vminhkiet.auth_service.dto.UserMeResponse;
import com.vminhkiet.auth_service.service.UserService;
import com.vminhkiet.auth_service.model.User;

import java.util.List;

@RestController
@RequestMapping("api/user")
public class UserController {
    @Autowired
    private UserService userService;

    /**
     * Thông tin tài khoản đang đăng nhập (email, username). Cần header Authorization: Bearer JWT.
     * GET /api/user/me
     */
    @GetMapping("/me")
    public ResponseEntity<UserMeResponse> getMe(Authentication authentication) {
        if (authentication == null || !authentication.isAuthenticated()) {
            return ResponseEntity.status(HttpStatus.UNAUTHORIZED).build();
        }
        Long userId = Long.parseLong(authentication.getName());
        return ResponseEntity.ok(userService.getUserMe(userId));
    }

    // TODO: Re-enable when admin web sends JWT
    // @PreAuthorize("hasRole('ADMIN')")
    @GetMapping("/users")
    public ResponseEntity<List<User>> getAllUsers(){
        List<User> users = userService.getAllUser();

        return ResponseEntity.ok(users);
    }

    @GetMapping("/{userId}")
    public ResponseEntity<UserMeResponse> getUserById(@PathVariable Long userId) {
        try {
            return ResponseEntity.ok(userService.getUserMe(userId));
        } catch (IllegalArgumentException e) {
            return ResponseEntity.status(HttpStatus.NOT_FOUND).build();
        }
    }

    @PutMapping("/{userId}/ban")
    public ResponseEntity<Void> toggleBan(@PathVariable Long userId) {
        try {
            userService.toggleBan(userId);
            return ResponseEntity.ok().build();
        } catch (IllegalArgumentException e) {
            return ResponseEntity.status(HttpStatus.NOT_FOUND).build();
        }
    }


}
