package com.vminhkiet.profile_service.serviceImpl;

import com.vminhkiet.profile_service.dto.*;
import com.vminhkiet.profile_service.model.UserProfile;
import com.vminhkiet.profile_service.repository.UserProfileRepository;
import com.vminhkiet.profile_service.service.ProfileService;
import lombok.RequiredArgsConstructor;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

@Service
@RequiredArgsConstructor
public class ProfileServiceImpl implements ProfileService {

    private final UserProfileRepository repo;

    @Value("${profile.default-coins:1000}")
    private Long defaultCoins;

    @Override
    @Transactional
    public ProfileResponse getOrCreateProfile(String userId) {
        UserProfile profile = repo.findById(userId).orElseGet(() -> {
            UserProfile newProfile = UserProfile.builder()
                    .userId(userId)
                    .displayName("Player_" + userId)
                    .imageId("")
                    .coins(defaultCoins)
                    .build();
            return repo.save(newProfile);
        });
        return ProfileResponse.from(profile);
    }

    @Override
    @Transactional
    public ProfileResponse updateProfile(String userId, UpdateProfileRequest req) {
        UserProfile profile = repo.findById(userId).orElseGet(() -> {
            UserProfile newProfile = UserProfile.builder()
                    .userId(userId)
                    .displayName("Player_" + userId)
                    .imageId("")
                    .coins(defaultCoins)
                    .build();
            return repo.save(newProfile);
        });

        if (req.getDisplayName() != null) profile.setDisplayName(req.getDisplayName());
        if (req.getImageId()   != null) profile.setImageId(req.getImageId());

        return ProfileResponse.from(repo.save(profile));
    }

    @Override
    @Transactional
    public ProfileResponse createProfile(String userId, String displayName) {
        if (repo.existsById(userId))
            return ProfileResponse.from(repo.findById(userId).get());
        UserProfile profile = UserProfile.builder()
                .userId(userId)
                .displayName(displayName)
                .imageId("")
                .coins(defaultCoins)
                .build();
        return ProfileResponse.from(repo.save(profile));
    }

    @Override
    public ProfileResponse findProfile(String userId) {
        return repo.findById(userId).map(ProfileResponse::from).orElse(null);
    }

    @Override
    @Transactional
    public CoinDeductResponse deductCoins(String userId, Long amount) {
        UserProfile profile = repo.findByUserIdForUpdate(userId).orElseGet(() -> {
            UserProfile newProfile = UserProfile.builder()
                    .userId(userId)
                    .displayName("Player_" + userId)
                    .imageId("")
                    .coins(defaultCoins)
                    .build();
            return repo.save(newProfile);
        });

        if (profile.getCoins() < amount) {
            return CoinDeductResponse.builder()
                    .success(false)
                    .message("Không đủ coin. Hiện có: " + profile.getCoins() + ", cần: " + amount)
                    .remainingCoins(profile.getCoins())
                    .build();
        }

        profile.setCoins(profile.getCoins() - amount);
        repo.save(profile);

        return CoinDeductResponse.builder()
                .success(true)
                .message("Trừ coin thành công")
                .remainingCoins(profile.getCoins())
                .build();
    }

    @Override
    @Transactional
    public void addCoins(String userId, Long amount) {
        UserProfile profile = repo.findByUserIdForUpdate(userId).orElseGet(() -> {
            UserProfile newProfile = UserProfile.builder()
                    .userId(userId)
                    .displayName("Player_" + userId)
                    .imageId("")
                    .coins(defaultCoins)
                    .build();
            return repo.save(newProfile);
        });

        profile.setCoins(profile.getCoins() + amount);
        repo.save(profile);
    }
}
