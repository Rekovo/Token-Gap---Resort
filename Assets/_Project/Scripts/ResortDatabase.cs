using System;
using System.IO;
using SQLite;
using UnityEngine;

public class ResortDatabase : IDisposable
{
    private readonly SQLiteConnection db;

    public ResortDatabase(string pathOverride = null)   //override = temp file for tests
    {
        string path = pathOverride ??
            Path.Combine(Application.persistentDataPath, "ResortEmpire.db");
        db = new SQLiteConnection(path);

        db.ExecuteScalar<string>("PRAGMA journal_mode=WAL;");
        db.Execute("PRAGMA foreign_keys = ON;");

        Migrate();
    }

    private static readonly string[][] Migrations =
    {
        new[] {
@"CREATE TABLE PLAYER (
    player_id TEXT PRIMARY KEY,
    created_at INTEGER NOT NULL,
    resort_value INTEGER NOT NULL DEFAULT 0 CHECK (resort_value >= 0),
    highest_wave_completed INTEGER NOT NULL DEFAULT 0 CHECK (highest_wave_completed >= 0))",

@"CREATE TABLE CURRENCY_BALANCE (
    player_id TEXT NOT NULL REFERENCES PLAYER(player_id),
    currency_code TEXT NOT NULL,
    amount INTEGER NOT NULL DEFAULT 0 CHECK (amount >= 0),
    PRIMARY KEY (player_id, currency_code))",

@"CREATE TABLE WAVE_RUN (
    wave_run_id INTEGER PRIMARY KEY AUTOINCREMENT,
    player_id TEXT NOT NULL REFERENCES PLAYER(player_id),
    wave_number INTEGER NOT NULL CHECK (wave_number >= 1),
    status TEXT NOT NULL CHECK (status IN ('ACTIVE','PAUSED','COMPLETE')),
    arrival_elapsed_s REAL NOT NULL DEFAULT 0,
    arrival_closed INTEGER NOT NULL DEFAULT 0 CHECK (arrival_closed IN (0,1)),
    rating INTEGER CHECK (rating BETWEEN 1 AND 6),
    satisfaction_pct REAL CHECK (satisfaction_pct BETWEEN 0 AND 100),
    coins_awarded INTEGER, energy_awarded INTEGER, resort_value_gain INTEGER,
    sim_state_json TEXT,
    started_at INTEGER NOT NULL, completed_at INTEGER)",

"CREATE UNIQUE INDEX ux_wave_run_one_open ON WAVE_RUN(player_id) WHERE status <> 'COMPLETE'",
"CREATE INDEX ix_wave_run_player ON WAVE_RUN(player_id, wave_number)",

@"CREATE TABLE CURRENCY_LEDGER (
    ledger_id INTEGER PRIMARY KEY AUTOINCREMENT,
    player_id TEXT NOT NULL REFERENCES PLAYER(player_id),
    currency_code TEXT NOT NULL,
    delta INTEGER NOT NULL,
    reason TEXT NOT NULL,
    wave_run_id INTEGER REFERENCES WAVE_RUN(wave_run_id),
    created_at INTEGER NOT NULL)",

@"CREATE TABLE BUILDING_STATE (
    player_id TEXT NOT NULL REFERENCES PLAYER(player_id),
    building_key TEXT NOT NULL,
    is_unlocked INTEGER NOT NULL DEFAULT 0 CHECK (is_unlocked IN (0,1)),
    level INTEGER NOT NULL DEFAULT 0 CHECK (level >= 0),
    workers_assigned INTEGER NOT NULL DEFAULT 0 CHECK (workers_assigned >= 0),
    PRIMARY KEY (player_id, building_key))",

@"CREATE TABLE WAVE_NEED_RESULT (
    wave_run_id INTEGER NOT NULL REFERENCES WAVE_RUN(wave_run_id) ON DELETE CASCADE,
    need_key TEXT NOT NULL,
    demand_total INTEGER NOT NULL, units_served INTEGER NOT NULL,
    avg_contribution_pct REAL, bottleneck_seconds REAL,
    PRIMARY KEY (wave_run_id, need_key))",

@"CREATE TABLE SESSION (
    session_id INTEGER PRIMARY KEY AUTOINCREMENT,
    player_id TEXT NOT NULL REFERENCES PLAYER(player_id),
    started_at INTEGER NOT NULL, last_heartbeat_at INTEGER NOT NULL,
    ended_at INTEGER, end_reason TEXT, app_version TEXT)",

"CREATE INDEX ix_session_player ON SESSION(player_id, started_at)",

@"CREATE TABLE REWARD_CLAIM (
    claim_id INTEGER PRIMARY KEY AUTOINCREMENT,
    player_id TEXT NOT NULL REFERENCES PLAYER(player_id),
    session_id INTEGER NOT NULL REFERENCES SESSION(session_id),
    reward_kind TEXT NOT NULL,
    absence_seconds INTEGER NOT NULL,
    claimed_at INTEGER NOT NULL)",

@"CREATE TABLE ANALYTICS_EVENT (
    event_id INTEGER PRIMARY KEY AUTOINCREMENT,
    session_id INTEGER NOT NULL REFERENCES SESSION(session_id),
    wave_run_id INTEGER REFERENCES WAVE_RUN(wave_run_id),
    event_type TEXT NOT NULL, payload_json TEXT, created_at INTEGER NOT NULL)",

@"CREATE TRIGGER trg_resort_value_only_grows
  BEFORE UPDATE OF resort_value ON PLAYER
  WHEN NEW.resort_value < OLD.resort_value
  BEGIN SELECT RAISE(ABORT, 'resort_value can only grow'); END",

@"CREATE TRIGGER trg_reputation_never_decreases
  BEFORE UPDATE OF amount ON CURRENCY_BALANCE
  WHEN OLD.currency_code = 'REPUTATION' AND NEW.amount < OLD.amount
  BEGIN SELECT RAISE(ABORT, 'reputation cannot decrease'); END",
        },
        // new[] { "CREATE TABLE STAFF_HIRE (...)" }, 
    };

    private void Migrate()
    {
        int current = db.ExecuteScalar<int>("PRAGMA user_version;");
        for (int i = current; i < Migrations.Length; i++)
        {
            int target = i + 1;
            db.RunInTransaction(() =>
            {
                foreach (var sql in Migrations[i]) db.Execute(sql);
                db.Execute($"PRAGMA user_version = {target};");
            });
        }
    }

    public void CreateNewPlayer(string playerId, int startingCoins = 100)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        db.RunInTransaction(() =>
        {
            db.Execute("INSERT OR IGNORE INTO PLAYER (player_id, created_at) VALUES (?, ?)",
                       playerId, now);
            db.Execute("INSERT OR IGNORE INTO CURRENCY_BALANCE VALUES (?, 'COINS', ?)",
                       playerId, startingCoins);
            db.Execute("INSERT OR IGNORE INTO CURRENCY_BALANCE VALUES (?, 'GEMS', 0)",
                       playerId);
        });
    }

    public int GetSchemaVersion() => db.ExecuteScalar<int>("PRAGMA user_version;");
    public bool ForeignKeysEnabled() => db.ExecuteScalar<int>("PRAGMA foreign_keys;") == 1;

    public string GetPlayerSummary(string playerId)
{
    var player = db.Find<PlayerRecord>(playerId);
    var balances = db.Query<CurrencyRecord>(
        "SELECT * FROM CURRENCY_BALANCE WHERE player_id = ?", playerId);

    string result = player == null
        ? "Player not found"
        : $"Player: {player.player_id}\nCreated at: {player.created_at}\nResort value: {player.resort_value}";

    foreach (var b in balances)
        result += $"\n{b.currency_code}: {b.amount}";
    return result;
}

    public void Dispose() => db?.Close();
}

[Table("PLAYER")]
public class PlayerRecord
{
    [PrimaryKey] public string player_id { get; set; }
    public long created_at { get; set; }
    public int resort_value { get; set; }
    public int highest_wave_completed { get; set; }
}

[Table("CURRENCY_BALANCE")]
public class CurrencyRecord
{
    public string player_id { get; set; }
    public string currency_code { get; set; }
    public int amount { get; set; }
}