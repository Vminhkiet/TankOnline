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
    [SerializeField] private string shopVersionUrl = "http://localhost:8080/api/shop/items/version";
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
    private const string ShopVersionCacheKey = "shop_items_cache_version";

    private GameObject currentPreviewInstance;
    private readonly Dictionary<string, ShopItemDTO> shopItemsByName = new Dictionary<string, ShopItemDTO>(StringComparer.OrdinalIgnoreCase);
    private long cachedShopVersion = -1;
    private bool shopDataLoaded = false;

    private IEnumerator Start()
    {
        // Load cached data for immediate display
        TryLoadCachedShopItems();

        // Then check server version and re-fetch if needed
        yield return StartCoroutine(RefreshShopIfNeeded());

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

    [System.Serializable]
    private class ShopVersionResponse
    {
        public long version;
    }

    /// <summary>
    /// Call this whenever the shop UI is opened (e.g. from a button).
    /// It checks the server version first, and only re-fetches items if something changed.
    /// </summary>
    public void OnEnable()
    {
        StartCoroutine(RefreshShopIfNeeded());
    }

    private IEnumerator RefreshShopIfNeeded()
    {
        // Step 1: Ask the server for the current version number (very lightweight)
        long serverVersion = -1;
        using (UnityWebRequest versionRequest = UnityWebRequest.Get(shopVersionUrl))
        {
            versionRequest.SetRequestHeader("Accept", "application/json");
            string jwt = PlayerPrefs.GetString("jwt", "");
            if (!string.IsNullOrEmpty(jwt))
                versionRequest.SetRequestHeader("Authorization", "Bearer " + jwt);

            yield return versionRequest.SendWebRequest();

            if (versionRequest.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    ShopVersionResponse versionResponse = JsonUtility.FromJson<ShopVersionResponse>(versionRequest.downloadHandler.text);
                    serverVersion = versionResponse.version;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("Failed to parse shop version: " + e.Message);
                }
            }
            else
            {
                Debug.LogWarning($"Failed to check shop version: {versionRequest.error}. Will re-fetch items.");
            }
        }

        // Step 2: Compare with cached version
        if (serverVersion >= 0 && serverVersion == cachedShopVersion && shopDataLoaded)
        {
            Debug.Log($"Shop data is up-to-date (version {cachedShopVersion}). Skipping fetch.");
            yield break;
        }

        // Step 3: Version changed or no cache — do a full fetch
        Debug.Log($"Shop data changed (cached={cachedShopVersion}, server={serverVersion}). Fetching new items...");
        yield return StartCoroutine(FetchAvailableTanksFromAPI(serverVersion));
    }

    private bool TryLoadCachedShopItems()
    {
        string cachedJson = PlayerPrefs.GetString(ShopItemsCacheJsonKey, "");
        string cachedVersionStr = PlayerPrefs.GetString(ShopVersionCacheKey, "-1");

        if (string.IsNullOrWhiteSpace(cachedJson))
            return false;

        if (long.TryParse(cachedVersionStr, out long ver))
            cachedShopVersion = ver;

        bool success = TryApplyShopItemsJson(cachedJson);
        if (success)
            shopDataLoaded = true;
        return success;
    }

    private IEnumerator FetchAvailableTanksFromAPI(long newVersion)
    {
        string jwt = PlayerPrefs.GetString("jwt", "");

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

                if (TryApplyShopItemsJson(json))
                {
                    shopDataLoaded = true;
                    cachedShopVersion = newVersion;

                    PlayerPrefs.SetString(ShopItemsCacheJsonKey, json);
                    PlayerPrefs.SetString(ShopVersionCacheKey, newVersion.ToString());
                    PlayerPrefs.Save();

                    // Re-select current tank if it became unavailable
                    if (CurrentTank != null && !System.Array.Exists(availableTanks, t => t == CurrentTank))
                    {
                        TankDefinitionSO fallback = GetFirstAvailableTank();
                        if (fallback != null)
                            SelectTankInternal(fallback, false);
                        else
                            RefreshUI(null);
                    }
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
