package com.vminhkiet.profile_service.service;

import com.vminhkiet.profile_service.dto.*;

public interface ProfileService {
    ProfileResponse    getOrCreateProfile(String userId);
    ProfileResponse    findProfile(String userId);
    ProfileResponse    createProfile(String userId, String displayName);
    ProfileResponse    updateProfile(String userId, UpdateProfileRequest req);
    CoinDeductResponse deductCoins(String userId, Long amount);
    void               addCoins(String userId, Long amount);
    void               addRp(String userId, int amount);
}
