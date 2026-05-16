using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class TankSelectionManager : MonoBehaviour
{
    [Serializable]
    private class UITextBinding
    {
        [SerializeField] private Text legacyText;
        [SerializeField] private TMP_Text tmpText;

        public void SetText(string value)
        {
            if (legacyText != null)
                legacyText.text = value;

            if (tmpText != null)
                tmpText.text = value;
        }
    }

    [Header("Available Tanks")]
    [SerializeField] private string shopApiUrl = "http://localhost:8080/api/shop/items";
    [SerializeField] private bool useCachedShopItems = true;
    [SerializeField, Min(0f)] private float shopItemsCacheDurationSeconds = 300f;
    [SerializeField] private TankDefinitionSO[] availableTanks;
    [SerializeField] private TankDefinitionSO defaultTank;

    [Header("Preview")]
    [SerializeField] private Transform previewAnchor;
    [SerializeField] private bool disablePreviewScripts = true;
    [SerializeField] private bool disablePreviewPhysics = true;
    [SerializeField] private bool disablePreviewAudio = true;
    [SerializeField] private bool disablePreviewCanvases = true;

    [Header("Tank Info")]
    [SerializeField] private UITextBinding tankNameText = new UITextBinding();
    [SerializeField] private UITextBinding descriptionText = new UITextBinding();
    [SerializeField] private UITextBinding priceText = new UITextBinding();
    [SerializeField] private string priceFormat = "{0}";

    [Header("Special Ability")]
    [SerializeField] private UITextBinding abilityNameText = new UITextBinding();
    [SerializeField] private UITextBinding abilityDescriptionText = new UITextBinding();
    [SerializeField] private Image abilityIcon;

    [Header("Stat Bars")]
    [SerializeField] private TankStatBar damageBar;
    [SerializeField] private TankStatBar armorBar;
    [SerializeField] private TankStatBar speedBar;
    [SerializeField] private TankStatBar fireRateBar;
    [FormerlySerializedAs("mobilityBar")]
    [SerializeField] private TankStatBar fireRangeBar;

    public TankDefinitionSO CurrentTank { get; private set; }
    public Transform PreviewAnchor => previewAnchor;

    public event Action<TankDefinitionSO> SelectionChanged;

    private const string ShopItemsCacheJsonKey = "shop_items_cache_json";
    private const string ShopItemsCacheTimestampKey = "shop_items_cache_timestamp";

    private GameObject currentPreviewInstance;
    private readonly Dictionary<string, ShopItemDTO> shopItemsByName = new Dictionary<string, ShopItemDTO>(StringComparer.OrdinalIgnoreCase);

    private IEnumerator Start()
    {
        bool hasUsableCache = useCachedShopItems && TryLoadCachedShopItems();

        if (!hasUsableCache || IsShopItemsCacheExpired())
        {
            yield return StartCoroutine(FetchAvailableTanksFromAPI());
        }

        TankDefinitionSO startupTank = defaultTank != null ? defaultTank : GetFirstAvailableTank();

        if (startupTank != null)
        {
            SelectTankInternal(startupTank, false);
            yield break;
        }

        RefreshUI(null);
    }

    [System.Serializable]
    private class ShopItemDTO
    {
        public int id;
        public string name;
        public string description;
        public string imageUrl;
        public float price;
        public string category;
        public bool available;
    }

    [System.Serializable]
    private class ShopItemArrayWrapper
    {
        public ShopItemDTO[] array;
    }

    private bool TryLoadCachedShopItems()
    {
        string cachedJson = PlayerPrefs.GetString(ShopItemsCacheJsonKey, "");

        if (string.IsNullOrWhiteSpace(cachedJson))
            return false;

        return TryApplyShopItemsJson(cachedJson);
    }

    private bool IsShopItemsCacheExpired()
    {
        if (!useCachedShopItems)
            return true;

        if (shopItemsCacheDurationSeconds <= 0f)
            return true;

        string timestampValue = PlayerPrefs.GetString(ShopItemsCacheTimestampKey, "");

        if (!long.TryParse(timestampValue, out long cachedUnixTime))
            return true;

        long nowUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return nowUnixTime - cachedUnixTime >= shopItemsCacheDurationSeconds;
    }

    private IEnumerator FetchAvailableTanksFromAPI()
    {
        string jwt = PlayerPrefs.GetString("jwt", "");

        if (string.IsNullOrEmpty(jwt))
        {
            Debug.LogWarning("Fetching shop items without JWT. Login first if the shop API requires authentication.");
        }

        using (UnityWebRequest request = UnityWebRequest.Get(shopApiUrl))
        {
            request.SetRequestHeader("Accept", "application/json");

            if (!string.IsNullOrEmpty(jwt))
            {
                request.SetRequestHeader("Authorization", "Bearer " + jwt);
            }

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                string json = request.downloadHandler.text;
                string cachedJson = PlayerPrefs.GetString(ShopItemsCacheJsonKey, "");

                if (useCachedShopItems && json == cachedJson)
                {
                    PlayerPrefs.SetString(ShopItemsCacheTimestampKey, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
                    PlayerPrefs.Save();
                    yield break;
                }

                if (TryApplyShopItemsJson(json) && useCachedShopItems)
                {
                    PlayerPrefs.SetString(ShopItemsCacheJsonKey, json);
                    PlayerPrefs.SetString(ShopItemsCacheTimestampKey, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
                    PlayerPrefs.Save();
                }
            }
            else
            {
                Debug.LogError(
                    $"Failed to fetch shop items from {shopApiUrl}: HTTP {request.responseCode} - {request.error}\n" +
                    $"Response: {request.downloadHandler.text}");
            }
        }
    }

    private bool TryApplyShopItemsJson(string json)
    {
        try
        {
            string wrappedJson = "{\"array\":" + json + "}";
            ShopItemArrayWrapper wrapper = JsonUtility.FromJson<ShopItemArrayWrapper>(wrappedJson);

            if (wrapper == null || wrapper.array == null)
                return false;

            UpdateAvailableTanks(wrapper.array);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("Failed to parse shop items: " + e.Message);
            return false;
        }
    }

    private void UpdateAvailableTanks(ShopItemDTO[] apiItems)
    {
        TankSelectionButton[] allButtons = FindObjectsOfType<TankSelectionButton>(true);
        List<TankDefinitionSO> newAvailableTanks = new List<TankDefinitionSO>();
        shopItemsByName.Clear();

        foreach (var apiItem in apiItems)
        {
            if (apiItem != null && !string.IsNullOrWhiteSpace(apiItem.name))
            {
                shopItemsByName[apiItem.name] = apiItem;
            }
        }

        foreach (var button in allButtons)
        {
            if (button.TankData != null)
            {
                bool isAvailable = false;

                if (shopItemsByName.TryGetValue(button.TankData.TankName, out ShopItemDTO apiItem))
                {
                    isAvailable = apiItem.available;
                }

                button.gameObject.SetActive(isAvailable);
                
                if (isAvailable && !newAvailableTanks.Contains(button.TankData))
                {
                    newAvailableTanks.Add(button.TankData);
                }
            }
        }

        availableTanks = newAvailableTanks.ToArray();
        
        if (defaultTank != null && !newAvailableTanks.Contains(defaultTank))
        {
            defaultTank = newAvailableTanks.Count > 0 ? newAvailableTanks[0] : null;
        }
    }

    private void OnDestroy()
    {
        ClearPreview();
    }

    public void SelectTankByIndex(int index)
    {
        if (availableTanks == null || index < 0 || index >= availableTanks.Length)
        {
            Debug.LogWarning($"TankSelectionManager received an invalid tank index: {index}.", this);
            return;
        }

        SelectTank(availableTanks[index]);
    }

    public void SelectTank(TankDefinitionSO tankData)
    {
        SelectTankInternal(tankData, true);
    }

    private void SelectTankInternal(TankDefinitionSO tankData, bool playClickSound)
    {
        if (tankData == null)
        {
            Debug.LogWarning("TankSelectionManager received a null tank definition.", this);
            return;
        }

        CurrentTank = tankData;
        SpawnPreview(tankData);
        RefreshUI(tankData);
        SelectionChanged?.Invoke(tankData);

        if (playClickSound)
            UIAudioManager.Instance?.PlayClick();
    }

    public GameObject GetSelectedGameplayPrefab()
    {
        return CurrentTank != null ? CurrentTank.GameplayPrefab : null;
    }

    private TankDefinitionSO GetFirstAvailableTank()
    {
        if (availableTanks == null)
            return null;

        for (int i = 0; i < availableTanks.Length; i++)
        {
            if (availableTanks[i] != null)
                return availableTanks[i];
        }

        return null;
    }

    private void SpawnPreview(TankDefinitionSO tankData)
    {
        ClearPreview();

        if (previewAnchor == null)
        {
            Debug.LogWarning("TankSelectionManager is missing a preview anchor transform.", this);
            return;
        }

        GameObject previewPrefab = tankData.PreviewPrefab;

        if (previewPrefab == null)
        {
            Debug.LogWarning($"Tank '{tankData.TankName}' does not have a preview prefab assigned.", this);
            return;
        }

        currentPreviewInstance = Instantiate(previewPrefab, previewAnchor);
        currentPreviewInstance.name = previewPrefab.name + "_Preview";

        Transform previewTransform = currentPreviewInstance.transform;
        previewTransform.localPosition = tankData.PreviewLocalPosition;
        previewTransform.localRotation = Quaternion.Euler(tankData.PreviewLocalEulerAngles);
        previewTransform.localScale = tankData.PreviewLocalScale;

        PreparePreviewObject(currentPreviewInstance);
    }

    private void PreparePreviewObject(GameObject previewObject)
    {
        if (disablePreviewScripts)
        {
            MonoBehaviour[] behaviours = previewObject.GetComponentsInChildren<MonoBehaviour>(true);

            for (int i = 0; i < behaviours.Length; i++)
            {
                behaviours[i].enabled = false;
            }
        }

        if (disablePreviewPhysics)
        {
            Rigidbody[] rigidbodies = previewObject.GetComponentsInChildren<Rigidbody>(true);

            for (int i = 0; i < rigidbodies.Length; i++)
            {
                rigidbodies[i].isKinematic = true;
                rigidbodies[i].detectCollisions = false;
            }

            Collider[] colliders = previewObject.GetComponentsInChildren<Collider>(true);

            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }
        }

        if (disablePreviewAudio)
        {
            AudioSource[] audioSources = previewObject.GetComponentsInChildren<AudioSource>(true);

            for (int i = 0; i < audioSources.Length; i++)
            {
                audioSources[i].enabled = false;
            }
        }

        if (disablePreviewCanvases)
        {
            Canvas[] canvases = previewObject.GetComponentsInChildren<Canvas>(true);

            for (int i = 0; i < canvases.Length; i++)
            {
                canvases[i].enabled = false;
            }
        }
    }

    private void ClearPreview()
    {
        if (currentPreviewInstance == null)
            return;

        Destroy(currentPreviewInstance);
        currentPreviewInstance = null;
    }

    private void RefreshUI(TankDefinitionSO tankData)
    {
        if (tankData == null)
        {
            tankNameText.SetText(string.Empty);
            descriptionText.SetText(string.Empty);
            priceText.SetText(string.Empty);
            abilityNameText.SetText(string.Empty);
            abilityDescriptionText.SetText(string.Empty);

            if (abilityIcon != null)
            {
                abilityIcon.sprite = null;
                abilityIcon.enabled = false;
            }

            ApplyStats(default(TankStats));
            return;
        }

        tankNameText.SetText(tankData.TankName);
        descriptionText.SetText(GetDisplayDescription(tankData));
        priceText.SetText(FormatPrice(GetDisplayPrice(tankData)));
        abilityNameText.SetText(tankData.SpecialAbility.AbilityName);
        abilityDescriptionText.SetText(tankData.SpecialAbility.Description);

        if (abilityIcon != null)
        {
            abilityIcon.sprite = tankData.SpecialAbility.Icon;
            abilityIcon.enabled = tankData.SpecialAbility.Icon != null;
        }

        ApplyStats(tankData.Stats);
    }

    private string GetDisplayDescription(TankDefinitionSO tankData)
    {
        if (shopItemsByName.TryGetValue(tankData.TankName, out ShopItemDTO shopItem) &&
            !string.IsNullOrWhiteSpace(shopItem.description))
        {
            return shopItem.description;
        }

        return tankData.Description;
    }

    private int GetDisplayPrice(TankDefinitionSO tankData)
    {
        if (shopItemsByName.TryGetValue(tankData.TankName, out ShopItemDTO shopItem))
        {
            return Mathf.Max(0, Mathf.RoundToInt(shopItem.price));
        }

        return tankData.Price;
    }

    private void ApplyStats(TankStats stats)
    {
        if (damageBar != null)
            damageBar.SetStatValue(stats.Damage);

        if (armorBar != null)
            armorBar.SetStatValue(stats.Armor);

        if (speedBar != null)
            speedBar.SetStatValue(stats.Speed);

        if (fireRateBar != null)
            fireRateBar.SetStatValue(stats.FireRate);

        if (fireRangeBar != null)
            fireRangeBar.SetStatValue(stats.FireRange);
    }

    private string FormatPrice(int price)
    {
        if (string.IsNullOrWhiteSpace(priceFormat))
            return price.ToString();

        try
        {
            return string.Format(priceFormat, price);
        }
        catch (FormatException)
        {
            return price.ToString();
        }
    }
}
