package com.vminhkiet.shop_service.model;

import jakarta.persistence.*;
import lombok.*;
import java.util.List;

@Entity
@Table(name = "shop_categories")
@Getter
@Setter
@NoArgsConstructor
@AllArgsConstructor
@Builder
public class Shopcategory {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(nullable = false, unique = true)
    private String name;

    private String description;

    // Quan hệ 1-nhiều với Item (Một danh mục có nhiều vật phẩm)
    @OneToMany(mappedBy = "category", cascade = CascadeType.ALL)
    private List<Item> items;
}