using System;
using TMPro;
using UnityEngine;
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

    private GameObject currentPreviewInstance;

    private void Start()
    {
        TankDefinitionSO startupTank = defaultTank != null ? defaultTank : GetFirstAvailableTank();

        if (startupTank != null)
        {
            SelectTankInternal(startupTank, false);
            return;
        }

        RefreshUI(null);
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
        descriptionText.SetText(tankData.Description);
        priceText.SetText(FormatPrice(tankData.Price));
        abilityNameText.SetText(tankData.SpecialAbility.AbilityName);
        abilityDescriptionText.SetText(tankData.SpecialAbility.Description);

        if (abilityIcon != null)
        {
            abilityIcon.sprite = tankData.SpecialAbility.Icon;
            abilityIcon.enabled = tankData.SpecialAbility.Icon != null;
        }

        ApplyStats(tankData.Stats);
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
