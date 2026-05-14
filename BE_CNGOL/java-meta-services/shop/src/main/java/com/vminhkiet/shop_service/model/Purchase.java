package com.vminhkiet.shop_service.model;

import jakarta.persistence.*;
import lombok.*;
import java.math.BigDecimal;
import java.time.LocalDateTime;

@Entity
@Table(name = "purchases")
@Getter
@Setter
@NoArgsConstructor
@AllArgsConstructor
@Builder
public class Purchase {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(name = "player_id", nullable = false)
    private Long playerId;

    @Column(name = "item_id", nullable = false)
    private Long itemId;

    @Column(nullable = false)
    private Integer quantity;

    @Column(name = "total_price", nullable = false)
    private BigDecimal totalPrice;

    @Column(name = "purchase_date")
    private LocalDateTime purchaseDate;

    @Column(length = 20)
    private String status; // Ví dụ: SUCCESS, FAILED, PENDING

    @PrePersist
    protected void onCreate() {
        this.purchaseDate = LocalDateTime.now();
        if (this.status == null) {
            this.status = "SUCCESS";
        }
    }
}