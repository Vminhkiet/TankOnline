using System.Collections.Generic;
using UnityEngine;

public class MainScreenButtonManager : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject mainPanel;

    [Header("Managed Buttons")]
    [SerializeField] private MainScreenToggleButton[] managedButtons;

    private readonly List<MainScreenToggleButton> registeredButtons = new List<MainScreenToggleButton>();

    public MainScreenToggleButton CurrentButton { get; private set; }

    private void Start()
    {
        RegisterManagedButtons();
        ShowMainPanel();
    }

    private void RegisterManagedButtons()
    {
        if (managedButtons == null || managedButtons.Length == 0)
            return;

        for (int i = 0; i < managedButtons.Length; i++)
        {
            MainScreenToggleButton button = managedButtons[i];
            if (button != null)
            {
                RegisterButton(button);
            }
        }
    }

    public void RegisterButton(MainScreenToggleButton button)
    {
        if (button == null || registeredButtons.Contains(button))
            return;

        registeredButtons.Add(button);
        button.RefreshState(CurrentButton);
    }

    public void UnregisterButton(MainScreenToggleButton button)
    {
        if (button == null)
            return;

        registeredButtons.Remove(button);

        if (CurrentButton == button)
            CurrentButton = null;
    }

    public void ToggleButton(MainScreenToggleButton button)
    {
        if (button == null)
            return;

        // Deselect previous button
        if (CurrentButton != null && CurrentButton != button)
        {
            CurrentButton.SetSelected(false);
        }

        RegisterButton(button);

        if (CurrentButton == button)
        {
            ShowMainPanel();
            return;
        }

        ShowButtonPanel(button);
    }

    public void ShowMainPanel()
    {
        if (CurrentButton != null)
        {
            CurrentButton.SetSelected(false);
        }

        CurrentButton = null;

        if (mainPanel != null)
            mainPanel.SetActive(true);

        SetAllTargetPanelsActiveState(null);
        RefreshAllButtons();
    }

    private void ShowButtonPanel(MainScreenToggleButton button)
    {
        CurrentButton = button;
        button.SetSelected(true);

        bool keepMainPanelActive = !button.HideMainButtons || ShouldKeepMainPanelActive(button);

        SetAllTargetPanelsActiveState(button);

        if (button.TargetPanel != null)
            button.TargetPanel.SetActive(true);

        if (mainPanel != null)
            mainPanel.SetActive(keepMainPanelActive);

        RefreshAllButtons();
    }

    private void SetAllTargetPanelsActiveState(MainScreenToggleButton selectedButton)
    {
        for (int i = 0; i < registeredButtons.Count; i++)
        {
            MainScreenToggleButton button = registeredButtons[i];

            if (button == null)
                continue;

            GameObject targetPanel = button.TargetPanel;

            if (targetPanel == null)
                continue;

            targetPanel.SetActive(button == selectedButton);
        }
    }

    private void RefreshAllButtons()
    {
        for (int i = 0; i < registeredButtons.Count; i++)
        {
            if (registeredButtons[i] == null)
                continue;

            registeredButtons[i].RefreshState(CurrentButton);
        }
    }

    private bool ShouldKeepMainPanelActive(MainScreenToggleButton button)
    {
        if (mainPanel == null || button == null)
            return false;

        if (IsObjectInsideMainPanel(button.TargetPanel))
            return true;

        if (IsObjectInsideMainPanel(button.ButtonRoot))
            return true;

        return false;
    }

    private bool IsObjectInsideMainPanel(GameObject targetObject)
    {
        if (mainPanel == null || targetObject == null)
            return false;

        return targetObject.transform.IsChildOf(mainPanel.transform);
    }
}
