using UnityEngine;

public class GuestMovement : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 2f;
    [SerializeField] private float stoppingDistance = 0.1f;

    private GuestWaveController waveController;
    private Vector3 destination;
    private bool hasDestination;

    // Called by the spawner to assign a destination
    public void SetDestination(
        Vector3 newDestination,
        GuestWaveController controller)
    {
        destination = newDestination;
        waveController = controller;
        hasDestination = true;
    }

    private void Update()
    {
        // Don't move without a destination or controller
        if (!hasDestination || waveController == null)
            return;

        // Only move while the wave is active
        if (waveController.CurrentState != GuestWaveState.Active)
            return;

        // Move towards the destination
        transform.position = Vector3.MoveTowards(
            transform.position,
            destination,
            moveSpeed * Time.deltaTime
        );

        // Check whether the guest reached the destination
        float distance = Vector3.Distance(
            transform.position,
            destination
        );

        if (distance <= stoppingDistance)
        {
            hasDestination = false;

            Debug.Log(gameObject.name + " reached its destination.");
        }
    }
}