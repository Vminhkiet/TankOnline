package com.vminhkiet.shop_service.util; 
import org.springframework.stereotype.Component;

import com.vminhkiet.shop_service.dto.ItemDTO;
import com.vminhkiet.shop_service.model.Item;

@Component
public class ItemMapper {

    public ItemDTO toDTO(Item item) {
        if (item == null) {
            return null;
        }

        return ItemDTO.builder()
                .id(item.getId())
                .name(item.getName())
                .description(item.getDescription())
                .imageUrl(item.getImageUrl())
                .price(item.getPrice())
                // Kiểm tra category nếu nó là Object thì dùng toString(), nếu là String thì để nguyên
                .category(item.getCategory() != null ? item.getCategory().toString() : null)
                .available(item.getAvailble())
                .status(Boolean.TRUE.equals(item.getAvailble()) ? "On Sale" : "Discontinued")
                .damage(item.getDamage())
                .armor(item.getArmor())
                .speed(item.getSpeed())
                .health(item.getHealth())
                .fireRate(item.getFireRate())
                .fireRange(item.getFireRange())
                .build();
    }
}
