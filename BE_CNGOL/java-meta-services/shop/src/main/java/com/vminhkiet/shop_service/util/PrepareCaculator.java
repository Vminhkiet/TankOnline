package com.vminhkiet.shop_service.util;

import java.math.BigDecimal;
import org.springframework.stereotype.Component;

@Component
public class PrepareCaculator {

    /**
     * Tính tổng tiền: Đơn giá * Số lượng
     */
    public BigDecimal calculateTotal(BigDecimal price, int quantity) {
        if (price == null) {
            return BigDecimal.ZERO;
        }
        return price.multiply(BigDecimal.valueOf(quantity));
    }

    /**
     * Tính tổng tiền sau khi áp dụng giảm giá (ví dụ: giảm x %)
     */
    public BigDecimal calculateTotalWithDiscount(BigDecimal price, int quantity, double discountPercent) {
        BigDecimal subTotal = calculateTotal(price, quantity);
        BigDecimal discountAmount = subTotal.multiply(BigDecimal.valueOf(discountPercent / 100));
        return subTotal.subtract(discountAmount);
    }
}