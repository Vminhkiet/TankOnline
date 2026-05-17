package com.vminhkiet.profile_service.service;

import com.vminhkiet.profile_service.dto.CreateGiftCodeRequest;
import com.vminhkiet.profile_service.dto.RedeemCodeResponse;
import com.vminhkiet.profile_service.model.GiftCode;

import java.util.List;

public interface GiftCodeService {
    GiftCode       createCode(CreateGiftCodeRequest req);
    RedeemCodeResponse redeemCode(String userId, String code);
    List<GiftCode> getAllCodes();
    void           deactivateCode(Long id);
    void           deleteCode(Long id);
}
