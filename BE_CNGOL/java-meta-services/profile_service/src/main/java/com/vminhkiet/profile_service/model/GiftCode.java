package com.vminhkiet.profile_service.model;

import jakarta.persistence.*;
import lombok.*;
import java.time.Instant;

@Entity
@Table(name = "gift_codes")
@Data
@NoArgsConstructor
@AllArgsConstructor
@Builder
public class GiftCode {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(unique = true, nullable = false)
    private String code;

    @Column(name = "coin_reward", nullable = false)
    private Long coinReward;

    // Tên model xe tăng tặng thưởng (null = không tặng item)
    @Column(name = "item_reward")
    private String itemReward;

    @Column(name = "max_uses", nullable = false)
    private Integer maxUses;

    @Column(name = "current_uses", nullable = false)
    private Integer currentUses;

    @Column(name = "expires_at")
    private Instant expiresAt;

    @Column(name = "is_active", nullable = false)
    private Boolean isActive;

    @Column(name = "created_at")
    private Instant createdAt;

    @PrePersist
    protected void onCreate() {
        createdAt = Instant.now();
        if (currentUses == null) currentUses = 0;
        if (isActive == null) isActive = true;
    }

    public boolean isExpired() {
        return expiresAt != null && Instant.now().isAfter(expiresAt);
    }

    public boolean isFull() {
        return currentUses >= maxUses;
    }

    public boolean isUsable() {
        return Boolean.TRUE.equals(isActive) && !isExpired() && !isFull();
    }
}
