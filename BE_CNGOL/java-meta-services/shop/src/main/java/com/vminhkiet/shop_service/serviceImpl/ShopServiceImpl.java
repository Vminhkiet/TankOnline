package com.vminhkiet.shop_service.serviceImpl;

import lombok.RequiredArgsConstructor;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import com.vminhkiet.shop_service.dto.*;
import com.vminhkiet.shop_service.model.Item;
import com.vminhkiet.shop_service.model.PlayerInfo;
import com.vminhkiet.shop_service.model.Purchase;
import com.vminhkiet.shop_service.repository.ItemRepository;
import com.vminhkiet.shop_service.repository.PlayerItemRepository;
import com.vminhkiet.shop_service.repository.PurchaseRepository;
import com.vminhkiet.shop_service.service.GameService;

import java.math.BigDecimal;
import java.time.LocalDateTime;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.concurrent.atomic.AtomicLong;
import java.util.stream.Collectors;
import org.springframework.web.client.RestTemplate;
import org.springframework.http.*;
import org.springframework.beans.factory.annotation.Value;

@Service
@RequiredArgsConstructor
public class ShopServiceImpl implements GameService {

        private final ItemRepository itemRepository;
        private final PlayerItemRepository playerItemRepository;
        private final PurchaseRepository purchaseRepository;
        private static final String STATUS_ON_SALE = "On Sale";
        private static final String STATUS_DISCONTINUED = "Discontinued";
        private final AtomicLong shopVersion = new AtomicLong(0);
        private final RestTemplate restTemplate = new RestTemplate();

        @Value("${profile.service.url:http://localhost:8087}")
        private String profileServiceUrl;

        @Override
        public List<ItemDTO> getAllItems() {
                return itemRepository.findByAvailbleTrue().stream()
                                .map(this::convertToDTO)
                                .collect(Collectors.toList());
        }

        @Override
        public List<ItemDTO> getItemsByCategory(String category) {
                return itemRepository.findByCategoryAndAvailbleTrue(category).stream()
                                .map(this::convertToDTO)
                                .collect(Collectors.toList());
        }

        @Override
        public ItemDTO getItemById(Long id) {
                Item item = itemRepository.findById(id)
                                .orElseThrow(() -> new RuntimeException("Item not found"));
                return convertToDTO(item);
        }

        @Override
        @Transactional
        public PurchaseResponse purchaseItem(Long playerId, PurchaseRequest request) {
                // 1. Kiểm tra danh sách request có trống không
                if (request.getItems() == null || request.getItems().isEmpty()) {
                        return PurchaseResponse.builder()
                                        .success(false)
                                        .message("Purchase list is empty")
                                        .build();
                }

                BigDecimal totalOrderPrice = BigDecimal.ZERO;
                List<String> messages = new ArrayList<>();

                // 2. Lặp qua danh sách các DTO mới (ItemPurchaseDTO)
                for (ItemPurchaseDTO itemRequest : request.getItems()) {

                        // Tìm vật phẩm
                        Item item = itemRepository.findById(itemRequest.getItemId())
                                        .orElseThrow(() -> new RuntimeException(
                                                        "Item not found: " + itemRequest.getItemId()));

                        // Kiểm tra tính khả dụng
                        if (item.getAvailble() == null || !item.getAvailble()) {
                                throw new RuntimeException("Item " + item.getName() + " is not available");
                        }

                        // 3. Tính tiền cho từng loại item và cộng dồn
                        BigDecimal itemTotalPrice = item.getPrice()
                                        .multiply(BigDecimal.valueOf(itemRequest.getQuantity()));
                        totalOrderPrice = totalOrderPrice.add(itemTotalPrice);

                        // Trừ coin từ profile_service
                        boolean deductOk = deductCoinsFromProfile(String.valueOf(playerId), itemTotalPrice.longValue());
                        if (!deductOk) {
                            throw new RuntimeException("Không đủ coin để mua: " + item.getName());
                        }

                        // 4. Lưu vào kho đồ (Inventory) - Chỗ này dùng PlayerInfo theo code cũ của bạn
                        PlayerInfo playerItem = PlayerInfo.builder()
                                        .playerId(playerId)
                                        .item(item)
                                        .quantity(itemRequest.getQuantity())
                                        .purchasedAt(LocalDateTime.now())
                                        .build();
                        playerItemRepository.save(playerItem);

                        // 5. Lưu vào lịch sử giao dịch (Purchases) cho từng item
                        Purchase purchase = Purchase.builder()
                                        .playerId(playerId)
                                        .itemId(item.getId())
                                        .quantity(itemRequest.getQuantity())
                                        .totalPrice(itemTotalPrice)
                                        .status("SUCCESS")
                                        .purchaseDate(LocalDateTime.now())
                                        .build();
                        purchaseRepository.save(purchase);

                        messages.add(item.getName() + " (x" + itemRequest.getQuantity() + ")");
                }

                // 6. Trả về kết quả tổng hợp
                return PurchaseResponse.builder()
                                .success(true)
                                .message("Purchased successfully: " + String.join(", ", messages))
                                .totalPrice(totalOrderPrice)
                                .purchasedAt(LocalDateTime.now())
                                .build();
        }

        // Helper method để convert Entity sang DTO
        private ItemDTO convertToDTO(Item item) {
                return ItemDTO.builder()
                                .id(item.getId())
                                .name(item.getName())
                                .description(item.getDescription())
                                .imageUrl(item.getImageUrl())
                                .price(item.getPrice())
                                .category(item.getCategory() != null ? item.getCategory().toString() : null)
                                .available(item.getAvailble())
                                .status(getItemStatus(item))
                                .build();
        }

        private boolean deductCoinsFromProfile(String userId, long amount) {
            try {
                String url = profileServiceUrl + "/internal/coins/deduct";
                HttpHeaders headers = new HttpHeaders();
                headers.setContentType(MediaType.APPLICATION_JSON);
                Map<String, Object> body = Map.of("userId", userId, "amount", amount);
                HttpEntity<Map<String, Object>> entity = new HttpEntity<>(body, headers);
                ResponseEntity<Map> response = restTemplate.postForEntity(url, entity, Map.class);
                if (response.getStatusCode() == HttpStatus.OK && response.getBody() != null) {
                    return Boolean.TRUE.equals(response.getBody().get("success"));
                }
                return false;
            } catch (Exception e) {
                // Nếu profile_service không khả dụng, cho phép mua (graceful degradation)
                return true;
            }
        }

        private String getItemStatus(Item item) {
                return Boolean.TRUE.equals(item.getAvailble()) ? STATUS_ON_SALE : STATUS_DISCONTINUED;
        }

        @Override
        @Transactional
        public ItemDTO createItem(ItemDTO itemDTO) {
                // Chuyển từ DTO sang Entity để lưu vào DB
                Item item = Item.builder()
                                .name(itemDTO.getName())
                                .description(itemDTO.getDescription())
                                .imageUrl(itemDTO.getImageUrl())
                                .price(itemDTO.getPrice())
                                .availble(itemDTO.getAvailable())
                                .category(itemDTO.getCategory())
                                .build();

                Item savedItem = itemRepository.save(item);
                shopVersion.incrementAndGet();
                return convertToDTO(savedItem);
        }

        @Override
        @Transactional
        public ItemDTO updateItem(Long id, ItemDTO itemDTO) {
                // 1. Kiểm tra xem item có tồn tại không
                Item item = itemRepository.findById(id)
                                .orElseThrow(() -> new RuntimeException("Item không tồn tại để cập nhật"));

                // 2. Cập nhật thông tin mới
                item.setName(itemDTO.getName());
                item.setDescription(itemDTO.getDescription());
                item.setImageUrl(itemDTO.getImageUrl());
                item.setPrice(itemDTO.getPrice());
                item.setAvailble(itemDTO.getAvailable());
                item.setCategory(itemDTO.getCategory());
                // 3. Lưu lại (Hàm save sẽ tự hiểu là Update vì đã có ID)
                Item updatedItem = itemRepository.save(item);
                shopVersion.incrementAndGet();
                return convertToDTO(updatedItem);
        }

        @Override
        @Transactional
        public void deleteItem(Long id) {
                // Cách 1: Xóa mềm (An toàn cho dữ liệu lịch sử)
                Item item = itemRepository.findById(id)
                                .orElseThrow(() -> new RuntimeException("Item không tồn tại để xóa"));
                item.setAvailble(false);
                itemRepository.save(item);
                shopVersion.incrementAndGet();

                /*
                 * Cách 2: Xóa vĩnh viễn (Chỉ dùng khi chưa có giao dịch nào liên quan)
                 * itemRepository.deleteById(id);
                 */
        }

        @Override
        public long getShopVersion() {
                return shopVersion.get();
        }
}
