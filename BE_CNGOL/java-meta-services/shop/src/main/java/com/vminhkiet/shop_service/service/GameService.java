package com.vminhkiet.shop_service.service;

import java.util.List;

import com.vminhkiet.shop_service.dto.ItemDTO;
import com.vminhkiet.shop_service.dto.PurchaseRequest;
import com.vminhkiet.shop_service.dto.PurchaseResponse;

public interface GameService {
    List<ItemDTO> getAllItems();

    List<ItemDTO> getItemsByCategory(String category);

    ItemDTO getItemById(Long id);

    PurchaseResponse purchaseItem(Long playerId, PurchaseRequest request);

    ItemDTO createItem(ItemDTO itemDTO);

    ItemDTO updateItem(Long id, ItemDTO itemDTO);

    void deleteItem(Long id);

    long getShopVersion();

    List<Long> getPurchasedItemIds(Long playerId);

    void deployItem(Long playerId, Long itemId);

    Long getDeployedTankId(Long playerId);
}
