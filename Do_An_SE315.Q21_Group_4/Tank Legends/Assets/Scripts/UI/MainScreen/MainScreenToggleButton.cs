using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class MainScreenToggleButton : MonoBehaviour
{
    [SerializeField] private MainScreenButtonManager manager;
    [SerializeField] private GameObject targetPanel;
    [SerializeField] private GameObject buttonRoot;
    [SerializeField] private bool hideMainButtons = true;
    [SerializeField] private bool hideOtherButtons = true;
    [SerializeField] private GameObject selectedVisual;
    [Tooltip("Nếu true, click vào nút đang chọn lần 2 sẽ đóng panel của nó và quay về Main. Nếu false, click lần 2 sẽ giữ nguyên panel.")]
    [SerializeField] private bool allowDeselect = true;

    private Button cachedButton;
    private bool isSelected;

    public GameObject TargetPanel => targetPanel;
    public GameObject ButtonRoot => buttonRoot;
    public bool HideMainButtons => hideMainButtons;
    public bool HideOtherButtons => hideOtherButtons;
    public bool IsSelected => isSelected;
    public bool AllowDeselect => allowDeselect;

    private void Awake()
    {
        ResolveManagerReference();

        if (buttonRoot == null)
            buttonRoot = gameObject;

        cachedButton = GetComponent<Button>();
        cachedButton.onClick.AddListener(HandleClick);

        if (selectedVisual != null)
            selectedVisual.SetActive(false);
    }

    private void OnDestroy()
    {
        if (cachedButton != null)
            cachedButton.onClick.RemoveListener(HandleClick);

        if (manager != null)
            manager.UnregisterButton(this);
    }

    public void RefreshState(MainScreenToggleButton selectedButton)
    {
        bool isSelected = selectedButton == this;
        this.isSelected = isSelected;

        bool shouldBeVisible = selectedButton == null || isSelected || !selectedButton.HideOtherButtons;

        if (buttonRoot != null)
            buttonRoot.SetActive(shouldBeVisible);

        UpdateSelectedVisual();
    }

    public void SetSelected(bool selected)
    {
        isSelected = selected;
        UpdateSelectedVisual();
    }

    private void UpdateSelectedVisual()
    {
        if (selectedVisual != null)
            selectedVisual.SetActive(isSelected);
    }

    private void HandleClick()
    {
        ResolveManagerReference();

        if (manager == null)
        {
            Debug.LogWarning("MainScreenToggleButton is missing a MainScreenButtonManager reference.", this);
            return;
        }

        if (targetPanel == null)
        {
            Debug.LogWarning("MainScreenToggleButton is missing a target panel reference.", this);
            return;
        }

        manager.ToggleButton(this);
    }

    private void Reset()
    {
        ResolveManagerReference();
        buttonRoot = gameObject;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ResolveManagerReference();

        if (buttonRoot == null)
            buttonRoot = gameObject;
    }
#endif

    private void ResolveManagerReference()
    {
        if (manager != null)
            return;

        manager = GetComponentInParent<MainScreenButtonManager>();

        if (manager == null)
            manager = FindObjectOfType<MainScreenButtonManager>();
    }
}
