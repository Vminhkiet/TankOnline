package com.vminhkiet.shop_service.dto;

import lombok.*;

import java.math.BigDecimal;
import java.time.LocalDateTime;

@Getter
@Setter
@Builder
@NoArgsConstructor
@AllArgsConstructor
public class PurchaseResponse {
    private boolean success;
    private String message;
    private BigDecimal totalPrice;
    private LocalDateTime purchasedAt;
}
