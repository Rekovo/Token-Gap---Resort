using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class TapSelector : MonoBehaviour
{
    [Header("Raycast Settings")]
    [SerializeField] Camera mainCamera;
    [SerializeField] LayerMask selectableLayer = Physics.DefaultRaycastLayers;

    [Header("Input")]
    [SerializeField] InputActionReference pointerPress;
    [SerializeField] InputActionReference pointerPosition;
    [SerializeField] InputActionReference touch1Press;

    [Tooltip("Max finger travel to still count as a tap, in millimetres")]
    [SerializeField] float maxMoveMm = 4f;
    [SerializeField] float maxDuration = 0.35f;

    public event Action<BuildingSlot> BuildingTapped;

    private Vector2 downPos;
    private float downTime;
    private bool tracking;
    
    private BuildingSlot hoveredBuilding;

    void Awake()
    {
        if (mainCamera == null) mainCamera = Camera.main;
    }

    void Update()
    {
        HandleHover();

        if (touch1Press.action.IsPressed()) { tracking = false; return; }

        if (pointerPress.action.WasPressedThisFrame())
        {
            tracking = true;
            downPos = pointerPosition.action.ReadValue<Vector2>();
            downTime = Time.unscaledTime;
        }

        if (!tracking) return;

        Vector2 pos = pointerPosition.action.ReadValue<Vector2>();

        if (MovedTooFar(pos)) { tracking = false; return; }

        if (pointerPress.action.WasReleasedThisFrame())
        {
            tracking = false;
            if (Time.unscaledTime - downTime <= maxDuration)
                HandleTap(pos);
        }
    }

    void HandleHover()
    {
        Vector2 screenPos = pointerPosition.action.ReadValue<Vector2>();
        Ray ray = mainCamera.ScreenPointToRay(screenPos);
        
        if (Physics.Raycast(ray, out RaycastHit hit, 1000f, selectableLayer))
        {
            BuildingSlot building = hit.collider.GetComponentInParent<BuildingSlot>();
            if (building != hoveredBuilding)
            {
                if (hoveredBuilding != null) hoveredBuilding.SetHighlight(false);
                hoveredBuilding = building;
                if (hoveredBuilding != null) hoveredBuilding.SetHighlight(true);
            }
        }
        else if (hoveredBuilding != null)
        {
            hoveredBuilding.SetHighlight(false);
            hoveredBuilding = null;
        }
    }

    bool MovedTooFar(Vector2 pos)
    {
        float dpi = Screen.dpi > 0 ? Screen.dpi : 160f;
        float maxPixels = maxMoveMm / 25.4f * dpi;
        return (pos - downPos).magnitude > maxPixels;
    }

    void HandleTap(Vector2 screenPos)
    {
        Ray ray = mainCamera.ScreenPointToRay(screenPos);
        if (Physics.Raycast(ray, out RaycastHit hit, 1000f, selectableLayer))
        {
            BuildingSlot building = hit.collider.GetComponentInParent<BuildingSlot>();
            if (building != null)
            {
                BuildingTapped?.Invoke(building);
            }
        }
    }
}