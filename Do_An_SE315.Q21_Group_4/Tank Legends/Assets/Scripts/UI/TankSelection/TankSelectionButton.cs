using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class TankSelectionButton : MonoBehaviour
{
    [SerializeField] private TankSelectionManager selectionManager;
    [SerializeField] private TankDefinitionSO tankData;
    [SerializeField] private GameObject blurObject;

    private Button cachedButton;

    private void Awake()
    {
        TryAssignBlurObject();
        cachedButton = GetComponent<Button>();
        cachedButton.onClick.AddListener(HandleClick);
    }

    private void OnEnable()
    {
        if (selectionManager != null)
            selectionManager.SelectionChanged += HandleSelectionChanged;

        RefreshSelectionVisual();
    }

    private void Start()
    {
        RefreshSelectionVisual();
    }

    private void OnDisable()
    {
        if (selectionManager != null)
            selectionManager.SelectionChanged -= HandleSelectionChanged;
    }

    private void OnDestroy()
    {
        if (cachedButton != null)
            cachedButton.onClick.RemoveListener(HandleClick);
    }

    private void Reset()
    {
        selectionManager = GetComponentInParent<TankSelectionManager>();
        TryAssignBlurObject();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (selectionManager == null)
            selectionManager = GetComponentInParent<TankSelectionManager>();

        TryAssignBlurObject();
    }
#endif

    private void HandleClick()
    {
        if (selectionManager == null || tankData == null)
        {
            Debug.LogWarning("TankSelectionButton is missing a manager or tank data reference.", this);
            return;
        }

        selectionManager.SelectTank(tankData);
    }

    private void HandleSelectionChanged(TankDefinitionSO selectedTank)
    {
        UpdateBlurState(selectedTank == tankData);
    }

    private void RefreshSelectionVisual()
    {
        bool isSelected = selectionManager != null && selectionManager.CurrentTank == tankData && tankData != null;
        UpdateBlurState(isSelected);
    }

    private void UpdateBlurState(bool isSelected)
    {
        if (blurObject == null)
            return;

        blurObject.SetActive(!isSelected);
    }

    private void TryAssignBlurObject()
    {
        if (blurObject != null)
            return;

        Transform[] children = GetComponentsInChildren<Transform>(true);

        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] == transform)
                continue;

            if (children[i].name == "Blur")
            {
                blurObject = children[i].gameObject;
                return;
            }
        }
    }
}
