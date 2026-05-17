package com.vminhkiet.profile_service.dto;

import com.vminhkiet.profile_service.model.UserProfile;
import lombok.Data;
import java.time.Instant;

@Data
public class ProfileResponse {
    private String userId;
    private String displayName;
    private String imageId;
    private Long   coins;
    private Instant createdAt;
    private Instant updatedAt;

    public static ProfileResponse from(UserProfile p) {
        ProfileResponse r = new ProfileResponse();
        r.userId      = p.getUserId();
        r.displayName = p.getDisplayName();
        r.imageId   = p.getImageId();
        r.coins       = p.getCoins();
        r.createdAt   = p.getCreatedAt();
        r.updatedAt   = p.getUpdatedAt();
        return r;
    }
}
