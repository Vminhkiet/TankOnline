package com.vminhkiet.shop_service.dto;

import lombok.*;
import java.util.List;

import com.vminhkiet.shop_service.dto.ItemDTO;
@Getter
@Setter
@NoArgsConstructor
@AllArgsConstructor
@Builder
public class PlayerInventoryResponse {
    private Long playerId;
    private String playerName;
    private List<ItemDTO> items; 
}
