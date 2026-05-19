package com.vminhkiet.shop_service.saga;

import com.vminhkiet.shop_service.dto.ItemPurchaseDTO;
import com.vminhkiet.shop_service.dto.PurchaseRequest;
import com.vminhkiet.shop_service.dto.PurchaseResponse;
import com.vminhkiet.shop_service.model.Item;
import com.vminhkiet.shop_service.model.PlayerInfo;
import com.vminhkiet.shop_service.repository.ItemRepository;
import com.vminhkiet.shop_service.repository.PlayerItemRepository;
import com.vminhkiet.shop_service.repository.PurchaseRepository;
import com.vminhkiet.shop_service.serviceImpl.ShopServiceImpl;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.extension.ExtendWith;
import org.mockito.ArgumentCaptor;
import org.mockito.Mock;
import org.springframework.http.HttpEntity;
import org.mockito.junit.jupiter.MockitoExtension;
import org.springframework.core.ParameterizedTypeReference;
import org.springframework.http.HttpMethod;
import org.springframework.http.ResponseEntity;
import org.springframework.test.util.ReflectionTestUtils;
import org.springframework.web.client.RestTemplate;

import java.math.BigDecimal;
import java.util.List;
import java.util.Map;
import java.util.Optional;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;
import static org.mockito.ArgumentMatchers.*;
import static org.mockito.Mockito.*;

/**
 * Saga 2 — Shop Purchase Compensation.
 * Kiểm tra ShopServiceImpl: nếu DB save lỗi sau khi đã trừ coins → phải hoàn trả coins.
 */
@ExtendWith(MockitoExtension.class)
@DisplayName("Saga 2: Shop Purchase – compensation khi DB lỗi sau khi trừ coins")
class SagaTwoPurchaseCompensationTest {

    @Mock ItemRepository       itemRepository;
    @Mock PlayerItemRepository playerItemRepository;
    @Mock PurchaseRepository   purchaseRepository;
    @Mock RestTemplate         restTemplate;

    private ShopServiceImpl shopService;

    private static final String PROFILE_URL = "http://test-profile";
    private static final String DEDUCT_URL  = PROFILE_URL + "/internal/coins/deduct";
    private static final String REFUND_URL  = PROFILE_URL + "/internal/coins/add";
    private static final Long   PLAYER_ID   = 1L;
    private static final Long   ITEM_ID     = 10L;
    private static final long   ITEM_PRICE  = 50L;

    @BeforeEach
    void setUp() {
        shopService = new ShopServiceImpl(itemRepository, playerItemRepository, purchaseRepository);
        ReflectionTestUtils.setField(shopService, "restTemplate",     restTemplate);
        ReflectionTestUtils.setField(shopService, "profileServiceUrl", PROFILE_URL);
    }

    private Item mockItem(long price) {
        return Item.builder()
                .id(ITEM_ID).name("Tank Skin")
                .price(BigDecimal.valueOf(price))
                .availble(true).build();
    }

    @SuppressWarnings("rawtypes")
    private void stubDeductSuccess() {
        when(restTemplate.exchange(
                eq(DEDUCT_URL), eq(HttpMethod.POST), any(),
                any(ParameterizedTypeReference.class)))
                .thenReturn((ResponseEntity) ResponseEntity.ok(Map.of("success", true)));
    }

    @SuppressWarnings({"unchecked", "rawtypes"})
    private void stubRefundOk() {
        when(restTemplate.exchange(
                eq(REFUND_URL), eq(HttpMethod.POST), any(),
                any(ParameterizedTypeReference.class)))
                .thenReturn((ResponseEntity) ResponseEntity.ok(Map.of("message", "refunded")));
    }

    // ── Kịch bản BÌNH THƯỜNG ────────────────────────────────────────────────

    @Test
    @DisplayName("Khi mua hàng thành công → không gọi refund, trả về success=true")
    @SuppressWarnings("rawtypes")
    void whenPurchaseSucceeds_thenNoRefundCalled() {
        when(itemRepository.findById(ITEM_ID)).thenReturn(Optional.of(mockItem(ITEM_PRICE)));
        stubDeductSuccess();
        when(playerItemRepository.save(any(PlayerInfo.class))).thenAnswer(i -> i.getArgument(0));
        when(purchaseRepository.save(any())).thenAnswer(i -> i.getArgument(0));

        PurchaseResponse resp = shopService.purchaseItem(PLAYER_ID,
                new PurchaseRequest(List.of(new ItemPurchaseDTO(ITEM_ID, 1))));

        assertThat(resp.isSuccess()).isTrue();
        assertThat(resp.getMessage()).contains("Tank Skin");
        verify(restTemplate, never()).exchange(
                eq(REFUND_URL), any(HttpMethod.class), any(),
                any(ParameterizedTypeReference.class));
    }

    // ── Kịch bản COMPENSATION ────────────────────────────────────────────────

    @Test
    @DisplayName("Khi DB save lỗi SAU khi trừ coins → hoàn trả đúng số coins")
    @SuppressWarnings({"unchecked", "rawtypes"})
    void whenDbSaveFailsAfterCoinDeduction_thenRefundCalledWithCorrectAmount() {
        when(itemRepository.findById(ITEM_ID)).thenReturn(Optional.of(mockItem(ITEM_PRICE)));
        stubDeductSuccess();
        when(playerItemRepository.save(any(PlayerInfo.class))).thenAnswer(i -> i.getArgument(0));
        when(purchaseRepository.save(any())).thenThrow(new RuntimeException("DB write failed"));
        stubRefundOk();

        assertThatThrownBy(() -> shopService.purchaseItem(PLAYER_ID,
                new PurchaseRequest(List.of(new ItemPurchaseDTO(ITEM_ID, 1)))))
                .isInstanceOf(RuntimeException.class)
                .hasMessageContaining("DB write failed");

        // Xác nhận refund được gọi với đúng userId và amount
        @SuppressWarnings({"unchecked", "rawtypes"})
        ArgumentCaptor<HttpEntity> entityCaptor = ArgumentCaptor.forClass(HttpEntity.class);
        verify(restTemplate).exchange(eq(REFUND_URL), eq(HttpMethod.POST),
                entityCaptor.capture(), any(ParameterizedTypeReference.class));

        @SuppressWarnings("unchecked")
        Map<String, Object> body = (Map<String, Object>) entityCaptor.getValue().getBody();
        assertThat(body).containsEntry("userId", PLAYER_ID.toString());
        assertThat(body).containsEntry("amount", ITEM_PRICE);
    }

    @Test
    @DisplayName("Khi lỗi ở item thứ 2 → hoàn trả tổng coins của cả 2 item đã deduct")
    @SuppressWarnings({"unchecked", "rawtypes"})
    void whenSecondItemDbFails_thenRefundsTotalBothDeducted() {
        Item skin1 = Item.builder().id(1L).name("Skin A").price(BigDecimal.valueOf(30L)).availble(true).build();
        Item skin2 = Item.builder().id(2L).name("Skin B").price(BigDecimal.valueOf(70L)).availble(true).build();

        when(itemRepository.findById(1L)).thenReturn(Optional.of(skin1));
        when(itemRepository.findById(2L)).thenReturn(Optional.of(skin2));
        when(restTemplate.exchange(eq(DEDUCT_URL), eq(HttpMethod.POST), any(),
                any(ParameterizedTypeReference.class)))
                .thenReturn((ResponseEntity) ResponseEntity.ok(Map.of("success", true)));
        when(playerItemRepository.save(any(PlayerInfo.class))).thenAnswer(i -> i.getArgument(0));
        // item 1 purchase OK, item 2 purchase FAIL
        when(purchaseRepository.save(any()))
                .thenAnswer(i -> i.getArgument(0))
                .thenThrow(new RuntimeException("DB error on item 2"));
        stubRefundOk();

        assertThatThrownBy(() -> shopService.purchaseItem(PLAYER_ID,
                new PurchaseRequest(List.of(
                        new ItemPurchaseDTO(1L, 1),
                        new ItemPurchaseDTO(2L, 1)))))
                .isInstanceOf(RuntimeException.class);

        // Refund phải = 30 (skin1) + 70 (skin2) = 100
        @SuppressWarnings({"unchecked", "rawtypes"})
        ArgumentCaptor<HttpEntity> entityCaptor = ArgumentCaptor.forClass(HttpEntity.class);
        verify(restTemplate).exchange(eq(REFUND_URL), eq(HttpMethod.POST),
                entityCaptor.capture(), any(ParameterizedTypeReference.class));

        @SuppressWarnings("unchecked")
        Map<String, Object> body = (Map<String, Object>) entityCaptor.getValue().getBody();
        assertThat(body).containsEntry("amount", 100L);
    }

    // ── Validation ───────────────────────────────────────────────────────────

    @Test
    @DisplayName("Khi danh sách items rỗng → success=false, không gọi profile_service")
    void whenEmptyPurchaseList_thenReturnFalseNoProfileCall() {
        PurchaseResponse resp = shopService.purchaseItem(PLAYER_ID,
                new PurchaseRequest(List.of()));

        assertThat(resp.isSuccess()).isFalse();
        assertThat(resp.getMessage()).containsIgnoringCase("empty");
        verifyNoInteractions(restTemplate);
    }

    @Test
    @DisplayName("Khi không đủ coins → ném exception, không lưu DB, không refund")
    @SuppressWarnings({"unchecked", "rawtypes"})
    void whenInsufficientCoins_thenExceptionAndNoDbWrite() {
        when(itemRepository.findById(ITEM_ID)).thenReturn(Optional.of(mockItem(ITEM_PRICE)));
        when(restTemplate.exchange(eq(DEDUCT_URL), eq(HttpMethod.POST), any(),
                any(ParameterizedTypeReference.class)))
                .thenReturn((ResponseEntity) ResponseEntity.ok(Map.of("success", false)));

        assertThatThrownBy(() -> shopService.purchaseItem(PLAYER_ID,
                new PurchaseRequest(List.of(new ItemPurchaseDTO(ITEM_ID, 1)))))
                .isInstanceOf(RuntimeException.class)
                .hasMessageContaining("Không đủ coin");

        verifyNoInteractions(playerItemRepository);
        verifyNoInteractions(purchaseRepository);
        // Không có refund vì chưa deduct thành công
        verify(restTemplate, never()).exchange(eq(REFUND_URL), any(), any(),
                any(ParameterizedTypeReference.class));
    }
}
