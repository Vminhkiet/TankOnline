package com.vminhkiet.shop_service.dto;
import lombok.*;
import java.math.BigDecimal;
@Data
@Getter
@Setter
@NoArgsConstructor
@AllArgsConstructor
@Builder
public class ItemDTO {
    private Long id;
    private String name;
    private String description; 
    private String imageUrl;
    private BigDecimal price;
    private String category;
    private Boolean available; 
}