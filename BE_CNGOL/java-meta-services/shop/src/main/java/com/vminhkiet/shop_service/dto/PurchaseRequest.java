package com.vminhkiet.shop_service.dto;

import java.util.List;
import jakarta.validation.constraints.Min;
import jakarta.validation.constraints.NotNull;
import lombok.*;

@Data
@NoArgsConstructor
@AllArgsConstructor
@Builder
public class PurchaseRequest {

    private List<ItemPurchaseDTO> items;
    // @NotNull
    // private Long itemId;

    // @Min(1)
    // private int quantity;
}