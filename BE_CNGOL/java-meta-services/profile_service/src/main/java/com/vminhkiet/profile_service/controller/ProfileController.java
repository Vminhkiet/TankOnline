package com.vminhkiet.profile_service.controller;

import com.vminhkiet.profile_service.dto.ProfileResponse;
import com.vminhkiet.profile_service.dto.UpdateProfileRequest;
import com.vminhkiet.profile_service.service.ProfileService;
import jakarta.validation.Valid;
import lombok.RequiredArgsConstructor;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.*;

@RestController
@RequestMapping("/api/profile")
@RequiredArgsConstructor
public class ProfileController {

    private final ProfileService profileService;

    /**
     * Lấy profile của player đang đăng nhập (tự tạo nếu chưa có).
     * GET /api/profile/me
     */
    @GetMapping("/me")
    public ResponseEntity<ProfileResponse> getMyProfile(
            @RequestHeader("X-User-Id") String userId) {
        ProfileResponse profile = profileService.findProfile(userId);
        if (profile == null) return ResponseEntity.notFound().build();
        return ResponseEntity.ok(profile);
    }

    /**
     * Cập nhật displayName và avatarUrl.
     * PUT /api/profile/me
     */
    @PutMapping("/me")
    public ResponseEntity<ProfileResponse> updateMyProfile(
            @RequestHeader("X-User-Id") String userId,
            @Valid @RequestBody UpdateProfileRequest req) {
        return ResponseEntity.ok(profileService.updateProfile(userId, req));
    }

    /**
     * Xem profile của player khác theo userId.
     * GET /api/profile/{userId}
     */
    @GetMapping("/{userId}")
    public ResponseEntity<ProfileResponse> getProfile(@PathVariable String userId) {
        ProfileResponse profile = profileService.findProfile(userId);
        if (profile == null) return ResponseEntity.notFound().build();
        return ResponseEntity.ok(profile);
    }

    /**
     * Xem số coin hiện tại.
     * GET /api/profile/me/coins
     */
    @GetMapping("/me/coins")
    public ResponseEntity<?> getMyCoins(@RequestHeader("X-User-Id") String userId) {
        ProfileResponse profile = profileService.findProfile(userId);
        if (profile == null) return ResponseEntity.notFound().build();
        return ResponseEntity.ok(java.util.Map.of("coins", profile.getCoins()));
    }
}
