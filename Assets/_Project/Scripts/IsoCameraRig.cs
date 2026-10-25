using UnityEngine;
using UnityEngine.InputSystem;

public class IsoCameraRig : MonoBehaviour
{
    [Header("References")]
    [SerializeField] Camera cam;

    [Header("Input actions (Camera map, project-wide asset)")]
    [SerializeField] InputActionReference pointerPress;
    [SerializeField] InputActionReference pointerPosition;
    [SerializeField] InputActionReference touch0Position;
    [SerializeField] InputActionReference touch1Position;
    [SerializeField] InputActionReference touch1Press;
    [SerializeField] InputActionReference scroll;

    [Header("View")]
    [SerializeField] Vector2 pitchYaw = new(30f, 45f);
    [SerializeField] float distance = 40f;

    [Header("Zoom (orthographic size)")]
    [SerializeField] float minSize = 4f;
    [SerializeField] float maxSize = 10f;
    [SerializeField, Range(0.01f, 0.3f)] float scrollStep = 0.1f;

    [Header("Pan limits (world XZ)")]
    [SerializeField] Rect panBounds = new(-15f, -15f, 30f, 30f);

    static readonly Plane Ground = new(Vector3.up, Vector3.zero);

    Vector3 dragAnchorWorld;
    bool isPanning;
    bool waitForRelease;
    float lastPinchDistance;
    bool isPinching;

    void Awake() => ApplyView();

    void OnValidate()
    {
        if (cam != null) ApplyView();
        if (maxSize < minSize) maxSize = minSize;
    }

    void Update()
    {
        if (!HandlePinch())
            HandlePan();

        HandleScroll();
    }

    void ApplyView()
    {
        cam.orthographic = true;
        cam.transform.localRotation = Quaternion.Euler(pitchYaw.x, pitchYaw.y, 0f);
        cam.transform.localPosition = -(cam.transform.localRotation * Vector3.forward) * distance;
    }

    void HandlePan()
    {
        if (!pointerPress.action.IsPressed())
        {
            isPanning = false;
            waitForRelease = false;
            return;
        }

        if (waitForRelease) return;

        Vector2 screenPos = pointerPosition.action.ReadValue<Vector2>();

        if (!isPanning)
        {
            isPanning = true;
            dragAnchorWorld = ScreenToGround(screenPos);
            return;
        }

        Vector3 nowWorld = ScreenToGround(screenPos);
        Vector3 p = transform.position + (dragAnchorWorld - nowWorld);
        p.x = Mathf.Clamp(p.x, panBounds.xMin, panBounds.xMax);
        p.z = Mathf.Clamp(p.z, panBounds.yMin, panBounds.yMax);
        transform.position = p;
    }

    bool HandlePinch()
    {
        if (!touch1Press.action.IsPressed())
        {
            if (isPinching)
            {
                isPinching = false;
                waitForRelease = true;
            }
            return false;
        }

        isPanning = false;

        float dist = Vector2.Distance(
            touch0Position.action.ReadValue<Vector2>(),
            touch1Position.action.ReadValue<Vector2>());

        if (!isPinching)
        {
            isPinching = true;
            lastPinchDistance = dist;
            return true;
        }

        if (dist > 1f && lastPinchDistance > 1f)
            SetZoom(cam.orthographicSize * (lastPinchDistance / dist));

        lastPinchDistance = dist;
        return true;
    }

    void HandleScroll()
    {
        float y = scroll.action.ReadValue<Vector2>().y;
        if (Mathf.Approximately(y, 0f)) return;

        SetZoom(cam.orthographicSize * (1f - Mathf.Sign(y) * scrollStep));
    }

    void SetZoom(float size) => cam.orthographicSize = Mathf.Clamp(size, minSize, maxSize);

    public Vector3 ScreenToGround(Vector2 screenPos)
    {
        Ray ray = cam.ScreenPointToRay(screenPos);
        return Ground.Raycast(ray, out float t) ? ray.GetPoint(t) : transform.position;
    }
}