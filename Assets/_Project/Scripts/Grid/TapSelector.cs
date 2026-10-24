using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class TapSelector : MonoBehaviour
{
    [SerializeField] IsoCameraRig cameraRig;
    [SerializeField] GridMap grid;

    [SerializeField] InputActionReference pointerPress;
    [SerializeField] InputActionReference pointerPosition;
    [SerializeField] InputActionReference touch1Press;

    [Tooltip("Max finger travel to still count as a tap, in millimetres")]
    [SerializeField] float maxMoveMm = 4f;
    [SerializeField] float maxDuration = 0.35f;

    public event Action<BuildingSlot> BuildingTapped;

    Vector2 downPos;
    float downTime;
    bool tracking;

    void Update()
    {
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

    bool MovedTooFar(Vector2 pos)
    {
        float dpi = Screen.dpi > 0 ? Screen.dpi : 160f;
        float maxPixels = maxMoveMm / 25.4f * dpi;
        return (pos - downPos).magnitude > maxPixels;
    }

    void HandleTap(Vector2 screenPos)
    {
        Vector3 world = cameraRig.ScreenToGround(screenPos);
        Vector2Int cell = grid.WorldToCell(world);

        if (grid.Data.TryGetOccupant(cell, out BuildingSlot building))
            BuildingTapped?.Invoke(building);
    }
}