package com.vminhkiet.profile_service.controller;

import com.vminhkiet.profile_service.dto.CoinDeductRequest;
import com.vminhkiet.profile_service.dto.CoinDeductResponse;
import com.vminhkiet.profile_service.dto.ProfileResponse;
import com.vminhkiet.profile_service.service.ProfileService;
import jakarta.validation.Valid;
import lombok.RequiredArgsConstructor;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.*;

import java.util.Map;

/**
 * Internal endpoints — chỉ gọi từ các service nội bộ (shop_service).
 * Không expose ra ngoài qua API Gateway.
 */
@RestController
@RequestMapping("/internal/coins")
@RequiredArgsConstructor
public class InternalCoinController {

    private final ProfileService profileService;

    /**
     * Auth service gọi sau khi signup để tạo profile cho user mới.
     * POST /internal/profile/create
     */
    @PostMapping("/profile/create")
    public ResponseEntity<ProfileResponse> createProfile(@RequestBody Map<String, String> body) {
        String userId = body.get("userId");
        String displayName = body.getOrDefault("displayName", "Player_" + userId);
        if (userId == null || userId.isBlank())
            return ResponseEntity.badRequest().build();
        ProfileResponse profile = profileService.createProfile(userId, displayName);
        return ResponseEntity.ok(profile);
    }

    /**
     * Shop service gọi endpoint này để trừ coin khi mua hàng.
     * POST /internal/coins/deduct
     */
    @PostMapping("/deduct")
    public ResponseEntity<CoinDeductResponse> deductCoins(
            @Valid @RequestBody CoinDeductRequest req) {
        CoinDeductResponse response = profileService.deductCoins(req.getUserId(), req.getAmount());
        return ResponseEntity.ok(response);
    }

    /**
     * Cộng coin — dùng cho các reward khác (nếu cần).
     * POST /internal/coins/add
     */
    @PostMapping("/add")
    public ResponseEntity<?> addCoins(@Valid @RequestBody CoinDeductRequest req) {
        profileService.addCoins(req.getUserId(), req.getAmount());
        return ResponseEntity.ok(Map.of("message", "Đã cộng " + req.getAmount() + " coin cho user " + req.getUserId()));
    }
}
