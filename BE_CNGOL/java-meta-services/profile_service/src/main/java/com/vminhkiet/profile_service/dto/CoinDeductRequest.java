package com.vminhkiet.profile_service.dto;

import jakarta.validation.constraints.Min;
import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.NotNull;
import lombok.Data;

@Data
public class CoinDeductRequest {

    @NotBlank
    private String userId;

    @NotNull @Min(1)
    private Long amount;
}
