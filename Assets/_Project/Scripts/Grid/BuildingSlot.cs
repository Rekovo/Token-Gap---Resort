using System.Collections;
using UnityEngine;

public class BuildingSlot : MonoBehaviour
{
    [SerializeField] GridMap grid;
    [SerializeField] Vector2Int footprint = new(2, 2);
    [Tooltip("Where the Worker stands (empty child transform)")]
    [SerializeField] Transform workerAnchor;

    [SerializeField, Tooltip("Stable key matching BUILDING_DEFINITION, e.g. RESTAURANT")]
    private string buildingKey;
    [SerializeField, Tooltip("Optional; auto-found in the active scene when omitted")]
    private GuestWaveController waveController;
    [SerializeField, Tooltip("Model-only child used for the tier appearance and purchase pulse")]
    private Transform visualRoot;

    public Vector2Int Origin { get; private set; }
    public Transform WorkerAnchor => workerAnchor;
    public BuildingStateRecord PersistedState { get; private set; }
    public string BuildingKey => buildingKey;
    public bool IsUnlocked => PersistedState != null && PersistedState.is_unlocked == 1;
    public bool HasWorker => PersistedState != null && PersistedState.workers_assigned > 0;
    public int Level => PersistedState != null ? PersistedState.level : 0;
    private ResortDatabase database;
    private Renderer[] visualRenderers;
    private MaterialPropertyBlock propBlock;
    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor"); // URP
    private static readonly int ColorID = Shader.PropertyToID("_Color"); // Standard
    private bool highlighted;
    private Vector3 visualBaseScale = Vector3.one;
    private Coroutine upgradePulse;

    void Start()
    {
        if (waveController == null) waveController = FindAnyObjectByType<GuestWaveController>();
        ResolveVisualRoot();
        if (waveController != null) waveController.OnBuildingUpgraded += HandleBuildingUpgraded;

        if (!string.IsNullOrWhiteSpace(buildingKey))
        {
            database = new ResortDatabase();
            database.CreateNewPlayer("test-player-001");
            RefreshPersistedState();
            if (PersistedState == null)
                Debug.LogWarning($"{name}: no saved building state found for key '{buildingKey}'.", this);
        }

        if (grid == null || grid.Data == null)
        {
            Debug.LogError($"{name}: assign a GridMap before starting.", this);
            return;
        }

        Vector3 bottomLeftCentre = transform.position
            - new Vector3(footprint.x - 1, 0, footprint.y - 1) * 0.5f * grid.CellSize;
        Origin = grid.WorldToCell(bottomLeftCentre);

        if (!grid.Data.TryPlace(this, Origin, footprint))
            Debug.LogError($"{name}: footprint overlaps another building or leaves the grid.", this);
    }

    public bool TryMoveWorkerTo(BuildingSlot target)
    {
        if (target == null || target == this || database == null || target.database == null
            || !IsUnlocked || !target.IsUnlocked || !HasWorker || target.HasWorker)
            return false;
        GuestWaveController controller = waveController != null ? waveController : target.waveController;
        if (controller == null) controller = FindAnyObjectByType<GuestWaveController>();
        if (controller != null && controller.CurrentState != GuestWaveState.Ready
            && controller.CurrentState != GuestWaveState.Active
            && controller.CurrentState != GuestWaveState.Resolving)
            return false;
        if (!database.MoveWorker("test-player-001", buildingKey, target.buildingKey)) return false;
        RefreshPersistedState();
        target.RefreshPersistedState();

        if (controller != null)
        {
            // Re-read both records after the successful transaction so the simulation
            // always uses the authoritative assignments for source and destination.
            controller.RefreshBuildingPerformance(buildingKey);
            controller.RefreshBuildingPerformance(target.buildingKey);
        }
        return true;
    }

    /// <summary>Runs the prototype purchase/unlock transaction and refreshes this building's saved state.</summary>
    public bool TryPurchaseUnlock()
    {
        if (database == null || IsUnlocked || string.IsNullOrWhiteSpace(buildingKey)) return false;
        GuestWaveController controller = waveController != null ? waveController : FindAnyObjectByType<GuestWaveController>();
        bool succeeded = controller != null
            && controller.TryUnlockServiceBuilding(buildingKey);
        if (!succeeded) return false;

        RefreshPersistedState();
        GetComponent<BuildingWorkerIndicator>()?.RefreshFromSave();
        return true;
    }

    public int UpgradeCoinCost => waveController != null
        ? waveController.GetBuildingUpgradeCost(buildingKey) : -1;

    public bool CanUpgrade => IsUnlocked && waveController != null
        && waveController.CanUpgradeServiceBuilding(buildingKey);

    /// <summary>Purchases one level through the authoritative controller and persisted player state.</summary>
    public bool TryPurchaseUpgrade()
    {
        return IsUnlocked && waveController != null
            && waveController.TryUpgradeServiceBuilding(buildingKey);
    }

    private void HandleBuildingUpgraded(BuildingUpgradeInfo info)
    {
        if (info.BuildingKey != buildingKey) return;
        RefreshPersistedState();
        PlayUpgradePulse();
    }

    public void RefreshPersistedState()
    {
        if (database == null || string.IsNullOrWhiteSpace(buildingKey)) return;
        PersistedState = database.GetBuildingState("test-player-001", buildingKey);
        ApplyLevelAppearance();
    }

    public void SetHighlight(bool active)
    {
        highlighted = active;
        ApplyLevelAppearance();
    }

    private void ApplyLevelAppearance()
    {
        if (visualRenderers == null)
        {
            visualRenderers = GetComponentsInChildren<Renderer>(true);
            propBlock = new MaterialPropertyBlock();
        }

        Color tint = highlighted ? Color.gray : Color.white;

        foreach (Renderer r in visualRenderers)
        {
            r.GetPropertyBlock(propBlock);
            propBlock.SetColor(BaseColorID, tint);
            propBlock.SetColor(ColorID, tint);
            r.SetPropertyBlock(propBlock);
        }
    }

    private void ResolveVisualRoot()
    {
        if (visualRoot == null) visualRoot = transform.Find("Visual");
        if (visualRoot != null) visualBaseScale = visualRoot.localScale;
    }

    private void PlayUpgradePulse()
    {
        if (visualRoot == null) return;
        if (upgradePulse != null) StopCoroutine(upgradePulse);
        upgradePulse = StartCoroutine(UpgradePulseRoutine());
    }

    private IEnumerator UpgradePulseRoutine()
    {
        const float pulseScale = 1.1f;
        const float riseDuration = 0.12f;
        const float fallDuration = 0.2f;
        float elapsed = 0f;
        while (elapsed < riseDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            visualRoot.localScale = visualBaseScale * Mathf.Lerp(1f, pulseScale, elapsed / riseDuration);
            yield return null;
        }

        elapsed = 0f;
        while (elapsed < fallDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            visualRoot.localScale = visualBaseScale * Mathf.Lerp(pulseScale, 1f, elapsed / fallDuration);
            yield return null;
        }
        visualRoot.localScale = visualBaseScale;
        upgradePulse = null;
    }

    void OnDestroy()
    {
        if (waveController != null) waveController.OnBuildingUpgraded -= HandleBuildingUpgraded;
        if (grid != null && grid.Data != null) grid.Data.Remove(this);
        database?.Dispose();
    }

    [ContextMenu("Snap to grid")]
    void SnapToGrid()
    {
        Vector3 local = transform.position - grid.transform.position;
        float cs = grid.CellSize;

        Vector3 corner = local - new Vector3(footprint.x, 0, footprint.y) * 0.5f * cs;
        corner.x = Mathf.Round(corner.x / cs) * cs;
        corner.z = Mathf.Round(corner.z / cs) * cs;
        transform.position = grid.transform.position + corner
            + new Vector3(footprint.x, 0, footprint.y) * 0.5f * cs
            + Vector3.up * (transform.position.y - grid.transform.position.y);
    }
}
