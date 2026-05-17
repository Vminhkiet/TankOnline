package com.vminhkiet.profile_service.repository;

import com.vminhkiet.profile_service.model.GiftCodeRedemption;
import org.springframework.data.jpa.repository.JpaRepository;

public interface GiftCodeRedemptionRepository extends JpaRepository<GiftCodeRedemption, Long> {

    boolean existsByGiftCode_IdAndUserId(Long giftCodeId, String userId);

    void deleteByGiftCode_Id(Long giftCodeId);
}
