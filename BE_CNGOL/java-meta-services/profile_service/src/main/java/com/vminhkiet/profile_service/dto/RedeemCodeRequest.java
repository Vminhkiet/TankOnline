package com.vminhkiet.profile_service.dto;

import jakarta.validation.constraints.NotBlank;
import lombok.Data;

@Data
public class RedeemCodeRequest {

    @NotBlank(message = "Code không được để trống")
    private String code;
}
