using UnityEngine;

public class TapDebug : MonoBehaviour
{
    [SerializeField] TapSelector selector;
    void OnEnable()  => selector.BuildingTapped += OnTap;
    void OnDisable() => selector.BuildingTapped -= OnTap;
    void OnTap(BuildingSlot b) => Debug.Log($"Tapped {b.name} at cell {b.Origin}");
}