package com.vminhkiet.shop_service.dto;

import jakarta.validation.constraints.Min;
import jakarta.validation.constraints.NotNull;
import lombok.AllArgsConstructor;
import lombok.Data;
import lombok.NoArgsConstructor;

@Data // Tự động tạo Getter, Setter, toString, equals, hashCode
@NoArgsConstructor // Tạo constructor không tham số
@AllArgsConstructor // Tạo constructor đầy đủ tham số
public class ItemPurchaseDTO {

    @NotNull(message = "ID vật phẩm không được để trống")
    private Long itemId;

    @NotNull(message = "Số lượng không được để trống")
    @Min(value = 1, message = "Số lượng mua tối thiểu phải là 1") // Chặn trường hợp mua 0 hoặc số âm
    private Integer quantity;
}