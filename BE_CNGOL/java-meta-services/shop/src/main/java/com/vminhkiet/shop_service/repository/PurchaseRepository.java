package com.vminhkiet.shop_service.repository;

import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.stereotype.Repository;

import com.vminhkiet.shop_service.model.Purchase;

import java.util.List;

@Repository
public interface PurchaseRepository extends JpaRepository<Purchase, Long> {

    // Lấy lịch sử mua hàng của một người chơi cụ thể, sắp xếp theo ngày mới nhất
    List<Purchase> findByPlayerIdOrderByPurchaseDateDesc(Long playerId);

    // Tìm các giao dịch mua một vật phẩm cụ thể của người chơi
    List<Purchase> findByPlayerIdAndItemId(Long playerId, Long itemId);
    
    // Thống kê các giao dịch thành công/thất bại (nếu bạn có trường status)
    List<Purchase> findByStatus(String status);
}