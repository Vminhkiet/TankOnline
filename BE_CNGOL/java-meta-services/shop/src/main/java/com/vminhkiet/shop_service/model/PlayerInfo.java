package com.vminhkiet.shop_service.model;

import jakarta.persistence.*;
import lombok.*;
import java.time.LocalDateTime;

@Entity
@Table(name = "player_items")
@Getter
@Setter
@NoArgsConstructor
@AllArgsConstructor
@Builder
public class PlayerInfo {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    private Long playerId; 

    // XÓA DÒNG NÀY: private Long itemId; <--- ĐÂY LÀ NGUYÊN NHÂN GÂY LỖI

    @ManyToOne(fetch = FetchType.LAZY)
    @JoinColumn(name = "item_id") // Hibernate sẽ tự quản lý ID thông qua object này
    private Item item;

    private int quantity = 1;
    private LocalDateTime purchasedAt;
}