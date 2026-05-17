package com.vminhkiet.profile_service.dto;

import jakarta.validation.constraints.Size;
import lombok.Data;

@Data
public class UpdateProfileRequest {

    @Size(max = 50, message = "Display name tối đa 50 ký tự")
    private String displayName;

    @Size(max = 500, message = "Avatar URL tối đa 500 ký tự")
    private String imageId;
}
