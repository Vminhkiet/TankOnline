package com.vminhkiet.shop_service.controller;

import lombok.RequiredArgsConstructor;

import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.*;

import com.vminhkiet.shop_service.dto.ItemDTO;
import com.vminhkiet.shop_service.dto.ItemResponse;
import com.vminhkiet.shop_service.dto.PurchaseRequest;
import com.vminhkiet.shop_service.dto.PurchaseResponse;
import com.vminhkiet.shop_service.service.GameService;

import jakarta.validation.Valid;

import java.util.Map;
import java.util.List;

@RestController
@RequestMapping("/api/shop")
@RequiredArgsConstructor
public class ShopController {

    private final GameService shopService;

    @GetMapping("/items")
    public ResponseEntity<List<ItemDTO>> getAllItems() {
        return ResponseEntity.ok(shopService.getAllItems());
    }

    @GetMapping("/items/category/{category}")
    public ResponseEntity<List<ItemDTO>> getItemsByCategory(@PathVariable String category) {
        return ResponseEntity.ok(shopService.getItemsByCategory(category));
    }

    @GetMapping("/items/{id}")
    public ResponseEntity<ItemDTO> getItemById(@PathVariable Long id) {
        try {
            ItemDTO item = shopService.getItemById(id);

            if (item == null) {
                ItemDTO errorResponse = ItemDTO.builder()
                        .name("Lỗi")
                        .description("Không tìm thấy sản phẩm với ID: " + id)
                        .build();
                return ResponseEntity.ok(errorResponse);
            }
            return ResponseEntity.ok(item);
        } catch (Exception e) {
            // Nếu thằng Service văng lỗi 500, nó sẽ nhảy vào đây
            ItemDTO errorResponse = ItemDTO.builder()
                    .name("Lỗi hệ thống")
                    .description("Service đang bị lỗi hoặc DB trống: " + e.getMessage())
                    .build();
            return ResponseEntity.ok(errorResponse);
        }
    }

    @PostMapping("/purchase")
    public ResponseEntity<?> purchaseItem(
            @RequestHeader(value = "X-Player-Id", required = false) Long playerId,
            @Valid @RequestBody PurchaseRequest request) { // 1. Đổi Map thành PurchaseRequest và thêm @Valid

        // 2. Kiểm tra Header Player ID
        if (playerId == null) {
            return ResponseEntity.badRequest().body("Lỗi: Header X-Player-Id bị thiếu hoặc sai tên!");
        }

        // 3. Gọi Service đã sửa để xử lý mua danh sách item
        try {
            PurchaseResponse response = gameService.purchaseItem(playerId, request);
            return ResponseEntity.ok(response);
        } catch (RuntimeException e) {
            // Trả về lỗi nếu trong quá trình mua có món đồ không tồn tại hoặc không bán
            return ResponseEntity.status(HttpStatus.BAD_REQUEST).body(e.getMessage());
        }
    }

    private final GameService gameService;

    @PostMapping("/admin/items")
    public ResponseEntity<ItemDTO> createItem(@RequestBody ItemDTO itemDTO) {

        return ResponseEntity.ok(gameService.createItem(itemDTO));
    }

    @PutMapping("/admin/items/{id}")
    public ResponseEntity<?> updateItem(@PathVariable Long id, @RequestBody ItemDTO itemDTO) {
        try {
            // Vì gameService.updateItem trả về ItemDTO, nên biến hứng phải là ItemDTO
            ItemDTO response = gameService.updateItem(id, itemDTO);
            return ResponseEntity.ok(response);
        } catch (Exception e) {
            // Nếu lỗi (không tìm thấy ID), tạo một ItemDTO lỗi để trả về 200
            ItemDTO errorResponse = ItemDTO.builder()
                    .id(id)
                    .name("ERROR: Không tìm thấy ID này")
                    .description(e.getMessage())
                    .build();
            return ResponseEntity.ok(errorResponse);
        }
    }

    @DeleteMapping("/admin/items/{id}")
    public ResponseEntity<String> deleteItem(@PathVariable Long id) {

        gameService.deleteItem(id);
        return ResponseEntity.ok("Xóa thành công vật phẩm ID: " + id);
    }
}