
using UnityEngine;

// Temporary testing tool for SCRUM-20.
// Moves a Worker between two buildings using the existing system.
public class WorkerMoveDebugTest : MonoBehaviour
{
    [Header("Test Buildings")]
    [SerializeField] private BuildingSlot reception;
    [SerializeField] private BuildingSlot beachBar;

    [ContextMenu("TEST: Reception to Beach Bar")]
    public void MoveToBeachBar()
    {
        MoveWorker(reception, beachBar);
    }

    [ContextMenu("TEST: Beach Bar to Reception")]
    public void MoveToReception()
    {
        MoveWorker(beachBar, reception);
    }

    private void MoveWorker(BuildingSlot from, BuildingSlot to)
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("Enter Play Mode before testing.");
            return;
        }

        if (from == null || to == null)
        {
            Debug.LogError("Assign both buildings in the Inspector!");
            return;
        }

        bool success = from.TryMoveWorkerTo(to);

        if (success)
        {
            Debug.Log("Worker moved: " +
                      from.BuildingKey + " -> " +
                      to.BuildingKey);
        }
        else
        {
            Debug.LogWarning(
                "Worker move failed. Check the building assignments."
            );
        }
    }
}
