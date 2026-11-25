
using UnityEngine;

public class UIManager : MonoBehaviour
{
    [Header("Menu Panels")]
    [SerializeField] private GameObject buildMenuPanel;
    [SerializeField] private GameObject workersMenuPanel;
    [SerializeField] private GameObject guestsMenuPanel;
    [SerializeField] private GameObject settingsPanel;

    private WorkerManagementPanelUI workerManagement;
    private WaveNeedsPanelUI waveNeeds;
    private TapSelector tapSelector;
    private GameObject workersReturnPanel;

    public bool IsAnyMenuOpen => buildMenuPanel.activeSelf || workersMenuPanel.activeSelf
        || (guestsMenuPanel.activeSelf && (waveNeeds == null || waveNeeds.BlocksIslandInput))
        || settingsPanel.activeSelf;

    private void Awake()
    {
        workerManagement = workersMenuPanel.GetComponent<WorkerManagementPanelUI>();
        if (workerManagement == null) workerManagement = workersMenuPanel.AddComponent<WorkerManagementPanelUI>();
        workerManagement.Initialize(FindAnyObjectByType<GuestWaveController>());

        waveNeeds = guestsMenuPanel.GetComponent<WaveNeedsPanelUI>();
        if (waveNeeds != null) waveNeeds.SetManagementOpener(OpenWorkersForNeed);
        tapSelector = FindAnyObjectByType<TapSelector>();
    }

    private void Start()
    {
        CloseAllMenus();
        if (tapSelector != null) tapSelector.BuildingTapped += OpenWorkersForBuilding;
    }

    private void OnDestroy()
    {
        if (tapSelector != null) tapSelector.BuildingTapped -= OpenWorkersForBuilding;
        if (waveNeeds != null) waveNeeds.SetManagementOpener(null);
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
        OpenWorkersFromView(null, (ResortNeed?)null);
    }

    public void OpenWorkersForNeed(ResortNeed need)
    {
        OpenWorkersFromView(null, need);
    }

    private void OpenWorkersForBuilding(BuildingSlot building)
    {
        if (building == null || !building.IsUnlocked || IsAnyMenuOpen) return;
        OpenWorkersFromView(null, building.BuildingKey);
    }

    /// <summary>Entry point for future preparation views such as the wave forecast.</summary>
    public void OpenWorkersFromView(GameObject originPanel, string buildingKey)
    {
        if (workersMenuPanel.activeSelf) return;
        workersReturnPanel = originPanel != null ? originPanel : CurrentPanel();
        workerManagement.FocusBuilding(buildingKey);
        OpenMenu(workersMenuPanel);
        if (originPanel != null && originPanel != workersMenuPanel) originPanel.SetActive(false);
    }

    private void OpenWorkersFromView(GameObject originPanel, ResortNeed? need)
    {
        if (workersMenuPanel.activeSelf) return;
        workersReturnPanel = originPanel != null ? originPanel : CurrentPanel();
        workerManagement.FocusNeed(need);
        OpenMenu(workersMenuPanel);
    }

    public void CloseWorkersMenu()
    {
        GameObject destination = workersReturnPanel;
        workersReturnPanel = null;
        CloseAllMenus();
        if (destination != null) destination.SetActive(true);
    }

    private GameObject CurrentPanel()
    {
        if (guestsMenuPanel.activeSelf) return guestsMenuPanel;
        if (buildMenuPanel.activeSelf) return buildMenuPanel;
        if (settingsPanel.activeSelf) return settingsPanel;
        return null;
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
