using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// Handles client-side purchase flow for selected tank.
/// Requires TankSelectionManager + ProfileUIManager references.
/// </summary>
public class TankPurchaseManager : MonoBehaviour
{
    [Serializable]
    private class PurchaseItemRequest
    {
        public int itemId;
        public int quantity;
    }

    [Serializable]
    private class PurchaseRequest
    {
        public PurchaseItemRequest[] items;
    }

    [Serializable]
    private class PurchaseResponse
    {
        public bool success;
        public string message;
        public float totalPrice;
        public string purchasedAt;
    }

    [Serializable]
    private class MyItemsResponseWrapper
    {
        public long[] itemIds;
    }

    private const string PurchasePath = "/api/shop/purchase";
    private const string MyItemsPath = "/api/shop/my-items";
    private const string PlayerIdKey = "profile_player_id";

    [Header("References")]
    [SerializeField] private TankSelectionManager tankSelectionManager;
    [SerializeField] private ProfileUIManager profileUIManager;

    [Header("UI")]
    [SerializeField] private Button buyButton;
    [SerializeField] private TMP_Text buyButtonText;
    [SerializeField] private string buyingLabel = "Purchasing...";
    [SerializeField] private string ownedLabel = "Owned";
    [SerializeField] private TMP_Text statusText;

    private bool isPurchasing;
    private readonly System.Collections.Generic.HashSet<int> ownedItemIds = new System.Collections.Generic.HashSet<int>();
    private bool ownershipLoadedFromServer;

    private void Awake()
    {
        if (buyButton != null)
            buyButton.onClick.AddListener(OnBuyButtonClicked);
    }

    private void OnEnable()
    {
        if (tankSelectionManager != null)
            tankSelectionManager.SelectionChanged += HandleSelectionChanged;

        StartCoroutine(LoadOwnedItemsCoroutine());
        RefreshBuyState();
        ClearStatus();
    }

    private void OnDisable()
    {
        if (tankSelectionManager != null)
            tankSelectionManager.SelectionChanged -= HandleSelectionChanged;
    }

    private void OnDestroy()
    {
        if (buyButton != null)
            buyButton.onClick.RemoveListener(OnBuyButtonClicked);
    }

    private void HandleSelectionChanged(TankDefinitionSO _)
    {
        RefreshBuyState();
        ClearStatus();
    }

    public void OnBuyButtonClicked()
    {
        if (isPurchasing)
            return;

        StartCoroutine(PurchaseSelectedTankCoroutine());
    }

    private IEnumerator PurchaseSelectedTankCoroutine()
    {
        if (tankSelectionManager == null)
        {
            SetStatus("Thiếu TankSelectionManager.");
            yield break;
        }

        if (!tankSelectionManager.TryGetSelectedShopItem(out int itemId, out _, out bool available))
        {
            SetStatus("Không lấy được thông tin vật phẩm đang chọn.");
            yield break;
        }

        if (!available)
        {
            SetStatus("Vật phẩm hiện không khả dụng.");
            yield break;
        }

        if (ownershipLoadedFromServer && ownedItemIds.Contains(itemId))
        {
            SetStatus("Bạn đã sở hữu vật phẩm này.");
            RefreshBuyState();
            yield break;
        }

        long playerId = ResolvePlayerId();
        if (playerId <= 0)
        {
            SetStatus("Thiếu playerId để mua hàng.");
            yield break;
        }

        isPurchasing = true;
        RefreshBuyState();
        SetStatus("Đang gửi yêu cầu mua...");

        PurchaseRequest payload = new PurchaseRequest
        {
            items = new[]
            {
                new PurchaseItemRequest { itemId = itemId, quantity = 1 }
            }
        };

        string json = JsonUtility.ToJson(payload);

        using (UnityWebRequest req = GameApiClient.CreateRequest(PurchasePath, UnityWebRequest.kHttpVerbPOST, json))
        {
            req.SetRequestHeader("X-Player-Id", playerId.ToString());

            GameApiClient.ApiCallResult result = default;
            yield return GameApiClient.Send(req, r => result = r);

            isPurchasing = false;

            if (!result.Success)
            {
                SetStatus(string.IsNullOrWhiteSpace(result.Body)
                    ? ("Mua thất bại: " + result.ErrorMessage)
                    : result.Body);
                RefreshBuyState();
                yield break;
            }

            PurchaseResponse response = null;
            try
            {
                response = JsonUtility.FromJson<PurchaseResponse>(result.Body);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Shop] Parse purchase response failed: " + ex.Message);
            }

            if (response != null && !response.success)
            {
                SetStatus(string.IsNullOrWhiteSpace(response.message) ? "Mua không thành công." : response.message);
                RefreshBuyState();
                yield break;
            }

            ownedItemIds.Add(itemId);
            SetStatus(response != null && !string.IsNullOrWhiteSpace(response.message)
                ? response.message
                : "Mua thành công.");

            if (profileUIManager != null)
                profileUIManager.Refresh();

            yield return LoadOwnedItemsCoroutine();
            RefreshBuyState();
        }
    }

    private void RefreshBuyState()
    {
        bool canBuy = false;
        bool isOwned = false;

        if (tankSelectionManager != null &&
            tankSelectionManager.TryGetSelectedShopItem(out int itemId, out _, out bool available))
        {
            isOwned = ownershipLoadedFromServer && ownedItemIds.Contains(itemId);
            canBuy = ownershipLoadedFromServer && available && !isOwned;
        }

        if (buyButton != null)
            buyButton.interactable = !isPurchasing && canBuy;

        if (buyButtonText != null)
        {
            if (isPurchasing)
                buyButtonText.text = buyingLabel;
            else if (isOwned)
                buyButtonText.text = ownedLabel;
        }

        if (!ownershipLoadedFromServer)
        {
            if (buyButton != null)
                buyButton.interactable = false;
        }
    }

    private long ResolvePlayerId()
    {
        if (profileUIManager != null && profileUIManager.CurrentProfile != null)
        {
            string userIdRaw = profileUIManager.CurrentProfile.userId;
            if (long.TryParse(userIdRaw, out long parsedFromProfile) && parsedFromProfile > 0)
            {
                PlayerPrefs.SetString(PlayerIdKey, parsedFromProfile.ToString());
                PlayerPrefs.Save();
                return parsedFromProfile;
            }
        }

        string cached = PlayerPrefs.GetString(PlayerIdKey, "");
        if (long.TryParse(cached, out long parsedCached) && parsedCached > 0)
            return parsedCached;

        return 0;
    }

    private IEnumerator LoadOwnedItemsCoroutine()
    {
        ownershipLoadedFromServer = false;
        ownedItemIds.Clear();

        long playerId = ResolvePlayerId();
        if (playerId <= 0)
        {
            SetStatus("Thiếu playerId để tải vật phẩm đã mua.");
            yield break;
        }

        using (UnityWebRequest req = GameApiClient.CreateRequest(MyItemsPath, UnityWebRequest.kHttpVerbGET))
        {
            req.SetRequestHeader("X-Player-Id", playerId.ToString());

            GameApiClient.ApiCallResult result = default;
            yield return GameApiClient.Send(req, r => result = r);

            if (!result.Success)
            {
                SetStatus("Không tải được danh sách đã mua: " + result.ErrorMessage);
                yield break;
            }

            string json = result.Body?.Trim();
            if (string.IsNullOrEmpty(json))
            {
                ownershipLoadedFromServer = true;
                yield break;
            }

            try
            {
                if (json.StartsWith("["))
                {
                    string wrapped = "{\"itemIds\":" + json + "}";
                    MyItemsResponseWrapper wrapper = JsonUtility.FromJson<MyItemsResponseWrapper>(wrapped);
                    if (wrapper != null && wrapper.itemIds != null)
                    {
                        for (int i = 0; i < wrapper.itemIds.Length; i++)
                            ownedItemIds.Add((int)wrapper.itemIds[i]);
                    }

                    ownershipLoadedFromServer = true;
                    yield break;
                }

                SetStatus("Dữ liệu my-items không đúng định dạng.");
            }
            catch (Exception ex)
            {
                SetStatus("Lỗi parse my-items: " + ex.Message);
            }
        }
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
    }

    private void ClearStatus()
    {
        if (statusText != null)
            statusText.text = string.Empty;
    }
}
