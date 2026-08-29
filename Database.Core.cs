using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InputFiles;
using Telegram.Bot.Types.ReplyMarkups;
using Microsoft.Data.Sqlite;

static partial class Database
{
    private static string ConnectionString =>
        $"Data Source={Environment.GetEnvironmentVariable("ALICE_DB_PATH") ?? "gamedata.db"}";
    private static SqliteConnection OpenCon()
    {
        var con = new SqliteConnection(ConnectionString);
        con.Open();
        using var p = con.CreateCommand();
        p.CommandText = "PRAGMA busy_timeout=5000; PRAGMA journal_mode=WAL;";
        p.ExecuteNonQuery();
        return con;
    }

    public static SqliteConnection OpenConForAdmin() => OpenCon();

    public static void CreateConsistentBackup(string destinationPath)
    {
        string fullPath = System.IO.Path.GetFullPath(destinationPath);
        if (System.IO.File.Exists(fullPath))
            System.IO.File.Delete(fullPath);

        using var source = OpenCon();
        using var destination = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = fullPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString());
        destination.Open();
        source.BackupDatabase(destination);
    }

    public static bool ValidateDatabaseFile(string path, out string error)
    {
        error = "";
        try
        {
            using var con = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = System.IO.Path.GetFullPath(path),
                    Mode = SqliteOpenMode.ReadOnly,
                    Pooling = false
                }.ToString());
            con.Open();

            using (var integrity = con.CreateCommand())
            {
                integrity.CommandText = "PRAGMA quick_check;";
                string result = Convert.ToString(integrity.ExecuteScalar()) ?? "";
                if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    error = string.IsNullOrWhiteSpace(result) ? "SQLite integrity check failed." : result;
                    return false;
                }
            }

            using var schema = con.CreateCommand();
            schema.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Countries'";
            if (Convert.ToInt32(schema.ExecuteScalar()) != 1)
            {
                error = "جدول Countries در فایل وجود ندارد.";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static void CheckpointAndClearPools()
    {
        using (var con = OpenCon())
        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
    }

    public static void Init()
    {
        using var con = OpenCon();
        string countries = @"
        CREATE TABLE IF NOT EXISTS Countries(
            ChatId INTEGER NOT NULL,
            OwnerId INTEGER NOT NULL,
            Name TEXT NOT NULL,
            OwnerName TEXT NOT NULL,
            Faction INTEGER NOT NULL,
            FlagFileId TEXT,
            Money INTEGER NOT NULL DEFAULT 10000,
            Population INTEGER NOT NULL DEFAULT 100000,
            Cities INTEGER DEFAULT 4,
            FactoryLevel INTEGER DEFAULT 1,
            PortLevel INTEGER DEFAULT 1,
            MineLevel INTEGER DEFAULT 1,
            Iron INTEGER DEFAULT 0,
            Soldiers INTEGER DEFAULT 10000,
            RecruitmentRate INTEGER DEFAULT 0,
            Welfare REAL DEFAULT 100,
            Tanks INTEGER DEFAULT 0,
            Planes INTEGER DEFAULT 0,
            Bombers INTEGER DEFAULT 0,
            AntiAir INTEGER DEFAULT 0,
            DefenseFighters INTEGER DEFAULT 0,
            AirDefStrategy INTEGER DEFAULT 1,
            AirDefTactic INTEGER DEFAULT 1,
            Besieged INTEGER DEFAULT 0,
            DefenseWins INTEGER DEFAULT 0,
            CreatedAtMs INTEGER DEFAULT 0,
            DefenseTanks INTEGER DEFAULT 0,
            DefenseSoldiers INTEGER DEFAULT 0,
            DefenseStrategy INTEGER DEFAULT 1,
            DefenseTactic INTEGER DEFAULT 1,
            TaxRate INTEGER DEFAULT 30,
            PRIMARY KEY(ChatId,OwnerId)
        );";
        string flags = @"
        CREATE TABLE IF NOT EXISTS FactionFlags(
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Faction TEXT NOT NULL,
            FileId TEXT NOT NULL
        );";
        string settings = @"
        CREATE TABLE IF NOT EXISTS Settings(
            Key TEXT PRIMARY KEY,
            Value TEXT
        );";
        string royal = @"
        CREATE TABLE IF NOT EXISTS RoyalCoins(
            OwnerId INTEGER PRIMARY KEY,
            Amount INTEGER DEFAULT 0
        );";
        string cooldowns = @"
        CREATE TABLE IF NOT EXISTS LeaveCooldowns(
            OwnerId INTEGER NOT NULL,
            ChatId INTEGER NOT NULL,
            UntilUnixMs INTEGER NOT NULL,
            PRIMARY KEY(OwnerId,ChatId)
        );";
        string defeats = @"
        CREATE TABLE IF NOT EXISTS RoutDefeats(
            DefenderId INTEGER NOT NULL,
            ChatId INTEGER NOT NULL,
            AttackerId INTEGER NOT NULL,
            Count INTEGER NOT NULL DEFAULT 0,
            PRIMARY KEY(DefenderId,ChatId,AttackerId)
        );";
        string shieldExemptions = @"
        CREATE TABLE IF NOT EXISTS ShieldExemptions(
            OwnerId INTEGER NOT NULL,
            ChatId INTEGER NOT NULL,
            PRIMARY KEY(OwnerId,ChatId)
        );";
        string alliances = @"
        CREATE TABLE IF NOT EXISTS Alliances(
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            ChatId INTEGER NOT NULL,
            Name TEXT NOT NULL,
            FlagFileId TEXT,
            LeaderId INTEGER NOT NULL,
            CreatedAtMs INTEGER NOT NULL
        );";
        string allianceMembers = @"
        CREATE TABLE IF NOT EXISTS AllianceMembers(
            AllianceId INTEGER NOT NULL,
            ChatId INTEGER NOT NULL,
            UserId INTEGER NOT NULL,
            PRIMARY KEY(ChatId,UserId)
        );";
        string allianceInvites = @"
        CREATE TABLE IF NOT EXISTS AllianceInvites(
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            AllianceId INTEGER NOT NULL,
            ChatId INTEGER NOT NULL,
            TargetUserId INTEGER NOT NULL,
            LeaderId INTEGER NOT NULL,
            CreatedAtMs INTEGER NOT NULL
        );";
        string transfers = @"
        CREATE TABLE IF NOT EXISTS Transfers(
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            ChatId INTEGER NOT NULL,
            AllianceId INTEGER NOT NULL,
            SenderId INTEGER NOT NULL,
            ReceiverId INTEGER NOT NULL,
            ResourceType TEXT NOT NULL,
            ModelName TEXT NOT NULL DEFAULT '',
            Amount INTEGER NOT NULL,
            ArriveAtMs INTEGER NOT NULL,
            Notified INTEGER DEFAULT 0
        );";
        string deployments = @"
        CREATE TABLE IF NOT EXISTS Deployments(
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            ChatId INTEGER NOT NULL,
            AllianceId INTEGER NOT NULL,
            InitiatorId INTEGER NOT NULL,
            TargetUserId INTEGER NOT NULL,
            Type TEXT NOT NULL,
            DurationHours INTEGER NOT NULL,
            FormationType TEXT NOT NULL,
            Strategy INTEGER DEFAULT 1,
            Tactic INTEGER DEFAULT 1,
            Tanks INTEGER DEFAULT 0,
            Soldiers INTEGER DEFAULT 0,
            Fighters INTEGER DEFAULT 0,
            Bombers INTEGER DEFAULT 0,
            CreatedAtMs INTEGER NOT NULL,
            EndAtMs INTEGER NOT NULL,
            LastWarnMs INTEGER DEFAULT 0,
            AnnounceMsgId INTEGER DEFAULT 0
        );";
        string deploymentContributors = @"
        CREATE TABLE IF NOT EXISTS DeploymentContributors(
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            DeploymentId INTEGER NOT NULL,
            UserId INTEGER NOT NULL,
            Tanks INTEGER DEFAULT 0,
            Soldiers INTEGER DEFAULT 0,
            Fighters INTEGER DEFAULT 0,
            Bombers INTEGER DEFAULT 0,
            Strategy INTEGER DEFAULT 1,
            Tactic INTEGER DEFAULT 1
        );";
        string deploymentContributorModels = @"
        CREATE TABLE IF NOT EXISTS DeploymentContributorModels(
            DeploymentId INTEGER NOT NULL,
            UserId INTEGER NOT NULL,
            Category TEXT NOT NULL,
            ModelName TEXT NOT NULL,
            Count INTEGER NOT NULL DEFAULT 0,
            PRIMARY KEY(DeploymentId,UserId,Category,ModelName)
        );";
    string visionLogs = @"CREATE TABLE IF NOT EXISTS VisionLogs(Id INTEGER PRIMARY KEY AUTOINCREMENT, SourceChatId INTEGER NOT NULL DEFAULT 0, SourceUserId INTEGER NOT NULL DEFAULT 0, DestChatId INTEGER NOT NULL, IsUserMode INTEGER NOT NULL DEFAULT 0, CreatedAtMs INTEGER NOT NULL);";
    string visionMessageMap = @"CREATE TABLE IF NOT EXISTS VisionMessageMap(Id INTEGER PRIMARY KEY AUTOINCREMENT, SourceChatId INTEGER NOT NULL, SourceMessageId INTEGER NOT NULL, SourceUserId INTEGER NOT NULL DEFAULT 0, DestChatId INTEGER NOT NULL, DestMessageId INTEGER NOT NULL, CreatedAtMs INTEGER NOT NULL);";
        string groupLockExemptions = @"
        CREATE TABLE IF NOT EXISTS GroupLockExemptions(
            ChatId INTEGER PRIMARY KEY
        );";

        string attackAbandonLocks = @"CREATE TABLE IF NOT EXISTS AttackAbandonLocks(OwnerId INTEGER NOT NULL, LockedUntilMs INTEGER NOT NULL, PRIMARY KEY(OwnerId));";
        string dailyDefendCounts = @"CREATE TABLE IF NOT EXISTS DailyDefendCounts(DefenderId INTEGER NOT NULL, AttackDate TEXT NOT NULL, Count INTEGER NOT NULL DEFAULT 0, PRIMARY KEY(DefenderId,AttackDate));";
        string attackerFlags = @"CREATE TABLE IF NOT EXISTS AttackerFlags(OwnerId INTEGER NOT NULL, AttackDate TEXT NOT NULL, PRIMARY KEY(OwnerId, AttackDate));";
        string heavyOffensiveWins = @"CREATE TABLE IF NOT EXISTS HeavyOffensiveWins(OwnerId INTEGER NOT NULL, ChatId INTEGER NOT NULL, Count INTEGER NOT NULL DEFAULT 0, PRIMARY KEY(OwnerId,ChatId));";
        string activeSieges = @"CREATE TABLE IF NOT EXISTS ActiveSieges(DefenderId INTEGER NOT NULL, ChatId INTEGER NOT NULL, AttackerId INTEGER NOT NULL, PRIMARY KEY(DefenderId,ChatId));";
        string warBattles = @"CREATE TABLE IF NOT EXISTS WarBattles(Id INTEGER PRIMARY KEY AUTOINCREMENT, Timestamp TEXT NOT NULL, ChatId INTEGER, AttackerId INTEGER, DefenderId INTEGER, AttackerName TEXT, DefenderName TEXT, Winner TEXT, PenetrationKm REAL, SuccessPercent INTEGER, AtkTankLoss INTEGER, AtkSoldierLoss INTEGER, DefTankLoss INTEGER, DefSoldierLoss INTEGER, LootMoney INTEGER, LootIron INTEGER, DurationMinutes INTEGER, Report TEXT);";
        string battleJobs = @"CREATE TABLE IF NOT EXISTS BattleJobs(BattleId INTEGER PRIMARY KEY, JobType TEXT NOT NULL, RequestJson TEXT NOT NULL, ContextJson TEXT NOT NULL DEFAULT '', Status TEXT NOT NULL DEFAULT 'Pending', ResultJson TEXT NOT NULL DEFAULT '', LastError TEXT NOT NULL DEFAULT '', CreatedAtMs INTEGER NOT NULL, UpdatedAtMs INTEGER NOT NULL);";
        string eqModels = @"CREATE TABLE IF NOT EXISTS EquipmentModels(OwnerId INTEGER NOT NULL, ChatId INTEGER NOT NULL, Category TEXT NOT NULL, ModelName TEXT NOT NULL, Count INTEGER NOT NULL DEFAULT 0, PRIMARY KEY(OwnerId,ChatId,Category,ModelName));";
        string defenseModels = @"CREATE TABLE IF NOT EXISTS DefenseModels(OwnerId INTEGER NOT NULL, ChatId INTEGER NOT NULL, Category TEXT NOT NULL, ModelName TEXT NOT NULL, DefPct INTEGER NOT NULL DEFAULT 100, PRIMARY KEY(OwnerId,ChatId,Category,ModelName));";
        string defenseModelAmounts = @"CREATE TABLE IF NOT EXISTS DefenseModelAmounts(OwnerId INTEGER NOT NULL, ChatId INTEGER NOT NULL, Category TEXT NOT NULL, ModelName TEXT NOT NULL, Count INTEGER NOT NULL DEFAULT 0, PRIMARY KEY(OwnerId,ChatId,Category,ModelName));";
        string defenseConfigurationFlags = @"CREATE TABLE IF NOT EXISTS DefenseConfigurationFlags(OwnerId INTEGER NOT NULL, ChatId INTEGER NOT NULL, SoldiersConfigured INTEGER NOT NULL DEFAULT 0, PRIMARY KEY(OwnerId,ChatId));";
        string botGroupStatus = @"CREATE TABLE IF NOT EXISTS BotGroupStatus(ChatId INTEGER PRIMARY KEY, IsActive INTEGER NOT NULL DEFAULT 1, UpdatedAtMs INTEGER NOT NULL DEFAULT 0);";
        string spamRestrictions = @"CREATE TABLE IF NOT EXISTS SpamRestrictions(UserId INTEGER PRIMARY KEY, ChatId INTEGER NOT NULL DEFAULT 0, UntilMs INTEGER NOT NULL DEFAULT 0, Level INTEGER NOT NULL DEFAULT 0, Reason TEXT NOT NULL DEFAULT '', LastFingerprint TEXT NOT NULL DEFAULT '', DroppedCount INTEGER NOT NULL DEFAULT 0, UpdatedAtMs INTEGER NOT NULL DEFAULT 0);";
        //  – naval expansion tables
        string navalInvasions = @"CREATE TABLE IF NOT EXISTS NavalInvasions(Id INTEGER PRIMARY KEY AUTOINCREMENT, ChatId INTEGER NOT NULL, AttackerId INTEGER NOT NULL, DefenderId INTEGER NOT NULL, Boats INTEGER DEFAULT 0, Submarines INTEGER DEFAULT 0, Battleships INTEGER DEFAULT 0, BoatModels TEXT DEFAULT '', SubModels TEXT DEFAULT '', BattleshipModels TEXT DEFAULT '', Strategy INTEGER DEFAULT 1, Tactic INTEGER DEFAULT 1, CreatedAtMs INTEGER NOT NULL, ArriveAtMs INTEGER NOT NULL, Processed INTEGER DEFAULT 0, AttackerName TEXT DEFAULT '', DefenderName TEXT DEFAULT '');";
        string attackShields = @"CREATE TABLE IF NOT EXISTS AttackShields(OwnerId INTEGER NOT NULL, ChatId INTEGER NOT NULL, ShieldUntilMs INTEGER NOT NULL, AttackCount INTEGER DEFAULT 0, LastAttackMs INTEGER DEFAULT 0, PRIMARY KEY(OwnerId,ChatId));";
        string boatFuelStates = @"CREATE TABLE IF NOT EXISTS BoatFuelStates(OwnerId INTEGER NOT NULL, ChatId INTEGER NOT NULL, FuelPct INTEGER DEFAULT 100, PRIMARY KEY(OwnerId,ChatId));";
        string navalBoatCooldowns = @"CREATE TABLE IF NOT EXISTS NavalBoatCooldowns(OwnerId INTEGER NOT NULL, ChatId INTEGER NOT NULL, CooldownUntilMs INTEGER NOT NULL, PRIMARY KEY(OwnerId,ChatId));";
        foreach (var sql in new[] { countries, flags, settings, royal, cooldowns, defeats, shieldExemptions, alliances, allianceMembers, allianceInvites, transfers, deployments, deploymentContributors, deploymentContributorModels, groupLockExemptions, visionLogs, visionMessageMap, attackAbandonLocks, dailyDefendCounts, attackerFlags, heavyOffensiveWins, activeSieges, warBattles, battleJobs, eqModels, defenseModels, defenseModelAmounts, defenseConfigurationFlags, botGroupStatus, spamRestrictions, navalInvasions, attackShields, boatFuelStates, navalBoatCooldowns })
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        using (var indexes = con.CreateCommand())
        {
            indexes.CommandText = @"
                CREATE INDEX IF NOT EXISTS IX_Transfers_ArriveAtMs ON Transfers(ArriveAtMs);
                CREATE INDEX IF NOT EXISTS IX_Deployments_EndAtMs ON Deployments(EndAtMs);
                CREATE INDEX IF NOT EXISTS IX_DeploymentContributors_DeploymentUser
                    ON DeploymentContributors(DeploymentId, UserId);
                CREATE INDEX IF NOT EXISTS IX_AllianceMembers_AllianceId
                    ON AllianceMembers(AllianceId);
                CREATE INDEX IF NOT EXISTS IX_AllianceInvites_Target
                    ON AllianceInvites(ChatId, TargetUserId);
                CREATE INDEX IF NOT EXISTS IX_VisionLogs_SourceChat
                    ON VisionLogs(SourceChatId);
                CREATE INDEX IF NOT EXISTS IX_VisionLogs_SourceUser
                    ON VisionLogs(SourceUserId);
                CREATE INDEX IF NOT EXISTS IX_VisionMessageMap_Source
                    ON VisionMessageMap(SourceChatId, SourceMessageId, DestChatId);
                CREATE INDEX IF NOT EXISTS IX_VisionMessageMap_Destination
                    ON VisionMessageMap(DestChatId, DestMessageId);";
            indexes.ExecuteNonQuery();
        }

        EnsureColumn(con, "Countries", "Soldiers", "INTEGER DEFAULT 10000");
        EnsureColumn(con, "Countries", "RecruitmentRate", "INTEGER DEFAULT 0");
        EnsureColumn(con, "Countries", "Welfare", "REAL DEFAULT 100");
        EnsureColumn(con, "Countries", "PortLevel", "INTEGER DEFAULT 1");
        EnsureColumn(con, "Countries", "MineLevel", "INTEGER DEFAULT 1");
        EnsureColumn(con, "Countries", "Iron", "INTEGER DEFAULT 0");
        EnsureColumn(con, "Countries", "Tanks", "INTEGER DEFAULT 0");
        EnsureColumn(con, "Countries", "Planes", "INTEGER DEFAULT 0");
        EnsureColumn(con, "Countries", "Bombers", "INTEGER DEFAULT 0");
        EnsureColumn(con, "Countries", "AntiAir", "INTEGER DEFAULT 0");
        EnsureColumn(con, "Countries", "DefenseFighters", "INTEGER DEFAULT 0");
        EnsureColumn(con, "Countries", "AirDefStrategy", "INTEGER DEFAULT 1");
        EnsureColumn(con, "Countries", "AirDefTactic", "INTEGER DEFAULT 1");
        EnsureColumn(con, "Countries", "Besieged", "INTEGER DEFAULT 0");
        EnsureColumn(con, "Countries", "DefenseWins", "INTEGER DEFAULT 0");
        EnsureColumn(con, "Countries", "CreatedAtMs", "INTEGER DEFAULT 0");
        EnsureColumn(con, "Countries", "DefenseTanks", "INTEGER DEFAULT 0");
        EnsureColumn(con, "Countries", "DefenseSoldiers", "INTEGER DEFAULT 0");
        EnsureColumn(con, "Countries", "DefenseStrategy", "INTEGER DEFAULT 1");
        EnsureColumn(con, "Countries", "DefenseTactic", "INTEGER DEFAULT 1");
        EnsureColumn(con, "Countries", "TaxRate", "INTEGER DEFAULT 30");
        EnsureColumn(con, "Countries", "Cities", "INTEGER DEFAULT 4");
        EnsureColumn(con, "Countries", "DefTankPct", "INTEGER DEFAULT 100");
        EnsureColumn(con, "Countries", "DefSoldierPct", "INTEGER DEFAULT 100");
        EnsureColumn(con, "Countries", "DefFighterPct", "INTEGER DEFAULT 100");
        EnsureColumn(con, "Countries", "Boats", "INTEGER DEFAULT 0");
        EnsureColumn(con, "Countries", "Submarines", "INTEGER DEFAULT 0");
        EnsureColumn(con, "Countries", "Battleships", "INTEGER DEFAULT 0");
        EnsureColumn(con, "Countries", "BattleshipDamage", "INTEGER DEFAULT 0");
        EnsureColumn(con, "Countries", "DefenseBoats", "INTEGER DEFAULT 0");
        EnsureColumn(con, "Countries", "DefenseSubmarines", "INTEGER DEFAULT 0");
        EnsureColumn(con, "Countries", "BoatsFuel", "INTEGER DEFAULT 100");
        EnsureColumn(con, "Countries", "SubmarinesFuel", "INTEGER DEFAULT 100");
        EnsureColumn(con, "Countries", "BoatsAtSea", "INTEGER DEFAULT 0");
        EnsureColumn(con, "Countries", "SubmarinesAtSea", "INTEGER DEFAULT 0");
        EnsureColumn(con, "Countries", "BattleshipsAtSea", "INTEGER DEFAULT 0");
        // FIX(2): ستون جدید برای پیام پین‌شدهٔ صف‌آرایی (روی دیتابیس‌های قدیمی هم اضافه می‌شود)
        EnsureColumn(con, "Deployments", "AnnounceMsgId", "INTEGER DEFAULT 0");
        EnsureColumn(con, "DeploymentContributors", "ChatId", "INTEGER DEFAULT 0");
        EnsureColumn(con, "Transfers", "ModelName", "TEXT DEFAULT ''");
        // Legacy databases used 100% as an implicit default, which made every soldier
        // unavailable for attack. Only 20..99 can be recognized as explicit old choices;
        // untouched or ambiguous 100% rows safely fall back to the mandatory 20%.
        using (var migrateDefenseFlags = con.CreateCommand())
        {
            migrateDefenseFlags.CommandText = @"INSERT OR IGNORE INTO DefenseConfigurationFlags
                (OwnerId,ChatId,SoldiersConfigured)
                SELECT OwnerId,ChatId,1 FROM Countries WHERE DefSoldierPct BETWEEN 20 AND 99";
            migrateDefenseFlags.ExecuteNonQuery();
        }
        using (var backfillContributorChat = con.CreateCommand())
        {
            backfillContributorChat.CommandText = @"UPDATE DeploymentContributors
                SET ChatId=COALESCE((SELECT d.ChatId FROM Deployments d
                                    WHERE d.Id=DeploymentContributors.DeploymentId),ChatId)
                WHERE ChatId=0";
            backfillContributorChat.ExecuteNonQuery();
        }

        using (var fix = con.CreateCommand())
        {
            fix.CommandText = @"
                UPDATE Countries SET Population = 100000 WHERE Population IS NULL OR Population < 100000;
                UPDATE Countries SET Money       = 10000 WHERE Money IS NULL;
                UPDATE Countries SET Soldiers    = 10000 WHERE Soldiers IS NULL;
                UPDATE Countries SET Iron        = 0     WHERE Iron IS NULL;
                UPDATE Countries SET Tanks       = 0     WHERE Tanks IS NULL;
                UPDATE Countries SET Planes      = 0     WHERE Planes IS NULL;
                UPDATE Countries SET Bombers     = 0     WHERE Bombers IS NULL;
                UPDATE Countries SET AntiAir     = 0     WHERE AntiAir IS NULL;
                UPDATE Countries SET DefenseFighters = 0 WHERE DefenseFighters IS NULL;
                UPDATE Countries SET AirDefStrategy = 1 WHERE AirDefStrategy IS NULL;
                UPDATE Countries SET AirDefTactic = 1 WHERE AirDefTactic IS NULL;
                UPDATE Countries SET FactoryLevel= 1     WHERE FactoryLevel IS NULL OR FactoryLevel < 1;
                UPDATE Countries SET PortLevel   = 1     WHERE PortLevel IS NULL OR PortLevel < 1;
                UPDATE Countries SET MineLevel   = 1     WHERE MineLevel IS NULL OR MineLevel < 1;
                UPDATE Countries SET RecruitmentRate = 0 WHERE RecruitmentRate IS NULL;
                UPDATE Countries SET Welfare     = 100   WHERE Welfare IS NULL;
                UPDATE Countries SET TaxRate     = 30    WHERE TaxRate IS NULL;
                UPDATE Countries SET Cities      = 4     WHERE Cities IS NULL OR Cities < 1;
                UPDATE Countries SET DefenseStrategy = 1 WHERE DefenseStrategy IS NULL;
                UPDATE Countries SET DefenseTactic   = 1 WHERE DefenseTactic IS NULL;";
            fix.ExecuteNonQuery();
        }
    }

    private static void EnsureColumn(SqliteConnection con, string table, string column, string type)
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        bool exists = false;
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                if (reader.GetString(1) == column) { exists = true; break; }
            }
        }
        if (!exists)
        {
            using var alterCmd = con.CreateCommand();
            alterCmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type}";
            alterCmd.ExecuteNonQuery();
        }
    }

    private const string COUNTRY_COLS =
        "ChatId,OwnerId,Name,OwnerName,Faction,FlagFileId,Money,Population," +
        "FactoryLevel,PortLevel,MineLevel,Iron,Soldiers,RecruitmentRate,Welfare," +
        "Tanks,DefenseTanks,DefenseSoldiers,DefenseStrategy,DefenseTactic,Planes,TaxRate,Cities,Bombers,AntiAir,DefenseFighters,AirDefStrategy,AirDefTactic,Besieged,DefenseWins,CreatedAtMs,DefTankPct,DefSoldierPct,DefFighterPct," +
        "Boats,Submarines,Battleships,BattleshipDamage,DefenseBoats,DefenseSubmarines,BoatsFuel,SubmarinesFuel,BoatsAtSea,SubmarinesAtSea,BattleshipsAtSea";

    private static Country ReadCountry(SqliteDataReader r)
    {
        return new Country
        {
            ChatId = r.GetInt64(0),
            OwnerId = r.GetInt64(1),
            Name = r.GetString(2),
            OwnerName = r.GetString(3),
            Faction = (Faction)r.GetInt32(4),
            FlagFileId = r.IsDBNull(5) ? "" : r.GetString(5),
            Money = r.GetInt64(6),
            Population = r.GetInt64(7),
            FactoryLevel = r.IsDBNull(8) ? 1 : r.GetInt32(8),
            PortLevel = r.IsDBNull(9) ? 1 : r.GetInt32(9),
            MineLevel = r.IsDBNull(10) ? 1 : r.GetInt32(10),
            Iron = r.IsDBNull(11) ? 0 : r.GetInt64(11),
            Soldiers = r.IsDBNull(12) ? 10000 : r.GetInt64(12),
            RecruitmentRate = r.IsDBNull(13) ? 0 : r.GetInt32(13),
            Welfare = r.IsDBNull(14) ? 100 : r.GetDouble(14),
            Tanks = r.IsDBNull(15) ? 0 : r.GetInt64(15),
            DefenseTanks = r.IsDBNull(16) ? 0 : r.GetInt64(16),
            DefenseSoldiers = r.IsDBNull(17) ? 0 : r.GetInt64(17),
            DefenseStrategy = r.IsDBNull(18) ? 1 : r.GetInt32(18),
            DefenseTactic = r.IsDBNull(19) ? 1 : r.GetInt32(19),
            Planes = r.IsDBNull(20) ? 0 : r.GetInt64(20),
            TaxRate = r.IsDBNull(21) ? 30 : r.GetInt32(21),
            Cities = r.IsDBNull(22) ? 4 : r.GetInt32(22),
            Bombers = r.IsDBNull(23) ? 0 : r.GetInt64(23),
            AntiAir = r.IsDBNull(24) ? 0 : r.GetInt64(24),
            DefenseFighters = r.IsDBNull(25) ? 0 : r.GetInt64(25),
            AirDefStrategy = r.IsDBNull(26) ? 1 : r.GetInt32(26),
            AirDefTactic = r.IsDBNull(27) ? 1 : r.GetInt32(27),
            Besieged = r.IsDBNull(28) ? 0 : r.GetInt32(28),
            DefenseWins = r.IsDBNull(29) ? 0 : r.GetInt32(29),
            CreatedAtMs = r.IsDBNull(30) ? 0 : r.GetInt64(30),
            DefTankPct = r.FieldCount > 31 && !r.IsDBNull(31) ? r.GetInt32(31) : 100,
            DefSoldierPct = r.FieldCount > 32 && !r.IsDBNull(32) ? r.GetInt32(32) : 100,
            DefFighterPct = r.FieldCount > 33 && !r.IsDBNull(33) ? r.GetInt32(33) : 100,
            Boats = r.FieldCount > 34 && !r.IsDBNull(34) ? r.GetInt64(34) : 0,
            Submarines = r.FieldCount > 35 && !r.IsDBNull(35) ? r.GetInt64(35) : 0,
            Battleships = r.FieldCount > 36 && !r.IsDBNull(36) ? r.GetInt64(36) : 0,
            BattleshipDamage = r.FieldCount > 37 && !r.IsDBNull(37) ? r.GetInt64(37) : 0,
            DefenseBoats = r.FieldCount > 38 && !r.IsDBNull(38) ? r.GetInt64(38) : 0,
            DefenseSubmarines = r.FieldCount > 39 && !r.IsDBNull(39) ? r.GetInt64(39) : 0,
            BoatsFuel = r.FieldCount > 40 && !r.IsDBNull(40) ? r.GetInt32(40) : 100,
            SubmarinesFuel = r.FieldCount > 41 && !r.IsDBNull(41) ? r.GetInt32(41) : 100,
            BoatsAtSea = r.FieldCount > 42 && !r.IsDBNull(42) ? r.GetInt64(42) : 0,
            SubmarinesAtSea = r.FieldCount > 43 && !r.IsDBNull(43) ? r.GetInt64(43) : 0,
            BattleshipsAtSea = r.FieldCount > 44 && !r.IsDBNull(44) ? r.GetInt64(44) : 0,
        };
    }

    public static bool CountryExists(long ownerId, long chatId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Countries WHERE OwnerId=@id AND ChatId=@chat";
        cmd.Parameters.AddWithValue("@id", ownerId);
        cmd.Parameters.AddWithValue("@chat", chatId);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    public static bool CountryNameExists(string name)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Countries WHERE lower(Name)=lower(@name)";
        cmd.Parameters.AddWithValue("@name", name);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    public static void AddCountry(Country c)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"
        INSERT INTO Countries
          (ChatId,OwnerId,Name,OwnerName,Faction,FlagFileId,Money,Population,FactoryLevel,PortLevel,MineLevel,Iron,Soldiers,RecruitmentRate,Welfare,Tanks,Planes,DefenseTanks,DefenseSoldiers,DefenseStrategy,DefenseTactic,TaxRate,Cities,Bombers,AntiAir,DefenseFighters,AirDefStrategy,AirDefTactic,Besieged,DefenseWins,CreatedAtMs,Boats,Submarines,Battleships,BattleshipDamage,DefenseBoats,DefenseSubmarines,BoatsFuel,SubmarinesFuel,BoatsAtSea,SubmarinesAtSea,BattleshipsAtSea)
        VALUES
          (@ChatId,@OwnerId,@Name,@OwnerName,@Faction,@FlagFileId,@Money,@Population,@FactoryLevel,@PortLevel,@MineLevel,@Iron,@Soldiers,@RecruitmentRate,@Welfare,@Tanks,@Planes,@DefenseTanks,@DefenseSoldiers,@DefenseStrategy,@DefenseTactic,@TaxRate,@Cities,@Bombers,@AntiAir,@DefenseFighters,@AirDefStrategy,@AirDefTactic,@Besieged,@DefenseWins,@CreatedAtMs,@Boats,@Submarines,@Battleships,@BattleshipDamage,@DefenseBoats,@DefenseSubmarines,@BoatsFuel,@SubmarinesFuel,@BoatsAtSea,@SubmarinesAtSea,@BattleshipsAtSea)";
        cmd.Parameters.AddWithValue("@ChatId", c.ChatId);
        cmd.Parameters.AddWithValue("@OwnerId", c.OwnerId);
        cmd.Parameters.AddWithValue("@Name", c.Name);
        cmd.Parameters.AddWithValue("@OwnerName", c.OwnerName);
        cmd.Parameters.AddWithValue("@Faction", (int)c.Faction);
        cmd.Parameters.AddWithValue("@FlagFileId", c.FlagFileId);
        cmd.Parameters.AddWithValue("@Money", c.Money);
        cmd.Parameters.AddWithValue("@Population", c.Population);
        cmd.Parameters.AddWithValue("@FactoryLevel", c.FactoryLevel);
        cmd.Parameters.AddWithValue("@PortLevel", c.PortLevel);
        cmd.Parameters.AddWithValue("@MineLevel", c.MineLevel);
        cmd.Parameters.AddWithValue("@Iron", c.Iron);
        cmd.Parameters.AddWithValue("@Soldiers", c.Soldiers);
        cmd.Parameters.AddWithValue("@RecruitmentRate", c.RecruitmentRate);
        cmd.Parameters.AddWithValue("@Welfare", c.Welfare);
        cmd.Parameters.AddWithValue("@Tanks", c.Tanks);
        cmd.Parameters.AddWithValue("@Planes", c.Planes);
        cmd.Parameters.AddWithValue("@DefenseTanks", c.DefenseTanks);
        cmd.Parameters.AddWithValue("@DefenseSoldiers", c.DefenseSoldiers);
        cmd.Parameters.AddWithValue("@DefenseStrategy", c.DefenseStrategy);
        cmd.Parameters.AddWithValue("@DefenseTactic", c.DefenseTactic);
        cmd.Parameters.AddWithValue("@TaxRate", c.TaxRate);
        cmd.Parameters.AddWithValue("@Cities", c.Cities);
        cmd.Parameters.AddWithValue("@Bombers", c.Bombers);
        cmd.Parameters.AddWithValue("@AntiAir", c.AntiAir);
        cmd.Parameters.AddWithValue("@DefenseFighters", c.DefenseFighters);
        cmd.Parameters.AddWithValue("@AirDefStrategy", c.AirDefStrategy);
        cmd.Parameters.AddWithValue("@AirDefTactic", c.AirDefTactic);
        cmd.Parameters.AddWithValue("@Besieged", c.Besieged);
        cmd.Parameters.AddWithValue("@DefenseWins", c.DefenseWins);
        cmd.Parameters.AddWithValue("@CreatedAtMs", c.CreatedAtMs);
        cmd.Parameters.AddWithValue("@Boats", c.Boats);
        cmd.Parameters.AddWithValue("@Submarines", c.Submarines);
        cmd.Parameters.AddWithValue("@Battleships", c.Battleships);
        cmd.Parameters.AddWithValue("@BattleshipDamage", c.BattleshipDamage);
        cmd.Parameters.AddWithValue("@DefenseBoats", c.DefenseBoats);
        cmd.Parameters.AddWithValue("@DefenseSubmarines", c.DefenseSubmarines);
        cmd.Parameters.AddWithValue("@BoatsFuel", c.BoatsFuel);
        cmd.Parameters.AddWithValue("@SubmarinesFuel", c.SubmarinesFuel);
        cmd.Parameters.AddWithValue("@BoatsAtSea", c.BoatsAtSea);
        cmd.Parameters.AddWithValue("@SubmarinesAtSea", c.SubmarinesAtSea);
        cmd.Parameters.AddWithValue("@BattleshipsAtSea", c.BattleshipsAtSea);
        cmd.ExecuteNonQuery();
    }

    public static Country? GetCountry(long ownerId, long chatId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = $"SELECT {COUNTRY_COLS} FROM Countries WHERE OwnerId=@id AND ChatId=@chat";
        cmd.Parameters.AddWithValue("@id", ownerId);
        cmd.Parameters.AddWithValue("@chat", chatId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return ReadCountry(reader);
    }

    public class EquipmentModel
    {
        public string ModelName { get; set; } = "";
        public long Count { get; set; }
    }

    public static List<EquipmentModel> GetEquipmentModels(long ownerId, long chatId, string category)
    {
        var result = new List<EquipmentModel>();
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT ModelName, Count FROM EquipmentModels WHERE OwnerId=@id AND ChatId=@chat AND Category=@cat AND Count>0";
        cmd.Parameters.AddWithValue("@id", ownerId);
        cmd.Parameters.AddWithValue("@chat", chatId);
        cmd.Parameters.AddWithValue("@cat", category);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new EquipmentModel { ModelName = reader.GetString(0), Count = reader.GetInt64(1) });
        }
        return result;
    }

    public static void AddEquipmentModel(long ownerId, long chatId, string category, string modelName, long amount)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"INSERT INTO EquipmentModels(OwnerId, ChatId, Category, ModelName, Count)
                             VALUES(@id, @chat, @cat, @model, MAX(0, @amt))
                             ON CONFLICT(OwnerId, ChatId, Category, ModelName)
                             DO UPDATE SET Count = MAX(0, Count + @amt);
                             DELETE FROM EquipmentModels
                             WHERE OwnerId=@id AND ChatId=@chat AND Category=@cat
                               AND ModelName=@model AND Count<=0;";
        cmd.Parameters.AddWithValue("@id", ownerId);
        cmd.Parameters.AddWithValue("@chat", chatId);
        cmd.Parameters.AddWithValue("@cat", category);
        cmd.Parameters.AddWithValue("@model", modelName);
        cmd.Parameters.AddWithValue("@amt", amount);
        cmd.ExecuteNonQuery();
    }

    public static List<(string ModelName, int DefPct)> GetDefenseModels(long ownerId, long chatId, string category)
    {
        var result = new List<(string, int)>();
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT ModelName, DefPct FROM DefenseModels WHERE OwnerId=@id AND ChatId=@chat AND Category=@cat";
        cmd.Parameters.AddWithValue("@id", ownerId);
        cmd.Parameters.AddWithValue("@chat", chatId);
        cmd.Parameters.AddWithValue("@cat", category);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add((reader.GetString(0), reader.GetInt32(1)));
        }
        return result;
    }

    public static void SetDefenseModel(long ownerId, long chatId, string category, string modelName, int defPct)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"INSERT INTO DefenseModels(OwnerId, ChatId, Category, ModelName, DefPct)
                             VALUES(@id, @chat, @cat, @model, @pct)
                             ON CONFLICT(OwnerId, ChatId, Category, ModelName)
                             DO UPDATE SET DefPct=@pct";
        cmd.Parameters.AddWithValue("@id", ownerId);
        cmd.Parameters.AddWithValue("@chat", chatId);
        cmd.Parameters.AddWithValue("@cat", category);
        cmd.Parameters.AddWithValue("@model", modelName);
        cmd.Parameters.AddWithValue("@pct", Math.Clamp(defPct, 20, 100));
        cmd.ExecuteNonQuery();
    }

    public static void ClearDefenseModels(long ownerId, long chatId, string category)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "DELETE FROM DefenseModels WHERE OwnerId=@id AND ChatId=@chat AND Category=@cat";
        cmd.Parameters.AddWithValue("@id", ownerId);
        cmd.Parameters.AddWithValue("@chat", chatId);
        cmd.Parameters.AddWithValue("@cat", category);
        cmd.ExecuteNonQuery();
    }

    public static bool IsDefenseSoldierConfigured(long ownerId,long chatId)
    {
        using var con=OpenCon();using var cmd=con.CreateCommand();
        cmd.CommandText="SELECT SoldiersConfigured FROM DefenseConfigurationFlags WHERE OwnerId=@o AND ChatId=@c";
        cmd.Parameters.AddWithValue("@o",ownerId);cmd.Parameters.AddWithValue("@c",chatId);
        return Convert.ToInt32(cmd.ExecuteScalar()??0)==1;
    }

    public static void SetDefenseSoldierConfigured(long ownerId,long chatId,bool configured=true)
    {
        using var con=OpenCon();using var cmd=con.CreateCommand();
        cmd.CommandText=@"INSERT INTO DefenseConfigurationFlags(OwnerId,ChatId,SoldiersConfigured)
 VALUES(@o,@c,@v) ON CONFLICT(OwnerId,ChatId) DO UPDATE SET SoldiersConfigured=@v";
        cmd.Parameters.AddWithValue("@o",ownerId);cmd.Parameters.AddWithValue("@c",chatId);
        cmd.Parameters.AddWithValue("@v",configured?1:0);cmd.ExecuteNonQuery();
    }

    public static Dictionary<string, long> GetDefenseModelAmounts(long ownerId, long chatId,
        string category)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"SELECT ModelName,Count FROM DefenseModelAmounts
                            WHERE OwnerId=@owner AND ChatId=@chat AND Category=@category";
        cmd.Parameters.AddWithValue("@owner", ownerId);
        cmd.Parameters.AddWithValue("@chat", chatId);
        cmd.Parameters.AddWithValue("@category", category);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) result[reader.GetString(0)] = Math.Max(0, reader.GetInt64(1));
        return result;
    }

    public static void ReplaceDefenseModelAmounts(long ownerId, long chatId, string category,
        IReadOnlyDictionary<string, long> amounts)
    {
        using var con = OpenCon();
        using var transaction = con.BeginTransaction();
        using (var delete = con.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = @"DELETE FROM DefenseModelAmounts
                                   WHERE OwnerId=@owner AND ChatId=@chat AND Category=@category";
            delete.Parameters.AddWithValue("@owner", ownerId);
            delete.Parameters.AddWithValue("@chat", chatId);
            delete.Parameters.AddWithValue("@category", category);
            delete.ExecuteNonQuery();
        }
        foreach (var item in amounts.Where(x => x.Value > 0))
        {
            using var insert = con.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = @"INSERT INTO DefenseModelAmounts
                (OwnerId,ChatId,Category,ModelName,Count)
                VALUES(@owner,@chat,@category,@model,@count)";
            insert.Parameters.AddWithValue("@owner", ownerId);
            insert.Parameters.AddWithValue("@chat", chatId);
            insert.Parameters.AddWithValue("@category", category);
            insert.Parameters.AddWithValue("@model", item.Key);
            insert.Parameters.AddWithValue("@count", item.Value);
            insert.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public static List<(string ModelName, long Count)> GetEquipmentBreakdownForReconcile(Country c, string resType)
    {
        var dict = new Dictionary<string, long>(StringComparer.Ordinal);
        if (c == null) return new List<(string, long)>();
        string category = resType switch { "tanks" => "Tanks", "planes" => "Planes", "bombers" => "Bombers", "boats" => "Boats", "submarines" => "Submarines", "battleships" => "Battleships", _ => "" };
        if (string.IsNullOrEmpty(category)) return new List<(string, long)>();
        long total = resType switch { "tanks" => c.Tanks, "planes" => c.Planes, "bombers" => c.Bombers, "boats" => c.Boats, "submarines" => c.Submarines, "battleships" => c.Battleships, _ => 0 };
        if (total <= 0) return new List<(string, long)>();
        var foreign = GetEquipmentModels(c.OwnerId, c.ChatId, category);
        long sumForeign = foreign.Sum(x => x.Count);
        long dom = Math.Max(0, total - sumForeign);
        string defaultModel = resType switch
        {
            "tanks" => GetDefaultTankModel(c.Faction),
            "planes" => GetDefaultPlaneModel(c.Faction),
            "bombers" => GetDefaultBomberModel(c.Faction),
            "boats" => GetDefaultBoatModel(c.Faction),
            "submarines" => GetDefaultSubModel(c.Faction),
            "battleships" => GetDefaultBattleshipModel(c.Faction),
            _ => ""
        };
        if (dom > 0)
        {
            if (!dict.ContainsKey(defaultModel)) dict[defaultModel] = 0;
            dict[defaultModel] += dom;
        }
        foreach (var f in foreign.Where(x => x.Count > 0))
        {
            if (!dict.ContainsKey(f.ModelName)) dict[f.ModelName] = 0;
            dict[f.ModelName] += f.Count;
        }
        var list = new List<(string ModelName, long Count)>();
        if (dict.ContainsKey(defaultModel))
        {
            list.Add((defaultModel, dict[defaultModel]));
            dict.Remove(defaultModel);
        }
        foreach (var kv in dict) list.Add((kv.Key, kv.Value));
        return list;
    }

    public static string GetDefaultTankModel(Faction f) => f switch
    {
        Faction.USSR => "T-28",
        Faction.USA => "M2 Medium",
        Faction.Reich => "Panzer III",
        _ => "تانک نامشخص"
    };

    public static string GetDefaultPlaneModel(Faction f) => f switch
    {
        Faction.USSR => "I-16",
        Faction.USA => "P-36",
        Faction.Reich => "Bf 109",
        _ => "جنگنده نامشخص"
    };

    public static string GetDefaultBomberModel(Faction f) => f switch
    {
        Faction.USSR => "DB-3",
        Faction.USA => "B-17",
        Faction.Reich => "He 111",
        _ => "بمب‌افکن نامشخص"
    };

    public static string GetDefaultBoatModel(Faction f) => f switch
    {
        Faction.USSR => "G-5",
        Faction.USA => "PT Boat",
        Faction.Reich => "S-Boot",
        _ => "قایق نامشخص"
    };

    public static string GetDefaultSubModel(Faction f) => f switch
    {
        Faction.USSR => "S-class",
        Faction.USA => "Gato",
        Faction.Reich => "Type VIIC",
        _ => "زیردریایی نامشخص"
    };

    public static string GetDefaultBattleshipModel(Faction f) => f switch
    {
        Faction.USSR => "Sovetsky Soyuz",
        Faction.USA => "Iowa",
        Faction.Reich => "Bismarck",
        _ => "نبردناو نامشخص"
    };

    public static void UpdateCountryName(long ownerId, long chatId, string newName)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "UPDATE Countries SET Name=@name WHERE OwnerId=@id AND ChatId=@chat";
        cmd.Parameters.AddWithValue("@name", newName);
        cmd.Parameters.AddWithValue("@id", ownerId);
        cmd.Parameters.AddWithValue("@chat", chatId);
        cmd.ExecuteNonQuery();
    }

    public static void UpdateCountryFlag(long ownerId, long chatId, string flagId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "UPDATE Countries SET FlagFileId=@flag WHERE OwnerId=@id AND ChatId=@chat";
        cmd.Parameters.AddWithValue("@flag", flagId);
        cmd.Parameters.AddWithValue("@id", ownerId);
        cmd.Parameters.AddWithValue("@chat", chatId);
        cmd.ExecuteNonQuery();
    }

    public static void UpdateBuildingLevel(long ownerId, long chatId, string buildingType, int newLevel, long moneyDelta)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        string levelCol = buildingType switch
        {
            "factory" => "FactoryLevel",
            "port" => "PortLevel",
            "mine" => "MineLevel",
            _ => throw new ArgumentException("invalid building type")
        };
        cmd.CommandText = $"UPDATE Countries SET {levelCol} = @level, Money = Money + @delta " +
                          "WHERE OwnerId = @id AND ChatId = @chat AND Money + @delta >= 0";
        cmd.Parameters.AddWithValue("@level", newLevel);
        cmd.Parameters.AddWithValue("@delta", moneyDelta);
        cmd.Parameters.AddWithValue("@id", ownerId);
        cmd.Parameters.AddWithValue("@chat", chatId);
        cmd.ExecuteNonQuery();
    }

    public static List<string> GetFactionFlags(string faction)
    {
        List<string> list = new();
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT FileId FROM FactionFlags WHERE Faction=@f ORDER BY Id";
        cmd.Parameters.AddWithValue("@f", faction);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(reader.GetString(0));
        return list;
    }

    public static void AddFactionFlag(string faction, string fileId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "INSERT INTO FactionFlags(Faction,FileId) VALUES(@f,@id)";
        cmd.Parameters.AddWithValue("@f", faction);
        cmd.Parameters.AddWithValue("@id", fileId);
        cmd.ExecuteNonQuery();
    }

    public static void RemoveFactionFlag(string faction, int index)
    {
        var flags = GetFactionFlags(faction);
        if (index < 0 || index >= flags.Count) return;
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "DELETE FROM FactionFlags WHERE rowid = (SELECT rowid FROM FactionFlags WHERE Faction=@f AND FileId=@id LIMIT 1)";
        cmd.Parameters.AddWithValue("@f", faction);
        cmd.Parameters.AddWithValue("@id", flags[index]);
        cmd.ExecuteNonQuery();
    }

    public static void SetSetting(string key, string value)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"INSERT INTO Settings(Key,Value) VALUES(@k,@v)
                            ON CONFLICT(Key) DO UPDATE SET Value=@v";
        cmd.Parameters.AddWithValue("@k", key);
        cmd.Parameters.AddWithValue("@v", value);
        cmd.ExecuteNonQuery();
    }

    public static string GetSetting(string key)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT Value FROM Settings WHERE Key=@k";
        cmd.Parameters.AddWithValue("@k", key);
        var result = cmd.ExecuteScalar();
        return result?.ToString() ?? "";
    }

    public static SpamRestrictionInfo? GetSpamRestriction(long userId)
    {
        using var con=OpenCon();using var cmd=con.CreateCommand();
        cmd.CommandText="SELECT UserId,ChatId,UntilMs,Level,Reason,LastFingerprint,DroppedCount,UpdatedAtMs FROM SpamRestrictions WHERE UserId=@id";
        cmd.Parameters.AddWithValue("@id",userId);using var r=cmd.ExecuteReader();if(!r.Read())return null;
        return new SpamRestrictionInfo{UserId=r.GetInt64(0),ChatId=r.GetInt64(1),UntilMs=r.GetInt64(2),Level=r.GetInt32(3),
            Reason=r.IsDBNull(4)?"":r.GetString(4),LastFingerprint=r.IsDBNull(5)?"":r.GetString(5),
            DroppedCount=r.GetInt32(6),UpdatedAtMs=r.GetInt64(7)};
    }

    public static void SaveSpamRestriction(SpamRestrictionInfo item)
    {
        using var con=OpenCon();using var cmd=con.CreateCommand();
        cmd.CommandText=@"INSERT INTO SpamRestrictions(UserId,ChatId,UntilMs,Level,Reason,LastFingerprint,DroppedCount,UpdatedAtMs)
 VALUES(@u,@c,@until,@level,@reason,@fingerprint,@dropped,@updated)
 ON CONFLICT(UserId) DO UPDATE SET ChatId=@c,UntilMs=@until,Level=@level,Reason=@reason,
 LastFingerprint=@fingerprint,DroppedCount=@dropped,UpdatedAtMs=@updated";
        cmd.Parameters.AddWithValue("@u",item.UserId);cmd.Parameters.AddWithValue("@c",item.ChatId);
        cmd.Parameters.AddWithValue("@until",item.UntilMs);cmd.Parameters.AddWithValue("@level",item.Level);
        cmd.Parameters.AddWithValue("@reason",item.Reason);cmd.Parameters.AddWithValue("@fingerprint",item.LastFingerprint);
        cmd.Parameters.AddWithValue("@dropped",item.DroppedCount);cmd.Parameters.AddWithValue("@updated",item.UpdatedAtMs);
        cmd.ExecuteNonQuery();
    }

    public static void ClearSpamRestriction(long userId)
    {
        using var con=OpenCon();using var cmd=con.CreateCommand();cmd.CommandText="DELETE FROM SpamRestrictions WHERE UserId=@id";
        cmd.Parameters.AddWithValue("@id",userId);cmd.ExecuteNonQuery();
    }

    public static List<SpamRestrictionInfo> GetSpamRestrictionReport(int limit=30)
    {
        var list=new List<SpamRestrictionInfo>();using var con=OpenCon();using var cmd=con.CreateCommand();
        cmd.CommandText="SELECT UserId,ChatId,UntilMs,Level,Reason,LastFingerprint,DroppedCount,UpdatedAtMs FROM SpamRestrictions ORDER BY UpdatedAtMs DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit",Math.Clamp(limit,1,100));using var r=cmd.ExecuteReader();
        while(r.Read())list.Add(new SpamRestrictionInfo{UserId=r.GetInt64(0),ChatId=r.GetInt64(1),UntilMs=r.GetInt64(2),Level=r.GetInt32(3),
            Reason=r.IsDBNull(4)?"":r.GetString(4),LastFingerprint=r.IsDBNull(5)?"":r.GetString(5),DroppedCount=r.GetInt32(6),UpdatedAtMs=r.GetInt64(7)});
        return list;
    }

    public static void UpdateCountryResources(long ownerId, long chatId, long money, long iron, long tanks = 0)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"UPDATE Countries SET Money=@money, Iron=@iron, Tanks=@tanks
                            WHERE OwnerId=@id AND ChatId=@chat";
        cmd.Parameters.AddWithValue("@money", money);
        cmd.Parameters.AddWithValue("@tanks", tanks);
        cmd.Parameters.AddWithValue("@iron", iron);
        cmd.Parameters.AddWithValue("@id", ownerId);
        cmd.Parameters.AddWithValue("@chat", chatId);
        cmd.ExecuteNonQuery();
    }

    public static void UpdatePlanesResources(long ownerId, long chatId, long money, long iron, long planes)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"UPDATE Countries SET Money=@money, Iron=@iron, Planes=@planes
                            WHERE OwnerId=@id AND ChatId=@chat";
        cmd.Parameters.AddWithValue("@money", money);
        cmd.Parameters.AddWithValue("@iron", iron);
        cmd.Parameters.AddWithValue("@planes", planes);
        cmd.Parameters.AddWithValue("@id", ownerId);
        cmd.Parameters.AddWithValue("@chat", chatId);
        cmd.ExecuteNonQuery();
    }

    public static void UpdateBombersResources(long ownerId, long chatId, long money, long iron, long bombers)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"UPDATE Countries SET Money=@money, Iron=@iron, Bombers=@bombers
                            WHERE OwnerId=@id AND ChatId=@chat";
        cmd.Parameters.AddWithValue("@money", money);
        cmd.Parameters.AddWithValue("@iron", iron);
        cmd.Parameters.AddWithValue("@bombers", bombers);
        cmd.Parameters.AddWithValue("@id", ownerId);
        cmd.Parameters.AddWithValue("@chat", chatId);
        cmd.ExecuteNonQuery();
    }

    public static void UpdateAntiAirResources(long ownerId, long chatId, long money, long iron, long antiair)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"UPDATE Countries SET Money=@money, Iron=@iron, AntiAir=@antiair
                            WHERE OwnerId=@id AND ChatId=@chat";
        cmd.Parameters.AddWithValue("@money", money);
        cmd.Parameters.AddWithValue("@iron", iron);
        cmd.Parameters.AddWithValue("@antiair", antiair);
        cmd.Parameters.AddWithValue("@id", ownerId);
        cmd.Parameters.AddWithValue("@chat", chatId);
        cmd.ExecuteNonQuery();
    }

    public static void UpdateCountryFull(Country c)
    {
        using var con = OpenCon();
        UpdateCountryFull(con, null, c);
    }

    public static void UpdateCountriesFullAtomically(IEnumerable<Country> countries)
    {
        using var con = OpenCon();
        using var transaction = con.BeginTransaction();
        foreach (var country in countries
            .GroupBy(x => (x.ChatId, x.OwnerId))
            .Select(x => x.Last()))
        {
            UpdateCountryFull(con, transaction, country);
        }
        transaction.Commit();
    }

    public static bool ApplyDirectBattleSettlement(
        long battleId,
        Country attacker,
        Country defender,
        IReadOnlyList<(long OwnerId, Faction Faction, string Category, Dictionary<string, long> Losses)> equipmentLosses,
        IReadOnlyList<(long ContributorId, long Tanks, long Soldiers, long Fighters, long Bombers)> deploymentLosses,
        IReadOnlyCollection<long> defensiveDeploymentIds)
    {
        using var con = OpenCon();
        using var transaction = con.BeginTransaction();
        using (var claim = con.CreateCommand())
        {
            claim.Transaction = transaction;
            claim.CommandText = "SELECT Status FROM BattleJobs WHERE BattleId=@id";
            claim.Parameters.AddWithValue("@id", battleId);
            string status = claim.ExecuteScalar()?.ToString() ?? "";
            if (status is "Settled" or "Completed")
            {
                transaction.Rollback();
                return false;
            }
        }

        foreach (var item in equipmentLosses)
            DeductEquipmentLosses(con, transaction, item.OwnerId, attacker.ChatId,
                item.Faction, item.Category, item.Losses);

        foreach (var item in deploymentLosses)
            ApplyDefensiveDeploymentLosses(con, transaction, defender.ChatId, defender.OwnerId,
                item.ContributorId, item.Tanks, item.Soldiers, item.Fighters, item.Bombers,
                defensiveDeploymentIds);

        UpdateCountryFull(con, transaction, attacker);
        UpdateCountryFull(con, transaction, defender);
        using (var settle = con.CreateCommand())
        {
            settle.Transaction = transaction;
            settle.CommandText = "UPDATE BattleJobs SET Status='Settled',UpdatedAtMs=@now WHERE BattleId=@id";
            settle.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            settle.Parameters.AddWithValue("@id", battleId);
            settle.ExecuteNonQuery();
        }
        transaction.Commit();
        return true;
    }

    private static void DeductEquipmentLosses(
        SqliteConnection con,
        SqliteTransaction transaction,
        long ownerId,
        long chatId,
        Faction faction,
        string category,
        IReadOnlyDictionary<string, long> losses)
    {
        var stored = new List<(string ModelName, long Count)>();
        using (var select = con.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = @"SELECT ModelName,Count FROM EquipmentModels
                                   WHERE OwnerId=@owner AND ChatId=@chat AND Category=@category
                                   ORDER BY ModelName";
            select.Parameters.AddWithValue("@owner", ownerId);
            select.Parameters.AddWithValue("@chat", chatId);
            select.Parameters.AddWithValue("@category", category);
            using var reader = select.ExecuteReader();
            while (reader.Read()) stored.Add((reader.GetString(0), reader.GetInt64(1)));
        }

        string Canonicalize(string model) => category switch
        {
            "Tanks" => WarEngine.CanonicalTankModel(model, faction),
            "Planes" => WarEngine.CanonicalFighterModel(model, faction),
            "Bombers" => WarEngine.CanonicalBomberModel(model, faction),
            _ => model
        };

        for (int lossIndex = 0; lossIndex < losses.Count; lossIndex++)
        {
            var loss = losses.ElementAt(lossIndex);
            long remaining = Math.Max(0, loss.Value);
            for (int rowIndex = 0; rowIndex < stored.Count && remaining > 0; rowIndex++)
            {
                var row = stored[rowIndex];
                if (!Canonicalize(row.ModelName).Equals(loss.Key, StringComparison.OrdinalIgnoreCase)) continue;
                long take = Math.Min(remaining, row.Count);
                if (take <= 0) continue;
                using var update = con.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = @"UPDATE EquipmentModels SET Count=MAX(0,Count-@take)
                                       WHERE OwnerId=@owner AND ChatId=@chat
                                         AND Category=@category AND ModelName=@model";
                update.Parameters.AddWithValue("@take", take);
                update.Parameters.AddWithValue("@owner", ownerId);
                update.Parameters.AddWithValue("@chat", chatId);
                update.Parameters.AddWithValue("@category", category);
                update.Parameters.AddWithValue("@model", row.ModelName);
                update.ExecuteNonQuery();
                stored[rowIndex] = (row.ModelName, row.Count - take);
                remaining -= take;
            }
        }
    }

    private static void UpdateCountryFull(
        SqliteConnection con,
        SqliteTransaction? transaction,
        Country c)
    {
        using var cmd = con.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"UPDATE Countries SET Money=@money, Iron=@iron, Population=@pop, Soldiers=@sol,
                            RecruitmentRate=@rr, Welfare=@wf, Tanks=@tanks, Planes=@planes, Bombers=@bombers, AntiAir=@antiair,
                            AirDefStrategy=@ads, AirDefTactic=@adt, Besieged=@bsg, Cities=@cities, DefenseWins=@dwins, TaxRate=@tax, DefTankPct=@dtp, DefSoldierPct=@dsp, DefFighterPct=@dfp,
                            Boats=@boats, Submarines=@subs, Battleships=@bships, BattleshipDamage=@bdmg, DefenseBoats=@dbboats, DefenseSubmarines=@dbsubs,
                            BoatsFuel=@bfuel, SubmarinesFuel=@sfuel, BoatsAtSea=@bsea, SubmarinesAtSea=@ssea, BattleshipsAtSea=@bssea
                            WHERE OwnerId=@id AND ChatId=@chat";
        cmd.Parameters.AddWithValue("@money", c.Money);
        cmd.Parameters.AddWithValue("@iron", c.Iron);
        cmd.Parameters.AddWithValue("@pop", c.Population);
        cmd.Parameters.AddWithValue("@sol", c.Soldiers);
        cmd.Parameters.AddWithValue("@rr", c.RecruitmentRate);
        cmd.Parameters.AddWithValue("@wf", c.Welfare);
        cmd.Parameters.AddWithValue("@tanks", c.Tanks);
        cmd.Parameters.AddWithValue("@planes", c.Planes);
        cmd.Parameters.AddWithValue("@bombers", c.Bombers);
        cmd.Parameters.AddWithValue("@antiair", c.AntiAir);
        cmd.Parameters.AddWithValue("@ads", c.AirDefStrategy);
        cmd.Parameters.AddWithValue("@adt", c.AirDefTactic);
        cmd.Parameters.AddWithValue("@bsg", c.Besieged);
        cmd.Parameters.AddWithValue("@cities", c.Cities);
        cmd.Parameters.AddWithValue("@dwins", c.DefenseWins);
        cmd.Parameters.AddWithValue("@tax", c.TaxRate);
        cmd.Parameters.AddWithValue("@dtp", c.DefTankPct);
        cmd.Parameters.AddWithValue("@dsp", c.DefSoldierPct);
        cmd.Parameters.AddWithValue("@dfp", c.DefFighterPct);
        cmd.Parameters.AddWithValue("@boats", c.Boats);
        cmd.Parameters.AddWithValue("@subs", c.Submarines);
        cmd.Parameters.AddWithValue("@bships", c.Battleships);
        cmd.Parameters.AddWithValue("@bdmg", c.BattleshipDamage);
        cmd.Parameters.AddWithValue("@dbboats", c.DefenseBoats);
        cmd.Parameters.AddWithValue("@dbsubs", c.DefenseSubmarines);
        cmd.Parameters.AddWithValue("@bfuel", c.BoatsFuel);
        cmd.Parameters.AddWithValue("@sfuel", c.SubmarinesFuel);
        cmd.Parameters.AddWithValue("@bsea", c.BoatsAtSea);
        cmd.Parameters.AddWithValue("@ssea", c.SubmarinesAtSea);
        cmd.Parameters.AddWithValue("@bssea", c.BattleshipsAtSea);
        cmd.Parameters.AddWithValue("@id", c.OwnerId);
        cmd.Parameters.AddWithValue("@chat", c.ChatId);
        cmd.ExecuteNonQuery();
    }

    public static long GetRoyalCoins(long ownerId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO RoyalCoins(OwnerId,Amount) VALUES(@id,0)";
        cmd.Parameters.AddWithValue("@id", ownerId);
        cmd.ExecuteNonQuery();
        cmd.CommandText = "SELECT Amount FROM RoyalCoins WHERE OwnerId=@id";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public static void AddRoyalCoins(long ownerId, long amount)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "INSERT INTO RoyalCoins(OwnerId,Amount) VALUES(@id,@amount) " +
                          "ON CONFLICT(OwnerId) DO UPDATE SET Amount=Amount+@amount";
        cmd.Parameters.AddWithValue("@id", ownerId);
        cmd.Parameters.AddWithValue("@amount", amount);
        cmd.ExecuteNonQuery();
    }

    public static bool TryUpgradeMineWithRoyal(long ownerId,long chatId,int expectedLevel,int targetLevel,int royalCost)
    {
        if ((targetLevel != 6 && targetLevel != 7) || targetLevel != expectedLevel + 1 || royalCost <= 0)
            return false;
        using var con=OpenCon();using var transaction=con.BeginTransaction();
        using(var debit=con.CreateCommand())
        {
            debit.Transaction=transaction;
            debit.CommandText="UPDATE RoyalCoins SET Amount=Amount-@cost WHERE OwnerId=@owner AND Amount>=@cost";
            debit.Parameters.AddWithValue("@cost",royalCost);debit.Parameters.AddWithValue("@owner",ownerId);
            if(debit.ExecuteNonQuery()!=1){transaction.Rollback();return false;}
        }
        using(var upgrade=con.CreateCommand())
        {
            upgrade.Transaction=transaction;
            upgrade.CommandText="UPDATE Countries SET MineLevel=@target WHERE OwnerId=@owner AND ChatId=@chat AND MineLevel=@expected";
            upgrade.Parameters.AddWithValue("@target",targetLevel);upgrade.Parameters.AddWithValue("@expected",expectedLevel);
            upgrade.Parameters.AddWithValue("@owner",ownerId);upgrade.Parameters.AddWithValue("@chat",chatId);
            if(upgrade.ExecuteNonQuery()!=1){transaction.Rollback();return false;}
        }
        transaction.Commit();return true;
    }

    public static void DeleteCountry(long ownerId, long chatId)
    {
        // Settle every related deployment before removing the country. Deleting the parent
        // deployment directly used to orphan allied contributors and permanently hide their forces.
        var relatedDeployments = GetActiveDeployments()
            .Where(d => d.ChatId == chatId &&
                (d.InitiatorId == ownerId || d.TargetUserId == ownerId ||
                 GetDeploymentContributors(d.Id).Any(c => c.UserId == ownerId)))
            .ToList();
        foreach (var deployment in relatedDeployments)
        {
            if (deployment.InitiatorId == ownerId || deployment.TargetUserId == ownerId)
            {
                if (!CancelDeploymentForces(deployment))
                    throw new InvalidOperationException($"Cannot delete country while deployment {deployment.Id} is unsettled.");
            }
            else if (!WithdrawDeploymentContribution(deployment.Id, ownerId, chatId,
                         out _, out _, out _, out _, out _))
                throw new InvalidOperationException($"Cannot withdraw country from deployment {deployment.Id}.");
        }

        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "DELETE FROM Countries WHERE OwnerId=@oid AND ChatId=@cid";
        cmd.Parameters.AddWithValue("@oid", ownerId);
        cmd.Parameters.AddWithValue("@cid", chatId);
        cmd.ExecuteNonQuery();

        using var delCd = con.CreateCommand();
        delCd.CommandText = "DELETE FROM LeaveCooldowns WHERE OwnerId=@oid AND ChatId=@cid";
        delCd.Parameters.AddWithValue("@oid", ownerId);
        delCd.Parameters.AddWithValue("@cid", chatId);
        delCd.ExecuteNonQuery();

        using var delShield = con.CreateCommand();
        delShield.CommandText = "DELETE FROM ShieldExemptions WHERE OwnerId=@oid AND ChatId=@cid";
        delShield.Parameters.AddWithValue("@oid", ownerId);
        delShield.Parameters.AddWithValue("@cid", chatId);
        delShield.ExecuteNonQuery();

        using var delDefeat = con.CreateCommand();
        delDefeat.CommandText = "DELETE FROM RoutDefeats WHERE ChatId=@cid AND (DefenderId=@oid OR AttackerId=@oid)";
        delDefeat.Parameters.AddWithValue("@oid", ownerId);
        delDefeat.Parameters.AddWithValue("@cid", chatId);
        delDefeat.ExecuteNonQuery();

        using var delSiege = con.CreateCommand();
        delSiege.CommandText = @"UPDATE Countries SET Besieged=0 WHERE ChatId=@cid AND OwnerId IN
 (SELECT DefenderId FROM ActiveSieges WHERE ChatId=@cid AND AttackerId=@oid);
 DELETE FROM ActiveSieges WHERE ChatId=@cid AND (DefenderId=@oid OR AttackerId=@oid);";
        delSiege.Parameters.AddWithValue("@oid", ownerId);
        delSiege.Parameters.AddWithValue("@cid", chatId);
        delSiege.ExecuteNonQuery();

        using var delAlly = con.CreateCommand();
        delAlly.CommandText = "DELETE FROM AllianceMembers WHERE AllianceId IN (SELECT Id FROM Alliances WHERE ChatId=@cid AND LeaderId=@oid); " +
                              "DELETE FROM Alliances WHERE ChatId=@cid AND LeaderId=@oid; " +
                              "DELETE FROM AllianceMembers WHERE ChatId=@cid AND UserId=@oid; " +
                              "DELETE FROM AllianceInvites WHERE ChatId=@cid AND (TargetUserId=@oid OR LeaderId=@oid); " +
                              // Transfers intentionally survive country deletion: receiver deletion returns the
                              // shipment to sender, while sender deletion still allows an already-sent shipment to arrive.
                              "SELECT 1;";
        delAlly.Parameters.AddWithValue("@cid", chatId);
        delAlly.Parameters.AddWithValue("@oid", ownerId);
        delAlly.ExecuteNonQuery();
    }

    public static void SetLeaveCooldown(long ownerId, long chatId, double hours)
    {
        long until = DateTimeOffset.UtcNow.AddHours(hours).ToUnixTimeMilliseconds();
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"INSERT INTO LeaveCooldowns(OwnerId,ChatId,UntilUnixMs) VALUES(@o,@c,@u)
                            ON CONFLICT(OwnerId,ChatId) DO UPDATE SET UntilUnixMs=@u";
        cmd.Parameters.AddWithValue("@o", ownerId);
        cmd.Parameters.AddWithValue("@c", chatId);
        cmd.Parameters.AddWithValue("@u", until);
        cmd.ExecuteNonQuery();
    }

    public static long GetLeaveCooldownRemainingMs(long ownerId, long chatId)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT UntilUnixMs FROM LeaveCooldowns WHERE OwnerId=@o AND ChatId=@c";
        cmd.Parameters.AddWithValue("@o", ownerId);
        cmd.Parameters.AddWithValue("@c", chatId);
        var res = cmd.ExecuteScalar();
        if (res == null || res == DBNull.Value) return 0;
        long until = Convert.ToInt64(res);
        if (until <= now)
        {
            using var del = con.CreateCommand();
            del.CommandText = "DELETE FROM LeaveCooldowns WHERE OwnerId=@o AND ChatId=@c";
            del.Parameters.AddWithValue("@o", ownerId);
            del.Parameters.AddWithValue("@c", chatId);
            del.ExecuteNonQuery();
            return 0;
        }
        return until - now;
    }

    public static void ClearLeaveCooldown(long ownerId, long chatId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "DELETE FROM LeaveCooldowns WHERE OwnerId=@o AND ChatId=@c";
        cmd.Parameters.AddWithValue("@o", ownerId);
        cmd.Parameters.AddWithValue("@c", chatId);
        cmd.ExecuteNonQuery();
    }

    public static void SetShieldExemption(long ownerId, long chatId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"INSERT OR IGNORE INTO ShieldExemptions(OwnerId,ChatId) VALUES(@o,@c)";
        cmd.Parameters.AddWithValue("@o", ownerId);
        cmd.Parameters.AddWithValue("@c", chatId);
        cmd.ExecuteNonQuery();
    }

    public static bool HasShieldExemption(long ownerId, long chatId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM ShieldExemptions WHERE OwnerId=@o AND ChatId=@c";
        cmd.Parameters.AddWithValue("@o", ownerId);
        cmd.Parameters.AddWithValue("@c", chatId);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    public static void ClearShieldExemption(long ownerId, long chatId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "DELETE FROM ShieldExemptions WHERE OwnerId=@o AND ChatId=@c";
        cmd.Parameters.AddWithValue("@o", ownerId);
        cmd.Parameters.AddWithValue("@c", chatId);
        cmd.ExecuteNonQuery();
    }

    // ── AttackAbandonLocks ──────────────────────────────────────────
    public static void SetAttackAbandonLock(long ownerId, long durationMs)
    {
        long until = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + durationMs;
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO AttackAbandonLocks(OwnerId,LockedUntilMs) VALUES(@o,@u)";
        cmd.Parameters.AddWithValue("@o", ownerId);
        cmd.Parameters.AddWithValue("@u", until);
        cmd.ExecuteNonQuery();
    }
    public static bool HasAttackAbandonLock(long ownerId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT LockedUntilMs FROM AttackAbandonLocks WHERE OwnerId=@o";
        cmd.Parameters.AddWithValue("@o", ownerId);
        var val = cmd.ExecuteScalar();
        if (val == null || val == DBNull.Value) return false;
        return Convert.ToInt64(val) > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
    public static long GetAttackAbandonLockUntilMs(long ownerId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT LockedUntilMs FROM AttackAbandonLocks WHERE OwnerId=@o";
        cmd.Parameters.AddWithValue("@o", ownerId);
        var val = cmd.ExecuteScalar();
        if (val == null || val == DBNull.Value) return 0;
        return Convert.ToInt64(val);
    }
    // ── DailyDefendCounts ────────────────────────────────────────────
    public static int GetDailyDefendCount(long defenderId, string date)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT Count FROM DailyDefendCounts WHERE DefenderId=@d AND AttackDate=@dt";
        cmd.Parameters.AddWithValue("@d", defenderId);
        cmd.Parameters.AddWithValue("@dt", date);
        var val = cmd.ExecuteScalar();
        return val == null || val == DBNull.Value ? 0 : Convert.ToInt32(val);
    }
    public static void IncDailyDefendCount(long defenderId, string date)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"INSERT INTO DailyDefendCounts(DefenderId,AttackDate,Count) VALUES(@d,@dt,1)
            ON CONFLICT(DefenderId,AttackDate) DO UPDATE SET Count=Count+1";
        cmd.Parameters.AddWithValue("@d", defenderId);
        cmd.Parameters.AddWithValue("@dt", date);
        cmd.ExecuteNonQuery();
    }

    // FIX(1b): AttackerFlags — ثبت حمله واقعی برای جلوگیری از فرار با حذف کشور
    public static void SetAttackerFlag(long ownerId, string date)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"INSERT OR REPLACE INTO AttackerFlags(OwnerId, AttackDate) VALUES(@o, @d)";
        cmd.Parameters.AddWithValue("@o", ownerId);
        cmd.Parameters.AddWithValue("@d", date);
        cmd.ExecuteNonQuery();
    }
    public static bool HasAttackerFlag(long ownerId, string date)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM AttackerFlags WHERE OwnerId=@o AND AttackDate=@d";
        cmd.Parameters.AddWithValue("@o", ownerId);
        cmd.Parameters.AddWithValue("@d", date);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    public static int GetRoutDefeats(long defenderId, long chatId, long attackerId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT Count FROM RoutDefeats WHERE DefenderId=@d AND ChatId=@c AND AttackerId=@a";
        cmd.Parameters.AddWithValue("@d", defenderId);
        cmd.Parameters.AddWithValue("@c", chatId);
        cmd.Parameters.AddWithValue("@a", attackerId);
        var res = cmd.ExecuteScalar();
        return (res == null || res == DBNull.Value) ? 0 : Convert.ToInt32(res);
    }

    public static int AddRoutDefeat(long defenderId, long chatId, long attackerId, int delta)
    {
        int cur = GetRoutDefeats(defenderId, chatId, attackerId);
        int nv = Math.Max(0, cur + delta);
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"INSERT INTO RoutDefeats(DefenderId,ChatId,AttackerId,Count) VALUES(@d,@c,@a,@n)
                            ON CONFLICT(DefenderId,ChatId,AttackerId) DO UPDATE SET Count=@n";
        cmd.Parameters.AddWithValue("@d", defenderId);
        cmd.Parameters.AddWithValue("@c", chatId);
        cmd.Parameters.AddWithValue("@a", attackerId);
        cmd.Parameters.AddWithValue("@n", nv);
        cmd.ExecuteNonQuery();
        return nv;
    }

    public static int MaxRoutDefeats(long defenderId, long chatId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(Count),0) FROM RoutDefeats WHERE DefenderId=@d AND ChatId=@c";
        cmd.Parameters.AddWithValue("@d", defenderId);
        cmd.Parameters.AddWithValue("@c", chatId);
        var res = cmd.ExecuteScalar();
        return (res == null || res == DBNull.Value) ? 0 : Convert.ToInt32(res);
    }

    public static List<(long AttackerId,string AttackerName,long DefenderId,string DefenderName,int Count)> GetRoutBattleProgress(long ownerId,long chatId)
    {
        var list=new List<(long AttackerId,string AttackerName,long DefenderId,string DefenderName,int Count)>();
        using var con=OpenCon();using var cmd=con.CreateCommand();
        cmd.CommandText=@"SELECT r.AttackerId,a.Name,r.DefenderId,d.Name,r.Count
 FROM RoutDefeats r
 JOIN Countries a ON a.OwnerId=r.AttackerId AND a.ChatId=r.ChatId
 JOIN Countries d ON d.OwnerId=r.DefenderId AND d.ChatId=r.ChatId
 WHERE r.ChatId=@chat AND r.Count>0 AND (r.AttackerId=@owner OR r.DefenderId=@owner)
 ORDER BY r.Count DESC,a.Name,d.Name";
        cmd.Parameters.AddWithValue("@owner",ownerId);cmd.Parameters.AddWithValue("@chat",chatId);
        using var r=cmd.ExecuteReader();while(r.Read())list.Add((r.GetInt64(0),r.GetString(1),r.GetInt64(2),r.GetString(3),r.GetInt32(4)));
        return list;
    }

    public static void SetActiveSiege(long defenderId,long chatId,long attackerId,int cities)
    {
        using var con=OpenCon();using var tx=con.BeginTransaction();
        if(cities<4)
        {
            using var siege=con.CreateCommand();siege.Transaction=tx;
            siege.CommandText=@"INSERT INTO ActiveSieges(DefenderId,ChatId,AttackerId) VALUES(@d,@c,@a)
 ON CONFLICT(DefenderId,ChatId) DO UPDATE SET AttackerId=@a";
            siege.Parameters.AddWithValue("@d",defenderId);siege.Parameters.AddWithValue("@c",chatId);siege.Parameters.AddWithValue("@a",attackerId);
            siege.ExecuteNonQuery();
        }
        else
        {
            using var clear=con.CreateCommand();clear.Transaction=tx;clear.CommandText="DELETE FROM ActiveSieges WHERE DefenderId=@d AND ChatId=@c";
            clear.Parameters.AddWithValue("@d",defenderId);clear.Parameters.AddWithValue("@c",chatId);clear.ExecuteNonQuery();
        }
        using var state=con.CreateCommand();state.Transaction=tx;
        state.CommandText="UPDATE Countries SET Besieged=@state WHERE OwnerId=@d AND ChatId=@c";
        state.Parameters.AddWithValue("@state",cities>=4?0:cities<=2?2:1);state.Parameters.AddWithValue("@d",defenderId);state.Parameters.AddWithValue("@c",chatId);
        state.ExecuteNonQuery();tx.Commit();
    }

    public static void RefreshActiveSiegeAfterCityRecovery(long defenderId,long chatId,int cities)
    {
        using var con=OpenCon();using var tx=con.BeginTransaction();
        if(cities>=4)
        {
            using var clear=con.CreateCommand();clear.Transaction=tx;clear.CommandText="DELETE FROM ActiveSieges WHERE DefenderId=@d AND ChatId=@c";
            clear.Parameters.AddWithValue("@d",defenderId);clear.Parameters.AddWithValue("@c",chatId);clear.ExecuteNonQuery();
        }
        using var state=con.CreateCommand();state.Transaction=tx;
        state.CommandText=@"UPDATE Countries SET Besieged=CASE
 WHEN @cities>=4 THEN 0
 WHEN EXISTS(SELECT 1 FROM ActiveSieges s WHERE s.DefenderId=@d AND s.ChatId=@c) THEN CASE WHEN @cities<=2 THEN 2 ELSE 1 END
 ELSE 0 END WHERE OwnerId=@d AND ChatId=@c";
        state.Parameters.AddWithValue("@cities",cities);state.Parameters.AddWithValue("@d",defenderId);state.Parameters.AddWithValue("@c",chatId);
        state.ExecuteNonQuery();tx.Commit();
    }

    public static string RepairSiegeIntegrity()
    {
        using var con=OpenCon();using var tx=con.BeginTransaction();
        int migrated,progressRemoved,activeRemoved,statesFixed;
        using(var migrate=con.CreateCommand())
        {
            migrate.Transaction=tx;migrate.CommandText=@"INSERT OR IGNORE INTO ActiveSieges(DefenderId,ChatId,AttackerId)
 SELECT c.OwnerId,c.ChatId,
   (SELECT w.AttackerId FROM WarBattles w
    WHERE w.DefenderId=c.OwnerId AND w.ChatId=c.ChatId AND w.Winner='AttackerHeavyVictory' AND w.AttackerId IS NOT NULL
    ORDER BY w.Id DESC LIMIT 1)
 FROM Countries c
 WHERE c.Besieged>0 AND c.Cities<4
   AND EXISTS(SELECT 1 FROM WarBattles w WHERE w.DefenderId=c.OwnerId AND w.ChatId=c.ChatId
              AND w.Winner='AttackerHeavyVictory' AND w.AttackerId IS NOT NULL)";
            migrated=migrate.ExecuteNonQuery();
        }
        using(var cleanProgress=con.CreateCommand())
        {
            cleanProgress.Transaction=tx;cleanProgress.CommandText=@"DELETE FROM RoutDefeats
 WHERE Count<=0
 OR NOT EXISTS(SELECT 1 FROM Countries a WHERE a.OwnerId=RoutDefeats.AttackerId AND a.ChatId=RoutDefeats.ChatId)
 OR NOT EXISTS(SELECT 1 FROM Countries d WHERE d.OwnerId=RoutDefeats.DefenderId AND d.ChatId=RoutDefeats.ChatId)
 OR EXISTS(SELECT 1 FROM AllianceMembers x JOIN AllianceMembers y
    ON y.AllianceId=x.AllianceId AND y.ChatId=x.ChatId
    WHERE x.ChatId=RoutDefeats.ChatId AND x.UserId=RoutDefeats.AttackerId AND y.UserId=RoutDefeats.DefenderId)";
            progressRemoved=cleanProgress.ExecuteNonQuery();
        }
        using(var cleanActive=con.CreateCommand())
        {
            cleanActive.Transaction=tx;cleanActive.CommandText=@"DELETE FROM ActiveSieges
 WHERE NOT EXISTS(SELECT 1 FROM Countries a WHERE a.OwnerId=ActiveSieges.AttackerId AND a.ChatId=ActiveSieges.ChatId)
 OR NOT EXISTS(SELECT 1 FROM Countries d WHERE d.OwnerId=ActiveSieges.DefenderId AND d.ChatId=ActiveSieges.ChatId)
 OR EXISTS(SELECT 1 FROM Countries d WHERE d.OwnerId=ActiveSieges.DefenderId AND d.ChatId=ActiveSieges.ChatId AND d.Cities>=4)
 OR EXISTS(SELECT 1 FROM AllianceMembers x JOIN AllianceMembers y
    ON y.AllianceId=x.AllianceId AND y.ChatId=x.ChatId
    WHERE x.ChatId=ActiveSieges.ChatId AND x.UserId=ActiveSieges.AttackerId AND y.UserId=ActiveSieges.DefenderId)";
            activeRemoved=cleanActive.ExecuteNonQuery();
        }
        using(var fix=con.CreateCommand())
        {
            fix.Transaction=tx;fix.CommandText=@"UPDATE Countries SET Besieged=CASE
 WHEN Cities>=4 THEN 0
 WHEN EXISTS(SELECT 1 FROM ActiveSieges s WHERE s.DefenderId=Countries.OwnerId AND s.ChatId=Countries.ChatId)
   THEN CASE WHEN Cities<=2 THEN 2 ELSE 1 END
 ELSE 0 END
 WHERE Besieged<>CASE
 WHEN Cities>=4 THEN 0
 WHEN EXISTS(SELECT 1 FROM ActiveSieges s WHERE s.DefenderId=Countries.OwnerId AND s.ChatId=Countries.ChatId)
   THEN CASE WHEN Cities<=2 THEN 2 ELSE 1 END
 ELSE 0 END";
            statesFixed=fix.ExecuteNonQuery();
        }
        tx.Commit();return $"migrated={migrated}, progressRemoved={progressRemoved}, activeRemoved={activeRemoved}, statesFixed={statesFixed}";
    }

    public static int IncrementHeavyOffensiveWins(long ownerId, long chatId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"INSERT INTO HeavyOffensiveWins(OwnerId,ChatId,Count)
                            VALUES(@owner,@chat,1)
                            ON CONFLICT(OwnerId,ChatId) DO UPDATE SET Count=Count+1;
                            SELECT Count FROM HeavyOffensiveWins WHERE OwnerId=@owner AND ChatId=@chat";
        cmd.Parameters.AddWithValue("@owner", ownerId);
        cmd.Parameters.AddWithValue("@chat", chatId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public static void ResetHeavyOffensiveWins(long ownerId, long chatId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "DELETE FROM HeavyOffensiveWins WHERE OwnerId=@owner AND ChatId=@chat";
        cmd.Parameters.AddWithValue("@owner", ownerId);
        cmd.Parameters.AddWithValue("@chat", chatId);
        cmd.ExecuteNonQuery();
    }

    public static PersistedBattleJob EnsureBattleJob(long battleId, string jobType,
        string requestJson, string contextJson)
    {
        using var con = OpenCon();
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        using (var insert = con.CreateCommand())
        {
            insert.CommandText = @"INSERT OR IGNORE INTO BattleJobs
                (BattleId,JobType,RequestJson,ContextJson,Status,CreatedAtMs,UpdatedAtMs)
                VALUES(@id,@type,@request,@context,'Pending',@now,@now)";
            insert.Parameters.AddWithValue("@id", battleId);
            insert.Parameters.AddWithValue("@type", jobType);
            insert.Parameters.AddWithValue("@request", requestJson);
            insert.Parameters.AddWithValue("@context", contextJson);
            insert.Parameters.AddWithValue("@now", now);
            insert.ExecuteNonQuery();
        }
        using var select = con.CreateCommand();
        select.CommandText = @"SELECT BattleId,JobType,RequestJson,ContextJson,Status,ResultJson,LastError
                               FROM BattleJobs WHERE BattleId=@id";
        select.Parameters.AddWithValue("@id", battleId);
        using var reader = select.ExecuteReader();
        if (!reader.Read()) throw new InvalidOperationException("Battle job could not be persisted.");
        return new PersistedBattleJob
        {
            BattleId = reader.GetInt64(0), JobType = reader.GetString(1),
            RequestJson = reader.GetString(2), ContextJson = reader.GetString(3),
            Status = reader.GetString(4), ResultJson = reader.GetString(5), LastError = reader.GetString(6)
        };
    }

    public static List<PersistedBattleJob> GetRecoverableBattleJobs()
    {
        var jobs = new List<PersistedBattleJob>();
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"SELECT BattleId,JobType,RequestJson,ContextJson,Status,ResultJson,LastError
                            FROM BattleJobs WHERE Status IN ('Pending','Running','Resolved','Settled')
                            ORDER BY CreatedAtMs";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            jobs.Add(new PersistedBattleJob
            {
                BattleId = reader.GetInt64(0), JobType = reader.GetString(1),
                RequestJson = reader.GetString(2), ContextJson = reader.GetString(3),
                Status = reader.GetString(4), ResultJson = reader.GetString(5), LastError = reader.GetString(6)
            });
        return jobs;
    }

    public static void UpdateBattleJob(long battleId, string status,
        string resultJson = "", string error = "")
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"UPDATE BattleJobs SET Status=@status,
                              ResultJson=CASE WHEN @result='' THEN ResultJson ELSE @result END,
                              LastError=@error, UpdatedAtMs=@now WHERE BattleId=@id";
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@result", resultJson);
        cmd.Parameters.AddWithValue("@error", error);
        cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("@id", battleId);
        cmd.ExecuteNonQuery();
    }

    public static void SaveBattleResult(BattleRequest request, BattleResult result)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"INSERT INTO WarBattles
            (Timestamp,ChatId,AttackerId,DefenderId,AttackerName,DefenderName,Winner,
             PenetrationKm,SuccessPercent,AtkTankLoss,AtkSoldierLoss,DefTankLoss,DefSoldierLoss,
             LootMoney,LootIron,DurationMinutes,Report)
            VALUES(@time,@chat,@attacker,@defender,@an,@dn,@winner,@depth,@success,
                   @atl,@asl,@dtl,@dsl,@money,@iron,@duration,@report)";
        var attacker = request.Attackers.First();
        var defender = request.Defenders.First();
        cmd.Parameters.AddWithValue("@time", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@chat", request.ChatId);
        cmd.Parameters.AddWithValue("@attacker", attacker.OwnerId);
        cmd.Parameters.AddWithValue("@defender", defender.OwnerId);
        cmd.Parameters.AddWithValue("@an", attacker.CountryName);
        cmd.Parameters.AddWithValue("@dn", defender.CountryName);
        cmd.Parameters.AddWithValue("@winner", result.OutcomeKind.ToString());
        cmd.Parameters.AddWithValue("@depth", result.EffectiveAdvanceKm);
        cmd.Parameters.AddWithValue("@success", result.SuccessPercent);
        cmd.Parameters.AddWithValue("@atl", result.AttackerTanksLost);
        cmd.Parameters.AddWithValue("@asl", result.AttackerSoldiersLost);
        cmd.Parameters.AddWithValue("@dtl", result.DefenderTanksLost);
        cmd.Parameters.AddWithValue("@dsl", result.DefenderSoldiersLost);
        cmd.Parameters.AddWithValue("@money", result.AttackerMoneyGained);
        cmd.Parameters.AddWithValue("@iron", result.AttackerIronGained);
        cmd.Parameters.AddWithValue("@duration", result.DurationMinutes);
        cmd.Parameters.AddWithValue("@report", result.AttackerReport);
        cmd.ExecuteNonQuery();
    }

    public static void SetBesieged(long ownerId, long chatId, int state)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "UPDATE Countries SET Besieged=@b WHERE OwnerId=@o AND ChatId=@c";
        cmd.Parameters.AddWithValue("@b", state);
        cmd.Parameters.AddWithValue("@o", ownerId);
        cmd.Parameters.AddWithValue("@c", chatId);
        cmd.ExecuteNonQuery();
    }

    public static void SetCities(long ownerId, long chatId, int cities)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "UPDATE Countries SET Cities=@n WHERE OwnerId=@o AND ChatId=@c";
        cmd.Parameters.AddWithValue("@n", cities);
        cmd.Parameters.AddWithValue("@o", ownerId);
        cmd.Parameters.AddWithValue("@c", chatId);
        cmd.ExecuteNonQuery();
    }

    public static void SetDefenseWins(long ownerId, long chatId, int wins)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "UPDATE Countries SET DefenseWins=@n WHERE OwnerId=@o AND ChatId=@c";
        cmd.Parameters.AddWithValue("@n", wins);
        cmd.Parameters.AddWithValue("@o", ownerId);
        cmd.Parameters.AddWithValue("@c", chatId);
        cmd.ExecuteNonQuery();
    }

    public const int MAX_CITIES = 20;
    public static bool AddCityToAttacker(long ownerId, long chatId)
    {
        var c = GetCountry(ownerId, chatId);
        if (c == null) return false;
        if (c.Cities >= MAX_CITIES) return false;
        int cities=c.Cities+1;
        SetCities(ownerId, chatId, cities);
        RefreshActiveSiegeAfterCityRecovery(ownerId,chatId,cities);
        return true;
    }

    public static List<Country> GetAllCountries()
    {
        List<Country> list = new();
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = $"SELECT {COUNTRY_COLS} FROM Countries";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(ReadCountry(reader));
        return list;
    }

    public static void UpdateDefense(long ownerId, long chatId, long defenseTanks, long defenseSoldiers, int strategy = 1, int tactic = 1)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"UPDATE Countries SET DefenseTanks=@dt, DefenseSoldiers=@ds, DefenseStrategy=@dstr, DefenseTactic=@dtac
                            WHERE OwnerId=@oid AND ChatId=@cid";
        cmd.Parameters.AddWithValue("@dt", defenseTanks);
        cmd.Parameters.AddWithValue("@ds", defenseSoldiers);
        cmd.Parameters.AddWithValue("@dstr", strategy);
        cmd.Parameters.AddWithValue("@dtac", tactic);
        cmd.Parameters.AddWithValue("@oid", ownerId);
        cmd.Parameters.AddWithValue("@cid", chatId);
        cmd.ExecuteNonQuery();
    }

    public static void UpdateDefenseFull(long ownerId, long chatId, long defenseTanks, long defenseSoldiers, long defenseFighters, int strategy = 1, int tactic = 1, int defTankPct = 100, int defSoldierPct = 100, int defFighterPct = 100)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"UPDATE Countries SET DefenseTanks=@dt, DefenseSoldiers=@ds, DefenseFighters=@df, DefenseStrategy=@dstr, DefenseTactic=@dtac, DefTankPct=@dtp, DefSoldierPct=@dsp, DefFighterPct=@dfp
                            WHERE OwnerId=@oid AND ChatId=@cid";
        cmd.Parameters.AddWithValue("@dt", defenseTanks);
        cmd.Parameters.AddWithValue("@ds", defenseSoldiers);
        cmd.Parameters.AddWithValue("@df", defenseFighters);
        cmd.Parameters.AddWithValue("@dstr", strategy);
        cmd.Parameters.AddWithValue("@dtac", tactic);
        cmd.Parameters.AddWithValue("@dtp", defTankPct);
        cmd.Parameters.AddWithValue("@dsp", defSoldierPct);
        cmd.Parameters.AddWithValue("@dfp", defFighterPct);
        cmd.Parameters.AddWithValue("@oid", ownerId);
        cmd.Parameters.AddWithValue("@cid", chatId);
        cmd.ExecuteNonQuery();
    }

    public static void ReconcileDefense(long ownerId, long chatId)
    {
        var c = GetCountry(ownerId, chatId);
        if (c == null) return;

        // Exact per-model defense. A compulsory 20% reserve is repaired with the
        // country's factory model first and foreign equipment only for the remainder.
        long ExactDefenseTotal(string category, string resourceType)
        {
            var breakdown = GetEquipmentBreakdownForReconcile(c, resourceType);
            long total = breakdown.Sum(x => x.Count);
            if (total <= 0) return 0;
            long mandatory = (long)Math.Ceiling(total * 0.20);
            var saved = GetDefenseModelAmounts(ownerId, chatId, category);
            long selected = breakdown.Sum(x => Math.Min(x.Count, saved.GetValueOrDefault(x.ModelName)));
            // Legacy aggregate defense values defaulted to 100% and are not proof of an
            // explicit choice. Without an exact per-model record, reserve only mandatory 20%.
            if (saved.Count == 0) selected = mandatory;
            return Math.Clamp(selected, mandatory, total);
        }

        long dt = ExactDefenseTotal("Tanks", "tanks");
        long df = ExactDefenseTotal("Planes", "planes");
        int effectiveSoldierPct=IsDefenseSoldierConfigured(ownerId,chatId)
            ? Math.Clamp(c.DefSoldierPct,20,100) : 20;
        long ds=(long)Math.Ceiling(c.Soldiers*(effectiveSoldierPct/100.0));

        long minTanks = (long)Math.Ceiling(c.Tanks * 0.2);
        long minSoldiers = (long)Math.Ceiling(c.Soldiers * 0.2);
        long minFighters = (long)Math.Ceiling(c.Planes * 0.2);
        long minBoats = (long)Math.Ceiling(c.Boats * 0.2);
        long minSubs = (long)Math.Ceiling(c.Submarines * 0.2);
        dt = Math.Clamp(dt, minTanks, c.Tanks);
        ds = Math.Clamp(ds, minSoldiers, c.Soldiers);
        df = Math.Clamp(df, minFighters, c.Planes);

        //  – naval defense per-model
        long db = 0, dsb = 0;
        var boatDefModels = GetDefenseModels(ownerId, chatId, "Boats");
        if (boatDefModels.Count > 0)
        {
            var breakdown = GetEquipmentBreakdownForReconcile(c, "boats");
            foreach (var (model, count) in breakdown)
            {
                int pct = 100;
                var dm = boatDefModels.FirstOrDefault(x => x.ModelName == model);
                if (dm != default) pct = dm.DefPct;
                else if (c.DefTankPct > 0) pct = c.DefTankPct;
                db += (long)Math.Ceiling(count * Math.Clamp(pct, 20, 100) / 100.0);
            }
        }
        else
        {
            db = c.DefenseBoats > 0 ? c.DefenseBoats : minBoats;
        }
        var subDefModels = GetDefenseModels(ownerId, chatId, "Submarines");
        if (subDefModels.Count > 0)
        {
            var breakdown = GetEquipmentBreakdownForReconcile(c, "submarines");
            foreach (var (model, count) in breakdown)
            {
                int pct = 100;
                var dm = subDefModels.FirstOrDefault(x => x.ModelName == model);
                if (dm != default) pct = dm.DefPct;
                else if (c.DefTankPct > 0) pct = c.DefTankPct;
                dsb += (long)Math.Ceiling(count * Math.Clamp(pct, 20, 100) / 100.0);
            }
        }
        else
        {
            dsb = c.DefenseSubmarines > 0 ? c.DefenseSubmarines : minSubs;
        }
        db = Math.Clamp(db, minBoats, c.Boats);
        dsb = Math.Clamp(dsb, minSubs, c.Submarines);

        bool needUpdate = dt != c.DefenseTanks || ds != c.DefenseSoldiers || df != c.DefenseFighters ||
            db != c.DefenseBoats || dsb != c.DefenseSubmarines || c.DefTankPct == 0 ||
            c.DefSoldierPct != effectiveSoldierPct;
        if (needUpdate)
        {
            c.DefenseTanks = dt;
            c.DefenseSoldiers = ds;
            c.DefenseFighters = df;
            c.DefenseBoats = db;
            c.DefenseSubmarines = dsb;
            if (c.DefTankPct == 0) c.DefTankPct = 100;
            c.DefSoldierPct = effectiveSoldierPct;
            if (c.DefFighterPct == 0) c.DefFighterPct = 100;
            UpdateDefenseFull(ownerId, chatId, dt, ds, df, c.DefenseStrategy, c.DefenseTactic, c.DefTankPct, c.DefSoldierPct, c.DefFighterPct);
            // Also update naval defence via full update
            using var con2 = OpenCon();
            using var cmd2 = con2.CreateCommand();
            cmd2.CommandText = "UPDATE Countries SET DefenseBoats=@db, DefenseSubmarines=@dsb WHERE OwnerId=@oid AND ChatId=@cid";
            cmd2.Parameters.AddWithValue("@db", db);
            cmd2.Parameters.AddWithValue("@dsb", dsb);
            cmd2.Parameters.AddWithValue("@oid", ownerId);
            cmd2.Parameters.AddWithValue("@cid", chatId);
            cmd2.ExecuteNonQuery();
        }
    }

    public static void EnsureMinDefense(long ownerId, long chatId) => ReconcileDefense(ownerId, chatId);

    public static List<Country> GetCountriesByChatId(long chatId)
    {
        var list = new List<Country>();
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = $"SELECT {COUNTRY_COLS} FROM Countries WHERE ChatId=@cid";
        cmd.Parameters.AddWithValue("@cid", chatId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) list.Add(ReadCountry(reader));
        return list;
    }

    public static void SetBotGroupActive(long chatId,bool active)
    {
        if(chatId>=0)return;
        using var con=OpenCon();using var cmd=con.CreateCommand();
        cmd.CommandText=@"INSERT INTO BotGroupStatus(ChatId,IsActive,UpdatedAtMs) VALUES(@chat,@active,@now)
 ON CONFLICT(ChatId) DO UPDATE SET IsActive=@active,UpdatedAtMs=@now
 WHERE BotGroupStatus.IsActive!=excluded.IsActive";
        cmd.Parameters.AddWithValue("@chat",chatId);cmd.Parameters.AddWithValue("@active",active?1:0);
        cmd.Parameters.AddWithValue("@now",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());cmd.ExecuteNonQuery();
    }

    public static bool IsBotGroupActive(long chatId)
    {
        if(chatId>=0)return true;
        using var con=OpenCon();using var cmd=con.CreateCommand();
        cmd.CommandText="SELECT IsActive FROM BotGroupStatus WHERE ChatId=@chat";cmd.Parameters.AddWithValue("@chat",chatId);
        object? value=cmd.ExecuteScalar();return value==null||value==DBNull.Value||Convert.ToInt32(value)==1;
    }

    public static List<long> GetUserActiveChatIds(long ownerId) =>
        GetUserChatIds(ownerId).Where(IsBotGroupActive).ToList();

    public static List<long> GetUserChatIds(long ownerId)
    {
        var list = new List<long>();
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT ChatId FROM Countries WHERE OwnerId=@oid";
        cmd.Parameters.AddWithValue("@oid", ownerId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetInt64(0));
        return list;
    }

    public static List<Country> GetAttackableTargets(long chatId, long attackerId)
    {
        var list = new List<Country>();
        using var con = OpenCon();

        // Get alliance id in same connection (optimized, no extra OpenCon)
        long aid = 0;
        using (var cmdAid = con.CreateCommand())
        {
            cmdAid.CommandText = "SELECT AllianceId FROM AllianceMembers WHERE ChatId=@cid AND UserId=@uid LIMIT 1";
            cmdAid.Parameters.AddWithValue("@cid", chatId);
            cmdAid.Parameters.AddWithValue("@uid", attackerId);
            var v = cmdAid.ExecuteScalar();
            if (v != null && v != DBNull.Value) aid = Convert.ToInt64(v);
        }

        using var cmd = con.CreateCommand();
        if (aid != 0)
        {
            // Optimized single query: exclude allies directly in SQL, no HashSet needed
            cmd.CommandText = $"SELECT {COUNTRY_COLS} FROM Countries WHERE ChatId=@cid AND OwnerId!=@attacker AND OwnerId NOT IN (SELECT UserId FROM AllianceMembers WHERE AllianceId=@aid)";
            cmd.Parameters.AddWithValue("@cid", chatId);
            cmd.Parameters.AddWithValue("@attacker", attackerId);
            cmd.Parameters.AddWithValue("@aid", aid);
        }
        else
        {
            cmd.CommandText = $"SELECT {COUNTRY_COLS} FROM Countries WHERE ChatId=@cid AND OwnerId!=@attacker";
            cmd.Parameters.AddWithValue("@cid", chatId);
            cmd.Parameters.AddWithValue("@attacker", attackerId);
        }

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(ReadCountry(reader));
        }
        return list;
    }

    public static List<Alliance> GetAlliancesByChatId(long chatId)
    {
        var list = new List<Alliance>();
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT Id, ChatId, Name, FlagFileId, LeaderId, CreatedAtMs FROM Alliances WHERE ChatId=@cid";
        cmd.Parameters.AddWithValue("@cid", chatId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new Alliance
            {
                Id = r.GetInt64(0),
                ChatId = r.GetInt64(1),
                Name = r.GetString(2),
                FlagFileId = r.IsDBNull(3) ? "" : r.GetString(3),
                LeaderId = r.GetInt64(4),
                CreatedAtMs = r.GetInt64(5)
            });
        }
        return list;
    }

    public static Alliance? GetAllianceById(long allianceId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT Id, ChatId, Name, FlagFileId, LeaderId, CreatedAtMs FROM Alliances WHERE Id=@id";
        cmd.Parameters.AddWithValue("@id", allianceId);
        using var r = cmd.ExecuteReader();
        if (r.Read())
        {
            return new Alliance
            {
                Id = r.GetInt64(0),
                ChatId = r.GetInt64(1),
                Name = r.GetString(2),
                FlagFileId = r.IsDBNull(3) ? "" : r.GetString(3),
                LeaderId = r.GetInt64(4),
                CreatedAtMs = r.GetInt64(5)
            };
        }
        return null;
    }

    public static bool AllianceNameExists(long chatId, string name)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM Alliances WHERE ChatId=@cid AND LOWER(Name)=LOWER(@name) LIMIT 1";
        cmd.Parameters.AddWithValue("@cid", chatId);
        cmd.Parameters.AddWithValue("@name", name.Trim());
        using var r = cmd.ExecuteReader();
        return r.Read();
    }

    public static long GetUserAllianceId(long chatId, long userId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT AllianceId FROM AllianceMembers WHERE ChatId=@cid AND UserId=@uid LIMIT 1";
        cmd.Parameters.AddWithValue("@cid", chatId);
        cmd.Parameters.AddWithValue("@uid", userId);
        var val = cmd.ExecuteScalar();
        return val != null && val != DBNull.Value ? Convert.ToInt64(val) : 0;
    }

    public static List<long> GetAllianceMembers(long allianceId)
    {
        var list = new List<long>();
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT UserId FROM AllianceMembers WHERE AllianceId=@aid";
        cmd.Parameters.AddWithValue("@aid", allianceId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetInt64(0));
        return list;
    }

    public static long AddAlliance(Alliance a)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "INSERT INTO Alliances(ChatId, Name, FlagFileId, LeaderId, CreatedAtMs) VALUES(@cid, @name, @flag, @lid, @ms); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@cid", a.ChatId);
        cmd.Parameters.AddWithValue("@name", a.Name);
        cmd.Parameters.AddWithValue("@flag", a.FlagFileId);
        cmd.Parameters.AddWithValue("@lid", a.LeaderId);
        cmd.Parameters.AddWithValue("@ms", a.CreatedAtMs);
        long aid = Convert.ToInt64(cmd.ExecuteScalar());
        using var cmd2 = con.CreateCommand();
        cmd2.CommandText = "INSERT OR REPLACE INTO AllianceMembers(AllianceId, ChatId, UserId) VALUES(@aid, @cid, @uid)";
        cmd2.Parameters.AddWithValue("@aid", aid);
        cmd2.Parameters.AddWithValue("@cid", a.ChatId);
        cmd2.Parameters.AddWithValue("@uid", a.LeaderId);
        cmd2.ExecuteNonQuery();
        return aid;
    }

    public static void AddAllianceMember(long allianceId, long chatId, long userId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO AllianceMembers(AllianceId, ChatId, UserId) VALUES(@aid, @cid, @uid)";
        cmd.Parameters.AddWithValue("@aid", allianceId);
        cmd.Parameters.AddWithValue("@cid", chatId);
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.ExecuteNonQuery();
    }

    public static void RemoveAllianceMember(long allianceId, long chatId, long userId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "DELETE FROM AllianceMembers WHERE ChatId=@cid AND UserId=@uid";
        cmd.Parameters.AddWithValue("@cid", chatId);
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.ExecuteNonQuery();
    }

    public static void DeleteAlliance(long allianceId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "DELETE FROM AllianceMembers WHERE AllianceId=@aid; DELETE FROM Alliances WHERE Id=@aid; DELETE FROM AllianceInvites WHERE AllianceId=@aid;";
        cmd.Parameters.AddWithValue("@aid", allianceId);
        cmd.ExecuteNonQuery();
    }

    public static long AddAllianceInvite(AllianceInvite inv)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "INSERT INTO AllianceInvites(AllianceId, ChatId, TargetUserId, LeaderId, CreatedAtMs) VALUES(@aid, @cid, @tuid, @lid, @ms); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@aid", inv.AllianceId);
        cmd.Parameters.AddWithValue("@cid", inv.ChatId);
        cmd.Parameters.AddWithValue("@tuid", inv.TargetUserId);
        cmd.Parameters.AddWithValue("@lid", inv.LeaderId);
        cmd.Parameters.AddWithValue("@ms", inv.CreatedAtMs);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public static AllianceInvite? GetAllianceInvite(long inviteId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT Id, AllianceId, ChatId, TargetUserId, LeaderId, CreatedAtMs FROM AllianceInvites WHERE Id=@id";
        cmd.Parameters.AddWithValue("@id", inviteId);
        using var r = cmd.ExecuteReader();
        if (r.Read())
        {
            return new AllianceInvite
            {
                Id = r.GetInt64(0),
                AllianceId = r.GetInt64(1),
                ChatId = r.GetInt64(2),
                TargetUserId = r.GetInt64(3),
                LeaderId = r.GetInt64(4),
                CreatedAtMs = r.GetInt64(5)
            };
        }
        return null;
    }

    public static void DeleteAllianceInvite(long inviteId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "DELETE FROM AllianceInvites WHERE Id=@id";
        cmd.Parameters.AddWithValue("@id", inviteId);
        cmd.ExecuteNonQuery();
    }

    public static void DeleteUserInvites(long chatId, long targetUserId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "DELETE FROM AllianceInvites WHERE ChatId=@cid AND TargetUserId=@uid";
        cmd.Parameters.AddWithValue("@cid", chatId);
        cmd.Parameters.AddWithValue("@uid", targetUserId);
        cmd.ExecuteNonQuery();
    }

    public static long AddTransfer(Transfer t)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"INSERT INTO Transfers(ChatId, AllianceId, SenderId, ReceiverId, ResourceType, ModelName, Amount, ArriveAtMs, Notified)
                            VALUES(@cid, @aid, @sid, @rid, @res, @model, @amt, @ms, @notif); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@cid", t.ChatId);
        cmd.Parameters.AddWithValue("@aid", t.AllianceId);
        cmd.Parameters.AddWithValue("@sid", t.SenderId);
        cmd.Parameters.AddWithValue("@rid", t.ReceiverId);
        cmd.Parameters.AddWithValue("@res", t.ResourceType);
        cmd.Parameters.AddWithValue("@model", t.ModelName ?? "");
        cmd.Parameters.AddWithValue("@amt", t.Amount);
        cmd.Parameters.AddWithValue("@ms", t.ArriveAtMs);
        cmd.Parameters.AddWithValue("@notif", t.Notified);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public static (long Boats,long Submarines,long Battleships) GetOutgoingNavalTransfers(long ownerId,long chatId)
    {
        using var con=OpenCon();using var cmd=con.CreateCommand();
        cmd.CommandText=@"SELECT
 COALESCE(SUM(CASE WHEN ResourceType='boats' THEN Amount ELSE 0 END),0),
 COALESCE(SUM(CASE WHEN ResourceType='submarines' THEN Amount ELSE 0 END),0),
 COALESCE(SUM(CASE WHEN ResourceType='battleships' THEN Amount ELSE 0 END),0)
 FROM Transfers WHERE SenderId=@owner AND ChatId=@chat";
        cmd.Parameters.AddWithValue("@owner",ownerId);cmd.Parameters.AddWithValue("@chat",chatId);
        using var reader=cmd.ExecuteReader();reader.Read();return(reader.GetInt64(0),reader.GetInt64(1),reader.GetInt64(2));
    }

    public static long GetOutgoingTransferAmount(long ownerId,long chatId,string resourceType)
    {
        var naval=GetOutgoingNavalTransfers(ownerId,chatId);
        return resourceType switch{"boats"=>naval.Boats,"submarines"=>naval.Submarines,"battleships"=>naval.Battleships,_=>0};
    }

    public static long GetBattleshipCapacityUsed(long ownerId, long chatId, bool includeIncoming = true)
    {
        using var con = OpenCon();
        long owned;
        using (var country = con.CreateCommand())
        {
            country.CommandText = "SELECT Battleships+BattleshipsAtSea FROM Countries WHERE OwnerId=@o AND ChatId=@c";
            country.Parameters.AddWithValue("@o", ownerId); country.Parameters.AddWithValue("@c", chatId);
            owned = Convert.ToInt64(country.ExecuteScalar() ?? 0);
        }
        using var outgoing = con.CreateCommand();
        outgoing.CommandText = @"SELECT COALESCE(SUM(Amount),0) FROM Transfers
                                 WHERE SenderId=@o AND ChatId=@c AND ResourceType='battleships'";
        outgoing.Parameters.AddWithValue("@o", ownerId); outgoing.Parameters.AddWithValue("@c", chatId);
        owned += Convert.ToInt64(outgoing.ExecuteScalar() ?? 0);
        if (!includeIncoming) return owned;
        using var incoming = con.CreateCommand();
        incoming.CommandText = @"SELECT COALESCE(SUM(Amount),0) FROM Transfers
                                 WHERE ReceiverId=@o AND ChatId=@c AND ResourceType='battleships'";
        incoming.Parameters.AddWithValue("@o", ownerId); incoming.Parameters.AddWithValue("@c", chatId);
        return owned + Convert.ToInt64(incoming.ExecuteScalar() ?? 0);
    }

    public static bool TryCreateTransfers(
        long senderId,
        long chatId,
        long allianceId,
        long receiverId,
        string resourceType,
        IReadOnlyList<(string ModelName, long Amount)> shipments,
        long arriveAtMs)
    {
        string resourceColumn = resourceType switch
        {
            "money" => "Money",
            "iron" => "Iron",
            "soldiers" => "Soldiers",
            "tanks" => "Tanks",
            "planes" => "Planes",
            "bombers" => "Bombers",
            "boats" => "Boats",
            "submarines" => "Submarines",
            "battleships" => "Battleships",
            _ => throw new ArgumentException("Invalid transfer resource type.", nameof(resourceType))
        };
        string equipmentCategory = resourceType switch
        {
            "tanks" => "Tanks",
            "planes" => "Planes",
            "bombers" => "Bombers",
            "boats" => "Boats",
            "submarines" => "Submarines",
            "battleships" => "Battleships",
            _ => ""
        };

        var validShipments = shipments
            .Where(x => x.Amount > 0)
            .ToList();
        if (validShipments.Count == 0)
            return false;

        long totalAmount = checked(validShipments.Sum(x => x.Amount));
        using var con = OpenCon();
        using var transaction = con.BeginTransaction();

        if (resourceType == "battleships")
        {
            long receiverOwned;
            using (var capacity = con.CreateCommand())
            {
                capacity.Transaction = transaction;
                capacity.CommandText = @"SELECT Battleships+BattleshipsAtSea+
                    COALESCE((SELECT SUM(Amount) FROM Transfers
                              WHERE ReceiverId=@receiver AND ChatId=@chat AND ResourceType='battleships'),0)+
                    COALESCE((SELECT SUM(Amount) FROM Transfers
                              WHERE SenderId=@receiver AND ChatId=@chat AND ResourceType='battleships'),0)
                    FROM Countries WHERE OwnerId=@receiver AND ChatId=@chat";
                capacity.Parameters.AddWithValue("@receiver", receiverId);
                capacity.Parameters.AddWithValue("@chat", chatId);
                object? value = capacity.ExecuteScalar();
                if (value == null || value == DBNull.Value) { transaction.Rollback(); return false; }
                receiverOwned = Convert.ToInt64(value);
            }
            if (receiverOwned + totalAmount > 3) { transaction.Rollback(); return false; }
            foreach (var shipment in validShipments)
            {
                using var units = con.CreateCommand();
                units.Transaction = transaction;
                units.CommandText = @"SELECT COUNT(*) FROM BattleshipUnits
                    WHERE OwnerId=@owner AND ChatId=@chat AND ModelName=@model
                      AND Status='Ready' AND OperationId IS NULL";
                units.Parameters.AddWithValue("@owner", senderId);
                units.Parameters.AddWithValue("@chat", chatId);
                units.Parameters.AddWithValue("@model", shipment.ModelName);
                if (Convert.ToInt64(units.ExecuteScalar()) < shipment.Amount)
                { transaction.Rollback(); return false; }
            }
        }

        using (var deduct = con.CreateCommand())
        {
            deduct.Transaction = transaction;
            deduct.CommandText = $@"UPDATE Countries
                                    SET {resourceColumn}={resourceColumn}-@amount
                                    WHERE OwnerId=@owner AND ChatId=@chat
                                      AND {resourceColumn}>=@amount";
            deduct.Parameters.AddWithValue("@amount", totalAmount);
            deduct.Parameters.AddWithValue("@owner", senderId);
            deduct.Parameters.AddWithValue("@chat", chatId);
            if (deduct.ExecuteNonQuery() != 1)
            {
                transaction.Rollback();
                return false;
            }
        }

        if (!string.IsNullOrEmpty(equipmentCategory))
        {
            foreach (var shipment in validShipments.Where(x => !string.IsNullOrWhiteSpace(x.ModelName)))
            {
                using var model = con.CreateCommand();
                model.Transaction = transaction;
                model.CommandText = @"UPDATE EquipmentModels
                                      SET Count=MAX(0, Count-@amount)
                                      WHERE OwnerId=@owner AND ChatId=@chat
                                        AND Category=@category AND ModelName=@model;
                                      DELETE FROM EquipmentModels
                                      WHERE OwnerId=@owner AND ChatId=@chat
                                        AND Category=@category AND ModelName=@model
                                        AND Count<=0;";
                model.Parameters.AddWithValue("@amount", shipment.Amount);
                model.Parameters.AddWithValue("@owner", senderId);
                model.Parameters.AddWithValue("@chat", chatId);
                model.Parameters.AddWithValue("@category", equipmentCategory);
                model.Parameters.AddWithValue("@model", shipment.ModelName);
                model.ExecuteNonQuery();
            }
        }

        foreach (var shipment in validShipments)
        {
            using var insert = con.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = @"INSERT INTO Transfers
                (ChatId,AllianceId,SenderId,ReceiverId,ResourceType,ModelName,Amount,ArriveAtMs,Notified)
                VALUES(@chat,@alliance,@sender,@receiver,@resource,@model,@amount,@arrive,0);
                SELECT last_insert_rowid();";
            insert.Parameters.AddWithValue("@chat", chatId);
            insert.Parameters.AddWithValue("@alliance", allianceId);
            insert.Parameters.AddWithValue("@sender", senderId);
            insert.Parameters.AddWithValue("@receiver", receiverId);
            insert.Parameters.AddWithValue("@resource", resourceType);
            insert.Parameters.AddWithValue("@model", shipment.ModelName ?? "");
            insert.Parameters.AddWithValue("@amount", shipment.Amount);
            insert.Parameters.AddWithValue("@arrive", arriveAtMs);
            long transferId = Convert.ToInt64(insert.ExecuteScalar());

            if (resourceType == "battleships")
            {
                var unitIds = new List<long>();
                using (var selectUnits = con.CreateCommand())
                {
                    selectUnits.Transaction = transaction;
                    selectUnits.CommandText = @"SELECT Id FROM BattleshipUnits
                        WHERE OwnerId=@owner AND ChatId=@chat AND ModelName=@model
                          AND Status='Ready' AND OperationId IS NULL
                        ORDER BY DamagePercent,Id LIMIT @amount";
                    selectUnits.Parameters.AddWithValue("@owner", senderId);
                    selectUnits.Parameters.AddWithValue("@chat", chatId);
                    selectUnits.Parameters.AddWithValue("@model", shipment.ModelName);
                    selectUnits.Parameters.AddWithValue("@amount", shipment.Amount);
                    using var reader = selectUnits.ExecuteReader();
                    while (reader.Read()) unitIds.Add(reader.GetInt64(0));
                }
                if (unitIds.Count != shipment.Amount) { transaction.Rollback(); return false; }
                foreach (long unitId in unitIds)
                {
                    using var mark = con.CreateCommand();
                    mark.Transaction = transaction;
                    mark.CommandText = "UPDATE BattleshipUnits SET Status='Transfer' WHERE Id=@id AND Status='Ready'";
                    mark.Parameters.AddWithValue("@id", unitId);
                    if (mark.ExecuteNonQuery() != 1) { transaction.Rollback(); return false; }
                    using var link = con.CreateCommand();
                    link.Transaction = transaction;
                    link.CommandText = "INSERT INTO TransferBattleships(TransferId,UnitId) VALUES(@transfer,@unit)";
                    link.Parameters.AddWithValue("@transfer", transferId);
                    link.Parameters.AddWithValue("@unit", unitId);
                    link.ExecuteNonQuery();
                }
            }
        }

        transaction.Commit();
        return true;
    }

    public static List<Transfer> GetActiveTransfers()
    {
        var list = new List<Transfer>();
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT Id, ChatId, AllianceId, SenderId, ReceiverId, ResourceType, ModelName, Amount, ArriveAtMs, Notified FROM Transfers";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            // Handle both old schema (no ModelName) and new
            string modelName = "";
            long amount;
            long arrive;
            int notified;
            if (r.FieldCount >= 10)
            {
                modelName = r.IsDBNull(6) ? "" : r.GetString(6);
                amount = r.GetInt64(7);
                arrive = r.GetInt64(8);
                notified = r.GetInt32(9);
            }
            else
            {
                // fallback for old DB without ModelName column during transition
                amount = r.GetInt64(6);
                arrive = r.GetInt64(7);
                notified = r.GetInt32(8);
            }
            list.Add(new Transfer
            {
                Id = r.GetInt64(0),
                ChatId = r.GetInt64(1),
                AllianceId = r.GetInt64(2),
                SenderId = r.GetInt64(3),
                ReceiverId = r.GetInt64(4),
                ResourceType = r.GetString(5),
                ModelName = modelName,
                Amount = amount,
                ArriveAtMs = arrive,
                Notified = notified
            });
        }
        return list;
    }

    public static void UpdateTransferNotified(long id, int notified = 1)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "UPDATE Transfers SET Notified=@n WHERE Id=@id";
        cmd.Parameters.AddWithValue("@n", notified);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public static bool DeleteTransfer(long id)
    {
        using var con = OpenCon();
        using var transaction = con.BeginTransaction();
        long sender,chat,amount; string resource,model;
        using (var select = con.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT SenderId,ChatId,ResourceType,ModelName,Amount FROM Transfers WHERE Id=@id";
            select.Parameters.AddWithValue("@id", id);
            using var reader=select.ExecuteReader(); if(!reader.Read()) return false;
            sender=reader.GetInt64(0);chat=reader.GetInt64(1);resource=reader.GetString(2);
            model=reader.IsDBNull(3)?"":reader.GetString(3);amount=reader.GetInt64(4);
        }
        string column=resource switch {"money"=>"Money","iron"=>"Iron","soldiers"=>"Soldiers","tanks"=>"Tanks",
            "planes"=>"Planes","bombers"=>"Bombers","boats"=>"Boats","submarines"=>"Submarines","battleships"=>"Battleships",
            _=>throw new InvalidOperationException("Unknown transfer resource.")};
        using(var restore=con.CreateCommand())
        {
            restore.Transaction=transaction;restore.CommandText=$"UPDATE Countries SET {column}={column}+@amount WHERE OwnerId=@owner AND ChatId=@chat";
            restore.Parameters.AddWithValue("@amount",amount);restore.Parameters.AddWithValue("@owner",sender);restore.Parameters.AddWithValue("@chat",chat);
            if(restore.ExecuteNonQuery()!=1){transaction.Rollback();return false;}
        }
        string category=resource switch{"tanks"=>"Tanks","planes"=>"Planes","bombers"=>"Bombers","boats"=>"Boats",
            "submarines"=>"Submarines","battleships"=>"Battleships",_=>""};
        if(category!=""&&!string.IsNullOrWhiteSpace(model))
        {
            using var restoreModel=con.CreateCommand();restoreModel.Transaction=transaction;
            restoreModel.CommandText=@"INSERT INTO EquipmentModels(OwnerId,ChatId,Category,ModelName,Count)
VALUES(@o,@c,@cat,@m,@n) ON CONFLICT(OwnerId,ChatId,Category,ModelName) DO UPDATE SET Count=Count+@n";
            restoreModel.Parameters.AddWithValue("@o",sender);restoreModel.Parameters.AddWithValue("@c",chat);
            restoreModel.Parameters.AddWithValue("@cat",category);restoreModel.Parameters.AddWithValue("@m",model);
            restoreModel.Parameters.AddWithValue("@n",amount);restoreModel.ExecuteNonQuery();
        }
        if(resource=="battleships")
        {
            using var units=con.CreateCommand();units.Transaction=transaction;
            units.CommandText="UPDATE BattleshipUnits SET Status='Ready',OperationId=NULL WHERE Id IN (SELECT UnitId FROM TransferBattleships WHERE TransferId=@id)";
            units.Parameters.AddWithValue("@id",id);units.ExecuteNonQuery();
            using var links=con.CreateCommand();links.Transaction=transaction;links.CommandText="DELETE FROM TransferBattleships WHERE TransferId=@id";
            links.Parameters.AddWithValue("@id",id);links.ExecuteNonQuery();
            UpdateLegacyBattleshipDamage(con,transaction,sender,chat);
        }
        using(var delete=con.CreateCommand()){delete.Transaction=transaction;delete.CommandText="DELETE FROM Transfers WHERE Id=@id";
            delete.Parameters.AddWithValue("@id",id);if(delete.ExecuteNonQuery()!=1){transaction.Rollback();return false;}}
        transaction.Commit();return true;
    }

    public static string CompleteTransfer(Transfer transfer, string resolvedModelName)
    {
        string resourceColumn = transfer.ResourceType switch
        {
            "money" => "Money",
            "iron" => "Iron",
            "soldiers" => "Soldiers",
            "tanks" => "Tanks",
            "planes" => "Planes",
            "bombers" => "Bombers",
            "boats" => "Boats",
            "submarines" => "Submarines",
            "battleships" => "Battleships",
            _ => throw new ArgumentException("Invalid transfer resource type.")
        };
        string equipmentCategory = transfer.ResourceType switch
        {
            "tanks" => "Tanks",
            "planes" => "Planes",
            "bombers" => "Bombers",
            "boats" => "Boats",
            "submarines" => "Submarines",
            "battleships" => "Battleships",
            _ => ""
        };

        using var con = OpenCon();
        using var transaction = con.BeginTransaction();

        bool receiverExists;
        long receiverBattleships = 0;
        using (var receiver = con.CreateCommand())
        {
            receiver.Transaction = transaction;
            receiver.CommandText = @"SELECT Battleships+BattleshipsAtSea+
                COALESCE((SELECT SUM(Amount) FROM Transfers WHERE SenderId=@owner AND ChatId=@chat AND ResourceType='battleships'),0)
                FROM Countries WHERE OwnerId=@owner AND ChatId=@chat";
            receiver.Parameters.AddWithValue("@owner", transfer.ReceiverId);
            receiver.Parameters.AddWithValue("@chat", transfer.ChatId);
            object? value = receiver.ExecuteScalar();
            receiverExists = value != null && value != DBNull.Value;
            if (receiverExists)
                receiverBattleships = Convert.ToInt64(value);
        }

        bool capacityReturn = transfer.ResourceType == "battleships" &&
                              receiverExists &&
                              receiverBattleships + transfer.Amount > 3;
        long recipientId = receiverExists && !capacityReturn
            ? transfer.ReceiverId
            : transfer.SenderId;
        string outcome = receiverExists && !capacityReturn
            ? "delivered"
            : capacityReturn ? "capacity" : "returned";

        int credited;
        using (var credit = con.CreateCommand())
        {
            credit.Transaction = transaction;
            credit.CommandText = $@"UPDATE Countries
                                    SET {resourceColumn}={resourceColumn}+@amount
                                    WHERE OwnerId=@owner AND ChatId=@chat";
            credit.Parameters.AddWithValue("@amount", transfer.Amount);
            credit.Parameters.AddWithValue("@owner", recipientId);
            credit.Parameters.AddWithValue("@chat", transfer.ChatId);
            credited = credit.ExecuteNonQuery();
        }

        if (credited == 1 &&
            !string.IsNullOrEmpty(equipmentCategory) &&
            !string.IsNullOrWhiteSpace(resolvedModelName))
        {
            using var model = con.CreateCommand();
            model.Transaction = transaction;
            model.CommandText = @"INSERT INTO EquipmentModels
                (OwnerId,ChatId,Category,ModelName,Count)
                VALUES(@owner,@chat,@category,@model,@amount)
                ON CONFLICT(OwnerId,ChatId,Category,ModelName)
                DO UPDATE SET Count=MAX(0,Count+@amount)";
            model.Parameters.AddWithValue("@owner", recipientId);
            model.Parameters.AddWithValue("@chat", transfer.ChatId);
            model.Parameters.AddWithValue("@category", equipmentCategory);
            model.Parameters.AddWithValue("@model", resolvedModelName);
            model.Parameters.AddWithValue("@amount", transfer.Amount);
            model.ExecuteNonQuery();
        }

        if (transfer.ResourceType == "battleships")
        {
            var transferUnits=new List<long>();
            using(var readUnits=con.CreateCommand())
            {
                readUnits.Transaction=transaction;readUnits.CommandText="SELECT UnitId FROM TransferBattleships WHERE TransferId=@transfer ORDER BY UnitId";
                readUnits.Parameters.AddWithValue("@transfer",transfer.Id);using var reader=readUnits.ExecuteReader();
                while(reader.Read())transferUnits.Add(reader.GetInt64(0));
            }
            foreach(long unitId in transferUnits)
            {
                int slot;
                if(recipientId==transfer.SenderId)
                {
                    using var oldSlot=con.CreateCommand();oldSlot.Transaction=transaction;
                    oldSlot.CommandText="SELECT SlotNumber FROM BattleshipUnits WHERE Id=@id";oldSlot.Parameters.AddWithValue("@id",unitId);
                    slot=Convert.ToInt32(oldSlot.ExecuteScalar()??0);
                }
                else
                {
                    using(var releaseSlot=con.CreateCommand()){releaseSlot.Transaction=transaction;releaseSlot.CommandText="UPDATE BattleshipUnits SET SlotNumber=0 WHERE Id=@id";releaseSlot.Parameters.AddWithValue("@id",unitId);releaseSlot.ExecuteNonQuery();}
                    using var freeSlot=con.CreateCommand();freeSlot.Transaction=transaction;
                    freeSlot.CommandText=@"SELECT n FROM (SELECT 1 n UNION ALL SELECT 2 UNION ALL SELECT 3)
 WHERE NOT EXISTS(SELECT 1 FROM BattleshipUnits b WHERE b.OwnerId=@owner AND b.ChatId=@chat AND b.SlotNumber=n AND b.Status!='Sunk') ORDER BY n LIMIT 1";
                    freeSlot.Parameters.AddWithValue("@owner",recipientId);freeSlot.Parameters.AddWithValue("@chat",transfer.ChatId);
                    object? value=freeSlot.ExecuteScalar();if(value==null||value==DBNull.Value){transaction.Rollback();return "capacity";}slot=Convert.ToInt32(value);
                }
                using var moveUnit=con.CreateCommand();moveUnit.Transaction=transaction;
                moveUnit.CommandText=@"UPDATE BattleshipUnits SET OwnerId=@owner,ChatId=@chat,SlotNumber=@slot,Status='Ready',OperationId=NULL WHERE Id=@id";
                moveUnit.Parameters.AddWithValue("@owner",recipientId);moveUnit.Parameters.AddWithValue("@chat",transfer.ChatId);
                moveUnit.Parameters.AddWithValue("@slot",slot);moveUnit.Parameters.AddWithValue("@id",unitId);
                if(moveUnit.ExecuteNonQuery()!=1){transaction.Rollback();return "dropped";}
            }
            using var deleteLinks = con.CreateCommand();
            deleteLinks.Transaction = transaction;
            deleteLinks.CommandText = "DELETE FROM TransferBattleships WHERE TransferId=@transfer";
            deleteLinks.Parameters.AddWithValue("@transfer", transfer.Id);
            deleteLinks.ExecuteNonQuery();
            UpdateLegacyBattleshipDamage(con,transaction,transfer.SenderId,transfer.ChatId);
            UpdateLegacyBattleshipDamage(con,transaction,recipientId,transfer.ChatId);
        }

        using (var delete = con.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM Transfers WHERE Id=@id";
            delete.Parameters.AddWithValue("@id", transfer.Id);
            if (delete.ExecuteNonQuery() != 1)
            {
                transaction.Rollback();
                return "already-processed";
            }
        }

        transaction.Commit();
        return credited == 1 ? outcome : "dropped";
    }

    public static long AddDeployment(Deployment d)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"INSERT INTO Deployments(ChatId, AllianceId, InitiatorId, TargetUserId, Type, DurationHours, FormationType, Strategy, Tactic, Tanks, Soldiers, Fighters, Bombers, CreatedAtMs, EndAtMs, LastWarnMs, AnnounceMsgId)
                            VALUES(@cid, @aid, @iid, @tid, @type, @dur, @form, @str, @tac, @tnk, @sol, @fig, @bom, @cms, @ems, @lms, @amid); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@cid", d.ChatId);
        cmd.Parameters.AddWithValue("@aid", d.AllianceId);
        cmd.Parameters.AddWithValue("@iid", d.InitiatorId);
        cmd.Parameters.AddWithValue("@tid", d.TargetUserId);
        cmd.Parameters.AddWithValue("@type", d.Type);
        cmd.Parameters.AddWithValue("@dur", d.DurationHours);
        cmd.Parameters.AddWithValue("@form", d.FormationType);
        cmd.Parameters.AddWithValue("@str", d.Strategy);
        cmd.Parameters.AddWithValue("@tac", d.Tactic);
        cmd.Parameters.AddWithValue("@tnk", d.Tanks);
        cmd.Parameters.AddWithValue("@sol", d.Soldiers);
        cmd.Parameters.AddWithValue("@fig", d.Fighters);
        cmd.Parameters.AddWithValue("@bom", d.Bombers);
        cmd.Parameters.AddWithValue("@cms", d.CreatedAtMs);
        cmd.Parameters.AddWithValue("@ems", d.EndAtMs);
        cmd.Parameters.AddWithValue("@lms", d.LastWarnMs);
        cmd.Parameters.AddWithValue("@amid", d.AnnounceMsgId);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static long[] AllocateExact(long requested, long[] capacities)
    {
        long capacity = capacities.Sum();
        long target = Math.Min(Math.Max(0, requested), capacity);
        var result = new long[capacities.Length];
        if (target == 0 || capacity == 0) return result;
        var fractions = new decimal[capacities.Length];
        long assigned = 0;
        for (int i = 0; i < capacities.Length; i++)
        {
            decimal exact = (decimal)target * capacities[i] / capacity;
            result[i] = Math.Min(capacities[i], (long)decimal.Floor(exact));
            fractions[i] = exact - result[i];
            assigned += result[i];
        }
        foreach (int i in Enumerable.Range(0, capacities.Length)
                     .OrderByDescending(i => fractions[i]).ThenBy(i => i))
        {
            if (assigned >= target) break;
            if (result[i] >= capacities[i]) continue;
            result[i]++;
            assigned++;
        }
        return result;
    }

    private static void ReserveContributorModels(SqliteConnection con, SqliteTransaction transaction,
        long deploymentId, long userId, long chatId, string category, long amount,
        IReadOnlyDictionary<string, long>? selectedModels = null)
    {
        if (amount <= 0) return;
        Faction faction;
        long availableAggregate;
        using (var country = con.CreateCommand())
        {
            country.Transaction = transaction;
            string column = category switch { "Tanks" => "Tanks", "Planes" => "Planes", _ => "Bombers" };
            country.CommandText = $"SELECT Faction,{column} FROM Countries WHERE OwnerId=@owner AND ChatId=@chat";
            country.Parameters.AddWithValue("@owner", userId);
            country.Parameters.AddWithValue("@chat", chatId);
            using var reader = country.ExecuteReader();
            if (!reader.Read()) throw new InvalidOperationException("Deployment contributor country is missing.");
            faction = (Faction)reader.GetInt32(0);
            availableAggregate = reader.GetInt64(1) + amount;
        }

        var reserved = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = con.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = @"SELECT ModelName,SUM(Count) FROM DeploymentContributorModels dcm
                                JOIN Deployments d ON d.Id=dcm.DeploymentId
                                WHERE d.ChatId=@chat AND dcm.UserId=@owner AND dcm.Category=@category
                                GROUP BY ModelName";
            cmd.Parameters.AddWithValue("@chat", chatId);
            cmd.Parameters.AddWithValue("@owner", userId);
            cmd.Parameters.AddWithValue("@category", category);
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) reserved[reader.GetString(0)] = reader.GetInt64(1);
        }

        var capacities = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = con.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = @"SELECT ModelName,Count FROM EquipmentModels
                                WHERE OwnerId=@owner AND ChatId=@chat AND Category=@category
                                ORDER BY ModelName";
            cmd.Parameters.AddWithValue("@owner", userId);
            cmd.Parameters.AddWithValue("@chat", chatId);
            cmd.Parameters.AddWithValue("@category", category);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string model = reader.GetString(0);
                capacities[model] = Math.Max(0, reader.GetInt64(1) - reserved.GetValueOrDefault(model));
            }
        }

        string defaultModel = category switch
        {
            "Tanks" => GetDefaultTankModel(faction),
            "Planes" => GetDefaultPlaneModel(faction),
            _ => GetDefaultBomberModel(faction)
        };
        long explicitAvailable = capacities.Values.Sum();
        if (explicitAvailable < availableAggregate)
            capacities[defaultModel] = capacities.GetValueOrDefault(defaultModel) +
                                       (availableAggregate - explicitAvailable);

        List<KeyValuePair<string, long>> selected;
        if (selectedModels != null)
        {
            selected = selectedModels
                .Where(x => x.Value > 0)
                .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Select(x => new KeyValuePair<string, long>(x.Key, x.Sum(y => y.Value)))
                .ToList();
            if (selected.Sum(x => x.Value) != amount)
                throw new InvalidOperationException($"Selected {category} models do not match deployment total.");
            foreach (var item in selected)
                if (item.Value > capacities.GetValueOrDefault(item.Key))
                    throw new InvalidOperationException($"Insufficient {item.Key} available for deployment.");
        }
        else
        {
            var models = capacities.Where(x => x.Value > 0)
                .OrderBy(x => x.Key, StringComparer.Ordinal).ToList();
            if (models.Count == 0) models.Add(new KeyValuePair<string, long>(defaultModel, amount));
            long[] normalized = AllocateExact(Math.Min(availableAggregate, models.Sum(x => x.Value)),
                models.Select(x => x.Value).ToArray());
            long[] allocated = AllocateExact(amount, normalized);
            selected = models.Select((x, i) => new KeyValuePair<string, long>(x.Key, allocated[i]))
                .Where(x => x.Value > 0).ToList();
        }

        foreach (var item in selected)
        {
            using var insert = con.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = @"INSERT INTO DeploymentContributorModels
                (DeploymentId,UserId,Category,ModelName,Count) VALUES(@deployment,@user,@category,@model,@count)
                ON CONFLICT(DeploymentId,UserId,Category,ModelName) DO UPDATE SET Count=Count+@count";
            insert.Parameters.AddWithValue("@deployment", deploymentId);
            insert.Parameters.AddWithValue("@user", userId);
            insert.Parameters.AddWithValue("@category", category);
            insert.Parameters.AddWithValue("@model", item.Key);
            insert.Parameters.AddWithValue("@count", item.Value);
            insert.ExecuteNonQuery();
        }
    }

    public static Dictionary<string, long> GetReservedEquipmentModels(long ownerId, long chatId, string category)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"SELECT dcm.ModelName,SUM(dcm.Count) FROM DeploymentContributorModels dcm
                            JOIN Deployments d ON d.Id=dcm.DeploymentId
                            WHERE d.ChatId=@chat AND dcm.UserId=@owner AND dcm.Category=@category
                            GROUP BY dcm.ModelName";
        cmd.Parameters.AddWithValue("@chat", chatId);
        cmd.Parameters.AddWithValue("@owner", ownerId);
        cmd.Parameters.AddWithValue("@category", category);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) result[reader.GetString(0)] = reader.GetInt64(1);
        return result;
    }

    public static List<ModelAmount> GetDeploymentContributorModels(long deploymentId, long userId,
        string category)
    {
        var result = new List<ModelAmount>();
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"SELECT ModelName,Count FROM DeploymentContributorModels
                            WHERE DeploymentId=@deployment AND UserId=@user AND Category=@category
                            ORDER BY ModelName";
        cmd.Parameters.AddWithValue("@deployment", deploymentId);
        cmd.Parameters.AddWithValue("@user", userId);
        cmd.Parameters.AddWithValue("@category", category);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) result.Add(new ModelAmount(reader.GetString(0), reader.GetInt64(1)));
        return result;
    }

    private static void ReduceContributorModels(SqliteConnection con, SqliteTransaction transaction,
        long deploymentId, long userId, string category, long loss)
    {
        if (loss <= 0) return;
        var rows = new List<(string Model, long Count)>();
        using (var select = con.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = @"SELECT ModelName,Count FROM DeploymentContributorModels
                                   WHERE DeploymentId=@deployment AND UserId=@user AND Category=@category
                                   ORDER BY ModelName";
            select.Parameters.AddWithValue("@deployment", deploymentId);
            select.Parameters.AddWithValue("@user", userId);
            select.Parameters.AddWithValue("@category", category);
            using var reader = select.ExecuteReader();
            while (reader.Read()) rows.Add((reader.GetString(0), reader.GetInt64(1)));
        }
        long[] reductions = AllocateExact(loss, rows.Select(x => x.Count).ToArray());
        for (int i = 0; i < rows.Count; i++)
        {
            if (reductions[i] <= 0) continue;
            using var update = con.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = @"UPDATE DeploymentContributorModels SET Count=MAX(0,Count-@loss)
                                   WHERE DeploymentId=@deployment AND UserId=@user
                                     AND Category=@category AND ModelName=@model";
            update.Parameters.AddWithValue("@loss", reductions[i]);
            update.Parameters.AddWithValue("@deployment", deploymentId);
            update.Parameters.AddWithValue("@user", userId);
            update.Parameters.AddWithValue("@category", category);
            update.Parameters.AddWithValue("@model", rows[i].Model);
            update.ExecuteNonQuery();
        }
    }

    public static long TryCreateDeploymentWithForces(Deployment deployment,
        IReadOnlyDictionary<string, long>? tankModels = null,
        IReadOnlyDictionary<string, long>? fighterModels = null,
        IReadOnlyDictionary<string, long>? bomberModels = null)
    {
        if (deployment.Tanks < 0 || deployment.Soldiers < 0 ||
            deployment.Fighters < 0 || deployment.Bombers < 0) return 0;
        using var con = OpenCon();
        using var transaction = con.BeginTransaction();

        using (var deduct = con.CreateCommand())
        {
            deduct.Transaction = transaction;
            deduct.CommandText = @"UPDATE Countries SET
                                      Tanks=Tanks-@tanks,
                                      Soldiers=Soldiers-@soldiers,
                                      Planes=Planes-@fighters,
                                      Bombers=Bombers-@bombers
                                    WHERE OwnerId=@owner AND ChatId=@chat
                                      AND Tanks>=@tanks AND Soldiers>=@soldiers
                                      AND Planes>=@fighters AND Bombers>=@bombers";
            deduct.Parameters.AddWithValue("@tanks", deployment.Tanks);
            deduct.Parameters.AddWithValue("@soldiers", deployment.Soldiers);
            deduct.Parameters.AddWithValue("@fighters", deployment.Fighters);
            deduct.Parameters.AddWithValue("@bombers", deployment.Bombers);
            deduct.Parameters.AddWithValue("@owner", deployment.InitiatorId);
            deduct.Parameters.AddWithValue("@chat", deployment.ChatId);
            if (deduct.ExecuteNonQuery() != 1)
            {
                transaction.Rollback();
                return 0;
            }
        }

        long deploymentId;
        using (var insert = con.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = @"INSERT INTO Deployments
                (ChatId,AllianceId,InitiatorId,TargetUserId,Type,DurationHours,FormationType,
                 Strategy,Tactic,Tanks,Soldiers,Fighters,Bombers,CreatedAtMs,EndAtMs,LastWarnMs,AnnounceMsgId)
                VALUES(@chat,@alliance,@initiator,@target,@type,@duration,@formation,
                       @strategy,@tactic,@tanks,@soldiers,@fighters,@bombers,@created,@ends,@warn,@announce);
                SELECT last_insert_rowid();";
            insert.Parameters.AddWithValue("@chat", deployment.ChatId);
            insert.Parameters.AddWithValue("@alliance", deployment.AllianceId);
            insert.Parameters.AddWithValue("@initiator", deployment.InitiatorId);
            insert.Parameters.AddWithValue("@target", deployment.TargetUserId);
            insert.Parameters.AddWithValue("@type", deployment.Type);
            insert.Parameters.AddWithValue("@duration", deployment.DurationHours);
            insert.Parameters.AddWithValue("@formation", deployment.FormationType);
            insert.Parameters.AddWithValue("@strategy", deployment.Strategy);
            insert.Parameters.AddWithValue("@tactic", deployment.Tactic);
            insert.Parameters.AddWithValue("@tanks", deployment.Tanks);
            insert.Parameters.AddWithValue("@soldiers", deployment.Soldiers);
            insert.Parameters.AddWithValue("@fighters", deployment.Fighters);
            insert.Parameters.AddWithValue("@bombers", deployment.Bombers);
            insert.Parameters.AddWithValue("@created", deployment.CreatedAtMs);
            insert.Parameters.AddWithValue("@ends", deployment.EndAtMs);
            insert.Parameters.AddWithValue("@warn", deployment.LastWarnMs);
            insert.Parameters.AddWithValue("@announce", deployment.AnnounceMsgId);
            deploymentId = Convert.ToInt64(insert.ExecuteScalar());
        }

        using (var contributor = con.CreateCommand())
        {
            contributor.Transaction = transaction;
            contributor.CommandText = @"INSERT INTO DeploymentContributors
                (DeploymentId,UserId,ChatId,Tanks,Soldiers,Fighters,Bombers,Strategy,Tactic)
                VALUES(@deployment,@user,@chat,@tanks,@soldiers,@fighters,@bombers,@strategy,@tactic)";
            contributor.Parameters.AddWithValue("@deployment", deploymentId);
            contributor.Parameters.AddWithValue("@user", deployment.InitiatorId);
            contributor.Parameters.AddWithValue("@chat", deployment.ChatId);
            contributor.Parameters.AddWithValue("@tanks", deployment.Tanks);
            contributor.Parameters.AddWithValue("@soldiers", deployment.Soldiers);
            contributor.Parameters.AddWithValue("@fighters", deployment.Fighters);
            contributor.Parameters.AddWithValue("@bombers", deployment.Bombers);
            contributor.Parameters.AddWithValue("@strategy", deployment.Strategy);
            contributor.Parameters.AddWithValue("@tactic", deployment.Tactic);
            contributor.ExecuteNonQuery();
        }
        ReserveContributorModels(con, transaction, deploymentId, deployment.InitiatorId,
            deployment.ChatId, "Tanks", deployment.Tanks, tankModels);
        ReserveContributorModels(con, transaction, deploymentId, deployment.InitiatorId,
            deployment.ChatId, "Planes", deployment.Fighters, fighterModels);
        ReserveContributorModels(con, transaction, deploymentId, deployment.InitiatorId,
            deployment.ChatId, "Bombers", deployment.Bombers, bomberModels);

        transaction.Commit();
        return deploymentId;
    }

    public static bool TryJoinDeploymentWithForces(
        long deploymentId,
        DeploymentContributor contributor,
        long chatId,
        long nowMs,
        IReadOnlyDictionary<string, long>? tankModels = null,
        IReadOnlyDictionary<string, long>? fighterModels = null,
        IReadOnlyDictionary<string, long>? bomberModels = null)
    {
        if (contributor.Tanks < 0 || contributor.Soldiers < 0 ||
            contributor.Fighters < 0 || contributor.Bombers < 0) return false;
        using var con = OpenCon();
        using var transaction = con.BeginTransaction();

        using (var deduct = con.CreateCommand())
        {
            deduct.Transaction = transaction;
            deduct.CommandText = @"UPDATE Countries SET
                                      Tanks=Tanks-@tanks,
                                      Soldiers=Soldiers-@soldiers,
                                      Planes=Planes-@fighters,
                                      Bombers=Bombers-@bombers
                                    WHERE OwnerId=@owner AND ChatId=@chat
                                      AND Tanks>=@tanks AND Soldiers>=@soldiers
                                      AND Planes>=@fighters AND Bombers>=@bombers";
            deduct.Parameters.AddWithValue("@tanks", contributor.Tanks);
            deduct.Parameters.AddWithValue("@soldiers", contributor.Soldiers);
            deduct.Parameters.AddWithValue("@fighters", contributor.Fighters);
            deduct.Parameters.AddWithValue("@bombers", contributor.Bombers);
            deduct.Parameters.AddWithValue("@owner", contributor.UserId);
            deduct.Parameters.AddWithValue("@chat", chatId);
            if (deduct.ExecuteNonQuery() != 1)
            {
                transaction.Rollback();
                return false;
            }
        }

        using (var update = con.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = @"UPDATE Deployments SET
                                      Tanks=Tanks+@tanks,
                                      Soldiers=Soldiers+@soldiers,
                                      Fighters=Fighters+@fighters,
                                      Bombers=Bombers+@bombers
                                    WHERE Id=@deployment AND ChatId=@chat AND EndAtMs>@now";
            update.Parameters.AddWithValue("@tanks", contributor.Tanks);
            update.Parameters.AddWithValue("@soldiers", contributor.Soldiers);
            update.Parameters.AddWithValue("@fighters", contributor.Fighters);
            update.Parameters.AddWithValue("@bombers", contributor.Bombers);
            update.Parameters.AddWithValue("@deployment", deploymentId);
            update.Parameters.AddWithValue("@chat", chatId);
            update.Parameters.AddWithValue("@now", nowMs);
            if (update.ExecuteNonQuery() != 1)
            {
                transaction.Rollback();
                return false;
            }
        }

        using (var insert = con.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = @"INSERT INTO DeploymentContributors
                (DeploymentId,UserId,ChatId,Tanks,Soldiers,Fighters,Bombers,Strategy,Tactic)
                VALUES(@deployment,@user,@chat,@tanks,@soldiers,@fighters,@bombers,@strategy,@tactic)";
            insert.Parameters.AddWithValue("@deployment", deploymentId);
            insert.Parameters.AddWithValue("@user", contributor.UserId);
            insert.Parameters.AddWithValue("@chat", chatId);
            insert.Parameters.AddWithValue("@tanks", contributor.Tanks);
            insert.Parameters.AddWithValue("@soldiers", contributor.Soldiers);
            insert.Parameters.AddWithValue("@fighters", contributor.Fighters);
            insert.Parameters.AddWithValue("@bombers", contributor.Bombers);
            insert.Parameters.AddWithValue("@strategy", contributor.Strategy);
            insert.Parameters.AddWithValue("@tactic", contributor.Tactic);
            insert.ExecuteNonQuery();
        }
        ReserveContributorModels(con, transaction, deploymentId, contributor.UserId,
            chatId, "Tanks", contributor.Tanks, tankModels);
        ReserveContributorModels(con, transaction, deploymentId, contributor.UserId,
            chatId, "Planes", contributor.Fighters, fighterModels);
        ReserveContributorModels(con, transaction, deploymentId, contributor.UserId,
            chatId, "Bombers", contributor.Bombers, bomberModels);

        transaction.Commit();
        return true;
    }

    // FIX(2): ذخیرهٔ MessageId پیام پین‌شدهٔ صف‌آرایی
    public static void UpdateDeploymentAnnounceMsg(long id, int msgId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "UPDATE Deployments SET AnnounceMsgId=@m WHERE Id=@id";
        cmd.Parameters.AddWithValue("@m", msgId);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public static List<Deployment> GetActiveDeployments()
    {
        var list = new List<Deployment>();
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT Id, ChatId, AllianceId, InitiatorId, TargetUserId, Type, DurationHours, FormationType, Strategy, Tactic, Tanks, Soldiers, Fighters, Bombers, CreatedAtMs, EndAtMs, LastWarnMs, AnnounceMsgId FROM Deployments";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new Deployment
            {
                Id = r.GetInt64(0),
                ChatId = r.GetInt64(1),
                AllianceId = r.GetInt64(2),
                InitiatorId = r.GetInt64(3),
                TargetUserId = r.GetInt64(4),
                Type = r.GetString(5),
                DurationHours = r.GetInt32(6),
                FormationType = r.GetString(7),
                Strategy = r.GetInt32(8),
                Tactic = r.GetInt32(9),
                Tanks = r.GetInt64(10),
                Soldiers = r.GetInt64(11),
                Fighters = r.GetInt64(12),
                Bombers = r.GetInt64(13),
                CreatedAtMs = r.GetInt64(14),
                EndAtMs = r.GetInt64(15),
                LastWarnMs = r.GetInt64(16),
                AnnounceMsgId = r.IsDBNull(17) ? 0 : r.GetInt32(17)
            });
        }
        return list;
    }

    public static int GetRecentAllianceDeploymentsCount(long allianceId, long sinceMs)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Deployments WHERE AllianceId=@aid AND CreatedAtMs>=@ms";
        cmd.Parameters.AddWithValue("@aid", allianceId);
        cmd.Parameters.AddWithValue("@ms", sinceMs);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public static bool HasRecentTargetDeployment(long chatId, long targetUserId, long sinceMs)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM Deployments WHERE ChatId=@cid AND TargetUserId=@tid AND CreatedAtMs>=@ms LIMIT 1";
        cmd.Parameters.AddWithValue("@cid", chatId);
        cmd.Parameters.AddWithValue("@tid", targetUserId);
        cmd.Parameters.AddWithValue("@ms", sinceMs);
        using var r = cmd.ExecuteReader();
        return r.Read();
    }

    public static void UpdateDeploymentWarnMs(long id, long warnMs)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "UPDATE Deployments SET LastWarnMs=@ms WHERE Id=@id";
        cmd.Parameters.AddWithValue("@ms", warnMs);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public static void DeleteDeployment(long id)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "DELETE FROM DeploymentContributorModels WHERE DeploymentId=@id; DELETE FROM DeploymentContributors WHERE DeploymentId=@id; DELETE FROM Deployments WHERE Id=@id;";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public static void UpdateDeploymentEndMs(long id, long endMs)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "UPDATE Deployments SET EndAtMs=@ms WHERE Id=@id";
        cmd.Parameters.AddWithValue("@ms", endMs);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public static bool WithdrawDeploymentContribution(
        long deploymentId,
        long userId,
        long chatId,
        out long tanks,
        out long soldiers,
        out long fighters,
        out long bombers,
        out bool deploymentDeleted)
    {
        tanks = soldiers = fighters = bombers = 0;
        deploymentDeleted = false;
        using var con = OpenCon();
        using var transaction = con.BeginTransaction();

        using (var totals = con.CreateCommand())
        {
            totals.Transaction = transaction;
            totals.CommandText = @"SELECT COALESCE(SUM(Tanks),0), COALESCE(SUM(Soldiers),0),
                                          COALESCE(SUM(Fighters),0), COALESCE(SUM(Bombers),0), COUNT(*)
                                   FROM DeploymentContributors
                                   WHERE DeploymentId=@deployment AND UserId=@user";
            totals.Parameters.AddWithValue("@deployment", deploymentId);
            totals.Parameters.AddWithValue("@user", userId);
            using var reader = totals.ExecuteReader();
            if (!reader.Read() || reader.GetInt64(4) == 0)
            {
                transaction.Rollback();
                return false;
            }
            tanks = reader.GetInt64(0);
            soldiers = reader.GetInt64(1);
            fighters = reader.GetInt64(2);
            bombers = reader.GetInt64(3);
        }

        using (var restore = con.CreateCommand())
        {
            restore.Transaction = transaction;
            restore.CommandText = @"UPDATE Countries SET
                                      Tanks=Tanks+@tanks,
                                      Soldiers=Soldiers+@soldiers,
                                      Planes=Planes+@fighters,
                                      Bombers=Bombers+@bombers
                                    WHERE OwnerId=@owner AND ChatId=@chat";
            restore.Parameters.AddWithValue("@tanks", tanks);
            restore.Parameters.AddWithValue("@soldiers", soldiers);
            restore.Parameters.AddWithValue("@fighters", fighters);
            restore.Parameters.AddWithValue("@bombers", bombers);
            restore.Parameters.AddWithValue("@owner", userId);
            restore.Parameters.AddWithValue("@chat", chatId);
            if (restore.ExecuteNonQuery() != 1)
            {
                transaction.Rollback();
                return false;
            }
        }

        using (var deleteModels = con.CreateCommand())
        {
            deleteModels.Transaction = transaction;
            deleteModels.CommandText = @"DELETE FROM DeploymentContributorModels
                                         WHERE DeploymentId=@deployment AND UserId=@user";
            deleteModels.Parameters.AddWithValue("@deployment", deploymentId);
            deleteModels.Parameters.AddWithValue("@user", userId);
            deleteModels.ExecuteNonQuery();
        }

        using (var deleteContribution = con.CreateCommand())
        {
            deleteContribution.Transaction = transaction;
            deleteContribution.CommandText = @"DELETE FROM DeploymentContributors
                                               WHERE DeploymentId=@deployment AND UserId=@user";
            deleteContribution.Parameters.AddWithValue("@deployment", deploymentId);
            deleteContribution.Parameters.AddWithValue("@user", userId);
            deleteContribution.ExecuteNonQuery();
        }

        using (var updateDeployment = con.CreateCommand())
        {
            updateDeployment.Transaction = transaction;
            updateDeployment.CommandText = @"UPDATE Deployments SET
                                               Tanks=MAX(0,Tanks-@tanks),
                                               Soldiers=MAX(0,Soldiers-@soldiers),
                                               Fighters=MAX(0,Fighters-@fighters),
                                               Bombers=MAX(0,Bombers-@bombers)
                                             WHERE Id=@deployment";
            updateDeployment.Parameters.AddWithValue("@tanks", tanks);
            updateDeployment.Parameters.AddWithValue("@soldiers", soldiers);
            updateDeployment.Parameters.AddWithValue("@fighters", fighters);
            updateDeployment.Parameters.AddWithValue("@bombers", bombers);
            updateDeployment.Parameters.AddWithValue("@deployment", deploymentId);
            if (updateDeployment.ExecuteNonQuery() != 1)
            {
                transaction.Rollback();
                return false;
            }
        }

        using (var remaining = con.CreateCommand())
        {
            remaining.Transaction = transaction;
            remaining.CommandText = "SELECT COUNT(*) FROM DeploymentContributors WHERE DeploymentId=@deployment";
            remaining.Parameters.AddWithValue("@deployment", deploymentId);
            deploymentDeleted = Convert.ToInt32(remaining.ExecuteScalar()) == 0;
        }
        if (deploymentDeleted)
        {
            using var deleteDeployment = con.CreateCommand();
            deleteDeployment.Transaction = transaction;
            deleteDeployment.CommandText = "DELETE FROM Deployments WHERE Id=@deployment";
            deleteDeployment.Parameters.AddWithValue("@deployment", deploymentId);
            deleteDeployment.ExecuteNonQuery();
        }

        transaction.Commit();
        return true;
    }

    public static bool ReturnDeploymentForcesAndDelete(
        long deploymentId,
        long chatId,
        IReadOnlyList<(long UserId, long Tanks, long Soldiers, long Fighters, long Bombers)> returns,
        bool allowBattleLosses = false)
    {
        using var con = OpenCon();
        using var transaction = con.BeginTransaction();

        var capacities = new Dictionary<long, (long Tanks, long Soldiers, long Fighters, long Bombers)>();
        using (var ledger = con.CreateCommand())
        {
            ledger.Transaction = transaction;
            ledger.CommandText = @"SELECT UserId,COALESCE(SUM(Tanks),0),COALESCE(SUM(Soldiers),0),
                                           COALESCE(SUM(Fighters),0),COALESCE(SUM(Bombers),0)
                                    FROM DeploymentContributors WHERE DeploymentId=@id GROUP BY UserId";
            ledger.Parameters.AddWithValue("@id", deploymentId);
            using var reader = ledger.ExecuteReader();
            while (reader.Read()) capacities[reader.GetInt64(0)] =
                (reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4));
        }
        var normalizedReturns = returns.GroupBy(x => x.UserId).ToDictionary(g => g.Key, g => (
            Tanks: g.Sum(x => x.Tanks), Soldiers: g.Sum(x => x.Soldiers),
            Fighters: g.Sum(x => x.Fighters), Bombers: g.Sum(x => x.Bombers)));
        if (capacities.Count == 0 || capacities.Keys.Any(x => !normalizedReturns.ContainsKey(x)) ||
            normalizedReturns.Any(x => !capacities.TryGetValue(x.Key, out var cap) ||
                x.Value.Tanks < 0 || x.Value.Soldiers < 0 || x.Value.Fighters < 0 || x.Value.Bombers < 0 ||
                x.Value.Tanks > cap.Tanks || x.Value.Soldiers > cap.Soldiers ||
                x.Value.Fighters > cap.Fighters || x.Value.Bombers > cap.Bombers ||
                (!allowBattleLosses && (x.Value.Tanks != cap.Tanks || x.Value.Soldiers != cap.Soldiers ||
                    x.Value.Fighters != cap.Fighters || x.Value.Bombers != cap.Bombers))))
        {
            transaction.Rollback();
            return false;
        }

        using (var claim = con.CreateCommand())
        {
            claim.Transaction = transaction;
            claim.CommandText = "DELETE FROM Deployments WHERE Id=@id";
            claim.Parameters.AddWithValue("@id", deploymentId);
            if (claim.ExecuteNonQuery() != 1)
            {
                transaction.Rollback();
                return false;
            }
        }

        foreach (var (userId, item) in normalizedReturns)
        {
            using var restore = con.CreateCommand();
            restore.Transaction = transaction;
            restore.CommandText = @"UPDATE Countries SET
                                      Tanks=Tanks+@tanks,
                                      Soldiers=Soldiers+@soldiers,
                                      Planes=Planes+@fighters,
                                      Bombers=Bombers+@bombers
                                    WHERE OwnerId=@owner AND ChatId=@chat";
            restore.Parameters.AddWithValue("@tanks", item.Tanks);
            restore.Parameters.AddWithValue("@soldiers", item.Soldiers);
            restore.Parameters.AddWithValue("@fighters", item.Fighters);
            restore.Parameters.AddWithValue("@bombers", item.Bombers);
            restore.Parameters.AddWithValue("@owner", userId);
            restore.Parameters.AddWithValue("@chat", chatId);
            if (restore.ExecuteNonQuery() != 1)
            {
                transaction.Rollback();
                return false;
            }
        }

        using (var contributorModels = con.CreateCommand())
        {
            contributorModels.Transaction = transaction;
            contributorModels.CommandText = "DELETE FROM DeploymentContributorModels WHERE DeploymentId=@id";
            contributorModels.Parameters.AddWithValue("@id", deploymentId);
            contributorModels.ExecuteNonQuery();
        }

        using (var contributors = con.CreateCommand())
        {
            contributors.Transaction = transaction;
            contributors.CommandText = "DELETE FROM DeploymentContributors WHERE DeploymentId=@id";
            contributors.Parameters.AddWithValue("@id", deploymentId);
            contributors.ExecuteNonQuery();
        }

        transaction.Commit();
        return true;
    }

    public static string RepairDeploymentIntegrity()
    {
        using var con = OpenCon();
        using var transaction = con.BeginTransaction();
        int totalsFixed = 0, emptyRecovered = 0, orphanContributorsRecovered = 0, modelRowsFixed = 0;

        // From this version onward contributors carry ChatId, so even a legacy/orphan row
        // whose parent deployment vanished can be returned safely instead of being lost.
        var orphanContributors = new List<(long DeploymentId,long UserId,long ChatId,long Tanks,long Soldiers,long Fighters,long Bombers)>();
        using (var orphanSelect = con.CreateCommand())
        {
            orphanSelect.Transaction = transaction;
            orphanSelect.CommandText = @"SELECT dc.DeploymentId,dc.UserId,dc.ChatId,
 COALESCE(SUM(dc.Tanks),0),COALESCE(SUM(dc.Soldiers),0),COALESCE(SUM(dc.Fighters),0),COALESCE(SUM(dc.Bombers),0)
 FROM DeploymentContributors dc LEFT JOIN Deployments d ON d.Id=dc.DeploymentId
 WHERE d.Id IS NULL AND dc.ChatId!=0 GROUP BY dc.DeploymentId,dc.UserId,dc.ChatId";
            using var reader=orphanSelect.ExecuteReader();
            while(reader.Read())orphanContributors.Add((reader.GetInt64(0),reader.GetInt64(1),reader.GetInt64(2),
                reader.GetInt64(3),reader.GetInt64(4),reader.GetInt64(5),reader.GetInt64(6)));
        }
        foreach(var orphan in orphanContributors)
        {
            using var restore=con.CreateCommand();restore.Transaction=transaction;
            restore.CommandText=@"UPDATE Countries SET Tanks=Tanks+@t,Soldiers=Soldiers+@s,Planes=Planes+@f,Bombers=Bombers+@b
 WHERE OwnerId=@o AND ChatId=@c";
            restore.Parameters.AddWithValue("@t",orphan.Tanks);restore.Parameters.AddWithValue("@s",orphan.Soldiers);
            restore.Parameters.AddWithValue("@f",orphan.Fighters);restore.Parameters.AddWithValue("@b",orphan.Bombers);
            restore.Parameters.AddWithValue("@o",orphan.UserId);restore.Parameters.AddWithValue("@c",orphan.ChatId);
            if(restore.ExecuteNonQuery()!=1)continue;
            using var deleteModels=con.CreateCommand();deleteModels.Transaction=transaction;
            deleteModels.CommandText="DELETE FROM DeploymentContributorModels WHERE DeploymentId=@d AND UserId=@u";
            deleteModels.Parameters.AddWithValue("@d",orphan.DeploymentId);deleteModels.Parameters.AddWithValue("@u",orphan.UserId);deleteModels.ExecuteNonQuery();
            using var deleteContributor=con.CreateCommand();deleteContributor.Transaction=transaction;
            deleteContributor.CommandText="DELETE FROM DeploymentContributors WHERE DeploymentId=@d AND UserId=@u";
            deleteContributor.Parameters.AddWithValue("@d",orphan.DeploymentId);deleteContributor.Parameters.AddWithValue("@u",orphan.UserId);deleteContributor.ExecuteNonQuery();
            orphanContributorsRecovered++;
        }

        // Model reservations without a matching contributor can hide equipment forever.
        using (var orphanModels = con.CreateCommand())
        {
            orphanModels.Transaction = transaction;
            orphanModels.CommandText = @"DELETE FROM DeploymentContributorModels
                WHERE NOT EXISTS (SELECT 1 FROM DeploymentContributors dc
                                  WHERE dc.DeploymentId=DeploymentContributorModels.DeploymentId
                                    AND dc.UserId=DeploymentContributorModels.UserId)";
            modelRowsFixed += orphanModels.ExecuteNonQuery();
        }

        var deployments = new List<(long Id,long ChatId,long InitiatorId,long Tanks,long Soldiers,long Fighters,long Bombers)>();
        using (var select = con.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT Id,ChatId,InitiatorId,Tanks,Soldiers,Fighters,Bombers FROM Deployments";
            using var reader = select.ExecuteReader();
            while (reader.Read()) deployments.Add((reader.GetInt64(0),reader.GetInt64(1),reader.GetInt64(2),
                reader.GetInt64(3),reader.GetInt64(4),reader.GetInt64(5),reader.GetInt64(6)));
        }

        foreach (var deployment in deployments)
        {
            long tanks=0,soldiers=0,fighters=0,bombers=0,count=0;
            using (var sums = con.CreateCommand())
            {
                sums.Transaction = transaction;
                sums.CommandText = @"SELECT COALESCE(SUM(Tanks),0),COALESCE(SUM(Soldiers),0),
 COALESCE(SUM(Fighters),0),COALESCE(SUM(Bombers),0),COUNT(*)
 FROM DeploymentContributors WHERE DeploymentId=@id";
                sums.Parameters.AddWithValue("@id",deployment.Id);
                using var reader=sums.ExecuteReader();reader.Read();
                tanks=reader.GetInt64(0);soldiers=reader.GetInt64(1);fighters=reader.GetInt64(2);bombers=reader.GetInt64(3);count=reader.GetInt64(4);
            }
            if(count==0)
            {
                // A deployment cannot legitimately exist without its contributor ledger.
                // Old broken versions could leave this state after deducting the initiator.
                using var restore=con.CreateCommand();restore.Transaction=transaction;
                restore.CommandText=@"UPDATE Countries SET Tanks=Tanks+@t,Soldiers=Soldiers+@s,
 Planes=Planes+@f,Bombers=Bombers+@b WHERE OwnerId=@o AND ChatId=@c";
                restore.Parameters.AddWithValue("@t",Math.Max(0,deployment.Tanks));restore.Parameters.AddWithValue("@s",Math.Max(0,deployment.Soldiers));
                restore.Parameters.AddWithValue("@f",Math.Max(0,deployment.Fighters));restore.Parameters.AddWithValue("@b",Math.Max(0,deployment.Bombers));
                restore.Parameters.AddWithValue("@o",deployment.InitiatorId);restore.Parameters.AddWithValue("@c",deployment.ChatId);
                if(restore.ExecuteNonQuery()==1)emptyRecovered++;
                using var delete=con.CreateCommand();delete.Transaction=transaction;delete.CommandText="DELETE FROM Deployments WHERE Id=@id";
                delete.Parameters.AddWithValue("@id",deployment.Id);delete.ExecuteNonQuery();continue;
            }
            if(tanks!=deployment.Tanks||soldiers!=deployment.Soldiers||fighters!=deployment.Fighters||bombers!=deployment.Bombers)
            {
                using var update=con.CreateCommand();update.Transaction=transaction;
                update.CommandText="UPDATE Deployments SET Tanks=@t,Soldiers=@s,Fighters=@f,Bombers=@b WHERE Id=@id";
                update.Parameters.AddWithValue("@t",tanks);update.Parameters.AddWithValue("@s",soldiers);
                update.Parameters.AddWithValue("@f",fighters);update.Parameters.AddWithValue("@b",bombers);
                update.Parameters.AddWithValue("@id",deployment.Id);update.ExecuteNonQuery();totalsFixed++;
            }
        }
        transaction.Commit();
        return $"totals={totalsFixed}, emptyRecovered={emptyRecovered}, orphanContributors={orphanContributorsRecovered}, orphanModels={modelRowsFixed}";
    }

    public static bool CancelDeploymentForces(Deployment d)
    {
        var returns = GetDeploymentContributors(d.Id)
            .GroupBy(x => x.UserId)
            .Select(g => (
                UserId: g.Key,
                Tanks: g.Sum(x => x.Tanks),
                Soldiers: g.Sum(x => x.Soldiers),
                Fighters: g.Sum(x => x.Fighters),
                Bombers: g.Sum(x => x.Bombers)))
            .ToList();
        if (!ReturnDeploymentForcesAndDelete(d.Id, d.ChatId, returns)) return false;
        foreach (long contributorId in returns.Select(x => x.UserId).Distinct())
            ReconcileDefense(contributorId, d.ChatId);
        return true;
    }

    public static Deployment? GetDeploymentById(long id)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT Id, ChatId, AllianceId, InitiatorId, TargetUserId, Type, DurationHours, FormationType, Strategy, Tactic, Tanks, Soldiers, Fighters, Bombers, CreatedAtMs, EndAtMs, LastWarnMs, AnnounceMsgId FROM Deployments WHERE Id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        if (r.Read())
        {
            return new Deployment
            {
                Id = r.GetInt64(0), ChatId = r.GetInt64(1), AllianceId = r.GetInt64(2), InitiatorId = r.GetInt64(3), TargetUserId = r.GetInt64(4), Type = r.GetString(5), DurationHours = r.GetInt32(6), FormationType = r.GetString(7), Strategy = r.GetInt32(8), Tactic = r.GetInt32(9), Tanks = r.GetInt64(10), Soldiers = r.GetInt64(11), Fighters = r.GetInt64(12), Bombers = r.GetInt64(13), CreatedAtMs = r.GetInt64(14), EndAtMs = r.GetInt64(15), LastWarnMs = r.GetInt64(16), AnnounceMsgId = r.IsDBNull(17) ? 0 : r.GetInt32(17)
            };
        }
        return null;
    }

    public static void UpdateDeploymentForces(Deployment d)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "UPDATE Deployments SET Tanks=@t, Soldiers=@s, Fighters=@f, Bombers=@b WHERE Id=@id";
        cmd.Parameters.AddWithValue("@t", d.Tanks);
        cmd.Parameters.AddWithValue("@s", d.Soldiers);
        cmd.Parameters.AddWithValue("@f", d.Fighters);
        cmd.Parameters.AddWithValue("@b", d.Bombers);
        cmd.Parameters.AddWithValue("@id", d.Id);
        cmd.ExecuteNonQuery();
    }

    public static void ApplyDefensiveDeploymentLosses(
        long chatId,
        long targetUserId,
        long contributorUserId,
        long tanksLost,
        long soldiersLost,
        long fightersLost,
        long bombersLost,
        IReadOnlyCollection<long>? deploymentIds = null)
    {
        using var con = OpenCon();
        using var transaction = con.BeginTransaction();
        ApplyDefensiveDeploymentLosses(con, transaction, chatId, targetUserId, contributorUserId,
            tanksLost, soldiersLost, fightersLost, bombersLost, deploymentIds);
        transaction.Commit();
    }

    private static void ApplyDefensiveDeploymentLosses(
        SqliteConnection con,
        SqliteTransaction transaction,
        long chatId,
        long targetUserId,
        long contributorUserId,
        long tanksLost,
        long soldiersLost,
        long fightersLost,
        long bombersLost,
        IReadOnlyCollection<long>? deploymentIds)
    {
        if (deploymentIds != null && deploymentIds.Count == 0) return;
        var rows = new List<(long Id, long DeploymentId, long Tanks, long Soldiers, long Fighters, long Bombers)>();
        using (var select = con.CreateCommand())
        {
            select.Transaction = transaction;
            string idFilter = "";
            if (deploymentIds != null)
            {
                var parameterNames = deploymentIds.Select((_, i) => $"@deployment{i}").ToArray();
                idFilter = $" AND d.Id IN ({string.Join(",", parameterNames)})";
                int i = 0;
                foreach (long id in deploymentIds)
                    select.Parameters.AddWithValue($"@deployment{i++}", id);
            }
            select.CommandText = @"SELECT dc.Id,dc.DeploymentId,dc.Tanks,dc.Soldiers,dc.Fighters,dc.Bombers
                                   FROM DeploymentContributors dc
                                   JOIN Deployments d ON d.Id=dc.DeploymentId
                                   WHERE d.ChatId=@chat AND d.TargetUserId=@target
                                     AND d.Type='Defensive' AND dc.UserId=@user" + idFilter +
                                 " ORDER BY dc.Id";
            select.Parameters.AddWithValue("@chat", chatId);
            select.Parameters.AddWithValue("@target", targetUserId);
            select.Parameters.AddWithValue("@user", contributorUserId);
            using var reader = select.ExecuteReader();
            while (reader.Read())
                rows.Add((reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5)));
        }

        static long[] AllocateExact(long requestedLoss, long[] amounts)
        {
            long total = amounts.Sum();
            long target = Math.Min(Math.Max(0, requestedLoss), total);
            var allocated = new long[amounts.Length];
            if (target == 0 || total == 0) return allocated;

            var remainders = new decimal[amounts.Length];
            long assigned = 0;
            for (int i = 0; i < amounts.Length; i++)
            {
                decimal exact = (decimal)target * amounts[i] / total;
                allocated[i] = Math.Min(amounts[i], (long)decimal.Floor(exact));
                remainders[i] = exact - allocated[i];
                assigned += allocated[i];
            }
            foreach (int i in Enumerable.Range(0, amounts.Length)
                         .OrderByDescending(i => remainders[i]).ThenBy(i => i))
            {
                if (assigned >= target) break;
                if (allocated[i] >= amounts[i]) continue;
                allocated[i]++;
                assigned++;
            }
            return allocated;
        }

        long[] allocatedT = AllocateExact(tanksLost, rows.Select(x => x.Tanks).ToArray());
        long[] allocatedS = AllocateExact(soldiersLost, rows.Select(x => x.Soldiers).ToArray());
        long[] allocatedF = AllocateExact(fightersLost, rows.Select(x => x.Fighters).ToArray());
        long[] allocatedB = AllocateExact(bombersLost, rows.Select(x => x.Bombers).ToArray());

        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            long lt = allocatedT[rowIndex];
            long ls = allocatedS[rowIndex];
            long lf = allocatedF[rowIndex];
            long lb = allocatedB[rowIndex];
            ReduceContributorModels(con, transaction, row.DeploymentId, contributorUserId, "Tanks", lt);
            ReduceContributorModels(con, transaction, row.DeploymentId, contributorUserId, "Planes", lf);
            ReduceContributorModels(con, transaction, row.DeploymentId, contributorUserId, "Bombers", lb);

            using var updateContributor = con.CreateCommand();
            updateContributor.Transaction = transaction;
            updateContributor.CommandText = @"UPDATE DeploymentContributors SET
                                                 Tanks=MAX(0,Tanks-@t), Soldiers=MAX(0,Soldiers-@s),
                                                 Fighters=MAX(0,Fighters-@f), Bombers=MAX(0,Bombers-@b)
                                               WHERE Id=@id";
            updateContributor.Parameters.AddWithValue("@t", lt);
            updateContributor.Parameters.AddWithValue("@s", ls);
            updateContributor.Parameters.AddWithValue("@f", lf);
            updateContributor.Parameters.AddWithValue("@b", lb);
            updateContributor.Parameters.AddWithValue("@id", row.Id);
            updateContributor.ExecuteNonQuery();

            using var updateDeployment = con.CreateCommand();
            updateDeployment.Transaction = transaction;
            updateDeployment.CommandText = @"UPDATE Deployments SET
                                                Tanks=MAX(0,Tanks-@t), Soldiers=MAX(0,Soldiers-@s),
                                                Fighters=MAX(0,Fighters-@f), Bombers=MAX(0,Bombers-@b)
                                              WHERE Id=@id";
            updateDeployment.Parameters.AddWithValue("@t", lt);
            updateDeployment.Parameters.AddWithValue("@s", ls);
            updateDeployment.Parameters.AddWithValue("@f", lf);
            updateDeployment.Parameters.AddWithValue("@b", lb);
            updateDeployment.Parameters.AddWithValue("@id", row.DeploymentId);
            updateDeployment.ExecuteNonQuery();
        }
    }

    public static void AddDeploymentContributor(DeploymentContributor c)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"INSERT INTO DeploymentContributors
(DeploymentId,UserId,ChatId,Tanks,Soldiers,Fighters,Bombers,Strategy,Tactic)
VALUES(@did,@uid,COALESCE((SELECT ChatId FROM Deployments WHERE Id=@did),0),@t,@s,@f,@b,@str,@tac)";
        cmd.Parameters.AddWithValue("@did", c.DeploymentId);
        cmd.Parameters.AddWithValue("@uid", c.UserId);
        cmd.Parameters.AddWithValue("@t", c.Tanks);
        cmd.Parameters.AddWithValue("@s", c.Soldiers);
        cmd.Parameters.AddWithValue("@f", c.Fighters);
        cmd.Parameters.AddWithValue("@b", c.Bombers);
        cmd.Parameters.AddWithValue("@str", c.Strategy);
        cmd.Parameters.AddWithValue("@tac", c.Tactic);
        cmd.ExecuteNonQuery();
    }

    public static List<DeploymentContributor> GetDeploymentContributors(long depId)
    {
        var list = new List<DeploymentContributor>();
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT Id, DeploymentId, UserId, Tanks, Soldiers, Fighters, Bombers, Strategy, Tactic FROM DeploymentContributors WHERE DeploymentId=@did";
        cmd.Parameters.AddWithValue("@did", depId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new DeploymentContributor
            {
                Id = r.GetInt64(0), DeploymentId = r.GetInt64(1), UserId = r.GetInt64(2), Tanks = r.GetInt64(3), Soldiers = r.GetInt64(4), Fighters = r.GetInt64(5), Bombers = r.GetInt64(6), Strategy = r.GetInt32(7), Tactic = r.GetInt32(8)
            });
        }
        return list;
    }
    public static void DeleteDeploymentContributorById(long contribId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "DELETE FROM DeploymentContributors WHERE Id=@id";
        cmd.Parameters.AddWithValue("@id", contribId);
        cmd.ExecuteNonQuery();
    }

    public static void DeleteDeploymentContributorsByUser(long depId, long userId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "DELETE FROM DeploymentContributors WHERE DeploymentId=@did AND UserId=@uid";
        cmd.Parameters.AddWithValue("@did", depId);
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.ExecuteNonQuery();
    }
    public static long AddVisionLog(long sourceChatId, long sourceUserId, long destChatId, int isUserMode)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "INSERT INTO VisionLogs(SourceChatId, SourceUserId, DestChatId, IsUserMode, CreatedAtMs) VALUES(@sc, @su, @dc, @mode, @ms); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@sc", sourceChatId);
        cmd.Parameters.AddWithValue("@su", sourceUserId);
        cmd.Parameters.AddWithValue("@dc", destChatId);
        cmd.Parameters.AddWithValue("@mode", isUserMode);
        cmd.Parameters.AddWithValue("@ms", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        return Convert.ToInt64(cmd.ExecuteScalar());
    }
    public static List<(long Id, long SourceChatId, long SourceUserId, long DestChatId, int IsUserMode)> GetVisionLogsBySourceChat(long chatId)
    {
        var list = new List<(long, long, long, long, int)>();
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT Id, SourceChatId, SourceUserId, DestChatId, IsUserMode FROM VisionLogs WHERE SourceChatId=@cid";
        cmd.Parameters.AddWithValue("@cid", chatId);
        using var r = cmd.ExecuteReader();
        while(r.Read()){ list.Add((r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.GetInt64(3), r.GetInt32(4))); }
        return list;
    }
    public static List<(long Id, long SourceChatId, long SourceUserId, long DestChatId, int IsUserMode)> GetVisionLogsBySourceUser(long userId)
    {
        var list = new List<(long, long, long, long, int)>();
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT Id, SourceChatId, SourceUserId, DestChatId, IsUserMode FROM VisionLogs WHERE SourceUserId=@uid";
        cmd.Parameters.AddWithValue("@uid", userId);
        using var r = cmd.ExecuteReader();
        while(r.Read()){ list.Add((r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.GetInt64(3), r.GetInt32(4))); }
        return list;
    }
    public static void AddVisionMessageMap(long srcChat, long srcMsg, long srcUser, long dstChat, long dstMsg)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "INSERT INTO VisionMessageMap(SourceChatId, SourceMessageId, SourceUserId, DestChatId, DestMessageId, CreatedAtMs) VALUES(@sc, @sm, @su, @dc, @dm, @ms)";
        cmd.Parameters.AddWithValue("@sc", srcChat);
        cmd.Parameters.AddWithValue("@sm", srcMsg);
        cmd.Parameters.AddWithValue("@su", srcUser);
        cmd.Parameters.AddWithValue("@dc", dstChat);
        cmd.Parameters.AddWithValue("@dm", dstMsg);
        cmd.Parameters.AddWithValue("@ms", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        cmd.ExecuteNonQuery();
    }
    public static (long DestChatId, long DestMessageId, long SourceUserId)? GetDestMessageId(long srcChat, long srcMsg, long dstChat)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT DestChatId, DestMessageId, SourceUserId FROM VisionMessageMap WHERE SourceChatId=@sc AND SourceMessageId=@sm AND DestChatId=@dc LIMIT 1";
        cmd.Parameters.AddWithValue("@sc", srcChat);
        cmd.Parameters.AddWithValue("@sm", srcMsg);
        cmd.Parameters.AddWithValue("@dc", dstChat);
        using var r = cmd.ExecuteReader();
        if(r.Read()) return (r.GetInt64(0), r.GetInt64(1), r.GetInt64(2));
        return null;
    }
    public static (long SourceChatId, long SourceMessageId, long SourceUserId)? GetSourceByDestId(long dstChat, long dstMsg)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT SourceChatId, SourceMessageId, SourceUserId FROM VisionMessageMap WHERE DestChatId=@dc AND DestMessageId=@dm LIMIT 1";
        cmd.Parameters.AddWithValue("@dc", dstChat);
        cmd.Parameters.AddWithValue("@dm", dstMsg);
        using var r = cmd.ExecuteReader();
        if(r.Read()) return (r.GetInt64(0), r.GetInt64(1), r.GetInt64(2));
        return null;
    }

    // =====  – Naval Invasions & Shields & Fuel =====
    public static long AddNavalInvasion(NavalInvasion inv)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"INSERT INTO NavalInvasions(ChatId,AttackerId,DefenderId,Boats,Submarines,Battleships,BoatModels,SubModels,BattleshipModels,Strategy,Tactic,CreatedAtMs,ArriveAtMs,Processed,AttackerName,DefenderName)
                            VALUES(@cid,@aid,@did,@boats,@subs,@bships,@bmodels,@smodels,@bsmodels,@strat,@tac,@cMs,@aMs,0,@aName,@dName); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@cid", inv.ChatId);
        cmd.Parameters.AddWithValue("@aid", inv.AttackerId);
        cmd.Parameters.AddWithValue("@did", inv.DefenderId);
        cmd.Parameters.AddWithValue("@boats", inv.Boats);
        cmd.Parameters.AddWithValue("@subs", inv.Submarines);
        cmd.Parameters.AddWithValue("@bships", inv.Battleships);
        cmd.Parameters.AddWithValue("@bmodels", inv.BoatModels);
        cmd.Parameters.AddWithValue("@smodels", inv.SubModels);
        cmd.Parameters.AddWithValue("@bsmodels", inv.BattleshipModels);
        cmd.Parameters.AddWithValue("@strat", inv.Strategy);
        cmd.Parameters.AddWithValue("@tac", inv.Tactic);
        cmd.Parameters.AddWithValue("@cMs", inv.CreatedAtMs);
        cmd.Parameters.AddWithValue("@aMs", inv.ArriveAtMs);
        cmd.Parameters.AddWithValue("@aName", inv.AttackerName ?? "");
        cmd.Parameters.AddWithValue("@dName", inv.DefenderName ?? "");
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public static List<NavalInvasion> GetPendingNavalInvasions(long nowMs)
    {
        var list = new List<NavalInvasion>();
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT Id,ChatId,AttackerId,DefenderId,Boats,Submarines,Battleships,BoatModels,SubModels,BattleshipModels,Strategy,Tactic,CreatedAtMs,ArriveAtMs,Processed,AttackerName,DefenderName,Status,ResultJson FROM NavalInvasions WHERE ArriveAtMs<=@now AND (Processed=0 OR Status='Settled')";
        cmd.Parameters.AddWithValue("@now", nowMs);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new NavalInvasion
            {
                Id = r.GetInt64(0),
                ChatId = r.GetInt64(1),
                AttackerId = r.GetInt64(2),
                DefenderId = r.GetInt64(3),
                Boats = r.GetInt64(4),
                Submarines = r.GetInt64(5),
                Battleships = r.GetInt64(6),
                BoatModels = r.IsDBNull(7) ? "" : r.GetString(7),
                SubModels = r.IsDBNull(8) ? "" : r.GetString(8),
                BattleshipModels = r.IsDBNull(9) ? "" : r.GetString(9),
                Strategy = r.GetInt32(10),
                Tactic = r.GetInt32(11),
                CreatedAtMs = r.GetInt64(12),
                ArriveAtMs = r.GetInt64(13),
                Processed = r.GetInt32(14),
                AttackerName = r.IsDBNull(15) ? "" : r.GetString(15),
                DefenderName = r.IsDBNull(16) ? "" : r.GetString(16),
                Status = r.FieldCount > 17 && !r.IsDBNull(17) ? r.GetString(17) : "Pending",
                ResultJson = r.FieldCount > 18 && !r.IsDBNull(18) ? r.GetString(18) : ""
            });
        }
        return list;
    }

    public static void MarkNavalInvasionProcessed(long id)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "UPDATE NavalInvasions SET Processed=1,Status='Completed' WHERE Id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public static void DeleteNavalInvasion(long id)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "DELETE FROM NavalInvasions WHERE Id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public static List<NavalInvasion> GetActiveNavalInvasionsByAttacker(long attackerId, long chatId)
    {
        var list = new List<NavalInvasion>();
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT Id,ChatId,AttackerId,DefenderId,Boats,Submarines,Battleships,BoatModels,SubModels,BattleshipModels,CreatedAtMs,ArriveAtMs,AttackerName,DefenderName FROM NavalInvasions WHERE AttackerId=@aid AND ChatId=@cid AND Processed=0";
        cmd.Parameters.AddWithValue("@aid", attackerId);
        cmd.Parameters.AddWithValue("@cid", chatId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new NavalInvasion
            {
                Id = r.GetInt64(0), ChatId = r.GetInt64(1), AttackerId = r.GetInt64(2),
                DefenderId = r.GetInt64(3), Boats = r.GetInt64(4), Submarines = r.GetInt64(5),
                Battleships = r.GetInt64(6), BoatModels = r.GetString(7), SubModels = r.GetString(8),
                BattleshipModels = r.GetString(9), CreatedAtMs = r.GetInt64(10), ArriveAtMs = r.GetInt64(11),
                AttackerName = r.GetString(12), DefenderName = r.GetString(13)
            });
        }
        return list;
    }

    // Attack Shields – 5 attacks => 16h shield
    public static long GetAttackShieldUntilMs(long ownerId, long chatId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT ShieldUntilMs FROM AttackShields WHERE OwnerId=@o AND ChatId=@c";
        cmd.Parameters.AddWithValue("@o", ownerId);
        cmd.Parameters.AddWithValue("@c", chatId);
        var v = cmd.ExecuteScalar();
        if (v == null || v == DBNull.Value) return 0;
        return Convert.ToInt64(v);
    }

    public static bool IsAttackShieldActive(long ownerId, long chatId)
    {
        long until = GetAttackShieldUntilMs(ownerId, chatId);
        if (until == 0) return false;
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (now >= until)
        {
            // clear expired
            using var con = OpenCon();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "DELETE FROM AttackShields WHERE OwnerId=@o AND ChatId=@c";
            cmd.Parameters.AddWithValue("@o", ownerId);
            cmd.Parameters.AddWithValue("@c", chatId);
            cmd.ExecuteNonQuery();
            return false;
        }
        return true;
    }

    public static void AddAttackShieldHit(long defenderId, long chatId)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        const long resetAfterMs = 24 * 3600_000L;
        const long shieldDurationMs = 16 * 3600_000L;
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO AttackShields(OwnerId,ChatId,ShieldUntilMs,AttackCount,LastAttackMs)
            VALUES(@owner,@chat,0,1,@now)
            ON CONFLICT(OwnerId,ChatId) DO UPDATE SET
                ShieldUntilMs = CASE
                    WHEN AttackShields.ShieldUntilMs > @now
                        THEN AttackShields.ShieldUntilMs
                    WHEN (CASE
                            WHEN AttackShields.LastAttackMs > 0
                             AND @now-AttackShields.LastAttackMs > @resetAfter
                                THEN 1
                            ELSE AttackShields.AttackCount+1
                          END) >= 5
                        THEN @now+@shieldDuration
                    ELSE 0
                END,
                AttackCount = CASE
                    WHEN AttackShields.ShieldUntilMs > @now
                        THEN AttackShields.AttackCount
                    WHEN (CASE
                            WHEN AttackShields.LastAttackMs > 0
                             AND @now-AttackShields.LastAttackMs > @resetAfter
                                THEN 1
                            ELSE AttackShields.AttackCount+1
                          END) >= 5
                        THEN 0
                    WHEN AttackShields.LastAttackMs > 0
                     AND @now-AttackShields.LastAttackMs > @resetAfter
                        THEN 1
                    ELSE AttackShields.AttackCount+1
                END,
                LastAttackMs = CASE
                    WHEN AttackShields.ShieldUntilMs > @now
                        THEN AttackShields.LastAttackMs
                    ELSE @now
                END;";
        cmd.Parameters.AddWithValue("@owner", defenderId);
        cmd.Parameters.AddWithValue("@chat", chatId);
        cmd.Parameters.AddWithValue("@now", now);
        cmd.Parameters.AddWithValue("@resetAfter", resetAfterMs);
        cmd.Parameters.AddWithValue("@shieldDuration", shieldDurationMs);
        cmd.ExecuteNonQuery();
    }

    public static void ClearAttackShield(long ownerId, long chatId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "DELETE FROM AttackShields WHERE OwnerId=@o AND ChatId=@c";
        cmd.Parameters.AddWithValue("@o", ownerId);
        cmd.Parameters.AddWithValue("@c", chatId);
        cmd.ExecuteNonQuery();
    }

    // Boat fuel
    public static int GetBoatFuelPct(long ownerId, long chatId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT FuelPct FROM BoatFuelStates WHERE OwnerId=@o AND ChatId=@c";
        cmd.Parameters.AddWithValue("@o", ownerId);
        cmd.Parameters.AddWithValue("@c", chatId);
        var v = cmd.ExecuteScalar();
        if (v == null || v == DBNull.Value) return 100;
        return Convert.ToInt32(v);
    }

    public static void SetBoatFuelPct(long ownerId, long chatId, int pct)
    {
        pct = Math.Clamp(pct, 0, 100);
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"INSERT INTO BoatFuelStates(OwnerId,ChatId,FuelPct) VALUES(@o,@c,@pct)
                            ON CONFLICT(OwnerId,ChatId) DO UPDATE SET FuelPct=@pct";
        cmd.Parameters.AddWithValue("@o", ownerId);
        cmd.Parameters.AddWithValue("@c", chatId);
        cmd.Parameters.AddWithValue("@pct", pct);
        cmd.ExecuteNonQuery();
    }

    public static long GetNavalCooldownUntilMs(long ownerId, long chatId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT CooldownUntilMs FROM NavalBoatCooldowns WHERE OwnerId=@o AND ChatId=@c";
        cmd.Parameters.AddWithValue("@o", ownerId);
        cmd.Parameters.AddWithValue("@c", chatId);
        var v = cmd.ExecuteScalar();
        if (v == null || v == DBNull.Value) return 0;
        return Convert.ToInt64(v);
    }

    public static void SetNavalCooldown(long ownerId, long chatId, long untilMs)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"INSERT INTO NavalBoatCooldowns(OwnerId,ChatId,CooldownUntilMs) VALUES(@o,@c,@u)
                            ON CONFLICT(OwnerId,ChatId) DO UPDATE SET CooldownUntilMs=@u";
        cmd.Parameters.AddWithValue("@o", ownerId);
        cmd.Parameters.AddWithValue("@c", chatId);
        cmd.Parameters.AddWithValue("@u", untilMs);
        cmd.ExecuteNonQuery();
    }

    public static void ClearNavalCooldown(long ownerId, long chatId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "DELETE FROM NavalBoatCooldowns WHERE OwnerId=@o AND ChatId=@c";
        cmd.Parameters.AddWithValue("@o", ownerId);
        cmd.Parameters.AddWithValue("@c", chatId);
        cmd.ExecuteNonQuery();
    }


    public static void SetGroupLockExemption(long chatId, bool exempt)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        if (exempt)
            cmd.CommandText = "INSERT OR IGNORE INTO GroupLockExemptions(ChatId) VALUES(@cid)";
        else
            cmd.CommandText = "DELETE FROM GroupLockExemptions WHERE ChatId=@cid";
        cmd.Parameters.AddWithValue("@cid", chatId);
        cmd.ExecuteNonQuery();
    }

    public static bool HasGroupLockExemption(long chatId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM GroupLockExemptions WHERE ChatId=@cid LIMIT 1";
        cmd.Parameters.AddWithValue("@cid", chatId);
        using var r = cmd.ExecuteReader();
        return r.Read();
    }

    public static void ClearAllLeaveCooldownsInChat(long chatId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "DELETE FROM LeaveCooldowns WHERE ChatId=@cid";
        cmd.Parameters.AddWithValue("@cid", chatId);
        cmd.ExecuteNonQuery();
    }

    public static void SetAllShieldExemptionsInChat(long chatId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO ShieldExemptions(OwnerId, ChatId) SELECT OwnerId, ChatId FROM Countries WHERE ChatId=@cid";
        cmd.Parameters.AddWithValue("@cid", chatId);
        cmd.ExecuteNonQuery();
    }
}
