using UnityEngine;

public class GuestSpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GuestWaveController waveController;
    [SerializeField] private GameObject guestPrefab;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private Transform guestDestination;

    [Header("Spawn Settings")]
    [SerializeField] private float spawnSpacing = 1.5f;

    private void OnEnable()
    {
        if (waveController != null)
        {
            waveController.OnWaveStarted += HandleWaveStarted;
            waveController.OnWaveCompleted += HandleWaveCompleted;
        }
    }

    private void OnDisable()
    {
        if (waveController != null)
        {
            waveController.OnWaveStarted -= HandleWaveStarted;
            waveController.OnWaveCompleted -= HandleWaveCompleted;
        }
    }

    // Called when a new wave starts
    private void HandleWaveStarted(int guestCount)
    {
        if (guestPrefab == null ||
            spawnPoint == null ||
            guestDestination == null)
        {
            Debug.LogError("GuestSpawner: Missing references!");
            return;
        }

        Debug.Log("Spawning " + guestCount + " guests.");

        for (int i = 0; i < guestCount; i++)
        {
            // Position each guest next to the previous one
            Vector3 position = spawnPoint.position
                + new Vector3(i * spawnSpacing, 0, 0);

            // Create guest
            GameObject guest = Instantiate(
                guestPrefab,
                position,
                spawnPoint.rotation
            );

            guest.name = "Guest_" + (i + 1);

            // Register arrival
            waveController.GuestArrived();

            // Get the movement component
            GuestMovement movement =
                guest.GetComponent<GuestMovement>();

            if (movement != null)
            {
                // Give each guest a slightly different destination
                Vector3 destination = guestDestination.position
                    + new Vector3(i * spawnSpacing, 0, 0);

                movement.SetDestination(destination, waveController);
            }
            else
            {
                Debug.LogWarning(
                    guest.name + " is missing GuestMovement!"
                );
            }
        }

        // All scheduled guests have spawned
        waveController.FinishArrivals();
    }

    // Called when a wave completes
    private void HandleWaveCompleted()
    {
        Debug.Log("GuestSpawner: Wave completed!");
    }
}