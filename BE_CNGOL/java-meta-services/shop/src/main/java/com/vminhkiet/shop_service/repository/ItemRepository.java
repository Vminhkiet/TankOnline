package com.vminhkiet.shop_service.repository;

import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.stereotype.Repository;

import com.vminhkiet.shop_service.model.Item;

import java.util.List;

@Repository
public interface ItemRepository extends JpaRepository<Item, Long> {
    
    // Tìm các vật phẩm theo category và đang còn bán
    // Lưu ý: Nếu trong file Item.java bạn đặt tên là 'available' thì sửa lại tên method bên dưới nhé
    List<Item> findByCategoryAndAvailbleTrue(String category);
    
    // Tìm tất cả vật phẩm đang còn bán
    List<Item> findByAvailbleTrue();
}