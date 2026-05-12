using UnityEngine;
using UnityEngine.UI;

public class TankStatBar : MonoBehaviour
{
    [SerializeField] private TankStatType statType;
    [SerializeField] private Image fillImage;

#if UNITY_EDITOR
    [Range(0, 10)]
    [SerializeField] private int previewValue;
#endif

    public TankStatType StatType => statType;

    public void Refresh(TankStats stats)
    {
        SetStatValue(stats.GetValue(statType));
    }

    public void SetStatValue(int value)
    {
        ApplyFill(Mathf.Clamp(value, 0, 10));
    }

    private void ApplyFill(int value)
    {
        if (fillImage == null)
            return;

        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillOrigin = 0;
        fillImage.fillAmount = value * 0.1f;
    }

    private void Reset()
    {
        AutoAssignFillImage();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (fillImage == null)
            AutoAssignFillImage();

        ApplyFill(Mathf.Clamp(previewValue, 0, 10));
    }
#endif

    private void AutoAssignFillImage()
    {
        Image[] images = GetComponentsInChildren<Image>(true);

        if (images.Length > 1)
        {
            fillImage = images[1];
            return;
        }

        if (images.Length == 1)
            fillImage = images[0];
    }
}
