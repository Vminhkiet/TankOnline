package com.vminhkiet.profile_service.dto;

import lombok.Builder;
import lombok.Data;

@Data
@Builder
public class RedeemCodeResponse {
    private boolean success;
    private String  message;
    private Long    coinsEarned;
    private String  itemEarned;
    private Long    totalCoins;
}
