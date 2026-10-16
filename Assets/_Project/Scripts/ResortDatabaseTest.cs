using UnityEngine;

public class ResortDatabaseTest : MonoBehaviour
{
    private ResortDatabase database;
    private string summary = "";

    private void Start()
    {
        database = new ResortDatabase();
        string playerId = "test-player-001";

        database.CreateNewPlayer(playerId);   // only creates if missing

        summary = "Schema v" + database.GetSchemaVersion()
                + " | FK on: " + database.ForeignKeysEnabled()
                + "\n" + database.GetPlayerSummary(playerId);
        Debug.Log(summary);
    }

    private void OnGUI()
    {
        GUI.skin.label.fontSize = 36;
        GUI.Label(new Rect(20, 20, Screen.width - 40, Screen.height - 40), summary);
    }

    private void OnDestroy() => database?.Dispose();
}