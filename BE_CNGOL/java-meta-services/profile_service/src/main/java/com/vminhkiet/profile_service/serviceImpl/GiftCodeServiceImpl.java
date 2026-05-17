package com.vminhkiet.profile_service.serviceImpl;

import com.vminhkiet.profile_service.dto.CreateGiftCodeRequest;
import com.vminhkiet.profile_service.dto.RedeemCodeResponse;
import com.vminhkiet.profile_service.model.GiftCode;
import com.vminhkiet.profile_service.model.GiftCodeRedemption;
import com.vminhkiet.profile_service.repository.GiftCodeRedemptionRepository;
import com.vminhkiet.profile_service.repository.GiftCodeRepository;
import com.vminhkiet.profile_service.service.GiftCodeService;
import com.vminhkiet.profile_service.service.ProfileService;
import lombok.RequiredArgsConstructor;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.util.List;

@Service
@RequiredArgsConstructor
public class GiftCodeServiceImpl implements GiftCodeService {

    private final GiftCodeRepository           codeRepo;
    private final GiftCodeRedemptionRepository redemptionRepo;
    private final ProfileService               profileService;

    @Override
    @Transactional
    public GiftCode createCode(CreateGiftCodeRequest req) {
        if (codeRepo.findByCode(req.getCode()).isPresent()) {
            throw new IllegalArgumentException("Code '" + req.getCode() + "' đã tồn tại");
        }

        GiftCode code = GiftCode.builder()
                .code(req.getCode().toUpperCase())
                .coinReward(req.getCoinReward())
                .itemReward(req.getItemReward())
                .maxUses(req.getMaxUses())
                .currentUses(0)
                .expiresAt(req.getExpiresAt())
                .isActive(true)
                .build();

        return codeRepo.save(code);
    }

    @Override
    @Transactional
    public RedeemCodeResponse redeemCode(String userId, String rawCode) {
        String code = rawCode.toUpperCase().trim();

        GiftCode giftCode = codeRepo.findByCodeForUpdate(code)
                .orElseThrow(() -> new IllegalArgumentException("Code không tồn tại"));

        if (!giftCode.isUsable()) {
            String reason = !Boolean.TRUE.equals(giftCode.getIsActive()) ? "Code đã bị vô hiệu hóa"
                          : giftCode.isExpired()                          ? "Code đã hết hạn"
                          :                                                  "Code đã đạt giới hạn sử dụng";
            return RedeemCodeResponse.builder()
                    .success(false)
                    .message(reason)
                    .build();
        }

        if (redemptionRepo.existsByGiftCode_IdAndUserId(giftCode.getId(), userId)) {
            return RedeemCodeResponse.builder()
                    .success(false)
                    .message("Bạn đã sử dụng code này rồi")
                    .build();
        }

        // Tăng lượt dùng
        giftCode.setCurrentUses(giftCode.getCurrentUses() + 1);
        codeRepo.save(giftCode);

        // Lưu redemption record
        redemptionRepo.save(GiftCodeRedemption.builder()
                .giftCode(giftCode)
                .userId(userId)
                .build());

        // Cộng coin
        profileService.addCoins(userId, giftCode.getCoinReward());

        // Lấy profile để lấy totalCoins
        long totalCoins = profileService.getOrCreateProfile(userId).getCoins();

        return RedeemCodeResponse.builder()
                .success(true)
                .message("Đổi code thành công!")
                .coinsEarned(giftCode.getCoinReward())
                .itemEarned(giftCode.getItemReward())
                .totalCoins(totalCoins)
                .build();
    }

    @Override
    public List<GiftCode> getAllCodes() {
        return codeRepo.findAll();
    }

    @Override
    @Transactional
    public void deactivateCode(Long id) {
        GiftCode code = codeRepo.findById(id)
                .orElseThrow(() -> new IllegalArgumentException("Không tìm thấy code ID: " + id));
        code.setIsActive(false);
        codeRepo.save(code);
    }

    @Override
    @Transactional
    public void deleteCode(Long id) {
        if (!codeRepo.existsById(id)) {
            throw new IllegalArgumentException("Không tìm thấy code ID: " + id);
        }
        redemptionRepo.deleteByGiftCode_Id(id);
        codeRepo.deleteById(id);
    }
}
