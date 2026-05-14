package com.vminhkiet.shop_service.repository;

import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.stereotype.Repository;

import com.vminhkiet.shop_service.model.PlayerInfo;

import java.util.List;

@Repository
public interface PlayerItemRepository extends JpaRepository<PlayerInfo, Long> {
    
    // Tìm danh sách tất cả vật phẩm mà một người chơi đang sở hữu
    List<PlayerInfo> findByPlayerId(Long playerId);
    
    // Bạn có thể thêm hàm này để kiểm tra xem người chơi đã có vật phẩm đó chưa (để cộng dồn quantity)
    // Optional<PlayerItem> findByPlayerIdAndItemId(Long playerId, Long itemId);
}