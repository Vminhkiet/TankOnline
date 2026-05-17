package com.vminhkiet.profile_service.controller;

import com.vminhkiet.profile_service.dto.CreateGiftCodeRequest;
import com.vminhkiet.profile_service.dto.RedeemCodeRequest;
import com.vminhkiet.profile_service.dto.RedeemCodeResponse;
import com.vminhkiet.profile_service.model.GiftCode;
import com.vminhkiet.profile_service.service.GiftCodeService;
import jakarta.validation.Valid;
import lombok.RequiredArgsConstructor;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.*;

import java.util.List;
import java.util.Map;

@RestController
@RequiredArgsConstructor
public class GiftCodeController {

    private final GiftCodeService giftCodeService;

    private boolean isAdmin(String roles) {
        return roles != null && roles.contains("ROLE_ADMIN");
    }

    /**
     * Player nhập code để nhận thưởng.
     * POST /api/profile/giftcode/redeem
     */
    @PostMapping("/api/profile/giftcode/redeem")
    public ResponseEntity<RedeemCodeResponse> redeemCode(
            @RequestHeader("X-User-Id") String userId,
            @Valid @RequestBody RedeemCodeRequest req) {
        try {
            RedeemCodeResponse response = giftCodeService.redeemCode(userId, req.getCode());
            return ResponseEntity.ok(response);
        } catch (IllegalArgumentException e) {
            return ResponseEntity.ok(RedeemCodeResponse.builder()
                    .success(false)
                    .message(e.getMessage())
                    .build());
        }
    }

    /**
     * Admin tạo gift code mới.
     * POST /api/profile/admin/giftcode
     */
    @PostMapping("/api/profile/admin/giftcode")
    public ResponseEntity<?> createCode(
            @RequestHeader("X-User-Roles") String roles,
            @Valid @RequestBody CreateGiftCodeRequest req) {

        if (!isAdmin(roles)) {
            return ResponseEntity.status(HttpStatus.FORBIDDEN)
                    .body(Map.of("error", "Chỉ admin mới có quyền tạo gift code"));
        }
        try {
            GiftCode created = giftCodeService.createCode(req);
            return ResponseEntity.status(HttpStatus.CREATED).body(created);
        } catch (IllegalArgumentException e) {
            return ResponseEntity.badRequest().body(Map.of("error", e.getMessage()));
        }
    }

    /**
     * Admin xem tất cả gift codes.
     * GET /api/profile/admin/giftcode
     */
    @GetMapping("/api/profile/admin/giftcode")
    public ResponseEntity<?> getAllCodes(
            @RequestHeader("X-User-Roles") String roles) {

        if (!isAdmin(roles)) {
            return ResponseEntity.status(HttpStatus.FORBIDDEN)
                    .body(Map.of("error", "Chỉ admin mới có quyền xem danh sách gift code"));
        }
        List<GiftCode> codes = giftCodeService.getAllCodes();
        return ResponseEntity.ok(codes);
    }

    /**
     * Admin xóa hẳn gift code khỏi DB.
     * DELETE /api/profile/admin/giftcode/{id}/permanent
     */
    @DeleteMapping("/api/profile/admin/giftcode/{id}/permanent")
    public ResponseEntity<?> deleteCode(
            @RequestHeader("X-User-Roles") String roles,
            @PathVariable Long id) {

        if (!isAdmin(roles)) {
            return ResponseEntity.status(HttpStatus.FORBIDDEN)
                    .body(Map.of("error", "Chi admin moi co quyen xoa gift code"));
        }
        try {
            giftCodeService.deleteCode(id);
            return ResponseEntity.ok(Map.of("message", "Gift code ID " + id + " da duoc xoa khoi he thong"));
        } catch (IllegalArgumentException e) {
            return ResponseEntity.badRequest().body(Map.of("error", e.getMessage()));
        }
    }

    /**
     * Admin vô hiệu hóa gift code.
     * DELETE /api/profile/admin/giftcode/{id}
     */
    @DeleteMapping("/api/profile/admin/giftcode/{id}")
    public ResponseEntity<?> deactivateCode(
            @RequestHeader("X-User-Roles") String roles,
            @PathVariable Long id) {

        if (!isAdmin(roles)) {
            return ResponseEntity.status(HttpStatus.FORBIDDEN)
                    .body(Map.of("error", "Chỉ admin mới có quyền vô hiệu hóa gift code"));
        }
        try {
            giftCodeService.deactivateCode(id);
            return ResponseEntity.ok(Map.of("message", "Code đã bị vô hiệu hóa"));
        } catch (IllegalArgumentException e) {
            return ResponseEntity.badRequest().body(Map.of("error", e.getMessage()));
        }
    }
}
