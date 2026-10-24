
using UnityEngine;

public class UIManager : MonoBehaviour
{
    [Header("Menu Panels")]
    [SerializeField] private GameObject buildMenuPanel;
    [SerializeField] private GameObject workersMenuPanel;
    [SerializeField] private GameObject guestsMenuPanel;
    [SerializeField] private GameObject settingsPanel;

    private void Start()
    {
        CloseAllMenus();
    }

    public void CloseAllMenus()
    {
        buildMenuPanel.SetActive(false);
        workersMenuPanel.SetActive(false);
        guestsMenuPanel.SetActive(false);
        settingsPanel.SetActive(false);
    }

    private void OpenMenu(GameObject panel)
    {
        CloseAllMenus();
        panel.SetActive(true);
    }

    public void OpenBuildMenu()
    {
        OpenMenu(buildMenuPanel);
    }

    public void OpenWorkersMenu()
    {
        OpenMenu(workersMenuPanel);
    }

    public void OpenGuestsMenu()
    {
        OpenMenu(guestsMenuPanel);
    }

    public void OpenSettingsMenu()
    {
        OpenMenu(settingsPanel);
    }
}
