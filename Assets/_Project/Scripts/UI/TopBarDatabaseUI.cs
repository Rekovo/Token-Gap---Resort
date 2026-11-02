using UnityEngine;
using TMPro;

public class TopBarDatabaseUI : MonoBehaviour
{
    public TMP_Text moneyText;

    private ResortDatabase database;
    private string playerId = "test-player-001";

    private void Start()
    {
        database = new ResortDatabase();

        UpdateMoney();
    }

    public void UpdateMoney()
    {
        int coins = database.GetCurrencyAmount(playerId, "COINS");

        moneyText.text = "Coins: " + coins.ToString();
    }

    private void OnDestroy()
    {
        database?.Dispose();
    }
}
