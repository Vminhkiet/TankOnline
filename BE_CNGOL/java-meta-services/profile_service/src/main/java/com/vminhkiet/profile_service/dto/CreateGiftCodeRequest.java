package com.vminhkiet.profile_service.dto;

import jakarta.validation.constraints.*;
import lombok.Data;
import java.time.Instant;

@Data
public class CreateGiftCodeRequest {

    @NotBlank
    private String code;

    @NotNull @Min(0)
    private Long coinReward;

    private String itemReward;

    @NotNull @Min(1)
    private Integer maxUses;

    private Instant expiresAt;
}
