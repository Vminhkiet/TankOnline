package com.vminhkiet.profile_service.model;

import jakarta.persistence.*;
import lombok.*;
import java.time.Instant;

@Entity
@Table(name = "gift_code_redemptions",
       uniqueConstraints = @UniqueConstraint(columnNames = {"gift_code_id", "user_id"}))
@Data
@NoArgsConstructor
@AllArgsConstructor
@Builder
public class GiftCodeRedemption {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @ManyToOne(fetch = FetchType.LAZY)
    @JoinColumn(name = "gift_code_id", nullable = false)
    private GiftCode giftCode;

    @Column(name = "user_id", nullable = false)
    private String userId;

    @Column(name = "redeemed_at")
    private Instant redeemedAt;

    @PrePersist
    protected void onCreate() {
        redeemedAt = Instant.now();
    }
}
