using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class MainScreenToggleButton : MonoBehaviour
{
    [SerializeField] private MainScreenButtonManager manager;
    [SerializeField] private GameObject targetPanel;
    [SerializeField] private GameObject buttonRoot;

    private Button cachedButton;

    public GameObject TargetPanel => targetPanel;
    public GameObject ButtonRoot => buttonRoot;

    private void Awake()
    {
        ResolveManagerReference();

        if (buttonRoot == null)
            buttonRoot = gameObject;

        cachedButton = GetComponent<Button>();
        cachedButton.onClick.AddListener(HandleClick);

        if (manager != null)
            manager.RegisterButton(this);
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
        bool shouldBeVisible = selectedButton == null || isSelected;

        if (buttonRoot != null)
            buttonRoot.SetActive(shouldBeVisible);
    }

    private void HandleClick()
    {
        ResolveManagerReference();

        if (manager == null)
        {
            Debug.LogWarning("MainScreenToggleButton is missing a MainScreenButtonManager reference.", this);
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
