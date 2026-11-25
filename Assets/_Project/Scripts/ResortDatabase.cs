using System;
using System.Collections.Generic;
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
        new[] {
            "ALTER TABLE WAVE_RUN ADD COLUMN completion_token TEXT",
            "CREATE UNIQUE INDEX ux_wave_run_completion_token ON WAVE_RUN(completion_token) WHERE completion_token IS NOT NULL"
        },
        new[] {
            "ALTER TABLE WAVE_RUN ADD COLUMN total_need_units INTEGER",
            "ALTER TABLE WAVE_RUN ADD COLUMN completed_need_units INTEGER",
            "ALTER TABLE WAVE_RUN ADD COLUMN visual_guest_count INTEGER"
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

    /// <summary>
    /// Commits one completed wave and its currency rewards atomically. The unique
    /// completion token makes retries/reopening the same result idempotent, while a
    /// replayed wave can use a fresh token and receive a new reward.
    /// </summary>
    public bool RecordWaveCompletionOnce(string playerId, WaveCompletionResult result)
    {
        ValidateWaveResult(result);
        bool recorded = false;
        db.RunInTransaction(() =>
        {
            int existing = db.ExecuteScalar<int>("SELECT COUNT(*) FROM WAVE_RUN WHERE completion_token = ?", result.CompletionToken);
            if (existing != 0) return;

            int inserted = db.Execute(@"INSERT OR IGNORE INTO WAVE_RUN
                (player_id, wave_number, status, arrival_closed, rating, satisfaction_pct,
                 coins_awarded, energy_awarded, completion_token, started_at, completed_at,
                 total_need_units, completed_need_units, visual_guest_count)
                VALUES (?, ?, 'COMPLETE', 1, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                playerId, result.WaveNumber, result.Rating, result.Satisfaction * 100f,
                result.Coins, result.Energy, result.CompletionToken, result.StartedAtUnixSeconds,
                result.CompletedAtUnixSeconds, result.TotalNeedUnits, result.CompletedNeedUnits,
                result.VisualGuestCount);
            if (inserted != 1) throw new InvalidOperationException("Completed wave could not be saved.");

            long waveRunId = db.ExecuteScalar<long>("SELECT last_insert_rowid();");
            foreach (WaveNeedPerformance service in result.Services)
                db.Execute(@"INSERT INTO WAVE_NEED_RESULT
                    (wave_run_id, need_key, demand_total, units_served, avg_contribution_pct, bottleneck_seconds)
                    VALUES (?, ?, ?, ?, ?, ?)", waveRunId, NeedKey(service.Need), service.DemandTotal,
                    service.UnitsServed, service.AverageContribution * 100f, service.BottleneckSeconds);

            int progressed = db.Execute("UPDATE PLAYER SET highest_wave_completed = MAX(highest_wave_completed, ?) WHERE player_id = ?", result.WaveNumber, playerId);
            if (progressed != 1) throw new InvalidOperationException("Player progression could not be saved.");
            db.Execute("INSERT OR IGNORE INTO CURRENCY_BALANCE (player_id, currency_code, amount) VALUES (?, 'COINS', 0)", playerId);
            db.Execute("INSERT OR IGNORE INTO CURRENCY_BALANCE (player_id, currency_code, amount) VALUES (?, 'ENERGY', 0)", playerId);
            if (GetCurrencyAmount(playerId, "COINS") > int.MaxValue - result.Coins
                || GetCurrencyAmount(playerId, "ENERGY") > int.MaxValue - result.Energy)
                throw new OverflowException("Wave rewards exceed the supported currency balance.");

            if (result.Coins > 0)
            {
                db.Execute("UPDATE CURRENCY_BALANCE SET amount = amount + ? WHERE player_id = ? AND currency_code = 'COINS'", result.Coins, playerId);
                db.Execute("INSERT INTO CURRENCY_LEDGER (player_id, currency_code, delta, reason, wave_run_id, created_at) VALUES (?, 'COINS', ?, 'WAVE_REWARD', ?, ?)", playerId, result.Coins, waveRunId, result.CompletedAtUnixSeconds);
            }

            if (result.Energy > 0)
            {
                db.Execute("UPDATE CURRENCY_BALANCE SET amount = amount + ? WHERE player_id = ? AND currency_code = 'ENERGY'", result.Energy, playerId);
                db.Execute("INSERT INTO CURRENCY_LEDGER (player_id, currency_code, delta, reason, wave_run_id, created_at) VALUES (?, 'ENERGY', ?, 'WAVE_ENERGY_REWARD', ?, ?)", playerId, result.Energy, waveRunId, result.CompletedAtUnixSeconds);
            }

            recorded = true;
        });
        return recorded;
    }

    private static string NeedKey(ResortNeed need)
    {
        switch (need)
        {
            case ResortNeed.Rooms: return "ROOMS";
            case ResortNeed.Drinks: return "DRINKS";
            case ResortNeed.Food: return "FOOD";
            case ResortNeed.Relax: return "RELAX";
            case ResortNeed.OceanView: return "OCEAN_VIEW";
            default: throw new ArgumentOutOfRangeException(nameof(need));
        }
    }

    private static void ValidateWaveResult(WaveCompletionResult result)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        if (result.WaveNumber < 1 || string.IsNullOrWhiteSpace(result.CompletionToken)
            || float.IsNaN(result.Satisfaction) || float.IsInfinity(result.Satisfaction)
            || result.Satisfaction < 0f || result.Satisfaction > 1f
            || result.Rating < 1 || result.Rating > 6 || result.Coins < 0 || result.Energy < 0
            || result.TotalNeedUnits <= 0 || result.CompletedNeedUnits != result.TotalNeedUnits
            || result.VisualGuestCount < 0 || result.StartedAtUnixSeconds <= 0
            || result.CompletedAtUnixSeconds < result.StartedAtUnixSeconds
            || result.Services == null || result.Services.Count == 0)
            throw new ArgumentException("Completed wave result is invalid.", nameof(result));

        int total = 0;
        var seen = new HashSet<ResortNeed>();
        foreach (WaveNeedPerformance service in result.Services)
        {
            if (!seen.Add(service.Need) || service.DemandTotal <= 0
                || service.UnitsServed != service.DemandTotal
                || float.IsNaN(service.AverageContribution) || float.IsInfinity(service.AverageContribution)
                || service.AverageContribution < 0f || service.AverageContribution > 1f
                || float.IsNaN(service.BottleneckSeconds) || float.IsInfinity(service.BottleneckSeconds)
                || service.BottleneckSeconds < 0f)
                throw new ArgumentException("Per-service wave result is invalid.", nameof(result));
            NeedKey(service.Need);
            total = checked(total + service.DemandTotal);
        }
        if (total != result.TotalNeedUnits)
            throw new ArgumentException("Service totals do not match the completed wave.", nameof(result));
    }

    public SavedWaveResult GetCompletedWaveResult(string playerId, string completionToken)
    {
        if (string.IsNullOrWhiteSpace(completionToken)) return null;
        var runs = db.Query<WaveRunResultRow>(@"SELECT wave_run_id, wave_number, completion_token,
            satisfaction_pct, rating, coins_awarded, energy_awarded, started_at, completed_at,
            total_need_units, completed_need_units, visual_guest_count
            FROM WAVE_RUN WHERE player_id = ? AND completion_token = ? AND status = 'COMPLETE'",
            playerId, completionToken);
        return runs.Count == 0 ? null : ReadSavedWaveResult(runs[0]);
    }

    public SavedWaveResult GetLatestCompletedWaveResult(string playerId)
    {
        var runs = db.Query<WaveRunResultRow>(@"SELECT wave_run_id, wave_number, completion_token,
            satisfaction_pct, rating, coins_awarded, energy_awarded, started_at, completed_at,
            total_need_units, completed_need_units, visual_guest_count
            FROM WAVE_RUN WHERE player_id = ? AND status = 'COMPLETE'
            ORDER BY completed_at DESC, wave_run_id DESC LIMIT 1", playerId);
        return runs.Count == 0 ? null : ReadSavedWaveResult(runs[0]);
    }

    private SavedWaveResult ReadSavedWaveResult(WaveRunResultRow run)
    {
        var rows = db.Query<WaveNeedResultRow>(@"SELECT need_key, demand_total, units_served,
            avg_contribution_pct, bottleneck_seconds FROM WAVE_NEED_RESULT WHERE wave_run_id = ?
            ORDER BY need_key", run.wave_run_id);
        var services = new List<SavedWaveNeedResult>(rows.Count);
        foreach (WaveNeedResultRow row in rows)
            services.Add(new SavedWaveNeedResult(row));
        return new SavedWaveResult(run, services);
    }

    public bool MoveWorker(string playerId, string fromBuildingKey, string toBuildingKey)
    {
        if (string.Equals(fromBuildingKey, toBuildingKey, StringComparison.Ordinal)) return false;
        bool moved = false;
        db.RunInTransaction(() =>
        {
            int unlockedCount = db.ExecuteScalar<int>("SELECT COUNT(*) FROM BUILDING_STATE WHERE player_id = ? AND is_unlocked = 1", playerId);
            int assignedCount = db.ExecuteScalar<int>("SELECT COALESCE(SUM(workers_assigned), 0) FROM BUILDING_STATE WHERE player_id = ? AND is_unlocked = 1", playerId);
            if (unlockedCount < 2 || assignedCount != unlockedCount - 1) return;

            int source = db.ExecuteScalar<int>("SELECT workers_assigned FROM BUILDING_STATE WHERE player_id = ? AND building_key = ? AND is_unlocked = 1", playerId, fromBuildingKey);
            int target = db.ExecuteScalar<int>("SELECT workers_assigned FROM BUILDING_STATE WHERE player_id = ? AND building_key = ? AND is_unlocked = 1", playerId, toBuildingKey);
            int targetSlots = db.ExecuteScalar<int>("SELECT worker_slots FROM BUILDING_STATE WHERE player_id = ? AND building_key = ? AND is_unlocked = 1", playerId, toBuildingKey);
            if (source != 1 || target != 0 || targetSlots != 1) return;
            db.Execute("UPDATE BUILDING_STATE SET workers_assigned = 0 WHERE player_id = ? AND building_key = ?", playerId, fromBuildingKey);
            db.Execute("UPDATE BUILDING_STATE SET workers_assigned = 1 WHERE player_id = ? AND building_key = ?", playerId, toBuildingKey);
            moved = true;
        });
        return moved;
    }

    /// <summary>Purchases an unlocked-by-progression service and places its new Worker atomically.</summary>
    public bool TryUnlockBuilding(string playerId, string buildingKey, int coinCost)
    {
        if (coinCost < 0) throw new ArgumentOutOfRangeException(nameof(coinCost));
        bool unlocked = false;
        db.RunInTransaction(() =>
        {
            int requiredWave = db.ExecuteScalar<int>(
                "SELECT unlock_wave FROM BUILDING_DEFINITION WHERE building_key = ?", buildingKey);
            int wasUnlocked = db.ExecuteScalar<int>(
                "SELECT is_unlocked FROM BUILDING_STATE WHERE player_id = ? AND building_key = ?", playerId, buildingKey);
            if (requiredWave <= 1 || wasUnlocked != 0) return;

            int highestWave = GetHighestWaveCompleted(playerId);
            if (highestWave < requiredWave) return;

            int unlockedCount = db.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM BUILDING_STATE WHERE player_id = ? AND is_unlocked = 1", playerId);
            int assignedCount = db.ExecuteScalar<int>(
                "SELECT COALESCE(SUM(workers_assigned), 0) FROM BUILDING_STATE WHERE player_id = ? AND is_unlocked = 1", playerId);
            if (assignedCount != unlockedCount - 1) return;

            if (coinCost > 0)
            {
                int spent = db.Execute("UPDATE CURRENCY_BALANCE SET amount = amount - ? WHERE player_id = ? AND currency_code = 'COINS' AND amount >= ?",
                    coinCost, playerId, coinCost);
                if (spent == 0) return;
                db.Execute("INSERT INTO CURRENCY_LEDGER (player_id, currency_code, delta, reason, created_at) VALUES (?, 'COINS', ?, 'BUILDING_UNLOCK', ?)",
                    playerId, -coinCost, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            }

            int changed = db.Execute("UPDATE BUILDING_STATE SET is_unlocked = 1, level = 1, workers_assigned = 1 WHERE player_id = ? AND building_key = ? AND is_unlocked = 0 AND worker_slots = 1",
                playerId, buildingKey);
            if (changed == 1) unlocked = true;
        });
        return unlocked;
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
            if (changed != 1) return;
            int levelChanged = db.Execute("UPDATE BUILDING_STATE SET level = level + 1 WHERE player_id = ? AND building_key = ? AND is_unlocked = 1 AND level = ? AND level < 3", playerId, buildingKey, level);
            if (levelChanged != 1)
                throw new InvalidOperationException("Building level changed during upgrade transaction.");
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
    public int GetHighestWaveCompleted(string playerId) => db.ExecuteScalar<int>(
        "SELECT highest_wave_completed FROM PLAYER WHERE player_id = ?", playerId);
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

public sealed class WaveRunResultRow
{
    public long wave_run_id { get; set; }
    public int wave_number { get; set; }
    public string completion_token { get; set; }
    public double? satisfaction_pct { get; set; }
    public int? rating { get; set; }
    public int? coins_awarded { get; set; }
    public int? energy_awarded { get; set; }
    public long started_at { get; set; }
    public long? completed_at { get; set; }
    public int? total_need_units { get; set; }
    public int? completed_need_units { get; set; }
    public int? visual_guest_count { get; set; }
}

public sealed class WaveNeedResultRow
{
    public string need_key { get; set; }
    public int demand_total { get; set; }
    public int units_served { get; set; }
    public double? avg_contribution_pct { get; set; }
    public double? bottleneck_seconds { get; set; }
}

public sealed class SavedWaveNeedResult
{
    public string NeedKey { get; }
    public int DemandTotal { get; }
    public int UnitsServed { get; }
    public double? AverageContribution { get; }
    public double? BottleneckSeconds { get; }

    public SavedWaveNeedResult(WaveNeedResultRow row)
    {
        NeedKey = row.need_key;
        DemandTotal = row.demand_total;
        UnitsServed = row.units_served;
        AverageContribution = row.avg_contribution_pct / 100d;
        BottleneckSeconds = row.bottleneck_seconds;
    }
}

public sealed class SavedWaveResult
{
    public int WaveNumber { get; }
    public string CompletionToken { get; }
    public double? Satisfaction { get; }
    public int? Rating { get; }
    public bool PerfectStay => Rating == 6;
    public int? Coins { get; }
    public int? Energy { get; }
    public int? TotalNeedUnits { get; }
    public int? CompletedNeedUnits { get; }
    public int? VisualGuestCount { get; }
    public long StartedAtUnixSeconds { get; }
    public long? CompletedAtUnixSeconds { get; }
    public IReadOnlyList<SavedWaveNeedResult> Services { get; }
    public SavedWaveNeedResult MostSignificantBottleneck { get; }

    public SavedWaveResult(WaveRunResultRow row, List<SavedWaveNeedResult> services)
    {
        WaveNumber = row.wave_number;
        CompletionToken = row.completion_token;
        Satisfaction = row.satisfaction_pct / 100d;
        Rating = row.rating;
        Coins = row.coins_awarded;
        Energy = row.energy_awarded;
        TotalNeedUnits = row.total_need_units;
        CompletedNeedUnits = row.completed_need_units;
        VisualGuestCount = row.visual_guest_count;
        StartedAtUnixSeconds = row.started_at;
        CompletedAtUnixSeconds = row.completed_at;
        Services = services.AsReadOnly();
        foreach (SavedWaveNeedResult service in services)
            if (service.BottleneckSeconds > 0d
                && (MostSignificantBottleneck == null
                    || service.BottleneckSeconds > MostSignificantBottleneck.BottleneckSeconds))
                MostSignificantBottleneck = service;
    }
}
