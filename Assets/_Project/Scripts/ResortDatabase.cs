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
        new[] {
            @"CREATE TABLE BUILDING_DEFINITION (
    building_key TEXT PRIMARY KEY,
    need_key TEXT NOT NULL UNIQUE,
    unlock_wave INTEGER NOT NULL DEFAULT 1 CHECK (unlock_wave >= 1),
    worker_slots INTEGER NOT NULL DEFAULT 1 CHECK (worker_slots = 1),
    initial_workers INTEGER NOT NULL DEFAULT 0 CHECK (initial_workers IN (0,1)))",
            "INSERT INTO BUILDING_DEFINITION VALUES ('RECEPTION','ROOMS',1,1,1)",
            "INSERT INTO BUILDING_DEFINITION VALUES ('RESTAURANT','FOOD',1,1,1)",
            "INSERT INTO BUILDING_DEFINITION VALUES ('BEACH_BAR','DRINKS',1,1,0)",
            "INSERT INTO BUILDING_DEFINITION VALUES ('POOL','RELAX',3,1,0)",
            "INSERT INTO BUILDING_DEFINITION VALUES ('BUNGALOWS','OCEAN_VIEW',6,1,0)",
            "ALTER TABLE BUILDING_STATE ADD COLUMN need_key TEXT",
            "ALTER TABLE BUILDING_STATE ADD COLUMN worker_slots INTEGER NOT NULL DEFAULT 1 CHECK (worker_slots = 1)",
            @"CREATE TRIGGER trg_building_worker_limit_insert
  BEFORE INSERT ON BUILDING_STATE
  WHEN NEW.workers_assigned > NEW.worker_slots
  BEGIN SELECT RAISE(ABORT, 'workers_assigned exceeds worker_slots'); END",
            @"CREATE TRIGGER trg_building_worker_limit_update
  BEFORE UPDATE OF workers_assigned, worker_slots ON BUILDING_STATE
  WHEN NEW.workers_assigned > NEW.worker_slots
  BEGIN SELECT RAISE(ABORT, 'workers_assigned exceeds worker_slots'); END",
            "INSERT OR IGNORE INTO CURRENCY_BALANCE (player_id, currency_code, amount) SELECT player_id, 'ENERGY', 0 FROM PLAYER",
            "UPDATE BUILDING_STATE SET need_key = (SELECT need_key FROM BUILDING_DEFINITION WHERE building_key = BUILDING_STATE.building_key) WHERE need_key IS NULL"
        },
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

    public void CreateNewPlayer(string playerId, int startingCoins = 100, int startingEnergy = 0)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        db.RunInTransaction(() =>
        {
            db.Execute("INSERT OR IGNORE INTO PLAYER (player_id, created_at) VALUES (?, ?)",
                       playerId, now);
            db.Execute("INSERT OR IGNORE INTO CURRENCY_BALANCE VALUES (?, 'COINS', ?)",
                       playerId, startingCoins);
            db.Execute("INSERT OR IGNORE INTO CURRENCY_BALANCE VALUES (?, 'ENERGY', ?)",
                       playerId, startingEnergy);
            db.Execute(@"INSERT OR IGNORE INTO BUILDING_STATE
                (player_id, building_key, need_key, is_unlocked, level, workers_assigned, worker_slots)
                SELECT ?, building_key, need_key, CASE WHEN unlock_wave = 1 THEN 1 ELSE 0 END,
                       1, initial_workers, worker_slots FROM BUILDING_DEFINITION", playerId);
        });
    }
    
    public int GetCurrencyAmount(string playerId, string currencyCode)
    {
        return db.ExecuteScalar<int>(
            "SELECT amount FROM CURRENCY_BALANCE WHERE player_id = ? AND currency_code = ?",
            playerId, currencyCode
        );
    }

    public bool TrySpendCurrency(string playerId, string currencyCode, int amount, string reason, long? waveRunId = null)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (amount == 0) return true;
        bool spent = false;
        db.RunInTransaction(() =>
        {
            int changed = db.Execute("UPDATE CURRENCY_BALANCE SET amount = amount - ? WHERE player_id = ? AND currency_code = ? AND amount >= ?",
                amount, playerId, currencyCode, amount);
            if (changed == 0) return;
            db.Execute("INSERT INTO CURRENCY_LEDGER (player_id, currency_code, delta, reason, wave_run_id, created_at) VALUES (?, ?, ?, ?, ?, ?)",
                playerId, currencyCode, -amount, reason, waveRunId, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            spent = true;
        });
        return spent;
    }

    public void AddCurrency(string playerId, string currencyCode, int amount, string reason, long? waveRunId = null)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        db.RunInTransaction(() =>
        {
            db.Execute("INSERT OR IGNORE INTO CURRENCY_BALANCE (player_id, currency_code, amount) VALUES (?, ?, 0)", playerId, currencyCode);
            db.Execute("UPDATE CURRENCY_BALANCE SET amount = amount + ? WHERE player_id = ? AND currency_code = ?", amount, playerId, currencyCode);
            if (amount > 0)
                db.Execute("INSERT INTO CURRENCY_LEDGER (player_id, currency_code, delta, reason, wave_run_id, created_at) VALUES (?, ?, ?, ?, ?, ?)",
                    playerId, currencyCode, amount, reason, waveRunId, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        });
    }

    public void RecordWaveCompletion(string playerId, int waveNumber, int coinReward)
    {
        if (waveNumber < 1) throw new ArgumentOutOfRangeException(nameof(waveNumber));
        if (coinReward < 0) throw new ArgumentOutOfRangeException(nameof(coinReward));
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        db.RunInTransaction(() =>
        {
            db.Execute("UPDATE PLAYER SET highest_wave_completed = MAX(highest_wave_completed, ?) WHERE player_id = ?", waveNumber, playerId);
            db.Execute("UPDATE CURRENCY_BALANCE SET amount = amount + ? WHERE player_id = ? AND currency_code = 'COINS'", coinReward, playerId);
            if (coinReward > 0)
                db.Execute("INSERT INTO CURRENCY_LEDGER (player_id, currency_code, delta, reason, created_at) VALUES (?, 'COINS', ?, 'WAVE_REWARD', ?)", playerId, coinReward, now);
        });
    }

    public bool MoveWorker(string playerId, string fromBuildingKey, string toBuildingKey)
    {
        if (string.Equals(fromBuildingKey, toBuildingKey, StringComparison.Ordinal)) return false;
        bool moved = false;
        db.RunInTransaction(() =>
        {
            int source = db.ExecuteScalar<int>("SELECT workers_assigned FROM BUILDING_STATE WHERE player_id = ? AND building_key = ? AND is_unlocked = 1", playerId, fromBuildingKey);
            int target = db.ExecuteScalar<int>("SELECT workers_assigned FROM BUILDING_STATE WHERE player_id = ? AND building_key = ? AND is_unlocked = 1", playerId, toBuildingKey);
            if (source != 1 || target != 0) return;
            db.Execute("UPDATE BUILDING_STATE SET workers_assigned = 0 WHERE player_id = ? AND building_key = ?", playerId, fromBuildingKey);
            db.Execute("UPDATE BUILDING_STATE SET workers_assigned = 1 WHERE player_id = ? AND building_key = ?", playerId, toBuildingKey);
            moved = true;
        });
        return moved;
    }

    public bool TryUpgradeBuilding(string playerId, string buildingKey, int coinCost)
    {
        if (coinCost < 0) throw new ArgumentOutOfRangeException(nameof(coinCost));
        bool upgraded = false;
        db.RunInTransaction(() =>
        {
            int level = db.ExecuteScalar<int>("SELECT level FROM BUILDING_STATE WHERE player_id = ? AND building_key = ? AND is_unlocked = 1", playerId, buildingKey);
            if (level < 1 || level >= 3) return;
            int changed = db.Execute("UPDATE CURRENCY_BALANCE SET amount = amount - ? WHERE player_id = ? AND currency_code = 'COINS' AND amount >= ?", coinCost, playerId, coinCost);
            if (changed == 0 && coinCost > 0) return;
            db.Execute("UPDATE BUILDING_STATE SET level = level + 1 WHERE player_id = ? AND building_key = ?", playerId, buildingKey);
            if (coinCost > 0)
                db.Execute("INSERT INTO CURRENCY_LEDGER (player_id, currency_code, delta, reason, created_at) VALUES (?, 'COINS', ?, 'BUILDING_UPGRADE', ?)", playerId, -coinCost, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            upgraded = true;
        });
        return upgraded;
    }

    public BuildingStateRecord GetBuildingState(string playerId, string buildingKey)
    {
        var rows = db.Query<BuildingStateRecord>("SELECT * FROM BUILDING_STATE WHERE player_id = ? AND building_key = ?", playerId, buildingKey);
        return rows.Count == 0 ? null : rows[0];
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

[Table("BUILDING_STATE")]
public class BuildingStateRecord
{
    public string player_id { get; set; }
    public string building_key { get; set; }
    public string need_key { get; set; }
    public int is_unlocked { get; set; }
    public int level { get; set; }
    public int workers_assigned { get; set; }
    public int worker_slots { get; set; }
}
