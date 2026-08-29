// ===== /root/CountryBot/Program.cs =====
// Fixed version — daily update timer properly awaits, group messages + DB backup to admin
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
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
using Telegram.Bot.Types.ReplyMarkups;
using Telegram.Bot.Types.InputFiles;
using Microsoft.Data.Sqlite;

enum Faction { USSR, USA, Reich }

class Country
{
    public string Name { get; set; } = "";
    public long OwnerId { get; set; }
    public long ChatId { get; set; }
    public string OwnerName { get; set; } = "";
    public Faction Faction { get; set; }
    public string FlagFileId { get; set; } = "";
    public long Money { get; set; } = 10000;
    public long Population { get; set; } = 100000;
    public int Cities { get; set; } = 4;
    public int FactoryLevel { get; set; } = 1;
    public int PortLevel { get; set; } = 1;
    public int MineLevel { get; set; } = 1;
    public long Iron { get; set; } = 0;
    public long Soldiers { get; set; } = 10000;
    public long Tanks { get; set; } = 0;
    public long Planes { get; set; } = 0;
    public long Bombers { get; set; } = 0;
    public long AntiAir { get; set; } = 0;
    public long Boats { get; set; } = 0;
    public long Submarines { get; set; } = 0;
    public long Battleships { get; set; } = 0;
    public long BattleshipDamage { get; set; } = 0; // total damage % sum or damage points
    public long DefenseTanks { get; set; } = 0;
    public long DefenseSoldiers { get; set; } = 0;
    public long DefenseFighters { get; set; } = 0;
    public long DefenseBoats { get; set; } = 0;
    public long DefenseSubmarines { get; set; } = 0;
    //  – naval fuel & damage tracking
    public int BoatsFuel { get; set; } = 100; // 0-100 fuel percent for boats fleet
    public int SubmarinesFuel { get; set; } = 100;
    public long BoatsAtSea { get; set; } = 0;
    public long SubmarinesAtSea { get; set; } = 0;
    public long BattleshipsAtSea { get; set; } = 0;
    public int AirDefStrategy { get; set; } = 1;
    public int AirDefTactic { get; set; } = 1;
    public int Besieged { get; set; } = 0;
    public int DefenseWins { get; set; } = 0;
    public long CreatedAtMs { get; set; } = 0;
    public int DefenseStrategy { get; set; } = 1;
    public int DefenseTactic { get; set; } = 1;
    public int RecruitmentRate { get; set; } = 0;
    public double Welfare { get; set; } = 100;
    public int TaxRate { get; set; } = 30;
    public int DefTankPct { get; set; } = 100;
    public int DefSoldierPct { get; set; } = 100;
    public int DefFighterPct { get; set; } = 100;
}

class Alliance
{
    public long Id { get; set; }
    public long ChatId { get; set; }
    public string Name { get; set; } = "";
    public string FlagFileId { get; set; } = "";
    public long LeaderId { get; set; }
    public long CreatedAtMs { get; set; }
}

class AllianceInvite
{
    public long Id { get; set; }
    public long AllianceId { get; set; }
    public long ChatId { get; set; }
    public long TargetUserId { get; set; }
    public long LeaderId { get; set; }
    public long CreatedAtMs { get; set; }
}

class Transfer
{
    public long Id { get; set; }
    public long ChatId { get; set; }
    public long AllianceId { get; set; }
    public long SenderId { get; set; }
    public long ReceiverId { get; set; }
    public string ResourceType { get; set; } = "";
    public string ModelName { get; set; } = "";
    public long Amount { get; set; }
    public long ArriveAtMs { get; set; }
    public int Notified { get; set; }
}

class Deployment
{
    public long Id { get; set; }
    public long ChatId { get; set; }
    public long AllianceId { get; set; }
    public long InitiatorId { get; set; }
    public long TargetUserId { get; set; }
    public string Type { get; set; } = "";
    public int DurationHours { get; set; }
    public string FormationType { get; set; } = "";
    public int Strategy { get; set; } = 1;
    public int Tactic { get; set; } = 1;
    public long Tanks { get; set; }
    public long Soldiers { get; set; }
    public long Fighters { get; set; }
    public long Bombers { get; set; }
    public long CreatedAtMs { get; set; }
    public long EndAtMs { get; set; }
    public long LastWarnMs { get; set; }
    // FIX(2): پیام اعلام صف‌آرایی که در گروه پین می‌شود تا هنگام لغو/پایان آنپین و حذف شود
    public int AnnounceMsgId { get; set; } = 0;
}

class BattleJobContext
{
    public long AttackerId { get; set; }
    public long DefenderId { get; set; }
    public long ChatId { get; set; }
    public long DeploymentId { get; set; }
    public List<long> DefensiveDeploymentIds { get; set; } = new();
}

class PersistedBattleJob
{
    public long BattleId { get; set; }
    public string JobType { get; set; } = "";
    public string RequestJson { get; set; } = "";
    public string ContextJson { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public string ResultJson { get; set; } = "";
    public string LastError { get; set; } = "";
}

class DeploymentContributor
{
    public long Id { get; set; }
    public long DeploymentId { get; set; }
    public long UserId { get; set; }
    public long Tanks { get; set; }
    public long Soldiers { get; set; }
    public long Fighters { get; set; }
    public long Bombers { get; set; }
    public int Strategy { get; set; } = 1;
    public int Tactic { get; set; } = 1;
}

sealed class SpamRestrictionInfo
{
    public long UserId { get; set; }
    public long ChatId { get; set; }
    public long UntilMs { get; set; }
    public int Level { get; set; }
    public string Reason { get; set; } = "";
    public string LastFingerprint { get; set; } = "";
    public int DroppedCount { get; set; }
    public long UpdatedAtMs { get; set; }
}

class NavalInvasion
{
    public long Id { get; set; }
    public long ChatId { get; set; }
    public long AttackerId { get; set; }
    public long DefenderId { get; set; }
    public long Boats { get; set; }
    public long Submarines { get; set; }
    public long Battleships { get; set; }
    public string BoatModels { get; set; } = "";
    public string SubModels { get; set; } = "";
    public string BattleshipModels { get; set; } = "";
    public int Strategy { get; set; } = 1;
    public int Tactic { get; set; } = 1;
    public long CreatedAtMs { get; set; }
    public long ArriveAtMs { get; set; }
    public int Processed { get; set; } = 0;
    public string AttackerName { get; set; } = "";
    public string DefenderName { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public string ResultJson { get; set; } = "";
}

enum SessionStep
{
    None,
    WaitingCountryName,
    WaitingNewName,
    WaitingNewFlag,
    OwnerWaitingFlagManage,
    OwnerWaitingDailyTime,
    OwnerWaitingMinuteTime,
    WaitingDeleteConfirm,
    OwnerWaitingSpecialPhoto,
    OwnerWaitingNewDatabase,
    OwnerWaitingAnnounceAll,
    OwnerWaitingAnnouncePrivate,
    OwnerWaitingAnnounceGroup,
    OwnerWaitingVisionSource,
    OwnerWaitingVisionConfirm,
    WaitingRecruitmentRate,
    WaitingTaxRate,
    WaitingTradeAmount,
    OwnerWaitingRoyalDeposit,
    OwnerWaitingRoyalDepositAmount,
    OwnerWaitingRoyalDeduct,
    OwnerWaitingRoyalDeductAmount,
    AttackWaitingGroup,
    AttackWaitingTarget,
    AttackWaitingAttackType,
    AttackWaitingStrategy,
    AttackWaitingTactic,
    AttackWaitingTanks,
    AttackWaitingSoldiers,
    AttackWaitingFighters,
    AttackWaitingBombers,
    AttackWaitingTankModel,
    AttackWaitingPlaneModel,
    AttackWaitingBomberModel,
    AttackWaitingModelAmount,
    AttackWaitingAirStrategy,
    AttackWaitingAirTactic,
    DefenseWaitingGroup,
    DefenseWaitingStrategy,
    DefenseWaitingTactic,
    DefenseWaitingTanks,
    DefenseWaitingSoldiers,
    DefenseWaitingFighters,
    DefenseWaitingModelPct,
    DefenseWaitingTankModel,
    DefenseWaitingPlaneModel,
    NavalDefenseWaitingBoatModel,
    NavalDefenseWaitingSubmarineModel,
    NavalDefenseWaitingBattleshipModel,
    WaitingAllianceName,
    WaitingAllianceFlag,
    LeaderWaitingKickMember,
    TransferWaitingChat,
    TransferWaitingResource,
    TransferWaitingTarget,
    TransferWaitingDuration,
    TransferWaitingAmount,
    TransferWaitingModelAmount,
    DeployWaitingChat,
    DeployWaitingTarget,
    DeployWaitingDuration,
    DeployWaitingFormation,
    DeployWaitingStrategy,
    DeployWaitingTactic,
    DeployWaitingTanks,
    DeployWaitingSoldiers,
    DeployWaitingFighters,
    DeployWaitingBombers,
    DeployWaitingTankModel,
    DeployWaitingPlaneModel,
    DeployWaitingBomberModel,
    DeployJoinWaitingStrategy,
    DeployJoinWaitingTactic,
    DeployJoinWaitingTanks,
    DeployJoinWaitingTankModel,
    DeployJoinWaitingSoldiers,
    DeployJoinWaitingFighters,
    DeployJoinWaitingPlaneModel,
    DeployJoinWaitingBombers,
    DeployJoinWaitingBomberModel,
}

class UserSession
{
    public SessionStep Step { get; set; } = SessionStep.None;
    public long AllianceChatId { get; set; } = 0;
    public long AllianceId { get; set; } = 0;
    public string AllianceName { get; set; } = "";
    public long TransferChatId { get; set; } = 0;
    public long TransferAllianceId { get; set; } = 0;
    public string TransferResourceType { get; set; } = "";
    public long TransferTargetId { get; set; } = 0;
    public int TransferDurationMin { get; set; } = 0;
    public List<string> TransferModelNames { get; set; } = new();
    public List<long> TransferModelCounts { get; set; } = new();
    public List<long> TransferModelAmounts { get; set; } = new();
    public int TransferModelIndex { get; set; } = 0;
    public long VisionDestChatId { get; set; } = 0;
    public long VisionSourceId { get; set; } = 0;
    public long DeployChatId { get; set; } = 0;
    public long DeployAllianceId { get; set; } = 0;
    public string DeployType { get; set; } = "";
    public long DeployTargetId { get; set; } = 0;
    public int DeployDuration { get; set; } = 0;
    public string DeployFormation { get; set; } = "";
    public int DeployStrategy { get; set; } = 1;
    public int DeployTactic { get; set; } = 1;
    public long DeployTanks { get; set; } = 0;
    public long DeploySoldiers { get; set; } = 0;
    public long DeployFighters { get; set; } = 0;
    public long DeployBombers { get; set; } = 0;
    public int DefTankPct { get; set; } = 100;
    public int DefSoldierPct { get; set; } = 100;
    public int DefFighterPct { get; set; } = 100;
    //  – per-model defense & attack tracking
    public string DefenseCurrentCategory { get; set; } = "";
    public List<string> DefenseModelNames { get; set; } = new();
    public List<long> DefenseModelCounts { get; set; } = new();
    public List<int> DefenseModelPcts { get; set; } = new();
    public List<long> DefenseModelAmounts { get; set; } = new();
    public List<long> DefenseModelMinimums { get; set; } = new();
    public List<string> DefenseTankModelNamesFinal { get; set; } = new();
    public List<long> DefenseTankModelAmountsFinal { get; set; } = new();
    public Dictionary<string,long> NavalDefenseBoatsFinal { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string,long> NavalDefenseSubsFinal { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int DefenseModelIndex { get; set; } = 0;

    public string AttackCurrentCategory { get; set; } = "";
    public List<string> AttackModelNames { get; set; } = new();
    public List<long> AttackModelCounts { get; set; } = new();
    public List<long> AttackModelAmounts { get; set; } = new();
    public int AttackModelIndex { get; set; } = 0;
    public List<string> AttackTankModelNamesFinal { get; set; } = new();
    public List<long> AttackTankModelAmountsFinal { get; set; } = new();
    public List<string> AttackPlaneModelNamesFinal { get; set; } = new();
    public List<long> AttackPlaneModelAmountsFinal { get; set; } = new();
    public List<string> AttackBomberModelNamesFinal { get; set; } = new();
    public List<long> AttackBomberModelAmountsFinal { get; set; } = new();

    public string DeployCurrentCategory { get; set; } = "";
    public List<string> DeployModelNames { get; set; } = new();
    public List<long> DeployModelCounts { get; set; } = new();
    public List<long> DeployModelAmounts { get; set; } = new();
    public int DeployModelIndex { get; set; } = 0;
    // Exact per-model composition selected for either creating or joining a deployment.
    public List<string> DeployTankModelNamesFinal { get; set; } = new();
    public List<long> DeployTankModelAmountsFinal { get; set; } = new();
    public List<string> DeployPlaneModelNamesFinal { get; set; } = new();
    public List<long> DeployPlaneModelAmountsFinal { get; set; } = new();
    public List<string> DeployBomberModelNamesFinal { get; set; } = new();
    public List<long> DeployBomberModelAmountsFinal { get; set; } = new();

    public Faction Faction { get; set; }
    public string FactionStr { get; set; } = "";
    public long PromptChatId { get; set; }
    public int PromptMsgId { get; set; }
    public long ChatId { get; set; }
    public long AttackTargetId { get; set; } = 0;
    public long AttackChatId { get; set; } = 0;
    public int AttackStrategy { get; set; } = 0;
    public int AttackTactic { get; set; } = 0;
    public long AttackTanks { get; set; } = 0;
    public long AttackSoldiers { get; set; } = 0;
    public long AttackFighters { get; set; } = 0;
    public long AttackBombers { get; set; } = 0;
    public long AttackBoats { get; set; } = 0;
    public long AttackSubmarines { get; set; } = 0;
    public long AttackBattleships { get; set; } = 0;
    public int AttackAirStrategy { get; set; } = 0;
    public int AttackAirTactic { get; set; } = 0;
    public bool AttackIsNaval { get; set; } = false;
    public int AttackNavalStrategy { get; set; } = 0;
    public int AttackNavalTactic { get; set; } = 0;
    public long DefenseTanks { get; set; } = 0;
    public long DefenseSoldiers { get; set; } = 0;
    // Temporary plane total while the per-model naval defense flow is in progress.
    public long DefenseFighters { get; set; } = 0;
    public int DefenseStrategy { get; set; } = 1;
    public int DefenseTactic { get; set; } = 1;
    public int AnnounceCount { get; set; } = 0;
    public long DeployJoinId { get; set; } = 0;
    public int DeployJoinStrategy { get; set; } = 1;
    public int DeployJoinTactic { get; set; } = 1;
    public long DeployJoinTanks { get; set; } = 0;
    public long DeployJoinSoldiers { get; set; } = 0;
    public long DeployJoinFighters { get; set; } = 0;
    public long DeployJoinBombers { get; set; } = 0;
}

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

// ============================================================
//  Battle orchestration — 1939 ground and air engine
// ============================================================
partial class Program
{
    // Kept lazy enough for offline regression commands; normal bot startup validates it in Main.
    static readonly string BOT_TOKEN = Environment.GetEnvironmentVariable("BOT_TOKEN") ?? "";
    const long OWNER_ID = 8248899977L;
    static TelegramBotClient bot = null!;
    static readonly ConcurrentDictionary<long, UserSession> sessions = new();
    static readonly Random rng = new();
    static readonly ConcurrentDictionary<long, SemaphoreSlim> userLocks = new();
    static readonly ConcurrentDictionary<(long ChatId, long OwnerId), SemaphoreSlim> countryMutationLocks = new();
    static readonly HashSet<int> processedUpdates = new();
    static readonly object processedLock = new();

    enum SpamDecisionKind { Allow, Drop, Warn }
    readonly record struct SpamDecision(SpamDecisionKind Kind,long UserId,long UntilMs,string Reason);
    readonly record struct SpamEvent(long AtMs,string Fingerprint,bool InvalidCallback);
    sealed class SpamState
    {
        public readonly object Gate=new();
        public readonly Queue<SpamEvent> Events=new();
        public bool Loaded;
        public long RestrictUntilMs;
        public int Level;
        public long LastViolationMs;
        public long LastWarningMs;
        public string LastFingerprint="";
        public long LastFingerprintAtMs;
        public int Dropped;
        public string Reason="";
    }
    static readonly ConcurrentDictionary<long,SpamState> spamStates=new();
    static readonly HashSet<string> knownCallbackActions=new(StringComparer.Ordinal)
    {
        "cancel","faction","eq_details","dep_info","build_menu","upgrade","timing","tank_info","tank_buy",
        "plane_info","plane_buy","bomber_info","bomber_buy","aa_info","aa_buy","defense_status","defense_tactic",
        "defense_tactic_select","defense_set","naval_defense","naval_defense_strategy","naval_defense_tactic","naval_cancel",
        "naval_locked","defense_pct","defense_model_pct","boat_info","boat_buy","sub_info","sub_buy","battleship_info",
        "battleship_buy","battleship_repair","battleship_repair_quote","battleship_repair_unit","battleship_scrap_menu",
        "battleship_scrap","battleship_scrap_confirm","airdef_strategy","airdef_tactic","attack_group","attack_target",
        "revenge","attack_type","attack_strategy","attack_tactic","attack_air_strategy","attack_air_tactic",
        "attack_naval_strategy","attack_naval_tactic"
    };

    static bool IsKnownCallbackData(string data)
    {
        if(data.StartsWith("adm:",StringComparison.Ordinal)||data.StartsWith("spam_admin:",StringComparison.Ordinal)||
           data.StartsWith("ally_",StringComparison.Ordinal)||data.StartsWith("tf_",StringComparison.Ordinal)||
           data.StartsWith("dep_",StringComparison.Ordinal))return true;
        string action=data.Split(':',2)[0];return knownCallbackActions.Contains(action);
    }

    static string SpamFingerprint(Update update,out long userId,out long chatId,out bool invalidCallback,out bool callback)
    {
        userId=0;chatId=0;invalidCallback=false;callback=false;
        if(update.CallbackQuery!=null)
        {
            callback=true;userId=update.CallbackQuery.From.Id;chatId=update.CallbackQuery.Message?.Chat.Id??userId;
            string data=update.CallbackQuery.Data??"";invalidCallback=!IsKnownCallbackData(data);
            int messageId=update.CallbackQuery.Message?.MessageId??0;
            return $"cb:{chatId}:{messageId}:{data}";
        }
        if(update.Message?.From!=null)
        {
            userId=update.Message.From.Id;chatId=update.Message.Chat.Id;
            string value=(update.Message.Text??update.Message.Caption??update.Message.Type.ToString()).Trim().Replace('\n',' ');
            if(value.Length>80)value=value[..80];
            return $"msg:{chatId}:{value}";
        }
        return "";
    }

    static SpamDecision EvaluateSpam(Update update)
    {
        string fingerprint=SpamFingerprint(update,out long userId,out long chatId,out bool invalidCallback,out bool callback);
        if(userId==0||userId==OWNER_ID||fingerprint.Length==0)return new(SpamDecisionKind.Allow,userId,0,"");
        var state=spamStates.GetOrAdd(userId,_=>new SpamState());
        long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        lock(state.Gate)
        {
            if(!state.Loaded)
            {
                state.Loaded=true;
                try
                {
                    var saved=Database.GetSpamRestriction(userId);
                    if(saved!=null)
                    {
                        state.RestrictUntilMs=saved.UntilMs;state.Level=saved.Level;state.LastViolationMs=saved.UpdatedAtMs;
                        state.Dropped=saved.DroppedCount;state.Reason=saved.Reason;
                    }
                }
                catch { }
            }
            if(state.RestrictUntilMs>now)
            {
                state.Dropped++;
                if(state.Dropped%25==0)
                    try{Database.SaveSpamRestriction(new SpamRestrictionInfo{UserId=userId,ChatId=chatId,UntilMs=state.RestrictUntilMs,
                        Level=state.Level,Reason=state.Reason,LastFingerprint=state.LastFingerprint,DroppedCount=state.Dropped,UpdatedAtMs=state.LastViolationMs});}catch{}
                bool warn=now-state.LastWarningMs>=60_000;
                if(warn)state.LastWarningMs=now;
                return new(warn?SpamDecisionKind.Warn:SpamDecisionKind.Drop,userId,state.RestrictUntilMs,"محدودیت ضداسپم فعال است");
            }

            while(state.Events.Count>0&&now-state.Events.Peek().AtMs>60_000)state.Events.Dequeue();
            bool exactFast=callback&&state.LastFingerprint==fingerprint&&now-state.LastFingerprintAtMs<=1_200;
            state.LastFingerprint=fingerprint;state.LastFingerprintAtMs=now;
            state.Events.Enqueue(new SpamEvent(now,fingerprint,invalidCallback));
            var tenSeconds=state.Events.Where(x=>now-x.AtMs<=10_000).ToList();
            int sameTen=tenSeconds.Count(x=>x.Fingerprint==fingerprint);
            int invalidTen=tenSeconds.Count(x=>x.InvalidCallback);
            string? violation=null;
            if(invalidTen>=8)violation="دکمه‌های نامعتبر تکراری";
            else if(exactFast&&sameTen>=7)violation="فشردن پشت‌سرهم یک دکمه";
            else if(tenSeconds.Count>=30&&tenSeconds.GroupBy(x=>x.Fingerprint).Max(x=>x.Count())>=15)violation="درخواست تکراری سنگین";
            else if(tenSeconds.Count>=60)violation="حجم غیرعادی درخواست";
            else if(state.Events.Count>=180)violation="اسپم مداوم یک‌دقیقه‌ای";

            if(violation==null)
            {
                if(exactFast||invalidCallback)
                {
                    state.Dropped++;
                    return new(SpamDecisionKind.Drop,userId,0,"");
                }
                return new(SpamDecisionKind.Allow,userId,0,"");
            }

            state.Level=now-state.LastViolationMs>3_600_000?1:Math.Min(3,state.Level+1);
            state.LastViolationMs=now;
            long duration=state.Level switch{1=>15_000,2=>120_000,_=>1_800_000};
            state.RestrictUntilMs=now+duration;state.LastWarningMs=now;state.Dropped++;state.Reason=violation;
            string storedFingerprint=fingerprint.Length>120?fingerprint[..120]:fingerprint;
            try{Database.SaveSpamRestriction(new SpamRestrictionInfo{UserId=userId,ChatId=chatId,UntilMs=state.RestrictUntilMs,
                Level=state.Level,Reason=violation,LastFingerprint=storedFingerprint,DroppedCount=state.Dropped,UpdatedAtMs=now});}catch{}
            Console.WriteLine($"[SPAM BLOCK] user={userId} chat={chatId} level={state.Level} until={state.RestrictUntilMs} reason={violation}");
            return new(SpamDecisionKind.Warn,userId,state.RestrictUntilMs,violation);
        }
    }

    static void ClearSpamState(long userId)
    {
        spamStates.TryRemove(userId,out _);Database.ClearSpamRestriction(userId);
    }

    static void RestrictSpamUser(long userId,long chatId,TimeSpan duration,string reason)
    {
        long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();long until=now+(long)duration.TotalMilliseconds;
        var state=spamStates.GetOrAdd(userId,_=>new SpamState());
        lock(state.Gate){state.Loaded=true;state.RestrictUntilMs=until;state.Level=3;state.LastViolationMs=now;state.LastWarningMs=0;state.Reason=reason;
            Database.SaveSpamRestriction(new SpamRestrictionInfo{UserId=userId,ChatId=chatId,UntilMs=until,Level=3,Reason=reason,
                LastFingerprint=state.LastFingerprint,DroppedCount=state.Dropped,UpdatedAtMs=now});}
    }

    sealed class MsgContext { public long UserId; public long ChatId; public int MessageId; public bool Marked; }
    static readonly AsyncLocal<MsgContext?> incomingCtx = new();

    static void MarkIncomingHandled()
    {
        var c = incomingCtx.Value;
        if (c != null && !c.Marked)
        {
            c.Marked = true;
            Database.MarkPlayerActive(c.UserId);
            ScheduleDelete(c.ChatId, c.MessageId, 30);
        }
    }

    static Timer? assetUpdateTimer;
    static Timer? transferTimer;
    static readonly SemaphoreSlim transferProcessorLock = new(1, 1);
    static readonly SemaphoreSlim deploymentProcessorLock = new(1, 1);
    static readonly SemaphoreSlim navalProcessorLock = new(1, 1);
    static readonly JsonSerializerOptions BattleJsonOptions = new()
    {
        IncludeFields = true,
        PropertyNameCaseInsensitive = true
    };
    static int databaseMaintenanceRunning = 0;
    static int activeUpdateHandlers = 0;
    static int assetUpdateRunning = 0;
    static DateTime lastAssetRunUtc = DateTime.MinValue;

    static readonly ConcurrentDictionary<string, int> attackCounts = new();
    static int MAX_ATTACKS_PER_UPDATE = 8;
    const int MAX_NAVAL_ATTACKS_PER_UPDATE = 8;
    static readonly ConcurrentDictionary<string, int> navalAttackCounts = new();
    static readonly ConcurrentDictionary<string, int> transferCounts = new();
    static int MAX_TRANSFERS_PER_UPDATE = 2;
    static DateTime lastAssetUpdateAt = DateTime.MinValue;
    static int ATTACK_LOCK_MINUTES = 30;
    static double SHIELD_HOURS = 48.0;

    static string AtkKey(long chatId, long ownerId) => $"{chatId}:{ownerId}";
    static int GetAttackCount(long chatId, long ownerId) => attackCounts.TryGetValue(AtkKey(chatId, ownerId), out var v) ? v : 0;
    static int IncAttackCount(long chatId, long ownerId) => attackCounts.AddOrUpdate(AtkKey(chatId, ownerId), 1, (_, v) => v + 1);
    // Starting an attack forfeits every kind of protection held by the attacker.
    // Outgoing attacks never add a hit toward the attacker's own five-hit shield.
    internal static void BreakAttackerShieldOnAttack(long attackerId,long chatId)
    {
        Database.ClearAttackShield(attackerId,chatId);
        Database.SetShieldExemption(attackerId,chatId); // also ends the new-country shield
    }
    internal static void ApplyCompletedAttackShieldRules(long attackerId,long defenderId,long chatId,bool fullExemption)
    {
        BreakAttackerShieldOnAttack(attackerId,chatId);
        if(!fullExemption)Database.AddAttackShieldHit(defenderId,chatId);
    }
    static int GetNavalAttackCount(long chatId,long ownerId) => navalAttackCounts.TryGetValue(AtkKey(chatId,ownerId),out var v)?v:0;
    static int IncNavalAttackCount(long chatId,long ownerId) => navalAttackCounts.AddOrUpdate(AtkKey(chatId,ownerId),1,(_,v)=>v+1);
    static string TfKey(long chatId, long ownerId) => $"{chatId}:{ownerId}";
    static int GetTransferCount(long chatId, long ownerId) => transferCounts.TryGetValue(TfKey(chatId, ownerId), out var v) ? v : 0;
    static int IncTransferCount(long chatId, long ownerId) => transferCounts.AddOrUpdate(TfKey(chatId, ownerId), 1, (_, v) => v + 1);

    static string UpdateMode = "daily";
    static int UpdateValue = 1200;
    static string SpecialPhotoFileId = "";

    static readonly int[] FactoryUpgradeCost = { 0, 5, 12, 30, 80 };
    static readonly int[] PortUpgradeCost = { 0, 13, 25, 50, 75 };
    static readonly int[] MineUpgradeCost = { 0, 5, 12, 30, 80, 0, 0 };
    // Levels 6 and 7 are premium upgrades. These prices are keyed by TARGET level;
    // existing level-6/7 countries are never migrated, charged again, or downgraded.
    static readonly IReadOnlyDictionary<int, int> MineRoyalUpgradeCost =
        new Dictionary<int, int> { [6] = 5, [7] = 10 };
    static int MineRoyalCostForTargetLevel(int targetLevel) =>
        MineRoyalUpgradeCost.TryGetValue(targetLevel, out int cost) ? cost : 0;
    static readonly double[] FactoryIncome = { 0, 1, 2, 5, 15, 30 };
    static readonly double[] PortIncome = { 0, 1, 2, 4, 8, 15 };
    static readonly double[] MineIncome = { 0, 1, 2, 5, 15, 30, 40, 50 };

    const string MsgNoCountryGuide = "❌ شما در این گپ کشوری ندارید.\nℹ️ با نوشتن دستور «راهنما» می‌توانید راهنمای بازی را ببینید. دستور دریافت کشور «انتخاب کشور» است.";

    // ============================================================
    //  متن راهنمای کامل — FIX(4)
    //  در گروه و پیوی یکسان استفاده می‌شود.
    // ============================================================
        const string HelpText =
        "📘 <b>راهنمای کامل آلیس</b>\n" +
        "برای اجرای هر بخش، فقط کافی است دستور مربوطه را (در گروه) بنویسید.\n" +
        "بعضی بخش‌ها (حمله، ترنسفر، صف‌آرایی، وضعیت دفاع) برای تنظیم دقیق به <b>پیوی ربات</b> منتقل می‌شوند.\n" +
        "برای لغو هر عملیات نیمه‌کاره، کلمهٔ «<b>لغو</b>» را بنویسید.\n" +
        "──────────────\n\n" +

        "🌍 <b>شروع و مدیریت کشور</b>\n" +
        "• <b>انتخاب کشور</b> — ساخت کشور جدید (انتخاب فکشن 🇺🇸/☭/⚫ + نام).\n" +
        "  ❌ نام‌های مشابه بالای 90% ممنوع: «این نام خیلی شبیه به نام موجود است!!»\n" +
        "• <b>دارایی</b> (یا «کشورم») — مشاهدهٔ کامل اقتصادی، نظامی و دریایی.\n" +
        "• <b>مان پاور</b> — قدرت کل + تفکیک عوامل.\n" +
        "• <b>تغییر اسم</b> — تغییر نام کشور (بررسی شباهت 90%).\n" +
        "• <b>تغییر پرچم</b> — ارسال عکس.\n" +
        "• <b>انصراف</b> — حذف کامل کشور (۲۴ ساعت قفل ساخت مجدد).\n\n" +

        "🏗 <b>اقتصاد و توسعه</b>\n" +
        "• <b>اقتصاد</b> / ساختمان — ارتقای 🏭 کارخانه، ⚓ بندر و ⛏️ معدن.\n" +
        "• بندر سطح 4 لازم برای نبردناو (Bismarck/Iowa/Sovetsky Soyuz) حداکثر 3 عدد.\n" +
        "• <b>مالیات</b> ۰-۱۰۰٪، <b>آموزش سرباز</b> ۰-۱۰، <b>ترید</b> 1 رویال=10K پول.\n\n" +

        "⚔️ <b>ساخت ارتش — چندمدلی</b>\n" +
        "• هر کشور چندین مدل تجهیزات دارد و حتی با تغییر فکشن حفظ می‌شود.\n" +
        "• <b>ساخت تانک</b> — M2 Medium 🇺🇸 / T-28 ☭ / Panzer III ⚫ (هر ۵ عدد).\n" +
        "• <b>ساخت هواپیما</b> — P-36 / I-16 / Bf 109 + بمب‌افکن B-17 / DB-3 / He 111.\n" +
        "• <b>پدافند</b> — توپ 76mm ضد هوایی.\n" +
        "• در حمله و دفاع می‌توانید برای هر مدل جداگانه تعداد / درصد تعیین کنید.\n" +
        "• موتور جنگ هر مدل را با مشخصات واقعی ۱۹۳۹، مهمات و سوخت داخلی همان مدل شبیه‌سازی می‌کند.\n\n" +

        "⚓ <b>نیروی دریایی — ناوگان</b>\n" +
        "• دستور: <b>خرید ناو / خرید کشتی / خرید قایق / نیروی دریایی / ناوگان</b>\n" +
        "  🇩🇪 S-Boot 38–41 گره — هر 5: 2K پول+1K آهن\n" +
        "  🇺🇸 PT Boat 40–45 گره — هر 5: 3K+1.5K\n" +
        "  ☭ G-5 50–53 گره — هر 5: 2.5K+1.5K\n" +
        "• زیردریایی: Type VIIC 17.7/7.6 — 10K+5K | Gato 21/9 — 10K+5K | S-class 13–14/7–8 — 8K+4K\n" +
        "• نبردناو: Bismarck 30 گره 2092 خدمه 8x380mm — 50K+30K | Iowa 28 گره 1800 خدمه 9x406mm — 50K+40K | Sovetsky Soyuz 23 گره 1220 خدمه 12x305mm — 45K+25K (پورت>=4 max3)\n" +
        "• انتقال نبردناو: <b>نمیتوانید به این کشور نبردناو ترنسفر کنید، تعداد نبرد ناو: 3</b>\n" +
        "• <b>لغو لشکرکشی دریایی</b> در پیوی — انتخاب عملیات و بازگشت فوری کل ناوگان بدون تلفات.\n\n" +

        "🗡 <b>حمله زمینی و هوایی — موتور ۱۹۳۹</b>\n" +
        "• میدان هر نبرد ۴۰×۴۰ کیلومتر و حداکثر زمان عملیات ۲۴ ساعت است.\n" +
        "• زمین و آب‌وهوا پس از ثبت فرمان‌ها به‌صورت منسجم تولید می‌شوند.\n" +
        "• فرماندهان بر اساس استراتژی، تاکتیک، اطلاعات کشف‌شده و وضعیت واقعی میدان تصمیم می‌گیرند.\n" +
        "• پیروزی سنگین: بیش از ۳۵km پیشروی مؤثر با بازگشت حداقل ۵۰۰۰ سرباز و ۵۰ تانک سالم.\n" +
        "• <b>لیست نبردهای در جریان</b> در پیوی — نمایش پیشرفت گرفتن یا از دست دادن شهر.\n\n" +

        "🛡 <b>دفاع — چندمدلی و دریایی</b>\n" +
        "• <b>وضعیت دفاع</b> در پیوی: درصد برای هر مدل تانک/جنگنده/قایق/زیر جداگانه (20-100%). حداقل 20% همیشه در دفاع.\n" +
        "• دفاع دریایی: قایق و زیردریایی per-model.\n\n" +

        "🤝 <b>اتحادها</b>\n" +
        "• <b>ساخت اتحاد</b> (شباهت 90% چک)، <b>ایجاد درخواست عضویت</b> ریپلای، <b>وضعیت اتحاد</b>، <b>لیست اتحاد ها</b>، <b>حذف N</b>، <b>خروج</b>، <b>انحلال</b>.\n\n" +

        "🚚 <b>عملیات مشترک — ترنسفر و صف‌آرایی</b>\n" +
        "• <b>ترنسفر</b> — پول/آهن/سرباز/تانک/جنگنده/بمب‌افکن/قایق/زیر/نبردناو به هم‌اتحادی (پیوی). حفظ مدل حتی با تغییر فکشن. هر مدل مقدار جداگانه. نبردناو max3.\n" +
        "• <b>صف‌آرایی تهاجمی/دفاعی</b> فعال است و نیروهای چند کشور در موتور جدید به‌صورت مشارکت‌کننده مستقل محاسبه می‌شوند.\n" +
        "• نیروهای دفاعی در دارایی دیده نمی‌شوند، فقط در <b>جزئیات نظامی → اطلاعات نیروهای صف آرایی</b> گروه‌بندی فکشن با مجموع. پیام گروه فقط مشارکت‌کنندگان + 🎯 استراتژی: X | تاکتیک: Y. پس از join پیام پین ویرایش می‌شود.\n" +
        "• <b>اعزام نیرو</b> / دکمه ⚔️ مشارکت. <b>لغو صف آرایی</b> → آنپین+حذف.\n" +
        "• قبل از حمله/ترنسفر/صف‌آرایی یک‌بار در پیوی استارت کنید. «لغو» برای خروج.\n\n" +

        "🏆 <b>لیدربورد شبانه</b>\n" +
        "• هر شب 22:00 تهران +30 ثانیه، سه بورد عمومی ارسال می‌شود: برترین مان‌پاور پلیرها، برترین گروه‌ها از نظر تعداد پلیر و برترین گروه‌ها از نظر مجموع مان‌پاور.\n\n" +

        "──────────────\n📢 @alice_safe_house1";

    // ============================================================
    //  منطقه‌زمانی تهران — مقاوم و مستقل از تنظیمات سرور
    // ============================================================
    static readonly TimeSpan TehranOffset = TimeSpan.FromHours(3.5);
    static DateTime GetTehranNow()
    {
        return DateTime.UtcNow.AddHours(3.5);
    }

    static async Task RecoverPersistedBattleJobs(CancellationToken ct)
    {
        foreach (var job in Database.GetRecoverableBattleJobs())
        {
            if (!job.JobType.Equals("Direct", StringComparison.OrdinalIgnoreCase))
            {
                if (job.JobType.Equals("Deployment", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var deploymentContext = JsonSerializer.Deserialize<BattleJobContext>(job.ContextJson, BattleJsonOptions);
                        if (deploymentContext != null && Database.GetDeploymentById(deploymentContext.DeploymentId) == null)
                            Database.UpdateBattleJob(job.BattleId, "Completed", job.ResultJson);
                        else
                            Database.UpdateBattleJob(job.BattleId, "Pending", job.ResultJson);
                    }
                    catch (Exception ex) { Database.UpdateBattleJob(job.BattleId, "Pending", error: ex.Message); }
                }
                continue;
            }
            try
            {
                var request = JsonSerializer.Deserialize<BattleRequest>(job.RequestJson, BattleJsonOptions)
                    ?? throw new InvalidOperationException("Stored request is invalid.");
                var context = JsonSerializer.Deserialize<BattleJobContext>(job.ContextJson, BattleJsonOptions)
                    ?? throw new InvalidOperationException("Stored context is invalid.");
                BattleResult result;
                if (!string.IsNullOrWhiteSpace(job.ResultJson))
                    result = JsonSerializer.Deserialize<BattleResult>(job.ResultJson, BattleJsonOptions)
                        ?? throw new InvalidOperationException("Stored result is invalid.");
                else
                {
                    Database.UpdateBattleJob(job.BattleId, "Running");
                    result = await BattleExecutionScheduler.EnqueueAsync(request, ct);
                    Database.UpdateBattleJob(job.BattleId, "Resolved",
                        JsonSerializer.Serialize(result, BattleJsonOptions));
                }

                var attacker = Database.GetCountry(context.AttackerId, context.ChatId);
                var defender = Database.GetCountry(context.DefenderId, context.ChatId);
                if (attacker == null || defender == null)
                {
                    Database.UpdateBattleJob(job.BattleId, "Failed", error: "Country no longer exists.");
                    continue;
                }
                var ownDefense = request.Defenders.FirstOrDefault(x => x.OwnerId == context.DefenderId)
                    ?? request.Defenders.First();
                var deploymentParticipants = request.Defenders.Where(x => !ReferenceEquals(x, ownDefense)).ToList();
                var defensiveDeployments = context.DefensiveDeploymentIds
                    .Select(Database.GetDeploymentById).Where(x => x != null).Cast<Deployment>().ToList();
                bool applied = ApplyDirectBattleLosses(job.BattleId, attacker, defender, ownDefense,
                    deploymentParticipants, defensiveDeployments, result);
                if (applied)
                {
                    try { Database.SaveBattleResult(request, result); } catch { }
                    IncAttackCount(context.ChatId, context.AttackerId);
                    string today = DateTime.UtcNow.AddHours(3.5).ToString("yyyy-MM-dd");
                    Database.IncDailyDefendCount(context.DefenderId, today);
                    Database.SetAttackerFlag(context.AttackerId, today);
                    ApplyCompletedAttackShieldRules(context.AttackerId,context.DefenderId,context.ChatId,
                        Database.HasGroupLockExemption(context.ChatId));
                    try { await SendPermanent(context.AttackerId, result.AttackerReport, ct: ct); } catch { }
                    try { await SendPermanent(context.DefenderId, result.DefenderReport, ct: ct); } catch { }
                    try { await SendPermanent(context.ChatId, result.GroupAnnouncement, ct: ct); } catch { }
                    await ProcessStrategicBattleOutcome(context.AttackerId, context.DefenderId,
                        context.ChatId, result, ct);
                }
                Database.UpdateBattleJob(job.BattleId, "Completed",
                    JsonSerializer.Serialize(result, BattleJsonOptions));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BATTLE RECOVERY ERR] id={job.BattleId}: {ex}");
                Database.UpdateBattleJob(job.BattleId, "Pending", error: ex.Message);
            }
        }
    }

    static async Task Main(string[] args)
    {
        // خودآزمایی موتور نبرد بدون راه‌اندازی ربات: dotnet run -- selftest [تعداد seed]
        if (args.Length > 0 && args[0].Equals("selftest", StringComparison.OrdinalIgnoreCase))
        {
            int seeds = args.Length > 1 && int.TryParse(args[1], out int s)
                ? Math.Clamp(s, 1, 200) : 20;
            WarEngine.RunSelfTest(seeds);
            return;
        }
        if (args.Length > 0 && args[0].Equals("navaltest", StringComparison.OrdinalIgnoreCase))
        {
            NavalRegressionTests.Run();
            return;
        }
        if (args.Length > 0 && args[0].Equals("attacktest", StringComparison.OrdinalIgnoreCase))
        {
            AttackSelectionRegressionTests.Run();
            return;
        }
        if (args.Length > 0 && args[0].Equals("alltests", StringComparison.OrdinalIgnoreCase))
        {
            NavalRegressionTests.Run();
            EconomyRegressionTests.Run();
            AttackSelectionRegressionTests.Run();
            StrategicBattleRegressionTests.Run();
            SiegeRegressionTests.Run();
            GroupLifecycleRegressionTests.Run();
            return;
        }
        if (string.IsNullOrWhiteSpace(BOT_TOKEN))
            throw new InvalidOperationException("BOT_TOKEN environment variable is required.");
        Database.Init();
        Console.WriteLine($"[SIEGE INTEGRITY] {Database.RepairSiegeIntegrity()}");
        Console.WriteLine($"[DEPLOYMENT INTEGRITY] {Database.RepairDeploymentIntegrity()}");
        Database.InitNavalV2();
        Console.WriteLine($"[NAVAL INTEGRITY] {Database.RepairPendingNavalOperations()}");
        Database.InitActivity();
        Database.InitAdminPanel(OWNER_ID);
        LoadSettings();
        // Proxy support for IR filtering – if TELEGRAM_PROXY env or setting exists, use it
        try
        {
            string proxyUrl = Environment.GetEnvironmentVariable("TELEGRAM_PROXY") ?? Database.GetSetting("ProxyUrl");
            if (!string.IsNullOrWhiteSpace(proxyUrl))
            {
                var proxy = new System.Net.WebProxy(proxyUrl);
                var httpClient = new HttpClient(new HttpClientHandler { Proxy = proxy, UseProxy = true });
                bot = new TelegramBotClient(BOT_TOKEN, httpClient);
                Console.WriteLine("[BOT] Using configured proxy");
            }
            else
            {
                bot = new TelegramBotClient(BOT_TOKEN);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PROXY ERR] {ex.Message} – falling back to direct");
            bot = new TelegramBotClient(BOT_TOKEN);
        }
        Console.WriteLine("Bot starting...");
        using var cts = new CancellationTokenSource();
        await RecoverPersistedBattleJobs(cts.Token);
        bot.StartReceiving(
            updateHandler: HandleUpdateAsync,
            pollingErrorHandler: HandlePollingErrorAsync,
            receiverOptions: new ReceiverOptions { AllowedUpdates = Array.Empty<UpdateType>() },
            cancellationToken: cts.Token
        );
        Console.WriteLine("Bot is running...");
        // ArriveAtMs is an absolute persisted timestamp. Any operation that arrived while
        // the bot was offline is resolved immediately on startup; settlement is idempotent.
        try { await ProcessNavalInvasions(cts.Token); }
        catch (Exception ex) { Console.WriteLine($"[NAVAL STARTUP RECOVERY ERR] {ex}"); }
        StartAssetUpdateTimer();
        StartTransferTimer();
        StartActivityStatsTimer();
        StartLeaderboardTimer();
        await Task.Delay(-1);
    }

    static async Task<(bool Success, string Error)> RestoreDatabaseSafely(
        string uploadedPath,
        CancellationToken ct)
    {
        if (!Database.ValidateDatabaseFile(uploadedPath, out string validationError))
            return (false, $"فایل دیتابیس معتبر نیست: {validationError}");

        if (Interlocked.CompareExchange(ref databaseMaintenanceRunning, 1, 0) != 0)
            return (false, "عملیات نگهداری دیگری در حال اجراست.");

        bool transferLocked = false;
        bool deploymentLocked = false;
        bool navalLocked = false;
        string rollbackPath = $"gamedata_pre_restore_{DateTime.UtcNow:yyyyMMdd_HHmmss}.db";

        assetUpdateTimer?.Dispose();
        transferTimer?.Dispose();
        activityStatsTimer?.Dispose();
        leaderboardTimer?.Dispose();

        try
        {
            DateTime waitUntil = DateTime.UtcNow.AddMinutes(2);
            while ((Volatile.Read(ref activeUpdateHandlers) > 1 ||
                    Volatile.Read(ref assetUpdateRunning) != 0) &&
                   DateTime.UtcNow < waitUntil)
            {
                await Task.Delay(100, ct);
            }
            if (Volatile.Read(ref activeUpdateHandlers) > 1 ||
                Volatile.Read(ref assetUpdateRunning) != 0)
            {
                return (false, "ربات هنوز در حال پردازش عملیات دیگری است؛ کمی بعد دوباره تلاش کنید.");
            }

            await transferProcessorLock.WaitAsync(ct);
            transferLocked = true;
            await deploymentProcessorLock.WaitAsync(ct);
            deploymentLocked = true;
            await navalProcessorLock.WaitAsync(ct);
            navalLocked = true;

            Database.CreateConsistentBackup(rollbackPath);
            Database.CheckpointAndClearPools();

            System.IO.File.Move(uploadedPath, "gamedata.db", true);
            TryDeleteSqliteSidecar("gamedata.db-wal");
            TryDeleteSqliteSidecar("gamedata.db-shm");

            try
            {
                Database.Init();
                Console.WriteLine($"[SIEGE INTEGRITY] {Database.RepairSiegeIntegrity()}");
                Console.WriteLine($"[DEPLOYMENT INTEGRITY] {Database.RepairDeploymentIntegrity()}");
                Database.InitNavalV2();
                Console.WriteLine($"[NAVAL INTEGRITY] {Database.RepairPendingNavalOperations()}");
                Database.InitActivity();
                Database.InitAdminPanel(OWNER_ID);
                LoadSettings();
            }
            catch
            {
                SqliteConnection.ClearAllPools();
                System.IO.File.Copy(rollbackPath, "gamedata.db", true);
                TryDeleteSqliteSidecar("gamedata.db-wal");
                TryDeleteSqliteSidecar("gamedata.db-shm");
                Database.Init();
                Console.WriteLine($"[SIEGE INTEGRITY] {Database.RepairSiegeIntegrity()}");
                Console.WriteLine($"[DEPLOYMENT INTEGRITY] {Database.RepairDeploymentIntegrity()}");
                Database.InitNavalV2();
                Console.WriteLine($"[NAVAL INTEGRITY] {Database.RepairPendingNavalOperations()}");
                Database.InitActivity();
                Database.InitAdminPanel(OWNER_ID);
                LoadSettings();
                throw;
            }

            return (true, "");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return (false, "عملیات بازیابی لغو شد.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
        finally
        {
            if (navalLocked) navalProcessorLock.Release();
            if (deploymentLocked) deploymentProcessorLock.Release();
            if (transferLocked) transferProcessorLock.Release();
            Volatile.Write(ref databaseMaintenanceRunning, 0);
            try { await ProcessNavalInvasions(CancellationToken.None); }
            catch (Exception ex) { Console.WriteLine($"[NAVAL RESTORE RECOVERY ERR] {ex}"); }
            StartAssetUpdateTimer();
            StartTransferTimer();
            StartActivityStatsTimer();
            StartLeaderboardTimer();
        }
    }

    static void TryDeleteSqliteSidecar(string path)
    {
        try
        {
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SQLITE SIDECAR CLEANUP ERR] {path}: {ex.Message}");
        }
    }

    static void LoadSettings()
    {
        var mode = Database.GetSetting("UpdateMode");
        var val = Database.GetSetting("UpdateValue");
        var special = Database.GetSetting("SpecialPhotoFileId");
        if (!string.IsNullOrEmpty(mode)) UpdateMode = mode;
        if (TryParseInt(val, out int v)) UpdateValue = v;
        if (!string.IsNullOrEmpty(special)) SpecialPhotoFileId = special;
    }

    static string NormalizeDigits(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.Trim())
        {
            if (ch >= '\u06F0' && ch <= '\u06F9') sb.Append((char)('0' + (ch - '\u06F0')));
            else if (ch >= '\u0660' && ch <= '\u0669') sb.Append((char)('0' + (ch - '\u0660')));
            else if (ch == '\u066C' || ch == ',' || ch == '\u060C' || ch == ' ' || ch == '\u200c') { }
            else sb.Append(ch);
        }
        return sb.ToString();
    }

    static bool TryParseLong(string? s, out long v) =>
        long.TryParse(NormalizeDigits(s), NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
    static bool TryParseInt(string? s, out int v) =>
        int.TryParse(NormalizeDigits(s), NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
    static string InventoryLine(long amount) =>
        amount > 0 ? $"موجودی: {amount:N0}" : "⚠️ موجودی نداری";

    static bool HasAvailableForces(
        Country country,
        long tanks,
        long soldiers,
        long fighters,
        long bombers) =>
        tanks >= 0 && soldiers >= 0 && fighters >= 0 && bombers >= 0 &&
        country.Tanks >= tanks &&
        country.Soldiers >= soldiers &&
        country.Planes >= fighters &&
        country.Bombers >= bombers;

    static string AvailableForcesText(Country country) =>
        $"🛡 تانک: {country.Tanks:N0}\n" +
        $"🪖 سرباز: {country.Soldiers:N0}\n" +
        $"✈️ جنگنده: {country.Planes:N0}\n" +
        $"🛩 بمب‌افکن: {country.Bombers:N0}";

    static long GetCountryResourceAmount(Country country, string resourceType) => resourceType switch
    {
        "money" => country.Money,
        "iron" => country.Iron,
        "soldiers" => country.Soldiers,
        "tanks" => country.Tanks,
        "planes" => country.Planes,
        "bombers" => country.Bombers,
        "boats" => country.Boats,
        "submarines" => country.Submarines,
        "battleships" => country.Battleships,
        _ => 0
    };

    static async Task<bool> TryCreateTransfersSafely(
        long senderId,
        long chatId,
        long allianceId,
        long receiverId,
        string resourceType,
        IReadOnlyList<(string ModelName, long Amount)> shipments,
        long arriveAtMs,
        CancellationToken ct)
    {
        if(!Database.IsBotGroupActive(chatId))return false;
        // Receiver is locked too: battleship capacity (including in-flight transfers) must
        // be checked and reserved atomically against concurrent senders.
        var locks = await AcquireCountryMutationLocks(chatId, new[] { senderId, receiverId }, ct);
        try
        {
            return Database.TryCreateTransfers(
                senderId,
                chatId,
                allianceId,
                receiverId,
                resourceType,
                shipments,
                arriveAtMs);
        }
        finally
        {
            ReleaseCountryMutationLocks(locks);
        }
    }

    static IReadOnlyDictionary<string, long> SelectedDeploymentModels(
        IReadOnlyList<string> names, IReadOnlyList<long> amounts,
        long total, string defaultModel)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < names.Count && i < amounts.Count; i++)
            if (amounts[i] > 0)
                result[names[i]] = result.GetValueOrDefault(names[i]) + amounts[i];
        if (result.Count == 0 && total > 0) result[defaultModel] = total;
        if (result.Values.Sum() != total)
            throw new InvalidOperationException("Selected deployment model totals are inconsistent.");
        return result;
    }

    static async Task<long> TryCreateDeploymentSafely(Deployment deployment, CancellationToken ct,
        IReadOnlyDictionary<string, long>? tankModels = null,
        IReadOnlyDictionary<string, long>? fighterModels = null,
        IReadOnlyDictionary<string, long>? bomberModels = null)
    {
        if(!Database.IsBotGroupActive(deployment.ChatId))return 0;
        await deploymentProcessorLock.WaitAsync(ct);
        List<SemaphoreSlim>? locks = null;
        try
        {
            locks = await AcquireCountryMutationLocks(
                deployment.ChatId,
                new[] { deployment.InitiatorId },
                ct);
            try
            {
                return Database.TryCreateDeploymentWithForces(deployment,
                    tankModels, fighterModels, bomberModels);
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"[DEPLOYMENT MODEL RESERVATION] {ex.Message}");
                return 0;
            }
        }
        finally
        {
            if (locks != null) ReleaseCountryMutationLocks(locks);
            deploymentProcessorLock.Release();
        }
    }

    static async Task<bool> TryJoinDeploymentSafely(
        Deployment deployment,
        DeploymentContributor contributor,
        CancellationToken ct,
        IReadOnlyDictionary<string, long>? tankModels = null,
        IReadOnlyDictionary<string, long>? fighterModels = null,
        IReadOnlyDictionary<string, long>? bomberModels = null)
    {
        if(!Database.IsBotGroupActive(deployment.ChatId))return false;
        await deploymentProcessorLock.WaitAsync(ct);
        List<SemaphoreSlim>? locks = null;
        try
        {
            locks = await AcquireCountryMutationLocks(
                deployment.ChatId,
                new[] { contributor.UserId },
                ct);
            try
            {
                return Database.TryJoinDeploymentWithForces(
                    deployment.Id,
                    contributor,
                    deployment.ChatId,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    tankModels, fighterModels, bomberModels);
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"[DEPLOYMENT JOIN MODEL RESERVATION] {ex.Message}");
                return false;
            }
        }
        finally
        {
            if (locks != null) ReleaseCountryMutationLocks(locks);
            deploymentProcessorLock.Release();
        }
    }

    static async Task CancelDeploymentSafely(Deployment deployment, CancellationToken ct)
    {
        await deploymentProcessorLock.WaitAsync(ct);
        List<SemaphoreSlim>? locks = null;
        try
        {
            var contributorIds = Database.GetDeploymentContributors(deployment.Id)
                .Select(x => x.UserId)
                .Append(deployment.InitiatorId)
                .Append(deployment.TargetUserId);
            locks = await AcquireCountryMutationLocks(deployment.ChatId, contributorIds, ct);
            if (!Database.CancelDeploymentForces(deployment))
                throw new InvalidOperationException("Deployment cancellation ledger validation failed.");
            await UnpinAndDeleteAnnounce(deployment.ChatId, deployment.AnnounceMsgId, ct);
        }
        finally
        {
            if (locks != null)
                ReleaseCountryMutationLocks(locks);
            deploymentProcessorLock.Release();
        }
    }

    // Name similarity check – Levenshtein based, >90% considered too similar
    static int LevenshteinDistance(string s, string t)
    {
        if (string.IsNullOrEmpty(s)) return string.IsNullOrEmpty(t) ? 0 : t.Length;
        if (string.IsNullOrEmpty(t)) return s.Length;
        s = s.ToLowerInvariant().Trim();
        t = t.ToLowerInvariant().Trim();
        int n = s.Length;
        int m = t.Length;
        int[,] d = new int[n + 1, m + 1];
        for (int i = 0; i <= n; i++) d[i, 0] = i;
        for (int j = 0; j <= m; j++) d[0, j] = j;
        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = s[i - 1] == t[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        }
        return d[n, m];
    }

    static double CalculateNameSimilarity(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return 0;
        a = a.Trim().ToLowerInvariant();
        b = b.Trim().ToLowerInvariant();
        if (a == b) return 1.0;
        int maxLen = Math.Max(a.Length, b.Length);
        if (maxLen == 0) return 1.0;
        int distance = LevenshteinDistance(a, b);
        return 1.0 - (double)distance / maxLen;
    }

    static bool IsNameTooSimilar(string newName, IEnumerable<string> existingNames, double threshold = 0.9)
    {
        foreach (var existing in existingNames)
        {
            if (string.IsNullOrWhiteSpace(existing)) continue;
            // Exact match already handled elsewhere, but still consider similar
            double sim = CalculateNameSimilarity(newName, existing);
            if (sim >= threshold) return true;
        }
        return false;
    }

    static void ScheduleDelete(long chatId, int messageId, int seconds = 30)
    {
        if (messageId == 0) return;
        if (chatId == OWNER_ID) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds));
                await bot.DeleteMessageAsync(chatId, messageId);
            }
            catch { }
        });
    }

    static void DeleteNow(long chatId, int messageId)
    {
        if (messageId == 0) return;
        _ = Task.Run(async () =>
        {
            try { await bot.DeleteMessageAsync(chatId, messageId); } catch { }
        });
    }

    // FIX(2): آنپین + حذف پیام اعلام صف‌آرایی
    static async Task UnpinAndDeleteAnnounce(long chatId, int messageId, CancellationToken ct = default)
    {
        if (messageId == 0) return;
        try { await bot.UnpinChatMessageAsync(chatId, messageId, cancellationToken: ct); } catch { }
        try { await bot.DeleteMessageAsync(chatId, messageId, cancellationToken: ct); } catch { }
    }

    static async Task<Message> SendTemp(long chatId, string text, IReplyMarkup? markup = null,
        int? replyTo = null, ParseMode? parseMode = null, CancellationToken ct = default)
    {
        MarkIncomingHandled();
        var m = await bot.SendTextMessageAsync(chatId, text, parseMode: parseMode,
            replyToMessageId: replyTo, replyMarkup: markup, cancellationToken: ct);
        ScheduleDelete(chatId, m.MessageId, 30);
        return m;
    }

    static async Task<Message> SendTempPhoto(long chatId, string fileId, string caption,
        IReplyMarkup? markup = null, ParseMode? parseMode = null, CancellationToken ct = default)
    {
        MarkIncomingHandled();
        Message m;
        try
        {
            m = await bot.SendPhotoAsync(chatId, fileId, caption: caption, parseMode: parseMode,
                replyMarkup: markup, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PHOTO FALLBACK] {ex.Message}");
            m = await bot.SendTextMessageAsync(chatId, caption, parseMode: parseMode,
                replyMarkup: markup, cancellationToken: ct);
        }
        ScheduleDelete(chatId, m.MessageId, 30);
        return m;
    }

    static async Task<Message> SendPermanent(long chatId, string text, IReplyMarkup? markup = null,
        int? replyTo = null, ParseMode? parseMode = null, CancellationToken ct = default)
    {
        MarkIncomingHandled();
        return await bot.SendTextMessageAsync(chatId, text, parseMode: parseMode,
            replyToMessageId: replyTo, replyMarkup: markup, cancellationToken: ct);
    }

    static async Task<Message> SendPermanentPhoto(long chatId, string fileId, string caption,
        IReplyMarkup? markup = null, ParseMode? parseMode = null, CancellationToken ct = default)
    {
        MarkIncomingHandled();
        try
        {
            return await bot.SendPhotoAsync(chatId, fileId, caption: caption, parseMode: parseMode,
                replyMarkup: markup, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PHOTO FALLBACK] {ex.Message}");
            return await bot.SendTextMessageAsync(chatId, caption, parseMode: parseMode,
                replyMarkup: markup, cancellationToken: ct);
        }
    }

    static async Task<Message> SendPrompt(long uid, long chatId, string text, IReplyMarkup? markup = null, CancellationToken ct = default)
    {
        MarkIncomingHandled();
        ClearPromptNow(uid);
        var m = await bot.SendTextMessageAsync(chatId, text, replyMarkup: markup, cancellationToken: ct);
        if (sessions.TryGetValue(uid, out var s))
        {
            s.PromptChatId = chatId;
            s.PromptMsgId = m.MessageId;
        }
        return m;
    }

    static void TrackPrompt(long uid, long chatId, int messageId)
    {
        if (sessions.TryGetValue(uid, out var s))
        {
            s.PromptChatId = chatId;
            s.PromptMsgId = messageId;
        }
    }

    static void ClearPromptNow(long uid)
    {
        if (sessions.TryGetValue(uid, out var s) && s.PromptMsgId != 0)
        {
            DeleteNow(s.PromptChatId, s.PromptMsgId);
            s.PromptMsgId = 0;
        }
    }

    static void EndSession(long uid)
    {
        ClearPromptNow(uid);
        sessions.TryRemove(uid, out _);
    }

    static long SessionGameChatId(UserSession? session)
    {
        if(session==null)return 0;
        foreach(long id in new[]{session.AttackChatId,session.TransferChatId,session.DeployChatId,session.AllianceChatId,session.ChatId})
            if(id<0)return id;
        return 0;
    }

    static long ResolveCallbackGameChatId(long uid,string? data)
    {
        if(sessions.TryGetValue(uid,out var session))
        {
            long fromSession=SessionGameChatId(session);if(fromSession!=0)return fromSession;
        }
        var parts=(data??"").Split(':');
        if(parts.Length>1&&long.TryParse(parts[1],out long parsed)&&parsed<0)return parsed;
        return 0;
    }

    static SemaphoreSlim GetUserLock(long uid) =>
        userLocks.GetOrAdd(uid, _ => new SemaphoreSlim(1, 1));

    static Task<List<SemaphoreSlim>> AcquireCountryMutationLocks(
        long chatId,
        IEnumerable<long> ownerIds,
        CancellationToken ct) =>
        AcquireCountryMutationLocks(ownerIds.Select(ownerId => (chatId, ownerId)), ct);

    static async Task<List<SemaphoreSlim>> AcquireCountryMutationLocks(
        IEnumerable<(long ChatId, long OwnerId)> countryKeys,
        CancellationToken ct)
    {
        var locks = countryKeys
            .Distinct()
            .OrderBy(x => x.ChatId)
            .ThenBy(x => x.OwnerId)
            .Select(key => countryMutationLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1)))
            .ToList();
        var acquired = new List<SemaphoreSlim>(locks.Count);
        try
        {
            foreach (var item in locks)
            {
                if (!await item.WaitAsync(TimeSpan.FromSeconds(30), ct))
                    throw new TimeoutException("Timed out waiting for a country mutation lock.");
                acquired.Add(item);
            }
            return acquired;
        }
        catch
        {
            for (int i = acquired.Count - 1; i >= 0; i--)
                acquired[i].Release();
            throw;
        }
    }

    static void ReleaseCountryMutationLocks(List<SemaphoreSlim> locks)
    {
        for (int i = locks.Count - 1; i >= 0; i--)
            locks[i].Release();
    }

    static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken ct)
    {
        lock (processedLock)
        {
            if (!processedUpdates.Add(update.Id)) return;
            if (processedUpdates.Count > 5000) processedUpdates.Clear();
        }
        if(update.MyChatMember!=null)
        {
            string status=update.MyChatMember.NewChatMember.Status.ToString();
            bool active=status is not ("Left" or "Kicked");
            Database.SetBotGroupActive(update.MyChatMember.Chat.Id,active);
            Console.WriteLine($"[BOT GROUP STATUS] chat={update.MyChatMember.Chat.Id} status={status} active={active}");
            return;
        }
        SpamDecision spamDecision=EvaluateSpam(update);
        if(spamDecision.Kind!=SpamDecisionKind.Allow)
        {
            if(spamDecision.Kind==SpamDecisionKind.Warn)
            {
                long leftSeconds=Math.Max(1,(spamDecision.UntilMs-DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()+999)/1000);
                string warning=$"⛔ سیستم ضداسپم فعال شد. درخواست‌های تکراری موقتاً نادیده گرفته می‌شوند.\n⏱ زمان باقی‌مانده: {leftSeconds} ثانیه";
                try
                {
                    if(update.CallbackQuery!=null)
                        await botClient.AnswerCallbackQueryAsync(update.CallbackQuery.Id,warning,showAlert:true,cancellationToken:ct);
                    else if(update.Message!=null)
                        await botClient.SendTextMessageAsync(update.Message.Chat.Id,warning,cancellationToken:ct);
                }
                catch { }
            }
            return;
        }
        if (Volatile.Read(ref databaseMaintenanceRunning) != 0)
            return;

        Interlocked.Increment(ref activeUpdateHandlers);
        if (Volatile.Read(ref databaseMaintenanceRunning) != 0)
        {
            Interlocked.Decrement(ref activeUpdateHandlers);
            return;
        }
        long updateStartedMs = Environment.TickCount64;
        try
        {
            if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery != null)
            {
                long cbUid = update.CallbackQuery.From.Id;

                var cbChat = update.CallbackQuery.Message?.Chat;
                if (cbChat != null &&
                    (cbChat.Type == ChatType.Group || cbChat.Type == ChatType.Supergroup))
                {
                    Database.MarkGroupActive(cbChat.Id);
                    Database.SetBotGroupActive(cbChat.Id,true);
                    if(!string.IsNullOrWhiteSpace(cbChat.Title))groupTitleCache[cbChat.Id]=cbChat.Title;
                }

                var l = GetUserLock(cbUid);
                if (!await l.WaitAsync(TimeSpan.FromSeconds(30), ct))
                {
                    Console.WriteLine($"[UPDATE LOCK TIMEOUT] callback user={cbUid} data={update.CallbackQuery.Data}");
                    try { await bot.AnswerCallbackQueryAsync(update.CallbackQuery.Id,
                        "⚠️ عملیات قبلی هنوز در حال پردازش است؛ دوباره تلاش کنید.", showAlert: true, cancellationToken: ct); } catch { }
                    return;
                }
                List<SemaphoreSlim>? callbackCountryLocks = null;
                try
                {
                    long gameChatId=ResolveCallbackGameChatId(cbUid,update.CallbackQuery.Data);
                    if(gameChatId!=0&&!Database.IsBotGroupActive(gameChatId))
                    {
                        EndSession(cbUid);
                        try{await bot.AnswerCallbackQueryAsync(update.CallbackQuery.Id,
                            "⛔ ربات دیگر در گروه این کشور حضور ندارد؛ عملیات خصوصی غیرفعال است.",showAlert:true,cancellationToken:ct);}catch{}
                        return;
                    }
                    if (cbChat != null &&
                        (cbChat.Type == ChatType.Group || cbChat.Type == ChatType.Supergroup) &&
                        !(update.CallbackQuery.Data?.StartsWith("dep_", StringComparison.Ordinal) ?? false))
                    {
                        callbackCountryLocks = await AcquireCountryMutationLocks(
                            cbChat.Id,
                            new[] { cbUid },
                            ct);
                    }
                    await HandleCallbackAsync(update.CallbackQuery, ct);
                    Database.MarkPlayerActive(cbUid);
                }
                finally
                {
                    if (callbackCountryLocks != null)
                        ReleaseCountryMutationLocks(callbackCountryLocks);
                    l.Release();
                }
                return;
            }
            if (update.Type != UpdateType.Message || update.Message == null)
                return;
            var msg = update.Message;
            var user = msg.From;
            if (user == null) return;
            incomingCtx.Value = new MsgContext { UserId = user.Id, ChatId = msg.Chat.Id, MessageId = msg.MessageId };
            long uid = user.Id;
            var lk = GetUserLock(uid);
            if (!await lk.WaitAsync(TimeSpan.FromSeconds(30), ct))
            {
                Console.WriteLine($"[UPDATE LOCK TIMEOUT] message user={uid} chat={msg.Chat.Id}");
                try { await SendTemp(msg.Chat.Id, "⚠️ عملیات قبلی هنوز در حال پردازش است؛ کمی بعد دوباره تلاش کنید.", ct: ct); } catch { }
                return;
            }
            try
            {
                bool isPrivate = msg.Chat.Type == ChatType.Private;
                bool isOwner = uid == OWNER_ID;

                if (!isPrivate &&
                    (msg.Chat.Type == ChatType.Group || msg.Chat.Type == ChatType.Supergroup))
                {
                    Database.MarkGroupActive(msg.Chat.Id);
                    Database.SetBotGroupActive(msg.Chat.Id,true);
                    if(!string.IsNullOrWhiteSpace(msg.Chat.Title))groupTitleCache[msg.Chat.Id]=msg.Chat.Title;
                }
                if(isPrivate&&IsPanelAdmin(uid)&&IsSpamReportCommand(msg.Text?.Trim()??""))
                {
                    await SendSpamReport(uid,ct);
                    return;
                }
                if (isPrivate && IsPanelAdmin(uid))
                {
                    bool handledByPanel =
                        await TryHandleAdminPrivateMessageAsync(
                            msg,
                            user,
                            ct
                        );

                    if (handledByPanel)
                        return;
                }

                if (isPrivate && !isOwner) { await HandleUserPrivateAsync(msg, user, ct); return; }
                if (isPrivate && isOwner) { await HandleOwnerPrivateAsync(msg, user, ct); return; }
                await HandleGroupMessageAsync(msg, user, msg.Chat, ct);
            }
            finally { lk.Release(); incomingCtx.Value = null; }
        }
        catch (Exception ex)
        {
            string kind = update.CallbackQuery?.Data ?? update.Message?.Text ?? update.Type.ToString();
            Console.WriteLine($"[UPDATE ERR] update={update.Id} kind={kind}\n{ex}");
        }
        finally
        {
            long elapsedMs = Environment.TickCount64 - updateStartedMs;
            if (elapsedMs >= 5_000)
            {
                string kind = update.CallbackQuery?.Data ?? update.Message?.Text ?? update.Type.ToString();
                Console.WriteLine($"[SLOW UPDATE] update={update.Id} elapsed={elapsedMs}ms kind={kind}");
            }
            Interlocked.Decrement(ref activeUpdateHandlers);
        }
    }

    static Task HandlePollingErrorAsync(ITelegramBotClient bot, Exception ex, CancellationToken ct)
    {
        Console.WriteLine(ex.Message);
        return Task.CompletedTask;
    }

    static bool IsSpamReportCommand(string text)=>text is "گزارش اسپم" or "گزارش ضد اسپم" or "لیست اسپمرها";

    static async Task SendSpamReport(long adminId,CancellationToken ct)
    {
        long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var items=Database.GetSpamRestrictionReport(15);
        if(items.Count==0)
        {
            await SendTemp(adminId,"✅ هنوز هیچ محدودیت ضداسپمی ثبت نشده است.",ct:ct);
            return;
        }
        var lines=new List<string>{"🛡 گزارش ضداسپم"};
        var buttons=new List<InlineKeyboardButton[]>();
        foreach(var item in items)
        {
            string status=item.UntilMs>now?$"فعال — {FormatRemaining(item.UntilMs-now)}": "پایان‌یافته";
            string fingerprint=item.LastFingerprint.Replace('\n',' ');
            if(fingerprint.Length>55)fingerprint=fingerprint[..55]+"…";
            lines.Add($"\n👤 {item.UserId} | گپ {item.ChatId}\nوضعیت: {status} | مرحله {item.Level}\nحذف‌شده: {item.DroppedCount:N0} | علت: {item.Reason}\nآخرین الگو: {fingerprint}");
            buttons.Add(new[]{
                InlineKeyboardButton.WithCallbackData($"✅ رفع {item.UserId}",$"spam_admin:clear:{item.UserId}"),
                InlineKeyboardButton.WithCallbackData("⛔ ۳۰ دقیقه",$"spam_admin:block:{item.UserId}:{item.ChatId}")
            });
        }
        await SendPermanent(adminId,string.Join('\n',lines),new InlineKeyboardMarkup(buttons),ct:ct);
    }

    static async Task HandleSpamAdminCallback(CallbackQuery cb,CancellationToken ct)
    {
        if(cb.Data==null||!IsPanelAdmin(cb.From.Id))
        {
            try{await bot.AnswerCallbackQueryAsync(cb.Id,"⛔ دسترسی ندارید.",showAlert:true,cancellationToken:ct);}catch{}
            return;
        }
        var parts=cb.Data.Split(':');
        if(parts.Length<3||!TryParseLong(parts[2],out long userId))return;
        if(parts[1]=="clear")
        {
            ClearSpamState(userId);
            await bot.AnswerCallbackQueryAsync(cb.Id,"✅ محدودیت پاک شد.",showAlert:true,cancellationToken:ct);
        }
        else if(parts[1]=="block")
        {
            long chatId=parts.Length>3&&TryParseLong(parts[3],out long parsed)?parsed:0;
            RestrictSpamUser(userId,chatId,TimeSpan.FromMinutes(30),"محدودیت دستی مدیر");
            await bot.AnswerCallbackQueryAsync(cb.Id,"⛔ محدودیت ۳۰ دقیقه‌ای اعمال شد.",showAlert:true,cancellationToken:ct);
        }
        if(cb.Message!=null)DeleteNow(cb.Message.Chat.Id,cb.Message.MessageId);
        await SendSpamReport(cb.From.Id,ct);
    }

    static async Task HandleOwnerPrivateAsync(Message msg, User user, CancellationToken ct)
    {
        long uid = user.Id;
        string ownerTxt = msg.Text?.Trim() ?? "";

        if (ownerTxt == "عجله" || ownerTxt == "عجله تهاجمی")
        {
            var activeDeps = Database.GetActiveDeployments().Where(d => d.Type == "Offensive").ToList();
            if (activeDeps.Count == 0)
            {
                await SendTemp(uid, "❌ هیچ صفآرایی تهاجمی فعالی در کل ربات وجود ندارد.", ct: ct);
                return;
            }
            long nowRush = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1000L;
            foreach (var d in activeDeps) Database.UpdateDeploymentEndMs(d.Id, nowRush);
            await SendTemp(uid, $"⚡ دستور عجله تهاجمی اعمال شد!\n\n{activeDeps.Count} صفآرایی تهاجمی خاتمه یافت.", ct: ct);
            try { await ProcessActiveDeployments(ct); } catch { }
            return;
        }
        if (ownerTxt == "عجله دفاع" || ownerTxt == "عجله دفاعی")
        {
            var activeDeps = Database.GetActiveDeployments().Where(d => d.Type == "Defensive").ToList();
            if (activeDeps.Count == 0)
            {
                await SendTemp(uid, "❌ هیچ صفآرایی دفاعی فعالی وجود ندارد.", ct: ct);
                return;
            }
            long nowRush = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1000L;
            foreach (var d in activeDeps) Database.UpdateDeploymentEndMs(d.Id, nowRush);
            await SendTemp(uid, $"🛡 دستور عجله دفاعی اعمال شد!\n\n{activeDeps.Count} صفآرایی دفاعی خاتمه یافت.", ct: ct);
            try { await ProcessActiveDeployments(ct); } catch { }
            return;
        }

        if (ownerTxt == "حمله" || ownerTxt == "ترنسفر" || ownerTxt == "انتقال" || ownerTxt == "ارسال محموله" || ownerTxt == "ارسال منابع" ||
            (IsNavalCancellationCommand(ownerTxt) || IsOngoingBattlesCommand(ownerTxt)) ||
            ownerTxt == "صف آرایی تهاجمی" || ownerTxt == "صف آرایی دفاعی" || ownerTxt == "صف‌آرایی تهاجمی" || ownerTxt == "صف‌آرایی دفاعی" ||
            (sessions.TryGetValue(uid, out var atkSess) && atkSess != null &&
            (atkSess.Step == SessionStep.AttackWaitingGroup ||
             atkSess.Step == SessionStep.TransferWaitingAmount ||
             atkSess.Step == SessionStep.DefenseWaitingTankModel ||
             atkSess.Step == SessionStep.DefenseWaitingPlaneModel ||
             atkSess.Step == SessionStep.NavalDefenseWaitingBoatModel ||
             atkSess.Step == SessionStep.NavalDefenseWaitingSubmarineModel ||
             atkSess.Step == SessionStep.NavalDefenseWaitingBattleshipModel ||
             atkSess.Step == SessionStep.DeployWaitingTanks ||
             atkSess.Step == SessionStep.DeployWaitingSoldiers ||
             atkSess.Step == SessionStep.DeployWaitingFighters ||
             atkSess.Step == SessionStep.DeployWaitingBombers ||
             atkSess.Step == SessionStep.DeployWaitingTankModel ||
             atkSess.Step == SessionStep.DeployWaitingPlaneModel ||
             atkSess.Step == SessionStep.DeployWaitingBomberModel ||
             atkSess.Step == SessionStep.DeployJoinWaitingTanks ||
             atkSess.Step == SessionStep.DeployJoinWaitingTankModel ||
             atkSess.Step == SessionStep.DeployJoinWaitingSoldiers ||
             atkSess.Step == SessionStep.DeployJoinWaitingFighters ||
             atkSess.Step == SessionStep.DeployJoinWaitingPlaneModel ||
             atkSess.Step == SessionStep.DeployJoinWaitingBombers ||
             atkSess.Step == SessionStep.DeployJoinWaitingBomberModel ||
             atkSess.Step == SessionStep.AttackWaitingTarget ||
             atkSess.Step == SessionStep.AttackWaitingStrategy ||
             atkSess.Step == SessionStep.AttackWaitingTactic ||
             atkSess.Step == SessionStep.AttackWaitingTanks ||
             atkSess.Step == SessionStep.AttackWaitingSoldiers ||
             atkSess.Step == SessionStep.AttackWaitingFighters ||
             atkSess.Step == SessionStep.AttackWaitingBombers ||
             atkSess.Step == SessionStep.AttackWaitingTankModel ||
             atkSess.Step == SessionStep.AttackWaitingPlaneModel ||
             atkSess.Step == SessionStep.AttackWaitingBomberModel ||
             atkSess.Step == SessionStep.AttackWaitingModelAmount ||
             atkSess.Step == SessionStep.AttackWaitingAirStrategy ||
             atkSess.Step == SessionStep.AttackWaitingAirTactic)))
        {
            await HandleUserPrivateAsync(msg, user, ct);
            return;
        }

        if (sessions.TryGetValue(uid, out var dbSess) && dbSess != null && dbSess.Step == SessionStep.OwnerWaitingNewDatabase)
        {
            if (msg.Document != null)
            {
                string uploadPath = $"gamedata.{uid}.upload";
                var file = await bot.GetFileAsync(msg.Document.FileId, cancellationToken: ct);
                using (var stream = new System.IO.FileStream(uploadPath, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
                    await bot.DownloadFileAsync(file.FilePath!, stream, cancellationToken: ct);
                var restore = await RestoreDatabaseSafely(uploadPath, ct);
                EndSession(uid);
                TryDeleteSqliteSidecar(uploadPath);
                await SendTemp(uid,
                    restore.Success ? "✅ دیتابیس جدید با موفقیت جایگزین شد." : $"❌ بازیابی انجام نشد: {restore.Error}",
                    ct: ct);
            }
            else
            {
                await SendPrompt(uid, uid, "لطفاً فایل دیتابیس را ارسال کنید.", ct: ct);
            }
            return;
        }

        if (sessions.TryGetValue(uid, out var annSess) && annSess != null &&
            (annSess.Step == SessionStep.OwnerWaitingAnnounceAll ||
             annSess.Step == SessionStep.OwnerWaitingAnnouncePrivate ||
             annSess.Step == SessionStep.OwnerWaitingAnnounceGroup))
        {
            var countries = Database.GetAllCountries();
            var chatIds = countries.Select(x => x.ChatId).Distinct().ToList();
            var ownerIds = countries.Select(x => x.OwnerId).Distinct().ToList();
            bool toPrivate = annSess.Step == SessionStep.OwnerWaitingAnnounceAll || annSess.Step == SessionStep.OwnerWaitingAnnouncePrivate;
            bool toGroup = annSess.Step == SessionStep.OwnerWaitingAnnounceAll || annSess.Step == SessionStep.OwnerWaitingAnnounceGroup;
            int wanted = annSess.AnnounceCount;
            List<long> Shuffle(List<long> src) => src.OrderBy(_ => rng.Next()).ToList();
            int sentPrivate = 0, sentGroup = 0;
            if (toPrivate)
            {
                var shuffled = Shuffle(ownerIds);
                foreach (var target in shuffled)
                {
                    if (wanted > 0 && sentPrivate >= wanted) break;
                    try { await bot.CopyMessageAsync(target, msg.Chat.Id, msg.MessageId, cancellationToken: ct); sentPrivate++; } catch { }
                }
            }
            if (toGroup)
            {
                var shuffled = Shuffle(chatIds);
                foreach (var target in shuffled)
                {
                    if (wanted > 0 && sentGroup >= wanted) break;
                    try { await bot.CopyMessageAsync(target, msg.Chat.Id, msg.MessageId, cancellationToken: ct); sentGroup++; } catch { }
                }
            }
            EndSession(uid);
            await SendTemp(uid, $"✅ اعلامیه ارسال شد.\n👤 پیوی: {sentPrivate}\n👥 گپ: {sentGroup}", ct: ct);
            return;
        }

        if (sessions.TryGetValue(uid, out var sessFlag) && sessFlag != null && sessFlag.Step == SessionStep.OwnerWaitingFlagManage)
        {
            if (msg.Photo != null && msg.Photo.Length > 0)
            {
                string fileId = msg.Photo.Last().FileId;
                Database.AddFactionFlag(sessFlag.FactionStr, fileId);
                EndSession(uid);
                await SendTemp(uid, $"✅ پرچم به {sessFlag.FactionStr} اضافه شد.", ct: ct);
                return;
            }
        }

        string txt = msg.Text?.Trim() ?? "";

        if (sessions.TryGetValue(uid, out var flagManageSess) && flagManageSess != null
            && flagManageSess.Step == SessionStep.OwnerWaitingFlagManage
            && TryParseInt(txt, out int delIndex))
        {
            var flags = Database.GetFactionFlags(flagManageSess.FactionStr);
            if (delIndex >= 1 && delIndex <= flags.Count)
            {
                Database.RemoveFactionFlag(flagManageSess.FactionStr, delIndex - 1);
                EndSession(uid);
                await SendTemp(uid, $"✅ پرچم شماره {delIndex} حذف شد.", ct: ct);
                return;
            }
        }

        if (txt == "پرچم فکشن امریکا" || txt == "پرچم فکشن آمریکا") { await ShowFactionFlags(uid, "USA", "🇺🇸", ct); return; }
        if (txt == "پرچم فکشن شوروی") { await ShowFactionFlags(uid, "USSR", "☭", ct); return; }
        if (txt == "پرچم فکشن رایش") { await ShowFactionFlags(uid, "Reich", "⚫", ct); return; }
        if (txt == "عکس تهاجمی" || txt == "عکس های تهاجمی" || txt == "عکس‌های تهاجمی") { await ShowFactionFlags(uid, "OffensiveDeploy", "⚔️ عکس‌های تهاجمی", ct); return; }
        if (txt == "عکس دفاعی" || txt == "عکس های دفاعی" || txt == "عکس‌های دفاعی") { await ShowFactionFlags(uid, "DefensiveDeploy", "🛡 عکس‌های دفاعی", ct); return; }

        if (txt == "عکس اسپشیال")
        {
            sessions[uid] = new UserSession { Step = SessionStep.OwnerWaitingSpecialPhoto };
            if (!string.IsNullOrEmpty(SpecialPhotoFileId))
                await SendTempPhoto(uid, SpecialPhotoFileId, "📷 عکس اسپشیال فعلی", ct: ct);
            await SendPrompt(uid, uid, "عکس جدید را ارسال کنید.", ct: ct);
            return;
        }

        if (txt == "فیکس دفاع")
        {
            var all = Database.GetAllCountries();
            foreach (var c in all) Database.ReconcileDefense(c.OwnerId, c.ChatId);
            await SendTemp(uid, $"✅ دفاع همه کشورها بروزرسانی شد. ({all.Count} کشور)", ct: ct);
            return;
        }
        if (txt == "فیکس ناوگان" || txt == "فیکس عملیات دریایی")
        {
            await navalProcessorLock.WaitAsync(ct);
            try
            {
                string repair=Database.RepairPendingNavalOperations();
                await SendTemp(uid,$"✅ دفتر عملیات دریایی ترمیم شد.\n{repair}",ct:ct);
            }
            finally{navalProcessorLock.Release();}
            await ProcessNavalInvasions(ct);
            return;
        }
        if (txt == "فیکس صف آرایی" || txt == "فیکس صف‌آرایی")
        {
            await deploymentProcessorLock.WaitAsync(ct);
            try
            {
                string repair = Database.RepairDeploymentIntegrity();
                await SendTemp(uid, $"✅ بررسی و ترمیم دفتر صف‌آرایی انجام شد.\n{repair}", ct: ct);
            }
            finally { deploymentProcessorLock.Release(); }
            return;
        }

        if (txt == "واریز")
        {
            sessions[uid] = new UserSession { Step = SessionStep.OwnerWaitingRoyalDeposit };
            await SendPrompt(uid, uid, "آیدی عددی کاربر:", ct: ct);
            return;
        }

        if (txt == "کسر")
        {
            sessions[uid] = new UserSession { Step = SessionStep.OwnerWaitingRoyalDeduct };
            await SendPrompt(uid, uid, "آیدی عددی کاربر:", ct: ct);
            return;
        }

        if (txt == "آمار")
        {
            await SendActivityStats(uid, permanent: false, ct: ct);
            return;
        }

        if (txt == "آپلود دیتابیس")
        {
            sessions[uid] = new UserSession { Step = SessionStep.OwnerWaitingNewDatabase };
            await SendPrompt(uid, uid, "دیتابیس جدید را ارسال کنید.", ct: ct);
            return;
        }

        if (txt.StartsWith("اعلامیه"))
        {
            var words = txt.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            bool isAll = words.Contains("کامل");
            bool isPrivate = words.Contains("پیوی");
            bool isGroup = words.Contains("گپ");
            if (isAll || isPrivate || isGroup)
            {
                int count = 0;
                var last = words.Last();
                if (TryParseInt(last, out int n) && n > 0) count = n;
                SessionStep step = isAll ? SessionStep.OwnerWaitingAnnounceAll :
                                   isPrivate ? SessionStep.OwnerWaitingAnnouncePrivate :
                                   SessionStep.OwnerWaitingAnnounceGroup;
                sessions[uid] = new UserSession { Step = step, AnnounceCount = count };
                string scope = isAll ? "کامل (پیوی + گپ)" : isPrivate ? "پیوی" : "گپ";
                string howmany = count > 0 ? $"{count} موردِ رندوم" : "همه";
                await SendPrompt(uid, uid, $"📢 اعلامیه: {scope} — {howmany}\n\nپیام اعلامیه را ارسال کنید.", ct: ct);
                return;
            }
        }

        if (txt == "تایمینگ روزانه")
        {
            sessions[uid] = new UserSession { Step = SessionStep.OwnerWaitingDailyTime };
            await SendPrompt(uid, uid, "⏰ ساعت را به فرمت HHMM ارسال کنید (مثلاً 1430)", ct: ct);
            return;
        }

        if (txt == "تایمینگ دقیقه ای")
        {
            sessions[uid] = new UserSession { Step = SessionStep.OwnerWaitingMinuteTime };
            await SendPrompt(uid, uid, "⌛ هر چند دقیقه؟ (1 تا 3599)", ct: ct);
            return;
        }

        if (txt == "تایمینگ")
        {
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("⏰ روزانه","timing:daily"),
                    InlineKeyboardButton.WithCallbackData("⌛ دقیقه ای","timing:minute")
                }
            });
            await SendTemp(uid, "نوع زمان بندی را انتخاب کنید", markup: keyboard, ct: ct);
            return;
        }

        if (sessions.TryGetValue(uid, out var ownerSess2) && ownerSess2 != null)
        {
            if (ownerSess2.Step == SessionStep.OwnerWaitingRoyalDeposit)
            {
                if (!TryParseLong(txt, out long tid)) { await SendPrompt(uid, uid, "آیدی معتبر نیست. دوباره بفرستید:", ct: ct); return; }
                long cur = Database.GetRoyalCoins(tid);
                sessions[uid] = new UserSession { Step = SessionStep.OwnerWaitingRoyalDepositAmount, ChatId = tid };
                await SendPrompt(uid, uid, $"💎 رویال فعلی: {cur}\nچند رویال واریز?", ct: ct);
                return;
            }
            if (ownerSess2.Step == SessionStep.OwnerWaitingRoyalDepositAmount)
            {
                if (!TryParseLong(txt, out long amt) || amt <= 0) { await SendPrompt(uid, uid, "عدد معتبر نیست. دوباره:", ct: ct); return; }
                Database.AddRoyalCoins(ownerSess2.ChatId, amt);
                long tgt = ownerSess2.ChatId;
                EndSession(uid);
                await SendPermanent(uid, $"✅ {amt} رویال واریز شد.", ct: ct);
                try { await SendPermanent(tgt, $"💎 {amt} رویال کوین به حساب شما واریز شد!", ct: ct); } catch { }
                return;
            }
            if (ownerSess2.Step == SessionStep.OwnerWaitingRoyalDeduct)
            {
                if (!TryParseLong(txt, out long tid2)) { await SendPrompt(uid, uid, "آیدی معتبر نیست. دوباره:", ct: ct); return; }
                long cur2 = Database.GetRoyalCoins(tid2);
                sessions[uid] = new UserSession { Step = SessionStep.OwnerWaitingRoyalDeductAmount, ChatId = tid2 };
                await SendPrompt(uid, uid, $"💎 رویال فعلی: {cur2}\nچند رویال کسر?", ct: ct);
                return;
            }
            if (ownerSess2.Step == SessionStep.OwnerWaitingRoyalDeductAmount)
            {
                if (!TryParseLong(txt, out long amt2) || amt2 <= 0) { await SendPrompt(uid, uid, "عدد معتبر نیست. دوباره:", ct: ct); return; }
                Database.AddRoyalCoins(ownerSess2.ChatId, -amt2);
                long tgt = ownerSess2.ChatId;
                EndSession(uid);
                await SendPermanent(uid, $"✅ {amt2} رویال کسر شد.", ct: ct);
                try { await SendPermanent(tgt, $"💎 {amt2} رویال کوین از حساب شما کسر شد.", ct: ct); } catch { }
                return;
            }
            if (ownerSess2.Step == SessionStep.OwnerWaitingDailyTime)
            {
                string val = NormalizeDigits(txt);
                if (val.Length != 4 ||
                    !int.TryParse(val.Substring(0, 2), out int hh) ||
                    !int.TryParse(val.Substring(2, 2), out int mm) ||
                    hh > 23 || mm > 59)
                {
                    await SendPrompt(uid, uid, "فرمت صحیح نیست. دوباره به صورت HHMM:", ct: ct);
                    return;
                }
                UpdateMode = "daily";
                UpdateValue = hh * 60 + mm;
                Database.SetSetting("UpdateMode", UpdateMode);
                Database.SetSetting("UpdateValue", UpdateValue.ToString());
                StartAssetUpdateTimer();
                StartTransferTimer();
                EndSession(uid);
                await SendTemp(uid, $"✅ آپدیت روزانه روی {hh:D2}:{mm:D2} تنظیم شد", ct: ct);
                return;
            }
            if (ownerSess2.Step == SessionStep.OwnerWaitingMinuteTime)
            {
                if (!TryParseInt(txt, out int mins) || mins < 1 || mins > 3599)
                {
                    await SendPrompt(uid, uid, "عدد باید بین 1 تا 3599 باشد. دوباره:", ct: ct);
                    return;
                }
                UpdateMode = "minute";
                UpdateValue = mins;
                Database.SetSetting("UpdateMode", UpdateMode);
                Database.SetSetting("UpdateValue", UpdateValue.ToString());
                StartAssetUpdateTimer();
                StartTransferTimer();
                EndSession(uid);
                await SendTemp(uid, $"✅ آپدیت هر {mins} دقیقه تنظیم شد", ct: ct);
                return;
            }
            if (ownerSess2.Step == SessionStep.OwnerWaitingSpecialPhoto)
            {
                if (msg.Photo == null || msg.Photo.Length == 0)
                {
                    await SendPrompt(uid, uid, "لطفاً عکس ارسال کنید", ct: ct);
                    return;
                }
                SpecialPhotoFileId = msg.Photo.Last().FileId;
                Database.SetSetting("SpecialPhotoFileId", SpecialPhotoFileId);
                EndSession(uid);
                await SendTemp(uid, "✅ عکس اسپشیال ذخیره شد", ct: ct);
                return;
            }
        }

        // FIX(3)/(4): در پیوی مالک هم اگر چیز دیگری نبود، راهنما/استارت را پاسخ بده
        if (txt == "/start" || txt == "شروع" || txt == "start")
        {
            await SendStartMessage(uid, ct);
            return;
        }
        if (txt == "راهنما" || txt == "/help" || txt == "help")
        {
            await SendPermanent(uid, HelpText, parseMode: ParseMode.Html, ct: ct);
            return;
        }
    }

    static async Task ShowFactionFlags(long uid, string factionStr, string emoji, CancellationToken ct)
    {
        var flags = Database.GetFactionFlags(factionStr);
        sessions[uid] = new UserSession { Step = SessionStep.OwnerWaitingFlagManage, FactionStr = factionStr };
        await SendTemp(uid, $"{emoji} تعداد پرچم ها: {flags.Count}\nبرای حذف، شماره را ارسال کنید؛ برای افزودن، عکس بفرستید.", ct: ct);
        for (int i = 0; i < flags.Count; i++)
            await SendTempPhoto(uid, flags[i], $"شماره {i + 1}", ct: ct);
    }

    // ============================================================
    //  Group message handler
    // ============================================================
    static async Task HandleGroupMessageAsync(Message msg, User user, Chat chat, CancellationToken ct)
    {
        long uid = user.Id;
        string txt = msg.Text?.Trim() ?? "";
        if(IsOngoingBattlesCommand(txt))
        {
            if(Database.GetCountry(uid,chat.Id)==null)
            {
                await SendTemp(chat.Id,MsgNoCountryGuide,replyTo:msg.MessageId,ct:ct);
                return;
            }
            await SendTemp(chat.Id,"⚔️ لیست نبردهای در جریان به پیوی شما ارسال شد.",replyTo:msg.MessageId,ct:ct);
            try{await ShowOngoingBattles(uid,ct,chat.Id);}catch{await SendTemp(chat.Id,"❌ اول ربات را در پیوی استارت کنید.",replyTo:msg.MessageId,ct:ct);}
            return;
        }
        if (uid == OWNER_ID && txt == "یک مقصد است اینجا برایمان")
        {
            sessions[uid] = new UserSession { Step = SessionStep.OwnerWaitingVisionSource, VisionDestChatId = chat.Id };
            await SendTemp(chat.Id, "✅ این گروه به عنوان مقصد لاگ ثبت شد. حالا آیدی عددی گپ یا کاربر را بفرستید.", replyTo: msg.MessageId, ct: ct);
            return;
        }
        if (uid == OWNER_ID && sessions.TryGetValue(uid, out var visionSess) && visionSess != null && visionSess.Step == SessionStep.OwnerWaitingVisionSource)
        {
            if (TryParseLong(txt, out long srcId)){
                visionSess.VisionSourceId = srcId;
                visionSess.Step = SessionStep.OwnerWaitingVisionConfirm;
                await SendTemp(chat.Id, "برای تایید بنویسید amirr1202", replyTo: msg.MessageId, ct: ct);
                return;
            }
        }
        if (uid == OWNER_ID && sessions.TryGetValue(uid, out var visionConf) && visionConf != null && visionConf.Step == SessionStep.OwnerWaitingVisionConfirm && txt == "amirr1202")
        {
            long destId = visionConf.VisionDestChatId;
            long srcId = visionConf.VisionSourceId;
            bool isUser = srcId > 0;
            long srcChat = isUser ? 0 : srcId;
            long srcUser = isUser ? srcId : 0;
            Database.AddVisionLog(srcChat, srcUser, destId, isUser ? 1 : 0);
            EndSession(uid);
            if (!isUser){
                try { await bot.SendTextMessageAsync(srcId, "", cancellationToken: ct); } catch {}
                await SendTemp(chat.Id, $"✅ لاگ گپ فعال شد! مبدا:{srcId} مقصد:{destId}", ct: ct);
            } else {
                await SendTemp(chat.Id, $"✅ لاگ کاربر فعال شد! کاربر:{srcId}", ct: ct);
            }
            return;
        }
        if (uid == OWNER_ID && txt == "ایدی" && msg.ReplyToMessage != null)
        {
            var info = Database.GetSourceByDestId(chat.Id, msg.ReplyToMessage.MessageId);
            if (info != null && info.Value.SourceUserId != 0){
                try {
                    var uc = await bot.GetChatAsync(info.Value.SourceUserId, ct);
                    string un = string.IsNullOrEmpty(uc.Username) ? "ندارد" : "@"+uc.Username;
                    string nm = uc.FirstName + (string.IsNullOrEmpty(uc.LastName) ? "" : " "+uc.LastName);
                    await SendTemp(chat.Id, $"👤 {nm}\n🆔 {info.Value.SourceUserId}\n🔗 {un}", replyTo: msg.MessageId, ct: ct);
                } catch {}
                return;
            }
        }
        // بازگشت شخصی نیروها
        if (txt == "بازگشت" || txt == "بازگشت نیرو" || txt == "بازگشت نیروها" || txt == "برگشت")
        {
            await deploymentProcessorLock.WaitAsync(ct);
            try
            {
            var returnCountryLocks = await AcquireCountryMutationLocks(chat.Id, new[] { uid }, ct);
            try
            {
            var activeDepsInChat = Database.GetActiveDeployments().Where(d => d.ChatId == chat.Id).ToList();
            var myDeployments = new List<(Deployment dep, List<DeploymentContributor> myContribs)>();
            foreach (var dep in activeDepsInChat)
            {
                var contribs = Database.GetDeploymentContributors(dep.Id);
                var mine = contribs.Where(c => c.UserId == uid).ToList();
                if (mine.Count > 0) myDeployments.Add((dep, mine));
            }
            if (myDeployments.Count == 0)
            {
                await SendTemp(chat.Id, "❌ شما در هیچ صفآرایی فعالی در این گروه نیرو ندارید.", replyTo: msg.MessageId, ct: ct);
                return;
            }
            long totalTanks=0, totalSoldiers=0, totalFighters=0, totalBombers=0;
            int returnedDeployments=0;
            foreach (var (dep, _) in myDeployments)
            {
                bool withdrawn = Database.WithdrawDeploymentContribution(
                    dep.Id,
                    uid,
                    chat.Id,
                    out long sumT,
                    out long sumS,
                    out long sumF,
                    out long sumB,
                    out bool deploymentDeleted);
                if (!withdrawn)
                    continue;

                totalTanks += sumT;
                totalSoldiers += sumS;
                totalFighters += sumF;
                totalBombers += sumB;
                Database.ReconcileDefense(uid, chat.Id);

                // Defensive forces only exist in Deployments. Nothing is subtracted from
                // the defended country's own inventory when a contributor withdraws.
                if (deploymentDeleted)
                    await UnpinAndDeleteAnnounce(dep.ChatId, dep.AnnounceMsgId, ct);
                else
                    await RefreshDeploymentAnnouncement(dep.Id, ct);
                returnedDeployments++;
            }
            await SendTemp(chat.Id, $"✅ بازگشت انجام شد!\n👤 از {returnedDeployments} صفآرایی خارج شدید:\n🛡 تانک: {totalTanks:N0}\n🪖 سرباز: {totalSoldiers:N0}\n✈️ جنگنده: {totalFighters:N0}\n🛩 بمبافکن: {totalBombers:N0}", replyTo: msg.MessageId, ct: ct);
            return;
            }
            finally
            {
                ReleaseCountryMutationLocks(returnCountryLocks);
            }
            }
            finally
            {
                deploymentProcessorLock.Release();
            }
        }


        // Owner rush command in group
        if (uid == OWNER_ID && (txt == "عجله" || txt == "عجله تهاجمی"))
        {
            var activeDeps = Database.GetActiveDeployments().Where(d => d.ChatId == chat.Id && d.Type == "Offensive").ToList();
            if (activeDeps.Count == 0)
            {
                await SendTemp(chat.Id, "❌ هیچ صفآرایی تهاجمی فعالی در این گروه وجود ندارد.", replyTo: msg.MessageId, ct: ct);
                return;
            }
            long nowRush = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1000L;
            foreach (var d in activeDeps) Database.UpdateDeploymentEndMs(d.Id, nowRush);
            await SendTemp(chat.Id, $"⚡ دستور عجله تهاجمی اعمال شد! {activeDeps.Count} مورد", replyTo: msg.MessageId, ct: ct);
            try { await ProcessActiveDeployments(ct); } catch { }
            return;
        }
        if (uid == OWNER_ID && (txt == "عجله دفاع" || txt == "عجله دفاعی"))
        {
            var activeDeps = Database.GetActiveDeployments().Where(d => d.ChatId == chat.Id && d.Type == "Defensive").ToList();
            if (activeDeps.Count == 0)
            {
                await SendTemp(chat.Id, "❌ هیچ صفآرایی دفاعی فعالی در این گروه وجود ندارد.", replyTo: msg.MessageId, ct: ct);
                return;
            }
            long nowRush = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1000L;
            foreach (var d in activeDeps) Database.UpdateDeploymentEndMs(d.Id, nowRush);
            await SendTemp(chat.Id, $"🛡 دستور عجله دفاعی اعمال شد! {activeDeps.Count} مورد", replyTo: msg.MessageId, ct: ct);
            try { await ProcessActiveDeployments(ct); } catch { }
            return;
        }

        // VISION FORWARD - شفاف
        try {
            var logsChat = Database.GetVisionLogsBySourceChat(chat.Id);
            var logsUser = Database.GetVisionLogsBySourceUser(uid);
            var logsReply = new List<(long Id, long SourceChatId, long SourceUserId, long DestChatId, int IsUserMode)>();
            if (msg.ReplyToMessage?.From != null){
                logsReply = Database.GetVisionLogsBySourceUser(msg.ReplyToMessage.From.Id);
            }
            var allLogs = logsChat.Concat(logsUser).Concat(logsReply).GroupBy(x=>x.Id).Select(g=>g.First()).ToList();
            foreach (var vLog in allLogs){
                long destId = vLog.DestChatId;
                if (destId == chat.Id) continue;
                int replyMap = 0;
                if (msg.ReplyToMessage != null){
                    var mm = Database.GetDestMessageId(chat.Id, msg.ReplyToMessage.MessageId, destId);
                    if (mm != null) replyMap = (int)mm.Value.DestMessageId;
                }
                string senderName = user.FirstName;
                string grpTitle = chat.Title ?? "";
                string pref = vLog.IsUserMode==1 ? $"[{grpTitle}] {senderName}: " : $"{senderName}: ";
                Message sent = null;
                if (!string.IsNullOrEmpty(msg.Text)){
                    string t = pref + msg.Text;
                    if (replyMap!=0) sent = await bot.SendTextMessageAsync(destId, t, replyToMessageId: replyMap, cancellationToken: ct);
                    else sent = await bot.SendTextMessageAsync(destId, t, cancellationToken: ct);
                } else if (msg.Photo != null && msg.Photo.Length>0){
                    var fid = msg.Photo.Last().FileId;
                    string cap = pref + (msg.Caption ?? "");
                    if (replyMap!=0) sent = await bot.SendPhotoAsync(destId, new InputOnlineFile(fid), caption: cap, replyToMessageId: replyMap, cancellationToken: ct);
                    else sent = await bot.SendPhotoAsync(destId, new InputOnlineFile(fid), caption: cap, cancellationToken: ct);
                } else if (msg.Sticker != null){
                    if (replyMap!=0) sent = await bot.SendStickerAsync(destId, new InputOnlineFile(msg.Sticker.FileId), replyToMessageId: replyMap, cancellationToken: ct);
                    else sent = await bot.SendStickerAsync(destId, new InputOnlineFile(msg.Sticker.FileId), cancellationToken: ct);
                    if (sent!=null){ try { await bot.SendTextMessageAsync(destId, pref.Trim(), replyToMessageId: sent.MessageId, cancellationToken: ct); } catch {} }
                } else if (msg.Video != null){
                    var fid = msg.Video.FileId;
                    string cap = pref + (msg.Caption ?? "");
                    if (replyMap!=0) sent = await bot.SendVideoAsync(destId, new InputOnlineFile(fid), caption: cap, replyToMessageId: replyMap, cancellationToken: ct);
                    else sent = await bot.SendVideoAsync(destId, new InputOnlineFile(fid), caption: cap, cancellationToken: ct);
                } else if (msg.Document != null){
                    var fid = msg.Document.FileId;
                    string cap = pref + (msg.Caption ?? "");
                    var fname = msg.Document.FileName ?? "file";
                    if (replyMap!=0) sent = await bot.SendDocumentAsync(destId, new InputOnlineFile(fid), caption: cap, replyToMessageId: replyMap, cancellationToken: ct);
                    else sent = await bot.SendDocumentAsync(destId, new InputOnlineFile(fid), caption: cap, cancellationToken: ct);
                }
                if (sent!=null){
                    Database.AddVisionMessageMap(chat.Id, msg.MessageId, uid, destId, sent.MessageId);
                }
            }
        } catch (Exception ex){ Console.WriteLine($"[VISION ERR] {ex.Message}"); }
        if (uid == OWNER_ID && (txt == "معافیت کامل" || txt == "معافیت کامل قفل"))
        {
            bool current = Database.HasGroupLockExemption(chat.Id);
            if (!current)
            {
                Database.SetGroupLockExemption(chat.Id, true);
                Database.ClearAllLeaveCooldownsInChat(chat.Id);
                Database.SetAllShieldExemptionsInChat(chat.Id);
                await SendTemp(chat.Id, "✅ **معافیت کامل و سراسری برای این گروه ثبت شد!**\n\nتغییرات اعمال‌شده:\n۱. 🔓 **حذف قفل ۳۰ دقیقه‌ای**: حمله بلافاصله پس از آپدیت دارایی‌ها آزاد است.\n۲. 🛡 **حذف تمام سپرها**: سپر ۴۸ ساعتهٔ تمام کشورهای فعلی این گپ برداشته شد.\n۳. ⏳ **حذف تایمر انصراف**: تمام محدودیت‌های ۲۴ ساعتهٔ ساخت مجدد کشور برای بازیکنان این گروه پاک شد.\n۴. ⚡ **حذف تایمر ترنسفر**: زمان انتظار ارسال محموله‌ها صفر شد و محموله‌های جاری فوری تحویل داده شدند.", replyTo: msg.MessageId, ct: ct);
            }
            else
            {
                Database.SetGroupLockExemption(chat.Id, false);
                await SendTemp(chat.Id, "⛔ **معافیت کامل لغو شد.**\n\nقفل ۳۰ دقیقه‌ای ابتدای آپدیت مجدداً برای این گروه فعال شد.", replyTo: msg.MessageId, ct: ct);
            }
            return;
        }

        if (uid == OWNER_ID && (txt == "لغو معافیت کامل" || txt == "حذف معافیت کامل"))
        {
            Database.SetGroupLockExemption(chat.Id, false);
            await SendTemp(chat.Id, "⛔ **معافیت کامل لغو شد.**\n\nقفل ۳۰ دقیقه‌ای ابتدای آپدیت مجدداً برای این گروه فعال شد.", replyTo: msg.MessageId, ct: ct);
            return;
        }

        if (uid == OWNER_ID && (txt == "معاف" || txt == "معافیت") && msg.ReplyToMessage?.From != null)
        {
            long targetUid = msg.ReplyToMessage.From.Id;
            bool hadCooldown = Database.GetLeaveCooldownRemainingMs(targetUid, chat.Id) > 0;
            var targetCountry = Database.GetCountry(targetUid, chat.Id);
            bool hadShield = targetCountry != null && targetCountry.CreatedAtMs > 0 &&
                             !Database.HasShieldExemption(targetUid, chat.Id) &&
                             ((DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - targetCountry.CreatedAtMs) / 3600000.0) < SHIELD_HOURS;
            Database.ClearLeaveCooldown(targetUid, chat.Id);
            if (targetCountry != null) Database.SetShieldExemption(targetUid, chat.Id);
            string resultText;
            if (hadCooldown && hadShield)
                resultText = "✅ معافیت اعمال شد. هم تایمر انصراف و هم سپر ۴۸ ساعتهٔ این کاربر در این گپ برداشته شد.";
            else if (hadCooldown)
                resultText = "✅ معافیت اعمال شد. تایمر انصراف این کاربر در این گپ برداشته شد.";
            else if (hadShield)
                resultText = "✅ معافیت اعمال شد. سپر ۴۸ ساعتهٔ این کاربر در این گپ برداشته شد.";
            else
                resultText = "✅ معافیت ثبت شد. اگر این کاربر در این گپ کشور داشته باشد، سپرش برداشته شده و اگر تایمر انصرافی داشته باشد، پاک شده است.";
            await SendTemp(chat.Id, resultText, replyTo: msg.MessageId, ct: ct);
            return;
        }

        if (txt == "لغو")
        {
            if (sessions.ContainsKey(uid)) { EndSession(uid); await SendTemp(chat.Id, "✅ عملیات لغو شد.", ct: ct); }
            else await SendTemp(chat.Id, "عملیات فعالی وجود ندارد.", ct: ct);
            return;
        }

        if (txt == "انتخاب کشور")
        {
            if (Database.IsUserBanned(uid))
            {
                await SendTemp(chat.Id, "🚫 شما از بازی بن شده‌اید. لطفاً با ادمین تماس بگیرید.", ct: ct);
                return;
            }
            if (Database.CountryExists(uid, chat.Id))
            {
                await SendTemp(chat.Id, "شما قبلاً کشور دارید", ct: ct);
                return;
            }
            long remainMs = Database.GetLeaveCooldownRemainingMs(uid, chat.Id);
            if (remainMs > 0)
            {
                await SendTemp(chat.Id, $"⛔ شما اخیراً در این گروه انصراف داده‌اید.\n⏳ تا {FormatRemaining(remainMs)} دیگر نمی‌توانید کشور جدید بسازید.", ct: ct);
                return;
            }
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🔴 شوروی", $"faction:{uid}:USSR"),
                    InlineKeyboardButton.WithCallbackData("🔵 آمریکا", $"faction:{uid}:USA"),
                    InlineKeyboardButton.WithCallbackData("⚫ رایش", $"faction:{uid}:Reich")
                }
            });
            sessions[uid] = new UserSession { Step = SessionStep.None };
            await SendPrompt(uid, chat.Id, "فکشن را انتخاب کنید", keyboard, ct);
            return;
        }

        if (txt == "ارتقاع ساختمان" || txt == "ساختمان" || txt == "ارتقا اقتصاد" || txt == "اقتصاد")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return; }
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("🏭 کارخانه", $"build_menu:{uid}:factory") },
                new[] { InlineKeyboardButton.WithCallbackData("⚓ بندر", $"build_menu:{uid}:port") },
                new[] { InlineKeyboardButton.WithCallbackData("⛏️ معدن", $"build_menu:{uid}:mine") }
            });
            await SendTemp(chat.Id, "ساختمان مورد نظر را انتخاب کنید:", markup: keyboard, ct: ct);
            return;
        }

        if (txt == "انصراف")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return; }
            sessions[uid] = new UserSession { Step = SessionStep.WaitingDeleteConfirm, ChatId = chat.Id };
            await SendPrompt(uid, chat.Id,
                "⚠️ در صورت انصراف تمامی اطلاعات شما در این گپ پاک میشود و این عمل غیر قابل بازگشت است.\n\nمطمئن هستید؟\nاگر بلی بنویسید بلی\nدر غیر این صورت بنویسید خیر",
                ct: ct);
            return;
        }

        if (sessions.TryGetValue(uid, out var delSess) && delSess != null
            && delSess.Step == SessionStep.WaitingDeleteConfirm)
        {
            if (txt == "بلی")
            {
                Database.DeleteCountry(uid, chat.Id);
                Database.SetLeaveCooldown(uid, chat.Id, 24);
                // FIX(1b): اگه امروز حمله واقعی زده، ۳ روز قفل
                string todayDel = DateTime.UtcNow.AddHours(3.5).ToString("yyyy-MM-dd");
                bool hadRealAttack = Database.HasAttackerFlag(uid, todayDel);
                if (hadRealAttack)
                {
                    long threeDaysDel = 3L * 24 * 60 * 60 * 1000;
                    Database.SetAttackAbandonLock(uid, threeDaysDel);
                    var lockUntilDel = DateTimeOffset.FromUnixTimeMilliseconds(
                        Database.GetAttackAbandonLockUntilMs(uid))
                        .ToOffset(TimeSpan.FromHours(3.5));
                    EndSession(uid);
                    await SendTemp(chat.Id,
                        $"✅ اطلاعات شما در این گپ پاک شد.\n⏳ تا ۲۴ ساعت آینده نمی‌توانید در این گروه کشور جدید بسازید.\n⚠️ چون امروز حمله انجام داده‌اید، تا <b>{lockUntilDel:yyyy/MM/dd HH:mm}</b> (تهران) در همه گروه‌ها از حمله کردن قفل هستید.",
                        parseMode: ParseMode.Html, ct: ct);
                }
                else
                {
                    EndSession(uid);
                    await SendTemp(chat.Id, "✅ اطلاعات شما در این گپ پاک شد.\n⏳ تا ۲۴ ساعت آینده نمی‌توانید در این گروه کشور جدید بسازید.", ct: ct);
                }
                return;
            }
            if (txt == "خیر")
            {
                EndSession(uid);
                await SendTemp(chat.Id, "عملیات لغو شد.", ct: ct);
                return;
            }
        }

        if (txt == "دارایی" || txt == "داراییم" || txt == "کشورم" || txt == "کشور من")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return; }
            await SendCountryInfo(chat.Id, country, ct);
            return;
        }

        if (txt == "مان پاور" || txt == "مان‌پاور" || txt == "مانپاور" || txt == "قدرت نظامی" || txt == "قدرت")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return; }
            long manpower = CalcManpower(country);
            double popPower = (country.Population / 1000.0) * (country.Welfare / 100.0);
            double nonTaxIncome = CalcBuildingMoney(country) + CalcIronIncome(country);
            double incomePower = nonTaxIncome / 20.0;
            double groundPower = (country.Soldiers / 20.0) + (country.Tanks * 15);
            double airPower = (country.Planes * 12) + (country.Bombers * 25);
            double otherPower = (country.Cities * 50) + (country.AntiAir * 8) + (country.RecruitmentRate * 40) + (country.DefenseWins * 30);
            string breakdown = $"⚡ وضعیت مان‌پاور (قدرت نظامی) کشور {country.Name}:\n\n" +
                               $"🎖 مان‌پاور کل: {manpower / 1000.0:F1}K ({manpower:N0})\n\n" +
                               $"📊 جزئیات تاثیرگذاری (حدودی):\n" +
                               $"👥 جمعیت و رفاه: +{popPower / 1000.0:F1}K (تاثیر بسیار کم)\n" +
                               $"🏭 درآمدهای غیر مالیاتی: +{incomePower / 1000.0:F1}K (تاثیر متوسط)\n" +
                               $"🪖 ارتش زمینی (سرباز و تانک): +{groundPower / 1000.0:F1}K (تاثیر زیاد)\n" +
                               $"✈️ ارتش هوایی (جنگنده و بمب‌افکن): +{airPower / 1000.0:F1}K (تاثیر متوسط رو به بالا)\n" +
                               $"🏙 شهرها، پدافند، سربازگیری و دفاع: +{otherPower / 1000.0:F1}K (سایر عوامل)\n\n" +
                               $"ℹ️ مان‌پاور نشان‌دهنده قدرت انسانی و نظامی کشور شماست و به صورت حدودی با واحد K نمایش داده می‌شود.";
            await SendTemp(chat.Id, breakdown, ct: ct);
            return;
        }

        if (txt == "لیست کشور ها" || txt == "لیست کشورها" || txt == "کشور ها" || txt == "کشورها" || txt == "لیست" || txt == "برترین ها" || txt == "رتبه بندی" || txt == "لیدربورد" || txt == "قدرت ها" || txt == "قدرتمندترین ها")
        {
            var allInGroup = Database.GetCountriesByChatId(chat.Id)
                                     .OrderByDescending(c => CalcManpower(c))
                                     .Take(50)
                                     .ToList();
            if (allInGroup.Count == 0)
            {
                await SendTemp(chat.Id, "هنوز هیچ کشوری در این گروه ثبت نشده است.\nبا دستور «انتخاب کشور» اولین کشور را بسازید!", ct: ct);
                return;
            }
            var sb = new StringBuilder("🌍 لیست کشورهای گروه (بر اساس مان‌پاور حدودی):\n\n");
            for (int i = 0; i < allInGroup.Count; i++)
            {
                var c = allInGroup[i];
                string owner = (string.IsNullOrWhiteSpace(c.OwnerName) ? $"کاربر {c.OwnerId}" : c.OwnerName).Trim();
                string shortName = (c.Name.Length > 20 ? c.Name.Substring(0, 20) + "…" : c.Name).Trim();
                double mpK = CalcManpower(c) / 1000.0;
                string prefix = i == 0 ? "🥇 " : (i == 1 ? "🥈 " : (i == 2 ? "🥉 " : $"{i + 1}. "));
                sb.AppendLine($"{prefix}{owner} - {shortName} - {mpK:F1}K");
            }
            await SendTemp(chat.Id, sb.ToString(), ct: ct);
            return;
        }

        if (txt == "ساخت اتحاد" || txt == "ایجاد اتحاد" || txt == "تاسیس اتحاد")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return; }
            long curAid = Database.GetUserAllianceId(chat.Id, uid);
            if (curAid > 0)
            {
                await SendTemp(chat.Id, "❌ شما در حال حاضر در یک اتحاد عضو هستید! برای ساخت اتحاد جدید ابتدا با دستور «خروج از اتحاد» یا «انحلال اتحاد» از اتحاد فعلی خارج شوید.", ct: ct);
                return;
            }
            int totalPlayers = Database.GetCountriesByChatId(chat.Id).Count;
            int maxAlliances = Math.Max(1, totalPlayers / 2);
            var alliancesInChat = Database.GetAlliancesByChatId(chat.Id);
            if (alliancesInChat.Count >= maxAlliances)
            {
                await SendTemp(chat.Id, $"⛔ سقف تعداد اتحادهای مجاز در این گروه پر شده است!\n\n👥 تعداد بازیکنان گروه: {totalPlayers} نفر\n🏛 سقف مجاز اتحادها: {maxAlliances} اتحاد (به ازای هر ۲ بازیکن ۱ اتحاد)\n\n💡 برای ساخت اتحاد جدید، یا باید تعداد بازیکنان گروه بیشتر شود و یا یکی از اتحادهای فعلی منحل گردد.", ct: ct);
                return;
            }
            sessions[uid] = new UserSession { Step = SessionStep.WaitingAllianceName, AllianceChatId = chat.Id };
            await SendPrompt(uid, chat.Id, "🏛 نام اتحاد خود را ارسال کنید:", ct: ct);
            return;
        }

        if (txt == "ایجاد درخواست عضویت" || txt == "درخواست عضویت" || txt == "دعوت به اتحاد" || txt == "دعوت")
        {
            var leaderCountry = Database.GetCountry(uid, chat.Id);
            if (leaderCountry == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return; }
            long aid = Database.GetUserAllianceId(chat.Id, uid);
            if (aid == 0)
            {
                await SendTemp(chat.Id, "❌ شما در هیچ اتحادی عضو نیستید. ابتدا با دستور «ساخت اتحاد»، اتحاد خود را بسازید.", replyTo: msg.MessageId, ct: ct);
                return;
            }
            var alliance = Database.GetAllianceById(aid);
            if (alliance == null || alliance.LeaderId != uid)
            {
                await SendTemp(chat.Id, "❌ فقط رهبر اتحاد می‌تواند درخواست عضویت ارسال کند!", replyTo: msg.MessageId, ct: ct);
                return;
            }
            if (msg.ReplyToMessage == null || msg.ReplyToMessage.From == null || msg.ReplyToMessage.From.IsBot)
            {
                await SendTemp(chat.Id, "❌ برای ارسال دعوت، باید روی پیام بازیکن مورد نظر ریپلای کنید.", replyTo: msg.MessageId, ct: ct);
                return;
            }
            long tgtId = msg.ReplyToMessage.From.Id;
            if (tgtId == uid) { await SendTemp(chat.Id, "❌ نمی‌توانید خودتان را دعوت کنید!", replyTo: msg.MessageId, ct: ct); return; }
            var tgtCountry = Database.GetCountry(tgtId, chat.Id);
            if (tgtCountry == null) { await SendTemp(chat.Id, "❌ بازیکن مورد نظر در این گپ کشوری ندارد.", replyTo: msg.MessageId, ct: ct); return; }
            if (Database.GetUserAllianceId(chat.Id, tgtId) > 0) { await SendTemp(chat.Id, "❌ این بازیکن در حال حاضر در یک اتحاد دیگر عضو است!", replyTo: msg.MessageId, ct: ct); return; }
            int totPlayers = Database.GetCountriesByChatId(chat.Id).Count;
            int maxMembers = Math.Max(2, totPlayers / 2);
            if (Database.GetAllianceMembers(aid).Count >= maxMembers)
            {
                await SendTemp(chat.Id, $"⛔ ظرفیت اتحاد تکمیل است! سقف: {maxMembers} نفر", replyTo: msg.MessageId, ct: ct);
                return;
            }
            if (IsSuperpowerCollision(chat.Id, uid, tgtId, out string reason))
            {
                await SendTemp(chat.Id, reason, replyTo: msg.MessageId, ct: ct);
                return;
            }
            var inv = new AllianceInvite { AllianceId = aid, ChatId = chat.Id, TargetUserId = tgtId, LeaderId = uid, CreatedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
            long invId = Database.AddAllianceInvite(inv);
            await SendTemp(chat.Id, "✅ درخواست عضویت ارسال شد.", replyTo: msg.MessageId, ct: ct);
            await SendTemp(chat.Id, $"💌 درخواست پیوستن به اتحاد «{alliance.Name}» برای شما ارسال شد، برای تایید یا رد به پیوی مراجعه کنید.", replyTo: msg.ReplyToMessage.MessageId, ct: ct);
            string gTitle = $"گروه {chat.Id}";
            try { var ch = await bot.GetChatAsync(chat.Id, ct); if (!string.IsNullOrEmpty(ch.Title)) gTitle = ch.Title; } catch { }
            var kb = new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("✅ تایید و پیوستن", $"ally_accept:{invId}"), InlineKeyboardButton.WithCallbackData("❌ رد درخواست", $"ally_reject:{invId}") } });
            try { await bot.SendTextMessageAsync(tgtId, $"💌 **دعوت‌نامه رسمی اتحاد**\n\n👑 رهبر اتحاد «{alliance.Name}» ({FullName(user)}) در گپ «{gTitle}» از شما دعوت کرده است.", replyMarkup: kb, cancellationToken: ct); }
            catch { await SendTemp(chat.Id, $"⚠️ ارسال پیام به پیوی کاربر {tgtId} ممکن نشد.", replyTo: msg.ReplyToMessage.MessageId, ct: ct); }
            return;
        }

        if (txt == "ترنسفر" || txt == "انتقال" || txt == "ارسال محموله" || txt == "ارسال منابع")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return; }
            if (country.PortLevel < 3) { await SendTemp(chat.Id, "⚓ سطح بندر شما برای این عملیات کافی نیست! (حداقل سطح مورد نیاز: ۳)", replyTo: msg.MessageId, ct: ct); return; }
            await SendTemp(chat.Id, "📦 برای ارسال محموله اقتصادی و نظامی به متحدان خود، به پیوی ربات مراجعه کنید.", replyTo: msg.MessageId, ct: ct);
            try
            {
                long aid = Database.GetUserAllianceId(chat.Id, uid);
                if (aid == 0) { await bot.SendTextMessageAsync(uid, "❌ شما در آن گروه عضو هیچ اتحادی نیستید.", cancellationToken: ct); return; }
                var mems = Database.GetAllianceMembers(aid).Where(m => m != uid).ToList();
                if (mems.Count == 0) { await bot.SendTextMessageAsync(uid, "❌ اتحاد شما عضو دیگری ندارد.", cancellationToken: ct); return; }
                if (GetTransferCount(chat.Id, uid) >= MAX_TRANSFERS_PER_UPDATE && !Database.HasGroupLockExemption(chat.Id))
                { await bot.SendTextMessageAsync(uid, $"⛔ سهمیه ترنسفر تمام شد ({MAX_TRANSFERS_PER_UPDATE}).", cancellationToken: ct); return; }
                sessions[uid] = new UserSession { Step = SessionStep.TransferWaitingResource, TransferChatId = chat.Id, TransferAllianceId = aid };
                var kb = new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData("💰 پول", $"tf_res:{chat.Id}:money"), InlineKeyboardButton.WithCallbackData("🔩 آهن", $"tf_res:{chat.Id}:iron") },
                    new[] { InlineKeyboardButton.WithCallbackData("🪖 سرباز", $"tf_res:{chat.Id}:soldiers"), InlineKeyboardButton.WithCallbackData("🛡 تانک", $"tf_res:{chat.Id}:tanks") },
                    new[] { InlineKeyboardButton.WithCallbackData("✈️ جنگنده", $"tf_res:{chat.Id}:planes"), InlineKeyboardButton.WithCallbackData("🛩 بمب‌افکن", $"tf_res:{chat.Id}:bombers") },
                    new[] { InlineKeyboardButton.WithCallbackData("🚤 قایق", $"tf_res:{chat.Id}:boats"), InlineKeyboardButton.WithCallbackData("⚓ زیردریایی", $"tf_res:{chat.Id}:submarines") },
                    new[] { InlineKeyboardButton.WithCallbackData("🚢 نبردناو", $"tf_res:{chat.Id}:battleships") }
                });
                await bot.SendTextMessageAsync(uid, "📦 **ترنسفر**\n\nنوع منبع را انتخاب کنید:", replyMarkup: kb, cancellationToken: ct);
            }
            catch { }
            return;
        }

        if (txt == "صف آرایی تهاجمی" || txt == "صف آرایی دفاعی" || txt == "صف‌آرایی تهاجمی" || txt == "صف‌آرایی دفاعی")
        {
            bool isOff = txt.Contains("تهاجمی");
            long cid = chat.Id;
            var sc = Database.GetCountry(uid, cid);
            if (sc == null) { await SendTemp(cid, MsgNoCountryGuide, ct: ct); return; }
            if (sc.PortLevel < 3) { await SendTemp(cid, "⚓ سطح بندر شما برای این عملیات کافی نیست! (حداقل سطح مورد نیاز: ۳)", ct: ct); return; }
            long aid = Database.GetUserAllianceId(cid, uid);
            if (aid == 0) { await SendTemp(cid, "❌ فقط اعضای اتحاد می‌توانند صف‌آرایی کنند.", ct: ct); return; }
            var mems = Database.GetAllianceMembers(aid);
            int dailyLimit = mems.Count <= 5 ? 1 : (mems.Count <= 10 ? 2 : (mems.Count <= 20 ? 3 : 5));
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (Database.GetRecentAllianceDeploymentsCount(aid, nowMs - 86400000L) >= dailyLimit && !Database.HasGroupLockExemption(cid))
            { await SendTemp(cid, $"⛔ سقف روزانه صف‌آرایی ({dailyLimit}) پر شد.", ct: ct); return; }
            var tgts = isOff ? Database.GetAttackableTargets(cid, uid) : mems.Select(m => Database.GetCountry(m, cid)).Where(c => c != null).ToList()!;
            if (tgts.Count == 0) { await SendTemp(cid, isOff ? "❌ هیچ هدفی خارج از اتحاد وجود ندارد." : "❌ عضو معتبری برای دفاع وجود ندارد.", ct: ct); return; }
            await SendTemp(cid, "⚔️ برای تنظیم اسکجولر به پی‌وی ربات مراجعه کنید.", replyTo: msg.MessageId, ct: ct);
            var tkb = tgts.Select(t => new[] { InlineKeyboardButton.WithCallbackData($"🏳️ {t!.Name} ({t.OwnerName})", $"dep_target:{cid}:{aid}:{(isOff ? "Off" : "Def")}:{t.OwnerId}") }).ToArray();
            try { await SendPrompt(uid, uid, $"⚔️ **اعلام صف‌آرایی {(isOff ? "تهاجمی" : "دفاعی")}**\n\n🎯 کشور مورد نظر:", new InlineKeyboardMarkup(tkb), ct); } catch { }
            return;
        }

        if (txt == "لغو صف آرایی" || txt == "لغو صف‌آرایی" || txt == "حذف صف آرایی" || txt == "حذف صف‌آرایی")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return; }
            long aid = Database.GetUserAllianceId(chat.Id, uid);
            if (aid == 0) { await SendTemp(chat.Id, "❌ شما عضو هیچ اتحادی نیستید.", ct: ct); return; }
            var alliance = Database.GetAllianceById(aid);
            if (alliance == null) return;
            var deps = Database.GetActiveDeployments().Where(d => d.ChatId == chat.Id && d.AllianceId == aid).ToList();
            if (deps.Count == 0) { await SendTemp(chat.Id, "❌ هیچ صف‌آرایی فعالی از اتحاد شما نیست.", ct: ct); return; }
            var myDeps = deps.Where(d => d.InitiatorId == uid || alliance.LeaderId == uid).ToList();
            if (myDeps.Count == 0) { await SendTemp(chat.Id, "❌ شما دسترسی لغو این صف‌آرایی‌ها را ندارید.", ct: ct); return; }
            if (myDeps.Count == 1)
            {
                await CancelDeploymentSafely(myDeps[0], ct);
                await SendTemp(chat.Id, "🚫 **صف‌آرایی لغو شد!**", ct: ct);
            }
            else
            {
                var kb = myDeps.Select(d => { var tc = Database.GetCountry(d.TargetUserId, chat.Id); string tn = tc?.Name ?? $"کاربر {d.TargetUserId}"; return new[] { InlineKeyboardButton.WithCallbackData($"❌ لغو {(d.Type == "Offensive" ? "حمله" : "دفاع")} {tn}", $"dep_cancel:{d.Id}") }; }).ToArray();
                await SendTemp(chat.Id, "🚫 کدام عملیات لغو شود؟", markup: new InlineKeyboardMarkup(kb), ct: ct);
            }
            return;
        }

        if (txt == "اعزام نیرو" || txt == "مشارکت" || txt == "مشارکت در صف آرایی" || txt == "مشارکت در صف‌آرایی" || txt == "اعزام" || txt == "اعزام نیرو ها" || txt == "اعزام نیروها")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return; }
            if (country.PortLevel < 3) { await SendTemp(chat.Id, "⚓ سطح بندر شما برای این عملیات کافی نیست! (حداقل سطح مورد نیاز: ۳)", ct: ct); return; }
            long aid = Database.GetUserAllianceId(chat.Id, uid);
            if (aid == 0) { await SendTemp(chat.Id, "❌ شما عضو هیچ اتحادی نیستید.", ct: ct); return; }
            var deps = Database.GetActiveDeployments().Where(d => d.ChatId == chat.Id && d.AllianceId == aid).ToList();
            if (deps.Count == 0) { await SendTemp(chat.Id, "❌ هیچ صف‌آرایی فعالی از اتحاد شما نیست.", ct: ct); return; }
            var kb = deps.Select(d => { var tc = Database.GetCountry(d.TargetUserId, chat.Id); string tn = tc?.Name ?? $"کاربر {d.TargetUserId}"; return new[] { InlineKeyboardButton.WithCallbackData($"⚔️ {(d.Type == "Offensive" ? "حمله" : "دفاع")} {tn}", $"dep_join:{d.Id}") }; }).ToArray();
            await SendTemp(chat.Id, "⚔️ صف‌آرایی‌های فعال:", markup: new InlineKeyboardMarkup(kb), ct: ct);
            return;
        }

        if (txt == "لیست اتحاد ها" || txt == "لیست اتحادها" || txt == "اتحاد ها" || txt == "اتحادها")
        {
            var alliances = Database.GetAlliancesByChatId(chat.Id);
            if (alliances.Count == 0) { await SendTemp(chat.Id, "هنوز هیچ اتحادی در این گروه نیست.", ct: ct); return; }
            var listWithMp = alliances.Select(a =>
            {
                var members = Database.GetAllianceMembers(a.Id);
                double totalMp = members.Sum(m => { var c = Database.GetCountry(m, chat.Id); return c != null ? CalcManpower(c) : 0; });
                return new { Alliance = a, MembersCount = members.Count, TotalMp = totalMp };
            }).OrderByDescending(x => x.TotalMp).ToList();
            var sb = new StringBuilder("🏆 لیست اتحادهای گروه:\n\n");
            for (int i = 0; i < listWithMp.Count; i++)
            {
                var item = listWithMp[i];
                string prefix = i == 0 ? "🥇 " : (i == 1 ? "🥈 " : (i == 2 ? "🥉 " : $"{i + 1}. "));
                var lc = Database.GetCountry(item.Alliance.LeaderId, chat.Id);
                sb.AppendLine($"{prefix}«{item.Alliance.Name}» — ⚡ {item.TotalMp / 1000.0:F1}K\n   👑 رهبر: {lc?.OwnerName ?? $"کاربر {item.Alliance.LeaderId}"} | 👥 اعضا: {item.MembersCount}");
            }
            await SendTemp(chat.Id, sb.ToString(), ct: ct);
            return;
        }

        if (txt == "وضعیت اتحاد")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return; }
            long aid = Database.GetUserAllianceId(chat.Id, uid);
            if (aid == 0) { await SendTemp(chat.Id, "❌ شما در هیچ اتحادی عضو نیستید.", ct: ct); return; }
            var alliance = Database.GetAllianceById(aid);
            if (alliance == null) { await SendTemp(chat.Id, "❌ اطلاعات اتحاد یافت نشد.", ct: ct); return; }
            var memIds = Database.GetAllianceMembers(aid);
            var memCountries = memIds.Select(m => Database.GetCountry(m, chat.Id)).Where(c => c != null).OrderByDescending(c => CalcManpower(c!)).ToList();
            double totalMp = memCountries.Sum(c => CalcManpower(c!));
            var sb = new StringBuilder();
            sb.AppendLine($"🛡 وضعیت اتحاد «{alliance.Name}»");
            sb.AppendLine($"⚡ مان‌پاور کل: {totalMp / 1000.0:F1}K\n👥 رده‌بندی:");
            for (int i = 0; i < memCountries.Count; i++)
            {
                var c = memCountries[i]!;
                string role = c.OwnerId == alliance.LeaderId ? "👑 رهبر" : "👤 عضو";
                string sn = c.Name.Length > 20 ? c.Name.Substring(0, 20) + "…" : c.Name;
                sb.AppendLine($"{i + 1}. {role}: {c.OwnerName} — {sn} — ⚡ {CalcManpower(c) / 1000.0:F1}K");
            }
            if (uid == alliance.LeaderId) { sb.AppendLine("\n💡 برای اخراج: «حذف N» — برای انحلال: «انحلال اتحاد»"); sessions[uid] = new UserSession { Step = SessionStep.LeaderWaitingKickMember, AllianceChatId = chat.Id, AllianceId = alliance.Id }; }
            else { sb.AppendLine("\n💡 برای خروج: «خروج از اتحاد»"); }
            if (!string.IsNullOrEmpty(alliance.FlagFileId)) await SendTempPhoto(chat.Id, alliance.FlagFileId, sb.ToString(), ct: ct);
            else await SendTemp(chat.Id, sb.ToString(), ct: ct);
            return;
        }

        if (txt.StartsWith("حذف "))
        {
            var parts = txt.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && TryParseInt(parts[1], out int rank))
            {
                long aid = Database.GetUserAllianceId(chat.Id, uid);
                if (aid > 0)
                {
                    var alliance = Database.GetAllianceById(aid);
                    if (alliance != null && alliance.LeaderId == uid)
                    {
                        var memIds = Database.GetAllianceMembers(alliance.Id);
                        var memCountries = memIds.Select(m => Database.GetCountry(m, chat.Id)).Where(c => c != null).OrderByDescending(c => CalcManpower(c!)).ToList();
                        if (rank == 1) { await SendTemp(chat.Id, "❌ نمی‌توانید خودتان را اخراج کنید!", ct: ct); return; }
                        if (rank < 1 || rank > memCountries.Count) { await SendTemp(chat.Id, $"❌ شماره نامعتبر (۱ تا {memCountries.Count})", ct: ct); return; }
                        var tc = memCountries[rank - 1]!;
                        Database.RemoveAllianceMember(alliance.Id, chat.Id, tc.OwnerId);
                        await SendTemp(chat.Id, $"🚫 {tc.OwnerName} از اتحاد اخراج شد!", ct: ct);
                        try { await bot.SendTextMessageAsync(tc.OwnerId, $"🚫 شما از اتحاد «{alliance.Name}» اخراج شدید.", cancellationToken: ct); } catch { }
                        return;
                    }
                }
            }
        }

        if (txt == "انحلال اتحاد" || txt == "حذف اتحاد")
        {
            long aid = Database.GetUserAllianceId(chat.Id, uid);
            if (aid > 0)
            {
                var alliance = Database.GetAllianceById(aid);
                if (alliance != null && alliance.LeaderId == uid)
                {
                    Database.DeleteAlliance(alliance.Id);
                    if (sessions.ContainsKey(uid)) EndSession(uid);
                    await SendTemp(chat.Id, $"💥 اتحاد «{alliance.Name}» منحل شد!", ct: ct);
                    return;
                }
                else if (alliance != null) { await SendTemp(chat.Id, "❌ فقط رهبر می‌تواند اتحاد را منحل کند.", ct: ct); return; }
            }
        }

        if (txt == "خروج از اتحاد" || txt == "ترک اتحاد")
        {
            long aid = Database.GetUserAllianceId(chat.Id, uid);
            if (aid > 0)
            {
                var alliance = Database.GetAllianceById(aid);
                if (alliance != null && alliance.LeaderId == uid) { await SendTemp(chat.Id, "❌ شما رهبر هستید. از «انحلال اتحاد» استفاده کنید.", ct: ct); return; }
                else if (alliance != null)
                {
                    var c = Database.GetCountry(uid, chat.Id);
                    Database.RemoveAllianceMember(alliance.Id, chat.Id, uid);
                    await SendTemp(chat.Id, $"👋 {c?.OwnerName ?? $"کاربر {uid}"} از اتحاد خارج شد!", ct: ct);
                    try { await bot.SendTextMessageAsync(alliance.LeaderId, $"👋 {c?.OwnerName ?? $"کاربر {uid}"} از اتحاد خارج شد.", cancellationToken: ct); } catch { }
                    return;
                }
            }
        }

        if (txt == "راهنما")
        {
            // FIX(4): راهنمای کامل (در گروه)
            await SendTemp(chat.Id, HelpText, parseMode: ParseMode.Html, ct: ct);
            return;
        }

        if (txt == "خرید تانک" || txt == "ساخت تانک")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return; }
            InlineKeyboardMarkup tk = country.Faction switch
            {
                Faction.USA => new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("🇺🇸 M2 Medium", $"tank_info:{uid}:M2Medium") } }),
                Faction.USSR => new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("🇷🇺 T-28", $"tank_info:{uid}:T28") } }),
                _ => new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("🇩🇪 Panzer III", $"tank_info:{uid}:PanzerIII") } })
            };
            await SendTemp(chat.Id, "🛡️ تانک:", markup: tk, ct: ct);
            return;
        }

        if (txt == "خرید هواپیما" || txt == "ساخت هواپیما" || txt == "خرید جنگنده" || txt == "ساخت جنگنده")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return; }
            var fb = country.Faction switch
            {
                Faction.USA => new[] { InlineKeyboardButton.WithCallbackData("🇺🇸 P-36", $"plane_info:{uid}:P36"), InlineKeyboardButton.WithCallbackData("🇺🇸 B-17", $"bomber_info:{uid}:B17") },
                Faction.USSR => new[] { InlineKeyboardButton.WithCallbackData("🇷🇺 I-16", $"plane_info:{uid}:I16"), InlineKeyboardButton.WithCallbackData("🇷🇺 DB-3", $"bomber_info:{uid}:DB3") },
                _ => new[] { InlineKeyboardButton.WithCallbackData("🇩🇪 Bf 109", $"plane_info:{uid}:Bf109"), InlineKeyboardButton.WithCallbackData("🇩🇪 He 111", $"bomber_info:{uid}:He111") }
            };
            await SendTemp(chat.Id, "🛩️ نیروی هوایی:", markup: new InlineKeyboardMarkup(new[] { new[] { fb[0] }, new[] { fb[1] } }), ct: ct);
            return;
        }

        if (txt == "خرید بمب افکن" || txt == "ساخت بمب افکن" || txt == "خرید بمب‌افکن" || txt == "ساخت بمب‌افکن")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return; }
            var bk = country.Faction switch
            {
                Faction.USA => new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("🇺🇸 B-17", $"bomber_info:{uid}:B17") } }),
                Faction.USSR => new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("🇷🇺 DB-3", $"bomber_info:{uid}:DB3") } }),
                _ => new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("🇩🇪 He 111", $"bomber_info:{uid}:He111") } })
            };
            await SendTemp(chat.Id, "🛩️ بمب‌افکن:", markup: bk, ct: ct);
            return;
        }

        if (txt == "پدافند" || txt == "خرید پدافند" || txt == "ساخت پدافند" || txt == "ضدهوایی" || txt == "ضد هوایی" || txt == "خرید ضد هوایی" || txt == "ساخت ضد هوایی")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return; }
            await SendTemp(chat.Id, "🎯 پدافند:", markup: new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("🎯 توپ ۷۶ میلی‌متری", $"aa_info:{uid}:AA76") } }), ct: ct);
            return;
        }

        if (txt == "خرید ناو" || txt == "ساخت ناو" || txt == "خرید کشتی" || txt == "ساخت کشتی" || txt == "خرید قایق" || txt == "ساخت قایق" || txt == "نیروی دریایی" || txt == "ناوگان")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return; }
            // Check port level for battleship info
            string portInfo = country.PortLevel < 4 ? "\n⚠️ برای ساخت نبردناو بندر سطح ۴ لازم است" : "";
            // Keep the purchase menu read-only and fast. Per-ship damage is loaded only
            // when the user opens repair/scrap details, not on every «خرید ناو» command.
            string dmgInfo = $"\n🚢 نبردناو: کل {country.Battleships+country.BattleshipsAtSea}/3 | آماده در بندر {country.Battleships}";
            string seaInfo = (country.BoatsAtSea + country.SubmarinesAtSea + country.BattleshipsAtSea) > 0 ? $"\n🌊 در دریا: {country.BoatsAtSea}🚤 {country.SubmarinesAtSea}⚓ {country.BattleshipsAtSea}🚢" : "";
            var navalKb = country.Faction switch
            {
                Faction.USA => new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData("🚤 PT Boat (قایق) – 5 عدد", $"boat_info:{uid}:PTBoat") },
                    new[] { InlineKeyboardButton.WithCallbackData("🚢 Gato (زیردریایی)", $"sub_info:{uid}:Gato") },
                    new[] { InlineKeyboardButton.WithCallbackData("⚓ Iowa (نبردناو)", $"battleship_info:{uid}:Iowa") },
                    new[] { InlineKeyboardButton.WithCallbackData("🔧 تعمیر نبردناو", $"battleship_repair:{uid}"), InlineKeyboardButton.WithCallbackData("♻️ اوراق نبردناو", $"battleship_scrap_menu:{uid}") }
                }),
                Faction.USSR => new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData("🚤 G-5 (قایق) – 5 عدد", $"boat_info:{uid}:G5") },
                    new[] { InlineKeyboardButton.WithCallbackData("🚢 S-class (زیردریایی)", $"sub_info:{uid}:SClass") },
                    new[] { InlineKeyboardButton.WithCallbackData("⚓ Sovetsky Soyuz (نبردناو)", $"battleship_info:{uid}:Soyuz") },
                    new[] { InlineKeyboardButton.WithCallbackData("🔧 تعمیر نبردناو", $"battleship_repair:{uid}"), InlineKeyboardButton.WithCallbackData("♻️ اوراق نبردناو", $"battleship_scrap_menu:{uid}") }
                }),
                _ => new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData("🚤 S-Boot (قایق) – 5 عدد", $"boat_info:{uid}:SBoot") },
                    new[] { InlineKeyboardButton.WithCallbackData("🚢 Type VIIC (زیردریایی)", $"sub_info:{uid}:VIIC") },
                    new[] { InlineKeyboardButton.WithCallbackData("⚓ Bismarck (نبردناو)", $"battleship_info:{uid}:Bismarck") },
                    new[] { InlineKeyboardButton.WithCallbackData("🔧 تعمیر نبردناو", $"battleship_repair:{uid}"), InlineKeyboardButton.WithCallbackData("♻️ اوراق نبردناو", $"battleship_scrap_menu:{uid}") }
                })
            };
            await SendTemp(chat.Id, $"⚓ نیروی دریایی – فکشن {country.Faction}{portInfo}{dmgInfo}{seaInfo}\nبرای اطلاعات هر واحد روی دکمه بزنید:", markup: navalKb, ct: ct);
            return;
        }

        if (txt == "تعمیر ناو" || txt == "تعمیر ناوگان" || txt == "تعمیر کشتی" || txt == "تعمیر ناو جنگی" || txt == "تعمیرات ناو")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return; }
            Database.SyncBattleshipUnits(uid, chat.Id);
            var damaged = Database.GetBattleshipUnits(uid, chat.Id, onlyCombatReady: false).Where(x => x.DamagePercent > 0).ToList();
            if (damaged.Count == 0) { await SendTemp(chat.Id, "✅ نبردناو آسیب‌دیده‌ای ندارید.", ct: ct); return; }
            var rows = damaged.Select(x => new[] { InlineKeyboardButton.WithCallbackData($"🔧 {x.Model} شماره {x.ShipNumber} — {x.DamagePercent}٪", $"battleship_repair_quote:{x.UnitId}") }).ToList();
            rows.Add(new[] { InlineKeyboardButton.WithCallbackData("❌ لغو", "cancel") });
            await SendTemp(chat.Id, "🔧 نبردناو موردنظر را انتخاب کنید. هزینه تعمیر برابر درصد واقعی آسیب از قیمت پول و آهن همان مدل است.", markup: new InlineKeyboardMarkup(rows), ct: ct);
            return;
        }

        if (txt == "اوراق نبردناو" || txt == "اوراق ناو" || txt == "اسقاط نبردناو" || txt == "اسقاط ناو")
        {
            var country=Database.GetCountry(uid,chat.Id);
            if(country==null){await SendTemp(chat.Id,MsgNoCountryGuide,ct:ct);return;}
            Database.SyncBattleshipUnits(uid,chat.Id);
            var ships=Database.GetBattleshipUnits(uid,chat.Id,onlyCombatReady:false);
            if(ships.Count==0){await SendTemp(chat.Id,"❌ نبردناو آماده‌ای برای اوراق ندارید. ناوهای در مأموریت یا انتقال قابل اوراق نیستند.",ct:ct);return;}
            var rows=ships.Select(x=>new[]{InlineKeyboardButton.WithCallbackData($"♻️ {x.Model} شماره {x.ShipNumber} — آسیب {x.DamagePercent}٪",$"battleship_scrap:{x.UnitId}")}).ToList();
            rows.Add(new[]{InlineKeyboardButton.WithCallbackData("❌ لغو","cancel")});
            await SendTemp(chat.Id,"♻️ نبردناو موردنظر را انتخاب کنید. پس از تأیید، ۵۰٪ قیمت ساخت پول و آهن همان مدل برمی‌گردد.",markup:new InlineKeyboardMarkup(rows),ct:ct);
            return;
        }

        if (txt == "تغییر اسم" || txt == "تعویض اسم" || txt == "تغییر اسم کشور" || txt == "تعویض اسم کشور")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return; }
            sessions[uid] = new UserSession { Step = SessionStep.WaitingNewName, ChatId = chat.Id };
            await SendPrompt(uid, chat.Id, "اسم جدید را ارسال کنید.", ct: ct);
            return;
        }

        if (txt == "ترید")
        {
            long r = Database.GetRoyalCoins(uid);
            sessions[uid] = new UserSession { Step = SessionStep.WaitingTradeAmount, ChatId = chat.Id };
            await SendPrompt(uid, chat.Id, $"💎 رویال: {r}\n\nچند رویال تبدیل کنید? (هر رویال = 10K)", ct: ct);
            return;
        }

        if (txt == "آموزش سرباز" || txt == "نرخ سرباز گیری" || txt == "نرخ سربازگیری")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return; }
            sessions[uid] = new UserSession { Step = SessionStep.WaitingRecruitmentRate, ChatId = chat.Id };
            await SendPrompt(uid, chat.Id, $"🎯 نرخ فعلی: {country.RecruitmentRate}\nعدد 0 تا 10:", ct: ct);
            return;
        }

        if (txt == "مالیات" || txt == "نرخ مالیات")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return; }
            sessions[uid] = new UserSession { Step = SessionStep.WaitingTaxRate, ChatId = chat.Id };
            long est = CalcTaxIncome(country);
            await SendPrompt(uid, chat.Id, $"💰 نرخ فعلی: {country.TaxRate}%\n📈 برآورد: {est / 1000.0:F1}K\nعدد 0 تا 100:", ct: ct);
            return;
        }

        if (txt == "تغییر پرچم" || txt == "تعویض پرچم" || txt == "پرچم")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return; }
            sessions[uid] = new UserSession { Step = SessionStep.WaitingNewFlag, ChatId = chat.Id };
            await SendPrompt(uid, chat.Id, "عکس پرچم جدید را ارسال کنید.", ct: ct);
            return;
        }

        if (sessions.TryGetValue(uid, out var renameSess) && renameSess != null && renameSess.Step == SessionStep.WaitingNewName)
        {
            if (string.IsNullOrWhiteSpace(txt)) { await SendPrompt(uid, chat.Id, "اسم معتبر بفرستید.", ct: ct); return; }
            if (Database.CountryNameExists(txt)) { await SendPrompt(uid, chat.Id, "این اسم قبلاً استفاده شده.", ct: ct); return; }
            // Name similarity check >90% within same chat
            var existingCountryNames = Database.GetCountriesByChatId(chat.Id).Where(c => c.OwnerId != uid).Select(c => c.Name);
            if (IsNameTooSimilar(txt, existingCountryNames, 0.9))
            {
                await SendPrompt(uid, chat.Id, "❌ این نام خیلی شبیه به نام موجود است!! لطفاً نام دیگری انتخاب کنید.", ct: ct);
                return;
            }
            Database.UpdateCountryName(uid, chat.Id, txt);
            EndSession(uid);
            await SendTemp(chat.Id, $"✅ نام کشور به {txt} تغییر یافت.", ct: ct);
            return;
        }

        if (sessions.TryGetValue(uid, out var recSess) && recSess != null && recSess.Step == SessionStep.WaitingRecruitmentRate)
        {
            if (!TryParseInt(txt, out int rate) || rate < 0 || rate > 10) { await SendPrompt(uid, chat.Id, "❌ عدد 0 تا 10:", ct: ct); return; }
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { EndSession(uid); return; }
            country.RecruitmentRate = rate;
            Database.UpdateCountryFull(country);
            EndSession(uid);
            await SendTemp(chat.Id, $"✅ نرخ سربازگیری: {rate}\n🏥 هدف رفاه: {WelfareTarget(country):F0}%", ct: ct);
            return;
        }

        if (sessions.TryGetValue(uid, out var taxSess) && taxSess != null && taxSess.Step == SessionStep.WaitingTaxRate)
        {
            if (!TryParseInt(txt, out int tx) || tx < 0 || tx > 100) { await SendPrompt(uid, chat.Id, "❌ عدد 0 تا 100:", ct: ct); return; }
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { EndSession(uid); return; }
            country.TaxRate = tx;
            Database.UpdateCountryFull(country);
            EndSession(uid);
            long est = CalcTaxIncome(country);
            await SendTemp(chat.Id, $"✅ نرخ مالیات: {tx}%\n💰 برآورد: {est / 1000.0:F1}K\n🏥 هدف رفاه: {WelfareTarget(country):F0}%", ct: ct);
            return;
        }

        if (sessions.TryGetValue(uid, out var tradeSess) && tradeSess != null && tradeSess.Step == SessionStep.WaitingTradeAmount)
        {
            if (!TryParseLong(txt, out long ta) || ta <= 0) { await SendPrompt(uid, chat.Id, "عدد معتبر.", ct: ct); return; }
            long r = Database.GetRoyalCoins(uid);
            if (ta > r) { await SendPrompt(uid, chat.Id, $"رویال کافی نیست. موجودی: {r}", ct: ct); return; }
            var ctry = Database.GetCountry(uid, chat.Id);
            if (ctry == null) { EndSession(uid); return; }
            Database.AddRoyalCoins(uid, -ta);
            ctry.Money += ta * 10000L;
            Database.UpdateCountryResources(uid, chat.Id, ctry.Money, ctry.Iron, ctry.Tanks);
            EndSession(uid);
            await SendPermanent(chat.Id, $"✅ {ta} رویال تبدیل شد 💰 +{ta * 10}K", ct: ct);
            return;
        }

        if (sessions.TryGetValue(uid, out var flagSess) && flagSess != null && flagSess.Step == SessionStep.WaitingNewFlag)
        {
            if (msg.Photo == null || msg.Photo.Length == 0) { await SendPrompt(uid, chat.Id, "لطفاً عکس ارسال کنید.", ct: ct); return; }
            string fid = msg.Photo.Last().FileId;
            Database.UpdateCountryFlag(uid, chat.Id, fid);
            EndSession(uid);
            await SendTemp(chat.Id, "✅ پرچم تغییر کرد.", ct: ct);
            var country = Database.GetCountry(uid, chat.Id);
            if (country != null) await SendCountryInfo(chat.Id, country, ct);
            return;
        }

        if (sessions.TryGetValue(uid, out var allyFlagSess) && allyFlagSess != null && allyFlagSess.Step == SessionStep.WaitingAllianceFlag)
        {
            if (msg.Photo == null || msg.Photo.Length == 0) { await SendPrompt(uid, chat.Id, "عکس ارسال کنید.", ct: ct); return; }
            string fid = msg.Photo.Last().FileId;
            var al = new Alliance { ChatId = allyFlagSess.AllianceChatId, Name = allyFlagSess.AllianceName, FlagFileId = fid, LeaderId = uid, CreatedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
            long aid = Database.AddAlliance(al);
            EndSession(uid);
            await SendTemp(chat.Id, $"🎉 اتحاد «{al.Name}» تاسیس شد!\n👑 رهبر: {FullName(user)}", ct: ct);
            return;
        }

        if (sessions.TryGetValue(uid, out var allyNameSess) && allyNameSess != null && allyNameSess.Step == SessionStep.WaitingAllianceName)
        {
            if (string.IsNullOrWhiteSpace(txt)) { await SendPrompt(uid, chat.Id, "نام معتبر.", ct: ct); return; }
            if (Database.AllianceNameExists(chat.Id, txt)) { await SendPrompt(uid, chat.Id, "این نام قبلاً ثبت شده.", ct: ct); return; }
            var existingAllianceNames = Database.GetAlliancesByChatId(chat.Id).Select(a => a.Name);
            if (IsNameTooSimilar(txt, existingAllianceNames, 0.9))
            {
                await SendPrompt(uid, chat.Id, "❌ این نام خیلی شبیه به نام اتحاد موجود است!! لطفاً نام دیگری انتخاب کنید.", ct: ct);
                return;
            }
            allyNameSess.Step = SessionStep.WaitingAllianceFlag;
            allyNameSess.AllianceName = txt;
            await SendPrompt(uid, chat.Id, $"✅ نام: «{txt}»\n🚩 عکس پرچم را ارسال کنید:", ct: ct);
            return;
        }

        if (sessions.TryGetValue(uid, out var sess) && sess != null && sess.Step == SessionStep.WaitingCountryName)
        {
            long rm = Database.GetLeaveCooldownRemainingMs(uid, chat.Id);
            if (rm > 0) { EndSession(uid); await SendTemp(chat.Id, $"⛔ تا {FormatRemaining(rm)} نمی‌توانید کشور بسازید.", ct: ct); return; }
            if (string.IsNullOrWhiteSpace(txt)) { await SendPrompt(uid, chat.Id, "اسم معتبر.", ct: ct); return; }
            if (Database.CountryNameExists(txt)) { await SendPrompt(uid, chat.Id, "این اسم استفاده شده.", ct: ct); return; }
            var existingNamesForNew = Database.GetCountriesByChatId(chat.Id).Select(c => c.Name);
            if (IsNameTooSimilar(txt, existingNamesForNew, 0.9))
            {
                await SendPrompt(uid, chat.Id, "❌ این نام خیلی شبیه به نام موجود است!! لطفاً نام دیگری انتخاب کنید.", ct: ct);
                return;
            }
            var flags = Database.GetFactionFlags(sess.FactionStr);
            string flagId = flags.Count > 0 ? flags[rng.Next(flags.Count)] : "";
            var nc = new Country
            {
                ChatId = chat.Id, Name = txt, OwnerId = uid, OwnerName = FullName(user),
                Faction = sess.Faction, FlagFileId = flagId,
                Money = 10000, Population = 100000, FactoryLevel = 1, PortLevel = 1, MineLevel = 1, Iron = 0,
                CreatedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            try { Database.AddCountry(nc); } catch (Exception ex) { Console.WriteLine($"[AddCountry FAIL] {ex.Message}"); EndSession(uid); return; }
            EndSession(uid);
            await SendCountryInfo(chat.Id, nc, ct);
            return;
        }

        if (txt == "حمله")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return; }
            await SendTemp(chat.Id, "⚔️ برای مشخص‌کردن هدف به پیوی مراجعه کنید.", replyTo: msg.MessageId, ct: ct);
            var targets = Database.GetAttackableTargets(chat.Id, uid);
            if (targets.Count == 0) { await SendTemp(uid, "هیچ هدفی در این گروه وجود ندارد.", ct: ct); return; }
            var kb = targets.Select(t => new[] { InlineKeyboardButton.WithCallbackData($"{t.Name} ({t.OwnerName})", $"attack_target:{chat.Id}:{t.OwnerId}") }).ToArray();
            sessions[uid] = new UserSession { Step = SessionStep.AttackWaitingTarget, AttackChatId = chat.Id };
            await SendPrompt(uid, uid, "🎯 هدف را انتخاب کنید:", new InlineKeyboardMarkup(kb), ct);
            return;
        }

        if (txt == "وضعیت دفاع")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return; }
            await SendTemp(chat.Id, "🛡 وضعیت دفاع به پیوی ارسال شد.", replyTo: msg.MessageId, ct: ct);
            try { await SendDefenseStatus(uid, uid, chat.Id, ct); }
            catch { await SendTemp(chat.Id, "⚠️ ابتدا ربات را در پیوی استارت کنید.", replyTo: msg.MessageId, ct: ct); }
            return;
        }
    }

    // ============================================================
    //  Private message handlers
    // ============================================================
    static async Task SendStartMessage(long uid, CancellationToken ct)
    {
        // FIX(3): پیام خوش‌آمد/استارت در پیوی — تا کاربر فکر نکند بات خاموش است
        string startText =
            "👋 سلام! ربات «آلیس» روشن و فعال است ✅\n\n" +
            "🎮 این یک بازی استراتژیک جنگ جهانی است که <b>فقط داخل گروه‌ها</b> اجرا می‌شود.\n" +
            "برای بازی، ربات را به گروه خود اضافه کنید و در همان‌جا دستورها را بنویسید.\n\n" +
            "📌 دستورهای بازی (مثل «انتخاب کشور»، «دارایی»، «حمله» و ...) را باید <b>در گروه</b> بفرستید؛ " +
            "بعضی مراحل (حمله، ترنسفر، صف‌آرایی، وضعیت دفاع) به‌طور خودکار برای تنظیم دقیق به همین پیوی هدایت می‌شوند.\n\n" +
            "ℹ️ برای دیدن فهرست کامل دستورها و توضیح هرکدام، همین‌جا در پیوی بنویسید: <b>راهنما</b>\n" +
            "(دستور «راهنما» هم در گروه و هم در پیوی کار می‌کند.)";
        await SendPermanent(uid, startText, parseMode: ParseMode.Html, ct: ct);
    }

    static bool IsNavalCancellationCommand(string text) =>
        text is "لغو لشکر کشی دریایی" or "لغو لشکرکشی دریایی" or
            "لغو لشکرکشی دریائی" or "لغو عملیات دریایی" or "بازگشت ناوگان";
    static bool IsOngoingBattlesCommand(string text) =>
        text is "لیست نبرد های در جریان" or "لیست نبردهای در جریان" or
            "لیست نبرد‌های در جریان" or "نبرد های در جریان" or "نبردهای در جریان" or "نبرد‌های در جریان";

    static async Task ShowOngoingBattles(long uid,CancellationToken ct,long? onlyChatId=null)
    {
        Console.WriteLine($"[SIEGE INTEGRITY] {Database.RepairSiegeIntegrity()}");
        var lines=new List<string>{"⚔️ لیست نبردهای در جریان","فقط پیروزی‌های سنگین برای فتح شهر شمرده می‌شوند."};
        int shown=0;
        var chatIds=Database.GetUserActiveChatIds(uid);
        if(onlyChatId.HasValue)chatIds=chatIds.Where(x=>x==onlyChatId.Value).ToList();
        foreach(long chatId in chatIds)
        {
            var progress=Database.GetRoutBattleProgress(uid,chatId);
            if(progress.Count==0)continue;
            string title=await GetGroupTitleCached(chatId,ct);
            lines.Add($"\n💬 {title}");
            foreach(var battle in progress)
            {
                int remaining=Math.Max(0,5-battle.Count);
                if(battle.AttackerId==uid)
                    lines.Add($"🟢 علیه {battle.DefenderName}: {battle.Count}/5 — {remaining} پیروزی سنگین تا گرفتن یک شهر");
                else
                    lines.Add($"🔴 مقابل {battle.AttackerName}: {battle.Count}/5 — {remaining} شکست سنگین تا از دست دادن یک شهر");
                shown++;
            }
        }
        if(shown==0)lines.Add("\n✅ در حال حاضر هیچ نبردی با پیشرفت فتح شهر ندارید.");
        await SendPermanent(uid,string.Join('\n',lines),ct:ct);
    }

    static async Task ShowNavalCancellationMenu(long uid,CancellationToken ct)
    {
        var operations=Database.GetUserActiveChatIds(uid)
            .SelectMany(chatId=>Database.GetActiveNavalInvasionsByAttacker(uid,chatId))
            .Where(x=>x.Processed==0)
            .OrderBy(x=>x.ArriveAtMs)
            .ToList();
        if(operations.Count==0)
        {
            await SendTemp(uid,"❌ هیچ لشکرکشی دریایی فعال و قابل لغو ندارید.",ct:ct);
            return;
        }
        var lines=new List<string>
        {
            "↩️ لغو لشکرکشی دریایی",
            "با زدن دکمه، همان لحظه کل ناوگان آن عملیات بدون تلفات برمی‌گردد."
        };
        var buttons=new List<InlineKeyboardButton[]>();
        foreach(var op in operations)
        {
            string title=await GetGroupTitleCached(op.ChatId,ct);
            long left=Math.Max(0,op.ArriveAtMs-DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            lines.Add($"\n#{op.Id} | گپ: {title}\n🎯 {op.DefenderName} | ⏱ {FormatRemaining(left)}\n🚤 {op.Boats:N0} | ⚓ {op.Submarines:N0} | 🚢 {op.Battleships:N0}");
            buttons.Add(new[]{InlineKeyboardButton.WithCallbackData(
                $"↩️ لغو #{op.Id} — {op.DefenderName}",$"naval_cancel:{op.ChatId}:{op.Id}")});
        }
        await SendPermanent(uid,string.Join('\n',lines),new InlineKeyboardMarkup(buttons),ct:ct);
    }

    static async Task HandleUserPrivateAsync(Message msg, User user, CancellationToken ct)
    {
        long uid = user.Id;
        string txt = msg.Text?.Trim() ?? "";

        if(IsOngoingBattlesCommand(txt))
        {
            EndSession(uid);
            await ShowOngoingBattles(uid,ct);
            return;
        }
        if(IsNavalCancellationCommand(txt))
        {
            EndSession(uid);
            await ShowNavalCancellationMenu(uid,ct);
            return;
        }

        if (txt == "لغو")
        {
            var attackStates = new[] {
                SessionStep.AttackWaitingGroup, SessionStep.AttackWaitingTarget,
                SessionStep.AttackWaitingStrategy, SessionStep.AttackWaitingTactic,
                SessionStep.AttackWaitingTanks, SessionStep.AttackWaitingSoldiers,
                SessionStep.AttackWaitingFighters, SessionStep.AttackWaitingBombers,
                SessionStep.AttackWaitingAirStrategy, SessionStep.AttackWaitingAirTactic
            };
            if (sessions.TryGetValue(uid, out var cancelSess) && cancelSess != null &&
                attackStates.Contains(cancelSess.Step))
            {
                EndSession(uid);
                await SendTemp(uid, "✅ انصراف از حمله ثبت شد (بدون قفل جریمه).", ct: ct);
                return;
            }
            else
            {
                EndSession(uid);
                await SendTemp(uid, "✅ عملیات لغو شد.", ct: ct);
            }
            return;
        }

        if (sessions.TryGetValue(uid, out var sess) && sess != null)
        {
            long gameChatId=SessionGameChatId(sess);
            if(gameChatId!=0&&!Database.IsBotGroupActive(gameChatId))
            {
                EndSession(uid);
                await SendTemp(uid,"⛔ ربات از گروه مربوط به این کشور حذف شده است؛ هیچ عملیات خصوصی برای آن گروه قابل انجام نیست.",ct:ct);
                return;
            }
            if (sess.Step == SessionStep.TransferWaitingAmount)
            {
                // Single-model transfer (or fallback)
                if (!TryParseLong(txt, out long amount) || amount < 0) { await SendPrompt(uid, uid, "❌ عدد را به صورت عدد مثبت وارد کنید (0 برای لغو):", ct: ct); return; }
                if (amount == 0) { EndSession(uid); await SendTemp(uid, "✅ انتقال لغو شد.", ct: ct); return; }

                var c = Database.GetCountry(uid, sess.TransferChatId);
                if (c == null) { EndSession(uid); return; }
                long myAid = Database.GetUserAllianceId(sess.TransferChatId, uid);
                if (myAid == 0) { EndSession(uid); await SendTemp(uid, "❌ شما دیگر عضو اتحاد نیستید.", ct: ct); return; }
                long tgtAid = Database.GetUserAllianceId(sess.TransferChatId, sess.TransferTargetId);
                if (tgtAid != myAid) { EndSession(uid); await SendTemp(uid, "❌ گیرنده هم‌اتحاد شما نیست.", ct: ct); return; }
                var recv = Database.GetCountry(sess.TransferTargetId, sess.TransferChatId);
                if (recv == null) { EndSession(uid); await SendTemp(uid, "❌ گیرنده کشوری ندارد.", ct: ct); return; }
                if (GetTransferCount(sess.TransferChatId, uid) >= MAX_TRANSFERS_PER_UPDATE && !Database.HasGroupLockExemption(sess.TransferChatId))
                { EndSession(uid); await SendTemp(uid, $"⛔ سهمیه تمام شد ({MAX_TRANSFERS_PER_UPDATE}).", ct: ct); return; }

                // Rebuild breakdown if missing (for old sessions)
                if (sess.TransferModelNames.Count == 0)
                {
                    var breakdown = GetTransferSelectionBreakdown(c, sess.TransferResourceType);
                    sess.TransferModelNames = breakdown.Select(b => b.ModelName).ToList();
                    sess.TransferModelCounts = breakdown.Select(b => b.Count).ToList();
                    sess.TransferModelAmounts = new List<long>(new long[breakdown.Count]);
                    sess.TransferModelIndex = 0;
                }

                string resName = GetResName(sess.TransferResourceType);

                if (sess.TransferModelNames.Count == 0)
                {
                    await SendPrompt(uid, uid, "❌ موجودی‌ای برای انتقال ندارید.", ct: ct);
                    return;
                }

                // Single model validation
                long availSingle = sess.TransferModelCounts.Count > 0 ? sess.TransferModelCounts[0] : 0;
                // Fallback to total if counts not set
                if (availSingle == 0)
                {
                    availSingle = sess.TransferResourceType switch { "money" => c.Money, "iron" => c.Iron, "soldiers" => c.Soldiers, "tanks" => c.Tanks, "planes" => c.Planes, _ => c.Bombers };
                }

                long currentResourceTotal = GetCountryResourceAmount(c, sess.TransferResourceType);
                if (amount > availSingle || amount > currentResourceTotal)
                {
                    long availableNow = Math.Min(availSingle, currentResourceTotal);
                    await SendPrompt(uid, uid, $"❌ موجودی این مدل کافی نیست.\n📊 موجودی فعلی: {availableNow:N0}\n🔢 دوباره وارد کنید:", ct: ct);
                    return;
                }

                sess.TransferModelAmounts[0] = amount;

                // Finalize single-model transfer
                // Battleship cap check before deduct
                if (sess.TransferResourceType == "battleships")
                {
                    var recvCheck = Database.GetCountry(sess.TransferTargetId, sess.TransferChatId);
                    long usedCapacity = recvCheck == null ? 3 : Database.GetBattleshipCapacityUsed(recvCheck.OwnerId, recvCheck.ChatId);
                    if (recvCheck != null && usedCapacity + amount > 3)
                    {
                        EndSession(uid);
                        await SendTemp(uid, $"❌ ظرفیت نبردناو گیرنده کافی نیست: {usedCapacity}/3 (ناوهای در دریا و محموله‌های در راه هم حساب می‌شوند).", ct: ct);
                        return;
                    }
                }
                bool isTfExempt = Database.HasGroupLockExemption(sess.TransferChatId);
                long arrMs = isTfExempt ? 0 : DateTimeOffset.UtcNow.AddMinutes(sess.TransferDurationMin).ToUnixTimeMilliseconds();
                string modelToStore = sess.TransferModelNames.Count > 0 ? sess.TransferModelNames[0] : "";
                bool createdTransfer = await TryCreateTransfersSafely(
                    uid,
                    sess.TransferChatId,
                    myAid,
                    sess.TransferTargetId,
                    sess.TransferResourceType,
                    new List<(string ModelName, long Amount)> { (modelToStore, amount) },
                    arrMs,
                    ct);
                if (!createdTransfer)
                {
                    EndSession(uid);
                    await SendTemp(uid, "❌ موجودی تغییر کرده است و انتقال ثبت نشد.", ct: ct);
                    return;
                }
                Database.ReconcileDefense(uid, sess.TransferChatId);
                IncTransferCount(sess.TransferChatId, uid);
                EndSession(uid);

                string modelInfoSingle = string.IsNullOrWhiteSpace(modelToStore) ? "" : $" ({modelToStore})";
                if (isTfExempt)
                {
                    await SendTemp(uid, $"✅ محموله ارسال شد!\n📦 {amount:N0} {resName}{modelInfoSingle}\n⚡ تحویل فوری (معافیت کامل گروه)", ct: ct);
                    _ = Task.Run(async () => { try { await ProcessActiveTransfers(CancellationToken.None); } catch { } });
                }
                else
                {
                    await SendTemp(uid, $"✅ محموله ارسال شد!\n📦 {amount:N0} {resName}{modelInfoSingle}\n⏳ {sess.TransferDurationMin} دقیقه دیگر تحویل می‌شود.", ct: ct);
                }
                try { await bot.SendTextMessageAsync(sess.TransferTargetId, $"🚚 محموله از {c.OwnerName} ({c.Name}): {amount:N0} {resName}{modelInfoSingle} — {sess.TransferDurationMin} دقیقه دیگر", cancellationToken: ct); } catch { }
                return;
            }

            if (sess.Step == SessionStep.TransferWaitingModelAmount)
            {
                // Per-model amount entry
                if (!TryParseLong(txt, out long amount) || amount < 0) { await SendPrompt(uid, uid, "❌ عدد نامعتبر. لطفاً عدد مثبت (یا 0 برای رد شدن) وارد کنید:", ct: ct); return; }

                var c = Database.GetCountry(uid, sess.TransferChatId);
                if (c == null) { EndSession(uid); return; }

                int idx = sess.TransferModelIndex;
                if (idx < 0 || idx >= sess.TransferModelNames.Count)
                {
                    EndSession(uid);
                    await SendTemp(uid, "❌ خطای داخلی در انتقال. دوباره تلاش کنید.", ct: ct);
                    return;
                }

                long availModel = sess.TransferModelCounts[idx];
                if (amount > availModel)
                {
                    await SendPrompt(uid, uid, $"❌ موجودی این مدل کافی نیست.\n📦 مدل: {sess.TransferModelNames[idx]}\n📊 موجودی: {availModel:N0}\nدوباره وارد کنید:", ct: ct);
                    return;
                }

                sess.TransferModelAmounts[idx] = amount;
                sess.TransferModelIndex++;

                if (sess.TransferModelIndex < sess.TransferModelNames.Count)
                {
                    // Ask next model
                    var next = sess.TransferModelNames[sess.TransferModelIndex];
                    var nextAvail = sess.TransferModelCounts[sess.TransferModelIndex];
                    string rn = GetResName(sess.TransferResourceType);
                    await SendPrompt(uid, uid,
                        $"📦 انتقال {rn} – مدل {sess.TransferModelIndex + 1}/{sess.TransferModelNames.Count}\n\n🔧 مدل: {(string.IsNullOrWhiteSpace(next) ? rn : next)}\n📊 موجودی این مدل: {nextAvail:N0}\n\nچند عدد از این مدل ارسال شود؟ (0 برای رد شدن)",
                        ct: ct);
                    return;
                }

                // All models entered – finalize
                long totalAmount = sess.TransferModelAmounts.Sum();
                if (totalAmount <= 0)
                {
                    EndSession(uid);
                    await SendTemp(uid, "✅ انتقال لغو شد (مقداری انتخاب نشد).", ct: ct);
                    return;
                }
                long currentResourceTotal = GetCountryResourceAmount(c, sess.TransferResourceType);
                if (totalAmount > currentResourceTotal)
                {
                    EndSession(uid);
                    await SendTemp(uid,
                        $"❌ موجودی در طول عملیات تغییر کرده است. انتقال ثبت نشد.\n📊 موجودی فعلی: {currentResourceTotal:N0}",
                        ct: ct);
                    return;
                }

                long myAid = Database.GetUserAllianceId(sess.TransferChatId, uid);
                if (myAid == 0) { EndSession(uid); await SendTemp(uid, "❌ شما دیگر عضو اتحاد نیستید.", ct: ct); return; }
                long tgtAid = Database.GetUserAllianceId(sess.TransferChatId, sess.TransferTargetId);
                if (tgtAid != myAid) { EndSession(uid); await SendTemp(uid, "❌ گیرنده هم‌اتحاد شما نیست.", ct: ct); return; }
                var recv = Database.GetCountry(sess.TransferTargetId, sess.TransferChatId);
                if (recv == null) { EndSession(uid); await SendTemp(uid, "❌ گیرنده کشوری ندارد.", ct: ct); return; }
                if (GetTransferCount(sess.TransferChatId, uid) >= MAX_TRANSFERS_PER_UPDATE && !Database.HasGroupLockExemption(sess.TransferChatId))
                { EndSession(uid); await SendTemp(uid, $"⛔ سهمیه تمام شد ({MAX_TRANSFERS_PER_UPDATE}).", ct: ct); return; }

                // Battleship cap check for multi-model
                if (sess.TransferResourceType == "battleships")
                {
                    var recvCheck2 = Database.GetCountry(sess.TransferTargetId, sess.TransferChatId);
                    long usedCapacity = recvCheck2 == null ? 3 : Database.GetBattleshipCapacityUsed(recvCheck2.OwnerId, recvCheck2.ChatId);
                    if (recvCheck2 != null && usedCapacity + totalAmount > 3)
                    {
                        EndSession(uid);
                        await SendTemp(uid, $"❌ ظرفیت نبردناو گیرنده کافی نیست: {usedCapacity}/3 (ناوهای در دریا و محموله‌های در راه هم حساب می‌شوند).", ct: ct);
                        return;
                    }
                }
                bool isTfExempt = Database.HasGroupLockExemption(sess.TransferChatId);
                long arrMs = isTfExempt ? 0 : DateTimeOffset.UtcNow.AddMinutes(sess.TransferDurationMin).ToUnixTimeMilliseconds();
                var shipments = Enumerable.Range(0, sess.TransferModelNames.Count)
                    .Where(i => sess.TransferModelAmounts[i] > 0)
                    .Select(i => (ModelName: sess.TransferModelNames[i], Amount: sess.TransferModelAmounts[i]))
                    .ToList();
                bool createdTransfers = await TryCreateTransfersSafely(
                    uid,
                    sess.TransferChatId,
                    myAid,
                    sess.TransferTargetId,
                    sess.TransferResourceType,
                    shipments,
                    arrMs,
                    ct);
                if (!createdTransfers)
                {
                    EndSession(uid);
                    await SendTemp(uid, "❌ موجودی تغییر کرده است و انتقال ثبت نشد.", ct: ct);
                    return;
                }
                Database.ReconcileDefense(uid, sess.TransferChatId);
                long created = shipments.Count;

                IncTransferCount(sess.TransferChatId, uid);
                string summary = "";
                for (int i = 0; i < sess.TransferModelNames.Count; i++)
                {
                    if (sess.TransferModelAmounts[i] > 0)
                        summary += $"\n• {sess.TransferModelNames[i]}: {sess.TransferModelAmounts[i]:N0}";
                }

                EndSession(uid);

                string resNameFinal = GetResName(sess.TransferResourceType);
                if (isTfExempt)
                {
                    await SendTemp(uid, $"✅ محموله ارسال شد! ({created} مدل)\n📦 مجموع: {totalAmount:N0} {resNameFinal}\n{summary}\n⚡ تحویل فوری", ct: ct);
                    _ = Task.Run(async () => { try { await ProcessActiveTransfers(CancellationToken.None); } catch { } });
                }
                else
                {
                    await SendTemp(uid, $"✅ محموله ارسال شد! ({created} مدل)\n📦 مجموع: {totalAmount:N0} {resNameFinal}\n{summary}\n⏳ {sess.TransferDurationMin} دقیقه دیگر", ct: ct);
                }
                try { await bot.SendTextMessageAsync(sess.TransferTargetId, $"🚚 محموله از {c.OwnerName} ({c.Name}): {totalAmount:N0} {resNameFinal}{summary} — {sess.TransferDurationMin} دقیقه دیگر", cancellationToken: ct); } catch { }
                return;
            }

            if (sess.Step is SessionStep.NavalDefenseWaitingBoatModel or SessionStep.NavalDefenseWaitingSubmarineModel or SessionStep.NavalDefenseWaitingBattleshipModel)
            {
                if(!TryParseLong(txt,out long amount)||amount<0){await SendPrompt(uid,uid,"❌ تعداد معتبر وارد کنید.",ct:ct);return;}
                int index=sess.DefenseModelIndex;if(index<0||index>=sess.DefenseModelCounts.Count){EndSession(uid);return;}
                long minimum=sess.DefenseModelMinimums[index],available=sess.DefenseModelCounts[index];
                if(amount<minimum||amount>available){await SendPrompt(uid,uid,$"❌ مقدار مجاز بین {minimum:N0} تا {available:N0} است.",ct:ct);return;}
                sess.DefenseModelAmounts[index]=amount;sess.DefenseModelIndex++;
                if(sess.DefenseModelIndex<sess.DefenseModelNames.Count)
                {
                    int next=sess.DefenseModelIndex;await SendPrompt(uid,uid,$"⚓ مدل {next+1}/{sess.DefenseModelNames.Count}: {sess.DefenseModelNames[next]}\n📊 موجودی: {sess.DefenseModelCounts[next]:N0}\n🔒 حداقل: {sess.DefenseModelMinimums[next]:N0}\nتعداد دفاع:",ct:ct);return;
                }
                string category=sess.DefenseCurrentCategory=="boats"?"Boats":sess.DefenseCurrentCategory=="submarines"?"Submarines":"Battleships";
                var map=Enumerable.Range(0,sess.DefenseModelNames.Count).Where(i=>sess.DefenseModelAmounts[i]>0)
                    .ToDictionary(i=>sess.DefenseModelNames[i],i=>sess.DefenseModelAmounts[i],StringComparer.OrdinalIgnoreCase);
                Database.ReplaceNavalDefenseModels(uid,sess.AttackChatId,category,map);
                var country=Database.GetCountry(uid,sess.AttackChatId);if(country==null){EndSession(uid);return;}
                if(sess.DefenseCurrentCategory=="boats"){await BeginNavalDefenseCategory(uid,sess,country,"submarines",ct);return;}
                if(sess.DefenseCurrentCategory=="submarines"){await BeginNavalDefenseCategory(uid,sess,country,"battleships",ct);return;}
                long chat=sess.AttackChatId;EndSession(uid);await SendTemp(uid,"✅ آرایش مدل‌به‌مدل دفاع دریایی ذخیره شد.",ct:ct);await SendDefenseStatus(uid,uid,chat,ct);return;
            }

            if (sess.Step == SessionStep.DefenseWaitingTankModel)
            {
                if (!TryParseLong(txt, out long amount) || amount < 0)
                { await SendPrompt(uid, uid, "❌ یک تعداد معتبر وارد کنید.", ct: ct); return; }
                int index = sess.DefenseModelIndex;
                if (index < 0 || index >= sess.DefenseModelCounts.Count) { EndSession(uid); return; }
                long minimum = sess.DefenseModelMinimums[index];
                long available = sess.DefenseModelCounts[index];
                if (amount < minimum || amount > available)
                {
                    await SendPrompt(uid, uid,
                        $"❌ مقدار مجاز برای {sess.DefenseModelNames[index]} بین {minimum:N0} تا {available:N0} است.", ct: ct);
                    return;
                }
                sess.DefenseModelAmounts[index] = amount;
                sess.DefenseModelIndex++;
                if (sess.DefenseModelIndex < sess.DefenseModelNames.Count)
                {
                    int next = sess.DefenseModelIndex;
                    await SendPrompt(uid, uid,
                        $"🛡 دفاع تانک – مدل {next + 1}/{sess.DefenseModelNames.Count}\n\n🔧 مدل: {sess.DefenseModelNames[next]}\n📊 موجودی: {sess.DefenseModelCounts[next]:N0}\n🛡 مقدار فعلی دفاع: {sess.DefenseModelAmounts[next]:N0}\n🔒 حداقل اجباری: {sess.DefenseModelMinimums[next]:N0}\n\nتعداد دقیق را وارد کنید:", ct: ct);
                    return;
                }
                sess.DefenseTankModelNamesFinal = new List<string>(sess.DefenseModelNames);
                sess.DefenseTankModelAmountsFinal = new List<long>(sess.DefenseModelAmounts);
                sess.DefenseTanks = sess.DefenseModelAmounts.Sum();
                sess.DefTankPct = 100;
                sess.Step = SessionStep.DefenseWaitingSoldiers;
                await SendPrompt(uid, uid, $"🪖 درصد دفاع سرباز:\nکل: {Database.GetCountry(uid, sess.AttackChatId)?.Soldiers ?? 0:N0}",
                    BuildPercentKeyboard("soldier", sess.AttackChatId), ct);
                return;
            }

            if (sess.Step == SessionStep.DefenseWaitingPlaneModel)
            {
                if (!TryParseLong(txt, out long amount) || amount < 0)
                { await SendPrompt(uid, uid, "❌ یک تعداد معتبر وارد کنید.", ct: ct); return; }
                int index = sess.DefenseModelIndex;
                if (index < 0 || index >= sess.DefenseModelCounts.Count) { EndSession(uid); return; }
                long minimum = sess.DefenseModelMinimums[index];
                if (amount < minimum || amount > sess.DefenseModelCounts[index])
                {
                    await SendPrompt(uid, uid,
                        $"❌ مقدار مجاز برای {sess.DefenseModelNames[index]} بین {minimum:N0} تا {sess.DefenseModelCounts[index]:N0} است.", ct: ct);
                    return;
                }
                sess.DefenseModelAmounts[index] = amount;
                sess.DefenseModelIndex++;
                if (sess.DefenseModelIndex < sess.DefenseModelNames.Count)
                {
                    int next = sess.DefenseModelIndex;
                    await SendPrompt(uid, uid,
                        $"✈️ دفاع جنگنده – مدل {next + 1}/{sess.DefenseModelNames.Count}\n\n🔧 مدل: {sess.DefenseModelNames[next]}\n📊 موجودی: {sess.DefenseModelCounts[next]:N0}\n🛡 مقدار فعلی دفاع: {sess.DefenseModelAmounts[next]:N0}\n🔒 حداقل اجباری: {sess.DefenseModelMinimums[next]:N0}\n\nتعداد دقیق را وارد کنید:", ct: ct);
                    return;
                }
                var country = Database.GetCountry(uid, sess.AttackChatId);
                if (country == null) { EndSession(uid); return; }
                var tankMap = Enumerable.Range(0, sess.DefenseTankModelNamesFinal.Count)
                    .Where(i => i < sess.DefenseTankModelAmountsFinal.Count && sess.DefenseTankModelAmountsFinal[i] > 0)
                    .ToDictionary(i => sess.DefenseTankModelNamesFinal[i], i => sess.DefenseTankModelAmountsFinal[i], StringComparer.OrdinalIgnoreCase);
                var fighterMap = Enumerable.Range(0, sess.DefenseModelNames.Count)
                    .Where(i => sess.DefenseModelAmounts[i] > 0)
                    .ToDictionary(i => sess.DefenseModelNames[i], i => sess.DefenseModelAmounts[i], StringComparer.OrdinalIgnoreCase);
                bool tanksStillAvailable = tankMap.All(item =>
                    item.Value <= GetTransferBreakdown(country, "tanks")
                        .Where(x => x.ModelName.Equals(item.Key, StringComparison.OrdinalIgnoreCase)).Sum(x => x.Count));
                bool fightersStillAvailable = fighterMap.All(item =>
                    item.Value <= GetTransferBreakdown(country, "planes")
                        .Where(x => x.ModelName.Equals(item.Key, StringComparison.OrdinalIgnoreCase)).Sum(x => x.Count));
                if (!tanksStillAvailable || !fightersStillAvailable)
                {
                    EndSession(uid);
                    await SendTemp(uid, "❌ موجودی مدل‌ها تغییر کرده است؛ تنظیم دفاع را دوباره انجام دهید.", ct: ct);
                    return;
                }
                Database.ReplaceDefenseModelAmounts(uid, sess.AttackChatId, "Tanks", tankMap);
                Database.ReplaceDefenseModelAmounts(uid, sess.AttackChatId, "Planes", fighterMap);
                long fighters = sess.DefenseModelAmounts.Sum();
                Database.SetDefenseSoldierConfigured(uid,sess.AttackChatId,true);
                Database.UpdateDefenseFull(uid, sess.AttackChatId, sess.DefenseTanks,
                    sess.DefenseSoldiers, fighters, country.DefenseStrategy, country.DefenseTactic,
                    100, sess.DefSoldierPct > 0 ? sess.DefSoldierPct : 20, 100);
                Database.ReconcileDefense(uid, sess.AttackChatId);
                long defenseChat = sess.AttackChatId;
                EndSession(uid);
                await SendTemp(uid, "✅ ترکیب دقیق دفاع تانک و جنگنده ذخیره شد.", ct: ct);
                await SendDefenseStatus(uid, uid, defenseChat, ct);
                return;
            }

            if (sess.Step == SessionStep.DeployWaitingTanks)
            {
                // Legacy total tanks – now redirect to per-model
                var c = Database.GetCountry(uid, sess.DeployChatId);
                if (c == null) { EndSession(uid); return; }
                var breakdown = GetTransferBreakdown(c, "tanks");
                if (breakdown.Count == 0)
                {
                    sess.DeployTanks = 0;
                    sess.Step = SessionStep.DeployWaitingSoldiers;
                    await SendPrompt(uid, uid, $"🪖 سرباز:\nموجود: {c.Soldiers:N0}", ct: ct);
                    return;
                }
                sess.DeployModelNames = breakdown.Select(x => x.ModelName).ToList();
                sess.DeployModelCounts = breakdown.Select(x => x.Count).ToList();
                sess.DeployModelAmounts = new List<long>(new long[breakdown.Count]);
                sess.DeployModelIndex = 0;
                sess.DeployCurrentCategory = "tanks";
                sess.Step = SessionStep.DeployWaitingTankModel;
                await SendPrompt(uid, uid, $"🛡 صف آرایی – تانک مدل 1/{breakdown.Count}: {breakdown[0].ModelName} – موجودی {breakdown[0].Count:N0}\nچند تا اعزام شود؟ (0 برای رد)", ct: ct);
                return;
            }
            if (sess.Step == SessionStep.DeployWaitingTankModel)
            {
                if (!TryParseLong(txt, out long amt) || amt < 0) { await SendPrompt(uid, uid, "❌ عدد معتبر.", ct: ct); return; }
                int idx = sess.DeployModelIndex;
                if (idx < 0 || idx >= sess.DeployModelCounts.Count) { EndSession(uid); return; }
                if (amt > sess.DeployModelCounts[idx]) { await SendPrompt(uid, uid, $"❌ موجودی این مدل: {sess.DeployModelCounts[idx]:N0}", ct: ct); return; }
                sess.DeployModelAmounts[idx] = amt;
                sess.DeployModelIndex++;
                if (sess.DeployModelIndex < sess.DeployModelNames.Count)
                {
                    await SendPrompt(uid, uid, $"🛡 مدل {sess.DeployModelIndex + 1}/{sess.DeployModelNames.Count}: {sess.DeployModelNames[sess.DeployModelIndex]} – موجودی {sess.DeployModelCounts[sess.DeployModelIndex]:N0}\nچند تا؟ (0 برای رد)", ct: ct);
                    return;
                }
                sess.DeployTanks = sess.DeployModelAmounts.Sum();
                sess.DeployTankModelNamesFinal = new List<string>(sess.DeployModelNames);
                sess.DeployTankModelAmountsFinal = new List<long>(sess.DeployModelAmounts);
                sess.Step = SessionStep.DeployWaitingSoldiers;
                var c = Database.GetCountry(uid, sess.DeployChatId);
                await SendPrompt(uid, uid, $"🪖 سرباز:\nموجود: {c?.Soldiers ?? 0:N0}", ct: ct);
                return;
            }
            if (sess.Step == SessionStep.DeployWaitingSoldiers)
            {
                if (!TryParseLong(txt, out long sol) || sol < 0) { await SendPrompt(uid, uid, "❌ عدد معتبر.", ct: ct); return; }
                var c = Database.GetCountry(uid, sess.DeployChatId);
                if (c == null) { EndSession(uid); return; }
                if (sol > c.Soldiers) { await SendPrompt(uid, uid, $"❌ موجودی: {c.Soldiers}", ct: ct); return; }
                sess.DeploySoldiers = sol;
                // Per-model planes
                var planeBreakdown = GetTransferBreakdown(c, "planes");
                if (planeBreakdown.Count == 0)
                {
                    sess.DeployFighters = 0;
                    sess.Step = SessionStep.DeployWaitingBombers;
                    await SendPrompt(uid, uid, $"🛩 بمب‌افکن:\nموجود: {c.Bombers:N0}", ct: ct);
                    return;
                }
                sess.DeployModelNames = planeBreakdown.Select(x => x.ModelName).ToList();
                sess.DeployModelCounts = planeBreakdown.Select(x => x.Count).ToList();
                sess.DeployModelAmounts = new List<long>(new long[planeBreakdown.Count]);
                sess.DeployModelIndex = 0;
                sess.DeployCurrentCategory = "planes";
                sess.Step = SessionStep.DeployWaitingPlaneModel;
                await SendPrompt(uid, uid, $"✈️ صف آرایی – جنگنده مدل 1/{planeBreakdown.Count}: {planeBreakdown[0].ModelName} – موجودی {planeBreakdown[0].Count:N0}\nچند تا اعزام شود؟ (0 برای رد)", ct: ct);
                return;
            }
            if (sess.Step == SessionStep.DeployWaitingPlaneModel)
            {
                if (!TryParseLong(txt, out long amt) || amt < 0) { await SendPrompt(uid, uid, "❌ عدد معتبر.", ct: ct); return; }
                int idx = sess.DeployModelIndex;
                if (idx < 0 || idx >= sess.DeployModelCounts.Count) { EndSession(uid); return; }
                if (amt > sess.DeployModelCounts[idx]) { await SendPrompt(uid, uid, $"❌ موجودی این مدل: {sess.DeployModelCounts[idx]:N0}", ct: ct); return; }
                sess.DeployModelAmounts[idx] = amt;
                sess.DeployModelIndex++;
                if (sess.DeployModelIndex < sess.DeployModelNames.Count)
                {
                    await SendPrompt(uid, uid, $"✈️ مدل {sess.DeployModelIndex + 1}/{sess.DeployModelNames.Count}: {sess.DeployModelNames[sess.DeployModelIndex]} – موجودی {sess.DeployModelCounts[sess.DeployModelIndex]:N0}\nچند تا؟ (0 برای رد)", ct: ct);
                    return;
                }
                sess.DeployFighters = sess.DeployModelAmounts.Sum();
                sess.DeployPlaneModelNamesFinal = new List<string>(sess.DeployModelNames);
                sess.DeployPlaneModelAmountsFinal = new List<long>(sess.DeployModelAmounts);
                sess.Step = SessionStep.DeployWaitingBombers;
                var c = Database.GetCountry(uid, sess.DeployChatId);
                await SendPrompt(uid, uid, $"🛩 بمب‌افکن:\nموجود: {c?.Bombers ?? 0:N0}", ct: ct);
                return;
            }
            if (sess.Step == SessionStep.DeployWaitingFighters)
            {
                // Legacy – redirect to per-model
                var c = Database.GetCountry(uid, sess.DeployChatId);
                if (c == null) { EndSession(uid); return; }
                var planeBreakdown = GetTransferBreakdown(c, "planes");
                if (planeBreakdown.Count > 0)
                {
                    sess.DeployModelNames = planeBreakdown.Select(x => x.ModelName).ToList();
                    sess.DeployModelCounts = planeBreakdown.Select(x => x.Count).ToList();
                    sess.DeployModelAmounts = new List<long>(new long[planeBreakdown.Count]);
                    sess.DeployModelIndex = 0;
                    sess.DeployCurrentCategory = "planes";
                    sess.Step = SessionStep.DeployWaitingPlaneModel;
                    await SendPrompt(uid, uid, $"✈️ صف آرایی – جنگنده مدل 1/{planeBreakdown.Count}: {planeBreakdown[0].ModelName} – موجودی {planeBreakdown[0].Count:N0}\nچند تا اعزام شود؟ (0 برای رد)", ct: ct);
                    return;
                }
                if (!TryParseLong(txt, out long fig) || fig < 0) { await SendPrompt(uid, uid, "❌ عدد معتبر.", ct: ct); return; }
                if (fig > c.Planes) { await SendPrompt(uid, uid, $"❌ موجودی: {c.Planes}", ct: ct); return; }
                sess.DeployFighters = fig;
                sess.Step = SessionStep.DeployWaitingBombers;
                await SendPrompt(uid, uid, $"🛩 بمب‌افکن:\nموجود: {c.Bombers:N0}", ct: ct);
                return;
            }
            if (sess.Step == SessionStep.DeployWaitingBombers)
            {
                var c = Database.GetCountry(uid, sess.DeployChatId);
                if (c == null) { EndSession(uid); return; }
                var bomberBreakdown = GetTransferBreakdown(c, "bombers");
                if (bomberBreakdown.Count > 1)
                {
                    // Per-model for bombers
                    sess.DeployModelNames = bomberBreakdown.Select(x => x.ModelName).ToList();
                    sess.DeployModelCounts = bomberBreakdown.Select(x => x.Count).ToList();
                    sess.DeployModelAmounts = new List<long>(new long[bomberBreakdown.Count]);
                    sess.DeployModelIndex = 0;
                    sess.DeployCurrentCategory = "bombers";
                    sess.Step = SessionStep.DeployWaitingBomberModel;
                    await SendPrompt(uid, uid, $"🛩 صف آرایی – بمب‌افکن مدل 1/{bomberBreakdown.Count}: {bomberBreakdown[0].ModelName} – موجودی {bomberBreakdown[0].Count:N0}\nچند تا اعزام شود؟ (0 برای رد)", ct: ct);
                    return;
                }
                if (!TryParseLong(txt, out long bom) || bom < 0) { await SendPrompt(uid, uid, "❌ عدد معتبر.", ct: ct); return; }
                if (bom > c.Bombers) { await SendPrompt(uid, uid, $"❌ موجودی: {c.Bombers}", ct: ct); return; }
                sess.DeployBombers = bom;
                if (bom > 0 && bomberBreakdown.Count == 1)
                {
                    sess.DeployBomberModelNamesFinal = new List<string> { bomberBreakdown[0].ModelName };
                    sess.DeployBomberModelAmountsFinal = new List<long> { bom };
                }
                if (!HasAvailableForces(c, sess.DeployTanks, sess.DeploySoldiers, sess.DeployFighters, sess.DeployBombers))
                {
                    EndSession(uid);
                    await SendTemp(uid,
                        "❌ موجودی نیروها در طول عملیات تغییر کرده است. صف‌آرایی ثبت نشد.\n\n" +
                        AvailableForcesText(c),
                        ct: ct);
                    return;
                }
                long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                long endMs = nowMs + sess.DeployDuration * 3600000L;
                var dep = new Deployment
                {
                    ChatId = sess.DeployChatId, AllianceId = sess.DeployAllianceId, InitiatorId = uid, TargetUserId = sess.DeployTargetId,
                    Type = sess.DeployType, DurationHours = sess.DeployDuration, FormationType = sess.DeployFormation,
                    Strategy = sess.DeployStrategy, Tactic = sess.DeployTactic,
                    Tanks = sess.DeployTanks, Soldiers = sess.DeploySoldiers, Fighters = sess.DeployFighters, Bombers = sess.DeployBombers,
                    CreatedAtMs = nowMs, EndAtMs = endMs, LastWarnMs = nowMs
                };
                var selectedTankModels = SelectedDeploymentModels(sess.DeployTankModelNamesFinal,
                    sess.DeployTankModelAmountsFinal, sess.DeployTanks, Database.GetDefaultTankModel(c.Faction));
                var selectedFighterModels = SelectedDeploymentModels(sess.DeployPlaneModelNamesFinal,
                    sess.DeployPlaneModelAmountsFinal, sess.DeployFighters, Database.GetDefaultPlaneModel(c.Faction));
                var selectedBomberModels = SelectedDeploymentModels(sess.DeployBomberModelNamesFinal,
                    sess.DeployBomberModelAmountsFinal, sess.DeployBombers, Database.GetDefaultBomberModel(c.Faction));
                long depId = await TryCreateDeploymentSafely(dep, ct,
                    selectedTankModels, selectedFighterModels, selectedBomberModels);
                if (depId == 0)
                {
                    EndSession(uid);
                    await SendTemp(uid, "❌ موجودی نیروها تغییر کرده است و صف‌آرایی ثبت نشد.", ct: ct);
                    return;
                }
                Database.ReconcileDefense(uid, sess.DeployChatId);
                //  – defensive troops should NOT appear in target assets, only in separate deployment details
                // So we do NOT add to target country anymore
                EndSession(uid);
                var alliance = Database.GetAllianceById(sess.DeployAllianceId);
                string allyName = alliance?.Name ?? "اتحاد";
                var tc = Database.GetCountry(sess.DeployTargetId, sess.DeployChatId);
                string tName = tc?.Name ?? $"کاربر {sess.DeployTargetId}";
                //  – group message should only list participating players, not all alliance members
                string tags = HtmlTag(c.OwnerName, c.OwnerId); // only initiator at creation
                string targetTag = tc != null ? HtmlTag(tc.OwnerName, tc.OwnerId) : $"کاربر {sess.DeployTargetId}";
                bool isOff = sess.DeployType == "Offensive";
                // Formation is always Unified now (MultiFront removed)
                string bText = isOff ?
                    $"🚨 <b>اعلان جنگ و صف‌آرایی تهاجمی!</b> ⚔️\n\n👑 اتحاد <b>«{HtmlText(allyName)}»</b> علیه کشور <b>«{HtmlText(tName)}»</b> (مالک: {targetTag}) صف‌آرایی کرد!\n⏱ مدت: <b>{sess.DeployDuration} ساعت</b> (پایان: {FormatTime(endMs)})\n\n💥 <b>نیروهای اولیه:</b>\n🪖 سرباز: {sess.DeploySoldiers:N0} | 🛡 تانک: {sess.DeployTanks:N0}\n✈️ جنگنده: {sess.DeployFighters:N0} | 🛩 بمب‌افکن: {sess.DeployBombers:N0}\n\n👥 مشارکت‌کنندگان:\n{tags}\n\n🎯 استراتژی: {sess.DeployStrategy} | تاکتیک: {sess.DeployTactic}" :
                    $"🛡 <b>اعلام صف‌آرایی دفاعی!</b> 🏰\n\n👑 اتحاد <b>«{HtmlText(allyName)}»</b> برای حمایت از کشور <b>«{HtmlText(tName)}»</b> (مالک: {targetTag}) خط پدافندی تشکیل داد!\n⏱ مدت: <b>{sess.DeployDuration} ساعت</b> (پایان: {FormatTime(endMs)})\n\n🛡 <b>نیروهای پشتیبان:</b>\n🪖 سرباز: {sess.DeploySoldiers:N0} | 🛡 تانک: {sess.DeployTanks:N0}\n✈️ جنگنده: {sess.DeployFighters:N0} | 🛩 بمب‌افکن: {sess.DeployBombers:N0}\n\n👥 مشارکت‌کنندگان:\n{tags}\n\n🎯 استراتژی: {sess.DeployStrategy} | تاکتیک: {sess.DeployTactic}";
                string fCat = isOff ? "OffensiveDeploy" : "DefensiveDeploy";
                var photos = Database.GetFactionFlags(fCat);
                // FIX(1): دکمهٔ صحیح (dep_join) — قبلاً depjoin بود و کار نمی‌کرد
                var joinKb = new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("⚔️ مشارکت و اعزام نیرو", $"dep_join:{depId}") } });
                Message depMsg;
                if (photos.Count > 0)
                {
                    string rPhoto = photos[rng.Next(photos.Count)];
                    try { depMsg = await bot.SendPhotoAsync(sess.DeployChatId, new InputOnlineFile(rPhoto), caption: bText, parseMode: ParseMode.Html, replyMarkup: joinKb, cancellationToken: ct); }
                    catch { depMsg = await bot.SendTextMessageAsync(sess.DeployChatId, bText, parseMode: ParseMode.Html, replyMarkup: joinKb, cancellationToken: ct); }
                }
                else { depMsg = await bot.SendTextMessageAsync(sess.DeployChatId, bText, parseMode: ParseMode.Html, replyMarkup: joinKb, cancellationToken: ct); }
                // FIX(2): MessageId پیام اعلام را ذخیره کن تا هنگام لغو/پایان آنپین و حذف شود
                try { Database.UpdateDeploymentAnnounceMsg(depId, depMsg.MessageId); } catch { }
                try { await bot.PinChatMessageAsync(sess.DeployChatId, depMsg.MessageId, disableNotification: false, cancellationToken: ct); } catch { }
                await SendTemp(uid, "✅ صف‌آرایی ثبت و در گروه اعلام شد.", ct: ct);
                if (isOff)
                {
                    string gTitle = $"گروه {sess.DeployChatId}";
                    try { var ch = await bot.GetChatAsync(sess.DeployChatId, ct); if (!string.IsNullOrEmpty(ch.Title)) gTitle = ch.Title; } catch { }
                    try { await bot.SendTextMessageAsync(sess.DeployTargetId, $"⚠️ هشدار: صف‌آرایی تهاجمی علیه شما در «{gTitle}»!\n⏱ {sess.DeployDuration} ساعت دیگر", cancellationToken: ct); } catch { }
                }
                return;
            }

            if (sess.Step == SessionStep.DeployWaitingBomberModel)
            {
                if (!TryParseLong(txt, out long amt) || amt < 0) { await SendPrompt(uid, uid, "❌ عدد معتبر.", ct: ct); return; }
                int idx = sess.DeployModelIndex;
                if (idx < 0 || idx >= sess.DeployModelCounts.Count) { EndSession(uid); return; }
                if (amt > sess.DeployModelCounts[idx]) { await SendPrompt(uid, uid, $"❌ موجودی این مدل: {sess.DeployModelCounts[idx]:N0}", ct: ct); return; }
                sess.DeployModelAmounts[idx] = amt;
                sess.DeployModelIndex++;
                if (sess.DeployModelIndex < sess.DeployModelNames.Count)
                {
                    await SendPrompt(uid, uid, $"🛩 مدل {sess.DeployModelIndex + 1}/{sess.DeployModelNames.Count}: {sess.DeployModelNames[sess.DeployModelIndex]} – موجودی {sess.DeployModelCounts[sess.DeployModelIndex]:N0}\nچند تا؟ (0 برای رد)", ct: ct);
                    return;
                }
                sess.DeployBombers = sess.DeployModelAmounts.Sum();
                sess.DeployBomberModelNamesFinal = new List<string>(sess.DeployModelNames);
                sess.DeployBomberModelAmountsFinal = new List<long>(sess.DeployModelAmounts);
                long nowMs2 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                long endMs2 = nowMs2 + sess.DeployDuration * 3600000L;
                var dep2 = new Deployment
                {
                    ChatId = sess.DeployChatId, AllianceId = sess.DeployAllianceId, InitiatorId = uid, TargetUserId = sess.DeployTargetId,
                    Type = sess.DeployType, DurationHours = sess.DeployDuration, FormationType = sess.DeployFormation,
                    Strategy = sess.DeployStrategy, Tactic = sess.DeployTactic,
                    Tanks = sess.DeployTanks, Soldiers = sess.DeploySoldiers, Fighters = sess.DeployFighters, Bombers = sess.DeployBombers,
                    CreatedAtMs = nowMs2, EndAtMs = endMs2, LastWarnMs = nowMs2
                };
                var c2 = Database.GetCountry(uid, sess.DeployChatId);
                if (c2 == null || !HasAvailableForces(c2, sess.DeployTanks, sess.DeploySoldiers, sess.DeployFighters, sess.DeployBombers))
                {
                    EndSession(uid);
                    string available = c2 == null ? "کشور یافت نشد." : AvailableForcesText(c2);
                    await SendTemp(uid,
                        "❌ موجودی نیروها در طول عملیات تغییر کرده است. صف‌آرایی ثبت نشد.\n\n" + available,
                        ct: ct);
                    return;
                }
                var selectedTankModels2 = SelectedDeploymentModels(sess.DeployTankModelNamesFinal,
                    sess.DeployTankModelAmountsFinal, sess.DeployTanks, Database.GetDefaultTankModel(c2!.Faction));
                var selectedFighterModels2 = SelectedDeploymentModels(sess.DeployPlaneModelNamesFinal,
                    sess.DeployPlaneModelAmountsFinal, sess.DeployFighters, Database.GetDefaultPlaneModel(c2.Faction));
                var selectedBomberModels2 = SelectedDeploymentModels(sess.DeployBomberModelNamesFinal,
                    sess.DeployBomberModelAmountsFinal, sess.DeployBombers, Database.GetDefaultBomberModel(c2.Faction));
                long depId2 = await TryCreateDeploymentSafely(dep2, ct,
                    selectedTankModels2, selectedFighterModels2, selectedBomberModels2);
                if (depId2 == 0)
                {
                    EndSession(uid);
                    await SendTemp(uid, "❌ موجودی نیروها تغییر کرده است و صف‌آرایی ثبت نشد.", ct: ct);
                    return;
                }
                Database.ReconcileDefense(uid, sess.DeployChatId);
                EndSession(uid);
                var alliance2 = Database.GetAllianceById(sess.DeployAllianceId);
                string allyName2 = alliance2?.Name ?? "اتحاد";
                var tc2 = Database.GetCountry(sess.DeployTargetId, sess.DeployChatId);
                string tName2 = tc2?.Name ?? $"کاربر {sess.DeployTargetId}";
                string tags2 = c2 != null ? HtmlTag(c2.OwnerName, c2.OwnerId) : "";
                string targetTag2 = tc2 != null ? HtmlTag(tc2.OwnerName, tc2.OwnerId) : $"کاربر {sess.DeployTargetId}";
                bool isOff2 = sess.DeployType == "Offensive";
                string bText2 = isOff2 ?
                    $"🚨 <b>اعلان جنگ و صف‌آرایی تهاجمی!</b> ⚔️\n\n👑 اتحاد <b>«{HtmlText(allyName2)}»</b> علیه کشور <b>«{HtmlText(tName2)}»</b> (مالک: {targetTag2}) صف‌آرایی کرد!\n⏱ مدت: <b>{sess.DeployDuration} ساعت</b> (پایان: {FormatTime(endMs2)})\n\n💥 <b>نیروهای اولیه:</b>\n🪖 سرباز: {sess.DeploySoldiers:N0} | 🛡 تانک: {sess.DeployTanks:N0}\n✈️ جنگنده: {sess.DeployFighters:N0} | 🛩 بمب‌افکن: {sess.DeployBombers:N0}\n\n👥 مشارکت‌کنندگان:\n{tags2}\n\n🎯 استراتژی: {sess.DeployStrategy} | تاکتیک: {sess.DeployTactic}" :
                    $"🛡 <b>اعلام صف‌آرایی دفاعی!</b> 🏰\n\n👑 اتحاد <b>«{HtmlText(allyName2)}»</b> برای حمایت از کشور <b>«{HtmlText(tName2)}»</b> (مالک: {targetTag2}) خط پدافندی تشکیل داد!\n⏱ مدت: <b>{sess.DeployDuration} ساعت</b> (پایان: {FormatTime(endMs2)})\n\n🛡 <b>نیروهای پشتیبان:</b>\n🪖 سرباز: {sess.DeploySoldiers:N0} | 🛡 تانک: {sess.DeployTanks:N0}\n✈️ جنگنده: {sess.DeployFighters:N0} | 🛩 بمب‌افکن: {sess.DeployBombers:N0}\n\n👥 مشارکت‌کنندگان:\n{tags2}\n\n🎯 استراتژی: {sess.DeployStrategy} | تاکتیک: {sess.DeployTactic}";
                string fCat2 = isOff2 ? "OffensiveDeploy" : "DefensiveDeploy";
                var photos2 = Database.GetFactionFlags(fCat2);
                var joinKb2 = new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("⚔️ مشارکت و اعزام نیرو", $"dep_join:{depId2}") } });
                Message depMsg2;
                if (photos2.Count > 0)
                {
                    string rPhoto = photos2[rng.Next(photos2.Count)];
                    try { depMsg2 = await bot.SendPhotoAsync(sess.DeployChatId, new InputOnlineFile(rPhoto), caption: bText2, parseMode: ParseMode.Html, replyMarkup: joinKb2, cancellationToken: ct); }
                    catch { depMsg2 = await bot.SendTextMessageAsync(sess.DeployChatId, bText2, parseMode: ParseMode.Html, replyMarkup: joinKb2, cancellationToken: ct); }
                }
                else { depMsg2 = await bot.SendTextMessageAsync(sess.DeployChatId, bText2, parseMode: ParseMode.Html, replyMarkup: joinKb2, cancellationToken: ct); }
                try { Database.UpdateDeploymentAnnounceMsg(depId2, depMsg2.MessageId); } catch { }
                try { await bot.PinChatMessageAsync(sess.DeployChatId, depMsg2.MessageId, disableNotification: false, cancellationToken: ct); } catch { }
                await SendTemp(uid, "✅ صف‌آرایی ثبت و در گروه اعلام شد.", ct: ct);
                return;
            }

            // DeployJoin steps
            if (sess.Step == SessionStep.DeployJoinWaitingTanks)
            {
                var c = Database.GetCountry(uid, sess.DeployChatId);
                if (c == null) { EndSession(uid); return; }
                var breakdown = GetTransferBreakdown(c, "tanks");
                if (breakdown.Count == 0)
                {
                    sess.DeployJoinTanks = 0;
                    sess.Step = SessionStep.DeployJoinWaitingSoldiers;
                    await SendPrompt(uid, uid, $"🪖 سرباز:\nموجود: {c.Soldiers:N0}", ct: ct);
                    return;
                }
                sess.DeployModelNames = breakdown.Select(x => x.ModelName).ToList();
                sess.DeployModelCounts = breakdown.Select(x => x.Count).ToList();
                sess.DeployModelAmounts = new List<long>(new long[breakdown.Count]);
                sess.DeployModelIndex = 0;
                sess.Step = SessionStep.DeployJoinWaitingTankModel;
                await SendPrompt(uid, uid,
                    $"🛡 مشارکت – تانک مدل 1/{breakdown.Count}: {breakdown[0].ModelName} – موجودی {breakdown[0].Count:N0}\nچند تا اعزام شود؟ (0 برای رد)", ct: ct);
                return;
            }
            if (sess.Step == SessionStep.DeployJoinWaitingTankModel)
            {
                if (!TryParseLong(txt, out long amount) || amount < 0)
                { await SendPrompt(uid, uid, "❌ عدد معتبر (0 برای رد).", ct: ct); return; }
                int index = sess.DeployModelIndex;
                if (index < 0 || index >= sess.DeployModelCounts.Count) { EndSession(uid); return; }
                if (amount > sess.DeployModelCounts[index])
                { await SendPrompt(uid, uid, $"❌ موجودی این مدل: {sess.DeployModelCounts[index]:N0}", ct: ct); return; }
                sess.DeployModelAmounts[index] = amount;
                sess.DeployModelIndex++;
                if (sess.DeployModelIndex < sess.DeployModelNames.Count)
                {
                    int next = sess.DeployModelIndex;
                    await SendPrompt(uid, uid,
                        $"🛡 مشارکت – تانک مدل {next + 1}/{sess.DeployModelNames.Count}: {sess.DeployModelNames[next]} – موجودی {sess.DeployModelCounts[next]:N0}\nچند تا؟ (0 برای رد)", ct: ct);
                    return;
                }
                sess.DeployJoinTanks = sess.DeployModelAmounts.Sum();
                sess.DeployTankModelNamesFinal = new List<string>(sess.DeployModelNames);
                sess.DeployTankModelAmountsFinal = new List<long>(sess.DeployModelAmounts);
                sess.Step = SessionStep.DeployJoinWaitingSoldiers;
                var c = Database.GetCountry(uid, sess.DeployChatId);
                await SendPrompt(uid, uid, $"🪖 سرباز:\nموجود: {c?.Soldiers ?? 0:N0}", ct: ct);
                return;
            }
            if (sess.Step == SessionStep.DeployJoinWaitingSoldiers)
            {
                if (!TryParseLong(txt, out long sol) || sol < 0) { await SendPrompt(uid, uid, "❌ عدد معتبر.", ct: ct); return; }
                var c = Database.GetCountry(uid, sess.DeployChatId);
                if (c == null) { EndSession(uid); return; }
                if (sol > c.Soldiers) { await SendPrompt(uid, uid, $"❌ موجودی: {c.Soldiers}", ct: ct); return; }
                sess.DeployJoinSoldiers = sol;
                var planes = GetTransferBreakdown(c, "planes");
                if (planes.Count == 0)
                {
                    sess.DeployJoinFighters = 0;
                    var bombers = GetTransferBreakdown(c, "bombers");
                    if (bombers.Count == 0)
                    {
                        sess.DeployJoinBombers = 0;
                        sess.Step = SessionStep.DeployJoinWaitingBombers;
                        await SendPrompt(uid, uid, "🛩 بمب‌افکن ندارید؛ برای ثبت نهایی عدد 0 را ارسال کنید.", ct: ct);
                        return;
                    }
                    sess.DeployModelNames = bombers.Select(x => x.ModelName).ToList();
                    sess.DeployModelCounts = bombers.Select(x => x.Count).ToList();
                    sess.DeployModelAmounts = new List<long>(new long[bombers.Count]);
                    sess.DeployModelIndex = 0;
                    sess.Step = SessionStep.DeployJoinWaitingBomberModel;
                    await SendPrompt(uid, uid,
                        $"🛩 مشارکت – بمب‌افکن مدل 1/{bombers.Count}: {bombers[0].ModelName} – موجودی {bombers[0].Count:N0}\nچند تا اعزام شود؟ (0 برای رد)", ct: ct);
                    return;
                }
                sess.DeployModelNames = planes.Select(x => x.ModelName).ToList();
                sess.DeployModelCounts = planes.Select(x => x.Count).ToList();
                sess.DeployModelAmounts = new List<long>(new long[planes.Count]);
                sess.DeployModelIndex = 0;
                sess.Step = SessionStep.DeployJoinWaitingPlaneModel;
                await SendPrompt(uid, uid,
                    $"✈️ مشارکت – جنگنده مدل 1/{planes.Count}: {planes[0].ModelName} – موجودی {planes[0].Count:N0}\nچند تا اعزام شود؟ (0 برای رد)", ct: ct);
                return;
            }
            if (sess.Step == SessionStep.DeployJoinWaitingFighters)
            {
                var c = Database.GetCountry(uid, sess.DeployChatId);
                if (c == null) { EndSession(uid); return; }
                var breakdown = GetTransferBreakdown(c, "planes");
                if (breakdown.Count == 0)
                {
                    sess.DeployJoinFighters = 0;
                    sess.Step = SessionStep.DeployJoinWaitingBombers;
                    await SendPrompt(uid, uid, "🛩 برای انتخاب مدل‌های بمب‌افکن یک عدد ارسال کنید.", ct: ct);
                    return;
                }
                sess.DeployModelNames = breakdown.Select(x => x.ModelName).ToList();
                sess.DeployModelCounts = breakdown.Select(x => x.Count).ToList();
                sess.DeployModelAmounts = new List<long>(new long[breakdown.Count]);
                sess.DeployModelIndex = 0;
                sess.Step = SessionStep.DeployJoinWaitingPlaneModel;
                await SendPrompt(uid, uid,
                    $"✈️ مشارکت – جنگنده مدل 1/{breakdown.Count}: {breakdown[0].ModelName} – موجودی {breakdown[0].Count:N0}\nچند تا اعزام شود؟ (0 برای رد)", ct: ct);
                return;
            }
            if (sess.Step == SessionStep.DeployJoinWaitingPlaneModel)
            {
                if (!TryParseLong(txt, out long amount) || amount < 0)
                { await SendPrompt(uid, uid, "❌ عدد معتبر (0 برای رد).", ct: ct); return; }
                int index = sess.DeployModelIndex;
                if (index < 0 || index >= sess.DeployModelCounts.Count) { EndSession(uid); return; }
                if (amount > sess.DeployModelCounts[index])
                { await SendPrompt(uid, uid, $"❌ موجودی این مدل: {sess.DeployModelCounts[index]:N0}", ct: ct); return; }
                sess.DeployModelAmounts[index] = amount;
                sess.DeployModelIndex++;
                if (sess.DeployModelIndex < sess.DeployModelNames.Count)
                {
                    int next = sess.DeployModelIndex;
                    await SendPrompt(uid, uid,
                        $"✈️ مشارکت – جنگنده مدل {next + 1}/{sess.DeployModelNames.Count}: {sess.DeployModelNames[next]} – موجودی {sess.DeployModelCounts[next]:N0}\nچند تا؟ (0 برای رد)", ct: ct);
                    return;
                }
                sess.DeployJoinFighters = sess.DeployModelAmounts.Sum();
                sess.DeployPlaneModelNamesFinal = new List<string>(sess.DeployModelNames);
                sess.DeployPlaneModelAmountsFinal = new List<long>(sess.DeployModelAmounts);
                var c = Database.GetCountry(uid, sess.DeployChatId);
                if (c == null) { EndSession(uid); return; }
                var bombers = GetTransferBreakdown(c, "bombers");
                if (bombers.Count == 0)
                {
                    sess.DeployJoinBombers = 0;
                    sess.Step = SessionStep.DeployJoinWaitingBombers;
                    await SendPrompt(uid, uid, "🛩 بمب‌افکن ندارید؛ برای ثبت نهایی عدد 0 را ارسال کنید.", ct: ct);
                    return;
                }
                sess.DeployModelNames = bombers.Select(x => x.ModelName).ToList();
                sess.DeployModelCounts = bombers.Select(x => x.Count).ToList();
                sess.DeployModelAmounts = new List<long>(new long[bombers.Count]);
                sess.DeployModelIndex = 0;
                sess.Step = SessionStep.DeployJoinWaitingBomberModel;
                await SendPrompt(uid, uid,
                    $"🛩 مشارکت – بمب‌افکن مدل 1/{bombers.Count}: {bombers[0].ModelName} – موجودی {bombers[0].Count:N0}\nچند تا اعزام شود؟ (0 برای رد)", ct: ct);
                return;
            }
            if (sess.Step == SessionStep.DeployJoinWaitingBomberModel)
            {
                if (!TryParseLong(txt, out long amount) || amount < 0)
                { await SendPrompt(uid, uid, "❌ عدد معتبر (0 برای رد).", ct: ct); return; }
                int index = sess.DeployModelIndex;
                if (index < 0 || index >= sess.DeployModelCounts.Count) { EndSession(uid); return; }
                if (amount > sess.DeployModelCounts[index])
                { await SendPrompt(uid, uid, $"❌ موجودی این مدل: {sess.DeployModelCounts[index]:N0}", ct: ct); return; }
                sess.DeployModelAmounts[index] = amount;
                sess.DeployModelIndex++;
                if (sess.DeployModelIndex < sess.DeployModelNames.Count)
                {
                    int next = sess.DeployModelIndex;
                    await SendPrompt(uid, uid,
                        $"🛩 مشارکت – بمب‌افکن مدل {next + 1}/{sess.DeployModelNames.Count}: {sess.DeployModelNames[next]} – موجودی {sess.DeployModelCounts[next]:N0}\nچند تا؟ (0 برای رد)", ct: ct);
                    return;
                }
                sess.DeployJoinBombers = sess.DeployModelAmounts.Sum();
                sess.DeployBomberModelNamesFinal = new List<string>(sess.DeployModelNames);
                sess.DeployBomberModelAmountsFinal = new List<long>(sess.DeployModelAmounts);
                sess.Step = SessionStep.DeployJoinWaitingBombers;
                await SendPrompt(uid, uid, "✅ ترکیب نیرو کامل شد؛ برای ثبت نهایی عدد 0 را ارسال کنید.", ct: ct);
                return;
            }
            if (sess.Step == SessionStep.DeployJoinWaitingBombers)
            {
                long bom;
                if (sess.DeployBomberModelAmountsFinal.Count > 0)
                    bom = sess.DeployBomberModelAmountsFinal.Sum();
                else if (!TryParseLong(txt, out bom) || bom < 0)
                { await SendPrompt(uid, uid, "❌ عدد معتبر.", ct: ct); return; }
                var c = Database.GetCountry(uid, sess.DeployChatId);
                if (c == null) { EndSession(uid); return; }
                if (bom > c.Bombers) { await SendPrompt(uid, uid, $"❌ موجودی: {c.Bombers}", ct: ct); return; }
                sess.DeployJoinBombers = bom;
                var dep = Database.GetDeploymentById(sess.DeployJoinId);
                if (dep == null || dep.EndAtMs <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) { EndSession(uid); await SendTemp(uid, "❌ مهلت پایان یافته.", ct: ct); return; }
                if (!HasAvailableForces(c, sess.DeployJoinTanks, sess.DeployJoinSoldiers, sess.DeployJoinFighters, sess.DeployJoinBombers))
                {
                    EndSession(uid);
                    await SendTemp(uid,
                        "❌ موجودی نیروها در طول عملیات تغییر کرده است. اعزام انجام نشد.\n\n" +
                        AvailableForcesText(c),
                        ct: ct);
                    return;
                }
                var contrib = new DeploymentContributor { DeploymentId = dep.Id, UserId = uid, Tanks = sess.DeployJoinTanks, Soldiers = sess.DeployJoinSoldiers, Fighters = sess.DeployJoinFighters, Bombers = sess.DeployJoinBombers, Strategy = sess.DeployJoinStrategy, Tactic = sess.DeployJoinTactic };
                var selectedTankModels = SelectedDeploymentModels(sess.DeployTankModelNamesFinal,
                    sess.DeployTankModelAmountsFinal, sess.DeployJoinTanks, Database.GetDefaultTankModel(c.Faction));
                var selectedFighterModels = SelectedDeploymentModels(sess.DeployPlaneModelNamesFinal,
                    sess.DeployPlaneModelAmountsFinal, sess.DeployJoinFighters, Database.GetDefaultPlaneModel(c.Faction));
                var selectedBomberModels = SelectedDeploymentModels(sess.DeployBomberModelNamesFinal,
                    sess.DeployBomberModelAmountsFinal, sess.DeployJoinBombers, Database.GetDefaultBomberModel(c.Faction));
                bool joined = await TryJoinDeploymentSafely(dep, contrib, ct,
                    selectedTankModels, selectedFighterModels, selectedBomberModels);
                if (!joined)
                {
                    EndSession(uid);
                    await SendTemp(uid, "❌ موجودی نیروها تغییر کرده یا مهلت صف‌آرایی تمام شده است. اعزام انجام نشد.", ct: ct);
                    return;
                }
                Database.ReconcileDefense(uid, sess.DeployChatId);
                //  – defensive join no longer adds to target assets, only tracked separately
                // Refresh pinned deployment announcement to list all participants (only participating players)
                try { await RefreshDeploymentAnnouncement(dep.Id, ct); } catch { }
                EndSession(uid);
                await SendTemp(uid, "✅ نیروها اعزام شدند!", ct: ct);
                string announce = $"🚀 کشور «{c.Name}» ({c.OwnerName}) نیروی کمکی اعزام کرد: {sess.DeployJoinTanks:N0} تانک, {sess.DeployJoinSoldiers:N0} سرباز, {sess.DeployJoinFighters:N0} جنگنده, {sess.DeployJoinBombers:N0} بمب‌افکن";
                try { await SendPermanent(dep.ChatId, announce, ct: ct); } catch { }
                return;
            }

            // Attack steps –  per-model
            if (sess.Step == SessionStep.AttackWaitingTanks)
            {
                // Legacy fallback – should not happen now, redirect to per-model
                var atk = Database.GetCountry(uid, sess.AttackChatId);
                if (atk == null) { EndSession(uid); return; }
                var breakdown = GetAttackBreakdown(atk, "tanks");
                if (breakdown.Count > 0)
                {
                    sess.AttackModelNames = breakdown.Select(x => x.ModelName).ToList();
                    sess.AttackModelCounts = breakdown.Select(x => x.Count).ToList();
                    sess.AttackModelAmounts = new List<long>(new long[breakdown.Count]);
                    sess.AttackModelIndex = 0;
                    sess.AttackCurrentCategory = "tanks";
                    sess.Step = SessionStep.AttackWaitingTankModel;
                    await SendPrompt(uid, uid, $"🛡 حمله – تانک مدل 1/{breakdown.Count}: {breakdown[0].ModelName} – موجودی {breakdown[0].Count:N0}\nچند تا اعزام شود؟ (0 برای رد)", ct: ct);
                    return;
                }
                sess.AttackTanks = 0;
                sess.Step = SessionStep.AttackWaitingSoldiers;
                await SendPrompt(uid, uid, "🪖 تعداد سربازان اعزامی را وارد کنید.\n" + InventoryLine(GetAttackAvailableSoldiers(atk)), ct: ct);
                return;
            }

            if (sess.Step == SessionStep.AttackWaitingModelAmount)
            {
                //  – naval per-model amount
                if (!TryParseLong(txt, out long amt) || amt < 0) { await SendPrompt(uid, uid, "❌ عدد معتبر (0 برای رد).", ct: ct); return; }
                int idx = sess.AttackModelIndex;
                if (idx < 0 || idx >= sess.AttackModelCounts.Count) { EndSession(uid); return; }
                if (amt > sess.AttackModelCounts[idx]) { await SendPrompt(uid, uid, $"❌ موجودی این مدل: {sess.AttackModelCounts[idx]:N0}", ct: ct); return; }
                sess.AttackModelAmounts[idx] = amt;
                sess.AttackModelIndex++;
                if (sess.AttackModelIndex < sess.AttackModelNames.Count)
                {
                    var next = sess.AttackModelNames[sess.AttackModelIndex];
                    var nextCnt = sess.AttackModelCounts[sess.AttackModelIndex];
                    string cat = next.Contains(':') ? next.Split(':')[0] : "naval";
                    string modelOnly = next.Contains(':') ? next.Split(':',2)[1] : next;
                    await SendPrompt(uid, uid, $"⚓ حمله دریایی – مدل {sess.AttackModelIndex + 1}/{sess.AttackModelNames.Count}: {modelOnly} ({cat}) – موجودی {nextCnt:N0}\nچند تا اعزام شود؟ (0 برای رد)", ct: ct);
                    return;
                }
                // All naval models entered – finalize invasion
                long totalBoats = 0, totalSubs = 0, totalBS = 0;
                var boatModelsList = new List<string>();
                var subModelsList = new List<string>();
                var bsModelsList = new List<string>();
                for (int i = 0; i < sess.AttackModelNames.Count; i++)
                {
                    string full = sess.AttackModelNames[i];
                    long amount = sess.AttackModelAmounts[i];
                    if (amount <= 0) continue;
                    string[] partsArr = full.Split(':', 2);
                    string cat = partsArr.Length == 2 ? partsArr[0] : "boats";
                    string model = partsArr.Length == 2 ? partsArr[1] : full;
                    if (cat == "boats") { totalBoats += amount; boatModelsList.Add($"{model}:{amount}"); }
                    else if (cat == "submarines") { totalSubs += amount; subModelsList.Add($"{model}:{amount}"); }
                    else if (cat == "battleships") { totalBS += amount; bsModelsList.Add($"{model}:{amount}"); }
                }
                if (totalBoats + totalSubs + totalBS == 0)
                {
                    EndSession(uid);
                    await SendTemp(uid, "❌ هیچ نیروی دریایی انتخاب نشد – حمله لغو شد.", ct: ct);
                    return;
                }
                var attackerCountry = Database.GetCountry(uid, sess.AttackChatId);
                var defenderCountry = Database.GetCountry(sess.AttackTargetId, sess.AttackChatId);
                if (attackerCountry == null || defenderCountry == null) { EndSession(uid); await SendTemp(uid, "❌ کشور یافت نشد.", ct: ct); return; }

                bool fullExemption = Database.HasGroupLockExemption(sess.AttackChatId);
                if (Database.IsAttackShieldActive(defenderCountry.OwnerId, defenderCountry.ChatId) && !fullExemption)
                {
                    long until = Database.GetAttackShieldUntilMs(defenderCountry.OwnerId, defenderCountry.ChatId);
                    long leftH = Math.Max(1, (until - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) / 3600000);
                    EndSession(uid);
                    await SendTemp(uid, $"🛡 {defenderCountry.Name} تا {leftH} ساعت دیگر سپر فعال دارد.", ct: ct);
                    return;
                }
                var selectedBoats = boatModelsList.Select(x => x.Split(':', 2))
                    .Where(x => x.Length == 2 && long.TryParse(x[1], out _))
                    .Select(x => new NavalModelAmount(x[0], long.Parse(x[1]))).ToList();
                var selectedSubs = subModelsList.Select(x => x.Split(':', 2))
                    .Where(x => x.Length == 2 && long.TryParse(x[1], out _))
                    .Select(x => new NavalModelAmount(x[0], long.Parse(x[1]))).ToList();
                var selectedBs = bsModelsList.Select(x => x.Split(':', 2))
                    .Where(x => x.Length == 2 && long.TryParse(x[1], out _))
                    .Select(x => new NavalModelAmount(x[0], long.Parse(x[1]))).ToList();
                if(GetNavalAttackCount(sess.AttackChatId,uid)>=MAX_NAVAL_ATTACKS_PER_UPDATE&&!fullExemption)
                {
                    EndSession(uid);
                    await SendTemp(uid,$"⛔ سهمیه حمله دریایی این کشور تمام شده است ({MAX_NAVAL_ATTACKS_PER_UPDATE} عملیات تا آپدیت بعدی).",ct:ct);
                    return;
                }
                // معافیت کامل تمام انتظارهای حمله را حذف می‌کند؛ سفر دریایی فقط یک دقیقه است.
                int travelMinutes = fullExemption ? 1 : Random.Shared.Next(10, 301);
                long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                long operationId;
                try
                {
                    operationId = Database.CreateNavalOperation(attackerCountry, defenderCountry,
                        selectedBoats, selectedSubs, selectedBs, sess.AttackNavalTactic, nowMs, travelMinutes);
                    BreakAttackerShieldOnAttack(attackerCountry.OwnerId,attackerCountry.ChatId);
                    IncNavalAttackCount(attackerCountry.ChatId,attackerCountry.OwnerId);
                    ScheduleNavalArrival(operationId,nowMs+travelMinutes*60_000L);
                }
                catch (Exception ex)
                {
                    EndSession(uid);
                    Console.WriteLine($"[NAVAL LAUNCH ERR] {ex}");
                    await SendTemp(uid, "❌ موجودی یا وضعیت ناوگان تغییر کرده است؛ حمله ثبت نشد.", ct: ct);
                    return;
                }
                EndSession(uid);
                await SendTemp(uid, $"⚓ عملیات دریایی #{operationId} آغاز شد.\n🎯 مقصد: {defenderCountry.Name}\n" +
                    $"🚤 {totalBoats:N0} | ⚓ {totalSubs:N0} | 🚢 {totalBS:N0}\n" +
                    $"⏱ زمان تقریبی رسیدن: {travelMinutes} دقیقه", ct: ct);
                try
                {
                    string attackedGroupTitle=await GetGroupTitleCached(sess.AttackChatId,ct);
                    await SendPermanent(defenderCountry.OwnerId,
                        $"⚠️ هشدار حمله دریایی!\nکشور {attackerCountry.Name} ناوگانی به سمت شما فرستاده است.\n" +
                        $"💬 گپ مورد حمله: {attackedGroupTitle}\n🆔 شناسه گپ: {sess.AttackChatId}\n" +
                        $"⏱ زمان تقریبی رسیدن: {travelMinutes} دقیقه\nترکیب ناوگان مهاجم نامشخص است.", ct: ct);
                }
                catch { }
                try { await SendPermanent(sess.AttackChatId,
                    $"⚓ {attackerCountry.Name} عملیات دریایی علیه {defenderCountry.Name} آغاز کرد.", ct: ct); } catch { }
                return;
            }

            if (sess.Step == SessionStep.AttackWaitingTankModel)
            {
                if (!TryParseLong(txt, out long amt) || amt < 0) { await SendPrompt(uid, uid, "❌ عدد معتبر (0 برای رد).", ct: ct); return; }
                int idx = sess.AttackModelIndex;
                if (idx < 0 || idx >= sess.AttackModelCounts.Count) { EndSession(uid); return; }
                if (amt > sess.AttackModelCounts[idx]) { await SendPrompt(uid, uid, $"❌ موجودی این مدل: {sess.AttackModelCounts[idx]:N0}", ct: ct); return; }
                sess.AttackModelAmounts[idx] = amt;
                sess.AttackModelIndex++;
                if (sess.AttackModelIndex < sess.AttackModelNames.Count)
                {
                    var nextName = sess.AttackModelNames[sess.AttackModelIndex];
                    var nextCount = sess.AttackModelCounts[sess.AttackModelIndex];
                    await SendPrompt(uid, uid, $"🛡 حمله – تانک مدل {sess.AttackModelIndex + 1}/{sess.AttackModelNames.Count}: {nextName} – موجودی {nextCount:N0}\nچند تا اعزام شود؟ (0 برای رد)", ct: ct);
                    return;
                }
                // Finished tanks – calculate total and move to soldiers
                sess.AttackTanks = sess.AttackModelAmounts.Sum();
                // Save final tank breakdown for war engine
                sess.AttackTankModelNamesFinal = new List<string>(sess.AttackModelNames);
                sess.AttackTankModelAmountsFinal = new List<long>(sess.AttackModelAmounts);
                // Reset for next category
                sess.AttackModelNames = new List<string>();
                sess.AttackModelCounts = new List<long>();
                sess.AttackModelAmounts = new List<long>();
                sess.AttackModelIndex = 0;
                sess.AttackCurrentCategory = "soldiers";
                sess.Step = SessionStep.AttackWaitingSoldiers;
                var atk = Database.GetCountry(uid, sess.AttackChatId);
                await SendPrompt(uid, uid, "🪖 تعداد سربازان اعزامی را وارد کنید.\n" + InventoryLine(atk == null ? 0 : GetAttackAvailableSoldiers(atk)), ct: ct);
                return;
            }

            if (sess.Step == SessionStep.AttackWaitingSoldiers)
            {
                if (!TryParseLong(txt, out long soldiers) || soldiers < 0) { await SendPrompt(uid, uid, "❌ عدد معتبر.", ct: ct); return; }
                var atk = Database.GetCountry(uid, sess.AttackChatId);
                if (atk == null) { EndSession(uid); return; }
                long availableSoldiers = GetAttackAvailableSoldiers(atk);
                if (soldiers > availableSoldiers)
                { await SendPrompt(uid, uid, $"❌ قابل اعزام: {availableSoldiers:N0}؛ حداقل ۲۰٪ در دفاع می‌ماند.", ct: ct); return; }
                sess.AttackSoldiers = soldiers;

                // Now per-model planes
                var planeBreakdown = GetAttackBreakdown(atk, "planes");
                if (planeBreakdown.Count == 0)
                {
                    sess.AttackFighters=0;sess.AttackPlaneModelNamesFinal=new();sess.AttackPlaneModelAmountsFinal=new();
                    await BeginAttackBomberSelection(uid,sess,atk,ct);
                    return;
                }
                if (planeBreakdown.Count == 1)
                {
                    sess.AttackModelNames = new List<string> { planeBreakdown[0].ModelName };
                    sess.AttackModelCounts = new List<long> { planeBreakdown[0].Count };
                    sess.AttackModelAmounts = new List<long> { 0 };
                    sess.AttackModelIndex = 0;
                    sess.AttackCurrentCategory = "planes";
                    sess.Step = SessionStep.AttackWaitingPlaneModel;
                    await SendPrompt(uid, uid, $"✈️ جنگنده مدل {planeBreakdown[0].ModelName} – موجودی {planeBreakdown[0].Count:N0}\nچند تا اعزام شود؟", ct: ct);
                    return;
                }
                sess.AttackModelNames = planeBreakdown.Select(x => x.ModelName).ToList();
                sess.AttackModelCounts = planeBreakdown.Select(x => x.Count).ToList();
                sess.AttackModelAmounts = new List<long>(new long[planeBreakdown.Count]);
                sess.AttackModelIndex = 0;
                sess.AttackCurrentCategory = "planes";
                sess.Step = SessionStep.AttackWaitingPlaneModel;
                await SendPrompt(uid, uid, $"✈️ حمله – جنگنده‌ها – {planeBreakdown.Count} مدل\n🔧 مدل 1/{planeBreakdown.Count}: {planeBreakdown[0].ModelName} – {planeBreakdown[0].Count:N0}\nچند تا اعزام شود؟ (0 برای رد)", ct: ct);
                return;
            }

            if (sess.Step == SessionStep.AttackWaitingPlaneModel)
            {
                if (!TryParseLong(txt, out long amt) || amt < 0) { await SendPrompt(uid, uid, "❌ عدد معتبر.", ct: ct); return; }
                int idx = sess.AttackModelIndex;
                if (idx < 0 || idx >= sess.AttackModelCounts.Count) { EndSession(uid); return; }
                if (amt > sess.AttackModelCounts[idx]) { await SendPrompt(uid, uid, $"❌ موجودی: {sess.AttackModelCounts[idx]:N0}", ct: ct); return; }
                sess.AttackModelAmounts[idx] = amt;
                sess.AttackModelIndex++;
                if (sess.AttackModelIndex < sess.AttackModelNames.Count)
                {
                    await SendPrompt(uid, uid, $"✈️ مدل {sess.AttackModelIndex + 1}/{sess.AttackModelNames.Count}: {sess.AttackModelNames[sess.AttackModelIndex]} – موجودی {sess.AttackModelCounts[sess.AttackModelIndex]:N0}\nچند تا؟ (0 برای رد)", ct: ct);
                    return;
                }
                sess.AttackFighters = sess.AttackModelAmounts.Sum();
                sess.AttackPlaneModelNamesFinal = new List<string>(sess.AttackModelNames);
                sess.AttackPlaneModelAmountsFinal = new List<long>(sess.AttackModelAmounts);
                var atk = Database.GetCountry(uid, sess.AttackChatId);
                if(atk==null){EndSession(uid);return;}
                await BeginAttackBomberSelection(uid,sess,atk,ct);
                return;
            }

            if (sess.Step == SessionStep.AttackWaitingBomberModel)
            {
                if (!TryParseLong(txt, out long amt) || amt < 0) { await SendPrompt(uid, uid, "❌ عدد معتبر.", ct: ct); return; }
                int idx = sess.AttackModelIndex;
                if (idx < 0 || idx >= sess.AttackModelCounts.Count) { EndSession(uid); return; }
                if (amt > sess.AttackModelCounts[idx]) { await SendPrompt(uid, uid, $"❌ موجودی: {sess.AttackModelCounts[idx]:N0}", ct: ct); return; }
                sess.AttackModelAmounts[idx] = amt;
                sess.AttackModelIndex++;
                if (sess.AttackModelIndex < sess.AttackModelNames.Count)
                {
                    await SendPrompt(uid, uid, $"🛩 مدل {sess.AttackModelIndex + 1}/{sess.AttackModelNames.Count}: {sess.AttackModelNames[sess.AttackModelIndex]} – موجودی {sess.AttackModelCounts[sess.AttackModelIndex]:N0}\nچند تا؟ (0 برای رد)", ct: ct);
                    return;
                }
                sess.AttackBombers = sess.AttackModelAmounts.Sum();
                sess.AttackBomberModelNamesFinal = new List<string>(sess.AttackModelNames);
                sess.AttackBomberModelAmountsFinal = new List<long>(sess.AttackModelAmounts);
                await PromptAttackAirOrRun(uid,sess,ct);
                return;
            }

            if (sess.Step == SessionStep.AttackWaitingFighters)
            {
                var atk=Database.GetCountry(uid,sess.AttackChatId);if(atk==null){EndSession(uid);return;}
                var planes=GetAttackBreakdown(atk,"planes");
                if(planes.Count==0){sess.AttackFighters=0;await BeginAttackBomberSelection(uid,sess,atk,ct);return;}
                sess.AttackModelNames=planes.Select(x=>x.ModelName).ToList();sess.AttackModelCounts=planes.Select(x=>x.Count).ToList();
                sess.AttackModelAmounts=new List<long>(new long[planes.Count]);sess.AttackModelIndex=0;
                sess.AttackCurrentCategory="planes";sess.Step=SessionStep.AttackWaitingPlaneModel;
                await SendPrompt(uid,uid,$"✈️ انتخاب مدل‌به‌مدل فعال شد.\nمدل 1/{planes.Count}: {planes[0].ModelName}\nموجودی قابل اعزام: {planes[0].Count:N0}\nچند فروند؟",ct:ct);return;
            }
            if (sess.Step == SessionStep.AttackWaitingBombers)
            {
                var atk=Database.GetCountry(uid,sess.AttackChatId);if(atk==null){EndSession(uid);return;}
                await BeginAttackBomberSelection(uid,sess,atk,ct);return;
            }
        }

        if (txt == "وضعیت دفاع")
        {
            var chatIds = Database.GetUserActiveChatIds(uid);
            if (chatIds.Count == 0) { await SendTemp(uid, "❌ شما در هیچ گروهی کشور ندارید.", ct: ct); return; }
            if (chatIds.Count == 1) { await SendDefenseStatus(uid, uid, chatIds[0], ct); }
            else
            {
                var allC = Database.GetAllCountries();
                var kb = chatIds.Select(cid => { var n = allC.FirstOrDefault(c => c.ChatId == cid && c.OwnerId == uid)?.Name ?? cid.ToString(); return new[] { InlineKeyboardButton.WithCallbackData(n, $"defense_status:{cid}") }; }).ToArray();
                sessions[uid] = new UserSession { Step = SessionStep.DefenseWaitingGroup };
                await SendPrompt(uid, uid, "📋 گروه:", new InlineKeyboardMarkup(kb), ct);
            }
            return;
        }

        if (txt == "ترنسفر" || txt == "انتقال" || txt == "ارسال محموله" || txt == "ارسال منابع")
        {
            var chatIds = Database.GetUserActiveChatIds(uid);
            if (chatIds.Count == 0) { await SendTemp(uid, "❌ شما در هیچ گروهی کشور ندارید.", ct: ct); return; }
            if (chatIds.Count == 1)
            {
                long cid = chatIds[0];
                var pc = Database.GetCountry(uid, cid); if (pc != null && pc.PortLevel < 3) { await SendTemp(uid, "⚓ سطح بندر شما برای این عملیات کافی نیست! (حداقل سطح مورد نیاز: ۳)", ct: ct); return; }
                long aid = Database.GetUserAllianceId(cid, uid);
                if (aid == 0) { await SendTemp(uid, "❌ عضو هیچ اتحادی نیستید.", ct: ct); return; }
                var mems = Database.GetAllianceMembers(aid).Where(m => m != uid).ToList();
                if (mems.Count == 0) { await SendTemp(uid, "❌ اتحاد عضو دیگری ندارد.", ct: ct); return; }
                if (GetTransferCount(cid, uid) >= MAX_TRANSFERS_PER_UPDATE && !Database.HasGroupLockExemption(cid)) { await SendTemp(uid, $"⛔ سهمیه تمام شد ({MAX_TRANSFERS_PER_UPDATE}).", ct: ct); return; }
                sessions[uid] = new UserSession { Step = SessionStep.TransferWaitingResource, TransferChatId = cid, TransferAllianceId = aid };
                var kb = new InlineKeyboardMarkup(new[] {
                    new[] { InlineKeyboardButton.WithCallbackData("💰 پول", $"tf_res:{cid}:money"), InlineKeyboardButton.WithCallbackData("🔩 آهن", $"tf_res:{cid}:iron") },
                    new[] { InlineKeyboardButton.WithCallbackData("🪖 سرباز", $"tf_res:{cid}:soldiers"), InlineKeyboardButton.WithCallbackData("🛡 تانک", $"tf_res:{cid}:tanks") },
                    new[] { InlineKeyboardButton.WithCallbackData("✈️ جنگنده", $"tf_res:{cid}:planes"), InlineKeyboardButton.WithCallbackData("🛩 بمب‌افکن", $"tf_res:{cid}:bombers") },
                    new[] { InlineKeyboardButton.WithCallbackData("🚤 قایق", $"tf_res:{cid}:boats"), InlineKeyboardButton.WithCallbackData("⚓ زیردریایی", $"tf_res:{cid}:submarines") },
                    new[] { InlineKeyboardButton.WithCallbackData("🚢 نبردناو", $"tf_res:{cid}:battleships") }
                });
                await SendPrompt(uid, uid, "📦 **ترنسفر**\n\nنوع منبع:", kb, ct);
            }
            else
            {
                var kb = chatIds.Select(cid => { var c = Database.GetCountry(uid, cid); return new[] { InlineKeyboardButton.WithCallbackData(c?.Name ?? $"گروه {cid}", $"tf_chat:{cid}") }; }).ToArray();
                await SendPrompt(uid, uid, "📦 گروه:", new InlineKeyboardMarkup(kb), ct);
            }
            return;
        }

        if (txt == "صف آرایی تهاجمی" || txt == "صف آرایی دفاعی" || txt == "صف‌آرایی تهاجمی" || txt == "صف‌آرایی دفاعی")
        {
            bool isOff = txt.Contains("تهاجمی");
            var chatIds = Database.GetUserActiveChatIds(uid);
            if (chatIds.Count == 0) { await SendTemp(uid, "❌ شما کشوری ندارید.", ct: ct); return; }
            if (chatIds.Count == 1)
            {
                long cid = chatIds[0];
                var pc = Database.GetCountry(uid, cid); if (pc != null && pc.PortLevel < 3) { await SendTemp(uid, "⚓ سطح بندر شما برای این عملیات کافی نیست! (حداقل سطح مورد نیاز: ۳)", ct: ct); return; }
                long aid = Database.GetUserAllianceId(cid, uid);
                if (aid == 0) { await SendTemp(uid, "❌ عضو اتحاد نیستید.", ct: ct); return; }
                var mems = Database.GetAllianceMembers(aid);
                int dailyLimit = mems.Count <= 5 ? 1 : (mems.Count <= 10 ? 2 : (mems.Count <= 20 ? 3 : 5));
                if (Database.GetRecentAllianceDeploymentsCount(aid, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 86400000L) >= dailyLimit && !Database.HasGroupLockExemption(cid)) { await SendTemp(uid, $"⛔ سقف روزانه ({dailyLimit}) پر شد.", ct: ct); return; }
                var tgts = isOff ? Database.GetAttackableTargets(cid, uid) : mems.Select(m => Database.GetCountry(m, cid)).Where(c => c != null).ToList()!;
                if (tgts.Count == 0) { await SendTemp(uid, isOff ? "❌ هیچ هدفی نیست." : "❌ عضو معتبری برای دفاع نیست.", ct: ct); return; }
                var tkb = tgts.Select(t => new[] { InlineKeyboardButton.WithCallbackData($"🏳️ {t!.Name} ({t.OwnerName})", $"dep_target:{cid}:{aid}:{(isOff ? "Off" : "Def")}:{t.OwnerId}") }).ToArray();
                await SendPrompt(uid, uid, $"⚔️ صف‌آرایی {(isOff ? "تهاجمی" : "دفاعی")}\n🎯 کشور:", new InlineKeyboardMarkup(tkb), ct);
            }
            else
            {
                var kb = chatIds.Select(cid => { var c = Database.GetCountry(uid, cid); return new[] { InlineKeyboardButton.WithCallbackData(c?.Name ?? $"گروه {cid}", $"dep_chat:{cid}:{(isOff ? "Offensive" : "Defensive")}") }; }).ToArray();
                await SendPrompt(uid, uid, "⚔️ گروه:", new InlineKeyboardMarkup(kb), ct);
            }
            return;
        }

        if (txt == "حمله")
        {
            var chatIds = Database.GetUserActiveChatIds(uid);
            if (chatIds.Count == 0) { await SendTemp(uid, "❌ شما کشوری ندارید.", ct: ct); return; }
            if (chatIds.Count == 1)
            {
                long cid = chatIds[0];
                var targets = Database.GetAttackableTargets(cid, uid);
                if (targets.Count == 0) { await SendTemp(uid, "هیچ هدفی نیست.", ct: ct); return; }
                var kb = targets.Select(t => new[] { InlineKeyboardButton.WithCallbackData($"{t.Name} ({t.OwnerName})", $"attack_target:{cid}:{t.OwnerId}") }).ToArray();
                sessions[uid] = new UserSession { Step = SessionStep.AttackWaitingTarget, AttackChatId = cid };
                await SendPrompt(uid, uid, "🎯 هدف:", new InlineKeyboardMarkup(kb), ct);
            }
            else
            {
                var kb = chatIds.Select(cid =>
                {
                    var country = Database.GetCountry(uid, cid);
                    var name = country?.Name ?? cid.ToString();
                    return new[] { InlineKeyboardButton.WithCallbackData(name, $"attack_group:{cid}") };
                }).ToArray();
                sessions[uid] = new UserSession { Step = SessionStep.AttackWaitingGroup };
                await SendPrompt(uid, uid, "📋 گروه:", new InlineKeyboardMarkup(kb), ct);
            }
            return;
        }

        // FIX(3): /start و راهنما در پیوی — تا کاربر فکر نکند بات خاموش است
        if (txt == "/start" || txt == "شروع" || txt == "start")
        {
            await SendStartMessage(uid, ct);
            return;
        }
        if (txt == "راهنما" || txt == "/help" || txt == "help")
        {
            await SendPermanent(uid, HelpText, parseMode: ParseMode.Html, ct: ct);
            return;
        }

        // FIX(3): هر پیام ناشناختهٔ دیگر در پیوی → راهنمای کوتاه به‌جای سکوت
        await SendPermanent(uid,
            "ℹ️ این ربات یک بازی گروهی است و بیشتر دستورها فقط داخل گروه کار می‌کنند.\n" +
            "برای دیدن راهنمای کامل بنویسید: <b>راهنما</b>\n" +
            "برای شروع/توضیح بیشتر بنویسید: <b>/start</b>",
            parseMode: ParseMode.Html, ct: ct);
    }

    // ============================================================
    //  Callback handlers
    // ============================================================
    static async Task HandleCallbackAsync(CallbackQuery cb, CancellationToken ct)
    {
        if (cb.Data == null) return;

        if(cb.Data.StartsWith("spam_admin:",StringComparison.Ordinal))
        {
            await HandleSpamAdminCallback(cb,ct);
            return;
        }
        if (cb.Data.StartsWith("adm:", StringComparison.Ordinal))
        {
            await HandleAdminCallbackAsync(cb, ct);
            return;
        }

        if (cb.Data.StartsWith("ally_")) { await HandleAllianceInviteCallback(cb, ct); return; }
        if (cb.Data.StartsWith("tf_")) { await HandleTransferCallback(cb, ct); return; }
        if (cb.Data.StartsWith("dep_")) { await HandleDeploymentCallback(cb, ct); return; }
        if (cb.Message == null) return;
        var parts = cb.Data.Split(':');
        if (parts.Length < 1) return;

        if (parts[0] is "eq_details" or "dep_info" or "faction" or "build_menu" or "upgrade" or "tank_info" or "tank_buy" or "plane_info" or "plane_buy" or "bomber_info" or "bomber_buy" or "aa_info" or "aa_buy" or "boat_info" or "boat_buy" or "sub_info" or "sub_buy" or "battleship_info" or "battleship_buy" or "battleship_repair" or "battleship_scrap_menu" or "cancel")
        {
            if (parts.Length >= 2 && TryParseLong(parts[1], out long ownerBtn))
            {
                if (ownerBtn != cb.From.Id) { await bot.AnswerCallbackQueryAsync(cb.Id, "⛔ این دکمه برای شما نیست!", showAlert: true, cancellationToken: ct); return; }
            }
        }

        switch (parts[0])
        {
            case "cancel": await HandleCancelCallback(cb, ct); break;
            case "faction": await HandleFactionCallback(cb, parts, ct); break;
            case "eq_details": await SendCountryEquipmentDetails(cb, parts, ct); break;
            case "dep_info": await SendDeploymentInfoDetails(cb, parts, ct); break;
            case "build_menu": await HandleBuildMenuCallback(cb, parts, ct); break;
            case "upgrade": await HandleUpgradeCallback(cb, parts, ct); break;
            case "timing": await HandleTimingCallback(cb, parts, ct); break;
            case "tank_info": await HandleTankInfoCallback(cb, parts, ct); break;
            case "tank_buy": await HandleTankBuyCallback(cb, parts, ct); break;
            case "plane_info": await HandlePlaneInfoCallback(cb, parts, ct); break;
            case "plane_buy": await HandlePlaneBuyCallback(cb, parts, ct); break;
            case "bomber_info": await HandleBomberInfoCallback(cb, parts, ct); break;
            case "bomber_buy": await HandleBomberBuyCallback(cb, parts, ct); break;
            case "aa_info": await HandleAntiAirInfoCallback(cb, parts, ct); break;
            case "aa_buy": await HandleAntiAirBuyCallback(cb, parts, ct); break;
            case "defense_status": await HandleDefenseStatusCallback(cb, parts, ct); break;
            case "defense_tactic": await HandleDefenseTacticCallback(cb, parts, ct); break;
            case "defense_tactic_select": await HandleDefenseTacticSelectCallback(cb, parts, ct); break;
            case "defense_set": await HandleDefenseSetCallback(cb, parts, ct); break;
            case "naval_defense": await HandleNavalDefenseCallback(cb, parts, ct); break;
            case "naval_defense_strategy": await HandleNavalDefenseStrategyCallback(cb, parts, ct); break;
            case "naval_defense_tactic": await HandleNavalDefenseTacticCallback(cb, parts, ct); break;
            case "naval_cancel": await HandleNavalCancellationCallback(cb, parts, ct); break;
            case "naval_locked": await bot.AnswerCallbackQueryAsync(cb.Id, "🔒 این استراتژی فعلاً قفل است.", showAlert: true, cancellationToken: ct); break;
            case "defense_pct": await HandleDefensePctCallback(cb, parts, ct); break;
            case "defense_model_pct": await HandleDefenseModelPctCallback(cb, parts, ct); break;
            case "boat_info": await HandleBoatInfoCallback(cb, parts, ct); break;
            case "boat_buy": await HandleBoatBuyCallback(cb, parts, ct); break;
            case "sub_info": await HandleSubInfoCallback(cb, parts, ct); break;
            case "sub_buy": await HandleSubBuyCallback(cb, parts, ct); break;
            case "battleship_info": await HandleBattleshipInfoCallback(cb, parts, ct); break;
            case "battleship_buy": await HandleBattleshipBuyCallback(cb, parts, ct); break;
            case "battleship_repair": await HandleBattleshipRepairCallback(cb, parts, ct); break;
            case "battleship_repair_quote": await HandleBattleshipRepairQuoteCallback(cb, parts, ct); break;
            case "battleship_repair_unit": await HandleBattleshipRepairUnitCallback(cb, parts, ct); break;
            case "battleship_scrap_menu": await HandleBattleshipScrapMenuCallback(cb, ct); break;
            case "battleship_scrap": await HandleBattleshipScrapQuoteCallback(cb, parts, ct); break;
            case "battleship_scrap_confirm": await HandleBattleshipScrapConfirmCallback(cb, parts, ct); break;
            case "airdef_strategy": await HandleAirDefStrategyCallback(cb, parts, ct); break;
            case "airdef_tactic": await HandleAirDefTacticCallback(cb, parts, ct); break;
            case "attack_group": await HandleAttackGroupCallback(cb, parts, ct); break;
            case "attack_target": await HandleAttackTargetCallback(cb, parts, ct); break;
            case "revenge": await HandleRevengeCallback(cb, parts, ct); break;
            case "attack_type": await HandleAttackTypeCallback(cb, parts, ct); break;
            case "attack_strategy": await HandleAttackStrategyCallback(cb, parts, ct); break;
            case "attack_tactic": await HandleAttackTacticCallback(cb, parts, ct); break;
            case "attack_air_strategy": await HandleAttackAirStrategyCallback(cb, parts, ct); break;
            case "attack_air_tactic": await HandleAttackAirTacticCallback(cb, parts, ct); break;
            case "attack_naval_strategy": await HandleAttackNavalStrategyCallback(cb, parts, ct); break;
            case "attack_naval_tactic": await HandleAttackNavalTacticCallback(cb, parts, ct); break;
        }
    }

    static async Task HandleCancelCallback(CallbackQuery cb, CancellationToken ct)
    {
        long uid = cb.From.Id;
        await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
        EndSession(uid);
        if (cb.Message != null) DeleteNow(cb.Message.Chat.Id, cb.Message.MessageId);
    }

    static async Task HandleNavalCancellationCallback(CallbackQuery cb,string[] parts,CancellationToken ct)
    {
        long uid=cb.From.Id;
        if(parts.Length<3||!TryParseLong(parts[1],out long chatId)||!TryParseLong(parts[2],out long operationId))
        {
            await bot.AnswerCallbackQueryAsync(cb.Id,"❌ درخواست نامعتبر است.",showAlert:true,cancellationToken:ct);
            return;
        }
        NavalInvasion? cancelled=null;
        var locks=await AcquireCountryMutationLocks(chatId,new[]{uid},ct);
        try
        {
            var operation=Database.GetCancelableNavalOperation(operationId,uid,chatId);
            if(operation!=null&&Database.ReturnNavalOperationWithoutBattle(operation,"Cancelled"))cancelled=operation;
        }
        finally{ReleaseCountryMutationLocks(locks);}
        if(cancelled==null)
        {
            await bot.AnswerCallbackQueryAsync(cb.Id,"❌ عملیات قبلاً رسیده، لغو شده یا متعلق به شما نیست.",showAlert:true,cancellationToken:ct);
            return;
        }
        EndSession(uid);
        await bot.AnswerCallbackQueryAsync(cb.Id,"✅ کل ناوگان برگشت.",showAlert:true,cancellationToken:ct);
        if(cb.Message!=null)DeleteNow(cb.Message.Chat.Id,cb.Message.MessageId);
        string groupTitle=await GetGroupTitleCached(chatId,ct);
        await SendPermanent(uid,$"✅ لشکرکشی دریایی #{operationId} لغو شد.\n"+
            $"💬 گپ: {groupTitle}\n🎯 مقصد قبلی: {cancelled.DefenderName}\n"+
            $"↩️ کل ناوگان بدون تلفات همان لحظه به دارایی برگشت.\n"+
            $"🚤 {cancelled.Boats:N0} | ⚓ {cancelled.Submarines:N0} | 🚢 {cancelled.Battleships:N0}",ct:ct);
        try{await SendPermanent(cancelled.DefenderId,
            $"ℹ️ هشدار دریایی لغو شد.\nعملیات #{operationId} کشور {cancelled.AttackerName} در گپ «{groupTitle}» متوقف شد.",ct:ct);}catch{}
        try{await SendPermanent(chatId,
            $"↩️ {cancelled.AttackerName} لشکرکشی دریایی #{operationId} علیه {cancelled.DefenderName} را لغو کرد و ناوگانش برگشت.",ct:ct);}catch{}
        Console.WriteLine($"[NAVAL CANCELLED] operation={operationId} attacker={uid} chat={chatId} boats={cancelled.Boats} subs={cancelled.Submarines} battleships={cancelled.Battleships}");
    }

    static async Task HandleFactionCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length != 3) return;
        long uid = cb.From.Id;
        string facStr = parts[2];
        Faction fac = facStr switch { "USSR" => Faction.USSR, "USA" => Faction.USA, _ => Faction.Reich };
        sessions[uid] = new UserSession { Step = SessionStep.WaitingCountryName, Faction = fac, FactionStr = facStr };
        if (cb.Message != null) { await bot.EditMessageTextAsync(cb.Message.Chat.Id, cb.Message.MessageId, "اسم کشور را وارد کنید", cancellationToken: ct); TrackPrompt(uid, cb.Message.Chat.Id, cb.Message.MessageId); }
        await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
    }

    static async Task HandleBuildMenuCallback(
        CallbackQuery cb,
        string[] parts,
        CancellationToken ct)
    {
        if (parts.Length != 3 || cb.Message == null)
            return;

        long uid = cb.From.Id;
        string bt = parts[2];

        if (bt is not ("factory" or "port" or "mine"))
        {
            await bot.AnswerCallbackQueryAsync(
                cb.Id,
                "❌ ساختمان نامعتبر است.",
                showAlert: true,
                cancellationToken: ct
            );
            return;
        }

        long chatId = cb.Message.Chat.Id;
        var c = Database.GetCountry(uid, chatId);

        if (c == null)
        {
            await bot.AnswerCallbackQueryAsync(
                cb.Id,
                "❌ کشوری ندارید!",
                cancellationToken: ct
            );
            return;
        }

        int cur = bt switch
        {
            "factory" => c.FactoryLevel,
            "port" => c.PortLevel,
            "mine" => c.MineLevel,
            _ => 1
        };

        int max = MaxBuildLevel(c, bt);

        if (cur >= max)
        {
            string maxMessage = c.Besieged >= 2
                ? "🔒 به‌دلیل شرایط بحرانی، امکان ارتقا وجود ندارد."
                : "✅ این ساختمان در حداکثر سطح است.";

            await bot.AnswerCallbackQueryAsync(
                cb.Id,
                maxMessage,
                showAlert: true,
                cancellationToken: ct
            );
            return;
        }

        int next = cur + 1;

        double currentIncome = bt switch
        {
            "factory" => FactoryIncome[cur],
            "port" => PortIncome[cur],
            "mine" => MineIncome[cur],
            _ => 0
        };

        double nextIncome = bt switch
        {
            "factory" => FactoryIncome[next],
            "port" => PortIncome[next],
            "mine" => MineIncome[next],
            _ => 0
        };

        string buildingName = bt switch
        {
            "factory" => "🏭 کارخانه",
            "port" => "⚓ بندر",
            "mine" => "⛏️ معدن",
            _ => "ساختمان"
        };

        string incomeUnit = bt == "mine" ? "آهن" : "پول";

        bool usesRoyalCoins = bt == "mine" && next >= 6;

        string priceText;
        string balanceText;

        if (usesRoyalCoins)
        {
            int royalCost = MineRoyalCostForTargetLevel(next);
            long royalBalance = Database.GetRoyalCoins(uid);

            priceText = $"{royalCost:N0} رویال‌کوین 💎";
            balanceText = $"موجودی رویال: {royalBalance:N0}";
        }
        else
        {
            int costK = bt switch
            {
                "factory" => FactoryUpgradeCost[cur],
                "port" => PortUpgradeCost[cur],
                "mine" => MineUpgradeCost[cur],
                _ => 0
            };

            priceText = $"{costK:N0}K پول 💰";
            balanceText = $"پول: {(c.Money / 1000.0):F1}K";
        }

        string text =
            $"{buildingName}\n" +
            $"سطح فعلی: {cur}\n" +
            $"درآمد فعلی: {currentIncome:F1}K {incomeUnit}\n\n" +
            $"سطح بعدی: {next}\n" +
            $"درآمد بعدی: {nextIncome:F1}K {incomeUnit}\n" +
            $"هزینه ارتقا: {priceText}\n" +
            balanceText;

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    "✅ ارتقا",
                    $"upgrade:{uid}:{bt}"
                )
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    "❌ لغو",
                    $"cancel:{uid}"
                )
            }
        });

        await bot.EditMessageTextAsync(
            chatId,
            cb.Message.MessageId,
            text,
            replyMarkup: keyboard,
            cancellationToken: ct
        );

        await bot.AnswerCallbackQueryAsync(
            cb.Id,
            cancellationToken: ct
        );
    }

    static async Task HandleTimingCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length < 2 || cb.Message == null) return;
        long uid = cb.From.Id;
        if (parts[1] == "daily") { sessions[uid] = new UserSession { Step = SessionStep.OwnerWaitingDailyTime }; await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct); await SendPrompt(uid, cb.Message.Chat.Id, "⏰ ساعت HHMM:", ct: ct); return; }
        if (parts[1] == "minute") { sessions[uid] = new UserSession { Step = SessionStep.OwnerWaitingMinuteTime }; await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct); await SendPrompt(uid, cb.Message.Chat.Id, "⌛ هر چند دقیقه؟", ct: ct); return; }
        await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
    }

    static async Task HandleUpgradeCallback(
        CallbackQuery cb,
        string[] parts,
        CancellationToken ct)
    {
        if (parts.Length != 3 || cb.Message == null)
            return;

        long uid = cb.From.Id;
        string bt = parts[2];

        if (bt is not ("factory" or "port" or "mine"))
        {
            await bot.AnswerCallbackQueryAsync(
                cb.Id,
                "❌ ساختمان نامعتبر است.",
                showAlert: true,
                cancellationToken: ct
            );
            return;
        }

        long chatId = cb.Message.Chat.Id;
        var c = Database.GetCountry(uid, chatId);

        if (c == null)
        {
            await bot.AnswerCallbackQueryAsync(
                cb.Id,
                "❌ کشور یافت نشد!",
                cancellationToken: ct
            );
            return;
        }

        int cur = bt switch
        {
            "factory" => c.FactoryLevel,
            "port" => c.PortLevel,
            "mine" => c.MineLevel,
            _ => 1
        };

        int max = MaxBuildLevel(c, bt);

        if (cur >= max)
        {
            await bot.AnswerCallbackQueryAsync(
                cb.Id,
                c.Besieged >= 2
                    ? "🔒 به‌دلیل شرایط بحرانی امکان ارتقا وجود ندارد."
                    : "✅ ساختمان در حداکثر سطح است.",
                showAlert: true,
                cancellationToken: ct
            );
            return;
        }

        int newLevel = cur + 1;
        bool usesRoyalCoins = bt == "mine" && newLevel >= 6;

        long moneyCost = 0;
        int royalCost = 0;

        if (usesRoyalCoins)
        {
            royalCost = MineRoyalCostForTargetLevel(newLevel);
            long royalBalance = Database.GetRoyalCoins(uid);

            if (royalBalance < royalCost)
            {
                await bot.AnswerCallbackQueryAsync(
                    cb.Id,
                    $"💎 رویال‌کوین کافی نیست!\n" +
                    $"نیاز: {royalCost:N0}\n" +
                    $"موجودی: {royalBalance:N0}",
                    showAlert: true,
                    cancellationToken: ct
                );
                return;
            }

            if (!Database.TryUpgradeMineWithRoyal(uid, chatId, cur, newLevel, royalCost))
            {
                await bot.AnswerCallbackQueryAsync(cb.Id,
                    "❌ ارتقای معدن انجام نشد؛ سطح یا موجودی رویال تغییر کرده است.",
                    showAlert: true, cancellationToken: ct);
                return;
            }
        }
        else
        {
            int costK = bt switch
            {
                "factory" => FactoryUpgradeCost[cur],
                "port" => PortUpgradeCost[cur],
                "mine" => MineUpgradeCost[cur],
                _ => 0
            };

            moneyCost = costK * 1000L;

            if (c.Money < moneyCost)
            {
                await bot.AnswerCallbackQueryAsync(
                    cb.Id,
                    $"💰 پول کافی نیست!\n" +
                    $"نیاز: {costK:N0}K\n" +
                    $"موجودی: {(c.Money / 1000.0):F1}K",
                    showAlert: true,
                    cancellationToken: ct
                );
                return;
            }

            Database.UpdateBuildingLevel(
                uid,
                chatId,
                bt,
                newLevel,
                -moneyCost
            );
        }

        var updatedCountry = Database.GetCountry(uid, chatId);

        if (updatedCountry == null)
        {
            await bot.AnswerCallbackQueryAsync(
                cb.Id,
                "⚠️ ارتقا انجام شد، اما اطلاعات جدید دریافت نشد.",
                showAlert: true,
                cancellationToken: ct
            );
            return;
        }

        bool canUpgradeMore = newLevel < max;

        string buildingName = bt switch
        {
            "factory" => "کارخانه",
            "port" => "بندر",
            "mine" => "معدن",
            _ => "ساختمان"
        };

        string currentBalance = usesRoyalCoins
            ? $"💎 رویال باقی‌مانده: {Database.GetRoyalCoins(uid):N0}"
            : $"💰 پول باقی‌مانده: {(updatedCountry.Money / 1000.0):F1}K";

        string resultText =
            $"✅ {buildingName} به سطح {newLevel} ارتقا یافت.\n" +
            currentBalance;

        InlineKeyboardMarkup? keyboard = null;

        if (canUpgradeMore)
        {
            int followingLevel = newLevel + 1;
            bool nextUsesRoyal =
                bt == "mine" && followingLevel >= 6;

            string nextPrice;

            if (nextUsesRoyal)
            {
                int nextRoyalCost =
                    MineRoyalCostForTargetLevel(followingLevel);

                nextPrice =
                    $"{nextRoyalCost:N0} رویال‌کوین";
            }
            else
            {
                int nextCostK = bt switch
                {
                    "factory" => FactoryUpgradeCost[newLevel],
                    "port" => PortUpgradeCost[newLevel],
                    "mine" => MineUpgradeCost[newLevel],
                    _ => 0
                };

                nextPrice = $"{nextCostK:N0}K پول";
            }

            resultText +=
                $"\nارتقای بعدی: سطح {followingLevel}" +
                $" — {nextPrice}";

            keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        "⬆️ ارتقای بعدی",
                        $"upgrade:{uid}:{bt}"
                    )
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        "❌ بستن",
                        $"cancel:{uid}"
                    )
                }
            });
        }
        else
        {
            resultText += "\n🏁 حداکثر سطح";
        }

        await bot.EditMessageTextAsync(
            chatId,
            cb.Message.MessageId,
            resultText,
            replyMarkup: keyboard,
            cancellationToken: ct
        );

        await bot.AnswerCallbackQueryAsync(
            cb.Id,
            usesRoyalCoins
                ? $"✅ {royalCost:N0} رویال‌کوین کسر شد."
                : "✅ ارتقا انجام شد.",
            cancellationToken: ct
        );
    }

    static async Task HandleTankInfoCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length != 3 || cb.Message == null) return;
        long uid = cb.From.Id;
        string tid = parts[2];
        string info = tid switch
        {
            "M2Medium" => "🇺🇸 M2 Medium\n\n⚖️ ۱۸ تن | 🔫 ۳۷mm | 🛡 ۳۰mm | ⚡ ۴۲km/h\n💰 هر ۵ تانک: ۲K آهن + ۲K پول",
            "T28" => "🇷🇺 T-28\n\n⚖️ ۲۸ تن | 🔫 ۷۶mm | 🛡 ۸۰mm | ⚡ ۳۷km/h\n💰 هر ۵ تانک: ۳K آهن + ۳K پول",
            "PanzerIII" => "🇩🇪 Panzer III\n\n⚖️ ۲۳ تن | 🔫 ۵۰mm | 🛡 ۶۰mm | ⚡ ۴۰km/h\n💰 هر ۵ تانک: ۲.۵K آهن + ۲.۵K پول",
            _ => "تانک ناشناخته"
        };
        await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
        if (info == "تانک ناشناخته") { await SendTemp(cb.Message.Chat.Id, info, ct: ct); return; }
        var kb = new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("1", $"tank_buy:{uid}:{tid}:1"), InlineKeyboardButton.WithCallbackData("5", $"tank_buy:{uid}:{tid}:5") }, new[] { InlineKeyboardButton.WithCallbackData("10", $"tank_buy:{uid}:{tid}:10"), InlineKeyboardButton.WithCallbackData("25", $"tank_buy:{uid}:{tid}:25") }, new[] { InlineKeyboardButton.WithCallbackData("❌ انصراف", $"cancel:{uid}") } });
        await SendTemp(cb.Message.Chat.Id, info, markup: kb, ct: ct);
    }

    static async Task HandleTankBuyCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length != 4 || cb.Message == null) return;
        long uid = cb.From.Id;
        string tid = parts[2];
        if (!TryParseInt(parts[3], out int cnt) || cnt <= 0) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ تعداد نامعتبر", cancellationToken: ct); return; }
        long cid = cb.Message.Chat.Id;
        var c = Database.GetCountry(uid, cid);
        if (c == null) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ کشور یافت نشد!", cancellationToken: ct); return; }
        double i5 = tid switch { "M2Medium" => 2000, "T28" => 3000, "PanzerIII" => 2500, _ => 0 };
        double m5 = tid switch { "M2Medium" => 2000, "T28" => 3000, "PanzerIII" => 2500, _ => 0 };
        long ti = (long)Math.Ceiling(cnt / 5.0 * i5);
        long tm = (long)Math.Ceiling(cnt / 5.0 * m5);
        if (c.Iron < ti) { await bot.AnswerCallbackQueryAsync(cb.Id, $"❌ آهن: نیاز {ti / 1000.0:F1}K", cancellationToken: ct); return; }
        if (c.Money < tm) { await bot.AnswerCallbackQueryAsync(cb.Id, $"❌ پول: نیاز {tm / 1000.0:F1}K", cancellationToken: ct); return; }
        c.Iron -= ti; c.Tanks += cnt; c.Money -= tm;
        Database.UpdateCountryResources(uid, cid, c.Money, c.Iron, c.Tanks);
        string tn = tid switch { "M2Medium" => "M2 Medium", "T28" => "T-28", "PanzerIII" => "Panzer III", _ => tid };
        await SendTemp(cb.Message.Chat.Id, $"✅ {cnt} تانک {tn} خریداری شد!\n💰 پول: {(c.Money / 1000.0):F1}K\n🔩 آهن: {(c.Iron / 1000.0):F1}K", ct: ct);
        await bot.AnswerCallbackQueryAsync(cb.Id, "✅ خرید موفق", cancellationToken: ct);
    }

    static async Task HandlePlaneInfoCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length != 3 || cb.Message == null) return;
        long uid = cb.From.Id;
        string pid = parts[2];
        string info = pid switch
        {
            "Bf109" => "🇩🇪 Bf 109\n⚡ ۵۷۰km/h | 🎯 مانور ۸/۱۰\n💰 هر ۵: ۲K آهن + ۵K پول",
            "P36" => "🇺🇸 P-36\n⚡ ۵۰۰km/h | 🎯 مانور ۹/۱۰\n💰 هر ۵: ۱.۵K آهن + ۴K پول",
            "I16" => "🇷🇺 I-16\n⚡ ۵۲۰km/h | 🎯 مانور ۹/۱۰\n💰 هر ۵: ۱K آهن + ۳.۵K پول",
            _ => "ناشناخته"
        };
        await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
        var kb = new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("1", $"plane_buy:{uid}:{pid}:1"), InlineKeyboardButton.WithCallbackData("5", $"plane_buy:{uid}:{pid}:5") }, new[] { InlineKeyboardButton.WithCallbackData("10", $"plane_buy:{uid}:{pid}:10"), InlineKeyboardButton.WithCallbackData("25", $"plane_buy:{uid}:{pid}:25") }, new[] { InlineKeyboardButton.WithCallbackData("❌ انصراف", $"cancel:{uid}") } });
        await SendTemp(cb.Message.Chat.Id, info, markup: kb, ct: ct);
    }

    static async Task HandlePlaneBuyCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length != 4 || cb.Message == null) return;
        long uid = cb.From.Id;
        string pid = parts[2];
        if (!TryParseInt(parts[3], out int cnt) || cnt <= 0) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ تعداد", cancellationToken: ct); return; }
        long cid = cb.Message.Chat.Id;
        var c = Database.GetCountry(uid, cid);
        if (c == null) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ کشور", cancellationToken: ct); return; }
        double i5 = pid switch { "I16" => 1000, "P36" => 1500, "Bf109" => 2000, _ => 0 };
        double m5 = pid switch { "I16" => 3500, "P36" => 4000, "Bf109" => 5000, _ => 0 };
        long ti = (long)Math.Ceiling(cnt / 5.0 * i5);
        long tm = (long)Math.Ceiling(cnt / 5.0 * m5);
        if (c.Iron < ti) { await bot.AnswerCallbackQueryAsync(cb.Id, $"❌ آهن", cancellationToken: ct); return; }
        if (c.Money < tm) { await bot.AnswerCallbackQueryAsync(cb.Id, $"❌ پول", cancellationToken: ct); return; }
        c.Iron -= ti; c.Planes += cnt; c.Money -= tm;
        Database.UpdatePlanesResources(uid, cid, c.Money, c.Iron, c.Planes);
        string pn = pid switch { "I16" => "I-16", "P36" => "P-36", "Bf109" => "Bf 109", _ => pid };
        await SendTemp(cb.Message.Chat.Id, $"✅ {cnt} {pn} خریداری شد!", ct: ct);
        await bot.AnswerCallbackQueryAsync(cb.Id, "✅", cancellationToken: ct);
    }

    static async Task HandleBomberInfoCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length != 3 || cb.Message == null) return;
        long uid = cb.From.Id;
        string bid = parts[2];
        string info = bid switch
        {
            "B17" => "🇺🇸 B-17\n⚡ ۴۶۰km/h | 🛡 ۸/۱۰ | 💣 ۳۶۰۰kg\n💰 هر ۱: ۳K آهن + ۵K پول",
            "He111" => "🇩🇪 He 111\n⚡ ۴۳۵km/h | 🛡 ۵/۱۰ | 💣 ۲۰۰۰kg\n💰 هر ۱: ۲K آهن + ۴K پول",
            "DB3" => "🇷🇺 DB-3\n⚡ ۴۳۰km/h | 🛡 ۳/۱۰ | 💣 ۱۰۰۰kg\n💰 هر ۱: ۱K آهن + ۳K پول",
            _ => "ناشناخته"
        };
        await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
        var kb = new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("1", $"bomber_buy:{uid}:{bid}:1"), InlineKeyboardButton.WithCallbackData("2", $"bomber_buy:{uid}:{bid}:2") }, new[] { InlineKeyboardButton.WithCallbackData("5", $"bomber_buy:{uid}:{bid}:5"), InlineKeyboardButton.WithCallbackData("10", $"bomber_buy:{uid}:{bid}:10") }, new[] { InlineKeyboardButton.WithCallbackData("❌ انصراف", $"cancel:{uid}") } });
        await SendTemp(cb.Message.Chat.Id, info, markup: kb, ct: ct);
    }

    static async Task HandleBomberBuyCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length != 4 || cb.Message == null) return;
        long uid = cb.From.Id;
        string bid = parts[2];
        if (!TryParseInt(parts[3], out int cnt) || cnt <= 0) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ تعداد", cancellationToken: ct); return; }
        long cid = cb.Message.Chat.Id;
        var c = Database.GetCountry(uid, cid);
        if (c == null) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ کشور", cancellationToken: ct); return; }
        double i1 = bid switch { "DB3" => 1000, "He111" => 2000, "B17" => 3000, _ => 0 };
        double m1 = bid switch { "DB3" => 3000, "He111" => 4000, "B17" => 5000, _ => 0 };
        long ti = (long)(cnt * i1);
        long tm = (long)(cnt * m1);
        if (c.Iron < ti) { await bot.AnswerCallbackQueryAsync(cb.Id, $"❌ آهن", cancellationToken: ct); return; }
        if (c.Money < tm) { await bot.AnswerCallbackQueryAsync(cb.Id, $"❌ پول", cancellationToken: ct); return; }
        c.Iron -= ti; c.Bombers += cnt; c.Money -= tm;
        Database.UpdateBombersResources(uid, cid, c.Money, c.Iron, c.Bombers);
        string bn = bid switch { "DB3" => "DB-3", "He111" => "He 111", "B17" => "B-17", _ => bid };
        await SendTemp(cb.Message.Chat.Id, $"✅ {cnt} {bn} خریداری شد!", ct: ct);
        await bot.AnswerCallbackQueryAsync(cb.Id, "✅", cancellationToken: ct);
    }

    static async Task HandleAntiAirInfoCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length != 3 || cb.Message == null) return;
        long uid = cb.From.Id;
        string info = "🎯 توپ ۷۶mm\n💰 هر ۵: ۲K آهن + ۴K پول";
        await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
        // FIX(1): همهٔ callbackها aa_buy (قبلاً یکی اشتباه aabuy بود)
        var kb = new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("1", $"aa_buy:{uid}:AA76:1"), InlineKeyboardButton.WithCallbackData("5", $"aa_buy:{uid}:AA76:5") }, new[] { InlineKeyboardButton.WithCallbackData("10", $"aa_buy:{uid}:AA76:10"), InlineKeyboardButton.WithCallbackData("25", $"aa_buy:{uid}:AA76:25") }, new[] { InlineKeyboardButton.WithCallbackData("❌ انصراف", $"cancel:{uid}") } });
        await SendTemp(cb.Message.Chat.Id, info, markup: kb, ct: ct);
    }

    static async Task HandleAntiAirBuyCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length != 4 || cb.Message == null) return;
        long uid = cb.From.Id;
        if (!TryParseInt(parts[3], out int cnt) || cnt <= 0) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ تعداد", cancellationToken: ct); return; }
        long cid = cb.Message.Chat.Id;
        var c = Database.GetCountry(uid, cid);
        if (c == null) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ کشور", cancellationToken: ct); return; }
        long ti = (long)Math.Ceiling(cnt / 5.0 * 2000);
        long tm = (long)Math.Ceiling(cnt / 5.0 * 4000);
        if (c.Iron < ti) { await bot.AnswerCallbackQueryAsync(cb.Id, $"❌ آهن", cancellationToken: ct); return; }
        if (c.Money < tm) { await bot.AnswerCallbackQueryAsync(cb.Id, $"❌ پول", cancellationToken: ct); return; }
        c.Iron -= ti; c.AntiAir += cnt; c.Money -= tm;
        Database.UpdateAntiAirResources(uid, cid, c.Money, c.Iron, c.AntiAir);
        await SendTemp(cb.Message.Chat.Id, $"✅ {cnt} پدافند خریداری شد!", ct: ct);
        await bot.AnswerCallbackQueryAsync(cb.Id, "✅", cancellationToken: ct);
    }

    // ================= NAVAL SHOP –  =================
    static async Task HandleBoatInfoCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length != 3 || cb.Message == null) return;
        long uid = cb.From.Id;
        string bid = parts[2];
        string info = bid switch
        {
            "SBoot" => "🇩🇪 S-Boot (E-Boat)\n⚡ سرعت: 38–41 گره (70–76 km/h)\n🛡 زره: تقریباً هیچ (بدنه فولادی سبک)\n👥 خدمه: 21–24 نفر\n🔫 تسلیحات: 2x لوله اژدر 533mm، 1x توپ 20mm، چند مسلسل 7.92mm\n💰 هر 5 عدد: 2K پول + 1K آهن",
            "PTBoat" => "🇺🇸 PT Boat\n⚡ سرعت: 40–45 گره (74–83 km/h)\n🛡 زره: هیچ\n👥 خدمه: 10–14 نفر\n🔫 تسلیحات: 2–4 اژدر، مسلسل 12.7mm، گاهی توپ 20mm\n💰 هر 5 عدد: 3K پول + 1.5K آهن",
            "G5" => "🇷🇺 G-5\n⚡ سرعت: 50–53 گره (93–98 km/h)\n🛡 زره: هیچ\n👥 خدمه: 6 نفر\n🔫 تسلیحات: 2x اژدر 533mm، 2x مسلسل 7.62mm\n💰 هر 5 عدد: 2.5K پول + 1.5K آهن",
            _ => "قایق ناشناخته"
        };
        await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
        var kb = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("5", $"boat_buy:{uid}:{bid}:5"), InlineKeyboardButton.WithCallbackData("10", $"boat_buy:{uid}:{bid}:10") },
            new[] { InlineKeyboardButton.WithCallbackData("25", $"boat_buy:{uid}:{bid}:25"), InlineKeyboardButton.WithCallbackData("❌ انصراف", $"cancel:{uid}") }
        });
        await SendTemp(cb.Message.Chat.Id, info, markup: kb, ct: ct);
    }

    static async Task HandleBoatBuyCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length != 4 || cb.Message == null) return;
        long uid = cb.From.Id;
        string bid = parts[2];
        if (!TryParseInt(parts[3], out int cnt) || cnt <= 0) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ تعداد", cancellationToken: ct); return; }
        long cid = cb.Message.Chat.Id;
        var c = Database.GetCountry(uid, cid);
        if (c == null) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ کشور", cancellationToken: ct); return; }

        // Price per 5
        double moneyPer5 = bid switch { "SBoot" => 2000, "PTBoat" => 3000, "G5" => 2500, _ => 0 };
        double ironPer5 = bid switch { "SBoot" => 1000, "PTBoat" => 1500, "G5" => 1500, _ => 0 };
        long tm = (long)Math.Ceiling(cnt / 5.0 * moneyPer5);
        long ti = (long)Math.Ceiling(cnt / 5.0 * ironPer5);

        if (c.Money < tm) { await bot.AnswerCallbackQueryAsync(cb.Id, $"❌ پول کم: نیاز {tm}", cancellationToken: ct); return; }
        if (c.Iron < ti) { await bot.AnswerCallbackQueryAsync(cb.Id, $"❌ آهن کم: نیاز {ti}", cancellationToken: ct); return; }

        c.Money -= tm; c.Iron -= ti; c.Boats += cnt;
        Database.UpdateCountryFull(c);
        string modelName = bid switch { "SBoot" => "S-Boot", "PTBoat" => "PT Boat", "G5" => "G-5", _ => bid };
        Database.AddEquipmentModel(uid, cid, "Boats", modelName, cnt);

        await SendTemp(cb.Message.Chat.Id, $"✅ {cnt} قایق {modelName} خریداری شد!\n💰 باقی‌مانده: {(c.Money / 1000.0):F1}K | 🔩 آهن: {(c.Iron / 1000.0):F1}K", ct: ct);
        await bot.AnswerCallbackQueryAsync(cb.Id, "✅ خرید موفق", cancellationToken: ct);
    }

    static async Task HandleSubInfoCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length != 3 || cb.Message == null) return;
        long uid = cb.From.Id;
        string sid = parts[2];
        string info = sid switch
        {
            "VIIC" => "🇩🇪 Type VIIC U-boat\n⚡ سرعت: 17.7 گره روی آب / 7.6 گره زیر آب\n🛡 زره: ندارد (بدنه فشاری 18–22mm فولاد)\n👥 خدمه: 44–52 نفر\n🔫 تسلیحات: 5x لوله اژدر 533mm، 11–14 اژدر، 1x توپ 88mm، 1x توپ 20mm ضدهوایی\n💰 هر 1 عدد: 10K پول + 5K آهن",
            "Gato" => "🇺🇸 Gato\n⚡ سرعت: 21 گره روی آب / 9 گره زیر آب\n🛡 زره: ندارد (بدنه فشاری فولادی)\n👥 خدمه: 55–60 نفر\n🔫 تسلیحات: 8x لوله اژدر 533mm، 24 اژدر، 1x توپ 76mm، مسلسل ضدهوایی\n💰 هر 1 عدد: 10K پول + 5K آهن",
            "SClass" => "🇷🇺 S-class, Series IX\n⚡ سرعت: 13–14 گره روی آب / 7–8 گره زیر آب\n🛡 زره: ندارد (بدنه فشاری فولادی)\n👥 خدمه: 37–44 نفر\n🔫 تسلیحات: 6x لوله اژدر 533mm، 10 اژدر، 1x توپ 45mm، مسلسل ضدهوایی\n💰 هر 1 عدد: 8K پول + 4K آهن",
            _ => "زیردریایی ناشناخته"
        };
        await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
        var kb = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("1", $"sub_buy:{uid}:{sid}:1"), InlineKeyboardButton.WithCallbackData("2", $"sub_buy:{uid}:{sid}:2") },
            new[] { InlineKeyboardButton.WithCallbackData("5", $"sub_buy:{uid}:{sid}:5"), InlineKeyboardButton.WithCallbackData("❌ انصراف", $"cancel:{uid}") }
        });
        await SendTemp(cb.Message.Chat.Id, info, markup: kb, ct: ct);
    }

    static async Task HandleSubBuyCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length != 4 || cb.Message == null) return;
        long uid = cb.From.Id;
        string sid = parts[2];
        if (!TryParseInt(parts[3], out int cnt) || cnt <= 0) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ تعداد", cancellationToken: ct); return; }
        long cid = cb.Message.Chat.Id;
        var c = Database.GetCountry(uid, cid);
        if (c == null) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ کشور", cancellationToken: ct); return; }

        double moneyPer1 = sid switch { "VIIC" => 10000, "Gato" => 10000, "SClass" => 8000, _ => 0 };
        double ironPer1 = sid switch { "VIIC" => 5000, "Gato" => 5000, "SClass" => 4000, _ => 0 };
        long tm = (long)(cnt * moneyPer1);
        long ti = (long)(cnt * ironPer1);

        if (c.Money < tm) { await bot.AnswerCallbackQueryAsync(cb.Id, $"❌ پول کم", cancellationToken: ct); return; }
        if (c.Iron < ti) { await bot.AnswerCallbackQueryAsync(cb.Id, $"❌ آهن کم", cancellationToken: ct); return; }

        c.Money -= tm; c.Iron -= ti; c.Submarines += cnt;
        Database.UpdateCountryFull(c);
        string modelName = sid switch { "VIIC" => "Type VIIC", "Gato" => "Gato", "SClass" => "S-class", _ => sid };
        Database.AddEquipmentModel(uid, cid, "Submarines", modelName, cnt);

        await SendTemp(cb.Message.Chat.Id, $"✅ {cnt} زیردریایی {modelName} خریداری شد!", ct: ct);
        await bot.AnswerCallbackQueryAsync(cb.Id, "✅", cancellationToken: ct);
    }

    static string FormatBattleshipTechnicalSpec(string modelKey)
    {
        string model=modelKey=="Soyuz"?"Sovetsky Soyuz":modelKey;
        var s=WarEngineV2Core.GetBattleshipSpecByModel(model);
        var weapons=new List<string>{$"{s.MainGuns:0} × توپ {s.MainCaliber:0.#} میلی‌متری",$"{s.SecGuns:0} × توپ {s.SecondaryCaliber:0.#} میلی‌متری"};
        if(s.HeavyAACount>0)weapons.Add($"{s.HeavyAACount:0} × توپ {s.HeavyAACaliber:0.#} میلی‌متری ضدهوایی");
        if(s.MediumAACount>0)weapons.Add($"{s.MediumAACount:0} × توپ {s.MediumAACaliber:0.#} میلی‌متری ضدهوایی");
        if(s.LightAACount>0)weapons.Add($"{s.LightAACount:0} × توپ {s.LightAACaliber:0.#} میلی‌متری ضدهوایی");
        if(s.MachineGunCount>0)weapons.Add($"{s.MachineGunCount:0} × مسلسل {s.MachineGunCaliber:0.#} میلی‌متری");
        weapons.Add(s.ReconAircraft>0?$"{s.ReconAircraft:0} × هواپیمای شناسایی":"بدون هواپیمای شناسایی");
        return $"🚢 {s.Name}\nسرعت: {s.Speed:0.#} گره ({s.SpeedKph:0.#} کیلومتر بر ساعت)\n"+
               $"خدمه: {s.Crew:N0} نفر\n\n🛡 زره\nکمربند اصلی: {s.Belt:0}mm\nعرشه: {s.DeckMin:0}-{s.DeckMax:0}mm\n"+
               $"برجک‌ها: {s.Turret:0}mm\nبرج فرماندهی: {s.CommandArmor:0}mm\n\n🔫 تسلیحات\n• {string.Join("\n• ",weapons)}";
    }

    static async Task HandleBattleshipInfoCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length != 3 || cb.Message == null) return;
        long uid = cb.From.Id;
        string bid = parts[2];
        string price=bid switch{"Bismarck"=>"50K پول + 30K آهن","Iowa"=>"50K پول + 40K آهن","Soyuz"=>"45K پول + 25K آهن",_=>"نامشخص"};
        string info=FormatBattleshipTechnicalSpec(bid)+$"\n\n💰 هزینه ساخت: {price}\n⚠️ نیازمند بندر سطح ۴؛ سقف مالکیت، مأموریت و محموله‌های درراه روی‌هم ۳ فروند";
        await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
        var kb = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("1", $"battleship_buy:{uid}:{bid}:1") },
            new[] { InlineKeyboardButton.WithCallbackData("❌ انصراف", $"cancel:{uid}") }
        });
        await SendTemp(cb.Message.Chat.Id, info, markup: kb, ct: ct);
    }

    static async Task HandleBattleshipBuyCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length != 4 || cb.Message == null) return;
        long uid = cb.From.Id;
        string bid = parts[2];
        if (!TryParseInt(parts[3], out int cnt) || cnt != 1) cnt = 1; // battleship only 1 at a time
        long cid = cb.Message.Chat.Id;
        var c = Database.GetCountry(uid, cid);
        if (c == null) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ کشور", cancellationToken: ct); return; }

        if (c.PortLevel < 4)
        {
            await bot.AnswerCallbackQueryAsync(cb.Id, "⚓ برای ساخت نبردناو بندر سطح ۴ لازم است", showAlert: true, cancellationToken: ct);
            return;
        }
        long battleshipCapacityUsed = Database.GetBattleshipCapacityUsed(uid, cid);
        if (battleshipCapacityUsed >= 3)
        {
            await bot.AnswerCallbackQueryAsync(cb.Id, $"❌ حداکثر 3 نبردناو می‌توانید داشته باشید؛ ظرفیت فعلی/درراه: {battleshipCapacityUsed}/3", showAlert: true, cancellationToken: ct);
            return;
        }

        double moneyPer1 = bid switch { "Bismarck" => 50000, "Iowa" => 50000, "Soyuz" => 45000, _ => 0 };
        double ironPer1 = bid switch { "Bismarck" => 30000, "Iowa" => 40000, "Soyuz" => 25000, _ => 0 };
        long tm = (long)moneyPer1;
        long ti = (long)ironPer1;

        string modelName = bid switch { "Bismarck" => "Bismarck", "Iowa" => "Iowa", "Soyuz" => "Sovetsky Soyuz", _ => bid };
        // Group callbacks already hold this country's mutation lock in HandleUpdateAsync.
        // Acquiring it again here deadlocked because SemaphoreSlim is not re-entrant.
        bool purchased = Database.TryPurchaseBattleship(uid, cid, modelName, tm, ti);
        if (!purchased)
        {
            await bot.AnswerCallbackQueryAsync(cb.Id,
                "❌ خرید انجام نشد؛ پول/آهن، سطح بندر یا ظرفیت ۳ نبردناو (شامل ناوهای در دریا و درراه) را بررسی کنید.",
                showAlert: true, cancellationToken: ct);
            return;
        }
        long totalNow = Database.GetBattleshipCapacityUsed(uid, cid);
        await SendTemp(cb.Message.Chat.Id, $"✅ 1 نبردناو {modelName} خریداری شد! (ظرفیت: {totalNow}/3)", ct: ct);
        await bot.AnswerCallbackQueryAsync(cb.Id, "✅", cancellationToken: ct);
    }

    static async Task HandleBattleshipRepairCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length < 2 || cb.Message == null) return;
        long uid = cb.From.Id;
        long cid = cb.Message.Chat.Id;
        var c = Database.GetCountry(uid, cid);
        if (c == null) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ کشور یافت نشد", showAlert: true, cancellationToken: ct); return; }
        Database.SyncBattleshipUnits(uid, cid);
        var damaged = Database.GetBattleshipUnits(uid, cid, onlyCombatReady: false)
            .Where(x => x.DamagePercent > 0).ToList();
        if (damaged.Count == 0) { await bot.AnswerCallbackQueryAsync(cb.Id, "✅ آسیبی نیست", showAlert: true, cancellationToken: ct); return; }
        var rows = damaged.Select(x => new[]
        {
            InlineKeyboardButton.WithCallbackData($"🔧 {x.Model} شماره {x.ShipNumber} — آسیب {x.DamagePercent}٪",
                $"battleship_repair_quote:{x.UnitId}")
        }).ToList();
        rows.Add(new[] { InlineKeyboardButton.WithCallbackData("❌ لغو", $"cancel:{uid}") });
        await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
        await SendTemp(cb.Message.Chat.Id, "🔧 نبردناو موردنظر برای تعمیر فوری را انتخاب کنید.\nهزینه دقیقاً متناسب با درصد آسیب و قیمت همان مدل است.",
            markup: new InlineKeyboardMarkup(rows), ct: ct);
    }

    static async Task HandleBattleshipRepairQuoteCallback(CallbackQuery cb,string[] parts,CancellationToken ct)
    {
        if(parts.Length<2||cb.Message==null||!TryParseLong(parts[1],out long unitId))return;
        long uid=cb.From.Id,cid=cb.Message.Chat.Id;
        if(!Database.GetBattleshipRepairQuote(unitId,uid,cid,out string model,out int damage,out long money,out long iron))
        {await bot.AnswerCallbackQueryAsync(cb.Id,"❌ ناو قابل تعمیر نیست.",showAlert:true,cancellationToken:ct);return;}
        int shipNumber=Database.GetBattleshipUnits(uid,cid,false).FirstOrDefault(x=>x.UnitId==unitId)?.ShipNumber??0;
        var kb=new InlineKeyboardMarkup(new[]{
            new[]{InlineKeyboardButton.WithCallbackData($"✅ تعمیر فوری — {money:N0} پول + {iron:N0} آهن",$"battleship_repair_unit:{unitId}")},
            new[]{InlineKeyboardButton.WithCallbackData("❌ لغو","cancel")}});
        await bot.AnswerCallbackQueryAsync(cb.Id,cancellationToken:ct);
        await SendTemp(cid,$"🔧 تعمیر {model} شماره {shipNumber}\n💥 آسیب: {damage}٪\n💰 هزینه: {money:N0} پول\n🔩 هزینه: {iron:N0} آهن",markup:kb,ct:ct);
    }

    static async Task HandleBattleshipRepairUnitCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length < 2 || cb.Message == null || !TryParseLong(parts[1], out long unitId)) return;
        long uid = cb.From.Id, cid = cb.Message.Chat.Id;
        int shipNumber=Database.GetBattleshipUnits(uid,cid,false).FirstOrDefault(x=>x.UnitId==unitId)?.ShipNumber??0;
        bool repaired = Database.RepairBattleshipUnit(unitId, uid, cid, out long money, out long iron);
        if (!repaired)
        {
            await bot.AnswerCallbackQueryAsync(cb.Id, "❌ منابع کافی نیست، ناو در مأموریت است یا قبلاً تعمیر شده.",
                showAlert: true, cancellationToken: ct);
            return;
        }
        await bot.AnswerCallbackQueryAsync(cb.Id, "✅ تعمیر کامل شد.", cancellationToken: ct);
        await SendTemp(cid, $"✅ نبردناو شماره {shipNumber} فوراً تعمیر شد.\n💰 {money:N0} پول\n🔩 {iron:N0} آهن", ct: ct);
        DeleteNow(cb.Message.Chat.Id, cb.Message.MessageId);
    }

    static async Task HandleBattleshipScrapMenuCallback(CallbackQuery cb,CancellationToken ct)
    {
        if(cb.Message==null)return;long uid=cb.From.Id,cid=cb.Message.Chat.Id;
        var country=Database.GetCountry(uid,cid);if(country==null){await bot.AnswerCallbackQueryAsync(cb.Id,"❌ کشور یافت نشد.",showAlert:true,cancellationToken:ct);return;}
        Database.SyncBattleshipUnits(uid,cid);
        var ships=Database.GetBattleshipUnits(uid,cid,onlyCombatReady:false);
        if(ships.Count==0){await bot.AnswerCallbackQueryAsync(cb.Id,"❌ نبردناو آماده‌ای برای اوراق ندارید.",showAlert:true,cancellationToken:ct);return;}
        var rows=ships.Select(x=>new[]{InlineKeyboardButton.WithCallbackData($"♻️ {x.Model} شماره {x.ShipNumber} — آسیب {x.DamagePercent}٪",$"battleship_scrap:{x.UnitId}")}).ToList();
        rows.Add(new[]{InlineKeyboardButton.WithCallbackData("❌ لغو","cancel")});
        await bot.AnswerCallbackQueryAsync(cb.Id,cancellationToken:ct);
        await SendTemp(cid,"♻️ نبردناو موردنظر را انتخاب کنید. ۵۰٪ قیمت ساخت پول و آهن برمی‌گردد.",markup:new InlineKeyboardMarkup(rows),ct:ct);
    }

    static async Task HandleBattleshipScrapQuoteCallback(CallbackQuery cb,string[] parts,CancellationToken ct)
    {
        if(parts.Length<2||cb.Message==null||!TryParseLong(parts[1],out long unitId))return;
        long uid=cb.From.Id,cid=cb.Message.Chat.Id;
        if(!Database.GetBattleshipScrapQuote(unitId,uid,cid,out string model,out int damage,out long money,out long iron))
        {await bot.AnswerCallbackQueryAsync(cb.Id,"❌ این نبردناو قابل اوراق نیست یا در مأموریت/انتقال است.",showAlert:true,cancellationToken:ct);return;}
        int shipNumber=Database.GetBattleshipUnits(uid,cid,false).FirstOrDefault(x=>x.UnitId==unitId)?.ShipNumber??0;
        var kb=new InlineKeyboardMarkup(new[]{
            new[]{InlineKeyboardButton.WithCallbackData($"✅ اوراق — دریافت {money:N0} پول + {iron:N0} آهن",$"battleship_scrap_confirm:{unitId}")},
            new[]{InlineKeyboardButton.WithCallbackData("❌ لغو","cancel")}});
        await bot.AnswerCallbackQueryAsync(cb.Id,cancellationToken:ct);
        await SendTemp(cid,$"♻️ اوراق {model} شماره {shipNumber}\n💥 آسیب فعلی: {damage}٪\n💰 بازگشت پول: {money:N0}\n🔩 بازگشت آهن: {iron:N0}\n⚠️ این عملیات غیرقابل بازگشت است.",markup:kb,ct:ct);
    }

    static async Task HandleBattleshipScrapConfirmCallback(CallbackQuery cb,string[] parts,CancellationToken ct)
    {
        if(parts.Length<2||cb.Message==null||!TryParseLong(parts[1],out long unitId))return;
        long uid=cb.From.Id,cid=cb.Message.Chat.Id;
        int shipNumber=Database.GetBattleshipUnits(uid,cid,false).FirstOrDefault(x=>x.UnitId==unitId)?.ShipNumber??0;
        // The outer group callback already owns the country mutation lock. Do not re-enter it.
        bool scrapped=Database.ScrapBattleshipUnit(unitId,uid,cid,out string model,out long money,out long iron);
        if(!scrapped)
        {await bot.AnswerCallbackQueryAsync(cb.Id,"❌ اوراق انجام نشد؛ وضعیت ناو یا موجودی تغییر کرده است.",showAlert:true,cancellationToken:ct);return;}
        await bot.AnswerCallbackQueryAsync(cb.Id,"✅ نبردناو اوراق شد.",cancellationToken:ct);
        await SendTemp(cid,$"✅ {model} شماره {shipNumber} اوراق شد.\n💰 {money:N0} پول\n🔩 {iron:N0} آهن بازگردانده شد.",ct:ct);
        DeleteNow(cb.Message.Chat.Id,cb.Message.MessageId);
    }

    // ============================================================
    //  تایمر آپدیت دارایی — نسخه اصلاح‌شده (FIXED)
    // ============================================================
    static void StartAssetUpdateTimer()
    {
        try
        {
            assetUpdateTimer?.Dispose();
            assetUpdateTimer = null;
            if (UpdateMode == "minute")
            {
                long msLong = (long)UpdateValue * 60L * 1000L;
                if (msLong < 1000) msLong = 1000;
                var due = TimeSpan.FromMilliseconds(msLong);
                assetUpdateTimer = new Timer(async _ =>
                {
                    try { await RunAssetUpdate(); }
                    catch (Exception ex) { Console.WriteLine($"[TIMER RUN ERR] {ex.Message}"); }
                }, null, due, due);
                Console.WriteLine($"[TIMER] minute mode: every {UpdateValue} min");
            }
            else
            {
                var now = GetTehranNow();
                var target = new DateTime(now.Year, now.Month, now.Day, UpdateValue / 60, UpdateValue % 60, 0);
                if (target <= now) target = target.AddDays(1);
                TimeSpan delay = target - now;
                if (delay < TimeSpan.Zero) delay = TimeSpan.FromMinutes(1);
                if (delay > TimeSpan.FromDays(2)) delay = TimeSpan.FromDays(2);
                assetUpdateTimer = new Timer(async _ =>
                {
                    try { await RunAssetUpdate(); }
                    catch (Exception ex) { Console.WriteLine($"[TIMER RUN ERR] {ex.Message}"); }
                    try { StartAssetUpdateTimer(); }
                    catch (Exception ex) { Console.WriteLine($"[TIMER RESCHEDULE ERR] {ex.Message}"); }
                }, null, delay, Timeout.InfiniteTimeSpan);
                Console.WriteLine($"[TIMER] daily mode: next run in {delay.TotalMinutes:F1} min (Tehran target {target:yyyy-MM-dd HH:mm})");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TIMER SETUP ERR] {ex.Message} — retry in 60s");
            try
            {
                assetUpdateTimer?.Dispose();
                assetUpdateTimer = new Timer(_ =>
                {
                    try { StartAssetUpdateTimer(); } catch { }
                }, null, TimeSpan.FromMinutes(1), Timeout.InfiniteTimeSpan);
            }
            catch { }
        }
    }

        static void StartTransferTimer()
    {
        try
        {
            transferTimer?.Dispose();
            transferTimer = null;
            transferTimer = new Timer(async _ =>
            {
                // Naval arrivals are time-sensitive (full exemption = one minute), so they
                // must not wait behind a long transfer/deployment batch.
                try { await ProcessNavalInvasions(CancellationToken.None); }
                catch (Exception ex) { Console.WriteLine($"[NAVAL TIMER ERR] {ex}"); }
                try { await ProcessActiveTransfers(CancellationToken.None); }
                catch (Exception ex) { Console.WriteLine($"[TRANSFER TIMER ERR] {ex}"); }
                try { await ProcessActiveDeployments(CancellationToken.None); }
                catch (Exception ex) { Console.WriteLine($"[DEPLOY TIMER ERR] {ex}"); }
            }, null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30));
            Console.WriteLine("[TIMER] naval-first operations timer started (every 30s)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TRANSFER TIMER SETUP ERR] {ex.Message}");
        }
    }

    static double GetPopulationFactor(long population) => 1.0;
    internal static bool HasDefaultCitySiege(Country c) => c.Cities<4&&c.Besieged>0;
    static int MaxBuildLevel(Country c, string buildingType) => HasDefaultCitySiege(c)&&c.Besieged>=2 ? 3 : buildingType == "mine" ? 7 : 5;
    static double SiegeIncomeFactor(Country c) => HasDefaultCitySiege(c)&&c.Besieged>=2 ? 0.5 : 1.0;

    static long CalcTaxIncome(Country c)
    {
        double income = c.Population * (c.TaxRate / 100.0) * 0.3;
        return (long)Math.Max(0, income);
    }

    static double WelfareTarget(Country c)
    {
        double portBoost = c.PortLevel > 0 ? 5.0 : 0.0;
        double target = 100.0 - c.TaxRate - (c.RecruitmentRate * 3.0) + portBoost;
        return Math.Clamp(target, 0.0, 100.0);
    }

    static double NextWelfare(Country c)
    {
        double target = WelfareTarget(c);
        double next = c.Welfare + (target - c.Welfare) * 0.5;
        return Math.Clamp(next, 0.0, 100.0);
    }

    static long CalcBuildingMoney(Country c)
    {
        return (long)((FactoryIncome[c.FactoryLevel] + PortIncome[c.PortLevel]) * 1000);
    }

    static long CalcIronIncome(Country c)
    {
        return (long)(MineIncome[c.MineLevel] * 1000);
    }

    internal static bool PassesAttackTypePowerRule(Country attacker,Country defender,bool isNaval) =>
        !isNaval || CalcManpower(defender) >= CalcManpower(attacker) / 4;

    static long CalcManpower(Country c)
    {
        double popPower = (c.Population / 1000.0) * (c.Welfare / 100.0);
        double nonTaxIncome = CalcBuildingMoney(c) + CalcIronIncome(c);
        double incomePower = nonTaxIncome / 20.0;
        double groundPower = (c.Soldiers / 20.0) + (c.Tanks * 15);
        double airPower = (c.Planes * 12) + (c.Bombers * 25);
        double otherPower = (c.Cities * 50) + (c.AntiAir * 8) + (c.RecruitmentRate * 40) + (c.DefenseWins * 30);
        return (long)Math.Ceiling(Math.Max(0, popPower + incomePower + groundPower + airPower + otherPower));
    }

    static bool IsSuperpowerCollision(long chatId, long leaderId, long targetId, out string reason)
    {
        reason = "";
        var all = Database.GetCountriesByChatId(chatId).OrderByDescending(c => CalcManpower(c)).ToList();
        if (all.Count <= 1) return false;
        var leader = all.FirstOrDefault(c => c.OwnerId == leaderId);
        var target = all.FirstOrDefault(c => c.OwnerId == targetId);
        if (leader == null || target == null) return false;
        int leaderRank = all.IndexOf(leader) + 1;
        int targetRank = all.IndexOf(target) + 1;
        double totalMp = all.Sum(c => CalcManpower(c));
        double leaderMp = CalcManpower(leader);
        double targetMp = CalcManpower(target);
        if (all.Count >= 3 && leaderRank <= 2 && targetRank <= 2) { reason = "رتبه ۱ و ۲ نمی‌توانند هم‌اتحاد شوند."; return true; }
        if (all.Count >= 4 && leaderRank <= 3 && targetRank <= 3 && (leaderMp + targetMp) > (totalMp * 0.40)) { reason = "ترکیب دو قدرت برتر باعث ابرقدرت می‌شود."; return true; }
        long aid = Database.GetUserAllianceId(chatId, leaderId);
        double curAllianceMp = leaderMp;
        if (aid > 0) { var members = Database.GetAllianceMembers(aid); curAllianceMp = members.Sum(m => { var c = all.FirstOrDefault(x => x.OwnerId == m); return c != null ? CalcManpower(c) : 0; }); }
        double avgMp = totalMp / all.Count;
        if (targetMp > (avgMp * 1.3) && (curAllianceMp + targetMp) > (totalMp * 0.45) && all.Count >= 3) { reason = "مان‌پاور اتحاد از حد مجاز فراتر می‌رود."; return true; }
        return false;
    }

    static async Task HandleAllianceInviteCallback(CallbackQuery cb, CancellationToken ct)
    {
        if (cb.Data == null || cb.From == null) return;
        var parts = cb.Data.Split(':');
        if (parts.Length < 2 || !TryParseLong(parts[1], out long invId)) return;
        var inv = Database.GetAllianceInvite(invId);
        if (inv == null) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ منقضی شده.", showAlert: true, cancellationToken: ct); if (cb.Message != null) DeleteNow(cb.Message.Chat.Id, cb.Message.MessageId); return; }
        if (cb.From.Id != inv.TargetUserId) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ برای شما نیست!", showAlert: true, cancellationToken: ct); return; }
        string action = parts[0];
        if (action == "ally_reject") { Database.DeleteAllianceInvite(invId); await bot.AnswerCallbackQueryAsync(cb.Id, "❌ رد شد.", cancellationToken: ct); if (cb.Message != null) await bot.EditMessageTextAsync(cb.Message.Chat.Id, cb.Message.MessageId, "❌ رد شد.", cancellationToken: ct); try { await bot.SendTextMessageAsync(inv.LeaderId, "❌ دعوت رد شد.", cancellationToken: ct); } catch { } return; }
        if (action == "ally_accept")
        {
            var alliance = Database.GetAllianceById(inv.AllianceId);
            if (alliance == null) { Database.DeleteAllianceInvite(invId); await bot.AnswerCallbackQueryAsync(cb.Id, "❌ اتحاد منحل شده.", showAlert: true, cancellationToken: ct); return; }
            if (Database.GetUserAllianceId(inv.ChatId, inv.TargetUserId) > 0) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ عضو اتحاد دیگری هستید!", showAlert: true, cancellationToken: ct); return; }
            int totalPlayers = Database.GetCountriesByChatId(inv.ChatId).Count;
            int maxMembers = Math.Max(2, totalPlayers / 2);
            if (Database.GetAllianceMembers(inv.AllianceId).Count >= maxMembers) { await bot.AnswerCallbackQueryAsync(cb.Id, "⛔ ظرفیت پر!", showAlert: true, cancellationToken: ct); return; }
            if (IsSuperpowerCollision(inv.ChatId, inv.LeaderId, inv.TargetUserId, out string reason)) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ " + reason, showAlert: true, cancellationToken: ct); return; }
            Database.AddAllianceMember(inv.AllianceId, inv.ChatId, inv.TargetUserId);
            Console.WriteLine($"[SIEGE INTEGRITY] {Database.RepairSiegeIntegrity()}");
            Database.DeleteUserInvites(inv.ChatId, inv.TargetUserId);
            await bot.AnswerCallbackQueryAsync(cb.Id, "🎉 عضو شدید!", cancellationToken: ct);
            if (cb.Message != null) await bot.EditMessageTextAsync(cb.Message.Chat.Id, cb.Message.MessageId, $"🎉 به اتحاد «{alliance.Name}» پیوستید!", cancellationToken: ct);
            var tc = Database.GetCountry(inv.TargetUserId, inv.ChatId);
            try { await SendPermanent(inv.ChatId, $"🎉 کشور {tc?.Name} ({tc?.OwnerName}) به اتحاد «{alliance.Name}» پیوست! 🤝", ct: ct); } catch { }
        }
    }

    static async Task HandleTransferCallback(CallbackQuery cb, CancellationToken ct)
    {
        if (cb.Data == null || cb.From == null) return;
        long uid = cb.From.Id;
        var parts = cb.Data.Split(':');
        if (parts.Length < 2) return;
        string action = parts[0];

        if (action == "tf_chat")
        {
            if (!TryParseLong(parts[1], out long cid)) return;
            long aid = Database.GetUserAllianceId(cid, uid);
            if (aid == 0) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ عضو اتحاد نیستید.", showAlert: true, cancellationToken: ct); return; }
            var mems = Database.GetAllianceMembers(aid).Where(m => m != uid).ToList();
            if (mems.Count == 0) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ عضو دیگری نیست.", showAlert: true, cancellationToken: ct); return; }
            if (GetTransferCount(cid, uid) >= MAX_TRANSFERS_PER_UPDATE && !Database.HasGroupLockExemption(cid)) { await bot.AnswerCallbackQueryAsync(cb.Id, $"⛔ سهمیه تمام شد.", showAlert: true, cancellationToken: ct); return; }
            sessions[uid] = new UserSession { Step = SessionStep.TransferWaitingResource, TransferChatId = cid, TransferAllianceId = aid };
            await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
            var kb = new InlineKeyboardMarkup(new[] {
                new[] { InlineKeyboardButton.WithCallbackData("💰 پول", $"tf_res:{cid}:money"), InlineKeyboardButton.WithCallbackData("🔩 آهن", $"tf_res:{cid}:iron") },
                new[] { InlineKeyboardButton.WithCallbackData("🪖 سرباز", $"tf_res:{cid}:soldiers"), InlineKeyboardButton.WithCallbackData("🛡 تانک", $"tf_res:{cid}:tanks") },
                new[] { InlineKeyboardButton.WithCallbackData("✈️ جنگنده", $"tf_res:{cid}:planes"), InlineKeyboardButton.WithCallbackData("🛩 بمب‌افکن", $"tf_res:{cid}:bombers") },
                new[] { InlineKeyboardButton.WithCallbackData("🚤 قایق", $"tf_res:{cid}:boats"), InlineKeyboardButton.WithCallbackData("⚓ زیردریایی", $"tf_res:{cid}:submarines") },
                new[] { InlineKeyboardButton.WithCallbackData("🚢 نبردناو", $"tf_res:{cid}:battleships") }
            });
            if (cb.Message != null) await bot.EditMessageTextAsync(uid, cb.Message.MessageId, "📦 نوع منبع (شامل نیروی دریایی):", replyMarkup: kb, cancellationToken: ct);
            return;
        }

        if (action == "tf_res")
        {
            if (parts.Length < 3 || !TryParseLong(parts[1], out long cid)) return;
            string res = parts[2];
            long aid = Database.GetUserAllianceId(cid, uid);
            if (aid == 0) return;
            var mems = Database.GetAllianceMembers(aid).Where(m => m != uid).ToList();
            var kbList = mems.Select(m =>
            {
                var c = Database.GetCountry(m, cid);
                long capacity = c == null ? 0 : Database.GetBattleshipCapacityUsed(m, cid);
                string navalCapacity = res == "battleships" ? $" – 🚢{capacity}/3" : "";
                return new[] { InlineKeyboardButton.WithCallbackData($"👑 {(c?.OwnerName ?? $"کاربر {m}")} ({c?.Name}){navalCapacity}", $"tf_target:{cid}:{res}:{m}") };
            }).ToArray();
            sessions[uid] = new UserSession { Step = SessionStep.TransferWaitingTarget, TransferChatId = cid, TransferAllianceId = aid, TransferResourceType = res };
            await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
            if (cb.Message != null)
            {
                try
                {
                    await bot.EditMessageTextAsync(uid, cb.Message.MessageId,
                        $"🎯 مقصد برای {res}:\n⚠️ نبردناو: حداکثر 3 عدد",
                        replyMarkup: new InlineKeyboardMarkup(kbList), cancellationToken: ct);
                }
                catch (Telegram.Bot.Exceptions.ApiRequestException ex) when
                    (ex.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase))
                {
                    // Repeated taps on the same resource are harmless; Telegram rejects an
                    // identical edit, so keep the current screen without polluting error logs.
                }
            }
            return;
        }

        if (action == "tf_target")
        {
            if (parts.Length < 4 || !TryParseLong(parts[1], out long cid) || !TryParseLong(parts[3], out long tgtId)) return;
            string res = parts[2];
            long aid = Database.GetUserAllianceId(cid, uid);
            if (aid == 0) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ عضو اتحاد نیستید.", showAlert: true, cancellationToken: ct); return; }
            if (Database.GetUserAllianceId(cid, tgtId) != aid) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ هم‌اتحاد نیست.", showAlert: true, cancellationToken: ct); return; }
            // Battleship cap check at target selection
            if (res == "battleships")
            {
                var recv = Database.GetCountry(tgtId, cid);
                long usedCapacity = recv == null ? 3 : Database.GetBattleshipCapacityUsed(recv.OwnerId, recv.ChatId);
                if (recv != null && usedCapacity >= 3)
                {
                    await bot.AnswerCallbackQueryAsync(cb.Id, $"⛔ ظرفیت نبردناو این کشور پر است: {usedCapacity}/3", showAlert: true, cancellationToken: ct);
                    return;
                }
            }
            var sess = sessions.GetOrAdd(uid, _ => new UserSession());
            sess.Step = SessionStep.TransferWaitingDuration; sess.TransferChatId = cid; sess.TransferAllianceId = aid; sess.TransferResourceType = res; sess.TransferTargetId = tgtId;
            await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
            var durKb = new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("⚡ ۱۵ دقیقه", $"tf_dur:15"), InlineKeyboardButton.WithCallbackData("🚀 ۳۰ دقیقه", $"tf_dur:30") }, new[] { InlineKeyboardButton.WithCallbackData("🚚 ۱ ساعت", $"tf_dur:60"), InlineKeyboardButton.WithCallbackData("🐢 ۲ ساعت", $"tf_dur:120") } });
            if (cb.Message != null) await bot.EditMessageTextAsync(uid, cb.Message.MessageId, "⏳ زمان:", replyMarkup: durKb, cancellationToken: ct);
            return;
        }

        if (action == "tf_dur")
        {
            if (parts.Length < 2 || !TryParseInt(parts[1], out int min)) return;
            if (!sessions.TryGetValue(uid, out var sess) || sess == null) return;
            sess.TransferDurationMin = min;
            var c = Database.GetCountry(uid, sess.TransferChatId);
            if (c == null) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ کشور یافت نشد.", showAlert: true, cancellationToken: ct); return; }

            var breakdown = GetTransferSelectionBreakdown(c, sess.TransferResourceType);
            if (breakdown.Count == 0)
            {
                await bot.AnswerCallbackQueryAsync(cb.Id, "❌ موجودی ندارید.", showAlert: true, cancellationToken: ct);
                return;
            }

            // Prepare session lists for per-model transfer
            sess.TransferModelNames = breakdown.Select(b => b.ModelName).ToList();
            sess.TransferModelCounts = breakdown.Select(b => b.Count).ToList();
            sess.TransferModelAmounts = new List<long>(new long[breakdown.Count]);
            sess.TransferModelIndex = 0;

            string rn = GetResName(sess.TransferResourceType);

            await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);

            if (breakdown.Count == 1)
            {
                // Single model – simple Persian prompt
                sess.Step = SessionStep.TransferWaitingAmount;
                string modelInfo = string.IsNullOrWhiteSpace(breakdown[0].ModelName) ? "" : $"\n🔧 مدل: {breakdown[0].ModelName}";
                if (cb.Message != null)
                    await bot.EditMessageTextAsync(uid, cb.Message.MessageId,
                        $"🔢 مقدار انتقال را وارد کنید:{modelInfo}\n📦 {rn}\n📊 موجودی: {breakdown[0].Count:N0}\n\n✍️ عدد را به فارسی یا انگلیسی بنویسید. 0 برای لغو.",
                        cancellationToken: ct);
            }
            else
            {
                // Multiple models – ask per model in Persian
                sess.Step = SessionStep.TransferWaitingModelAmount;
                var cur = breakdown[0];
                string modelInfo = string.IsNullOrWhiteSpace(cur.ModelName) ? rn : cur.ModelName;
                if (cb.Message != null)
                    await bot.EditMessageTextAsync(uid, cb.Message.MessageId,
                        $"📦 انتقال {rn} – چند نوع دارید ({breakdown.Count} مدل)\n\n🔧 مدل {1}/{breakdown.Count}: {modelInfo}\n📊 موجودی این مدل: {cur.Count:N0}\n\nچند عدد از این مدل ارسال شود؟ (0 برای رد شدن)\n✍️ عدد را وارد کنید:",
                        cancellationToken: ct);
            }
            return;
        }
    }

    static async Task BeginDeploymentJoinTankSelection(long uid, UserSession sess, CancellationToken ct)
    {
        var country = Database.GetCountry(uid, sess.DeployChatId);
        if (country == null) { EndSession(uid); return; }
        var breakdown = GetTransferBreakdown(country, "tanks");
        if (breakdown.Count == 0)
        {
            sess.DeployJoinTanks = 0;
            sess.Step = SessionStep.DeployJoinWaitingSoldiers;
            await SendPrompt(uid, uid, $"🪖 سرباز:\nموجود: {country.Soldiers:N0}", ct: ct);
            return;
        }
        sess.DeployModelNames = breakdown.Select(x => x.ModelName).ToList();
        sess.DeployModelCounts = breakdown.Select(x => x.Count).ToList();
        sess.DeployModelAmounts = new List<long>(new long[breakdown.Count]);
        sess.DeployModelIndex = 0;
        sess.Step = SessionStep.DeployJoinWaitingTankModel;
        await SendPrompt(uid, uid,
            $"🛡 مشارکت – تانک مدل 1/{breakdown.Count}: {breakdown[0].ModelName} – موجودی {breakdown[0].Count:N0}\nچند تا اعزام شود؟ (0 برای رد)", ct: ct);
    }

    static async Task HandleDeploymentCallback(CallbackQuery cb, CancellationToken ct)
    {
        if (cb.Data == null || cb.From == null) return;
        long uid = cb.From.Id;
        var parts = cb.Data.Split(':');
        if (parts.Length < 2) return;
        string action = parts[0];

        if (action == "dep_chat")
        {
            if (parts.Length < 3 || !TryParseLong(parts[1], out long cid)) return;
            bool isOff = parts[2] == "Offensive";
            long aid = Database.GetUserAllianceId(cid, uid);
            if (aid == 0) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ عضو اتحاد نیستید.", showAlert: true, cancellationToken: ct); return; }
            var tgts = isOff ? Database.GetAttackableTargets(cid, uid) : Database.GetAllianceMembers(aid).Select(m => Database.GetCountry(m, cid)).Where(c => c != null).ToList()!;
            if (tgts.Count == 0) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ هدفی نیست.", showAlert: true, cancellationToken: ct); return; }
            var tkb = tgts.Select(t => new[] { InlineKeyboardButton.WithCallbackData($"🏳️ {t!.Name} ({t.OwnerName})", $"dep_target:{cid}:{aid}:{(isOff ? "Off" : "Def")}:{t.OwnerId}") }).ToArray();
            await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
            if (cb.Message != null) await bot.EditMessageTextAsync(uid, cb.Message.MessageId, $"⚔️ صف‌آرایی {(isOff ? "تهاجمی" : "دفاعی")}\n🎯 کشور:", replyMarkup: new InlineKeyboardMarkup(tkb), cancellationToken: ct);
            return;
        }

        if (action == "dep_target")
        {
            if (parts.Length < 5) return;
            if (!TryParseLong(parts[1], out long cid) || !TryParseLong(parts[2], out long aid) || !TryParseLong(parts[4], out long tid)) return;
            string typeStr = parts[3] == "Off" ? "Offensive" : "Defensive";
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (typeStr == "Offensive" && Database.HasRecentTargetDeployment(cid, tid, nowMs - 86400000L) && !Database.HasGroupLockExemption(cid))
            { await bot.AnswerCallbackQueryAsync(cb.Id, "⛔ ۲۴ ساعت گذشته صف‌آرایی علیه این هدف اعلام شده!", showAlert: true, cancellationToken: ct); return; }
            sessions[uid] = new UserSession { Step = SessionStep.DeployWaitingDuration, DeployChatId = cid, DeployAllianceId = aid, DeployType = typeStr, DeployTargetId = tid };
            await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
            var durKb = new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("⏳ ۱ ساعت", $"dep_dur:1"), InlineKeyboardButton.WithCallbackData("⏳ ۲ ساعت", $"dep_dur:2") }, new[] { InlineKeyboardButton.WithCallbackData("⏳ ۳ ساعت", $"dep_dur:3") } });
            if (cb.Message != null) await bot.EditMessageTextAsync(uid, cb.Message.MessageId, "⏳ مدت:", replyMarkup: durKb, cancellationToken: ct);
            return;
        }

        if (action == "dep_dur")
        {
            if (parts.Length < 2 || !TryParseInt(parts[1], out int dur)) return;
            if (!sessions.TryGetValue(uid, out var sess) || sess == null) return;
            sess.DeployDuration = dur;
            sess.DeployFormation = "Unified"; //  – removed MultiFront mode
            sess.Step = SessionStep.DeployWaitingStrategy;
            await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
            var sk = new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("⚔️ هجوم سریع", $"dep_strat:1") }, new[] { InlineKeyboardButton.WithCallbackData("🛡 ضدحمله", $"dep_strat:2") } });
            if (cb.Message != null) await bot.EditMessageTextAsync(uid, cb.Message.MessageId, "🎯 استراتژی:", replyMarkup: sk, cancellationToken: ct);
            return;
        }

        if (action == "dep_form")
        {
            if (parts.Length < 2) return;
            if (!sessions.TryGetValue(uid, out var sess) || sess == null) return;
            sess.DeployFormation = parts[1];
            if (sess.DeployFormation == "Unified")
            {
                sess.Step = SessionStep.DeployWaitingStrategy;
                await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
                var sk = new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("⚔️ هجوم سریع", $"dep_strat:1") }, new[] { InlineKeyboardButton.WithCallbackData("🛡 ضدحمله", $"dep_strat:2") } });
                if (cb.Message != null) await bot.EditMessageTextAsync(uid, cb.Message.MessageId, "🎯 استراتژی:", replyMarkup: sk, cancellationToken: ct);
                return;
            }
            else
            {
                sess.Step = SessionStep.DeployWaitingTanks;
                await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
                var c = Database.GetCountry(uid, sess.DeployChatId);
                if (cb.Message != null) await bot.EditMessageTextAsync(uid, cb.Message.MessageId, $"🛡 تانک:\nموجود: {c?.Tanks ?? 0}", cancellationToken: ct);
                return;
            }
        }

        if (action == "dep_strat")
        {
            if (parts.Length < 2 || !TryParseInt(parts[1], out int str)) return;
            if (!sessions.TryGetValue(uid, out var sess) || sess == null) return;
            sess.DeployStrategy = str;
            sess.Step = SessionStep.DeployWaitingTactic;
            await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
            var tk = new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("🔥 ضربتی", $"dep_tac:1") }, new[] { InlineKeyboardButton.WithCallbackData("🎯 محاصره‌ای", $"dep_tac:2") } });
            if (cb.Message != null) await bot.EditMessageTextAsync(uid, cb.Message.MessageId, "🎯 تاکتیک:", replyMarkup: tk, cancellationToken: ct);
            return;
        }

        if (action == "dep_tac")
        {
            if (parts.Length < 2 || !TryParseInt(parts[1], out int tac)) return;
            if (!sessions.TryGetValue(uid, out var sess) || sess == null) return;
            sess.DeployTactic = tac;
            sess.Step = SessionStep.DeployWaitingTanks;
            await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
            var c = Database.GetCountry(uid, sess.DeployChatId);
            if (cb.Message != null) await bot.EditMessageTextAsync(uid, cb.Message.MessageId, $"🛡 تانک:\nموجود: {c?.Tanks ?? 0}", cancellationToken: ct);
            return;
        }

        if (action == "dep_join")
        {
            if (parts.Length < 2 || !TryParseLong(parts[1], out long depId)) return;
            var dep = Database.GetDeploymentById(depId);
            if (dep == null) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ پایان یافته.", showAlert: true, cancellationToken: ct); return; }
            var depC = Database.GetCountry(uid, dep.ChatId); if (depC != null && depC.PortLevel < 3) { await bot.AnswerCallbackQueryAsync(cb.Id, "⚓ سطح بندر شما برای اعزام نیرو کافی نیست! (حداقل سطح: ۳)", showAlert: true, cancellationToken: ct); return; }
            long aid = Database.GetUserAllianceId(dep.ChatId, uid);
            if (aid != dep.AllianceId) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ اتحاد شما نیست!", showAlert: true, cancellationToken: ct); return; }
            if (dep.EndAtMs <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ مهلت تمام شد.", showAlert: true, cancellationToken: ct); return; }
            sessions[uid] = new UserSession { DeployJoinId = dep.Id, DeployChatId = dep.ChatId, DeployAllianceId = dep.AllianceId };
            await bot.AnswerCallbackQueryAsync(cb.Id, "⚔️ به پی‌وی هدایت شدید.", cancellationToken: ct);
            if (dep.FormationType == "MultiFront")
            {
                sessions[uid].Step = SessionStep.DeployJoinWaitingStrategy;
                var sk = new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("⚔️ هجوم سریع", $"dep_jstrat:{dep.Id}:1") }, new[] { InlineKeyboardButton.WithCallbackData("🛡 ضدحمله", $"dep_jstrat:{dep.Id}:2") } });
                try { await bot.SendTextMessageAsync(uid, "🧩 استراتژی یگان کمکی:", replyMarkup: sk, cancellationToken: ct); }
                catch { await bot.AnswerCallbackQueryAsync(cb.Id, "⚠️ ابتدا ربات را در پیوی استارت کنید.", showAlert: true, cancellationToken: ct); }
            }
            else
            {
                try { await BeginDeploymentJoinTankSelection(uid, sessions[uid], ct); }
                catch { await bot.AnswerCallbackQueryAsync(cb.Id, "⚠️ ابتدا ربات را در پیوی استارت کنید.", showAlert: true, cancellationToken: ct); }
            }
            return;
        }

        if (action == "dep_jstrat")
        {
            if (parts.Length < 3 || !TryParseLong(parts[1], out long depId) || !TryParseInt(parts[2], out int str)) return;
            if (!sessions.TryGetValue(uid, out var sess) || sess == null) return;
            sess.DeployJoinStrategy = str;
            sess.Step = SessionStep.DeployJoinWaitingTactic;
            await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
            var tk = new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("🔥 ضربتی", $"dep_jtac:{depId}:1") }, new[] { InlineKeyboardButton.WithCallbackData("🎯 محاصره‌ای", $"dep_jtac:{depId}:2") } });
            if (cb.Message != null) await bot.EditMessageTextAsync(uid, cb.Message.MessageId, "🎯 تاکتیک:", replyMarkup: tk, cancellationToken: ct);
            return;
        }

        if (action == "dep_jtac")
        {
            if (parts.Length < 3 || !TryParseLong(parts[1], out long depId) || !TryParseInt(parts[2], out int tac)) return;
            if (!sessions.TryGetValue(uid, out var sess) || sess == null) return;
            sess.DeployJoinTactic = tac;
            await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
            if (cb.Message != null) DeleteNow(cb.Message.Chat.Id, cb.Message.MessageId);
            await BeginDeploymentJoinTankSelection(uid, sess, ct);
            return;
        }

        if (action == "dep_cancel")
        {
            if (parts.Length < 2 || !TryParseLong(parts[1], out long depId)) return;
            var dep = Database.GetDeploymentById(depId);
            if (dep == null) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ قبلاً خاتمه یافته.", showAlert: true, cancellationToken: ct); return; }
            var alliance = Database.GetAllianceById(dep.AllianceId);
            if (alliance == null || (dep.InitiatorId != uid && alliance.LeaderId != uid)) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ دسترسی ندارید.", showAlert: true, cancellationToken: ct); return; }
            await CancelDeploymentSafely(dep, ct);
            await bot.AnswerCallbackQueryAsync(cb.Id, "✅ لغو شد.", cancellationToken: ct);
            if (cb.Message != null) DeleteNow(cb.Message.Chat.Id, cb.Message.MessageId);
            try { await SendPermanent(dep.ChatId, "🚫 صف‌آرایی لغو شد.", ct: ct); } catch { }
            return;
        }
    }

    internal static string BuildNavalInventorySummary(Country c)
    {
        var outgoing=Database.GetOutgoingNavalTransfers(c.OwnerId,c.ChatId);
        long boatsTransfer=outgoing.Boats,subsTransfer=outgoing.Submarines,shipsTransfer=outgoing.Battleships;
        long boatsTotal=c.Boats+c.BoatsAtSea+boatsTransfer;
        long subsTotal=c.Submarines+c.SubmarinesAtSea+subsTransfer;
        long shipsTotal=c.Battleships+c.BattleshipsAtSea+shipsTransfer;
        string Segment(long ready,long mission,long transfer) =>
            $"آماده {ready:N0}"+(mission>0?$"، مأموریت {mission:N0}":"")+(transfer>0?$"، انتقال {transfer:N0}":"");
        return $"🚤 قایق: کل {boatsTotal:N0} ({Segment(c.Boats,c.BoatsAtSea,boatsTransfer)})\n"+
               $"⚓ زیردریایی: کل {subsTotal:N0} ({Segment(c.Submarines,c.SubmarinesAtSea,subsTransfer)})\n"+
               $"🚢 نبردناو: کل {shipsTotal:N0}/3 ({Segment(c.Battleships,c.BattleshipsAtSea,shipsTransfer)})";
    }

    static async Task SendCountryInfo(long chatId, Country c, CancellationToken ct)
    {
        double bInc = CalcBuildingMoney(c);
        double tInc = CalcTaxIncome(c);
        double iInc = CalcIronIncome(c) * SiegeIncomeFactor(c);
        bInc *= SiegeIncomeFactor(c);
        tInc *= SiegeIncomeFactor(c);
        double birthRate = c.Welfare / 100.0 * 0.05;
        double wTarget = WelfareTarget(c);
        bool defaultCitySiege=HasDefaultCitySiege(c);
        string status = !defaultCitySiege?"🏛 باثبات":c.Besieged>=2?"🆘 بحرانی":"⚠️ تحت محاصره";
        long mp = CalcManpower(c);
        string crisis = defaultCitySiege&&c.Besieged>=2 ? "🆘 بحرانی! (۵۰٪ درآمد، قفل سطح ۴-۵)\n\n" : "";
        string navalLine = BuildNavalInventorySummary(c);
        string info = crisis + $"🏳️ کشور: {c.Name}\n👤 مالک: {c.OwnerName}\n{status}\n⚡ مان‌پاور: {mp / 1000.0:F1}K\n\n" +
            $"💰 پول: {(c.Money / 1000.0):F1}K\n🏭 ساختمان: +{bInc / 1000.0:F1}K\n🧾 مالیات: +{tInc / 1000.0:F1}K ({c.TaxRate}%)\n\n" +
            $"🔩 آهن: {(c.Iron / 1000.0):F1}K\n⛏️ معدن: +{iInc / 1000.0:F1}K\n\n" +
            $"👥 جمعیت: {(c.Population / 1000.0):F1}K\n📊 تولد: {birthRate * 100:F2}%\n🏙 شهرها: {c.Cities}\n\n" +
            $"🪖 سرباز: {(c.Soldiers / 1000.0):F1}K\n🎯 سربازگیری: {c.RecruitmentRate}\n🏥 رفاه: {c.Welfare:F1}% (هدف: {wTarget:F0}%)\n\n" +
            $"🪖 تانک: {c.Tanks}\n✈️ جنگنده: {c.Planes}\n🛩 بمب‌افکن: {c.Bombers}\n🎯 پدافند: {c.AntiAir}\n" +
            $"{navalLine}\n\n" +
            $"🏭 کارخانه: {c.FactoryLevel} | ⚓ بندر: {c.PortLevel} | ⛏️ معدن: {c.MineLevel}";
        var kbDetails = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("⚔️ جزئیات نظامی", $"eq_details:{c.OwnerId}") },
            new[] { InlineKeyboardButton.WithCallbackData("🛡 اطلاعات نیروهای صف آرایی", $"dep_info:{c.OwnerId}") }
        });
        if (!string.IsNullOrEmpty(c.FlagFileId)) await SendTempPhoto(chatId, c.FlagFileId, info, markup: kbDetails, ct: ct);
        else await SendTemp(chatId, info, markup: kbDetails, ct: ct);
    }


    static async Task SendCountryEquipmentDetails(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length < 2 || cb.Message == null) return;
        if (!TryParseLong(parts[1], out long targetUid)) return;
        if (targetUid != cb.From.Id)
        {
            await bot.AnswerCallbackQueryAsync(cb.Id, "⛔ این دکمه فقط برای صاحب کشور است!", showAlert: true, cancellationToken: ct);
            return;
        }
        long uid = cb.From.Id;
        long chatId = cb.Message.Chat.Id;
        var c = Database.GetCountry(targetUid, chatId);
        if (c == null)
        {
            await bot.AnswerCallbackQueryAsync(cb.Id, "❌ کشور یافت نشد.", showAlert: true, cancellationToken: ct);
            return;
        }
        if (c.OwnerId != cb.From.Id)
        {
            await bot.AnswerCallbackQueryAsync(cb.Id, "⛔ این دکمه فقط برای صاحب کشور است!", showAlert: true, cancellationToken: ct);
            return;
        }

        // Helper to get faction from model name
        Faction GetFactionFromModel(string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName)) return c.Faction;
            string m = modelName.ToLowerInvariant();
            if (m.Contains("bismarck") || m.Contains("s-boot") || m.Contains("sboot") || m.Contains("viic") || m.Contains("panzer") || m.Contains("bf 109") || m.Contains("he 111")) return Faction.Reich;
            if (m.Contains("iowa") || m.Contains("pt") || m.Contains("gato") || m.Contains("m2") || m.Contains("p-36") || m.Contains("b-17")) return Faction.USA;
            if (m.Contains("soyuz") || m.Contains("sovetsky") || m.Contains("g-5") || m.Contains("g5") || m.Contains("s-class") || m.Contains("t-28") || m.Contains("i-16") || m.Contains("db-3")) return Faction.USSR;
            return c.Faction;
        }
        string FactionEmoji(Faction f) => f switch { Faction.USSR => "☭ شوروی", Faction.USA => "🇺🇸 آمریکا", Faction.Reich => "⚫ رایش", _ => f.ToString() };

        // Tanks
        var fTanks = Database.GetEquipmentModels(targetUid, chatId, "Tanks");
        var tankGroups = new Dictionary<Faction, List<(string Model, long Count)>>();
        long domTanks = Math.Max(0, c.Tanks - fTanks.Sum(x=>x.Count));
        if (domTanks>0) { var f=Database.GetDefaultTankModel(c.Faction); var fac=GetFactionFromModel(f); if (!tankGroups.ContainsKey(fac)) tankGroups[fac]=new(); tankGroups[fac].Add((f, domTanks)); }
        foreach (var ft in fTanks) { var fac=GetFactionFromModel(ft.ModelName); if (!tankGroups.ContainsKey(fac)) tankGroups[fac]=new(); tankGroups[fac].Add((ft.ModelName, ft.Count)); }

        // Planes
        var fPlanes = Database.GetEquipmentModels(targetUid, chatId, "Planes");
        var planeGroups = new Dictionary<Faction, List<(string Model, long Count)>>();
        long domPlanes = Math.Max(0, c.Planes - fPlanes.Sum(x=>x.Count));
        if (domPlanes>0) { var f=Database.GetDefaultPlaneModel(c.Faction); var fac=GetFactionFromModel(f); if (!planeGroups.ContainsKey(fac)) planeGroups[fac]=new(); planeGroups[fac].Add((f, domPlanes)); }
        foreach (var fp in fPlanes) { var fac=GetFactionFromModel(fp.ModelName); if (!planeGroups.ContainsKey(fac)) planeGroups[fac]=new(); planeGroups[fac].Add((fp.ModelName, fp.Count)); }

        // Bombers
        var fBombers = Database.GetEquipmentModels(targetUid, chatId, "Bombers");
        var bomberGroups = new Dictionary<Faction, List<(string Model, long Count)>>();
        long domBombers = Math.Max(0, c.Bombers - fBombers.Sum(x=>x.Count));
        if (domBombers>0) { var f=Database.GetDefaultBomberModel(c.Faction); var fac=GetFactionFromModel(f); if (!bomberGroups.ContainsKey(fac)) bomberGroups[fac]=new(); bomberGroups[fac].Add((f, domBombers)); }
        foreach (var fb in fBombers) { var fac=GetFactionFromModel(fb.ModelName); if (!bomberGroups.ContainsKey(fac)) bomberGroups[fac]=new(); bomberGroups[fac].Add((fb.ModelName, fb.Count)); }

        // Boats – listed separately by faction
        var fBoats = Database.GetEquipmentModels(targetUid, chatId, "Boats");
        var boatGroups = new Dictionary<Faction, List<(string Model, long Count)>>();
        long domBoats = Math.Max(0, c.Boats - fBoats.Sum(x=>x.Count));
        if (domBoats>0) { var f=Database.GetDefaultBoatModel(c.Faction); var fac=GetFactionFromModel(f); if (!boatGroups.ContainsKey(fac)) boatGroups[fac]=new(); boatGroups[fac].Add((f, domBoats)); }
        foreach (var fb in fBoats) { var fac=GetFactionFromModel(fb.ModelName); if (!boatGroups.ContainsKey(fac)) boatGroups[fac]=new(); boatGroups[fac].Add((fb.ModelName, fb.Count)); }

        // Submarines – separately by faction
        var fSubs = Database.GetEquipmentModels(targetUid, chatId, "Submarines");
        var subGroups = new Dictionary<Faction, List<(string Model, long Count)>>();
        long domSubs = Math.Max(0, c.Submarines - fSubs.Sum(x=>x.Count));
        if (domSubs>0) { var f=Database.GetDefaultSubModel(c.Faction); var fac=GetFactionFromModel(f); if (!subGroups.ContainsKey(fac)) subGroups[fac]=new(); subGroups[fac].Add((f, domSubs)); }
        foreach (var fs in fSubs) { var fac=GetFactionFromModel(fs.ModelName); if (!subGroups.ContainsKey(fac)) subGroups[fac]=new(); subGroups[fac].Add((fs.ModelName, fs.Count)); }

        // Battleships – separately by faction
        var fBS = Database.GetEquipmentModels(targetUid, chatId, "Battleships");
        var bsGroups = new Dictionary<Faction, List<(string Model, long Count)>>();
        long domBS = Math.Max(0, c.Battleships - fBS.Sum(x=>x.Count));
        if (domBS>0) { var f=Database.GetDefaultBattleshipModel(c.Faction); var fac=GetFactionFromModel(f); if (!bsGroups.ContainsKey(fac)) bsGroups[fac]=new(); bsGroups[fac].Add((f, domBS)); }
        foreach (var fb in fBS) { var fac=GetFactionFromModel(fb.ModelName); if (!bsGroups.ContainsKey(fac)) bsGroups[fac]=new(); bsGroups[fac].Add((fb.ModelName, fb.Count)); }

        var sb = new StringBuilder();
        sb.AppendLine($"⚔️ <b>جزئیات نظامی {c.Name} (خصوصی):</b>");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine($"👤 مالک: {c.OwnerName} | 💰 {c.Money:N0} | 🔩 {c.Iron:N0}");
        Database.SyncBattleshipUnits(c.OwnerId, c.ChatId);
        var damagedShips = Database.GetBattleshipUnits(c.OwnerId, c.ChatId, onlyCombatReady: false)
            .Where(x => x.DamagePercent > 0).ToList();
        foreach (var ship in damagedShips)
            sb.AppendLine($"🔧 {ship.Model} شماره {ship.ShipNumber}: آسیب {ship.DamagePercent}٪" +
                (ship.DamagePercent > 50 ? " — غیرقابل اعزام" : " — قابل اعزام با افت عملکرد"));
        foreach (var op in Database.GetActiveNavalInvasionsByAttacker(c.OwnerId, c.ChatId))
        {
            long left = Math.Max(0, op.ArriveAtMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            sb.AppendLine($"🌊 عملیات دریایی #{op.Id}: در حرکت به {op.DefenderName} — {FormatRemaining(left)} دیگر");
            sb.AppendLine($"   🚤 {op.Boats:N0} | ⚓ {op.Submarines:N0} | 🚢 {op.Battleships:N0}");
        }
        sb.AppendLine();

        sb.AppendLine("🛡 <b>تانک‌ها (تفکیک فکشن):</b>");
        if (tankGroups.Count==0) sb.AppendLine("  • هیچ تانکی موجود نمی‌باشد.");
        else foreach (var kv in tankGroups) { sb.AppendLine($"  <b>— {FactionEmoji(kv.Key)} —</b>"); foreach (var it in kv.Value) sb.AppendLine($"    • {it.Model}: {it.Count:N0} عدد"); }
        sb.AppendLine();

        sb.AppendLine("✈️ <b>جنگنده‌ها (تفکیک فکشن):</b>");
        if (planeGroups.Count==0) sb.AppendLine("  • هیچ جنگنده‌ای موجود نمی‌باشد.");
        else foreach (var kv in planeGroups) { sb.AppendLine($"  <b>— {FactionEmoji(kv.Key)} —</b>"); foreach (var it in kv.Value) sb.AppendLine($"    • {it.Model}: {it.Count:N0} عدد"); }
        sb.AppendLine();

        sb.AppendLine("🛩 <b>بمب‌افکن‌ها (تفکیک فکشن):</b>");
        if (bomberGroups.Count==0) sb.AppendLine("  • هیچ بمب‌افکنی موجود نمی‌باشد.");
        else foreach (var kv in bomberGroups) { sb.AppendLine($"  <b>— {FactionEmoji(kv.Key)} —</b>"); foreach (var it in kv.Value) sb.AppendLine($"    • {it.Model}: {it.Count:N0} عدد"); }
        sb.AppendLine();

        sb.AppendLine("🚤 <b>قایق‌های تندرو — تفکیک فکشن (جداگانه):</b>");
        if (boatGroups.Count==0) sb.AppendLine("  • هیچ قایقی موجود نمی‌باشد.");
        else foreach (var kv in boatGroups) { sb.AppendLine($"  <b>— {FactionEmoji(kv.Key)} —</b>"); foreach (var it in kv.Value) sb.AppendLine($"    • {it.Model}: {it.Count:N0} عدد"); }
        sb.AppendLine();

        sb.AppendLine("⚓ <b>زیردریایی‌ها — تفکیک فکشن (جداگانه):</b>");
        if (subGroups.Count==0) sb.AppendLine("  • هیچ زیردریایی موجود نمی‌باشد.");
        else foreach (var kv in subGroups) { sb.AppendLine($"  <b>— {FactionEmoji(kv.Key)} —</b>"); foreach (var it in kv.Value) sb.AppendLine($"    • {it.Model}: {it.Count:N0} عدد"); }
        sb.AppendLine();

        sb.AppendLine("🚢 <b>نبردناوها — تفکیک فکشن (جداگانه):</b>");
        if (bsGroups.Count==0) sb.AppendLine("  • هیچ نبردناوی موجود نمی‌باشد.");
        else foreach (var kv in bsGroups) { sb.AppendLine($"  <b>— {FactionEmoji(kv.Key)} —</b>"); foreach (var it in kv.Value) sb.AppendLine($"    • {it.Model}: {it.Count:N0} عدد"); }
        sb.AppendLine();

        sb.AppendLine($"🎯 <b>پدافند هوایی:</b> {c.AntiAir:N0} عدد");
        sb.AppendLine($"🛡 دفاع: تانک {c.DefenseTanks:N0} / سرباز {c.DefenseSoldiers:N0} / جنگنده {c.DefenseFighters:N0} / قایق {c.DefenseBoats:N0} / زیردریایی {c.DefenseSubmarines:N0}");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine("ℹ️ این اطلاعات خصوصی است و فقط برای مالک ارسال شد.");

        // Private send – buttons are private
        try { await bot.SendTextMessageAsync(uid, sb.ToString(), parseMode: ParseMode.Html, cancellationToken: ct); }
        catch { await bot.SendTextMessageAsync(chatId, sb.ToString(), parseMode: ParseMode.Html, replyToMessageId: cb.Message.MessageId, cancellationToken: ct); }
        await bot.AnswerCallbackQueryAsync(cb.Id, "✅ جزئیات نظامی به پیوی ارسال شد (خصوصی)", cancellationToken: ct);
    }

    static async Task SendDeploymentInfoDetails(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length < 2 || cb.Message == null) return;
        if (!TryParseLong(parts[1], out long targetUid)) return;
        if (targetUid != cb.From.Id)
        {
            await bot.AnswerCallbackQueryAsync(cb.Id, "⛔ این دکمه فقط برای صاحب کشور است!", showAlert: true, cancellationToken: ct);
            return;
        }
        long uid = cb.From.Id;
        long chatId = cb.Message.Chat.Id;
        var c = Database.GetCountry(targetUid, chatId);
        if (c == null)
        {
            await bot.AnswerCallbackQueryAsync(cb.Id, "❌ کشور یافت نشد.", showAlert: true, cancellationToken: ct);
            return;
        }
        if (c.OwnerId != cb.From.Id)
        {
            await bot.AnswerCallbackQueryAsync(cb.Id, "⛔ این دکمه فقط برای صاحب کشور است!", showAlert: true, cancellationToken: ct);
            return;
        }

        try
        {
            var allDeps = Database.GetActiveDeployments().Where(d => d.ChatId == chatId).ToList();
            // Filter: defensive targeting this country OR offensive initiated by this country OR user is contributor
            var relevantDeps = new List<Deployment>();
            var userContribDepIds = new HashSet<long>();
            foreach (var dep in allDeps)
            {
                var contribs = Database.GetDeploymentContributors(dep.Id);
                if (contribs.Any(cc => cc.UserId == targetUid) || dep.TargetUserId == targetUid || dep.InitiatorId == targetUid)
                    relevantDeps.Add(dep);
            }

            if (relevantDeps.Count == 0)
            {
                await bot.SendTextMessageAsync(uid, $"🛡 <b>اطلاعات نیروهای صف آرایی برای {c.Name} (خصوصی):</b>\n\n❌ در حال حاضر هیچ نیروی صف آرایی فعالی مرتبط با شما وجود ندارد.\n\nنیروهای صف آرایی پس از ایجاد، در دارایی شما نمایش داده نمی‌شوند و فقط اینجا قابل مشاهده هستند.\nℹ️ این پیام خصوصی است.", parseMode: ParseMode.Html, cancellationToken: ct);
                await bot.AnswerCallbackQueryAsync(cb.Id, "ℹ️ صف آرایی فعالی نیست – خصوصی ارسال شد", cancellationToken: ct);
                return;
            }

            var factionGroups = new Dictionary<Faction, List<(string PlayerName, long Tanks, long Soldiers, long Fighters, long Bombers)>>();
            long totalTanks=0, totalSoldiers=0, totalFighters=0, totalBombers=0;
            var allContribsFlat = new List<(string PlayerName, Faction Faction, long Tanks, long Soldiers, long Fighters, long Bombers)>();

            foreach (var dep in relevantDeps)
            {
                var contribs = Database.GetDeploymentContributors(dep.Id);
                foreach (var contrib in contribs)
                {
                    var contribCountry = Database.GetCountry(contrib.UserId, chatId);
                    Faction faction = contribCountry?.Faction ?? Faction.USA;
                    string playerName = contribCountry?.OwnerName ?? $"کاربر {contrib.UserId}";
                    allContribsFlat.Add((playerName, faction, contrib.Tanks, contrib.Soldiers, contrib.Fighters, contrib.Bombers));
                    if (!factionGroups.ContainsKey(faction)) factionGroups[faction]=new List<(string, long, long, long, long)>();
                    factionGroups[faction].Add((playerName, contrib.Tanks, contrib.Soldiers, contrib.Fighters, contrib.Bombers));
                    totalTanks+=contrib.Tanks; totalSoldiers+=contrib.Soldiers; totalFighters+=contrib.Fighters; totalBombers+=contrib.Bombers;
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"🛡 <b>اطلاعات نیروهای صف آرایی برای {c.Name} (خصوصی):</b>");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine($"📊 <b>مجموع کل نیروها:</b> 🛡 {totalTanks:N0} | 🪖 {totalSoldiers:N0} | ✈️ {totalFighters:N0} | 🛩 {totalBombers:N0}");
            sb.AppendLine($"👥 مشارکت‌کنندگان: {allContribsFlat.Select(x=>x.PlayerName).Distinct().Count()} نفر در {relevantDeps.Count} صف آرایی");
            sb.AppendLine();
            sb.AppendLine("📋 <b>لیست صف آرایی‌های فعال:</b>");
            foreach (var dep in relevantDeps.Take(10))
            {
                var target = Database.GetCountry(dep.TargetUserId, chatId);
                string targetName = target?.Name ?? $"کاربر {dep.TargetUserId}";
                string typeFa = dep.Type=="Offensive" ? "تهاجمی" : "دفاعی";
                long remaining = dep.EndAtMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                string left = remaining>0 ? FormatRemaining(remaining) : "در حال پایان";
                sb.AppendLine($"• {typeFa} به {targetName} | {dep.Tanks}🛡 {dep.Soldiers}🪖 | استراتژی {dep.Strategy} تاکتیک {dep.Tactic} | باقی {left}");
            }
            sb.AppendLine();
            foreach (var kvp in factionGroups.OrderBy(k=>k.Key.ToString()))
            {
                string emoji = kvp.Key switch { Faction.USSR => "☭ شوروی", Faction.USA => "🇺🇸 آمریکا", Faction.Reich => "⚫ رایش", _ => kvp.Key.ToString() };
                var fTanks = kvp.Value.Sum(x=>x.Tanks); var fSols = kvp.Value.Sum(x=>x.Soldiers); var fFig = kvp.Value.Sum(x=>x.Fighters); var fBom = kvp.Value.Sum(x=>x.Bombers);
                sb.AppendLine($"<b>— {emoji} —</b> 🛡 {fTanks:N0} | 🪖 {fSols:N0} | ✈️ {fFig:N0} | 🛩 {fBom:N0}");
                foreach (var p in kvp.Value) sb.AppendLine($"  • {p.PlayerName}: {p.Tanks}🛡 {p.Soldiers}🪖 {p.Fighters}✈️ {p.Bombers}🛩️");
                sb.AppendLine();
            }
            sb.AppendLine("ℹ️ این نیروها در دارایی شما محاسبه نمی‌شوند و فقط در دفاع مشارکت دارند. پیام خصوصی است.");

            await bot.SendTextMessageAsync(uid, sb.ToString(), parseMode: ParseMode.Html, cancellationToken: ct);
            await bot.AnswerCallbackQueryAsync(cb.Id, "✅ اطلاعات صف آرایی به پیوی ارسال شد (خصوصی)", cancellationToken: ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEP_INFO ERR] {ex.Message}");
            try { await bot.SendTextMessageAsync(uid, $"❌ خطا در دریافت اطلاعات صف آرایی: {ex.Message} (خصوصی)", cancellationToken: ct); } catch {}
            await bot.AnswerCallbackQueryAsync(cb.Id, "❌ خطا، دوباره تلاش کنید", showAlert: true, cancellationToken: ct);
        }
    }


        static string FullName(User u) => $"{u.FirstName} {u.LastName}".Trim();

                    static string FormatRemaining(long ms)
    {
        var t = TimeSpan.FromMilliseconds(ms);
        int h = (int)t.TotalHours;
        int m = t.Minutes;
        if (h > 0 && m > 0) return $"{h} ساعت و {m} دقیقه";
        if (h > 0) return $"{h} ساعت";
        if (m > 0) return $"{m} دقیقه";
        return "کمتر از یک دقیقه";
    }

    static string FormatTime(long unixMs)
    {
        try { return DateTimeOffset.FromUnixTimeMilliseconds(unixMs).ToOffset(TehranOffset).ToString("HH:mm"); }
        catch { return "نامشخص"; }
    }

    static string HtmlText(string? text) =>
        (text ?? "")
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");

    static string HtmlTag(string? name, long uid)
    {
        string clean = HtmlText(string.IsNullOrEmpty(name) ? $"کاربر {uid}" : name);
        return $"<a href=\"tg://user?id={uid}\">{clean}</a>";
    }

    static async Task RunAssetUpdate(bool force = false)
    {
        if (Volatile.Read(ref databaseMaintenanceRunning) != 0)
            return;
        if (Interlocked.Exchange(ref assetUpdateRunning, 1) == 1)
        {
            Console.WriteLine("[TIMER] skipped: previous run still in progress");
            return;
        }
        try
        {
            if (!force && (DateTime.UtcNow - lastAssetRunUtc).TotalSeconds < 30)
            {
                Console.WriteLine("[TIMER] skipped: ran too recently");
                return;
            }
            lastAssetRunUtc = DateTime.UtcNow;
            await RunAssetUpdateCore();
        }
        finally
        {
            Interlocked.Exchange(ref assetUpdateRunning, 0);
        }
    }

    static string GetResName(string resType) => resType switch
    {
        "money" => "دلار (پول)",
        "iron" => "تن آهن",
        "soldiers" => "سرباز",
        "tanks" => "دستگاه تانک",
        "planes" => "فروند جنگنده",
        "bombers" => "فروند بمب‌افکن",
        "boats" => "قایق تندرو",
        "submarines" => "زیردریایی",
        "battleships" => "نبردناو",
        _ => resType
    };

    static List<(string ModelName, long Count)> BuildCappedEquipmentBreakdown(
        Country c,
        string category,
        string defaultModel,
        long total)
    {
        if (total <= 0)
            return new List<(string, long)>();

        var reservedModels = Database.GetReservedEquipmentModels(c.OwnerId, c.ChatId, category);
        var explicitModels = Database.GetEquipmentModels(c.OwnerId, c.ChatId, category)
            .Where(x => x.Count > 0 && !string.IsNullOrWhiteSpace(x.ModelName))
            .GroupBy(x => x.ModelName, StringComparer.Ordinal)
            .Select(g => (ModelName: g.Key,
                Count: Math.Max(0, g.Sum(x => x.Count) - reservedModels.GetValueOrDefault(g.Key))))
            .Where(x => x.Count > 0)
            .ToList();

        long explicitTotal = explicitModels.Sum(x => x.Count);
        var result = new List<(string ModelName, long Count)>();

        if (explicitTotal <= total)
        {
            // Older countries may have an implicit domestic-model balance that was never
            // written to EquipmentModels. Keep that balance as the faction's default model.
            long implicitDefault = total - explicitTotal;
            long storedDefault = explicitModels
                .Where(x => x.ModelName == defaultModel)
                .Sum(x => x.Count);
            long defaultCount = implicitDefault + storedDefault;
            if (defaultCount > 0)
                result.Add((defaultModel, defaultCount));

            foreach (var model in explicitModels.Where(x => x.ModelName != defaultModel))
                result.Add(model);

            return result;
        }

        // The model ledger can be older than the aggregate country balance (for example
        // after an old deployment or battle). Scale it to the real aggregate total so the
        // UI can never offer more units than the country actually owns.
        var scaled = explicitModels
            .Select((model, index) =>
            {
                decimal exact = (decimal)model.Count * total / explicitTotal;
                long count = (long)decimal.Floor(exact);
                return new
                {
                    model.ModelName,
                    Count = count,
                    Fraction = exact - count,
                    Index = index
                };
            })
            .ToList();

        long remaining = total - scaled.Sum(x => x.Count);
        var extraIndexes = scaled
            .OrderByDescending(x => x.Fraction)
            .ThenBy(x => x.Index)
            .Take((int)Math.Min(remaining, scaled.Count))
            .Select(x => x.Index)
            .ToHashSet();

        var normalized = scaled
            .Select(x => (x.ModelName, Count: x.Count + (extraIndexes.Contains(x.Index) ? 1L : 0L)))
            .Where(x => x.Count > 0)
            .ToList();

        var normalizedDefault = normalized.FirstOrDefault(x => x.ModelName == defaultModel);
        if (normalizedDefault.Count > 0)
            result.Add(normalizedDefault);
        result.AddRange(normalized.Where(x => x.ModelName != defaultModel));
        return result;
    }

    static List<(string ModelName, long Count)> GetTransferBreakdown(Country c, string resType)
    {
        if (c == null)
            return new List<(string, long)>();

        long scalarTotal = resType switch
        {
            "money" => c.Money,
            "iron" => c.Iron,
            "soldiers" => c.Soldiers,
            _ => 0
        };
        if (resType is "money" or "iron" or "soldiers")
            return scalarTotal > 0
                ? new List<(string, long)> { ("", scalarTotal) }
                : new List<(string, long)>();

        var equipment = resType switch
        {
            "tanks" => (Category: "Tanks", DefaultModel: Database.GetDefaultTankModel(c.Faction), Total: c.Tanks),
            "planes" => (Category: "Planes", DefaultModel: Database.GetDefaultPlaneModel(c.Faction), Total: c.Planes),
            "bombers" => (Category: "Bombers", DefaultModel: Database.GetDefaultBomberModel(c.Faction), Total: c.Bombers),
            "boats" => (Category: "Boats", DefaultModel: Database.GetDefaultBoatModel(c.Faction), Total: c.Boats),
            "submarines" => (Category: "Submarines", DefaultModel: Database.GetDefaultSubModel(c.Faction), Total: c.Submarines),
            "battleships" => (Category: "Battleships", DefaultModel: Database.GetDefaultBattleshipModel(c.Faction), Total: c.Battleships),
            _ => (Category: "", DefaultModel: "", Total: 0L)
        };

        if (string.IsNullOrEmpty(equipment.Category))
            return new List<(string, long)>();

        return BuildCappedEquipmentBreakdown(
            c,
            equipment.Category,
            equipment.DefaultModel,
            equipment.Total);
    }

    static List<(string ModelName, long Count)> GetTransferSelectionBreakdown(Country c, string resType)
    {
        if (resType is not ("boats" or "submarines" or "battleships"))
            return GetTransferBreakdown(c, resType);
        if (resType == "battleships") Database.SyncBattleshipUnits(c.OwnerId, c.ChatId);
        return Database.GetNavalTransferableModels(c, resType)
            .Select(x => (ModelName: x.Model, x.Count)).ToList();
    }

    static long[] AllocateModelPriority(IReadOnlyList<(string ModelName, long Count)> models,
        string defaultModel, long requested)
    {
        var allocated = new long[models.Count];
        long remaining = Math.Min(Math.Max(0, requested), models.Sum(x => Math.Max(0, x.Count)));
        foreach (int i in Enumerable.Range(0, models.Count)
                     .OrderBy(i => models[i].ModelName.Equals(defaultModel, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                     .ThenBy(i => i))
        {
            long take = Math.Min(remaining, Math.Max(0, models[i].Count));
            allocated[i] = take;
            remaining -= take;
            if (remaining == 0) break;
        }
        return allocated;
    }

    static List<(string ModelName, long Count, long DefenseCount, long MinimumCount)>
        GetExactDefenseBreakdown(Country c, string resType)
    {
        var models = GetTransferBreakdown(c, resType);
        if (models.Count == 0)
            return new List<(string, long, long, long)>();
        string category = resType == "tanks" ? "Tanks" : "Planes";
        string defaultModel = resType == "tanks"
            ? Database.GetDefaultTankModel(c.Faction)
            : Database.GetDefaultPlaneModel(c.Faction);
        long total = models.Sum(x => x.Count);
        long mandatoryTotal = (long)Math.Ceiling(total * 0.20);
        long[] minimums = AllocateModelPriority(models, defaultModel, mandatoryTotal);
        var saved = Database.GetDefenseModelAmounts(c.OwnerId, c.ChatId, category);
        var selected = models.Select(x => Math.Min(x.Count, saved.GetValueOrDefault(x.ModelName))).ToArray();

        // Exact rows mean the player configured this category. Without them, old
        // DefenseTanks/DefenseFighters values are ambiguous legacy defaults (often 100%),
        // so use only the compulsory 20% with domestic-model priority.
        if (saved.Count == 0)
            selected = AllocateModelPriority(models, defaultModel, mandatoryTotal);
        else if (selected.Sum() < mandatoryTotal)
        {
            // A stale/invalid setup is repaired deterministically: domestic factory model first,
            // then foreign models in inventory order until the compulsory 20% is reached.
            selected = AllocateModelPriority(models, defaultModel, mandatoryTotal);
        }

        for (int i = 0; i < selected.Length; i++)
            selected[i] = Math.Clamp(selected[i], minimums[i], models[i].Count);
        return models.Select((x, i) =>
            (x.ModelName, x.Count, DefenseCount: selected[i], MinimumCount: minimums[i])).ToList();
    }

    internal static long GetAttackAvailableSoldiers(Country c)
    {
        int percent=Database.IsDefenseSoldierConfigured(c.OwnerId,c.ChatId)
            ? Math.Clamp(c.DefSoldierPct,20,100) : 20;
        long reserved=Math.Clamp((long)Math.Ceiling(c.Soldiers*(percent/100.0)),0,c.Soldiers);
        return Math.Max(0,c.Soldiers-reserved);
    }

    internal static List<(string ModelName, long Count)> GetAttackBreakdown(Country c, string resType)
    {
        var inventory = GetTransferBreakdown(c, resType);
        if (resType is not ("tanks" or "planes")) return inventory;
        var defense = GetExactDefenseBreakdown(c, resType)
            .ToDictionary(x => x.ModelName, x => x.DefenseCount, StringComparer.OrdinalIgnoreCase);
        return inventory.Select(x =>
                (x.ModelName, Count: Math.Max(0, x.Count - defense.GetValueOrDefault(x.ModelName))))
            .Where(x => x.Count > 0).ToList();
    }

    static List<(string ModelName, long Count, int DefPct)> GetDefenseBreakdown(Country c, string resType)
    {
        var transferBreakdown = GetTransferBreakdown(c, resType);
        string category = resType switch { "tanks" => "Tanks", "planes" => "Planes", "bombers" => "Bombers", "boats" => "Boats", "submarines" => "Submarines", "battleships" => "Battleships", _ => "" };
        var defenseMap = new Dictionary<string, int>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(category))
        {
            var defModels = Database.GetDefenseModels(c.OwnerId, c.ChatId, category);
            foreach (var dm in defModels)
                defenseMap[dm.ModelName] = dm.DefPct;
        }
        var result = new List<(string ModelName, long Count, int DefPct)>();
        foreach (var (model, count) in transferBreakdown)
        {
            int pct = 100;
            if (defenseMap.TryGetValue(model, out int saved)) pct = saved;
            else if (resType == "tanks" && c.DefTankPct > 0) pct = c.DefTankPct;
            else if (resType == "planes" && c.DefFighterPct > 0) pct = c.DefFighterPct;
            else if (resType == "boats" && c.DefTankPct > 0) pct = c.DefTankPct; // reuse tank pct for boats fallback, or 100
            else if (resType == "submarines" && c.DefTankPct > 0) pct = c.DefTankPct;
            else if (resType == "soldiers" && c.DefSoldierPct > 0) pct = c.DefSoldierPct;
            result.Add((model, count, Math.Clamp(pct, 20, 100)));
        }
        // If no breakdown but total exists (e.g., soldiers, boats), ensure at least one entry
        if (result.Count == 0)
        {
            long total = resType switch { "soldiers" => c.Soldiers, "boats" => c.Boats, "submarines" => c.Submarines, "battleships" => c.Battleships, _ => 0 };
            if (total > 0)
            {
                int pct = resType == "soldiers" ? c.DefSoldierPct : 100;
                result.Add(("", total, pct));
            }
        }
        return result;
    }

    static async Task ProcessActiveTransfers(CancellationToken ct)
    {
        await transferProcessorLock.WaitAsync(ct);
        try
        {
            if (Volatile.Read(ref databaseMaintenanceRunning) == 0)
                await ProcessActiveTransfersCore(ct);
        }
        finally
        {
            transferProcessorLock.Release();
        }
    }

    static async Task ProcessActiveTransfersCore(CancellationToken ct)
    {
        var transfers = Database.GetActiveTransfers();
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var t in transfers)
        {
            if(!Database.IsBotGroupActive(t.ChatId))continue;
            var receiver = Database.GetCountry(t.ReceiverId, t.ChatId);
            var sender = Database.GetCountry(t.SenderId, t.ChatId);
            string sName = sender?.OwnerName ?? $"کاربر {t.SenderId}";
            string rName = receiver?.OwnerName ?? $"کاربر {t.ReceiverId}";
            string rn = GetResName(t.ResourceType);
            if (t.ArriveAtMs <= now)
            {
                var mutationLocks = await AcquireCountryMutationLocks(
                    t.ChatId,
                    new[] { t.SenderId, t.ReceiverId },
                    ct);
                try
                {
                Faction modelFaction = sender?.Faction ?? receiver?.Faction ?? Faction.USA;
                string resolvedModel = !string.IsNullOrWhiteSpace(t.ModelName)
                    ? t.ModelName
                    : t.ResourceType switch
                    {
                        "tanks" => Database.GetDefaultTankModel(modelFaction),
                        "planes" => Database.GetDefaultPlaneModel(modelFaction),
                        "bombers" => Database.GetDefaultBomberModel(modelFaction),
                        "boats" => Database.GetDefaultBoatModel(modelFaction),
                        "submarines" => Database.GetDefaultSubModel(modelFaction),
                        "battleships" => Database.GetDefaultBattleshipModel(modelFaction),
                        _ => ""
                    };

                string outcome = Database.CompleteTransfer(t, resolvedModel);
                if (t.ResourceType == "battleships" && (outcome is "delivered" or "capacity" or "returned"))
                {
                    long unitOwner = outcome == "delivered" ? t.ReceiverId : t.SenderId;
                    Database.SyncBattleshipUnits(unitOwner, t.ChatId);
                }
                string modelInfo = string.IsNullOrWhiteSpace(t.ModelName) ? "" : $" ({t.ModelName})";
                if (outcome == "delivered")
                {
                    Database.ReconcileDefense(t.ReceiverId, t.ChatId);
                    try { await bot.SendTextMessageAsync(t.ReceiverId, $"📦 محموله رسید!\n{t.Amount:N0} {rn}{modelInfo} از {sName}", cancellationToken: ct); } catch { }
                    try { await bot.SendTextMessageAsync(t.SenderId, $"✅ محموله به {rName} تحویل شد.", cancellationToken: ct); } catch { }
                }
                else if (outcome == "capacity")
                {
                    Database.ReconcileDefense(t.SenderId, t.ChatId);
                    try { await bot.SendTextMessageAsync(t.SenderId, $"❌ ترنسفر نبردناو به {rName} ناموفق بود؛ ظرفیت گیرنده حداکثر ۳ نبردناو است و محموله برگشت خورد.", cancellationToken: ct); } catch { }
                }
                else if (outcome == "returned")
                {
                    Database.ReconcileDefense(t.SenderId, t.ChatId);
                    try { await bot.SendTextMessageAsync(t.SenderId, $"↩️ محموله برگشت خورد! گیرنده کشورش را از دست داده بود. {t.Amount:N0} {rn} به انبارت برگشت.", cancellationToken: ct); } catch { }
                }
                }
                finally
                {
                    ReleaseCountryMutationLocks(mutationLocks);
                }
            }
            else if ((t.ArriveAtMs - now) <= 5 * 60 * 1000 && t.Notified == 0)
            {
                Database.UpdateTransferNotified(t.Id, 1);
                try { await bot.SendTextMessageAsync(t.ReceiverId, $"⏳ محموله از {sName} تا ۵ دقیقه دیگر!", cancellationToken: ct); } catch { }
            }
        }
    }
    static async Task ProcessActiveDeployments(CancellationToken ct)
    {
        await deploymentProcessorLock.WaitAsync(ct);
        try
        {
            if (Volatile.Read(ref databaseMaintenanceRunning) == 0)
                await ProcessActiveDeploymentsCore(ct);
        }
        finally
        {
            deploymentProcessorLock.Release();
        }
    }

    static async Task ProcessActiveDeploymentsCore(CancellationToken ct)
    {
        var deployments = Database.GetActiveDeployments();
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var d in deployments)
        {
            if(!Database.IsBotGroupActive(d.ChatId))continue;
            var alliance = Database.GetAllianceById(d.AllianceId);
            string aName = alliance?.Name ?? "اتحاد";
            var tc = Database.GetCountry(d.TargetUserId, d.ChatId);
            string tName = tc?.Name ?? $"کاربر {d.TargetUserId}";
            if (d.EndAtMs <= now)
            {
                var participantIds = Database.GetDeploymentContributors(d.Id)
                    .Select(x => x.UserId)
                    .Append(d.InitiatorId)
                    .Append(d.TargetUserId)
                    .ToList();
                if (d.Type == "Offensive")
                {
                    var defensiveIds = deployments
                        .Where(x => x.ChatId == d.ChatId && x.Type == "Defensive" &&
                                    x.TargetUserId == d.TargetUserId && x.EndAtMs > now)
                        .SelectMany(x => Database.GetDeploymentContributors(x.Id))
                        .Select(x => x.UserId);
                    participantIds.AddRange(defensiveIds);
                }
                var mutationLocks = await AcquireCountryMutationLocks(d.ChatId, participantIds, ct);
                try
                {
                string gTitle = $"گروه {d.ChatId}";
                try { var ch = await bot.GetChatAsync(d.ChatId, ct); if (!string.IsNullOrEmpty(ch.Title)) gTitle = ch.Title; } catch { }
                if (d.Type == "Offensive")
                {
                    tc = Database.GetCountry(d.TargetUserId, d.ChatId);
                    if (tc == null)
                    {
                        if (!Database.CancelDeploymentForces(d))
                            throw new InvalidOperationException("Failed to return deployment forces after target deletion.");
                        await UnpinAndDeleteAnnounce(d.ChatId, d.AnnounceMsgId, ct);
                        try { await SendPermanent(d.ChatId, "❌ هدف صف‌آرایی وجود ندارد؛ نیروها بازگشتند.", ct: ct); } catch { }
                        continue;
                    }
                    if (await ProcessOffensiveDeploymentBattle(d, tc, ct))
                        await UnpinAndDeleteAnnounce(d.ChatId, d.AnnounceMsgId, ct);
                }
                else
                {
                    //  – defensive troops no longer in target assets, just return to contributors
                    // DeploymentContributors is the authoritative force ledger. Never scale returns
                    // from the cached totals on Deployments: an old/stale aggregate could otherwise
                    // return fewer units and make the remainder appear to vanish.
                    var defC = Database.GetDeploymentContributors(d.Id);
                    var returns = defC.GroupBy(x => x.UserId)
                        .Select(g => (
                            UserId: g.Key,
                            Tanks: g.Sum(x => Math.Max(0, x.Tanks)),
                            Soldiers: g.Sum(x => Math.Max(0, x.Soldiers)),
                            Fighters: g.Sum(x => Math.Max(0, x.Fighters)),
                            Bombers: g.Sum(x => Math.Max(0, x.Bombers))))
                        .ToList();
                    if (!Database.ReturnDeploymentForcesAndDelete(d.Id, d.ChatId, returns))
                        throw new InvalidOperationException("Defensive deployment return ledger validation failed.");
                    foreach (long contributorId in returns.Select(x => x.UserId).Distinct())
                        Database.ReconcileDefense(contributorId, d.ChatId);
                    await UnpinAndDeleteAnnounce(d.ChatId, d.AnnounceMsgId, ct);
                    try { await SendPermanent(d.ChatId, $"🛡 پایان دفاع اتحاد «{aName}» از «{tName}»", ct: ct); } catch { }
                }
                }
                finally
                {
                    ReleaseCountryMutationLocks(mutationLocks);
                }
            }
            else if (d.Type == "Offensive" && (now - d.LastWarnMs) >= 30 * 60 * 1000 && d.LastWarnMs > 0)
            {
                Database.UpdateDeploymentWarnMs(d.Id, now);
                try { await bot.SendTextMessageAsync(d.TargetUserId, $"⚠️ هشدار: صف‌آرایی «{aName}» علیه شما — {FormatRemaining(d.EndAtMs - now)} دیگر", cancellationToken: ct); } catch { }
            }
        }
    }

    static async Task<bool> ProcessOffensiveDeploymentBattle(Deployment deployment, Country defender, CancellationToken ct)
    {
        var attackerParticipants = BuildDeploymentParticipants(new List<Deployment> { deployment }, deployment.ChatId);
        if (attackerParticipants.Sum(x => x.Soldiers + x.Tanks.Sum(t => t.Count)) <= 0)
        {
            if (!Database.CancelDeploymentForces(deployment))
                throw new InvalidOperationException("Failed to return non-combat deployment forces.");
            return true;
        }

        var ownDefense = BuildOwnDefenseParticipant(defender);
        var defensiveDeployments = Database.GetActiveDeployments()
            .Where(d => d.ChatId == deployment.ChatId && d.Type == "Defensive" &&
                        d.TargetUserId == defender.OwnerId && d.EndAtMs > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            .ToList();
        var defenderDeployments = BuildDeploymentParticipants(defensiveDeployments, deployment.ChatId);
        var defenders = new List<BattleParticipant> { ownDefense };
        defenders.AddRange(defenderDeployments);
        var request = new BattleRequest
        {
            BattleId = deployment.Id,
            ChatId = deployment.ChatId,
            ScenarioSeed = WarEngine.CreateScenarioSeed(),
            Attackers = attackerParticipants,
            Defenders = defenders,
            AttackerOrders = new BattleOrders
            {
                GroundStrategy = deployment.Strategy,
                GroundTactic = deployment.Tactic,
                AirStrategy = attackerParticipants.Sum(x => x.Fighters.Sum(f => f.Count) + x.Bombers.Sum(b => b.Count)) > 0 ? 1 : 0,
                AirTactic = 1
            },
            DefenderOrders = new BattleOrders
            {
                GroundStrategy = defender.DefenseStrategy,
                GroundTactic = defender.DefenseTactic,
                AirStrategy = defender.AirDefStrategy,
                AirTactic = defender.AirDefTactic
            }
        };

        BattleResult result;
        try
        {
            var context = new BattleJobContext
            {
                AttackerId = deployment.InitiatorId,
                DefenderId = defender.OwnerId,
                ChatId = deployment.ChatId,
                DeploymentId = deployment.Id,
                DefensiveDeploymentIds = defensiveDeployments.Select(x => x.Id).ToList()
            };
            var persisted = Database.EnsureBattleJob(request.BattleId, "Deployment",
                JsonSerializer.Serialize(request, BattleJsonOptions),
                JsonSerializer.Serialize(context, BattleJsonOptions));
            request = JsonSerializer.Deserialize<BattleRequest>(persisted.RequestJson, BattleJsonOptions)
                ?? throw new InvalidOperationException("Persisted deployment battle request is invalid.");
            if (!string.IsNullOrWhiteSpace(persisted.ResultJson))
                result = JsonSerializer.Deserialize<BattleResult>(persisted.ResultJson, BattleJsonOptions)
                    ?? throw new InvalidOperationException("Persisted deployment battle result is invalid.");
            else
            {
                Database.UpdateBattleJob(request.BattleId, "Running");
                result = await BattleExecutionScheduler.EnqueueAsync(request, ct);
                Database.UpdateBattleJob(request.BattleId, "Resolved",
                    JsonSerializer.Serialize(result, BattleJsonOptions));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEPLOYMENT BATTLE ENGINE ERR] {ex}");
            try { Database.UpdateBattleJob(request.BattleId, "Pending", error: ex.Message); } catch { }
            try { await SendPermanent(deployment.ChatId, "❌ پردازش صف‌آرایی ناموفق بود؛ نیروها و رکورد عملیات محفوظ ماندند.", ct: ct); } catch { }
            return false;
        }

        var returns = new List<(long UserId, long Tanks, long Soldiers, long Fighters, long Bombers)>();
        var attackerEquipmentLosses = new List<(Country Country, ParticipantBattleLoss Loss)>();
        foreach (var participant in attackerParticipants)
        {
            result.AttackerParticipantLosses.TryGetValue(participant.OwnerId, out var loss);
            long tankLoss = loss?.TanksUnavailable.Values.Sum() ?? 0;
            long fighterLoss = loss?.FightersUnavailable.Values.Sum() ?? 0;
            long bomberLoss = loss?.BombersUnavailable.Values.Sum() ?? 0;
            returns.Add((participant.OwnerId,
                Math.Max(0, participant.Tanks.Sum(x => x.Count) - tankLoss),
                Math.Max(0, participant.Soldiers - (loss?.SoldiersUnavailable ?? 0)),
                Math.Max(0, participant.Fighters.Sum(x => x.Count) - fighterLoss),
                Math.Max(0, participant.Bombers.Sum(x => x.Count) - bomberLoss)));
            var country = Database.GetCountry(participant.OwnerId, deployment.ChatId);
            if (country != null && loss != null)
                attackerEquipmentLosses.Add((country, loss));
        }

        if (!Database.ReturnDeploymentForcesAndDelete(deployment.Id, deployment.ChatId, returns,
                allowBattleLosses: true))
            throw new InvalidOperationException("Deployment settlement ledger validation failed or was already completed.");
        foreach (var item in attackerEquipmentLosses)
        {
            DeductEquipmentLosses(item.Country, "Tanks", item.Loss.TanksUnavailable, WarEngine.CanonicalTankModel);
            DeductEquipmentLosses(item.Country, "Planes", item.Loss.FightersUnavailable, WarEngine.CanonicalFighterModel);
            DeductEquipmentLosses(item.Country, "Bombers", item.Loss.BombersUnavailable, WarEngine.CanonicalBomberModel);
        }
        try { Database.SaveBattleResult(request, result); }
        catch (Exception historyError) { Console.WriteLine($"[BATTLE HISTORY ERR] {historyError}"); }
        foreach (long ownerId in returns.Select(x => x.UserId).Distinct())
            Database.ReconcileDefense(ownerId, deployment.ChatId);

        ApplyDefenderBattleLosses(defender, ownDefense, defenderDeployments,
            defensiveDeployments, result);
        // غنیمت جنگی در نبرد صف‌آرایی: به کشور آغازگر حمله می‌رسد و از خزانه مدافع کم می‌شود
        if (result.AttackerWon)
        {
            var initiator = Database.GetCountry(deployment.InitiatorId, deployment.ChatId);
            if (initiator != null)
            {
                initiator.Money = Math.Max(0, initiator.Money + result.AttackerMoneyGained);
                initiator.Iron = Math.Max(0, initiator.Iron + result.AttackerIronGained);
                Database.UpdateCountryFull(initiator);
            }
            defender.Money = Math.Max(0, defender.Money - result.DefenderMoneyLost);
            defender.Iron = Math.Max(0, defender.Iron - result.DefenderIronLost);
        }
        Database.UpdateCountryFull(defender);
        Database.ReconcileDefense(defender.OwnerId, defender.ChatId);

        foreach (long ownerId in attackerParticipants.Select(x => x.OwnerId).Distinct())
        {
            try { await SendPermanent(ownerId, result.AttackerReport, ct: ct); } catch { }
        }
        try { await SendPermanent(defender.OwnerId, result.DefenderReport, ct: ct); } catch { }
        try { await SendPermanent(deployment.ChatId, result.GroupAnnouncement, ct: ct); } catch { }
        await ProcessStrategicBattleOutcome(deployment.InitiatorId, defender.OwnerId, deployment.ChatId, result, ct);
        Database.UpdateBattleJob(request.BattleId, "Completed",
            JsonSerializer.Serialize(result, BattleJsonOptions));
        return true;
    }

    static void ApplyDefenderBattleLosses(Country defender, BattleParticipant ownDefense,
        List<BattleParticipant> deploymentParticipants, List<Deployment> defensiveDeployments,
        BattleResult result)
    {
        foreach (var (ownerId, loss) in result.DefenderParticipantLosses)
        {
            var deployed = deploymentParticipants.Where(x => x.OwnerId == ownerId).ToList();
            long deployedSoldiers = deployed.Sum(x => x.Soldiers);
            long deployedTanks = deployed.Sum(x => x.Tanks.Sum(m => m.Count));
            long deployedFighters = deployed.Sum(x => x.Fighters.Sum(m => m.Count));
            long deployedBombers = deployed.Sum(x => x.Bombers.Sum(m => m.Count));
            long ownSoldiers = ownerId == defender.OwnerId ? ownDefense.Soldiers : 0;
            long ownTanks = ownerId == defender.OwnerId ? ownDefense.Tanks.Sum(x => x.Count) : 0;
            long ownFighters = ownerId == defender.OwnerId ? ownDefense.Fighters.Sum(x => x.Count) : 0;
            long totalTankLoss = loss.TanksUnavailable.Values.Sum();
            long totalFighterLoss = loss.FightersUnavailable.Values.Sum();
            long totalBomberLoss = loss.BombersUnavailable.Values.Sum();
            long ownSLoss = ProportionalShare(loss.SoldiersUnavailable, ownSoldiers, deployedSoldiers);
            long ownTLoss = ProportionalShare(totalTankLoss, ownTanks, deployedTanks);
            long ownFLoss = ProportionalShare(totalFighterLoss, ownFighters, deployedFighters);

            if (ownerId == defender.OwnerId)
            {
                defender.Soldiers = Math.Max(0, defender.Soldiers - ownSLoss);
                defender.Tanks = Math.Max(0, defender.Tanks - ownTLoss);
                defender.Planes = Math.Max(0, defender.Planes - ownFLoss);
                defender.AntiAir = Math.Max(0, defender.AntiAir - loss.AntiAirLost);
            }
            Database.ApplyDefensiveDeploymentLosses(defender.ChatId, defender.OwnerId, ownerId,
                totalTankLoss - ownTLoss,
                loss.SoldiersUnavailable - ownSLoss,
                totalFighterLoss - ownFLoss,
                totalBomberLoss,
                defensiveDeployments.Select(x => x.Id).ToArray());
            var ownerCountry = Database.GetCountry(ownerId, defender.ChatId);
            if (ownerCountry != null)
            {
                DeductEquipmentLosses(ownerCountry, "Tanks", loss.TanksUnavailable, WarEngine.CanonicalTankModel);
                DeductEquipmentLosses(ownerCountry, "Planes", loss.FightersUnavailable, WarEngine.CanonicalFighterModel);
                DeductEquipmentLosses(ownerCountry, "Bombers", loss.BombersUnavailable, WarEngine.CanonicalBomberModel);
            }
        }
    }

    static async Task RefreshDeploymentAnnouncement(long depId, CancellationToken ct = default)
    {
        try
        {
            var dep = Database.GetDeploymentById(depId);
            if (dep == null || dep.AnnounceMsgId == 0) return;
            var alliance = Database.GetAllianceById(dep.AllianceId);
            string allyName = alliance?.Name ?? "اتحاد";
            var targetCountry = Database.GetCountry(dep.TargetUserId, dep.ChatId);
            string tName = targetCountry?.Name ?? $"کاربر {dep.TargetUserId}";
            string targetTag = targetCountry != null ? HtmlTag(targetCountry.OwnerName, targetCountry.OwnerId) : $"کاربر {dep.TargetUserId}";

            var contribs = Database.GetDeploymentContributors(depId);
            var participantTags = new List<string>();
            foreach (var cbn in contribs)
            {
                var cc = Database.GetCountry(cbn.UserId, dep.ChatId);
                if (cc != null) participantTags.Add(HtmlTag(cc.OwnerName, cc.OwnerId));
                else participantTags.Add($"<a href=\"tg://user?id={cbn.UserId}\">کاربر {cbn.UserId}</a>");
            }
            string tags = string.Join(" ", participantTags.Distinct());

            bool isOff = dep.Type == "Offensive";
            long endMs = dep.EndAtMs;
            string bText = isOff ?
                $"🚨 <b>اعلان جنگ و صف‌آرایی تهاجمی!</b> ⚔️\n\n👑 اتحاد <b>«{HtmlText(allyName)}»</b> علیه کشور <b>«{HtmlText(tName)}»</b> (مالک: {targetTag}) صف‌آرایی کرد!\n⏱ مدت: <b>{dep.DurationHours} ساعت</b> (پایان: {FormatTime(endMs)})\n\n💥 <b>نیروهای فعلی:</b>\n🪖 سرباز: {dep.Soldiers:N0} | 🛡 تانک: {dep.Tanks:N0}\n✈️ جنگنده: {dep.Fighters:N0} | 🛩 بمب‌افکن: {dep.Bombers:N0}\n\n👥 مشارکت‌کنندگان ({contribs.Count} نفر):\n{tags}\n\n🎯 استراتژی: {dep.Strategy} | تاکتیک: {dep.Tactic}" :
                $"🛡 <b>اعلام صف‌آرایی دفاعی!</b> 🏰\n\n👑 اتحاد <b>«{HtmlText(allyName)}»</b> برای حمایت از کشور <b>«{HtmlText(tName)}»</b> (مالک: {targetTag}) خط پدافندی تشکیل داد!\n⏱ مدت: <b>{dep.DurationHours} ساعت</b> (پایان: {FormatTime(endMs)})\n\n🛡 <b>نیروهای پشتیبان فعلی:</b>\n🪖 سرباز: {dep.Soldiers:N0} | 🛡 تانک: {dep.Tanks:N0}\n✈️ جنگنده: {dep.Fighters:N0} | 🛩 بمب‌افکن: {dep.Bombers:N0}\n\n👥 مشارکت‌کنندگان ({contribs.Count} نفر):\n{tags}\n\n🎯 استراتژی: {dep.Strategy} | تاکتیک: {dep.Tactic}";

            var joinKb = new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("⚔️ مشارکت و اعزام نیرو", $"dep_join:{depId}") } });

            // Try edit caption if photo, else text
            try
            {
                await bot.EditMessageCaptionAsync(dep.ChatId, dep.AnnounceMsgId, bText, parseMode: ParseMode.Html, replyMarkup: joinKb, cancellationToken: ct);
            }
            catch
            {
                try { await bot.EditMessageTextAsync(dep.ChatId, dep.AnnounceMsgId, bText, parseMode: ParseMode.Html, replyMarkup: joinKb, cancellationToken: ct); } catch { }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[REFRESH DEP ANNOUNCE ERR] {ex.Message}");
        }
    }

    static async Task RunAssetUpdateCore()
    {
        try { await ProcessActiveTransfers(CancellationToken.None); } catch (Exception ex) { Console.WriteLine($"[Transfers ERR] {ex.Message}"); }
        try { await ProcessActiveDeployments(CancellationToken.None); } catch (Exception ex) { Console.WriteLine($"[Deployments ERR] {ex.Message}"); }
        try { await ProcessNavalInvasions(CancellationToken.None); } catch (Exception ex) { Console.WriteLine($"[NavalInvasions ERR] {ex.Message}"); }
        try { Console.WriteLine($"[SIEGE INTEGRITY] {Database.RepairSiegeIntegrity()}"); } catch(Exception ex) { Console.WriteLine($"[SIEGE INTEGRITY ERR] {ex.Message}"); }
        attackCounts.Clear();
        navalAttackCounts.Clear();
        transferCounts.Clear();
        lastAssetUpdateAt = DateTime.UtcNow;
        var eligibleCountries=Database.GetAllCountries().Where(c=>Database.IsBotGroupActive(c.ChatId)).ToList();
        var countryKeys = eligibleCountries.Select(c => (c.ChatId, c.OwnerId));
        var mutationLocks = await AcquireCountryMutationLocks(countryKeys, CancellationToken.None);
        var countries = new List<Country>();
        try
        {
        countries = eligibleCountries;
        Console.WriteLine($"[TIMER] RunAssetUpdate started at {DateTime.Now} — {countries.Count} countries");
        foreach (var c in countries)
        {
            double sf = SiegeIncomeFactor(c);
            long moneyGain = (long)(CalcBuildingMoney(c) * sf);
            long ironGain = (long)(CalcIronIncome(c) * sf);
            long taxGain = (long)(CalcTaxIncome(c) * sf);
            double birthRate = c.Welfare / 100.0 * 0.05;
            long births = (long)(c.Population * birthRate);
            long newPop = c.Population + births;
            long newSol = c.Soldiers + (long)(births * c.RecruitmentRate / 10.0);
            double newWelfare = NextWelfare(c);
            c.Money += moneyGain + taxGain;
            c.Iron += ironGain;
            c.Population = newPop;
            c.Soldiers = newSol;
            c.Welfare = newWelfare;

            Database.UpdateCountryFull(c);
            Database.ReconcileDefense(c.OwnerId, c.ChatId);
        }
        }
        finally
        {
            ReleaseCountryMutationLocks(mutationLocks);
        }

        string updateCaption =
            "🌅 گزارش روزانهٔ کشورها\n\n" +
            "💰 مالیات و درآمد ساختمان‌ها به خزانه واریز شد\n" +
            "👥 جمعیت بر اساس رفاه رشد کرد\n" +
            "🪖 سربازگیری طبق نرخ انجام شد\n" +
            "🏥 رفاه بر اساس مالیات، سربازگیری و بندر به‌روزرسانی شد\n" +

            "📊 برای مشاهدهٔ جزئیات بنویسید: کشورم";
        var chatIds = countries.Select(x => x.ChatId).Distinct().ToList();
        int sentGroups = 0;
        int failedGroups = 0;
        foreach (var cid in chatIds)
        {
            bool sent = false;
            for (int attempt = 0; attempt < 2 && !sent; attempt++)
            {
                try
                {
                    if (!string.IsNullOrEmpty(SpecialPhotoFileId))
                        await SendPermanentPhoto(cid, SpecialPhotoFileId, updateCaption, ct: CancellationToken.None);
                    else
                        await SendPermanent(cid, updateCaption, ct: CancellationToken.None);
                    sent = true;
                    sentGroups++;
                }
                catch (Telegram.Bot.Exceptions.ApiRequestException apiEx) when (apiEx.ErrorCode == 403)
                {
                    Database.SetBotGroupActive(cid,false);
                    Console.WriteLine($"[BOT GROUP STATUS] chat={cid} inactive after forbidden update send");
                    break;
                }
                catch (Telegram.Bot.Exceptions.ApiRequestException apiEx) when (apiEx.ErrorCode == 429)
                {
                    int waitSec = apiEx.Parameters?.RetryAfter ?? 3;
                    Console.WriteLine($"[UPDATE SEND ERR] chat {cid}: flood control, waiting {waitSec}s");
                    await Task.Delay(waitSec * 1000 + 200);
                }
                catch (Exception ex)
                {
                    if (!string.IsNullOrEmpty(SpecialPhotoFileId))
                    {
                        try
                        {
                            await SendPermanent(cid, updateCaption, ct: CancellationToken.None);
                            sent = true;
                            sentGroups++;
                            Console.WriteLine($"[UPDATE SEND ERR] chat {cid}: photo failed ({ex.Message}), fell back to text");
                        }
                        catch (Exception ex2)
                        {
                            Console.WriteLine($"[UPDATE SEND ERR] chat {cid}: {ex2.Message}");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[UPDATE SEND ERR] chat {cid}: {ex.Message}");
                    }
                    break;
                }
            }
            if (!sent) failedGroups++;
            await Task.Delay(60);
        }
        Console.WriteLine($"[TIMER] Update sent to {sentGroups} groups, failed {failedGroups}");

        string backupPath = $"gamedata_backup_{DateTime.Now:yyyyMMdd_HHmmss}.db";
        try
        {
            Database.CreateConsistentBackup(backupPath);
            using var backupStream = System.IO.File.OpenRead(backupPath);
            await bot.SendDocumentAsync(OWNER_ID,
                new InputOnlineFile(backupStream, System.IO.Path.GetFileName(backupPath)),
                caption: $"📦 بک‌آپ دیتابیس — {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n👥 تعداد کشورها: {countries.Count}",
                cancellationToken: CancellationToken.None);
            Console.WriteLine("[TIMER] DB backup sent to owner");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BACKUP ERR] {ex.Message}");
        }
        finally
        {
            TryDeleteSqliteSidecar(backupPath);
        }
    }

    static void ScheduleNavalArrival(long operationId,long arriveAtMs)
    {
        _=Task.Run(async () =>
        {
            try
            {
                long wait=Math.Max(0,arriveAtMs-DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                if(wait>0)await Task.Delay(TimeSpan.FromMilliseconds(wait));
                Console.WriteLine($"[NAVAL ARRIVAL WAKE] operation={operationId}");
                await ProcessNavalInvasions(CancellationToken.None);
            }
            catch(Exception ex){Console.WriteLine($"[NAVAL ARRIVAL WAKE ERR] operation={operationId} {ex}");}
        });
    }

    static async Task ProcessNavalInvasions(CancellationToken ct)
    {
        await navalProcessorLock.WaitAsync(ct);
        try
        {
            if (Volatile.Read(ref databaseMaintenanceRunning) == 0)
                await ProcessNavalInvasionsCore(ct);
        }
        finally
        {
            navalProcessorLock.Release();
        }
    }

    internal static void RedactDefenderNavalDoctrine(NavalBattleResult result)
    {
        result.AttackerReport=string.Join("\n",(result.AttackerReport??"").Split('\n')
            .Where(line=>!line.TrimStart().StartsWith("🛡 دکترین مدافع:",StringComparison.Ordinal)
                      &&!line.TrimStart().StartsWith("🛡 دفاع دشمن:",StringComparison.Ordinal)));
        result.GroupAnnouncement=string.Join("\n",(result.GroupAnnouncement??"").Split('\n').Select(line=>
        {
            if(line.TrimStart().StartsWith("🎯",StringComparison.Ordinal)&&line.Contains('↔'))
                return line[..line.IndexOf('↔')].TrimEnd();
            return line;
        }));
    }

    static void AppendNavalStrategicProgress(NavalBattleResult result)
    {
        if (result.AttackerReport.Contains("پیشرفت تخریب بندر", StringComparison.Ordinal) ||
            result.AttackerReport.Contains("سومین پیروزی این مهاجم", StringComparison.Ordinal)) return;
        if (result.PortLevelDamaged)
        {
            string portNews = "\n⚓ پس از سومین پیروزی این مهاجم، بندر مدافع یک سطح تخریب شد.";
            result.AttackerReport += portNews;
            result.DefenderReport += portNews;
            result.GroupAnnouncement += portNews;
        }
        else if (result.AttackerWon && !result.EmptyBase)
            result.AttackerReport += $"\n📈 پیشرفت تخریب بندر برابر این مدافع: {result.RivalryWinsAfter}/3";
    }

    static async Task ProcessNavalInvasionsCore(CancellationToken ct)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var inv in Database.GetPendingNavalInvasions(now))
        {
            ct.ThrowIfCancellationRequested();
            if(!Database.IsBotGroupActive(inv.ChatId))continue;
            Console.WriteLine($"[NAVAL RESOLUTION START] operation={inv.Id} status={inv.Status} due={inv.ArriveAtMs} now={now}");
            var locks = await AcquireCountryMutationLocks(inv.ChatId,
                new[] { inv.AttackerId, inv.DefenderId }, ct);
            try
            {
                if (inv.Status.Equals("Settled", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(inv.ResultJson))
                {
                    var recovered = JsonSerializer.Deserialize<NavalBattleResult>(inv.ResultJson);
                    if (recovered == null) throw new InvalidOperationException("Stored naval result is invalid.");
                    AppendNavalStrategicProgress(recovered);
                    RedactDefenderNavalDoctrine(recovered);
                    try { await SendPermanent(inv.AttackerId, recovered.AttackerReport, ct: ct); } catch { }
                    try { await SendPermanent(inv.DefenderId, recovered.DefenderReport, ct: ct); } catch { }
                    try { await SendPermanent(inv.ChatId, recovered.GroupAnnouncement, ct: ct); } catch { }
                    Database.MarkNavalInvasionProcessed(inv.Id);
                    continue;
                }
                var attacker = Database.GetCountry(inv.AttackerId, inv.ChatId);
                var defender = Database.GetCountry(inv.DefenderId, inv.ChatId);
                if (attacker == null)
                {
                    Database.MarkNavalInvasionProcessed(inv.Id);
                    continue;
                }
                if (defender == null)
                {
                    if(Database.ReturnNavalOperationWithoutBattle(inv))
                    {
                        try{await SendPermanent(inv.AttackerId,$"↩️ عملیات دریایی #{inv.Id} لغو شد؛ کشور مقصد وجود ندارد و تمام ناوگان بازگشت.",ct:ct);}catch{}
                    }
                    continue;
                }
                Database.SyncBattleshipUnits(defender.OwnerId, defender.ChatId);
                var attackerBoats = Database.DecodeNavalModels(inv.BoatModels);
                var attackerSubs = Database.DecodeNavalModels(inv.SubModels);
                bool harborStrike = inv.Tactic == 1;
                var defenderBoats = harborStrike
                    ? Database.GetEquipmentBreakdownForReconcile(defender, "boats")
                        .Select(x => new NavalModelAmount(x.ModelName, x.Count)).ToList()
                    : Database.GetNavalDefenseModels(defender, "boats");
                var defenderSubs = harborStrike
                    ? Database.GetEquipmentBreakdownForReconcile(defender, "submarines")
                        .Select(x => new NavalModelAmount(x.ModelName, x.Count)).ToList()
                    : Database.GetNavalDefenseModels(defender, "submarines");
                var defenderBs = new List<NavalBattleshipState>();
                if (harborStrike)
                    defenderBs.AddRange(Database.GetBattleshipUnits(defender.OwnerId, defender.ChatId, false));
                else
                {
                    var defenderBsWanted = Database.GetNavalDefenseModels(defender, "battleships")
                        .ToDictionary(x => x.Model, x => x.Count, StringComparer.OrdinalIgnoreCase);
                    foreach (var group in Database.GetBattleshipUnits(defender.OwnerId, defender.ChatId, true)
                                 .GroupBy(x => x.Model, StringComparer.OrdinalIgnoreCase))
                        defenderBs.AddRange(group.Take((int)Math.Min(int.MaxValue,
                            defenderBsWanted.GetValueOrDefault(group.Key))));
                }
                var attackerBs = Database.GetBattleshipUnits(attacker.OwnerId, attacker.ChatId,
                    onlyCombatReady: false, operationId: inv.Id);
                if(attackerBs.Count!=inv.Battleships)
                {
                    Console.WriteLine($"[NAVAL LEDGER FALLBACK] operation={inv.Id} sent={inv.Battleships} linked={attackerBs.Count}; returning fleet safely");
                    Database.ReturnNavalOperationWithoutBattle(inv);
                    try{await SendPermanent(inv.AttackerId,$"↩️ عملیات دریایی #{inv.Id} به‌دلیل ناسازگاری رکورد قدیمی بدون تلفات بازگردانده شد.",ct:ct);}catch{}
                    continue;
                }
                var orders = Database.GetNavalDefenseOrders(defender.OwnerId, defender.ChatId);
                var request = new NavalBattleRequest
                {
                    OperationId = inv.Id,
                    Seed = unchecked((ulong)inv.Id * 0x9E3779B97F4A7C15UL ^ (ulong)inv.CreatedAtMs),
                    AttackerName = attacker.Name,
                    DefenderName = defender.Name,
                    AttackerTactic = inv.Tactic,
                    DefenderStrategy = orders.Strategy,
                    DefenderTactic = orders.Tactic,
                    DefenderPortLevel = defender.PortLevel,
                    DefenderMoney = Math.Max(0, defender.Money),
                    DefenderIron = Math.Max(0, defender.Iron),
                    AttackerBoats = attackerBoats,
                    AttackerSubmarines = attackerSubs,
                    AttackerBattleships = attackerBs,
                    DefenderBoats = defenderBoats,
                    DefenderSubmarines = defenderSubs,
                    DefenderBattleships = defenderBs
                };
                NavalBattleResult result = NavalEngine.Resolve(request);
                if (!Database.SettleNavalOperation(inv, result, attackerBoats, attackerSubs,
                        defenderBoats, defenderSubs)) continue;
                AppendNavalStrategicProgress(result);
                RedactDefenderNavalDoctrine(result);
                try { await SendPermanent(inv.AttackerId, result.AttackerReport, ct: ct); } catch { }
                try { await SendPermanent(inv.DefenderId, result.DefenderReport, ct: ct); } catch { }
                try { await SendPermanent(inv.ChatId, result.GroupAnnouncement, ct: ct); } catch { }
                Database.MarkNavalInvasionProcessed(inv.Id);
                Console.WriteLine($"[NAVAL RESOLUTION COMPLETED] operation={inv.Id} outcome={result.Outcome} success={result.SuccessPercent}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NAVAL RESOLUTION ERR #{inv.Id}] {ex}");
            }
            finally { ReleaseCountryMutationLocks(locks); }
        }
    }

    static async Task HandleRevengeCallback(CallbackQuery cb,string[] parts,CancellationToken ct)
    {
        if(parts.Length<3||cb.Message==null||!TryParseLong(parts[1],out long chatId)||!TryParseLong(parts[2],out long targetId))return;
        long uid=cb.From.Id;
        var attacker=Database.GetCountry(uid,chatId);
        var target=Database.GetCountry(targetId,chatId);
        if(attacker==null||target==null)
        {
            await bot.AnswerCallbackQueryAsync(cb.Id,"❌ یکی از دو کشور دیگر وجود ندارد.",showAlert:true,cancellationToken:ct);
            return;
        }
        long myAlliance=Database.GetUserAllianceId(chatId,uid);
        if(myAlliance>0&&Database.GetUserAllianceId(chatId,targetId)==myAlliance)
        {
            await bot.AnswerCallbackQueryAsync(cb.Id,"❌ اکنون هم‌اتحاد هستید و امکان انتقام وجود ندارد.",showAlert:true,cancellationToken:ct);
            return;
        }
        await HandleAttackTargetCallback(cb,new[]{"attack_target",chatId.ToString(),targetId.ToString()},ct);
    }

    static async Task HandleAttackGroupCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
{
        if (parts.Length < 2 || cb.Message == null) return;
        long uid = cb.From.Id;
        if (!TryParseLong(parts[1], out long cid)) return;
        bool fullExemption = Database.HasGroupLockExemption(cid);
        if (Database.HasAttackAbandonLock(uid) && !fullExemption)
        { await bot.AnswerCallbackQueryAsync(cb.Id, "⛔ شما تا ۳ روز به دلیل بزن‌دررو از حمله قفل هستید.", showAlert: true, cancellationToken: ct); return; }
        var targets = Database.GetAttackableTargets(cid, uid);
        if (targets.Count == 0) { await bot.AnswerCallbackQueryAsync(cb.Id, "هدف نیست.", cancellationToken: ct); return; }
        var kb = targets.Select(t => new[] { InlineKeyboardButton.WithCallbackData($"{t.Name} ({t.OwnerName})", $"attack_target:{cid}:{t.OwnerId}") }).ToArray();
        sessions[uid] = new UserSession { Step = SessionStep.AttackWaitingTarget, AttackChatId = cid };
        await bot.EditMessageTextAsync(cb.Message.Chat.Id, cb.Message.MessageId, "🎯 هدف:", replyMarkup: new InlineKeyboardMarkup(kb), cancellationToken: ct);
        TrackPrompt(uid, cb.Message.Chat.Id, cb.Message.MessageId);
        await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
    }

    static async Task HandleAttackTargetCallback(
        CallbackQuery cb,
        string[] parts,
        CancellationToken ct)
{
        if (parts.Length < 3 || cb.Message == null)
            return;

        long uid = cb.From.Id;

        if (!TryParseLong(parts[1], out long cid) ||
            !TryParseLong(parts[2], out long tid))
            return;
        bool fullExemption = Database.HasGroupLockExemption(cid);
        if (Database.HasAttackAbandonLock(uid) && !fullExemption)
        { await bot.AnswerCallbackQueryAsync(cb.Id, "⛔ شما تا ۳ روز به دلیل بزن‌دررو از حمله قفل هستید.", showAlert: true, cancellationToken: ct); return; }

        var defender = Database.GetCountry(tid, cid);
        var attacker = Database.GetCountry(uid, cid);

        if (defender == null)
        {
            await bot.AnswerCallbackQueryAsync(
                cb.Id,
                "❌ هدف یافت نشد.",
                cancellationToken: ct
            );
            return;
        }

        //  – shield check (5 attacks => 16h shield)
        if (Database.IsAttackShieldActive(tid, cid) && !fullExemption)
        {
            long until = Database.GetAttackShieldUntilMs(tid, cid);
            long leftMs = until - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long leftH = Math.Max(1, leftMs / 3600000);
            await bot.AnswerCallbackQueryAsync(cb.Id, $"🛡 {defender.Name} به دلیل 5 حمله اخیر تا {leftH} ساعت دیگر سپر 16 ساعته دارد و قابل حمله نیست!", showAlert: true, cancellationToken: ct);
            return;
        }

        // The one-quarter power rule is evaluated only after the user chooses
        // «حمله دریایی». Ground/air attacks must never be blocked here.
        var session = sessions.GetOrAdd(
            uid,
            _ => new UserSession()
        );

        session.Step = SessionStep.AttackWaitingAttackType;
        session.AttackChatId = cid;
        session.AttackTargetId = tid;

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    "⚔️ حمله زمینی / هوایی (غیر دریایی)",
                    $"attack_type:{cid}:{tid}:ground"
                )
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    "⚓ حمله دریایی",
                    $"attack_type:{cid}:{tid}:naval"
                )
            }
        });

        string text =
            $"🎯 هدف: {defender.Name}\n\n" +
            "لطفاً نوع حمله را انتخاب کنید:\n\n" +
            "⚔️ غیر دریایی = نبرد زمینی و هوایی\n" +
            "⚓ دریایی = نبرد دریایی (ناوگان)";

        await bot.EditMessageTextAsync(
            cb.Message.Chat.Id,
            cb.Message.MessageId,
            text,
            replyMarkup: keyboard,
            cancellationToken: ct
        );

        TrackPrompt(
            uid,
            cb.Message.Chat.Id,
            cb.Message.MessageId
        );

        await bot.AnswerCallbackQueryAsync(
            cb.Id,
            cancellationToken: ct
        );
    }

    static async Task HandleAttackTypeCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length < 4 || cb.Message == null) return;
        long uid = cb.From.Id;
        if (!TryParseLong(parts[1], out long cid) || !TryParseLong(parts[2], out long tid)) return;
        string type = parts[3]; // ground or naval
        bool fullExemption = Database.HasGroupLockExemption(cid);
        var session = sessions.GetOrAdd(uid, _ => new UserSession());
        session.AttackChatId = cid;
        session.AttackTargetId = tid;
        session.AttackIsNaval = type == "naval";

        if (session.AttackIsNaval)
        {
            // Check power ratios and other naval rules before proceeding
            var attacker = Database.GetCountry(uid, cid);
            var defender = Database.GetCountry(tid, cid);
            if (attacker == null || defender == null)
            {
                await bot.AnswerCallbackQueryAsync(cb.Id, "❌ کشور یافت نشد.", showAlert: true, cancellationToken: ct);
                return;
            }

            // The one-quarter rule belongs exclusively to naval attacks.
            if (!PassesAttackTypePowerRule(attacker,defender,isNaval:true))
            {
                await bot.AnswerCallbackQueryAsync(cb.Id, "⛔ حمله به کشوری با قدرت کمتر از یک چهارم قدرت شما ممنوع است!", showAlert: true, cancellationToken: ct);
                return;
            }

            session.Step = SessionStep.AttackWaitingStrategy;
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("⚔️ نابودی ناوگان اصلی دشمن", $"attack_naval_strategy:{cid}:{tid}:1") },
                new[] { InlineKeyboardButton.WithCallbackData("🔒 استراتژی دوم — به‌زودی", "naval_locked") }
            });
            string text = $"🎯 هدف: {defender.Name}\n\n⚓ **استراتژی حمله دریایی را انتخاب کنید:**\n\n" +
                          "1️⃣ نابودی ناوگان اصلی دشمن\n" +
                          "🔒 استراتژی دوم فعلاً قفل است.";
            await bot.EditMessageTextAsync(cb.Message.Chat.Id, cb.Message.MessageId, text, replyMarkup: keyboard, cancellationToken: ct);
        }
        else
        {
            // Ground attack – shield and power ratio checks ()
            var attacker = Database.GetCountry(uid, cid);
            var defender = Database.GetCountry(tid, cid);
            if (attacker != null && defender != null)
            {
                if (Database.IsAttackShieldActive(tid, cid) && !fullExemption)
                {
                    long until = Database.GetAttackShieldUntilMs(tid, cid);
                    long leftH = Math.Max(1, (until - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) / 3600000);
                    await bot.AnswerCallbackQueryAsync(cb.Id, $"🛡 {defender.Name} سپر 16 ساعته دارد! تا {leftH} ساعت دیگر قابل حمله نیست.", showAlert: true, cancellationToken: ct);
                    return;
                }
            }
            session.Step = SessionStep.AttackWaitingStrategy;
            var def = Database.GetCountry(tid, cid);
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("⚔️ هجوم منسجم", $"attack_strategy:{cid}:{tid}:1") },
                new[] { InlineKeyboardButton.WithCallbackData("⭕ محاصره و ضربه", $"attack_strategy:{cid}:{tid}:2") }
            });
            string text = $"🎯 هدف: {def?.Name ?? "دشمن"}\n\n{GroundAttackStrategyGuide}";
            await bot.EditMessageTextAsync(cb.Message.Chat.Id, cb.Message.MessageId, text, replyMarkup: keyboard, cancellationToken: ct);
        }

        TrackPrompt(uid, cb.Message.Chat.Id, cb.Message.MessageId);
        await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
    }

    static async Task HandleAttackNavalStrategyCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length < 4 || cb.Message == null) return;
        long uid = cb.From.Id;
        if (!TryParseLong(parts[1], out long cid) || !TryParseLong(parts[2], out long tid) || !TryParseInt(parts[3], out int strategy)) return;
        if (strategy != 1)
        {
            await bot.AnswerCallbackQueryAsync(cb.Id, "🔒 این استراتژی فعلاً قفل است.", showAlert: true, cancellationToken: ct);
            return;
        }
        var session = sessions.GetOrAdd(uid, _ => new UserSession());
        session.AttackChatId = cid;
        session.AttackTargetId = tid;
        session.AttackNavalStrategy = strategy;
        session.Step = SessionStep.AttackWaitingTactic; // reuse for naval tactic

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("💥 حمله غافلگیرانه به پایگاه", $"attack_naval_tactic:{cid}:{tid}:1:1") },
            new[] { InlineKeyboardButton.WithCallbackData("⚔️ کشاندن به نبرد تعیین‌کننده", $"attack_naval_tactic:{cid}:{tid}:1:2") }
        });

        string guide = "⚓ استراتژی: نابودی ناوگان اصلی دشمن\n\n" +
            "1️⃣ حمله غافلگیرانه به پایگاه دریایی — تمرکز روی ناوگان مستقر در بندر\n" +
            "2️⃣ کشاندن ناوگان به نبرد تعیین‌کننده — درگیری بزرگ در آب‌های آزاد";

        await bot.EditMessageTextAsync(cb.Message.Chat.Id, cb.Message.MessageId, guide, replyMarkup: keyboard, cancellationToken: ct);
        TrackPrompt(uid, cb.Message.Chat.Id, cb.Message.MessageId);
        await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
    }

    static async Task HandleAttackNavalTacticCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length < 5 || cb.Message == null) return;
        long uid = cb.From.Id;
        if (!TryParseLong(parts[1], out long cid) || !TryParseLong(parts[2], out long tid) || !TryParseInt(parts[3], out int strat) || !TryParseInt(parts[4], out int tac)) return;
        if (strat != 1)
        {
            await bot.AnswerCallbackQueryAsync(cb.Id, "🔒 عملیات آبی‌خاکی و پیشروی زمینی فعلاً قفل است.", showAlert: true, cancellationToken: ct);
            return;
        }

        var session = sessions.GetOrAdd(uid, _ => new UserSession());
        session.AttackChatId = cid;
        session.AttackTargetId = tid;
        session.AttackNavalStrategy = strat;
        session.AttackNavalTactic = tac;

        var attacker = Database.GetCountry(uid, cid);
        if (attacker == null) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ کشور مهاجم یافت نشد.", showAlert: true, cancellationToken: ct); return; }

        // Now ask for naval forces per-model
        Database.SyncBattleshipUnits(uid, cid);
        var boatBreakdown = Database.GetNavalAttackableModels(attacker, "boats")
            .Select(x => (ModelName: x.Model, x.Count)).ToList();
        var subBreakdown = Database.GetNavalAttackableModels(attacker, "submarines")
            .Select(x => (ModelName: x.Model, x.Count)).ToList();
        var battleshipBreakdown = Database.GetNavalAttackableModels(attacker, "battleships")
            .Select(x => (ModelName: x.Model, x.Count)).ToList();

        // Combine all naval models into one list for asking?
        var allNaval = new List<(string Model, long Count, string Category)>();
        foreach (var b in boatBreakdown) allNaval.Add((b.ModelName, b.Count, "boats"));
        foreach (var b in subBreakdown) allNaval.Add((b.ModelName, b.Count, "submarines"));
        foreach (var b in battleshipBreakdown) allNaval.Add((b.ModelName, b.Count, "battleships"));

        if (allNaval.Count == 0)
        {
            await bot.AnswerCallbackQueryAsync(cb.Id, "❌ نیروی دریایی ندارید!", showAlert: true, cancellationToken: ct);
            return;
        }

        session.AttackModelNames = allNaval.Select(x => $"{x.Category}:{x.Model}").ToList();
        session.AttackModelCounts = allNaval.Select(x => x.Count).ToList();
        session.AttackModelAmounts = new List<long>(new long[allNaval.Count]);
        session.AttackModelIndex = 0;
        session.AttackCurrentCategory = "naval";
        session.Step = SessionStep.AttackWaitingModelAmount; //  – naval uses dedicated step

        string prompt = $"⚓ حمله دریایی – {allNaval.Count} مدل ناوگانی دارید\n🔧 مدل 1/{allNaval.Count}: {allNaval[0].Model} ({allNaval[0].Category}) – موجودی {allNaval[0].Count:N0}\nچند تا اعزام شود؟ (0 برای رد)";
        await bot.EditMessageTextAsync(cb.Message.Chat.Id, cb.Message.MessageId, prompt, cancellationToken: ct);
        TrackPrompt(uid, cb.Message.Chat.Id, cb.Message.MessageId);
        await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
    }

    static async Task HandleAttackStrategyCallback(
        CallbackQuery cb,
        string[] parts,
        CancellationToken ct)
{
        if (parts.Length < 4 || cb.Message == null)
            return;

        long uid = cb.From.Id;

        if (!TryParseLong(parts[1], out long cid) ||
            !TryParseLong(parts[2], out long tid) ||
            !TryParseInt(parts[3], out int strategy) ||
            strategy is < 1 or > 2)
            return;
        if (Database.HasAttackAbandonLock(uid) && !Database.HasGroupLockExemption(cid))
        { await bot.AnswerCallbackQueryAsync(cb.Id, "⛔ شما تا ۳ روز به دلیل بزن‌دررو از حمله قفل هستید.", showAlert: true, cancellationToken: ct); return; }

        var session = sessions.GetOrAdd(
            uid,
            _ => new UserSession()
        );

        session.Step = SessionStep.AttackWaitingTactic;
        session.AttackChatId = cid;
        session.AttackTargetId = tid;
        session.AttackStrategy = strategy;

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    GroundAttackTacticName(strategy, 1),
                    $"attack_tactic:{cid}:{tid}:{strategy}:1"
                )
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    GroundAttackTacticName(strategy, 2),
                    $"attack_tactic:{cid}:{tid}:{strategy}:2"
                )
            }
        });

        await bot.EditMessageTextAsync(
            cb.Message.Chat.Id,
            cb.Message.MessageId,
            GroundAttackTacticGuide(strategy),
            replyMarkup: keyboard,
            cancellationToken: ct
        );

        TrackPrompt(
            uid,
            cb.Message.Chat.Id,
            cb.Message.MessageId
        );

        await bot.AnswerCallbackQueryAsync(
            cb.Id,
            cancellationToken: ct
        );
    }

    static async Task PromptAttackAirOrRun(long uid,UserSession session,CancellationToken ct)
    {
        if(session.AttackFighters==0&&session.AttackBombers==0)
        {await RunAttackBattle(uid,session,ct);return;}
        session.Step=SessionStep.AttackWaitingAirStrategy;
        var keyboard=new InlineKeyboardMarkup(new[]{
            new[]{InlineKeyboardButton.WithCallbackData("✈️ برتری هوایی","attack_air_strategy:1")},
            new[]{InlineKeyboardButton.WithCallbackData("💣 بمباران راهبردی","attack_air_strategy:2")}});
        await SendPrompt(uid,uid,AirAttackStrategyGuide,keyboard,ct);
    }

    static async Task BeginAttackBomberSelection(long uid,UserSession session,Country attacker,CancellationToken ct)
    {
        var breakdown=GetTransferBreakdown(attacker,"bombers");
        if(breakdown.Count==0)
        {
            session.AttackBombers=0;session.AttackBomberModelNamesFinal=new();session.AttackBomberModelAmountsFinal=new();
            await PromptAttackAirOrRun(uid,session,ct);return;
        }
        session.AttackModelNames=breakdown.Select(x=>x.ModelName).ToList();
        session.AttackModelCounts=breakdown.Select(x=>x.Count).ToList();
        session.AttackModelAmounts=new List<long>(new long[breakdown.Count]);session.AttackModelIndex=0;
        session.AttackCurrentCategory="bombers";session.Step=SessionStep.AttackWaitingBomberModel;
        await SendPrompt(uid,uid,$"🛩 حمله — بمب‌افکن مدل 1/{breakdown.Count}: {breakdown[0].ModelName}\n📊 موجودی قابل اعزام: {breakdown[0].Count:N0}\nچند فروند اعزام شود؟ (0 برای رد)",ct:ct);
    }

    internal static void ResetAttackForceSelection(UserSession session)
    {
        session.AttackTanks=session.AttackSoldiers=session.AttackFighters=session.AttackBombers=0;
        session.AttackAirStrategy=session.AttackAirTactic=0;
        session.AttackCurrentCategory="";session.AttackModelIndex=0;
        session.AttackModelNames=new();session.AttackModelCounts=new();session.AttackModelAmounts=new();
        session.AttackTankModelNamesFinal=new();session.AttackTankModelAmountsFinal=new();
        session.AttackPlaneModelNamesFinal=new();session.AttackPlaneModelAmountsFinal=new();
        session.AttackBomberModelNamesFinal=new();session.AttackBomberModelAmountsFinal=new();
    }

    static async Task HandleAttackTacticCallback(
        CallbackQuery cb,
        string[] parts,
        CancellationToken ct)
    {
        if (parts.Length < 5 || cb.Message == null)
            return;

        long uid = cb.From.Id;

        if (!TryParseLong(parts[1], out long cid) ||
            !TryParseLong(parts[2], out long tid) ||
            !TryParseInt(parts[3], out int strategy) ||
            !TryParseInt(parts[4], out int tactic))
            return;

        var session = sessions.GetOrAdd(
            uid,
            _ => new UserSession()
        );

        session.AttackChatId = cid;
        session.AttackTargetId = tid;
        session.AttackStrategy = strategy;
        session.AttackTactic = tactic;
        ResetAttackForceSelection(session);

        var attacker = Database.GetCountry(uid, cid);

        if (attacker == null)
        {
            EndSession(uid);

            await bot.AnswerCallbackQueryAsync(
                cb.Id,
                "❌ کشور مهاجم یافت نشد.",
                showAlert: true,
                cancellationToken: ct
            );
            return;
        }

        string forcePrompt;

        //  – per-model attack for tanks
        var tankBreakdown = GetAttackBreakdown(attacker, "tanks");
        if (tankBreakdown.Count == 0)
        {
            session.AttackTanks = 0;
            session.AttackModelNames = new List<string>();
            session.AttackModelCounts = new List<long>();
            session.AttackModelAmounts = new List<long>();
            session.AttackModelIndex = 0;
            session.AttackCurrentCategory = "tanks";
            session.Step = SessionStep.AttackWaitingSoldiers;
            forcePrompt =
                "🪖 تعداد سربازان اعزامی را وارد کنید.\n" +
                InventoryLine(GetAttackAvailableSoldiers(attacker));
        }
        else if (tankBreakdown.Count == 1)
        {
            // Single model – ask amount directly but track per-model
            session.AttackModelNames = new List<string> { tankBreakdown[0].ModelName };
            session.AttackModelCounts = new List<long> { tankBreakdown[0].Count };
            session.AttackModelAmounts = new List<long> { 0 };
            session.AttackModelIndex = 0;
            session.AttackCurrentCategory = "tanks";
            session.Step = SessionStep.AttackWaitingTankModel;
            forcePrompt =
                $"🛡 تعداد تانک‌های اعزامی – مدل {tankBreakdown[0].ModelName} را وارد کنید.\n" +
                InventoryLine(tankBreakdown[0].Count);
        }
        else
        {
            session.AttackModelNames = tankBreakdown.Select(x => x.ModelName).ToList();
            session.AttackModelCounts = tankBreakdown.Select(x => x.Count).ToList();
            session.AttackModelAmounts = new List<long>(new long[tankBreakdown.Count]);
            session.AttackModelIndex = 0;
            session.AttackCurrentCategory = "tanks";
            session.Step = SessionStep.AttackWaitingTankModel;
            forcePrompt =
                $"🛡 حمله – تانک‌ها – {tankBreakdown.Count} مدل دارید\n\n" +
                $"🔧 مدل 1/{tankBreakdown.Count}: {tankBreakdown[0].ModelName}\n" +
                $"📊 موجودی: {tankBreakdown[0].Count:N0}\n\n" +
                $"چند عدد از این مدل اعزام شود؟ (0 برای رد شدن)";
        }

        await bot.EditMessageTextAsync(
            cb.Message.Chat.Id,
            cb.Message.MessageId,
            forcePrompt,
            replyMarkup: null,
            cancellationToken: ct
        );

        TrackPrompt(
            uid,
            cb.Message.Chat.Id,
            cb.Message.MessageId
        );

        await bot.AnswerCallbackQueryAsync(
            cb.Id,
            cancellationToken: ct
        );
    }

    static async Task HandleAttackAirStrategyCallback(
        CallbackQuery cb,
        string[] parts,
        CancellationToken ct)
    {
        if (parts.Length < 2 || cb.Message == null)
            return;

        long uid = cb.From.Id;

        if (!TryParseInt(parts[1], out int strategy) ||
            strategy is < 1 or > 2)
            return;

        if (!sessions.TryGetValue(uid, out var session) ||
            session == null)
        {
            await bot.AnswerCallbackQueryAsync(
                cb.Id,
                cancellationToken: ct
            );
            return;
        }

        session.AttackAirStrategy = strategy;
        session.Step = SessionStep.AttackWaitingAirTactic;

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    AirAttackTacticName(strategy, 1),
                    "attack_air_tactic:1"
                )
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    AirAttackTacticName(strategy, 2),
                    "attack_air_tactic:2"
                )
            }
        });

        await bot.EditMessageTextAsync(
            cb.Message.Chat.Id,
            cb.Message.MessageId,
            AirAttackTacticGuide(strategy),
            replyMarkup: keyboard,
            cancellationToken: ct
        );

        TrackPrompt(
            uid,
            cb.Message.Chat.Id,
            cb.Message.MessageId
        );

        await bot.AnswerCallbackQueryAsync(
            cb.Id,
            cancellationToken: ct
        );
    }

    static async Task HandleAttackAirTacticCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length < 2 || cb.Message == null) return;
        long uid = cb.From.Id;
        if (!TryParseInt(parts[1], out int aTac)) return;
        if (!sessions.TryGetValue(uid, out var sess) || sess == null) { await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct); return; }
        sess.AttackAirTactic = aTac;
        await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
        DeleteNow(cb.Message.Chat.Id, cb.Message.MessageId);
        await RunAttackBattle(uid, sess, ct);
    }

    static async Task RunAttackBattle(long uid, UserSession sess, CancellationToken ct)
    {
        long chatId = sess.AttackChatId;
        long defenderId = sess.AttackTargetId;
        EndSession(uid);
        if(!Database.IsBotGroupActive(chatId))
        {
            await SendTemp(uid,"⛔ ربات دیگر در گروه این کشور حضور ندارد؛ حمله ثبت نشد.",ct:ct);
            return;
        }

        bool deploymentLockHeld = false;
        var defensiveDeployments = Database.GetActiveDeployments()
            .Where(d => d.ChatId == chatId && d.Type == "Defensive" &&
                        d.TargetUserId == defenderId && d.EndAtMs > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            .ToList();
        if (defensiveDeployments.Count > 0)
        {
            await deploymentProcessorLock.WaitAsync(ct);
            deploymentLockHeld = true;
            try
            {
                defensiveDeployments = Database.GetActiveDeployments()
                    .Where(d => d.ChatId == chatId && d.Type == "Defensive" &&
                                d.TargetUserId == defenderId && d.EndAtMs > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                    .ToList();
            }
            catch
            {
                deploymentProcessorLock.Release();
                throw;
            }
        }
        var participantIds = defensiveDeployments
            .SelectMany(d => Database.GetDeploymentContributors(d.Id))
            .Select(x => x.UserId)
            .Append(uid)
            .Append(defenderId);
        List<SemaphoreSlim> locks;
        try
        {
            locks = await AcquireCountryMutationLocks(chatId, participantIds, ct);
        }
        catch
        {
            if (deploymentLockHeld) deploymentProcessorLock.Release();
            throw;
        }
        bool resultApplied = false;
        long queuedBattleId = 0;
        try
        {
            if (Database.HasAttackAbandonLock(uid) && !Database.HasGroupLockExemption(chatId))
            {
                await SendTemp(uid, "⛔ به‌دلیل قفل بزن‌دررو فعلاً امکان حمله ندارید.", ct: ct);
                return;
            }

            var attacker = Database.GetCountry(uid, chatId);
            var defender = Database.GetCountry(defenderId, chatId);
            if (attacker == null || defender == null)
            {
                await SendTemp(uid, "❌ کشور مهاجم یا مدافع یافت نشد.", ct: ct);
                return;
            }
            if (lastAssetUpdateAt != DateTime.MinValue &&
                (DateTime.UtcNow - lastAssetUpdateAt).TotalMinutes < ATTACK_LOCK_MINUTES &&
                !Database.HasGroupLockExemption(chatId))
            {
                int left = (int)Math.Ceiling(ATTACK_LOCK_MINUTES - (DateTime.UtcNow - lastAssetUpdateAt).TotalMinutes);
                await SendTemp(uid, $"⛔ تا {left} دقیقه دیگر حمله ممکن نیست.", ct: ct);
                return;
            }
            if (GetAttackCount(chatId, uid) >= MAX_ATTACKS_PER_UPDATE && !Database.HasGroupLockExemption(chatId))
            {
                await SendTemp(uid, $"⛔ سهمیه حمله تمام شده است ({MAX_ATTACKS_PER_UPDATE}).", ct: ct);
                return;
            }
            if (Database.IsAttackShieldActive(defenderId, chatId) && !Database.HasGroupLockExemption(chatId))
            {
                await SendTemp(uid, "🛡 کشور هدف در حال حاضر سپر فعال دارد.", ct: ct);
                return;
            }
            if (defender.CreatedAtMs > 0 && !Database.HasShieldExemption(defenderId, chatId) &&
                !Database.HasGroupLockExemption(chatId))
            {
                double ageHours = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - defender.CreatedAtMs) / 3600000.0;
                if (ageHours < SHIELD_HOURS)
                {
                    await SendTemp(uid, $"🛡 سپر کشور تازه‌ساخت تا {(int)Math.Ceiling(SHIELD_HOURS - ageHours)} ساعت دیگر فعال است.", ct: ct);
                    return;
                }
            }

            var selectedTanks = SessionModelAmounts(
                sess.AttackTankModelNamesFinal,
                sess.AttackTankModelAmountsFinal,
                new List<string>(), new List<long>(),
                sess.AttackTanks,
                Database.GetDefaultTankModel(attacker.Faction));
            var selectedFighters = SessionModelAmounts(
                sess.AttackPlaneModelNamesFinal,
                sess.AttackPlaneModelAmountsFinal,
                new List<string>(), new List<long>(), sess.AttackFighters,
                Database.GetDefaultPlaneModel(attacker.Faction));
            var selectedBombers = SessionModelAmounts(
                sess.AttackBomberModelNamesFinal,
                sess.AttackBomberModelAmountsFinal,
                new List<string>(), new List<long>(), sess.AttackBombers,
                Database.GetDefaultBomberModel(attacker.Faction));
            long soldiers = Math.Max(0, sess.AttackSoldiers);

            long availableSoldiers = GetAttackAvailableSoldiers(attacker);
            if (!AttackSelectionStateIsConsistent(sess,selectedTanks,selectedFighters,selectedBombers))
            {
                await SendTemp(uid,"❌ ترکیب انتخابی نیروها ناسازگار بود و برای جلوگیری از اعزام اشتباه ثبت نشد. حمله را دوباره تنظیم کنید.",ct:ct);
                return;
            }
            bool exactModelsAvailable =
                ModelSelectionFits(selectedTanks, GetAttackBreakdown(attacker, "tanks")) &&
                ModelSelectionFits(selectedFighters, GetAttackBreakdown(attacker, "planes")) &&
                ModelSelectionFits(selectedBombers, GetTransferBreakdown(attacker, "bombers"));
            if (soldiers > availableSoldiers || !exactModelsAvailable)
            {
                await SendTemp(uid,
                    "❌ موجودی قابل اعزام تغییر کرده یا بخشی از نیروها در دفاع اجباری است. حمله ثبت نشد.", ct: ct);
                return;
            }
            if (soldiers + selectedTanks.Sum(x => x.Count) <= 0)
            {
                await SendTemp(uid, "❌ برای عملیات باید نیروی زمینی اعزام شود.", ct: ct);
                return;
            }

            var ownDefense = BuildOwnDefenseParticipant(defender);
            var deploymentParticipants = BuildDeploymentParticipants(defensiveDeployments, chatId);
            var defenders = new List<BattleParticipant> { ownDefense };
            defenders.AddRange(deploymentParticipants);

            ulong scenarioSeed = WarEngine.CreateScenarioSeed();
            long battleId = unchecked((long)scenarioSeed);
            if (battleId == 0) battleId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() ^ uid ^ defenderId;
            var request = new BattleRequest
            {
                BattleId = battleId,
                ChatId = chatId,
                ScenarioSeed = scenarioSeed,
                Attackers = new List<BattleParticipant>
                {
                    new()
                    {
                        OwnerId = uid,
                        CountryName = attacker.Name,
                        OwnerName = attacker.OwnerName,
                        Faction = attacker.Faction,
                        Soldiers = soldiers,
                        Tanks = selectedTanks,
                        Fighters = selectedFighters,
                        Bombers = selectedBombers
                    }
                },
                Defenders = defenders,
                AttackerOrders = new BattleOrders
                {
                    GroundStrategy = sess.AttackStrategy,
                    GroundTactic = sess.AttackTactic,
                    AirStrategy = sess.AttackAirStrategy,
                    AirTactic = sess.AttackAirTactic <= 0 ? 1 : sess.AttackAirTactic
                },
                DefenderOrders = new BattleOrders
                {
                    GroundStrategy = defender.DefenseStrategy,
                    GroundTactic = defender.DefenseTactic,
                    AirStrategy = defender.AirDefStrategy,
                    AirTactic = defender.AirDefTactic
                }
            };

            var context = new BattleJobContext
            {
                AttackerId = uid,
                DefenderId = defenderId,
                ChatId = chatId,
                DefensiveDeploymentIds = defensiveDeployments.Select(x => x.Id).ToList()
            };
            queuedBattleId = request.BattleId;
            var persisted = Database.EnsureBattleJob(request.BattleId, "Direct",
                JsonSerializer.Serialize(request, BattleJsonOptions),
                JsonSerializer.Serialize(context, BattleJsonOptions));
            request = JsonSerializer.Deserialize<BattleRequest>(persisted.RequestJson, BattleJsonOptions)
                ?? throw new InvalidOperationException("Persisted battle request is invalid.");
            Database.UpdateBattleJob(request.BattleId, "Running");

            await SendTemp(uid, "⚙️ عملیات وارد صف پردازش موتور نبرد شد.", ct: ct);
            BattleResult result = await BattleExecutionScheduler.EnqueueAsync(request, ct);
            string resultJson = JsonSerializer.Serialize(result, BattleJsonOptions);
            Database.UpdateBattleJob(request.BattleId, "Resolved", resultJson);
            resultApplied = ApplyDirectBattleLosses(request.BattleId, attacker, defender, ownDefense,
                deploymentParticipants, defensiveDeployments, result);
            if (!resultApplied)
            {
                Database.UpdateBattleJob(request.BattleId, "Completed", resultJson);
                return;
            }
            try { Database.SaveBattleResult(request, result); }
            catch (Exception historyError) { Console.WriteLine($"[BATTLE HISTORY ERR] {historyError}"); }

            IncAttackCount(chatId, uid);
            string today = DateTime.UtcNow.AddHours(3.5).ToString("yyyy-MM-dd");
            Database.IncDailyDefendCount(defenderId, today);
            Database.SetAttackerFlag(uid, today);
            ApplyCompletedAttackShieldRules(uid,defenderId,chatId,
                Database.HasGroupLockExemption(chatId));

            try { await SendPermanent(uid, result.AttackerReport, ct: ct); } catch { }
            try { await SendPermanent(defenderId, result.DefenderReport, ct: ct); } catch { }
            try { await SendPermanent(chatId, result.GroupAnnouncement, ct: ct); } catch { }
            await ProcessStrategicBattleOutcome(uid, defenderId, chatId, result, ct);
            Database.UpdateBattleJob(request.BattleId, "Completed", resultJson);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NEW BATTLE ENGINE ERR] applied={resultApplied}: {ex}");
            if (!resultApplied && queuedBattleId != 0)
            {
                try { Database.UpdateBattleJob(queuedBattleId, "Pending", error: ex.Message); } catch { }
            }
            string message = resultApplied
                ? "⚠️ نتیجه نبرد اعمال شد، اما ارسال گزارش یا پردازش پیامدهای راهبردی کامل نشد. مدیران گزارش خطا را بررسی کنند."
                : "❌ موتور نتوانست این نبرد را تکمیل کند؛ هیچ نتیجه‌ای اعمال نشد.";
            try { await SendTemp(uid, message, ct: ct); } catch { }
        }
        finally
        {
            ReleaseCountryMutationLocks(locks);
            if (deploymentLockHeld) deploymentProcessorLock.Release();
        }
    }

    internal static bool AttackSelectionStateIsConsistent(UserSession session,
        IReadOnlyList<ModelAmount> tanks,IReadOnlyList<ModelAmount> fighters,IReadOnlyList<ModelAmount> bombers)
    {
        bool Valid(IReadOnlyList<ModelAmount> models,long expected)=>models.All(x=>x.Count>0&&!string.IsNullOrWhiteSpace(x.Model))&&
            models.GroupBy(x=>x.Model,StringComparer.OrdinalIgnoreCase).All(x=>x.Count()==1)&&models.Sum(x=>x.Count)==expected;
        return session.AttackSoldiers>=0&&Valid(tanks,session.AttackTanks)&&
               Valid(fighters,session.AttackFighters)&&Valid(bombers,session.AttackBombers);
    }

    internal static bool ModelSelectionFits(IReadOnlyList<ModelAmount> selected,
        IReadOnlyList<(string ModelName, long Count)> available)
    {
        var capacity = available.GroupBy(x => x.ModelName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Count), StringComparer.OrdinalIgnoreCase);
        foreach (var group in selected.GroupBy(x => x.Model, StringComparer.OrdinalIgnoreCase))
            if (group.Sum(x => x.Count) > capacity.GetValueOrDefault(group.Key)) return false;
        return true;
    }

    internal static List<ModelAmount> SessionModelAmounts(
        List<string> finalNames,
        List<long> finalAmounts,
        List<string> fallbackNames,
        List<long> fallbackAmounts,
        long fallbackTotal,
        string defaultModel)
    {
        var result = new List<ModelAmount>();
        bool explicitFinalSelection = finalNames.Count > 0;
        for (int i = 0; i < finalNames.Count && i < finalAmounts.Count; i++)
            if (finalAmounts[i] > 0) result.Add(new ModelAmount(finalNames[i], finalAmounts[i]));
        // Once a per-model category was shown, even an all-zero answer is authoritative.
        // Falling through to the generic working arrays could reinterpret bombers as tanks.
        if (!explicitFinalSelection)
        {
            for (int i = 0; i < fallbackNames.Count && i < fallbackAmounts.Count; i++)
                if (fallbackAmounts[i] > 0) result.Add(new ModelAmount(fallbackNames[i], fallbackAmounts[i]));
            if (result.Count == 0 && fallbackTotal > 0)
                result.Add(new ModelAmount(defaultModel, fallbackTotal));
        }
        return result;
    }

    static BattleParticipant BuildOwnDefenseParticipant(Country country)
    {
        var tanks = GetExactDefenseBreakdown(country, "tanks")
            .Select(x => new ModelAmount(x.ModelName, x.DefenseCount))
            .Where(x => x.Count > 0).ToList();
        var fighters = GetExactDefenseBreakdown(country, "planes")
            .Select(x => new ModelAmount(x.ModelName, x.DefenseCount))
            .Where(x => x.Count > 0).ToList();
        long soldiers = Math.Min(country.Soldiers,
            Math.Max(country.DefenseSoldiers, (long)Math.Ceiling(country.Soldiers * 0.2)));
        return new BattleParticipant
        {
            OwnerId = country.OwnerId,
            CountryName = country.Name,
            OwnerName = country.OwnerName,
            Faction = country.Faction,
            Soldiers = soldiers,
            Tanks = tanks,
            Fighters = fighters,
            AntiAir = country.AntiAir,
            IsHomelandDefender = country.Cities <= 2,
            Money = Math.Max(0, country.Money),
            Iron = Math.Max(0, country.Iron)
        };
    }

    static List<BattleParticipant> BuildDeploymentParticipants(List<Deployment> deployments, long chatId)
    {
        var entries = deployments.SelectMany(deployment =>
            Database.GetDeploymentContributors(deployment.Id)
                .Select(contributor => (DeploymentId: deployment.Id, Contributor: contributor)))
            .ToList();
        var result = new List<BattleParticipant>();
        foreach (var group in entries.GroupBy(x => x.Contributor.UserId))
        {
            var country = Database.GetCountry(group.Key, chatId);
            Faction faction = country?.Faction ?? Faction.USA;
            List<ModelAmount> ModelsFor(string category, Func<DeploymentContributor, long> totalSelector,
                string defaultModel)
            {
                var models = new List<ModelAmount>();
                foreach (var entry in group)
                {
                    long expected = Math.Max(0, totalSelector(entry.Contributor));
                    var stored = Database.GetDeploymentContributorModels(entry.DeploymentId,
                        group.Key, category);
                    long storedTotal = stored.Sum(x => Math.Max(0, x.Count));
                    models.AddRange(stored.Where(x => x.Count > 0));
                    if (storedTotal < expected)
                        models.Add(new ModelAmount(defaultModel, expected - storedTotal));
                }
                return models.GroupBy(x => x.Model, StringComparer.OrdinalIgnoreCase)
                    .Select(x => new ModelAmount(x.Key, x.Sum(y => y.Count)))
                    .Where(x => x.Count > 0).ToList();
            }

            result.Add(new BattleParticipant
            {
                OwnerId = group.Key,
                CountryName = country?.Name ?? $"کشور {group.Key}",
                OwnerName = country?.OwnerName ?? $"کاربر {group.Key}",
                Faction = faction,
                Soldiers = group.Sum(x => x.Contributor.Soldiers),
                Tanks = ModelsFor("Tanks", x => x.Tanks, Database.GetDefaultTankModel(faction)),
                Fighters = ModelsFor("Planes", x => x.Fighters, Database.GetDefaultPlaneModel(faction)),
                Bombers = ModelsFor("Bombers", x => x.Bombers, Database.GetDefaultBomberModel(faction))
            });
        }
        return result;
    }

    static bool ApplyDirectBattleLosses(long battleId, Country attacker, Country defender,
        BattleParticipant ownDefense, List<BattleParticipant> deploymentParticipants,
        List<Deployment> defensiveDeployments, BattleResult result)
    {
        var equipmentMutations = new List<(long OwnerId, Faction Faction, string Category, Dictionary<string, long> Losses)>();
        var deploymentMutations = new List<(long ContributorId, long Tanks, long Soldiers, long Fighters, long Bombers)>();

        if (result.AttackerParticipantLosses.TryGetValue(attacker.OwnerId, out var attackerLoss))
        {
            attacker.Soldiers = Math.Max(0, attacker.Soldiers - attackerLoss.SoldiersUnavailable);
            attacker.Tanks = Math.Max(0, attacker.Tanks - attackerLoss.TanksUnavailable.Values.Sum());
            attacker.Planes = Math.Max(0, attacker.Planes - attackerLoss.FightersUnavailable.Values.Sum());
            attacker.Bombers = Math.Max(0, attacker.Bombers - attackerLoss.BombersUnavailable.Values.Sum());
            equipmentMutations.Add((attacker.OwnerId, attacker.Faction, "Tanks", attackerLoss.TanksUnavailable));
            equipmentMutations.Add((attacker.OwnerId, attacker.Faction, "Planes", attackerLoss.FightersUnavailable));
            equipmentMutations.Add((attacker.OwnerId, attacker.Faction, "Bombers", attackerLoss.BombersUnavailable));
        }

        foreach (var (ownerId, loss) in result.DefenderParticipantLosses)
        {
            var deployed = deploymentParticipants.Where(x => x.OwnerId == ownerId).ToList();
            long deployedSoldiers = deployed.Sum(x => x.Soldiers);
            long deployedTanks = deployed.Sum(x => x.Tanks.Sum(m => m.Count));
            long deployedFighters = deployed.Sum(x => x.Fighters.Sum(m => m.Count));
            long deployedBombers = deployed.Sum(x => x.Bombers.Sum(m => m.Count));
            long ownSoldiers = ownerId == defender.OwnerId ? ownDefense.Soldiers : 0;
            long ownTanks = ownerId == defender.OwnerId ? ownDefense.Tanks.Sum(x => x.Count) : 0;
            long ownFighters = ownerId == defender.OwnerId ? ownDefense.Fighters.Sum(x => x.Count) : 0;
            long ownBombers = 0;
            long totalTankLoss = loss.TanksUnavailable.Values.Sum();
            long totalFighterLoss = loss.FightersUnavailable.Values.Sum();
            long totalBomberLoss = loss.BombersUnavailable.Values.Sum();
            long ownSLoss = ProportionalShare(loss.SoldiersUnavailable, ownSoldiers, deployedSoldiers);
            long ownTLoss = ProportionalShare(totalTankLoss, ownTanks, deployedTanks);
            long ownFLoss = ProportionalShare(totalFighterLoss, ownFighters, deployedFighters);
            long ownBLoss = ProportionalShare(totalBomberLoss, ownBombers, deployedBombers);

            if (ownerId == defender.OwnerId)
            {
                defender.Soldiers = Math.Max(0, defender.Soldiers - ownSLoss);
                defender.Tanks = Math.Max(0, defender.Tanks - ownTLoss);
                defender.Planes = Math.Max(0, defender.Planes - ownFLoss);
                defender.Bombers = Math.Max(0, defender.Bombers - ownBLoss);
                defender.AntiAir = Math.Max(0, defender.AntiAir - loss.AntiAirLost);
            }

            deploymentMutations.Add((ownerId,
                totalTankLoss - ownTLoss,
                loss.SoldiersUnavailable - ownSLoss,
                totalFighterLoss - ownFLoss,
                totalBomberLoss - ownBLoss));

            var ownerCountry = Database.GetCountry(ownerId, defender.ChatId);
            if (ownerCountry != null)
            {
                equipmentMutations.Add((ownerId, ownerCountry.Faction, "Tanks", loss.TanksUnavailable));
                equipmentMutations.Add((ownerId, ownerCountry.Faction, "Planes", loss.FightersUnavailable));
                equipmentMutations.Add((ownerId, ownerCountry.Faction, "Bombers", loss.BombersUnavailable));
            }
        }

        // غنیمت جنگی: فقط با پیروزی مهاجم؛ از خزانه مدافع برداشته و به مهاجم داده می‌شود
        if (result.AttackerWon)
        {
            attacker.Money = Math.Max(0, attacker.Money + result.AttackerMoneyGained);
            attacker.Iron = Math.Max(0, attacker.Iron + result.AttackerIronGained);
            defender.Money = Math.Max(0, defender.Money - result.DefenderMoneyLost);
            defender.Iron = Math.Max(0, defender.Iron - result.DefenderIronLost);
        }

        bool applied = Database.ApplyDirectBattleSettlement(battleId, attacker, defender,
            equipmentMutations, deploymentMutations,
            defensiveDeployments.Select(x => x.Id).ToArray());
        if (applied)
        {
            Database.ReconcileDefense(attacker.OwnerId, attacker.ChatId);
            Database.ReconcileDefense(defender.OwnerId, defender.ChatId);
        }
        return applied;
    }

    static long[] AllocateProportionallyExact(long requestedTotal, long[] capacities)
    {
        long capacity = capacities.Sum();
        long target = Math.Min(Math.Max(0, requestedTotal), capacity);
        var allocated = new long[capacities.Length];
        if (target == 0 || capacity == 0) return allocated;

        var fractions = new decimal[capacities.Length];
        long assigned = 0;
        for (int i = 0; i < capacities.Length; i++)
        {
            decimal exact = (decimal)target * capacities[i] / capacity;
            allocated[i] = Math.Min(capacities[i], (long)decimal.Floor(exact));
            fractions[i] = exact - allocated[i];
            assigned += allocated[i];
        }
        foreach (int i in Enumerable.Range(0, capacities.Length)
                     .OrderByDescending(i => fractions[i]).ThenBy(i => i))
        {
            if (assigned >= target) break;
            if (allocated[i] >= capacities[i]) continue;
            allocated[i]++;
            assigned++;
        }
        return allocated;
    }

    static long ProportionalShare(long loss, long own, long deployed)
    {
        long total = own + deployed;
        if (loss <= 0 || total <= 0 || own <= 0) return 0;
        return Math.Min(own, (long)Math.Round((double)loss * own / total));
    }

    static void DeductEquipmentLosses(Country country, string category,
        Dictionary<string, long> losses, Func<string, Faction, string> canonicalize)
    {
        var stored = Database.GetEquipmentModels(country.OwnerId, country.ChatId, category);
        foreach (var (canonicalModel, rawLoss) in losses)
        {
            long remaining = Math.Max(0, rawLoss);
            foreach (var row in stored.Where(x => canonicalize(x.ModelName, country.Faction)
                         .Equals(canonicalModel, StringComparison.OrdinalIgnoreCase)))
            {
                long take = Math.Min(remaining, row.Count);
                if (take <= 0) continue;
                Database.AddEquipmentModel(country.OwnerId, country.ChatId, category, row.ModelName, -take);
                row.Count -= take;
                remaining -= take;
                if (remaining == 0) break;
            }
        }
    }

    static async Task ProcessStrategicBattleOutcome(long attackerId, long defenderId,
        long chatId, BattleResult result, CancellationToken ct)
    {
        var attacker = Database.GetCountry(attackerId, chatId);
        var defender = Database.GetCountry(defenderId, chatId);
        if (attacker == null || defender == null) return;

        if (result.AttackerHeavyVictory)
        {
            int defeats = Database.AddRoutDefeat(defenderId, chatId, attackerId, 1);
            if (defeats >= 5)
            {
                Database.AddRoutDefeat(defenderId, chatId, attackerId, -defeats);
                int cities = Math.Max(0, defender.Cities - 1);
                Database.SetCities(defenderId, chatId, cities);
                bool gained = Database.AddCityToAttacker(attackerId, chatId);
                Database.SetActiveSiege(defenderId,chatId,attackerId,cities);
                if (cities == 0) Database.DeleteCountry(defenderId, chatId);
                try
                {
                    await SendPermanent(chatId,
                        $"🏙 پس از پنجمین شکست سنگین برابر {attacker.Name}، کشور {defender.Name} یک شهر از دست داد. شهرهای باقی‌مانده: {cities}" +
                        (gained ? "" : "\nسقف شهرهای مهاجم پر است."), ct: ct);
                }
                catch { }
                try
                {
                    string groupTitle=await GetGroupTitleCached(chatId,ct);
                    InlineKeyboardMarkup? revengeMarkup=cities>0
                        ?new InlineKeyboardMarkup(new[]{new[]{InlineKeyboardButton.WithCallbackData("⚔️ انتقام",$"revenge:{chatId}:{attackerId}")}})
                        :null;
                    await SendPermanent(defenderId,
                        $"🏙 یکی از شهرهای شما تصرف شد!\n⚔️ {attacker.Name} شهر شما را در گپ «{groupTitle}» تصرف کرد.\n"+
                        $"🏘 شهرهای باقی‌مانده: {cities}",revengeMarkup,ct:ct);
                }
                catch { }
                if(cities==0)
                    try{await SendPermanent(chatId,$"☠️ کشور {defender.Name} سقوط کرد.",ct:ct);}catch{}
            }

            // City transfer is handled exactly once by the per-attacker/defender rout
            // counter above. The old global HeavyOffensiveWins block awarded a second city
            // on the same fifth victory. Keep the legacy counter cleared but do not double-award.
            Database.ResetHeavyOffensiveWins(attackerId, chatId);
        }
        else if (result.DefenderVictory)
        {
            int defenses = defender.DefenseWins + 1;
            if (defenses >= 5)
            {
                defenses = 0;
                if (defender.Cities < Database.MAX_CITIES)
                {
                    int cities = Math.Min(Database.MAX_CITIES, defender.Cities + 1);
                    Database.SetCities(defenderId, chatId, cities);
                    Database.RefreshActiveSiegeAfterCityRecovery(defenderId,chatId,cities);
                    try { await SendPermanent(chatId, $"🛡 {defender.Name} با پنج دفاع موفق یک شهر را بازپس گرفت. شهرها: {cities}", ct: ct); }
                    catch { }
                }
            }
            Database.SetDefenseWins(defenderId, chatId, defenses);
        }
    }

    static async Task SendDefenseStatus(
        long sendTo,
        long ownerId,
        long chatId,
        CancellationToken ct)
    {
        Database.ReconcileDefense(ownerId, chatId);

        var country = Database.GetCountry(ownerId, chatId);

        if (country == null)
        {
            await SendTemp(
                sendTo,
                "❌ کشور یافت نشد.",
                ct: ct
            );
            return;
        }

        long minimumTanks =
            (long)Math.Ceiling(country.Tanks * 0.2);

        long minimumSoldiers =
            (long)Math.Ceiling(country.Soldiers * 0.2);

        string groundStrategy =
            GroundDefenseStrategyName(
                country.DefenseStrategy
            );

        string groundTactic =
            GroundDefenseTacticName(
                country.DefenseStrategy,
                country.DefenseTactic
            );

        string airStrategy =
            AirDefenseStrategyName(
                country.AirDefStrategy
            );

        string airTactic =
            AirDefenseTacticName(
                country.AirDefStrategy,
                country.AirDefTactic
            );

        // per-model defense breakdown – including naval
        var tankBreakdown = GetExactDefenseBreakdown(country, "tanks");
        var planeBreakdown = GetExactDefenseBreakdown(country, "planes");
        var boatInventory = Database.GetEquipmentBreakdownForReconcile(country, "boats");
        var subInventory = Database.GetEquipmentBreakdownForReconcile(country, "submarines");
        var bsInventory = Database.GetEquipmentBreakdownForReconcile(country, "battleships");
        var boatDefense = Database.GetNavalDefenseModels(country, "boats").ToDictionary(x=>x.Model,x=>x.Count,StringComparer.OrdinalIgnoreCase);
        var subDefense = Database.GetNavalDefenseModels(country, "submarines").ToDictionary(x=>x.Model,x=>x.Count,StringComparer.OrdinalIgnoreCase);
        var bsDefense = Database.GetNavalDefenseModels(country, "battleships").ToDictionary(x=>x.Model,x=>x.Count,StringComparer.OrdinalIgnoreCase);

        var sbDef = new StringBuilder();
        sbDef.AppendLine($"🛡 نیروهای مستقر در دفاع (جزئی per-model + دریایی):");
        if (tankBreakdown.Count > 0)
        {
            sbDef.AppendLine("🛡 تانک‌ها:");
            foreach (var (model, count, defCount, minimum) in tankBreakdown)
                sbDef.AppendLine($"  • {model}: دفاع {defCount:N0} از {count:N0}" +
                    (minimum > 0 ? $" | اجباری {minimum:N0}" : ""));
        }
        else
        {
            sbDef.AppendLine($"🛡 تانک: {country.DefenseTanks:N0} | حداقل: {minimumTanks:N0}");
        }
        sbDef.AppendLine($"🪖 سرباز: {country.DefenseSoldiers:N0} | حداقل: {minimumSoldiers:N0}");
        if (planeBreakdown.Count > 0)
        {
            sbDef.AppendLine("✈️ جنگنده‌ها:");
            foreach (var (model, count, defCount, minimum) in planeBreakdown)
                sbDef.AppendLine($"  • {model}: دفاع {defCount:N0} از {count:N0}" +
                    (minimum > 0 ? $" | اجباری {minimum:N0}" : ""));
        }
        else
        {
            sbDef.AppendLine($"✈️ جنگنده: {country.DefenseFighters:N0}");
        }
        sbDef.AppendLine($"🎯 پدافند: {country.AntiAir:N0}");

        // Naval defense — exact per model, with a compulsory 20% reserve.
        var navalOrders=Database.GetNavalDefenseOrders(country.OwnerId,country.ChatId);
        string navalDoctrine=(navalOrders.Strategy,navalOrders.Tactic) switch
        {(1,1)=>"استحکامات، توپخانه ساحلی و میدان مین",(1,2)=>"خروج سریع و ضدحمله",(2,1)=>"حمله و عقب‌نشینی",_=>"کمین دریایی"};
        sbDef.AppendLine($"⚓ دکترین دریایی: {navalDoctrine}");
        sbDef.AppendLine("🚤 قایق‌ها:");
        foreach(var x in boatInventory)sbDef.AppendLine($"  • {x.ModelName}: دفاع {boatDefense.GetValueOrDefault(x.ModelName):N0} از {x.Count:N0}");
        sbDef.AppendLine("⚓ زیردریایی‌ها:");
        foreach(var x in subInventory)sbDef.AppendLine($"  • {x.ModelName}: دفاع {subDefense.GetValueOrDefault(x.ModelName):N0} از {x.Count:N0}");
        sbDef.AppendLine("🚢 نبردناوها:");
        foreach(var x in bsInventory)sbDef.AppendLine($"  • {x.ModelName}: دفاع {bsDefense.GetValueOrDefault(x.ModelName):N0} از {x.Count:N0}");

        string text =
            $"🛡 وضعیت دفاع {country.Name}\n\n" +

            "⚔️ دفاع زمینی\n" +
            $"استراتژی: {groundStrategy}\n" +
            $"تاکتیک: {groundTactic}\n\n" +

            "🛫 دفاع هوایی\n" +
            $"استراتژی: {airStrategy}\n" +
            $"تاکتیک: {airTactic}\n\n" +

            "⚓ دفاع دریایی\n" +
            $"بندر سطح: {country.PortLevel}\n\n" +

            sbDef.ToString() + "\n" +

            "📊 کل موجودی کشور\n" +
            $"تانک: {country.Tanks:N0} | سرباز: {country.Soldiers:N0} | جنگنده: {country.Planes:N0}\n" +
            BuildNavalInventorySummary(country);

        bool isPrivate = sendTo == ownerId;

        if (isPrivate)
        {
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        "⚔️ تاکتیک زمینی",
                        $"defense_tactic:{chatId}"
                    )
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        "🛫 دفاع هوایی",
                        $"airdef_strategy:{chatId}"
                    )
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("⚓ دفاع دریایی", $"naval_defense:{chatId}"),
                    InlineKeyboardButton.WithCallbackData("⚙️ انتخاب نیروی زمینی", $"defense_set:{chatId}")
                }
            });

            await SendTemp(
                sendTo,
                text,
                markup: keyboard,
                ct: ct
            );
        }
        else
        {
            await SendTemp(
                sendTo,
                text + "\n\n⚙️ برای تنظیم به پیوی آلیس بروید.",
                ct: ct
            );
        }
    }

    static async Task HandleDefenseStatusCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length < 2) return;
        long uid = cb.From.Id;
        if (!TryParseLong(parts[1], out long cid)) return;
        await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
        EndSession(uid);
        await SendDefenseStatus(uid, uid, cid, ct);
    }

    static readonly int[] DefensePercents = { 20, 30, 40, 50, 60, 70, 80, 90, 100 };
    static InlineKeyboardMarkup BuildPercentKeyboard(string kind, long chatId)
    {
        var rows = new List<InlineKeyboardButton[]>();
        for (int i = 0; i < DefensePercents.Length; i += 2)
        {
            var row = new List<InlineKeyboardButton> { InlineKeyboardButton.WithCallbackData($"{DefensePercents[i]}%", $"defense_pct:{chatId}:{kind}:{DefensePercents[i]}") };
            if (i + 1 < DefensePercents.Length) row.Add(InlineKeyboardButton.WithCallbackData($"{DefensePercents[i + 1]}%", $"defense_pct:{chatId}:{kind}:{DefensePercents[i + 1]}"));
            rows.Add(row.ToArray());
        }
        rows.Add(new[] { InlineKeyboardButton.WithCallbackData("❌ انصراف", "cancel") });
        return new InlineKeyboardMarkup(rows);
    }

    static InlineKeyboardMarkup BuildModelPercentKeyboard(long chatId, string category, int modelIndex)
    {
        var rows = new List<InlineKeyboardButton[]>();
        for (int i = 0; i < DefensePercents.Length; i += 2)
        {
            var row = new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData($"{DefensePercents[i]}%", $"defense_model_pct:{chatId}:{category}:{modelIndex}:{DefensePercents[i]}")
            };
            if (i + 1 < DefensePercents.Length)
                row.Add(InlineKeyboardButton.WithCallbackData($"{DefensePercents[i + 1]}%", $"defense_model_pct:{chatId}:{category}:{modelIndex}:{DefensePercents[i + 1]}"));
            rows.Add(row.ToArray());
        }
        rows.Add(new[] { InlineKeyboardButton.WithCallbackData("❌ انصراف", "cancel") });
        return new InlineKeyboardMarkup(rows);
    }

    static async Task HandleNavalDefenseCallback(CallbackQuery cb,string[] parts,CancellationToken ct)
    {
        if(parts.Length<2||cb.Message==null||!TryParseLong(parts[1],out long cid))return;
        var kb=new InlineKeyboardMarkup(new[]{
            new[]{InlineKeyboardButton.WithCallbackData("🏰 دفاع از پایگاه دریایی",$"naval_defense_strategy:{cid}:1")},
            new[]{InlineKeyboardButton.WithCallbackData("🌊 جنگ نامتقارن و فرسایشی",$"naval_defense_strategy:{cid}:2")}});
        await bot.AnswerCallbackQueryAsync(cb.Id,cancellationToken:ct);
        await bot.EditMessageTextAsync(cb.Message.Chat.Id,cb.Message.MessageId,"⚓ استراتژی دفاع دریایی را انتخاب کنید:",replyMarkup:kb,cancellationToken:ct);
    }
    static async Task HandleNavalDefenseStrategyCallback(CallbackQuery cb,string[] parts,CancellationToken ct)
    {
        if(parts.Length<3||cb.Message==null||!TryParseLong(parts[1],out long cid)||!TryParseInt(parts[2],out int strategy))return;
        var kb=strategy==1?new InlineKeyboardMarkup(new[]{
            new[]{InlineKeyboardButton.WithCallbackData("🧱 استحکامات، توپخانه و میدان مین",$"naval_defense_tactic:{cid}:1:1")},
            new[]{InlineKeyboardButton.WithCallbackData("⚡ خروج سریع و ضدحمله",$"naval_defense_tactic:{cid}:1:2")}}):
            new InlineKeyboardMarkup(new[]{
            new[]{InlineKeyboardButton.WithCallbackData("🏃 حمله و عقب‌نشینی",$"naval_defense_tactic:{cid}:2:1")},
            new[]{InlineKeyboardButton.WithCallbackData("🐋 کمین دریایی",$"naval_defense_tactic:{cid}:2:2")}});
        await bot.AnswerCallbackQueryAsync(cb.Id,cancellationToken:ct);
        await bot.EditMessageTextAsync(cb.Message.Chat.Id,cb.Message.MessageId,"⚓ تاکتیک دفاع دریایی را انتخاب کنید:",replyMarkup:kb,cancellationToken:ct);
    }
    static async Task HandleNavalDefenseTacticCallback(CallbackQuery cb,string[] parts,CancellationToken ct)
    {
        if(parts.Length<4||!TryParseLong(parts[1],out long cid)||!TryParseInt(parts[2],out int strategy)||!TryParseInt(parts[3],out int tactic))return;
        long uid=cb.From.Id;var country=Database.GetCountry(uid,cid);
        if(country==null){await bot.AnswerCallbackQueryAsync(cb.Id,"❌ کشور یافت نشد.",showAlert:true,cancellationToken:ct);return;}
        Database.SetNavalDefenseOrders(uid,cid,strategy,tactic);
        await bot.AnswerCallbackQueryAsync(cb.Id,"✅ تاکتیک ذخیره شد؛ حالا تعداد دقیق مدل‌ها را تعیین کنید.",cancellationToken:ct);
        if(cb.Message!=null)DeleteNow(cb.Message.Chat.Id,cb.Message.MessageId);
        var session=new UserSession{AttackChatId=cid};sessions[uid]=session;
        await BeginNavalDefenseCategory(uid,session,country,"boats",ct);
    }
    static async Task BeginNavalDefenseCategory(long uid,UserSession sess,Country country,string resource,CancellationToken ct)
    {
        var inventory=Database.GetEquipmentBreakdownForReconcile(country,resource);
        if(resource=="battleships")
        {
            Database.SyncBattleshipUnits(country.OwnerId,country.ChatId);
            var ready=Database.GetBattleshipUnits(country.OwnerId,country.ChatId,true)
                .GroupBy(x=>x.Model,StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x=>x.Key,x=>(long)x.Count(),StringComparer.OrdinalIgnoreCase);
            inventory=inventory.Select(x=>(x.ModelName,Count:Math.Min(x.Count,ready.GetValueOrDefault(x.ModelName))))
                .Where(x=>x.Count>0).ToList();
        }
        string category=resource=="boats"?"Boats":resource=="submarines"?"Submarines":"Battleships";
        if(inventory.Count==0)
        {
            Database.ReplaceNavalDefenseModels(uid,country.ChatId,category,new Dictionary<string,long>());
            if(resource=="boats"){await BeginNavalDefenseCategory(uid,sess,country,"submarines",ct);return;}
            if(resource=="submarines"){await BeginNavalDefenseCategory(uid,sess,country,"battleships",ct);return;}
            EndSession(uid);await SendTemp(uid,"✅ دفاع دریایی ذخیره شد.",ct:ct);return;
        }
        string defaultModel=resource=="boats"?Database.GetDefaultBoatModel(country.Faction):resource=="submarines"?Database.GetDefaultSubModel(country.Faction):Database.GetDefaultBattleshipModel(country.Faction);
        long mandatory=(long)Math.Ceiling(inventory.Sum(x=>x.Count)*0.20);
        long[] minimums=AllocateModelPriority(inventory,defaultModel,mandatory);
        var current=Database.GetNavalDefenseModels(country,resource).ToDictionary(x=>x.Model,x=>x.Count,StringComparer.OrdinalIgnoreCase);
        sess.DefenseModelNames=inventory.Select(x=>x.ModelName).ToList();sess.DefenseModelCounts=inventory.Select(x=>x.Count).ToList();
        sess.DefenseModelMinimums=minimums.ToList();sess.DefenseModelAmounts=inventory.Select((x,i)=>Math.Max(current.GetValueOrDefault(x.ModelName),minimums[i])).ToList();
        sess.DefenseModelIndex=0;sess.DefenseCurrentCategory=resource;
        sess.Step=resource=="boats"?SessionStep.NavalDefenseWaitingBoatModel:resource=="submarines"?SessionStep.NavalDefenseWaitingSubmarineModel:SessionStep.NavalDefenseWaitingBattleshipModel;
        await SendPrompt(uid,uid,$"⚓ دفاع {category} — مدل 1/{inventory.Count}\n🔧 {inventory[0].ModelName}\n📊 موجودی: {inventory[0].Count:N0}\n🔒 حداقل اجباری: {minimums[0]:N0}\nتعداد دقیق دفاع را وارد کنید:",ct:ct);
    }

    static async Task HandleDefenseSetCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length < 2 || cb.Message == null) return;
        long uid = cb.From.Id;
        if (!TryParseLong(parts[1], out long cid)) return;
        var c = Database.GetCountry(uid, cid);
        if (c == null) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ کشور نیست!", cancellationToken: ct); return; }
        await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);

        // Ground/air defense is configured as exact per-model amounts. The compulsory
        // tank reserve is 20% overall, allocated to the domestic factory model first.
        var tankBreakdown = GetExactDefenseBreakdown(c, "tanks");
        if (tankBreakdown.Count == 0)
        {
            await bot.EditMessageTextAsync(cb.Message.Chat.Id, cb.Message.MessageId,
                $"🪖 درصد سرباز:\nکل: {c.Soldiers:N0}",
                replyMarkup: BuildPercentKeyboard("soldier", cid), cancellationToken: ct);
            sessions[uid] = new UserSession
                { Step = SessionStep.DefenseWaitingSoldiers, AttackChatId = cid, DefenseTanks = 0, DefTankPct = 100 };
            return;
        }

        var sess = new UserSession
        {
            Step = SessionStep.DefenseWaitingTankModel,
            AttackChatId = cid,
            DefenseCurrentCategory = "tanks",
            DefenseModelNames = tankBreakdown.Select(x => x.ModelName).ToList(),
            DefenseModelCounts = tankBreakdown.Select(x => x.Count).ToList(),
            DefenseModelAmounts = tankBreakdown.Select(x => x.DefenseCount).ToList(),
            DefenseModelMinimums = tankBreakdown.Select(x => x.MinimumCount).ToList(),
            DefenseModelIndex = 0
        };
        sessions[uid] = sess;

        var first = tankBreakdown[0];
        string msg = $"🛡 دفاع تانک – مدل 1/{tankBreakdown.Count}\n\n🔧 مدل: {first.ModelName}\n📊 موجودی: {first.Count:N0}\n🛡 مقدار فعلی دفاع: {first.DefenseCount:N0}\n🔒 حداقل اجباری این مدل: {first.MinimumCount:N0}\n\nتعداد دقیق این مدل در دفاع را وارد کنید:";
        DeleteNow(cb.Message.Chat.Id, cb.Message.MessageId);
        await SendPrompt(uid, uid, msg, ct: ct);
    }

    static async Task HandleDefensePctCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length < 4 || cb.Message == null) return;
        long uid = cb.From.Id;
        if (!TryParseLong(parts[1], out long cid) || !TryParseInt(parts[3], out int pct)) return;
        string kind = parts[2];
        var c = Database.GetCountry(uid, cid);
        if (c == null) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌", cancellationToken: ct); return; }
        if (kind == "tank")
        {
            long dt = (long)Math.Ceiling(c.Tanks * (pct / 100.0));
            sessions[uid] = new UserSession { Step = SessionStep.DefenseWaitingSoldiers, AttackChatId = cid, DefenseTanks = dt, DefTankPct = pct };
            await bot.AnswerCallbackQueryAsync(cb.Id, $"🛡 {pct}%", cancellationToken: ct);
            await bot.EditMessageTextAsync(cb.Message.Chat.Id, cb.Message.MessageId, $"🪖 درصد سرباز:\nکل: {c.Soldiers}", replyMarkup: BuildPercentKeyboard("soldier", cid), cancellationToken: ct);
            return;
        }
        if (kind == "soldier")
        {
            long defT = c.DefenseTanks; int dtp = 100;
            if (sessions.TryGetValue(uid, out var s) && s != null && s.AttackChatId == cid) { defT = s.DefenseTanks; dtp = s.DefTankPct > 0 ? s.DefTankPct : 100; }

            long ds = (long)Math.Ceiling(c.Soldiers * (pct / 100.0));
            var planeBreakdown = GetExactDefenseBreakdown(c, "planes");
            var currentSession = sessions.TryGetValue(uid, out var existing) && existing != null
                ? existing : new UserSession();
            if (planeBreakdown.Count == 0)
            {
                var tankMap = Enumerable.Range(0, currentSession.DefenseTankModelNamesFinal.Count)
                    .Where(i => i < currentSession.DefenseTankModelAmountsFinal.Count && currentSession.DefenseTankModelAmountsFinal[i] > 0)
                    .ToDictionary(i => currentSession.DefenseTankModelNamesFinal[i],
                        i => currentSession.DefenseTankModelAmountsFinal[i], StringComparer.OrdinalIgnoreCase);
                Database.ReplaceDefenseModelAmounts(uid, cid, "Tanks", tankMap);
                Database.ReplaceDefenseModelAmounts(uid, cid, "Planes", new Dictionary<string, long>());
                Database.SetDefenseSoldierConfigured(uid,cid,true);
                Database.UpdateDefenseFull(uid, cid, defT, ds, 0, c.DefenseStrategy, c.DefenseTactic, 100, pct, 100);
                EndSession(uid);
                await bot.AnswerCallbackQueryAsync(cb.Id, $"🪖 {pct}% – ذخیره شد.", cancellationToken: ct);
                DeleteNow(cb.Message.Chat.Id, cb.Message.MessageId);
                await SendDefenseStatus(uid, uid, cid, ct);
                return;
            }

            currentSession.Step = SessionStep.DefenseWaitingPlaneModel;
            currentSession.AttackChatId = cid;
            currentSession.DefenseTanks = defT;
            currentSession.DefenseSoldiers = ds;
            currentSession.DefTankPct = 100;
            currentSession.DefSoldierPct = pct;
            currentSession.DefenseCurrentCategory = "planes";
            currentSession.DefenseModelNames = planeBreakdown.Select(x => x.ModelName).ToList();
            currentSession.DefenseModelCounts = planeBreakdown.Select(x => x.Count).ToList();
            currentSession.DefenseModelAmounts = planeBreakdown.Select(x => x.DefenseCount).ToList();
            currentSession.DefenseModelMinimums = planeBreakdown.Select(x => x.MinimumCount).ToList();
            currentSession.DefenseModelIndex = 0;
            sessions[uid] = currentSession;

            await bot.AnswerCallbackQueryAsync(cb.Id, $"🪖 {pct}%", cancellationToken: ct);
            DeleteNow(cb.Message.Chat.Id, cb.Message.MessageId);
            var firstPlane = planeBreakdown[0];
            string msgPlane = $"✈️ دفاع جنگنده – مدل 1/{planeBreakdown.Count}\n\n🔧 مدل: {firstPlane.ModelName}\n📊 موجودی: {firstPlane.Count:N0}\n🛡 مقدار فعلی دفاع: {firstPlane.DefenseCount:N0}\n🔒 حداقل اجباری: {firstPlane.MinimumCount:N0}\n\nتعداد دقیق این مدل در دفاع را وارد کنید:";
            await SendPrompt(uid, uid, msgPlane, ct: ct);
            return;
        }
        if (kind == "fighter")
        {
            // Legacy single fighter handling – kept for backward compat, now redirects to per-model if needed
            long defT = c.DefenseTanks, defS = c.DefenseSoldiers;
            int dtp = 100, dsp = 100;
            if (sessions.TryGetValue(uid, out var s) && s != null && s.AttackChatId == cid) { defT = s.DefenseTanks; defS = s.DefenseSoldiers; dtp = s.DefTankPct > 0 ? s.DefTankPct : 100; dsp = s.DefSoldierPct > 0 ? s.DefSoldierPct : 100; }
            long df = (long)Math.Ceiling(c.Planes * (pct / 100.0));
            Database.UpdateDefenseFull(uid, cid, defT, defS, df, c.DefenseStrategy, c.DefenseTactic, dtp, dsp, pct);
            EndSession(uid);
            await bot.AnswerCallbackQueryAsync(cb.Id, $"✅ ذخیره شد.", cancellationToken: ct);
            DeleteNow(cb.Message.Chat.Id, cb.Message.MessageId);
            await SendDefenseStatus(uid, uid, cid, ct);
            return;
        }
        await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
    }

    static async Task HandleDefenseModelPctCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        // Callback: defense_model_pct:{chatId}:{category}:{modelIndex}:{pct}
        if (parts.Length < 5 || cb.Message == null) return;
        long uid = cb.From.Id;
        if (!TryParseLong(parts[1], out long cid) || !TryParseInt(parts[3], out int modelIdx) || !TryParseInt(parts[4], out int pct)) return;
        string category = parts[2]; // tanks or planes
        pct = Math.Clamp(pct, 20, 100);

        if (!sessions.TryGetValue(uid, out var sess) || sess == null) return;
        var c = Database.GetCountry(uid, cid);
        if (c == null) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ کشور نیست!", cancellationToken: ct); return; }

        // Ensure session matches
        if (sess.AttackChatId != cid && sess.AttackChatId != 0) { /* mismatch, but allow */ }

        if (modelIdx < 0 || modelIdx >= sess.DefenseModelNames.Count)
        {
            await bot.AnswerCallbackQueryAsync(cb.Id, "❌ ایندکس نامعتبر", cancellationToken: ct);
            return;
        }

        // Save pct for this model – now supports naval too
        sess.DefenseModelPcts[modelIdx] = pct;
        string modelName = sess.DefenseModelNames[modelIdx];
        string dbCategory = category switch
        {
            "tanks" => "Tanks",
            "planes" => "Planes",
            "boats" => "Boats",
            "submarines" => "Submarines",
            "battleships" => "Battleships",
            _ => "Tanks"
        };
        Database.SetDefenseModel(uid, cid, dbCategory, modelName, pct);

        await bot.AnswerCallbackQueryAsync(cb.Id, $"✅ {modelName}: {pct}%", cancellationToken: ct);

        // Move to next model in same category
        sess.DefenseModelIndex = modelIdx + 1;
        if (sess.DefenseModelIndex < sess.DefenseModelNames.Count)
        {
            var next = sess.DefenseModelNames[sess.DefenseModelIndex];
            var nextCount = sess.DefenseModelCounts[sess.DefenseModelIndex];
            var nextPct = sess.DefenseModelPcts[sess.DefenseModelIndex];
            string msg = category switch
            {
                "tanks" => $"🛡 درصد دفاع تانک – مدل {sess.DefenseModelIndex + 1}/{sess.DefenseModelNames.Count}\n\n🔧 مدل: {next}\n📊 موجودی: {nextCount:N0}\n📈 فعلی: {nextPct}%\n\nچند درصد در دفاع باشد؟",
                "planes" => $"✈️ درصد دفاع جنگنده – مدل {sess.DefenseModelIndex + 1}/{sess.DefenseModelNames.Count}\n\n🔧 مدل: {next}\n📊 موجودی: {nextCount:N0}\n📈 فعلی: {nextPct}%\n\nچند درصد در دفاع باشد؟",
                "boats" => $"🚤 درصد دفاع قایق – مدل {sess.DefenseModelIndex + 1}/{sess.DefenseModelNames.Count}\n\n🔧 مدل: {next}\n📊 موجودی: {nextCount:N0}\n📈 فعلی: {nextPct}%\n\nچند درصد در دفاع باشد؟",
                "submarines" => $"⚓ درصد دفاع زیردریایی – مدل {sess.DefenseModelIndex + 1}/{sess.DefenseModelNames.Count}\n\n🔧 مدل: {next}\n📊 موجودی: {nextCount:N0}\n📈 فعلی: {nextPct}%\n\nچند درصد در دفاع باشد؟",
                _ => $"🛡 درصد دفاع {category} – مدل {sess.DefenseModelIndex + 1}/{sess.DefenseModelNames.Count}\n\n🔧 مدل: {next}\n📊 موجودی: {nextCount:N0}\n📈 فعلی: {nextPct}%\n\nچند درصد در دفاع باشد؟"
            };
            await bot.EditMessageTextAsync(cb.Message.Chat.Id, cb.Message.MessageId, msg, replyMarkup: BuildModelPercentKeyboard(cid, category, sess.DefenseModelIndex), cancellationToken: ct);
            return;
        }

        // Finished current category
        if (category == "tanks")
        {
            long totalDefTanks = 0;
            for (int i = 0; i < sess.DefenseModelNames.Count; i++)
                totalDefTanks += (long)Math.Ceiling(sess.DefenseModelCounts[i] * sess.DefenseModelPcts[i] / 100.0);
            sess.DefenseTanks = totalDefTanks;
            sess.DefTankPct = 100;
            sess.Step = SessionStep.DefenseWaitingSoldiers;
            string msg = $"🪖 درصد دفاع سرباز:\nکل: {c.Soldiers:N0}\n\nدرصد را انتخاب کنید:";
            await bot.EditMessageTextAsync(cb.Message.Chat.Id, cb.Message.MessageId, msg, replyMarkup: BuildPercentKeyboard("soldier", cid), cancellationToken: ct);
            return;
        }
        else if (category == "planes")
        {
            long totalDefPlanes = 0;
            for (int i = 0; i < sess.DefenseModelNames.Count; i++)
                totalDefPlanes += (long)Math.Ceiling(sess.DefenseModelCounts[i] * sess.DefenseModelPcts[i] / 100.0);
            sess.DefenseTanks = sess.DefenseTanks; // keep
            sess.DefenseSoldiers = sess.DefenseSoldiers;
            // Store intermediate fighter count in session for later finalization, but continue to naval
            sess.DefenseModelNames = new List<string>(); // will be reused for boats
            sess.DefenseModelCounts = new List<long>();
            sess.DefenseModelPcts = new List<int>();
            // Go to boats
            var boatBreakdown = GetDefenseBreakdown(c, "boats");
            if (boatBreakdown.Count == 0)
            {
                // No boats, go to subs
                var subBreakdown = GetDefenseBreakdown(c, "submarines");
                if (subBreakdown.Count == 0)
                {
                    // No naval, finalize
                    long defT = sess.DefenseTanks;
                    long defS = sess.DefenseSoldiers;
                    int dtp = sess.DefTankPct > 0 ? sess.DefTankPct : 100;
                    int dsp = sess.DefSoldierPct > 0 ? sess.DefSoldierPct : 100;
                    Database.UpdateDefenseFull(uid, cid, defT, defS, totalDefPlanes, c.DefenseStrategy, c.DefenseTactic, dtp, dsp, 100);
                    Database.ReconcileDefense(uid, cid);
                    EndSession(uid);
                    DeleteNow(cb.Message.Chat.Id, cb.Message.MessageId);
                    await SendDefenseStatus(uid, uid, cid, ct);
                    return;
                }
                sess.DefenseCurrentCategory = "submarines";
                sess.DefenseModelNames = subBreakdown.Select(x => x.ModelName).ToList();
                sess.DefenseModelCounts = subBreakdown.Select(x => x.Count).ToList();
                sess.DefenseModelPcts = subBreakdown.Select(x => x.DefPct).ToList();
                sess.DefenseModelIndex = 0;
                var firstSub = subBreakdown[0];
                string msgSub = $"⚓ درصد دفاع زیردریایی – مدل 1/{subBreakdown.Count}\n\n🔧 مدل: {firstSub.ModelName}\n📊 موجودی: {firstSub.Count:N0}\n📈 فعلی: {firstSub.DefPct}%\n\nچند درصد در دفاع باشد؟";
                await bot.EditMessageTextAsync(cb.Message.Chat.Id, cb.Message.MessageId, msgSub, replyMarkup: BuildModelPercentKeyboard(cid, "submarines", 0), cancellationToken: ct);
                return;
            }
            // Boats exist
            sess.DefenseCurrentCategory = "boats";
            sess.DefenseModelNames = boatBreakdown.Select(x => x.ModelName).ToList();
            sess.DefenseModelCounts = boatBreakdown.Select(x => x.Count).ToList();
            sess.DefenseModelPcts = boatBreakdown.Select(x => x.DefPct).ToList();
            sess.DefenseModelIndex = 0;
            // Store plane total in a temp field (use DefenseFighters as temp)
            sess.DefenseFighters = totalDefPlanes;
            var firstBoat = boatBreakdown[0];
            string msgBoat = $"🚤 درصد دفاع قایق – مدل 1/{boatBreakdown.Count}\n\n🔧 مدل: {firstBoat.ModelName}\n📊 موجودی: {firstBoat.Count:N0}\n📈 فعلی: {firstBoat.DefPct}%\n\nچند درصد در دفاع باشد؟";
            await bot.EditMessageTextAsync(cb.Message.Chat.Id, cb.Message.MessageId, msgBoat, replyMarkup: BuildModelPercentKeyboard(cid, "boats", 0), cancellationToken: ct);
            return;
        }
        else if (category == "boats")
        {
            // Boats finished, go to submarines
            var subBreakdown = GetDefenseBreakdown(c, "submarines");
            if (subBreakdown.Count == 0)
            {
                // Finalize with existing totals
                long defT = sess.DefenseTanks;
                long defS = sess.DefenseSoldiers;
                long defF = sess.DefenseFighters; // plane total stored earlier
                int dtp = sess.DefTankPct > 0 ? sess.DefTankPct : 100;
                int dsp = sess.DefSoldierPct > 0 ? sess.DefSoldierPct : 100;
                Database.UpdateDefenseFull(uid, cid, defT, defS, defF, c.DefenseStrategy, c.DefenseTactic, dtp, dsp, 100);
                Database.ReconcileDefense(uid, cid);
                EndSession(uid);
                DeleteNow(cb.Message.Chat.Id, cb.Message.MessageId);
                await SendDefenseStatus(uid, uid, cid, ct);
                return;
            }
            sess.DefenseCurrentCategory = "submarines";
            sess.DefenseModelNames = subBreakdown.Select(x => x.ModelName).ToList();
            sess.DefenseModelCounts = subBreakdown.Select(x => x.Count).ToList();
            sess.DefenseModelPcts = subBreakdown.Select(x => x.DefPct).ToList();
            sess.DefenseModelIndex = 0;
            var firstSub = subBreakdown[0];
            string msgSub = $"⚓ درصد دفاع زیردریایی – مدل 1/{subBreakdown.Count}\n\n🔧 مدل: {firstSub.ModelName}\n📊 موجودی: {firstSub.Count:N0}\n📈 فعلی: {firstSub.DefPct}%\n\nچند درصد در دفاع باشد؟";
            await bot.EditMessageTextAsync(cb.Message.Chat.Id, cb.Message.MessageId, msgSub, replyMarkup: BuildModelPercentKeyboard(cid, "submarines", 0), cancellationToken: ct);
            return;
        }
        else if (category == "submarines")
        {
            // All naval finished, finalize
            long defT = sess.DefenseTanks;
            long defS = sess.DefenseSoldiers;
            long defF = sess.DefenseFighters; // includes plane total
            int dtp = sess.DefTankPct > 0 ? sess.DefTankPct : 100;
            int dsp = sess.DefSoldierPct > 0 ? sess.DefSoldierPct : 100;
            Database.UpdateDefenseFull(uid, cid, defT, defS, defF, c.DefenseStrategy, c.DefenseTactic, dtp, dsp, 100);
            Database.ReconcileDefense(uid, cid);
            EndSession(uid);
            DeleteNow(cb.Message.Chat.Id, cb.Message.MessageId);
            await SendDefenseStatus(uid, uid, cid, ct);
            return;
        }
    }

    static async Task HandleDefenseTacticCallback(
        CallbackQuery cb,
        string[] parts,
        CancellationToken ct)
    {
        if (parts.Length < 2)
            return;

        long uid = cb.From.Id;

        if (!TryParseLong(parts[1], out long chatId))
            return;

        await bot.AnswerCallbackQueryAsync(
            cb.Id,
            cancellationToken: ct
        );

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    "🛡 دفاع منسجم",
                    $"defense_tactic_select:{chatId}:1"
                )
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    "💥 دفاع و ضدحمله پراکنده",
                    $"defense_tactic_select:{chatId}:2"
                )
            }
        });

        await SendTemp(
            uid,
            GroundDefenseStrategyGuide,
            markup: keyboard,
            ct: ct
        );
    }

    static async Task HandleDefenseTacticSelectCallback(
        CallbackQuery cb,
        string[] parts,
        CancellationToken ct)
    {
        if (parts.Length < 3)
            return;

        long uid = cb.From.Id;

        if (!TryParseLong(parts[1], out long chatId) ||
            !TryParseInt(parts[2], out int strategy) ||
            strategy is < 1 or > 2)
            return;

        if (parts.Length >= 4 &&
            TryParseInt(parts[3], out int tactic))
        {
            if (tactic is < 1 or > 2)
                return;

            var country =
                Database.GetCountry(uid, chatId);

            if (country == null)
            {
                await bot.AnswerCallbackQueryAsync(
                    cb.Id,
                    "❌ کشور یافت نشد.",
                    cancellationToken: ct
                );
                return;
            }

            Database.UpdateDefense(
                uid,
                chatId,
                country.DefenseTanks,
                country.DefenseSoldiers,
                strategy,
                tactic
            );

            await bot.AnswerCallbackQueryAsync(
                cb.Id,
                "✅ استراتژی و تاکتیک دفاعی ذخیره شد.",
                cancellationToken: ct
            );

            if (cb.Message != null)
            {
                DeleteNow(
                    cb.Message.Chat.Id,
                    cb.Message.MessageId
                );
            }

            await SendDefenseStatus(
                uid,
                uid,
                chatId,
                ct
            );
            return;
        }

        await bot.AnswerCallbackQueryAsync(
            cb.Id,
            cancellationToken: ct
        );

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    GroundDefenseTacticName(strategy, 1),
                    $"defense_tactic_select:{chatId}:{strategy}:1"
                )
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    GroundDefenseTacticName(strategy, 2),
                    $"defense_tactic_select:{chatId}:{strategy}:2"
                )
            }
        });

        string guide =
            GroundDefenseTacticGuide(strategy);

        if (cb.Message != null)
        {
            await bot.EditMessageTextAsync(
                cb.Message.Chat.Id,
                cb.Message.MessageId,
                guide,
                replyMarkup: keyboard,
                cancellationToken: ct
            );
        }
        else
        {
            await SendTemp(
                uid,
                guide,
                markup: keyboard,
                ct: ct
            );
        }
    }

    static async Task HandleAirDefStrategyCallback(
        CallbackQuery cb,
        string[] parts,
        CancellationToken ct)
    {
        if (parts.Length < 2 || cb.Message == null)
            return;

        long uid = cb.From.Id;

        if (!TryParseLong(parts[1], out long chatId))
            return;

        if (parts.Length >= 3 &&
            TryParseInt(parts[2], out int strategy))
        {
            if (strategy is < 1 or > 2)
                return;

            await bot.AnswerCallbackQueryAsync(
                cb.Id,
                cancellationToken: ct
            );

            string tacticOne =
                AirDefenseTacticName(strategy, 1);

            string tacticTwo =
                AirDefenseTacticName(strategy, 2);

            if (strategy == 1)
                tacticTwo += " 🔒";

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        tacticOne,
                        $"airdef_tactic:{chatId}:{strategy}:1"
                    )
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        tacticTwo,
                        $"airdef_tactic:{chatId}:{strategy}:2"
                    )
                }
            });

            await bot.EditMessageTextAsync(
                cb.Message.Chat.Id,
                cb.Message.MessageId,
                AirDefenseTacticGuide(strategy),
                replyMarkup: keyboard,
                cancellationToken: ct
            );
            return;
        }

        await bot.AnswerCallbackQueryAsync(
            cb.Id,
            cancellationToken: ct
        );

        var strategyKeyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    "🗺 دفاع منطقه‌ای",
                    $"airdef_strategy:{chatId}:1"
                )
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    "🎯 دفاع نقطه‌ای",
                    $"airdef_strategy:{chatId}:2"
                )
            }
        });

        await SendTemp(
            uid,
            AirDefenseStrategyGuide,
            markup: strategyKeyboard,
            ct: ct
        );
    }

    static async Task HandleAirDefTacticCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length < 4 || cb.Message == null) return;
        long uid = cb.From.Id;
        if (!TryParseLong(parts[1], out long cid) || !TryParseInt(parts[2], out int str) || !TryParseInt(parts[3], out int tac)) return;
        if (str == 1 && tac == 2) { await bot.AnswerCallbackQueryAsync(cb.Id, "📡 رادار ندارید! قفل.", showAlert: true, cancellationToken: ct); return; }
        var c = Database.GetCountry(uid, cid);
        if (c == null) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌", cancellationToken: ct); return; }
        c.AirDefStrategy = str; c.AirDefTactic = tac;
        Database.UpdateCountryFull(c);
        await bot.AnswerCallbackQueryAsync(cb.Id, "✅ ذخیره شد.", cancellationToken: ct);
        DeleteNow(cb.Message.Chat.Id, cb.Message.MessageId);
        await SendDefenseStatus(uid, uid, cid, ct);
    }
}

// ===== MERGED ADMIN CORE =====
sealed class AdminAccount
{
    public long AdminId { get; set; }
    public string DisplayName { get; set; } = "";
    public long AddedBy { get; set; }
    public bool IsOwner { get; set; }
    public bool IsActive { get; set; }
    public long CreatedAtMs { get; set; }
    public long LastSeenMs { get; set; }
}

sealed class AdminAuditEntry
{
    public long Id { get; set; }
    public long AdminId { get; set; }
    public string Action { get; set; } = "";
    public string TargetType { get; set; } = "";
    public string TargetId { get; set; } = "";
    public string Details { get; set; } = "";
    public bool Success { get; set; }
    public long CreatedAtMs { get; set; }
}

sealed class AdminDashboardStats
{
    public int Countries { get; set; }
    public int Players { get; set; }
    public int Groups { get; set; }
    public int Alliances { get; set; }
    public int ActiveTransfers { get; set; }
    public int ActiveDeployments { get; set; }
    public int ActiveAdmins { get; set; }
    public int AuditEntries { get; set; }
}

sealed record AdminPermissionItem(
    string Code,
    string Title,
    string Category
);

static class AdminPermissionCatalog
{
    public static readonly AdminPermissionItem[] All =
    {
        new("DASH", "مشاهده داشبورد", "گزارش"),

        new("P_VIEW", "مشاهده پلیرها", "پلیر"),
        new("P_EDIT", "ویرایش اطلاعات پلیر", "پلیر"),
        new("P_BAN", "بن و رفع‌بن پلیر", "پلیر"),

        new("C_VIEW", "مشاهده کشورها", "کشور"),
        new("C_RES", "تغییر منابع اقتصادی", "کشور"),
        new("C_ARMY", "تغییر نیروهای نظامی", "کشور"),
        new("C_DELETE", "حذف کشور", "کشور"),

        new("G_VIEW", "مشاهده گروه‌ها", "گروه"),
        new("G_EDIT", "مدیریت گروه‌ها", "گروه"),
        new("ALLY", "مدیریت اتحادها", "گروه"),

        new("ROYAL", "مدیریت رویال‌کوین", "اقتصاد"),
        new("E_GLOBAL", "تنظیمات اقتصاد جهانی", "اقتصاد"),

        new("W_VIEW", "مشاهده وضعیت جنگ", "جنگ"),
        new("W_EDIT", "مدیریت جنگ و سپر", "جنگ"),

        new("O_VIEW", "مشاهده عملیات فعال", "عملیات"),
        new("O_EDIT", "مدیریت ترنسفر و صف‌آرایی", "عملیات"),

        new("ANN", "ارسال اعلامیه", "ارتباطات"),
        new("SET", "تغییر تنظیمات آلیس", "تنظیمات"),

        new("BACKUP", "دریافت بکاپ", "نگهداری"),
        new("RESTORE", "بازیابی دیتابیس", "نگهداری"),
        new("AUDIT", "مشاهده لاگ ممیزی", "نگهداری")
    };

    public static AdminPermissionItem? Find(string code) =>
        All.FirstOrDefault(
            x => string.Equals(
                x.Code,
                code,
                StringComparison.Ordinal
            )
        );

    public static bool Exists(string code) =>
        Find(code) != null;
}

static partial class Database
{
    public static void InitAdminPanel(long ownerId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();

        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS AdminUsers(
                AdminId INTEGER PRIMARY KEY,
                DisplayName TEXT NOT NULL DEFAULT '',
                AddedBy INTEGER NOT NULL DEFAULT 0,
                IsOwner INTEGER NOT NULL DEFAULT 0,
                IsActive INTEGER NOT NULL DEFAULT 1,
                CreatedAtMs INTEGER NOT NULL,
                LastSeenMs INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS AdminPermissions(
                AdminId INTEGER NOT NULL,
                PermissionCode TEXT NOT NULL,
                GrantedBy INTEGER NOT NULL,
                GrantedAtMs INTEGER NOT NULL,
                PRIMARY KEY(AdminId, PermissionCode)
            );

            CREATE INDEX IF NOT EXISTS IX_AdminPermissions_AdminId
                ON AdminPermissions(AdminId);

            CREATE TABLE IF NOT EXISTS AdminAudit(
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                AdminId INTEGER NOT NULL,
                Action TEXT NOT NULL,
                TargetType TEXT NOT NULL DEFAULT '',
                TargetId TEXT NOT NULL DEFAULT '',
                Details TEXT NOT NULL DEFAULT '',
                Success INTEGER NOT NULL DEFAULT 1,
                CreatedAtMs INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_AdminAudit_CreatedAtMs
                ON AdminAudit(CreatedAtMs DESC);

            CREATE INDEX IF NOT EXISTS IX_AdminAudit_AdminId
                ON AdminAudit(AdminId, CreatedAtMs DESC);

            CREATE TABLE IF NOT EXISTS AdminPendingActions(
                ActionId TEXT PRIMARY KEY,
                AdminId INTEGER NOT NULL,
                ActionType TEXT NOT NULL,
                TargetType TEXT NOT NULL DEFAULT '',
                TargetId TEXT NOT NULL DEFAULT '',
                Payload TEXT NOT NULL DEFAULT '',
                Stage INTEGER NOT NULL DEFAULT 1,
                ExpiresAtMs INTEGER NOT NULL,
                CreatedAtMs INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_AdminPendingActions_Expires
                ON AdminPendingActions(ExpiresAtMs);

            CREATE TABLE IF NOT EXISTS BannedUsers(
                UserId INTEGER PRIMARY KEY,
                Reason TEXT NOT NULL DEFAULT '',
                BannedBy INTEGER NOT NULL DEFAULT 0,
                BannedAtMs INTEGER NOT NULL
            );
        ";

        cmd.ExecuteNonQuery();

        // Extra migration for BannedUsers on old DBs
        try
        {
            using var mig = con.CreateCommand();
            mig.CommandText = "CREATE TABLE IF NOT EXISTS BannedUsers(UserId INTEGER PRIMARY KEY, Reason TEXT NOT NULL DEFAULT '', BannedBy INTEGER NOT NULL DEFAULT 0, BannedAtMs INTEGER NOT NULL);";
            mig.ExecuteNonQuery();
        }
        catch { }

        long nowMs =
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        using var ownerCmd = con.CreateCommand();

        ownerCmd.CommandText = @"
            INSERT INTO AdminUsers(
                AdminId,
                DisplayName,
                AddedBy,
                IsOwner,
                IsActive,
                CreatedAtMs,
                LastSeenMs
            )
            VALUES(
                $adminId,
                'مالک اصلی آلیس',
                $adminId,
                1,
                1,
                $nowMs,
                $nowMs
            )
            ON CONFLICT(AdminId) DO UPDATE SET
                IsOwner = 1,
                IsActive = 1,
                DisplayName =
                    CASE
                        WHEN AdminUsers.DisplayName = ''
                        THEN 'مالک اصلی آلیس'
                        ELSE AdminUsers.DisplayName
                    END;
        ";

        ownerCmd.Parameters.AddWithValue(
            "$adminId",
            ownerId
        );

        ownerCmd.Parameters.AddWithValue(
            "$nowMs",
            nowMs
        );

        ownerCmd.ExecuteNonQuery();

        CleanupExpiredAdminActions();
    }

    public static bool IsAdminActive(long adminId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();

        cmd.CommandText = @"
            SELECT COUNT(*)
            FROM AdminUsers
            WHERE AdminId = $adminId
              AND IsActive = 1;
        ";

        cmd.Parameters.AddWithValue(
            "$adminId",
            adminId
        );

        return Convert.ToInt64(
            cmd.ExecuteScalar()
        ) > 0;
    }

    public static AdminAccount? GetAdmin(long adminId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();

        cmd.CommandText = @"
            SELECT
                AdminId,
                DisplayName,
                AddedBy,
                IsOwner,
                IsActive,
                CreatedAtMs,
                LastSeenMs
            FROM AdminUsers
            WHERE AdminId = $adminId;
        ";

        cmd.Parameters.AddWithValue(
            "$adminId",
            adminId
        );

        using var reader = cmd.ExecuteReader();

        if (!reader.Read())
            return null;

        return ReadAdminAccount(reader);
    }

    public static List<AdminAccount> GetAdmins()
    {
        var result = new List<AdminAccount>();

        using var con = OpenCon();
        using var cmd = con.CreateCommand();

        cmd.CommandText = @"
            SELECT
                AdminId,
                DisplayName,
                AddedBy,
                IsOwner,
                IsActive,
                CreatedAtMs,
                LastSeenMs
            FROM AdminUsers
            ORDER BY
                IsOwner DESC,
                IsActive DESC,
                CreatedAtMs DESC;
        ";

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
            result.Add(ReadAdminAccount(reader));

        return result;
    }

    private static AdminAccount ReadAdminAccount(
        SqliteDataReader reader)
    {
        return new AdminAccount
        {
            AdminId = reader.GetInt64(0),
            DisplayName = reader.GetString(1),
            AddedBy = reader.GetInt64(2),
            IsOwner = reader.GetInt64(3) != 0,
            IsActive = reader.GetInt64(4) != 0,
            CreatedAtMs = reader.GetInt64(5),
            LastSeenMs = reader.GetInt64(6)
        };
    }

    public static void AddOrReactivateAdmin(
        long adminId,
        string displayName,
        long addedBy)
    {
        long nowMs =
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        using var con = OpenCon();
        using var cmd = con.CreateCommand();

        cmd.CommandText = @"
            INSERT INTO AdminUsers(
                AdminId,
                DisplayName,
                AddedBy,
                IsOwner,
                IsActive,
                CreatedAtMs,
                LastSeenMs
            )
            VALUES(
                $adminId,
                $displayName,
                $addedBy,
                0,
                1,
                $nowMs,
                0
            )
            ON CONFLICT(AdminId) DO UPDATE SET
                DisplayName = excluded.DisplayName,
                AddedBy = excluded.AddedBy,
                IsActive = 1;
        ";

        cmd.Parameters.AddWithValue(
            "$adminId",
            adminId
        );

        cmd.Parameters.AddWithValue(
            "$displayName",
            displayName
        );

        cmd.Parameters.AddWithValue(
            "$addedBy",
            addedBy
        );

        cmd.Parameters.AddWithValue(
            "$nowMs",
            nowMs
        );

        cmd.ExecuteNonQuery();
    }

    public static void SetAdminActive(
        long adminId,
        bool active)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();

        cmd.CommandText = @"
            UPDATE AdminUsers
            SET IsActive = $active
            WHERE AdminId = $adminId
              AND IsOwner = 0;
        ";

        cmd.Parameters.AddWithValue(
            "$active",
            active ? 1 : 0
        );

        cmd.Parameters.AddWithValue(
            "$adminId",
            adminId
        );

        cmd.ExecuteNonQuery();
    }

    public static bool DeleteAdmin(long adminId)
    {
        using var con = OpenCon();
        using var transaction = con.BeginTransaction();

        using var check = con.CreateCommand();
        check.Transaction = transaction;

        check.CommandText = @"
            SELECT IsOwner
            FROM AdminUsers
            WHERE AdminId = $adminId;
        ";

        check.Parameters.AddWithValue(
            "$adminId",
            adminId
        );

        object? value = check.ExecuteScalar();

        if (value == null ||
            value == DBNull.Value ||
            Convert.ToInt64(value) != 0)
        {
            transaction.Rollback();
            return false;
        }

        using var permissions = con.CreateCommand();
        permissions.Transaction = transaction;

        permissions.CommandText = @"
            DELETE FROM AdminPermissions
            WHERE AdminId = $adminId;
        ";

        permissions.Parameters.AddWithValue(
            "$adminId",
            adminId
        );

        permissions.ExecuteNonQuery();

        using var admin = con.CreateCommand();
        admin.Transaction = transaction;

        admin.CommandText = @"
            DELETE FROM AdminUsers
            WHERE AdminId = $adminId
              AND IsOwner = 0;
        ";

        admin.Parameters.AddWithValue(
            "$adminId",
            adminId
        );

        int affected = admin.ExecuteNonQuery();

        transaction.Commit();
        return affected > 0;
    }

    public static void TouchAdmin(long adminId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();

        cmd.CommandText = @"
            UPDATE AdminUsers
            SET LastSeenMs = $nowMs
            WHERE AdminId = $adminId;
        ";

        cmd.Parameters.AddWithValue(
            "$nowMs",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );

        cmd.Parameters.AddWithValue(
            "$adminId",
            adminId
        );

        cmd.ExecuteNonQuery();
    }

    public static HashSet<string> GetAdminPermissions(
        long adminId)
    {
        var result =
            new HashSet<string>(
                StringComparer.Ordinal
            );

        using var con = OpenCon();
        using var cmd = con.CreateCommand();

        cmd.CommandText = @"
            SELECT PermissionCode
            FROM AdminPermissions
            WHERE AdminId = $adminId;
        ";

        cmd.Parameters.AddWithValue(
            "$adminId",
            adminId
        );

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
            result.Add(reader.GetString(0));

        return result;
    }

    public static bool HasAdminPermission(
        long adminId,
        string permissionCode)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();

        cmd.CommandText = @"
            SELECT COUNT(*)
            FROM AdminPermissions
            WHERE AdminId = $adminId
              AND PermissionCode = $permissionCode;
        ";

        cmd.Parameters.AddWithValue(
            "$adminId",
            adminId
        );

        cmd.Parameters.AddWithValue(
            "$permissionCode",
            permissionCode
        );

        return Convert.ToInt64(
            cmd.ExecuteScalar()
        ) > 0;
    }

    public static void SetAdminPermission(
        long adminId,
        string permissionCode,
        bool enabled,
        long grantedBy)
    {
        if (!AdminPermissionCatalog.Exists(permissionCode))
            throw new ArgumentException(
                "Unknown permission.",
                nameof(permissionCode)
            );

        using var con = OpenCon();
        using var cmd = con.CreateCommand();

        if (enabled)
        {
            cmd.CommandText = @"
                INSERT INTO AdminPermissions(
                    AdminId,
                    PermissionCode,
                    GrantedBy,
                    GrantedAtMs
                )
                VALUES(
                    $adminId,
                    $permissionCode,
                    $grantedBy,
                    $nowMs
                )
                ON CONFLICT(
                    AdminId,
                    PermissionCode
                ) DO UPDATE SET
                    GrantedBy = excluded.GrantedBy,
                    GrantedAtMs = excluded.GrantedAtMs;
            ";

            cmd.Parameters.AddWithValue(
                "$grantedBy",
                grantedBy
            );

            cmd.Parameters.AddWithValue(
                "$nowMs",
                DateTimeOffset.UtcNow
                    .ToUnixTimeMilliseconds()
            );
        }
        else
        {
            cmd.CommandText = @"
                DELETE FROM AdminPermissions
                WHERE AdminId = $adminId
                  AND PermissionCode = $permissionCode;
            ";
        }

        cmd.Parameters.AddWithValue(
            "$adminId",
            adminId
        );

        cmd.Parameters.AddWithValue(
            "$permissionCode",
            permissionCode
        );

        cmd.ExecuteNonQuery();
    }

    public static void SetAllAdminPermissions(
        long adminId,
        bool enabled,
        long grantedBy)
    {
        using var con = OpenCon();
        using var transaction = con.BeginTransaction();

        using var clear = con.CreateCommand();
        clear.Transaction = transaction;

        clear.CommandText = @"
            DELETE FROM AdminPermissions
            WHERE AdminId = $adminId;
        ";

        clear.Parameters.AddWithValue(
            "$adminId",
            adminId
        );

        clear.ExecuteNonQuery();

        if (enabled)
        {
            long nowMs =
                DateTimeOffset.UtcNow
                    .ToUnixTimeMilliseconds();

            foreach (var permission in
                     AdminPermissionCatalog.All)
            {
                using var insert = con.CreateCommand();
                insert.Transaction = transaction;

                insert.CommandText = @"
                    INSERT INTO AdminPermissions(
                        AdminId,
                        PermissionCode,
                        GrantedBy,
                        GrantedAtMs
                    )
                    VALUES(
                        $adminId,
                        $code,
                        $grantedBy,
                        $nowMs
                    );
                ";

                insert.Parameters.AddWithValue(
                    "$adminId",
                    adminId
                );

                insert.Parameters.AddWithValue(
                    "$code",
                    permission.Code
                );

                insert.Parameters.AddWithValue(
                    "$grantedBy",
                    grantedBy
                );

                insert.Parameters.AddWithValue(
                    "$nowMs",
                    nowMs
                );

                insert.ExecuteNonQuery();
            }
        }

        transaction.Commit();
    }

    public static void WriteAdminAudit(
        long adminId,
        string action,
        string targetType = "",
        string targetId = "",
        string details = "",
        bool success = true)
    {
        try
        {
            using var con = OpenCon();
            using var cmd = con.CreateCommand();

            cmd.CommandText = @"
                INSERT INTO AdminAudit(
                    AdminId,
                    Action,
                    TargetType,
                    TargetId,
                    Details,
                    Success,
                    CreatedAtMs
                )
                VALUES(
                    $adminId,
                    $action,
                    $targetType,
                    $targetId,
                    $details,
                    $success,
                    $nowMs
                );
            ";

            cmd.Parameters.AddWithValue(
                "$adminId",
                adminId
            );

            cmd.Parameters.AddWithValue(
                "$action",
                action
            );

            cmd.Parameters.AddWithValue(
                "$targetType",
                targetType
            );

            cmd.Parameters.AddWithValue(
                "$targetId",
                targetId
            );

            cmd.Parameters.AddWithValue(
                "$details",
                details
            );

            cmd.Parameters.AddWithValue(
                "$success",
                success ? 1 : 0
            );

            cmd.Parameters.AddWithValue(
                "$nowMs",
                DateTimeOffset.UtcNow
                    .ToUnixTimeMilliseconds()
            );

            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[ADMIN AUDIT ERR] {ex.Message}"
            );
        }
    }

    public static List<AdminAuditEntry> GetRecentAdminAudit(
        int limit = 20)
    {
        limit = Math.Clamp(limit, 1, 100);

        var result = new List<AdminAuditEntry>();

        using var con = OpenCon();
        using var cmd = con.CreateCommand();

        cmd.CommandText = @"
            SELECT
                Id,
                AdminId,
                Action,
                TargetType,
                TargetId,
                Details,
                Success,
                CreatedAtMs
            FROM AdminAudit
            ORDER BY CreatedAtMs DESC
            LIMIT $limit;
        ";

        cmd.Parameters.AddWithValue(
            "$limit",
            limit
        );

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            result.Add(new AdminAuditEntry
            {
                Id = reader.GetInt64(0),
                AdminId = reader.GetInt64(1),
                Action = reader.GetString(2),
                TargetType = reader.GetString(3),
                TargetId = reader.GetString(4),
                Details = reader.GetString(5),
                Success = reader.GetInt64(6) != 0,
                CreatedAtMs = reader.GetInt64(7)
            });
        }

        return result;
    }

    public static AdminDashboardStats GetAdminDashboardStats()
    {
        long nowMs =
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        using var con = OpenCon();
        using var cmd = con.CreateCommand();

        cmd.CommandText = @"
            SELECT
                (SELECT COUNT(*) FROM Countries),
                (
                    SELECT COUNT(DISTINCT OwnerId)
                    FROM Countries
                ),
                (
                    SELECT COUNT(DISTINCT ChatId)
                    FROM Countries
                ),
                (SELECT COUNT(*) FROM Alliances),
                (
                    SELECT COUNT(*)
                    FROM Transfers
                    WHERE ArriveAtMs > $nowMs
                ),
                (
                    SELECT COUNT(*)
                    FROM Deployments
                    WHERE EndAtMs > $nowMs
                ),
                (
                    SELECT COUNT(*)
                    FROM AdminUsers
                    WHERE IsActive = 1
                ),
                (SELECT COUNT(*) FROM AdminAudit);
        ";

        cmd.Parameters.AddWithValue(
            "$nowMs",
            nowMs
        );

        using var reader = cmd.ExecuteReader();

        if (!reader.Read())
            return new AdminDashboardStats();

        return new AdminDashboardStats
        {
            Countries =
                Convert.ToInt32(reader.GetInt64(0)),
            Players =
                Convert.ToInt32(reader.GetInt64(1)),
            Groups =
                Convert.ToInt32(reader.GetInt64(2)),
            Alliances =
                Convert.ToInt32(reader.GetInt64(3)),
            ActiveTransfers =
                Convert.ToInt32(reader.GetInt64(4)),
            ActiveDeployments =
                Convert.ToInt32(reader.GetInt64(5)),
            ActiveAdmins =
                Convert.ToInt32(reader.GetInt64(6)),
            AuditEntries =
                Convert.ToInt32(reader.GetInt64(7))
        };
    }

    public static void CleanupExpiredAdminActions()
    {
        try
        {
            using var con = OpenCon();
            using var cmd = con.CreateCommand();

            cmd.CommandText = @"
                DELETE FROM AdminPendingActions
                WHERE ExpiresAtMs <= $nowMs;
            ";

            cmd.Parameters.AddWithValue(
                "$nowMs",
                DateTimeOffset.UtcNow
                    .ToUnixTimeMilliseconds()
            );

            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[ADMIN ACTION CLEANUP ERR] {ex.Message}"
            );
        }
    }

    // ================= NEW ADMIN HELPERS =================
    public static List<Country> GetCountriesByOwnerId(long ownerId)
    {
        var list = new List<Country>();
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = $"SELECT {COUNTRY_COLS} FROM Countries WHERE OwnerId=@oid";
        cmd.Parameters.AddWithValue("@oid", ownerId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadCountry(r));
        return list;
    }

    public static List<Country> SearchCountriesByName(string namePart, int limit = 20)
    {
        var list = new List<Country>();
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = $"SELECT {COUNTRY_COLS} FROM Countries WHERE Name LIKE @pat LIMIT @lim";
        cmd.Parameters.AddWithValue("@pat", $"%{namePart}%");
        cmd.Parameters.AddWithValue("@lim", limit);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadCountry(r));
        return list;
    }

    public static List<Country> GetSiegedCountries(int limit = 20)
    {
        var list = new List<Country>();
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = $"SELECT {COUNTRY_COLS} FROM Countries WHERE Besieged>0 ORDER BY Besieged DESC LIMIT @lim";
        cmd.Parameters.AddWithValue("@lim", limit);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadCountry(r));
        return list;
    }

    public static void BanUser(long userId, string reason, long bannedBy)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO BannedUsers(UserId, Reason, BannedBy, BannedAtMs) VALUES(@uid, @reason, @by, @ms)";
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.Parameters.AddWithValue("@reason", reason);
        cmd.Parameters.AddWithValue("@by", bannedBy);
        cmd.Parameters.AddWithValue("@ms", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        cmd.ExecuteNonQuery();

        // Delete all countries of this user
        using var del = con.CreateCommand();
        del.CommandText = "DELETE FROM Countries WHERE OwnerId=@uid";
        del.Parameters.AddWithValue("@uid", userId);
        del.ExecuteNonQuery();
    }

    public static void UnbanUser(long userId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "DELETE FROM BannedUsers WHERE UserId=@uid";
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.ExecuteNonQuery();
    }

    public static bool IsUserBanned(long userId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM BannedUsers WHERE UserId=@uid LIMIT 1";
        cmd.Parameters.AddWithValue("@uid", userId);
        using var r = cmd.ExecuteReader();
        return r.Read();
    }

    public static List<(long UserId, string Reason, long BannedBy, long BannedAtMs)> GetBannedUsers(int limit = 50)
    {
        var list = new List<(long, string, long, long)>();
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT UserId, Reason, BannedBy, BannedAtMs FROM BannedUsers ORDER BY BannedAtMs DESC LIMIT @lim";
        cmd.Parameters.AddWithValue("@lim", limit);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add((r.GetInt64(0), r.GetString(1), r.GetInt64(2), r.GetInt64(3)));
        }
        return list;
    }

    public static List<(long Id, string Timestamp, long ChatId, long AttackerId, long DefenderId, string AttackerName, string DefenderName, string Winner, double Penetration, int SuccessPercent)> GetRecentBattles(int limit = 20)
    {
        var list = new List<(long, string, long, long, long, string, string, string, double, int)>();
        try
        {
            using var con = OpenCon();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT Id, Timestamp, ChatId, AttackerId, DefenderId, AttackerName, DefenderName, Winner, PenetrationKm, SuccessPercent FROM WarBattles ORDER BY Id DESC LIMIT @lim";
            cmd.Parameters.AddWithValue("@lim", limit);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add((
                    r.GetInt64(0),
                    r.GetString(1),
                    r.GetInt64(2),
                    r.GetInt64(3),
                    r.GetInt64(4),
                    r.GetString(5),
                    r.GetString(6),
                    r.GetString(7),
                    r.IsDBNull(8) ? 0 : r.GetDouble(8),
                    r.IsDBNull(9) ? 0 : r.GetInt32(9)
                ));
            }
        }
        catch { }
        return list;
    }
}

// ===== MERGED ADMIN PANEL =====
sealed class AdminInputRequest
{
    public string Kind { get; set; } = "";
    public long ExpiresAtMs { get; set; }
    public long TargetId { get; set; } = 0;
    public long ChatId { get; set; } = 0;
    public string Extra { get; set; } = "";
}

partial class Program
{
    static readonly ConcurrentDictionary<long, AdminInputRequest>
        adminInputRequests = new();

    static bool IsPanelOwner(long userId) =>
        userId == OWNER_ID;

    static bool IsPanelAdmin(long userId) =>
        userId == OWNER_ID ||
        Database.IsAdminActive(userId);

    static bool CanAdmin(long userId, string permissionCode) =>
        userId == OWNER_ID ||
        Database.HasAdminPermission(
            userId,
            permissionCode
        );

    static bool CanAdminAny(
        long userId,
        params string[] permissionCodes)
    {
        if (userId == OWNER_ID)
            return true;

        foreach (string code in permissionCodes)
        {
            if (Database.HasAdminPermission(userId, code))
                return true;
        }

        return false;
    }

    static KeyboardButton AdminKeyboardButton(string text) =>
        new(text);

    static ReplyKeyboardMarkup BuildAdminReplyKeyboard(long userId)
    {
        var rows = new List<KeyboardButton[]>
        {
            new[]
            {
                AdminKeyboardButton("🧭 پنل مدیریت"),
                AdminKeyboardButton("📊 داشبورد")
            }
        };

        if (IsPanelOwner(userId))
        {
            rows.Add(new[]
            {
                AdminKeyboardButton("👮 مدیریت ادمین‌ها"),
                AdminKeyboardButton("📜 لاگ مدیریتی")
            });
        }
        else if (CanAdmin(userId, "AUDIT"))
        {
            rows.Add(new[]
            {
                AdminKeyboardButton("📜 لاگ مدیریتی")
            });
        }

        if (CanAdminAny(userId, "P_VIEW", "C_VIEW"))
        {
            rows.Add(new[]
            {
                AdminKeyboardButton("🔎 جستجوی پلیر"),
                AdminKeyboardButton("🌍 مدیریت کشور")
            });
        }

        if (CanAdminAny(userId, "G_VIEW", "G_EDIT", "ALLY"))
        {
            rows.Add(new[]
            {
                AdminKeyboardButton("👥 مدیریت گروه"),
                AdminKeyboardButton("🤝 مدیریت اتحاد")
            });
        }

        if (CanAdminAny(userId, "ROYAL", "E_GLOBAL", "ANN"))
        {
            rows.Add(new[]
            {
                AdminKeyboardButton("💎 اقتصاد و رویال"),
                AdminKeyboardButton("📢 اعلامیه")
            });
        }

        if (CanAdminAny(userId, "W_VIEW", "W_EDIT", "O_VIEW", "O_EDIT"))
        {
            rows.Add(new[]
            {
                AdminKeyboardButton("⚔️ جنگ و عملیات")
            });
        }

        if (CanAdminAny(userId, "SET", "BACKUP", "RESTORE"))
        {
            rows.Add(new[]
            {
                AdminKeyboardButton("🗄 نگهداری"),
                AdminKeyboardButton("⚙️ تنظیمات")
            });
        }

        rows.Add(new[]
        {
            AdminKeyboardButton("❌ بستن پنل")
        });

        return new ReplyKeyboardMarkup(rows)
        {
            ResizeKeyboard = true
        };
    }

    static async Task<bool> TryHandleAdminPrivateMessageAsync(
        Message message,
        User user,
        CancellationToken ct)
    {
        long userId = user.Id;

        if (!IsPanelAdmin(userId))
            return false;

        Database.TouchAdmin(userId);

        string text = message.Text?.Trim() ?? "";

        if (adminInputRequests.TryGetValue(userId, out var request))
        {
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (request.ExpiresAtMs <= nowMs)
            {
                adminInputRequests.TryRemove(userId, out _);
                await SendPermanent(userId, "⌛ زمان عملیات مدیریتی تمام شد.", ct: ct);
                return true;
            }

            // Dispatch by Kind
            switch (request.Kind)
            {
                case "add_admin":
                    await HandleAdminAddInput(userId, text, ct);
                    return true;
                case "search_player":
                    await HandleAdminSearchPlayerInput(userId, text, ct);
                    return true;
                case "search_country":
                    await HandleAdminSearchCountryInput(userId, text, ct);
                    return true;
                case "search_group":
                    await HandleAdminSearchGroupInput(userId, text, ct);
                    return true;
                case "edit_country_money":
                case "edit_country_iron":
                case "edit_country_pop":
                case "edit_country_soldiers":
                case "edit_country_tanks":
                case "edit_country_planes":
                case "edit_country_bombers":
                case "edit_country_antiair":
                    await HandleAdminEditCountryInput(userId, text, request, ct);
                    return true;
                case "ban_reason":
                    await HandleAdminBanReasonInput(userId, text, request, ct);
                    return true;
                case "royal_add":
                case "royal_deduct":
                    await HandleAdminRoyalInput(userId, text, request, ct);
                    return true;
                case "set_leaderboard_channel":
                    await HandleAdminSetLeaderboardChannelInput(userId, message, text, ct);
                    return true;
                case "announce_text":
                case "announce_scope":
                    await HandleAdminAnnounceTextInput(userId, message, text, request, ct);
                    return true;
                case "set_attack_lock":
                case "set_shield_hours":
                case "set_max_attacks":
                case "set_max_transfers":
                    await HandleAdminSettingsInput(userId, text, request, ct);
                    return true;
                case "awaiting_db_file":
                    await HandleAdminDbUpload(userId, message, text, ct);
                    return true;
            }
        }

        // Handle forwarded channel message for leaderboard channel setting
        if (message.ForwardFromChat != null && adminInputRequests.TryGetValue(userId, out var fwdReq) && fwdReq.Kind == "set_leaderboard_channel")
        {
            await HandleAdminSetLeaderboardChannelInput(userId, message, text, ct);
            return true;
        }

        switch (text)
        {
            case "پنل":
            case "/panel":
            case "/admin":
            case "🧭 پنل مدیریت":
                await SendAdminHome(userId, ct);
                return true;

            case "📊 داشبورد":
                await SendAdminDashboard(userId, ct);
                return true;

            case "👮 مدیریت ادمین‌ها":
                if (!IsPanelOwner(userId))
                {
                    await SendAdminDenied(userId, ct);
                    return true;
                }
                await SendAdminList(userId, 0, ct);
                return true;

            case "📜 لاگ مدیریتی":
                await SendAdminAudit(userId, ct);
                return true;

            case "🔎 جستجوی پلیر":
                await SendAdminPlayersHome(userId, ct);
                return true;

            case "🌍 مدیریت کشور":
                await SendAdminCountriesHome(userId, ct);
                return true;

            case "👥 مدیریت گروه":
                await SendAdminGroupsHome(userId, ct);
                return true;

            case "🤝 مدیریت اتحاد":
                await SendAdminAlliancesHome(userId, ct);
                return true;

            case "💎 اقتصاد و رویال":
                await SendAdminEconomyHome(userId, ct);
                return true;

            case "📢 اعلامیه":
                await SendAdminAnnounceHome(userId, ct);
                return true;

            case "⚔️ جنگ و عملیات":
                await SendAdminWarHome(userId, ct);
                return true;

            case "🗄 نگهداری":
                await SendAdminMaintenanceHome(userId, ct);
                return true;

            case "⚙️ تنظیمات":
                await SendAdminSettingsHome(userId, ct);
                return true;

            case "❌ بستن پنل":
                adminInputRequests.TryRemove(userId, out _);
                await SendPermanent(userId, "✅ پنل مدیریت بسته شد.", markup: new ReplyKeyboardRemove(), ct: ct);
                return true;
        }

        return false;
    }

    static async Task HandleAdminSearchPlayerInput(long adminId, string text, CancellationToken ct)
    {
        if (text is "لغو" or "cancel" or "انصراف")
        {
            adminInputRequests.TryRemove(adminId, out _);
            await SendAdminPlayersHome(adminId, ct);
            return;
        }
        if (!TryParseLong(text, out long targetId) || targetId <= 0)
        {
            await SendPermanent(adminId, "❌ آیدی نامعتبر است. لطفاً آیدی عددی پلیر را وارد کنید.\nبرای لغو بنویسید: لغو", ct: ct);
            return;
        }
        adminInputRequests.TryRemove(adminId, out _);
        await SendAdminPlayerDetail(adminId, targetId, ct);
    }

    static async Task HandleAdminSearchCountryInput(long adminId, string text, CancellationToken ct)
    {
        if (text is "لغو" or "cancel" or "انصراف")
        {
            adminInputRequests.TryRemove(adminId, out _);
            await SendAdminCountriesHome(adminId, ct);
            return;
        }
        if (string.IsNullOrWhiteSpace(text) || text.Length < 2)
        {
            await SendPermanent(adminId, "❌ نام کشور باید حداقل ۲ حرف باشد.\nبرای لغو: لغو", ct: ct);
            return;
        }
        var results = Database.SearchCountriesByName(text.Trim(), 15);
        adminInputRequests.TryRemove(adminId, out _);
        if (results.Count == 0)
        {
            await SendPermanent(adminId, $"❌ کشوری با نام «{text}» یافت نشد.", ct: ct);
            return;
        }
        var screen = BuildAdminCountrySearchResults(results, text);
        await SendPermanent(adminId, screen.Text, markup: screen.Keyboard, ct: ct);
    }

    static async Task HandleAdminSearchGroupInput(long adminId, string text, CancellationToken ct)
    {
        if (text is "لغو" or "cancel" or "انصراف")
        {
            adminInputRequests.TryRemove(adminId, out _);
            await SendAdminGroupsHome(adminId, ct);
            return;
        }
        if (!TryParseLong(text, out long chatId))
        {
            await SendPermanent(adminId, "❌ آیدی گروه نامعتبر. آیدی عددی (مثلاً -100123...) وارد کنید.\nلغو: لغو", ct: ct);
            return;
        }
        adminInputRequests.TryRemove(adminId, out _);
        await SendAdminGroupDetail(adminId, chatId, ct);
    }

    static async Task HandleAdminEditCountryInput(long adminId, string text, AdminInputRequest req, CancellationToken ct)
    {
        if (text is "لغو" or "cancel" or "انصراف")
        {
            adminInputRequests.TryRemove(adminId, out _);
            await SendAdminCountryDetail(adminId, req.TargetId, req.ChatId, ct);
            return;
        }
        if (!TryParseLong(text, out long newVal))
        {
            await SendPermanent(adminId, "❌ عدد نامعتبر. لطفاً عدد وارد کنید.\nلغو: لغو", ct: ct);
            return;
        }
        if (newVal < 0) newVal = 0;
        var country = Database.GetCountry(req.TargetId, req.ChatId);
        if (country == null)
        {
            adminInputRequests.TryRemove(adminId, out _);
            await SendPermanent(adminId, "❌ کشور یافت نشد.", ct: ct);
            return;
        }

        switch (req.Kind)
        {
            case "edit_country_money": country.Money = newVal; break;
            case "edit_country_iron": country.Iron = newVal; break;
            case "edit_country_pop": country.Population = Math.Max(1000, newVal); break;
            case "edit_country_soldiers": country.Soldiers = newVal; break;
            case "edit_country_tanks": country.Tanks = newVal; break;
            case "edit_country_planes": country.Planes = newVal; break;
            case "edit_country_bombers": country.Bombers = newVal; break;
            case "edit_country_antiair": country.AntiAir = newVal; break;
        }
        Database.UpdateCountryFull(country);
        Database.ReconcileDefense(country.OwnerId, country.ChatId);
        Database.WriteAdminAudit(adminId, "COUNTRY_EDIT", "Country", $"{req.TargetId}:{req.ChatId}", $"{req.Kind}={newVal}", true);
        adminInputRequests.TryRemove(adminId, out _);
        await SendPermanent(adminId, $"✅ مقدار جدید ذخیره شد: {newVal:N0}", ct: ct);
        await SendAdminCountryDetail(adminId, req.TargetId, req.ChatId, ct);
    }

    static async Task HandleAdminBanReasonInput(long adminId, string text, AdminInputRequest req, CancellationToken ct)
    {
        if (text is "لغو" or "cancel" or "انصراف")
        {
            adminInputRequests.TryRemove(adminId, out _);
            await SendAdminPlayerDetail(adminId, req.TargetId, ct);
            return;
        }
        string reason = text.Trim();
        if (reason.Length > 200) reason = reason[..200];
        Database.BanUser(req.TargetId, reason, adminId);
        Database.WriteAdminAudit(adminId, "PLAYER_BAN", "Player", req.TargetId.ToString(), reason, true);
        adminInputRequests.TryRemove(adminId, out _);
        await SendPermanent(adminId, $"✅ پلیر {req.TargetId} بن شد.\nدلیل: {reason}\nتمام کشورها حذف شد.", ct: ct);
    }

    static async Task HandleAdminRoyalInput(long adminId, string text, AdminInputRequest req, CancellationToken ct)
    {
        if (text is "لغو" or "cancel" or "انصراف")
        {
            adminInputRequests.TryRemove(adminId, out _);
            await SendAdminEconomyHome(adminId, ct);
            return;
        }

        long targetId = req.TargetId;
        long amount = 0;

        // Support "id amount" in one line when target not set
        if (targetId == 0)
        {
            var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && TryParseLong(parts[0], out long tid) && TryParseLong(parts[1], out long amt) && tid > 0 && amt > 0)
            {
                targetId = tid;
                amount = amt;
            }
            else if (!TryParseLong(text, out amount) || amount <= 0)
            {
                await SendPermanent(adminId, "❌ فرمت نامعتبر.\nبرای واریز بدون انتخاب قبلی: «آیدی مقدار» مثل 123456 100\nیا فقط مقدار اگر پلیر قبلاً انتخاب شده.\nلغو: لغو", ct: ct);
                return;
            }
            else
            {
                await SendPermanent(adminId, "❌ برای این حالت ابتدا آیدی و مقدار را با هم وارد کنید مثل: 123456 100\nلغو: لغو", ct: ct);
                return;
            }
        }
        else
        {
            if (!TryParseLong(text, out amount) || amount <= 0)
            {
                await SendPermanent(adminId, "❌ مقدار نامعتبر. عدد مثبت وارد کنید.\nلغو: لغو", ct: ct);
                return;
            }
        }
        if (req.Kind == "royal_add")
        {
            Database.AddRoyalCoins(targetId, amount);
            Database.WriteAdminAudit(adminId, "ROYAL_ADD", "Player", targetId.ToString(), $"+{amount}", true);
            await SendPermanent(adminId, $"✅ {amount:N0} رویال به {targetId} اضافه شد.\nموجودی جدید: {Database.GetRoyalCoins(targetId):N0}", ct: ct);
            try { await SendPermanent(targetId, $"💎 {amount:N0} رویال کوین به حساب شما واریز شد!", ct: ct); } catch { }
        }
        else
        {
            Database.AddRoyalCoins(targetId, -amount);
            Database.WriteAdminAudit(adminId, "ROYAL_DEDUCT", "Player", targetId.ToString(), $"-{amount}", true);
            await SendPermanent(adminId, $"✅ {amount:N0} رویال از {targetId} کسر شد.\nموجودی جدید: {Database.GetRoyalCoins(targetId):N0}", ct: ct);
            try { await SendPermanent(targetId, $"💎 {amount:N0} رویال کوین از حساب شما کسر شد.", ct: ct); } catch { }
        }
        adminInputRequests.TryRemove(adminId, out _);
    }

    static async Task HandleAdminSetLeaderboardChannelInput(long adminId, Message message, string text, CancellationToken ct)
    {
        if (text is "لغو" or "cancel" or "انصراف" or "0")
        {
            if (text == "0")
            {
                Database.SetSetting("LeaderboardChannelId", "0");
                adminInputRequests.TryRemove(adminId, out _);
                await SendPermanent(adminId, "✅ کانال لیدربورد حذف شد. فقط برای ادمین ارسال می‌شود.", ct: ct);
                await SendAdminSettingsHome(adminId, ct);
                return;
            }
            adminInputRequests.TryRemove(adminId, out _);
            await SendAdminSettingsHome(adminId, ct);
            return;
        }

        long channelId = 0;
        // Try forwarded chat
        if (message.ForwardFromChat != null)
        {
            channelId = message.ForwardFromChat.Id;
        }
        else if (TryParseLong(text, out long parsed))
        {
            channelId = parsed;
        }
        else if (text.StartsWith("@"))
        {
            // Try resolve username to chat id via GetChatAsync
            try
            {
                var ch = await bot.GetChatAsync(text, ct);
                channelId = ch.Id;
            }
            catch { }
        }

        if (channelId == 0)
        {
            await SendPermanent(adminId, "❌ فرمت نامعتبر.\nآیدی عددی کانال (مثلاً -100123...) یا @username یا فورواردی از کانال ارسال کنید.\nبرای حذف کانال 0 بنویسید.\nلغو: لغو", ct: ct);
            return;
        }

        // Test bot is admin in channel
        try
        {
            var me = await bot.GetChatMemberAsync(channelId, bot.BotId ?? OWNER_ID, ct);
            if (me.Status is not (ChatMemberStatus.Administrator or ChatMemberStatus.Creator))
            {
                await SendPermanent(adminId, "⚠️ ربات در کانال ادمین نیست! لطفاً ربات را ادمین کانال کنید و دوباره تلاش کنید.", ct: ct);
                return;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LB CHANNEL CHECK ERR] {ex.Message}");
            // still allow setting, but warn
        }

        Database.SetSetting("LeaderboardChannelId", channelId.ToString());
        Database.WriteAdminAudit(adminId, "SET_LB_CHANNEL", "Settings", channelId.ToString(), "", true);
        adminInputRequests.TryRemove(adminId, out _);
        await SendPermanent(adminId, $"✅ کانال لیدربورد تنظیم شد: {channelId}\n\nهر شب ساعت 22:00 (تهران) لیدربوردها به این کانال و به پیوی شما ارسال می‌شود.", ct: ct);

        // Send test leaderboards
        try { await SendNightlyLeaderboards(ct); } catch { }

        await SendAdminSettingsHome(adminId, ct);
    }

    static async Task HandleAdminAnnounceTextInput(long adminId, Message message, string text, AdminInputRequest req, CancellationToken ct)
    {
        if (text is "لغو" or "cancel" or "انصراف")
        {
            adminInputRequests.TryRemove(adminId, out _);
            await SendAdminAnnounceHome(adminId, ct);
            return;
        }

        if (string.IsNullOrWhiteSpace(text) && message.Photo == null && message.Document == null)
        {
            await SendPermanent(adminId, "❌ لطفاً متن اعلامیه را وارد کنید.\nلغو: لغو", ct: ct);
            return;
        }

        // Save announce and ask scope
        string payload = text;
        if (message.Photo != null && message.Photo.Length > 0)
            payload = message.Photo.Last().FileId + "|PHOTO|" + text;
        else if (message.Document != null)
            payload = message.Document.FileId + "|DOC|" + text;

        req.Extra = payload;
        req.Kind = "announce_scope"; // next step
        adminInputRequests[adminId] = req;

        var kb = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("👥 همه گروه‌ها", "adm:ann:groups"), InlineKeyboardButton.WithCallbackData("👤 همه پیوی‌ها", "adm:ann:private") },
            new[] { InlineKeyboardButton.WithCallbackData("🌐 همه (گروه+پیوی)", "adm:ann:all"), InlineKeyboardButton.WithCallbackData("❌ لغو", "adm:ann:cancel") }
        });

        await SendPermanent(adminId, "📢 متن دریافت شد. مقصد را انتخاب کنید:", markup: kb, ct: ct);
    }

    static async Task HandleAdminSettingsInput(long adminId, string text, AdminInputRequest req, CancellationToken ct)
    {
        if (text is "لغو" or "cancel" or "انصراف")
        {
            adminInputRequests.TryRemove(adminId, out _);
            await SendAdminSettingsHome(adminId, ct);
            return;
        }
        if (!TryParseInt(text, out int val) || val < 0)
        {
            await SendPermanent(adminId, "❌ عدد نامعتبر.\nلغو: لغو", ct: ct);
            return;
        }

        switch (req.Kind)
        {
            case "set_attack_lock":
                ATTACK_LOCK_MINUTES = Math.Clamp(val, 0, 1440);
                Database.SetSetting("AttackLockMinutes", ATTACK_LOCK_MINUTES.ToString());
                break;
            case "set_shield_hours":
                SHIELD_HOURS = Math.Clamp(val, 0, 720);
                Database.SetSetting("ShieldHours", SHIELD_HOURS.ToString());
                break;
            case "set_max_attacks":
                MAX_ATTACKS_PER_UPDATE = Math.Clamp(val, 1, 100);
                Database.SetSetting("MaxAttacks", MAX_ATTACKS_PER_UPDATE.ToString());
                break;
            case "set_max_transfers":
                MAX_TRANSFERS_PER_UPDATE = Math.Clamp(val, 1, 100);
                Database.SetSetting("MaxTransfers", MAX_TRANSFERS_PER_UPDATE.ToString());
                break;
        }
        Database.WriteAdminAudit(adminId, "SETTINGS_EDIT", "Settings", req.Kind, val.ToString(), true);
        adminInputRequests.TryRemove(adminId, out _);
        await SendPermanent(adminId, $"✅ تنظیم شد: {val}", ct: ct);
        await SendAdminSettingsHome(adminId, ct);
    }

    static async Task HandleAdminDbUpload(long adminId, Message message, string text, CancellationToken ct)
    {
        if (text is "لغو" or "cancel" or "انصراف")
        {
            adminInputRequests.TryRemove(adminId, out _);
            await SendAdminMaintenanceHome(adminId, ct);
            return;
        }
        if (message.Document == null)
        {
            await SendPermanent(adminId, "❌ لطفاً فایل دیتابیس را ارسال کنید.\nلغو: لغو", ct: ct);
            return;
        }
        string uploadPath = $"gamedata.{adminId}.upload";
        try
        {
            var file = await bot.GetFileAsync(message.Document.FileId, cancellationToken: ct);
            using (var stream = new System.IO.FileStream(uploadPath, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
                await bot.DownloadFileAsync(file.FilePath!, stream, cancellationToken: ct);

            var restore = await RestoreDatabaseSafely(uploadPath, ct);
            Database.WriteAdminAudit(adminId, "RESTORE_DB", "Maintenance", "", restore.Error, restore.Success);
            if (!restore.Success)
            {
                await SendPermanent(adminId, $"❌ بازیابی انجام نشد: {restore.Error}", ct: ct);
                return;
            }

            adminInputRequests.TryRemove(adminId, out _);
            await SendPermanent(adminId, "✅ دیتابیس جدید با بررسی سلامت و بکاپ بازگشت جایگزین شد.", ct: ct);
        }
        catch (Exception ex)
        {
            await SendPermanent(adminId, $"❌ خطا در آپلود: {ex.Message}", ct: ct);
        }
        finally
        {
            TryDeleteSqliteSidecar(uploadPath);
        }
    }

    static async Task HandleAdminAddInput(
        long ownerId,
        string text,
        CancellationToken ct)
    {
        if (!IsPanelOwner(ownerId))
        {
            adminInputRequests.TryRemove(
                ownerId,
                out _
            );

            await SendAdminDenied(ownerId, ct);
            return;
        }

        if (text is "لغو" or "cancel" or "انصراف")
        {
            adminInputRequests.TryRemove(
                ownerId,
                out _
            );

            await SendAdminHome(ownerId, ct);
            return;
        }

        string[] values = text.Split(
            ' ',
            2,
            StringSplitOptions.RemoveEmptyEntries
        );

        if (values.Length == 0 ||
            !TryParseLong(
                NormalizeDigits(values[0]),
                out long targetId
            ) ||
            targetId <= 0)
        {
            await SendPermanent(
                ownerId,
                "❌ فرمت نامعتبر است.\n\n" +
                "فرمت صحیح:\n" +
                "123456789 نام مدیر\n\n" +
                "برای لغو بنویسید: لغو",
                ct: ct
            );
            return;
        }

        if (targetId == OWNER_ID)
        {
            await SendPermanent(
                ownerId,
                "ℹ️ این آیدی متعلق به مالک اصلی است.",
                ct: ct
            );
            return;
        }

        string displayName =
            values.Length >= 2
                ? values[1].Trim()
                : $"مدیر {targetId}";

        if (displayName.Length > 80)
            displayName = displayName[..80];

        Database.AddOrReactivateAdmin(
            targetId,
            displayName,
            ownerId
        );

        Database.WriteAdminAudit(
            ownerId,
            "ADMIN_ADD",
            "Admin",
            targetId.ToString(),
            $"Name={displayName}"
        );

        adminInputRequests.TryRemove(
            ownerId,
            out _
        );

        await SendPermanent(
            ownerId,
            $"✅ مدیر اضافه شد.\n" +
            $"نام: {displayName}\n" +
            $"آیدی: {targetId}\n\n" +
            "این مدیر هنوز هیچ دسترسی عملیاتی ندارد.",
            ct: ct
        );

        try
        {
            await SendPermanent(
                targetId,
                "👮 شما به‌عنوان مدیر آلیس ثبت شدید.\n" +
                "دسترسی‌های شما توسط مالک تعیین می‌شود.",
                markup: BuildAdminReplyKeyboard(targetId),
                ct: ct
            );
        }
        catch
        {
        }

        await SendAdminDetails(
            ownerId,
            targetId,
            ct
        );
    }

    static async Task SendAdminHome(
        long userId,
        CancellationToken ct)
    {
        var screen = BuildAdminHomeScreen(userId);

        await SendPermanent(
            userId,
            "⌨️ میانبرهای مدیریت آلیس فعال شدند.",
            markup: BuildAdminReplyKeyboard(userId),
            ct: ct
        );

        await SendPermanent(
            userId,
            screen.Text,
            markup: screen.Keyboard,
            ct: ct
        );
    }

    static (
        string Text,
        InlineKeyboardMarkup Keyboard
    ) BuildAdminHomeScreen(long userId)
    {
        var account = Database.GetAdmin(userId);

        string displayName =
            account?.DisplayName ??
            (userId == OWNER_ID
                ? "مالک اصلی"
                : $"مدیر {userId}");

        var buttons =
            new List<InlineKeyboardButton>();

        if (CanAdmin(userId, "DASH"))
        {
            buttons.Add(
                InlineKeyboardButton.WithCallbackData(
                    "📊 داشبورد",
                    "adm:dash"
                )
            );
        }

        if (IsPanelOwner(userId))
        {
            buttons.Add(
                InlineKeyboardButton.WithCallbackData(
                    "👮 مدیران",
                    "adm:admins:0"
                )
            );
        }

        AddAdminModuleButton(
            buttons,
            userId,
            "🔎 پلیرها",
            "players",
            "P_VIEW"
        );

        AddAdminModuleButton(
            buttons,
            userId,
            "🌍 کشورها",
            "countries",
            "C_VIEW"
        );

        AddAdminModuleButton(
            buttons,
            userId,
            "👥 گروه‌ها",
            "groups",
            "G_VIEW"
        );

        AddAdminModuleButton(
            buttons,
            userId,
            "🤝 اتحادها",
            "alliances",
            "ALLY"
        );

        AddAdminModuleButton(
            buttons,
            userId,
            "💎 اقتصاد",
            "economy",
            "ROYAL"
        );

        AddAdminModuleButton(
            buttons,
            userId,
            "⚔️ جنگ",
            "war",
            "W_VIEW"
        );

        AddAdminModuleButton(
            buttons,
            userId,
            "🚚 عملیات",
            "operations",
            "O_VIEW"
        );

        AddAdminModuleButton(
            buttons,
            userId,
            "📢 اعلامیه",
            "announce",
            "ANN"
        );

        AddAdminModuleButton(
            buttons,
            userId,
            "⚙️ تنظیمات",
            "settings",
            "SET"
        );

        if (CanAdminAny(
                userId,
                "BACKUP",
                "RESTORE"))
        {
            buttons.Add(
                InlineKeyboardButton.WithCallbackData(
                    "🗄 نگهداری",
                    "adm:maintenance:home"
                )
            );
        }

        if (CanAdmin(userId, "AUDIT"))
        {
            buttons.Add(
                InlineKeyboardButton.WithCallbackData(
                    "📜 Audit Log",
                    "adm:audit"
                )
            );
        }

        var rows = PairAdminButtons(buttons);

        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData(
                "🔄 تازه‌سازی",
                "adm:home"
            ),
            InlineKeyboardButton.WithCallbackData(
                "❌ بستن",
                "adm:close"
            )
        });

        string text =
            "🧭 پنل مدیریت آلیس\n\n" +
            $"مدیر: {displayName}\n" +
            $"آیدی: {userId}\n" +
            $"سطح: {(userId == OWNER_ID ? "مالک اصلی" : "مدیر")}\n\n" +
            "یک بخش را انتخاب کنید.";

        return (
            text,
            new InlineKeyboardMarkup(rows)
        );
    }

    static void AddAdminModuleButton(
        List<InlineKeyboardButton> buttons,
        long userId,
        string title,
        string module,
        string permission)
    {
        if (!CanAdmin(userId, permission))
            return;

        // Creative mapping: direct home callback for each module
        string callback = module switch
        {
            "players" => "adm:players:home",
            "countries" => "adm:countries:home",
            "groups" => "adm:groups:home",
            "alliances" => "adm:alliances:home",
            "economy" => "adm:economy:home",
            "war" => "adm:war:home",
            "operations" => "adm:ops:home",
            "announce" => "adm:ann:home",
            "settings" => "adm:settings:home",
            "maintenance" => "adm:backup:get",
            _ => $"adm:todo:{module}"
        };

        buttons.Add(
            InlineKeyboardButton.WithCallbackData(
                title,
                callback
            )
        );
    }

    static List<InlineKeyboardButton[]> PairAdminButtons(
        List<InlineKeyboardButton> buttons)
    {
        var rows =
            new List<InlineKeyboardButton[]>();

        for (int i = 0; i < buttons.Count; i += 2)
        {
            if (i + 1 < buttons.Count)
            {
                rows.Add(new[]
                {
                    buttons[i],
                    buttons[i + 1]
                });
            }
            else
            {
                rows.Add(new[]
                {
                    buttons[i]
                });
            }
        }

        return rows;
    }

    static async Task SendAdminDashboard(
        long userId,
        CancellationToken ct)
    {
        if (!CanAdmin(userId, "DASH"))
        {
            await SendAdminDenied(userId, ct);
            return;
        }

        var screen = BuildAdminDashboardScreen();

        await SendPermanent(
            userId,
            screen.Text,
            markup: screen.Keyboard,
            ct: ct
        );
    }

    static (
        string Text,
        InlineKeyboardMarkup Keyboard
    ) BuildAdminDashboardScreen()
    {
        var stats =
            Database.GetAdminDashboardStats();

        var activity =
            Database.GetActivityStats();

        long databaseSize = 0;

        try
        {
            if (System.IO.File.Exists("gamedata.db"))
                databaseSize =
                    new System.IO.FileInfo("gamedata.db").Length;
        }
        catch
        {
        }

        string updateSchedule =
            UpdateMode == "daily"
                ? $"روزانه در {UpdateValue / 60:D2}:{UpdateValue % 60:D2}"
                : $"هر {UpdateValue} دقیقه";

        string lastUpdate =
            lastAssetUpdateAt == DateTime.MinValue
                ? "هنوز اجرا نشده"
                : lastAssetUpdateAt
                    .AddHours(3.5)
                    .ToString("yyyy-MM-dd HH:mm");

        string text =
            "📊 داشبورد مدیریتی آلیس\n\n" +

            "🌍 وضعیت بازی\n" +
            $"کشورها: {stats.Countries:N0}\n" +
            $"پلیرهای ثبت‌شده: {stats.Players:N0}\n" +
            $"گروه‌های دارای کشور: {stats.Groups:N0}\n" +
            $"اتحادها: {stats.Alliances:N0}\n\n" +

            "🟢 فعالیت\n" +
            $"۲۴ ساعت: {activity.Players24h:N0} پلیر | " +
            $"{activity.Groups24h:N0} گپ\n" +
            $"۷ روز: {activity.Players7d:N0} پلیر | " +
            $"{activity.Groups7d:N0} گپ\n" +
            $"۳۰ روز: {activity.Players30d:N0} پلیر | " +
            $"{activity.Groups30d:N0} گپ\n\n" +

            "🚚 عملیات فعال\n" +
            $"ترنسفر: {stats.ActiveTransfers:N0}\n" +
            $"صف‌آرایی: {stats.ActiveDeployments:N0}\n\n" +

            "⚙️ سیستم\n" +
            $"ادمین فعال: {stats.ActiveAdmins:N0}\n" +
            $"Audit: {stats.AuditEntries:N0}\n" +
            $"حجم دیتابیس: {databaseSize / 1024.0 / 1024.0:F2} MB\n" +
            $"آپدیت دارایی: {updateSchedule}\n" +
            $"آخرین آپدیت: {lastUpdate}";

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    "🔄 تازه‌سازی",
                    "adm:dash"
                )
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    "🏠 خانه",
                    "adm:home"
                )
            }
        });

        return (text, keyboard);
    }

    static async Task SendAdminList(
        long ownerId,
        int page,
        CancellationToken ct)
    {
        if (!IsPanelOwner(ownerId))
        {
            await SendAdminDenied(ownerId, ct);
            return;
        }

        var screen = BuildAdminListScreen(page);

        await SendPermanent(
            ownerId,
            screen.Text,
            markup: screen.Keyboard,
            ct: ct
        );
    }

    static (
        string Text,
        InlineKeyboardMarkup Keyboard
    ) BuildAdminListScreen(int requestedPage)
    {
        var admins = Database.GetAdmins();

        const int pageSize = 6;

        int totalPages = Math.Max(
            1,
            (int)Math.Ceiling(
                admins.Count / (double)pageSize
            )
        );

        int page = Math.Clamp(
            requestedPage,
            0,
            totalPages - 1
        );

        var items = admins
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToList();

        var rows =
            new List<InlineKeyboardButton[]>();

        foreach (var admin in items)
        {
            string icon = admin.IsOwner
                ? "👑"
                : admin.IsActive
                    ? "🟢"
                    : "🔴";

            rows.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    $"{icon} {admin.DisplayName}",
                    $"adm:view:{admin.AdminId}"
                )
            });
        }

        var navigation =
            new List<InlineKeyboardButton>();

        if (page > 0)
        {
            navigation.Add(
                InlineKeyboardButton.WithCallbackData(
                    "⬅️ قبلی",
                    $"adm:admins:{page - 1}"
                )
            );
        }

        if (page + 1 < totalPages)
        {
            navigation.Add(
                InlineKeyboardButton.WithCallbackData(
                    "بعدی ➡️",
                    $"adm:admins:{page + 1}"
                )
            );
        }

        if (navigation.Count > 0)
            rows.Add(navigation.ToArray());

        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData(
                "➕ افزودن مدیر",
                "adm:add"
            )
        });

        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData(
                "🏠 خانه",
                "adm:home"
            )
        });

        string text =
            "👮 مدیریت ادمین‌های آلیس\n\n" +
            $"تعداد: {admins.Count:N0}\n" +
            $"صفحه: {page + 1}/{totalPages}\n\n" +
            "👑 مالک | 🟢 فعال | 🔴 غیرفعال";

        return (
            text,
            new InlineKeyboardMarkup(rows)
        );
    }

    static async Task SendAdminDetails(
        long ownerId,
        long targetId,
        CancellationToken ct)
    {
        if (!IsPanelOwner(ownerId))
        {
            await SendAdminDenied(ownerId, ct);
            return;
        }

        var screen =
            BuildAdminDetailsScreen(targetId);

        await SendPermanent(
            ownerId,
            screen.Text,
            markup: screen.Keyboard,
            ct: ct
        );
    }

    static (
        string Text,
        InlineKeyboardMarkup Keyboard
    ) BuildAdminDetailsScreen(long targetId)
    {
        var admin = Database.GetAdmin(targetId);

        if (admin == null)
        {
            return (
                "❌ مدیر یافت نشد.",
                new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData(
                            "⬅️ بازگشت",
                            "adm:admins:0"
                        )
                    }
                })
            );
        }

        var permissions =
            Database.GetAdminPermissions(targetId);

        string created =
            FormatAdminTime(admin.CreatedAtMs);

        string lastSeen =
            admin.LastSeenMs > 0
                ? FormatAdminTime(admin.LastSeenMs)
                : "بدون فعالیت";

        var rows =
            new List<InlineKeyboardButton[]>();

        if (!admin.IsOwner)
        {
            rows.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    $"🔐 دسترسی‌ها ({permissions.Count})",
                    $"adm:perms:{targetId}:0"
                )
            });

            rows.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    admin.IsActive
                        ? "⏸ غیرفعال‌کردن"
                        : "▶️ فعال‌کردن",
                    $"adm:toggle:{targetId}"
                )
            });

            rows.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    "🗑 حذف مدیر",
                    $"adm:removeask:{targetId}"
                )
            });
        }

        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData(
                "⬅️ فهرست مدیران",
                "adm:admins:0"
            ),
            InlineKeyboardButton.WithCallbackData(
                "🏠 خانه",
                "adm:home"
            )
        });

        string text =
            "👮 مشخصات مدیر\n\n" +
            $"نام: {admin.DisplayName}\n" +
            $"آیدی: {admin.AdminId}\n" +
            $"نوع: {(admin.IsOwner ? "مالک اصلی" : "مدیر")}\n" +
            $"وضعیت: {(admin.IsActive ? "فعال 🟢" : "غیرفعال 🔴")}\n" +
            $"تعداد دسترسی: {permissions.Count}\n" +
            $"افزوده‌شده توسط: {admin.AddedBy}\n" +
            $"تاریخ افزودن: {created}\n" +
            $"آخرین فعالیت: {lastSeen}";

        return (
            text,
            new InlineKeyboardMarkup(rows)
        );
    }

    static (
        string Text,
        InlineKeyboardMarkup Keyboard
    ) BuildAdminPermissionScreen(
        long targetId,
        int requestedPage)
    {
        var admin = Database.GetAdmin(targetId);

        if (admin == null || admin.IsOwner)
        {
            return (
                "❌ مدیر قابل ویرایش نیست.",
                new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData(
                            "⬅️ بازگشت",
                            "adm:admins:0"
                        )
                    }
                })
            );
        }

        var granted =
            Database.GetAdminPermissions(targetId);

        const int pageSize = 7;

        int totalPages = Math.Max(
            1,
            (int)Math.Ceiling(
                AdminPermissionCatalog.All.Length /
                (double)pageSize
            )
        );

        int page = Math.Clamp(
            requestedPage,
            0,
            totalPages - 1
        );

        var permissions =
            AdminPermissionCatalog.All
                .Skip(page * pageSize)
                .Take(pageSize)
                .ToList();

        var rows =
            new List<InlineKeyboardButton[]>();

        foreach (var permission in permissions)
        {
            bool enabled =
                granted.Contains(permission.Code);

            rows.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    $"{(enabled ? "✅" : "▫️")} " +
                    $"{permission.Title}",
                    $"adm:perm:{targetId}:" +
                    $"{permission.Code}:{page}"
                )
            });
        }

        var navigation =
            new List<InlineKeyboardButton>();

        if (page > 0)
        {
            navigation.Add(
                InlineKeyboardButton.WithCallbackData(
                    "⬅️ قبلی",
                    $"adm:perms:{targetId}:{page - 1}"
                )
            );
        }

        if (page + 1 < totalPages)
        {
            navigation.Add(
                InlineKeyboardButton.WithCallbackData(
                    "بعدی ➡️",
                    $"adm:perms:{targetId}:{page + 1}"
                )
            );
        }

        if (navigation.Count > 0)
            rows.Add(navigation.ToArray());

        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData(
                "✅ اعطای همه",
                $"adm:allask:{targetId}:1"
            ),
            InlineKeyboardButton.WithCallbackData(
                "🚫 حذف همه",
                $"adm:allask:{targetId}:0"
            )
        });

        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData(
                "⬅️ مشخصات مدیر",
                $"adm:view:{targetId}"
            )
        });

        string text =
            "🔐 مدیریت دسترسی‌ها\n\n" +
            $"مدیر: {admin.DisplayName}\n" +
            $"آیدی: {targetId}\n" +
            $"دسترسی فعال: {granted.Count}\n" +
            $"صفحه: {page + 1}/{totalPages}\n\n" +
            "برای تغییر هر دسترسی روی آن بزنید.";

        return (
            text,
            new InlineKeyboardMarkup(rows)
        );
    }

    static async Task SendAdminAudit(
        long userId,
        CancellationToken ct)
    {
        if (!CanAdmin(userId, "AUDIT"))
        {
            await SendAdminDenied(userId, ct);
            return;
        }

        var screen = BuildAdminAuditScreen();

        await SendPermanent(
            userId,
            screen.Text,
            markup: screen.Keyboard,
            ct: ct
        );
    }

    static (
        string Text,
        InlineKeyboardMarkup Keyboard
    ) BuildAdminAuditScreen()
    {
        var entries =
            Database.GetRecentAdminAudit(15);

        var text = new StringBuilder();

        text.AppendLine("📜 آخرین عملیات مدیریتی");
        text.AppendLine();

        if (entries.Count == 0)
        {
            text.AppendLine("هنوز عملیاتی ثبت نشده است.");
        }
        else
        {
            foreach (var entry in entries)
            {
                text.Append(
                    entry.Success ? "✅ " : "❌ "
                );

                text.Append(entry.Action);
                text.Append(" | ");
                text.Append(entry.AdminId);

                if (!string.IsNullOrWhiteSpace(
                        entry.TargetId))
                {
                    text.Append(" → ");
                    text.Append(entry.TargetId);
                }

                text.AppendLine();
                text.AppendLine(
                    FormatAdminTime(entry.CreatedAtMs)
                );

                if (!string.IsNullOrWhiteSpace(
                        entry.Details))
                {
                    string details = entry.Details;

                    if (details.Length > 100)
                        details = details[..100] + "…";

                    text.AppendLine(details);
                }

                text.AppendLine("────────");
            }
        }

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    "🔄 تازه‌سازی",
                    "adm:audit"
                )
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    "🏠 خانه",
                    "adm:home"
                )
            }
        });

        return (text.ToString(), keyboard);
    }

    // ================= CREATIVE ADMIN MODULES –  =================
    static async Task SendAdminPlayersHome(long adminId, CancellationToken ct)
    {
        if (!CanAdmin(adminId, "P_VIEW"))
        {
            await SendAdminDenied(adminId, ct);
            return;
        }
        var screen = await BuildAdminPlayersHome(ct);
        await SendPermanent(adminId, screen.Text, markup: screen.Keyboard, ct: ct);
    }

    static async Task<(string Text, InlineKeyboardMarkup Keyboard)> BuildAdminPlayersHome(CancellationToken ct = default)
    {
        var all = Database.GetAllCountries();
        var top = all.Select(c => new { Country = c, MP = CalcManpower(c) })
                     .OrderByDescending(x => x.MP)
                     .Take(8)
                     .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("🔎 **مدیریت پلیرها** 🔎");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine($"👥 کل پلیرها: {all.Select(c => c.OwnerId).Distinct().Count():N0}");
        sb.AppendLine($"🌍 کل کشورها: {all.Count:N0}");
        sb.AppendLine();
        sb.AppendLine("🏆 تاپ پلیرها (مان‌پاور):");
        for (int i = 0; i < top.Count; i++)
        {
            sb.AppendLine($"{i + 1}. {top[i].Country.OwnerName} – {top[i].Country.Name} – {FormatManpowerK(top[i].MP)}");
        }
        sb.AppendLine();
        sb.AppendLine("برای دیدن جزئیات پلیر، آیدی عددی را وارد کنید یا از دکمه‌ها استفاده کنید.");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━");

        var kb = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("🔍 جستجو با آیدی", "adm:players:search"), InlineKeyboardButton.WithCallbackData("🚫 بن لیست", "adm:players:banned") },
            new[] { InlineKeyboardButton.WithCallbackData("🏆 تاپ 10 مان‌پاور", "adm:lb:topplayers"), InlineKeyboardButton.WithCallbackData("🔄 تازه‌سازی", "adm:players:home") },
            new[] { InlineKeyboardButton.WithCallbackData("🏠 خانه", "adm:home") }
        });

        return (sb.ToString(), kb);
    }

    static async Task SendAdminPlayerDetail(long adminId, long targetId, CancellationToken ct)
    {
        if (!CanAdmin(adminId, "P_VIEW"))
        {
            await SendAdminDenied(adminId, ct);
            return;
        }
        var screen = BuildAdminPlayerDetail(targetId);
        await SendPermanent(adminId, screen.Text, markup: screen.Keyboard, ct: ct);
    }

    static (string Text, InlineKeyboardMarkup Keyboard) BuildAdminPlayerDetail(long targetId)
    {
        var countries = Database.GetCountriesByOwnerId(targetId);
        long totalMP = countries.Sum(c => CalcManpower(c));
        long royal = Database.GetRoyalCoins(targetId);
        bool isBanned = Database.IsUserBanned(targetId);
        var sb = new StringBuilder();
        sb.AppendLine($"👤 **پروفایل پلیر {targetId}**");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine($"🔰 وضعیت: {(isBanned ? "🚫 بن شده" : "✅ فعال")}");
        sb.AppendLine($"💎 رویال: {royal:N0}");
        sb.AppendLine($"🌍 تعداد کشور: {countries.Count}");
        sb.AppendLine($"⚡ مجموع مان‌پاور: {FormatManpowerK(totalMP)}");
        sb.AppendLine();
        if (countries.Count > 0)
        {
            sb.AppendLine("🏳️ کشورها:");
            foreach (var c in countries.Take(10))
            {
                sb.AppendLine($"• {c.Name} در {c.ChatId} – {FormatManpowerK(CalcManpower(c))} – {c.Cities} شهر");
            }
        }
        else
        {
            sb.AppendLine("❌ کشوری ندارد.");
        }
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━");

        var rows = new List<InlineKeyboardButton[]>
        {
            new[] { InlineKeyboardButton.WithCallbackData("🌍 دیدن کشورها", $"adm:player:countries:{targetId}"), InlineKeyboardButton.WithCallbackData("💎 رویال", $"adm:player:royal:{targetId}") },
            new[] { InlineKeyboardButton.WithCallbackData(isBanned ? "✅ آنبن" : "🚫 بن", isBanned ? $"adm:player:unban:{targetId}" : $"adm:player:banask:{targetId}"), InlineKeyboardButton.WithCallbackData("🗑 حذف کشورها", $"adm:player:delcountries:{targetId}") },
            new[] { InlineKeyboardButton.WithCallbackData("🔙 بازگشت", "adm:players:home"), InlineKeyboardButton.WithCallbackData("🏠 خانه", "adm:home") }
        };
        return (sb.ToString(), new InlineKeyboardMarkup(rows));
    }

    static async Task SendAdminCountriesHome(long adminId, CancellationToken ct)
    {
        if (!CanAdmin(adminId, "C_VIEW"))
        {
            await SendAdminDenied(adminId, ct);
            return;
        }
        var screen = await BuildAdminCountriesHome(ct);
        await SendPermanent(adminId, screen.Text, markup: screen.Keyboard, ct: ct);
    }

    static async Task<(string Text, InlineKeyboardMarkup Keyboard)> BuildAdminCountriesHome(CancellationToken ct = default)
    {
        var all = Database.GetAllCountries();
        var top = all.Select(c => new { Country = c, MP = CalcManpower(c) }).OrderByDescending(x => x.MP).Take(8).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("🌍 **مدیریت کشورها** 🌍");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine($"📊 کل کشورها: {all.Count:N0}");
        sb.AppendLine();
        sb.AppendLine("🏆 تاپ کشورها:");
        for (int i = 0; i < top.Count; i++)
        {
            sb.AppendLine($"{i + 1}. {top[i].Country.Name} ({top[i].Country.OwnerName}) – {FormatManpowerK(top[i].MP)} – {top[i].Country.ChatId}");
        }
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
        var kb = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("🔍 جستجو نام", "adm:countries:search"), InlineKeyboardButton.WithCallbackData("⚔️ محاصره شده‌ها", "adm:countries:sieged") },
            new[] { InlineKeyboardButton.WithCallbackData("🔄 تازه‌سازی", "adm:countries:home"), InlineKeyboardButton.WithCallbackData("🏠 خانه", "adm:home") }
        });
        return (sb.ToString(), kb);
    }

    static (string Text, InlineKeyboardMarkup Keyboard) BuildAdminCountrySearchResults(List<Country> results, string query)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"🔍 نتایج جستجو برای «{query}» – {results.Count} مورد");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
        foreach (var c in results.Take(10))
        {
            sb.AppendLine($"• {c.Name} – {c.OwnerName} – {c.ChatId} – {FormatManpowerK(CalcManpower(c))}");
        }
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
        var rows = new List<InlineKeyboardButton[]>();
        foreach (var c in results.Take(8))
        {
            rows.Add(new[] { InlineKeyboardButton.WithCallbackData($"🏳️ {c.Name}", $"adm:country:view:{c.OwnerId}:{c.ChatId}") });
        }
        rows.Add(new[] { InlineKeyboardButton.WithCallbackData("🔙 بازگشت", "adm:countries:home"), InlineKeyboardButton.WithCallbackData("🏠 خانه", "adm:home") });
        return (sb.ToString(), new InlineKeyboardMarkup(rows));
    }

    static async Task SendAdminCountryDetail(long adminId, long ownerId, long chatId, CancellationToken ct)
    {
        if (!CanAdmin(adminId, "C_VIEW"))
        {
            await SendAdminDenied(adminId, ct);
            return;
        }
        var screen = BuildAdminCountryDetail(ownerId, chatId);
        await SendPermanent(adminId, screen.Text, markup: screen.Keyboard, ct: ct);
    }

    static (string Text, InlineKeyboardMarkup Keyboard) BuildAdminCountryDetail(long ownerId, long chatId)
    {
        var c = Database.GetCountry(ownerId, chatId);
        if (c == null)
        {
            return ("❌ کشور یافت نشد.", new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("🏠 خانه", "adm:home") } }));
        }
        var mp = CalcManpower(c);
        var sb = new StringBuilder();
        sb.AppendLine($"🏳️ **{c.Name}**");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine($"👤 مالک: {c.OwnerName} ({c.OwnerId})");
        sb.AppendLine($"🌍 گپ: {c.ChatId}");
        sb.AppendLine($"⚔️ فکشن: {c.Faction}");
        sb.AppendLine($"⚡ مان‌پاور: {FormatManpowerK(mp)} ({mp:N0})");
        sb.AppendLine($"💰 پول: {c.Money:N0} | 🔩 آهن: {c.Iron:N0}");
        sb.AppendLine($"👥 جمعیت: {c.Population:N0} | 🏙 شهرها: {c.Cities}");
        sb.AppendLine($"🪖 سرباز: {c.Soldiers:N0} | 🛡 تانک: {c.Tanks:N0} | ✈️ جنگنده: {c.Planes:N0} | 🛩 بمب‌افکن: {c.Bombers:N0} | 🎯 پدافند: {c.AntiAir:N0}");
        sb.AppendLine($"🏥 رفاه: {c.Welfare:F1}% | 💸 مالیات: {c.TaxRate}% | 🎯 سربازگیری: {c.RecruitmentRate}");
        sb.AppendLine($"🛡 محاصره: {c.Besieged} | 🏆 دفاع موفق: {c.DefenseWins}");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━");

        var rows = new List<InlineKeyboardButton[]>
        {
            new[] { InlineKeyboardButton.WithCallbackData("💰 ویرایش پول", $"adm:country:editmoney:{ownerId}:{chatId}"), InlineKeyboardButton.WithCallbackData("🔩 آهن", $"adm:country:editiron:{ownerId}:{chatId}") },
            new[] { InlineKeyboardButton.WithCallbackData("👥 جمعیت", $"adm:country:editpop:{ownerId}:{chatId}"), InlineKeyboardButton.WithCallbackData("🪖 سرباز", $"adm:country:editsoldiers:{ownerId}:{chatId}") },
            new[] { InlineKeyboardButton.WithCallbackData("🛡 تانک", $"adm:country:edittanks:{ownerId}:{chatId}"), InlineKeyboardButton.WithCallbackData("✈️ جنگنده", $"adm:country:editplanes:{ownerId}:{chatId}") },
            new[] { InlineKeyboardButton.WithCallbackData("🛩 بمب‌افکن", $"adm:country:editbombers:{ownerId}:{chatId}"), InlineKeyboardButton.WithCallbackData("🎯 پدافند", $"adm:country:editantiair:{ownerId}:{chatId}") },
            new[] { InlineKeyboardButton.WithCallbackData("🗑 حذف کشور", $"adm:country:delask:{ownerId}:{chatId}"), InlineKeyboardButton.WithCallbackData("👤 پلیر", $"adm:player:view:{ownerId}") },
            new[] { InlineKeyboardButton.WithCallbackData("🔙 بازگشت", "adm:countries:home"), InlineKeyboardButton.WithCallbackData("🏠 خانه", "adm:home") }
        };
        return (sb.ToString(), new InlineKeyboardMarkup(rows));
    }

    static async Task SendAdminGroupsHome(long adminId, CancellationToken ct)
    {
        if (!CanAdmin(adminId, "G_VIEW"))
        {
            await SendAdminDenied(adminId, ct);
            return;
        }
        var screen = await BuildAdminGroupsHome(ct);
        await SendPermanent(adminId, screen.Text, markup: screen.Keyboard, ct: ct);
    }

    static async Task<(string Text, InlineKeyboardMarkup Keyboard)> BuildAdminGroupsHome(CancellationToken ct = default)
    {
        var all = Database.GetAllCountries();
        var groups = all.GroupBy(c => c.ChatId).Select(g => new { ChatId = g.Key, Count = g.Count(), MP = g.Sum(c => CalcManpower(c)) }).OrderByDescending(x => x.Count).Take(10).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("👥 **مدیریت گروه‌ها** 👥");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine($"📊 کل گروه‌ها: {groups.Count} (از {all.Select(c => c.ChatId).Distinct().Count()} کل)");
        foreach (var g in groups)
        {
            string title = await GetGroupTitleCached(g.ChatId, ct);
            sb.AppendLine($"• {title} ({g.ChatId}) – 👥 {g.Count} – ⚡ {FormatManpowerK(g.MP)}");
        }
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
        var kb = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("🔍 جستجو گروه", "adm:groups:search"), InlineKeyboardButton.WithCallbackData("🏆 تاپ ممبر", "adm:lb:topgroups:count") },
            new[] { InlineKeyboardButton.WithCallbackData("⚡ تاپ مان‌پاور", "adm:lb:topgroups:mp"), InlineKeyboardButton.WithCallbackData("🔄 تازه‌سازی", "adm:groups:home") },
            new[] { InlineKeyboardButton.WithCallbackData("🏠 خانه", "adm:home") }
        });
        return (sb.ToString(), kb);
    }

    static async Task SendAdminGroupDetail(long adminId, long chatId, CancellationToken ct)
    {
        if (!CanAdmin(adminId, "G_VIEW"))
        {
            await SendAdminDenied(adminId, ct);
            return;
        }
        var screen = await BuildAdminGroupDetail(chatId, ct);
        await SendPermanent(adminId, screen.Text, markup: screen.Keyboard, ct: ct);
    }

    static async Task<(string Text, InlineKeyboardMarkup Keyboard)> BuildAdminGroupDetail(long chatId, CancellationToken ct = default)
    {
        var countries = Database.GetCountriesByChatId(chatId);
        string title = await GetGroupTitleCached(chatId, ct);
        long totalMP = countries.Sum(c => CalcManpower(c));
        var sb = new StringBuilder();
        sb.AppendLine($"👥 **گروه {title}**");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine($"🆔 آیدی: {chatId}");
        sb.AppendLine($"👥 تعداد کشور: {countries.Count}");
        sb.AppendLine($"⚡ مجموع مان‌پاور: {FormatManpowerK(totalMP)}");
        sb.AppendLine($"🔓 معافیت قفل: {(Database.HasGroupLockExemption(chatId) ? "✅ دارد" : "❌ ندارد")}");
        sb.AppendLine();
        sb.AppendLine("🏆 تاپ 5 کشور گروه:");
        foreach (var c in countries.OrderByDescending(c => CalcManpower(c)).Take(5))
        {
            sb.AppendLine($"• {c.Name} – {c.OwnerName} – {FormatManpowerK(CalcManpower(c))}");
        }
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
        var kb = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData(Database.HasGroupLockExemption(chatId) ? "🔒 حذف معافیت قفل" : "🔓 افزودن معافیت قفل", $"adm:group:togglelock:{chatId}"), InlineKeyboardButton.WithCallbackData("🧹 پاکسازی کول‌داون", $"adm:group:clearcd:{chatId}") },
            new[] { InlineKeyboardButton.WithCallbackData("🛡 معافیت سپر همه", $"adm:group:shieldall:{chatId}"), InlineKeyboardButton.WithCallbackData("📊 دارایی روزانه", $"adm:group:assetnow:{chatId}") },
            new[] { InlineKeyboardButton.WithCallbackData("🔙 بازگشت", "adm:groups:home"), InlineKeyboardButton.WithCallbackData("🏠 خانه", "adm:home") }
        });
        return (sb.ToString(), kb);
    }

    static async Task SendAdminAlliancesHome(long adminId, CancellationToken ct)
    {
        if (!CanAdmin(adminId, "ALLY"))
        {
            await SendAdminDenied(adminId, ct);
            return;
        }
        var screen = BuildAdminAlliancesHome();
        await SendPermanent(adminId, screen.Text, markup: screen.Keyboard, ct: ct);
    }

    static (string Text, InlineKeyboardMarkup Keyboard) BuildAdminAlliancesHome()
    {
        var allAlliances = new List<Alliance>();
        var allCountries = Database.GetAllCountries();
        var chatIds = allCountries.Select(c => c.ChatId).Distinct().ToList();
        foreach (var cid in chatIds)
        {
            allAlliances.AddRange(Database.GetAlliancesByChatId(cid));
        }
        var top = allAlliances.Take(15).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("🤝 **مدیریت اتحادها** 🤝");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine($"📊 کل اتحادها: {allAlliances.Count}");
        foreach (var a in top.Take(10))
        {
            var members = Database.GetAllianceMembers(a.Id);
            sb.AppendLine($"• {a.Name} – {a.ChatId} – 👑 {a.LeaderId} – 👥 {members.Count}");
        }
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
        var kb = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("🔄 تازه‌سازی", "adm:alliances:home"), InlineKeyboardButton.WithCallbackData("🏠 خانه", "adm:home") }
        });
        return (sb.ToString(), kb);
    }

    static async Task SendAdminEconomyHome(long adminId, CancellationToken ct)
    {
        if (!CanAdmin(adminId, "ROYAL"))
        {
            await SendAdminDenied(adminId, ct);
            return;
        }
        var screen = BuildAdminEconomyHome();
        await SendPermanent(adminId, screen.Text, markup: screen.Keyboard, ct: ct);
    }

    static (string Text, InlineKeyboardMarkup Keyboard) BuildAdminEconomyHome()
    {
        var sb = new StringBuilder();
        sb.AppendLine("💎 **اقتصاد و رویال** 💎");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
        string lbChannel = Database.GetSetting("LeaderboardChannelId");
        sb.AppendLine($"📢 کانال لیدربورد: {(string.IsNullOrWhiteSpace(lbChannel) || lbChannel == "0" ? "تنظیم نشده" : lbChannel)}");
        sb.AppendLine();
        sb.AppendLine("💡 برای واریز/کسر رویال، آیدی پلیر را وارد کنید.");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
        var kb = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("💰 واریز رویال", "adm:economy:royal:add"), InlineKeyboardButton.WithCallbackData("💸 کسر رویال", "adm:economy:royal:deduct") },
            new[] { InlineKeyboardButton.WithCallbackData("🏆 تاپ رویال", "adm:economy:toproyal"), InlineKeyboardButton.WithCallbackData("📊 تنظیمات اقتصادی", "adm:settings:home") },
            new[] { InlineKeyboardButton.WithCallbackData("🏠 خانه", "adm:home") }
        });
        return (sb.ToString(), kb);
    }

    static async Task SendAdminWarHome(long adminId, CancellationToken ct)
    {
        if (!CanAdmin(adminId, "W_VIEW"))
        {
            await SendAdminDenied(adminId, ct);
            return;
        }
        var screen = await BuildAdminWarHome(ct);
        await SendPermanent(adminId, screen.Text, markup: screen.Keyboard, ct: ct);
    }

    static async Task<(string Text, InlineKeyboardMarkup Keyboard)> BuildAdminWarHome(CancellationToken ct = default)
    {
        var sieged = Database.GetSiegedCountries(10);
        var recentBattles = Database.GetRecentBattles(5);
        var sb = new StringBuilder();
        sb.AppendLine("⚔️ **مدیریت جنگ** ⚔️");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine($"🔥 کشورهای محاصره شده: {sieged.Count}");
        foreach (var c in sieged.Take(5))
        {
            sb.AppendLine($"• {c.Name} – {c.OwnerName} – محاصره {c.Besieged} – {c.Cities} شهر");
        }
        sb.AppendLine();
        sb.AppendLine("📜 5 نبرد اخیر:");
        foreach (var b in recentBattles)
        {
            sb.AppendLine($"• {b.AttackerName} vs {b.DefenderName} – {b.Winner} – {b.SuccessPercent}%");
        }
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
        var kb = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("🔥 محاصره‌ها", "adm:war:sieged"), InlineKeyboardButton.WithCallbackData("📜 نبردها", "adm:war:battles") },
            new[] { InlineKeyboardButton.WithCallbackData("🛡 رفع بن حمله", "adm:war:clearlocks"), InlineKeyboardButton.WithCallbackData("🏠 خانه", "adm:home") }
        });
        return (sb.ToString(), kb);
    }

    static async Task SendAdminOperationsHome(long adminId, CancellationToken ct)
    {
        if (!CanAdmin(adminId, "O_VIEW"))
        {
            await SendAdminDenied(adminId, ct);
            return;
        }
        var screen = BuildAdminOperationsHome();
        await SendPermanent(adminId, screen.Text, markup: screen.Keyboard, ct: ct);
    }

    static (string Text, InlineKeyboardMarkup Keyboard) BuildAdminOperationsHome()
    {
        var transfers = Database.GetActiveTransfers();
        var deployments = Database.GetActiveDeployments();
        var sb = new StringBuilder();
        sb.AppendLine("🚚 **عملیات و لجستیک** 🚚");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine($"📦 ترنسفر فعال: {transfers.Count}");
        foreach (var t in transfers.Take(5))
        {
            sb.AppendLine($"• {t.SenderId} → {t.ReceiverId} – {t.ResourceType} {t.Amount} – {t.ModelName}");
        }
        sb.AppendLine();
        sb.AppendLine($"⚔️ صف‌آرایی فعال: {deployments.Count}");
        foreach (var d in deployments.Take(5))
        {
            sb.AppendLine($"• {d.Type} – {d.ChatId} – {d.Tanks}🛡 {d.Soldiers}🪖");
        }
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
        var kb = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("📦 ترنسفرها", "adm:ops:transfers"), InlineKeyboardButton.WithCallbackData("⚔️ صف‌آرایی‌ها", "adm:ops:deployments") },
            new[] { InlineKeyboardButton.WithCallbackData("🧹 لغو همه ترنسفرها (تست)", "adm:ops:cleartransfers"), InlineKeyboardButton.WithCallbackData("🏠 خانه", "adm:home") }
        });
        return (sb.ToString(), kb);
    }

    static async Task SendAdminAnnounceHome(long adminId, CancellationToken ct)
    {
        if (!CanAdmin(adminId, "ANN"))
        {
            await SendAdminDenied(adminId, ct);
            return;
        }
        var screen = BuildAdminAnnounceHome();
        await SendPermanent(adminId, screen.Text, markup: screen.Keyboard, ct: ct);
    }

    static (string Text, InlineKeyboardMarkup Keyboard) BuildAdminAnnounceHome()
    {
        var sb = new StringBuilder();
        sb.AppendLine("📢 **مدیریت اعلامیه** 📢");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine("برای ارسال اعلامیه، متن را تایپ کنید و سپس مقصد را انتخاب کنید.");
        sb.AppendLine();
        sb.AppendLine("• 👥 همه گروه‌ها");
        sb.AppendLine("• 👤 همه پیوی‌ها");
        sb.AppendLine("• 🌐 همه");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
        var kb = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("📝 نوشتن اعلامیه", "adm:ann:write"), InlineKeyboardButton.WithCallbackData("🏆 ارسال لیدربورد الان", "adm:lb:now") },
            new[] { InlineKeyboardButton.WithCallbackData("🏠 خانه", "adm:home") }
        });
        return (sb.ToString(), kb);
    }

    static async Task SendAdminSettingsHome(long adminId, CancellationToken ct)
    {
        if (!CanAdmin(adminId, "SET"))
        {
            await SendAdminDenied(adminId, ct);
            return;
        }
        var screen = BuildAdminSettingsHome();
        await SendPermanent(adminId, screen.Text, markup: screen.Keyboard, ct: ct);
    }

    static (string Text, InlineKeyboardMarkup Keyboard) BuildAdminSettingsHome()
    {
        string lbChannel = Database.GetSetting("LeaderboardChannelId");
        string updateMode = Database.GetSetting("UpdateMode");
        if (string.IsNullOrWhiteSpace(updateMode)) updateMode = UpdateMode;
        string updateVal = Database.GetSetting("UpdateValue");
        if (string.IsNullOrWhiteSpace(updateVal)) updateVal = UpdateValue.ToString();

        var sb = new StringBuilder();
        sb.AppendLine("⚙️ **تنظیمات آلیس** ⚙️");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine($"⏰ آپدیت: {updateMode} – {updateVal}");
        sb.AppendLine($"🔒 قفل حمله: {ATTACK_LOCK_MINUTES} دقیقه");
        sb.AppendLine($"🛡 سپر اولیه: {SHIELD_HOURS} ساعت");
        sb.AppendLine($"⚔️ سقف حمله: {MAX_ATTACKS_PER_UPDATE}");
        sb.AppendLine($"📦 سقف ترنسفر: {MAX_TRANSFERS_PER_UPDATE}");
        sb.AppendLine($"📢 کانال لیدربورد: {(string.IsNullOrWhiteSpace(lbChannel) || lbChannel == "0" ? "تنظیم نشده ❌" : lbChannel + " ✅")}");
        sb.AppendLine();
        sb.AppendLine("هر شب ساعت 22:00 سه لیدربورد به پیوی ادمین و کانال (اگر تنظیم شده) ارسال می‌شود.");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━");

        var kb = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("📢 تنظیم کانال لیدربورد", "adm:settings:lbchannel"), InlineKeyboardButton.WithCallbackData("🗑 حذف کانال", "adm:settings:lbchannel:clear") },
            new[] { InlineKeyboardButton.WithCallbackData("⏰ قفل حمله", "adm:settings:attacklock"), InlineKeyboardButton.WithCallbackData("🛡 سپر", "adm:settings:shield") },
            new[] { InlineKeyboardButton.WithCallbackData("⚔️ سقف حمله", "adm:settings:maxattacks"), InlineKeyboardButton.WithCallbackData("📦 سقف ترنسفر", "adm:settings:maxtransfers") },
            new[] { InlineKeyboardButton.WithCallbackData("🏆 ارسال لیدربورد الان", "adm:lb:now"), InlineKeyboardButton.WithCallbackData("🔄 تازه‌سازی", "adm:settings:home") },
            new[] { InlineKeyboardButton.WithCallbackData("🏠 خانه", "adm:home") }
        });
        return (sb.ToString(), kb);
    }

    static async Task SendAdminMaintenanceHome(long adminId, CancellationToken ct)
    {
        if (!CanAdminAny(adminId, "BACKUP", "RESTORE"))
        {
            await SendAdminDenied(adminId, ct);
            return;
        }
        var screen = BuildAdminMaintenanceHome();
        await SendPermanent(adminId, screen.Text, markup: screen.Keyboard, ct: ct);
    }

    static (string Text, InlineKeyboardMarkup Keyboard) BuildAdminMaintenanceHome()
    {
        long dbSize = 0;
        try { if (System.IO.File.Exists("gamedata.db")) dbSize = new System.IO.FileInfo("gamedata.db").Length; } catch { }
        var sb = new StringBuilder();
        sb.AppendLine("🗄 **نگهداری و بکاپ** 🗄");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine($"💾 حجم دیتابیس: {dbSize / 1024.0 / 1024.0:F2} MB");
        sb.AppendLine($"📊 کشورها: {Database.GetAllCountries().Count}");
        sb.AppendLine();
        sb.AppendLine("• بکاپ: فایل gamedata.db برای شما ارسال می‌شود");
        sb.AppendLine("• ریستور: فایل دیتابیس جدید را آپلود کنید");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
        var kb = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("📥 دریافت بکاپ", "adm:backup:get"), InlineKeyboardButton.WithCallbackData("📤 آپلود بکاپ", "adm:backup:upload") },
            new[] { InlineKeyboardButton.WithCallbackData("🧹 پاکسازی لاگ‌ها", "adm:maintenance:cleanup"), InlineKeyboardButton.WithCallbackData("🏠 خانه", "adm:home") }
        });
        return (sb.ToString(), kb);
    }

    static async Task HandleAdminCallbackAsync(
        CallbackQuery callback,
        CancellationToken ct)
    {
        long userId = callback.From.Id;

        if (!IsPanelAdmin(userId))
        {
            await AnswerAdminCallback(
                callback,
                "⛔ دسترسی مدیریتی ندارید.",
                true,
                ct
            );
            return;
        }

        if (callback.Data == null ||
            callback.Message == null)
        {
            return;
        }

        Database.TouchAdmin(userId);

        string[] parts =
            callback.Data.Split(':');

        if (parts.Length < 2)
            return;

        string action = parts[1];

        if (action == "home")
        {
            await RenderAdminScreen(
                callback,
                BuildAdminHomeScreen(userId),
                ct
            );

            await AnswerAdminCallback(
                callback,
                null,
                false,
                ct
            );
            return;
        }

        if (action == "dash")
        {
            if (!CanAdmin(userId, "DASH"))
            {
                await AnswerAdminCallback(
                    callback,
                    "⛔ دسترسی ندارید.",
                    true,
                    ct
                );
                return;
            }

            await RenderAdminScreen(
                callback,
                BuildAdminDashboardScreen(),
                ct
            );

            await AnswerAdminCallback(
                callback,
                null,
                false,
                ct
            );
            return;
        }

        if (action == "admins")
        {
            if (!IsPanelOwner(userId))
            {
                await AnswerAdminCallback(
                    callback,
                    "⛔ فقط مالک اصلی.",
                    true,
                    ct
                );
                return;
            }

            int page =
                parts.Length >= 3 &&
                TryParseInt(parts[2], out int p)
                    ? p
                    : 0;

            await RenderAdminScreen(
                callback,
                BuildAdminListScreen(page),
                ct
            );

            await AnswerAdminCallback(
                callback,
                null,
                false,
                ct
            );
            return;
        }

        if (action == "add")
        {
            if (!IsPanelOwner(userId))
            {
                await AnswerAdminCallback(
                    callback,
                    "⛔ فقط مالک اصلی.",
                    true,
                    ct
                );
                return;
            }

            adminInputRequests[userId] =
                new AdminInputRequest
                {
                    Kind = "add_admin",
                    ExpiresAtMs =
                        DateTimeOffset.UtcNow
                            .AddMinutes(5)
                            .ToUnixTimeMilliseconds()
                };

            var keyboard =
                new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton
                            .WithCallbackData(
                                "❌ لغو",
                                "adm:cancelinput"
                            )
                    }
                });

            await RenderAdminScreen(
                callback,
                (
                    "➕ افزودن مدیر\n\n" +
                    "آیدی عددی و نام مدیر را ارسال کنید.\n\n" +
                    "مثال:\n" +
                    "123456789 مدیر اقتصاد\n\n" +
                    "مدیر جدید بدون هیچ Permission ساخته می‌شود.",
                    keyboard
                ),
                ct
            );

            await AnswerAdminCallback(
                callback,
                null,
                false,
                ct
            );
            return;
        }

        if (action == "cancelinput")
        {
            adminInputRequests.TryRemove(
                userId,
                out _
            );

            await RenderAdminScreen(
                callback,
                BuildAdminHomeScreen(userId),
                ct
            );

            await AnswerAdminCallback(
                callback,
                "لغو شد.",
                false,
                ct
            );
            return;
        }

        if (action == "view" &&
            parts.Length >= 3 &&
            TryParseLong(parts[2], out long viewId))
        {
            if (!IsPanelOwner(userId))
            {
                await AnswerAdminCallback(
                    callback,
                    "⛔ فقط مالک اصلی.",
                    true,
                    ct
                );
                return;
            }

            await RenderAdminScreen(
                callback,
                BuildAdminDetailsScreen(viewId),
                ct
            );

            await AnswerAdminCallback(
                callback,
                null,
                false,
                ct
            );
            return;
        }

        if (action == "perms" &&
            parts.Length >= 4 &&
            TryParseLong(parts[2], out long permissionId) &&
            TryParseInt(parts[3], out int permissionPage))
        {
            if (!IsPanelOwner(userId))
            {
                await AnswerAdminCallback(
                    callback,
                    "⛔ فقط مالک اصلی.",
                    true,
                    ct
                );
                return;
            }

            await RenderAdminScreen(
                callback,
                BuildAdminPermissionScreen(
                    permissionId,
                    permissionPage
                ),
                ct
            );

            await AnswerAdminCallback(
                callback,
                null,
                false,
                ct
            );
            return;
        }

        if (action == "perm" &&
            parts.Length >= 5 &&
            TryParseLong(parts[2], out long permAdminId) &&
            TryParseInt(parts[4], out int permPage))
        {
            if (!IsPanelOwner(userId))
            {
                await AnswerAdminCallback(
                    callback,
                    "⛔ فقط مالک اصلی.",
                    true,
                    ct
                );
                return;
            }

            string permissionCode = parts[3];

            if (!AdminPermissionCatalog.Exists(
                    permissionCode))
            {
                await AnswerAdminCallback(
                    callback,
                    "Permission نامعتبر.",
                    true,
                    ct
                );
                return;
            }

            var admin =
                Database.GetAdmin(permAdminId);

            if (admin == null || admin.IsOwner)
            {
                await AnswerAdminCallback(
                    callback,
                    "مدیر قابل ویرایش نیست.",
                    true,
                    ct
                );
                return;
            }

            bool currentlyEnabled =
                Database.HasAdminPermission(
                    permAdminId,
                    permissionCode
                );

            Database.SetAdminPermission(
                permAdminId,
                permissionCode,
                !currentlyEnabled,
                userId
            );

            Database.WriteAdminAudit(
                userId,
                currentlyEnabled
                    ? "PERMISSION_REVOKE"
                    : "PERMISSION_GRANT",
                "Admin",
                permAdminId.ToString(),
                permissionCode
            );

            await RenderAdminScreen(
                callback,
                BuildAdminPermissionScreen(
                    permAdminId,
                    permPage
                ),
                ct
            );

            await AnswerAdminCallback(
                callback,
                currentlyEnabled
                    ? "دسترسی حذف شد."
                    : "دسترسی اعطا شد.",
                false,
                ct
            );
            return;
        }

        if (action == "allask" &&
            parts.Length >= 4 &&
            TryParseLong(parts[2], out long allAskId) &&
            TryParseInt(parts[3], out int allAskValue))
        {
            if (!IsPanelOwner(userId))
            {
                await AnswerAdminCallback(
                    callback,
                    "⛔ فقط مالک اصلی.",
                    true,
                    ct
                );
                return;
            }

            bool enableAll = allAskValue == 1;

            string text =
                enableAll
                    ? "⚠️ همه دسترسی‌ها، شامل Restore و حذف کشور، به این مدیر داده شود؟"
                    : "⚠️ تمام دسترسی‌های این مدیر حذف شود؟";

            var keyboard =
                new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton
                            .WithCallbackData(
                                "✅ تأیید نهایی",
                                $"adm:all:{allAskId}:{allAskValue}"
                            )
                    },
                    new[]
                    {
                        InlineKeyboardButton
                            .WithCallbackData(
                                "❌ انصراف",
                                $"adm:perms:{allAskId}:0"
                            )
                    }
                });

            await RenderAdminScreen(
                callback,
                (text, keyboard),
                ct
            );

            await AnswerAdminCallback(
                callback,
                null,
                false,
                ct
            );
            return;
        }

        if (action == "all" &&
            parts.Length >= 4 &&
            TryParseLong(parts[2], out long allId) &&
            TryParseInt(parts[3], out int allValue))
        {
            if (!IsPanelOwner(userId))
            {
                await AnswerAdminCallback(
                    callback,
                    "⛔ فقط مالک اصلی.",
                    true,
                    ct
                );
                return;
            }

            bool enable = allValue == 1;

            Database.SetAllAdminPermissions(
                allId,
                enable,
                userId
            );

            Database.WriteAdminAudit(
                userId,
                enable
                    ? "PERMISSIONS_GRANT_ALL"
                    : "PERMISSIONS_REVOKE_ALL",
                "Admin",
                allId.ToString()
            );

            await RenderAdminScreen(
                callback,
                BuildAdminPermissionScreen(allId, 0),
                ct
            );

            await AnswerAdminCallback(
                callback,
                enable
                    ? "همه دسترسی‌ها اعطا شد."
                    : "همه دسترسی‌ها حذف شد.",
                true,
                ct
            );
            return;
        }

        if (action == "toggle" &&
            parts.Length >= 3 &&
            TryParseLong(parts[2], out long toggleId))
        {
            if (!IsPanelOwner(userId))
            {
                await AnswerAdminCallback(
                    callback,
                    "⛔ فقط مالک اصلی.",
                    true,
                    ct
                );
                return;
            }

            var admin = Database.GetAdmin(toggleId);

            if (admin == null || admin.IsOwner)
            {
                await AnswerAdminCallback(
                    callback,
                    "مدیر قابل تغییر نیست.",
                    true,
                    ct
                );
                return;
            }

            bool newState = !admin.IsActive;

            Database.SetAdminActive(
                toggleId,
                newState
            );

            Database.WriteAdminAudit(
                userId,
                newState
                    ? "ADMIN_ACTIVATE"
                    : "ADMIN_DEACTIVATE",
                "Admin",
                toggleId.ToString()
            );

            await RenderAdminScreen(
                callback,
                BuildAdminDetailsScreen(toggleId),
                ct
            );

            await AnswerAdminCallback(
                callback,
                newState
                    ? "مدیر فعال شد."
                    : "مدیر غیرفعال شد.",
                false,
                ct
            );
            return;
        }

        if (action == "removeask" &&
            parts.Length >= 3 &&
            TryParseLong(parts[2], out long removeAskId))
        {
            if (!IsPanelOwner(userId))
            {
                await AnswerAdminCallback(
                    callback,
                    "⛔ فقط مالک اصلی.",
                    true,
                    ct
                );
                return;
            }

            var admin =
                Database.GetAdmin(removeAskId);

            if (admin == null || admin.IsOwner)
            {
                await AnswerAdminCallback(
                    callback,
                    "مدیر قابل حذف نیست.",
                    true,
                    ct
                );
                return;
            }

            var keyboard =
                new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton
                            .WithCallbackData(
                                "🗑 بله، حذف شود",
                                $"adm:remove:{removeAskId}"
                            )
                    },
                    new[]
                    {
                        InlineKeyboardButton
                            .WithCallbackData(
                                "❌ انصراف",
                                $"adm:view:{removeAskId}"
                            )
                    }
                });

            string text =
                "⚠️ تأیید حذف مدیر\n\n" +
                $"نام: {admin.DisplayName}\n" +
                $"آیدی: {admin.AdminId}\n\n" +
                "تمام Permissionهای این مدیر نیز حذف می‌شود.";

            await RenderAdminScreen(
                callback,
                (text, keyboard),
                ct
            );

            await AnswerAdminCallback(
                callback,
                null,
                false,
                ct
            );
            return;
        }

        if (action == "remove" &&
            parts.Length >= 3 &&
            TryParseLong(parts[2], out long removeId))
        {
            if (!IsPanelOwner(userId))
            {
                await AnswerAdminCallback(
                    callback,
                    "⛔ فقط مالک اصلی.",
                    true,
                    ct
                );
                return;
            }

            bool removed =
                Database.DeleteAdmin(removeId);

            Database.WriteAdminAudit(
                userId,
                "ADMIN_DELETE",
                "Admin",
                removeId.ToString(),
                "",
                removed
            );

            await RenderAdminScreen(
                callback,
                BuildAdminListScreen(0),
                ct
            );

            await AnswerAdminCallback(
                callback,
                removed
                    ? "مدیر حذف شد."
                    : "حذف انجام نشد.",
                !removed,
                ct
            );
            return;
        }

        if (action == "audit")
        {
            if (!CanAdmin(userId, "AUDIT"))
            {
                await AnswerAdminCallback(
                    callback,
                    "⛔ دسترسی ندارید.",
                    true,
                    ct
                );
                return;
            }

            await RenderAdminScreen(
                callback,
                BuildAdminAuditScreen(),
                ct
            );

            await AnswerAdminCallback(
                callback,
                null,
                false,
                ct
            );
            return;
        }

        // ================= PLAYERS MODULE =================
        if (action == "players")
        {
            if (!CanAdmin(userId, "P_VIEW"))
            {
                await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct);
                return;
            }
            string sub = parts.Length >= 3 ? parts[2] : "home";
            if (sub == "home")
            {
                await RenderAdminScreen(callback, await BuildAdminPlayersHome(ct), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return;
            }
            if (sub == "search")
            {
                adminInputRequests[userId] = new AdminInputRequest { Kind = "search_player", ExpiresAtMs = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds() };
                await RenderAdminScreen(callback, ("🔍 جستجوی پلیر\n\nآیدی عددی پلیر را وارد کنید.\nبرای لغو بنویسید: لغو", new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("❌ لغو", "adm:cancelinput") } })), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return;
            }
            if (sub == "banned")
            {
                var banned = Database.GetBannedUsers(20);
                var sb = new StringBuilder();
                sb.AppendLine("🚫 **لیست بن شده‌ها**");
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
                foreach (var b in banned)
                {
                    sb.AppendLine($"• {b.UserId} – {b.Reason} – {FormatAdminTime(b.BannedAtMs)}");
                }
                if (banned.Count == 0) sb.AppendLine("❌ کسی بن نیست.");
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
                var kb = new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("🔙 بازگشت", "adm:players:home"), InlineKeyboardButton.WithCallbackData("🏠 خانه", "adm:home") } });
                await RenderAdminScreen(callback, (sb.ToString(), kb), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return;
            }
        }

        if (action == "player" && parts.Length >= 4)
        {
            string sub = parts[2];
            if (!TryParseLong(parts[3], out long targetId)) { await AnswerAdminCallback(callback, "❌ آیدی نامعتبر", true, ct); return; }
            if (sub == "view")
            {
                await RenderAdminScreen(callback, BuildAdminPlayerDetail(targetId), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return;
            }
            if (sub == "countries")
            {
                var countries = Database.GetCountriesByOwnerId(targetId);
                var sb = new StringBuilder();
                sb.AppendLine($"🌍 کشورها پلیر {targetId} – {countries.Count} عدد");
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
                foreach (var c in countries.Take(10))
                    sb.AppendLine($"• {c.Name} – {c.ChatId} – {FormatManpowerK(CalcManpower(c))}");
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
                var kb = new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("🔙 بازگشت", $"adm:player:view:{targetId}"), InlineKeyboardButton.WithCallbackData("🏠 خانه", "adm:home") } });
                await RenderAdminScreen(callback, (sb.ToString(), kb), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return;
            }
            if (sub == "royal")
            {
                if (!CanAdmin(userId, "ROYAL")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return; }
                long royal = Database.GetRoyalCoins(targetId);
                var kb = new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData("💰 واریز", $"adm:economy:royal:add:{targetId}"), InlineKeyboardButton.WithCallbackData("💸 کسر", $"adm:economy:royal:deduct:{targetId}") },
                    new[] { InlineKeyboardButton.WithCallbackData("🔙 بازگشت", $"adm:player:view:{targetId}") }
                });
                await RenderAdminScreen(callback, ($"💎 رویال پلیر {targetId}\nموجودی: {royal:N0}", kb), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return;
            }
            if (sub == "banask")
            {
                if (!CanAdmin(userId, "P_BAN")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return; }
                adminInputRequests[userId] = new AdminInputRequest { Kind = "ban_reason", TargetId = targetId, ExpiresAtMs = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds() };
                await RenderAdminScreen(callback, ($"🚫 بن پلیر {targetId}\n\nدلیل بن را بنویسید.\nلغو: لغو", new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("❌ لغو", "adm:cancelinput") } })), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return;
            }
            if (sub == "unban")
            {
                if (!CanAdmin(userId, "P_BAN")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return; }
                Database.UnbanUser(targetId);
                Database.WriteAdminAudit(userId, "PLAYER_UNBAN", "Player", targetId.ToString(), "", true);
                await RenderAdminScreen(callback, BuildAdminPlayerDetail(targetId), ct);
                await AnswerAdminCallback(callback, "✅ آنبن شد.", false, ct);
                return;
            }
            if (sub == "delcountries")
            {
                if (!CanAdmin(userId, "C_DELETE")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return; }
                var countries = Database.GetCountriesByOwnerId(targetId);
                foreach (var c in countries) Database.DeleteCountry(c.OwnerId, c.ChatId);
                Database.WriteAdminAudit(userId, "PLAYER_DELCOUNTRIES", "Player", targetId.ToString(), $"{countries.Count}", true);
                await RenderAdminScreen(callback, BuildAdminPlayerDetail(targetId), ct);
                await AnswerAdminCallback(callback, $"✅ {countries.Count} کشور حذف شد.", false, ct);
                return;
            }
        }

        // ================= COUNTRIES MODULE =================
        if (action == "countries")
        {
            if (!CanAdmin(userId, "C_VIEW")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return; }
            string sub = parts.Length >= 3 ? parts[2] : "home";
            if (sub == "home")
            {
                await RenderAdminScreen(callback, await BuildAdminCountriesHome(ct), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return;
            }
            if (sub == "search")
            {
                adminInputRequests[userId] = new AdminInputRequest { Kind = "search_country", ExpiresAtMs = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds() };
                await RenderAdminScreen(callback, ("🔍 جستجوی کشور\n\nنام کشور را وارد کنید (حداقل 2 حرف).\nلغو: لغو", new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("❌ لغو", "adm:cancelinput") } })), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return;
            }
            if (sub == "sieged")
            {
                var sieged = Database.GetSiegedCountries(15);
                var sb = new StringBuilder();
                sb.AppendLine("🔥 **کشورهای محاصره شده**");
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
                foreach (var c in sieged)
                    sb.AppendLine($"• {c.Name} – {c.OwnerName} – محاصره {c.Besieged} – {c.Cities} شهر – {c.ChatId}");
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
                var kb = new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("🔙 بازگشت", "adm:countries:home"), InlineKeyboardButton.WithCallbackData("🏠 خانه", "adm:home") } });
                await RenderAdminScreen(callback, (sb.ToString(), kb), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return;
            }
        }

        if (action == "country" && parts.Length >= 4)
        {
            string sub = parts[2];
            if (!TryParseLong(parts[3], out long ownerId)) { await AnswerAdminCallback(callback, "❌ آیدی نامعتبر", true, ct); return; }
            long chatId = parts.Length >= 5 && TryParseLong(parts[4], out long cId) ? cId : 0;

            if (sub == "view" && chatId != 0)
            {
                await RenderAdminScreen(callback, BuildAdminCountryDetail(ownerId, chatId), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return;
            }
            if (sub.StartsWith("edit"))
            {
                if (!CanAdmin(userId, "C_RES") && (sub == "editmoney" || sub == "editiron" || sub == "editpop")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return; }
                if (!CanAdmin(userId, "C_ARMY") && (sub.Contains("soldiers") || sub.Contains("tanks") || sub.Contains("planes") || sub.Contains("bombers") || sub.Contains("antiair"))) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return; }
                string kind = sub switch
                {
                    "editmoney" => "edit_country_money",
                    "editiron" => "edit_country_iron",
                    "editpop" => "edit_country_pop",
                    "editsoldiers" => "edit_country_soldiers",
                    "edittanks" => "edit_country_tanks",
                    "editplanes" => "edit_country_planes",
                    "editbombers" => "edit_country_bombers",
                    "editantiair" => "edit_country_antiair",
                    _ => ""
                };
                if (string.IsNullOrEmpty(kind)) { await AnswerAdminCallback(callback, "❌ نامشخص", true, ct); return; }
                adminInputRequests[userId] = new AdminInputRequest { Kind = kind, TargetId = ownerId, ChatId = chatId, ExpiresAtMs = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds() };
                await RenderAdminScreen(callback, ($"✏️ ویرایش {sub}\n\nمقدار جدید را وارد کنید برای کشور {ownerId}:{chatId}\nلغو: لغو", new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("❌ لغو", "adm:cancelinput") } })), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return;
            }
            if (sub == "delask")
            {
                if (!CanAdmin(userId, "C_DELETE")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return; }
                var kb = new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData("✅ بله حذف شود", $"adm:country:del:{ownerId}:{chatId}"), InlineKeyboardButton.WithCallbackData("❌ انصراف", $"adm:country:view:{ownerId}:{chatId}") }
                });
                await RenderAdminScreen(callback, ($"⚠️ حذف کشور {ownerId}:{chatId}؟", kb), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return;
            }
            if (sub == "del" && chatId != 0)
            {
                if (!CanAdmin(userId, "C_DELETE")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return; }
                Database.DeleteCountry(ownerId, chatId);
                Database.WriteAdminAudit(userId, "COUNTRY_DELETE", "Country", $"{ownerId}:{chatId}", "", true);
                await RenderAdminScreen(callback, ("✅ کشور حذف شد.", new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("🏠 خانه", "adm:home") } })), ct);
                await AnswerAdminCallback(callback, "✅ حذف شد.", false, ct);
                return;
            }
        }

        // ================= GROUPS MODULE =================
        if (action == "groups")
        {
            if (!CanAdmin(userId, "G_VIEW")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return; }
            string sub = parts.Length >= 3 ? parts[2] : "home";
            if (sub == "home")
            {
                await RenderAdminScreen(callback, await BuildAdminGroupsHome(ct), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return;
            }
            if (sub == "search")
            {
                adminInputRequests[userId] = new AdminInputRequest { Kind = "search_group", ExpiresAtMs = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds() };
                await RenderAdminScreen(callback, ("🔍 جستجوی گروه\n\nآیدی عددی گروه را وارد کنید.\nلغو: لغو", new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("❌ لغو", "adm:cancelinput") } })), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return;
            }
        }

        if (action == "group" && parts.Length >= 4)
        {
            string sub = parts[2];
            if (!TryParseLong(parts[3], out long chatId)) { await AnswerAdminCallback(callback, "❌ آیدی نامعتبر", true, ct); return; }
            if (sub == "view")
            {
                await RenderAdminScreen(callback, await BuildAdminGroupDetail(chatId, ct), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return;
            }
            if (sub == "togglelock")
            {
                if (!CanAdmin(userId, "G_EDIT")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return; }
                bool has = Database.HasGroupLockExemption(chatId);
                Database.SetGroupLockExemption(chatId, !has);
                Database.WriteAdminAudit(userId, has ? "GROUP_LOCK_REMOVE" : "GROUP_LOCK_ADD", "Group", chatId.ToString(), "", true);
                await RenderAdminScreen(callback, await BuildAdminGroupDetail(chatId, ct), ct);
                await AnswerAdminCallback(callback, has ? "✅ معافیت حذف شد." : "✅ معافیت افزوده شد.", false, ct);
                return;
            }
            if (sub == "clearcd")
            {
                if (!CanAdmin(userId, "G_EDIT")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return; }
                Database.ClearAllLeaveCooldownsInChat(chatId);
                Database.WriteAdminAudit(userId, "GROUP_CLEAR_CD", "Group", chatId.ToString(), "", true);
                await RenderAdminScreen(callback, await BuildAdminGroupDetail(chatId, ct), ct);
                await AnswerAdminCallback(callback, "✅ کول‌داون‌ها پاک شد.", false, ct);
                return;
            }
            if (sub == "shieldall")
            {
                if (!CanAdmin(userId, "G_EDIT")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return; }
                Database.SetAllShieldExemptionsInChat(chatId);
                Database.WriteAdminAudit(userId, "GROUP_SHIELD_ALL", "Group", chatId.ToString(), "", true);
                await RenderAdminScreen(callback, await BuildAdminGroupDetail(chatId, ct), ct);
                await AnswerAdminCallback(callback, "✅ همه معافیت سپر گرفتند.", false, ct);
                return;
            }
            if (sub == "assetnow")
            {
                if (!CanAdmin(userId, "G_EDIT")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return; }
                try { await RunAssetUpdate(force: true); await AnswerAdminCallback(callback, "✅ آپدیت دارایی اجرا شد.", false, ct); }
                catch (Exception ex) { await AnswerAdminCallback(callback, $"❌ خطا: {ex.Message}", true, ct); }
                return;
            }
        }

        // ================= ALLIANCES MODULE =================
        if (action == "alliances")
        {
            if (!CanAdmin(userId, "ALLY")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return; }
            string sub = parts.Length >= 3 ? parts[2] : "home";
            if (sub == "home")
            {
                await RenderAdminScreen(callback, BuildAdminAlliancesHome(), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return;
            }
        }

        if (action == "alliance" && parts.Length >= 4)
        {
            if (!TryParseLong(parts[3], out long allianceId)) { await AnswerAdminCallback(callback, "❌ آیدی نامعتبر", true, ct); return; }
            string sub = parts[2];
            if (sub == "view")
            {
                var alliance = Database.GetAllianceById(allianceId);
                if (alliance == null) { await AnswerAdminCallback(callback, "❌ اتحاد یافت نشد.", true, ct); return; }
                var members = Database.GetAllianceMembers(allianceId);
                var sb = new StringBuilder();
                sb.AppendLine($"🤝 **{alliance.Name}**");
                sb.AppendLine($"🆔 {alliance.Id} | 🌍 {alliance.ChatId} | 👑 {alliance.LeaderId}");
                sb.AppendLine($"👥 اعضا: {members.Count}");
                foreach (var m in members.Take(15))
                {
                    var c = Database.GetCountry(m, alliance.ChatId);
                    sb.AppendLine($"• {m} – {c?.Name ?? "بدون کشور"} – {c?.OwnerName}");
                }
                var kb = new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData("🗑 حذف اتحاد", $"adm:alliance:del:{allianceId}"), InlineKeyboardButton.WithCallbackData("🔙 بازگشت", "adm:alliances:home") }
                });
                await RenderAdminScreen(callback, (sb.ToString(), kb), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return;
            }
            if (sub == "del")
            {
                if (!CanAdmin(userId, "ALLY")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return; }
                Database.DeleteAlliance(allianceId);
                Database.WriteAdminAudit(userId, "ALLIANCE_DELETE", "Alliance", allianceId.ToString(), "", true);
                await RenderAdminScreen(callback, BuildAdminAlliancesHome(), ct);
                await AnswerAdminCallback(callback, "✅ اتحاد حذف شد.", false, ct);
                return;
            }
        }

        // ================= ECONOMY MODULE =================
        if (action == "economy")
        {
            if (!CanAdmin(userId, "ROYAL")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return; }
            string sub = parts.Length >= 3 ? parts[2] : "home";
            if (sub == "home")
            {
                await RenderAdminScreen(callback, BuildAdminEconomyHome(), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return;
            }
            if (sub == "royal" && parts.Length >= 4)
            {
                string op = parts[3];
                long targetId = parts.Length >= 5 && TryParseLong(parts[4], out long tid) ? tid : 0;
                if (targetId == 0)
                {
                    adminInputRequests[userId] = new AdminInputRequest { Kind = op == "add" ? "royal_add" : "royal_deduct", TargetId = 0, ExpiresAtMs = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds(), Extra = op };
                    await RenderAdminScreen(callback, ("💎 رویال\n\nآیدی و مقدار را وارد کنید مثل: 123456 100\nلغو: لغو", new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("❌ لغو", "adm:cancelinput") } })), ct);
                    await AnswerAdminCallback(callback, null, false, ct);
                    return;
                }
                else
                {
                    adminInputRequests[userId] = new AdminInputRequest { Kind = op == "add" ? "royal_add" : "royal_deduct", TargetId = targetId, ExpiresAtMs = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds() };
                    await RenderAdminScreen(callback, ($"💎 {(op == "add" ? "واریز" : "کسر")} رویال برای {targetId}\n\nمقدار را وارد کنید:\nلغو: لغو", new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("❌ لغو", "adm:cancelinput") } })), ct);
                    await AnswerAdminCallback(callback, null, false, ct);
                    return;
                }
            }
            if (sub == "toproyal")
            {
                var all = Database.GetAllCountries().Select(c => c.OwnerId).Distinct().Take(100).Select(id => new { Id = id, Royal = Database.GetRoyalCoins(id) }).OrderByDescending(x => x.Royal).Take(10).ToList();
                var sb = new StringBuilder();
                sb.AppendLine("💎 **تاپ رویال**");
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
                foreach (var r in all)
                    sb.AppendLine($"• {r.Id} – {r.Royal:N0}");
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
                var kb = new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("🔙 بازگشت", "adm:economy:home") } });
                await RenderAdminScreen(callback, (sb.ToString(), kb), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return;
            }
        }

        // ================= WAR MODULE =================
        if (action == "war")
        {
            if (!CanAdmin(userId, "W_VIEW")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return; }
            string sub = parts.Length >= 3 ? parts[2] : "home";
            if (sub == "home")
            {
                await RenderAdminScreen(callback, await BuildAdminWarHome(ct), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return;
            }
            if (sub == "sieged")
            {
                var sieged = Database.GetSiegedCountries(20);
                var sb = new StringBuilder();
                sb.AppendLine("🔥 **محاصره شده‌ها**");
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
                foreach (var c in sieged)
                    sb.AppendLine($"• {c.Name} – {c.OwnerName} – {c.ChatId} – محاصره {c.Besieged} – {c.Cities} شهر");
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
                var kb = new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("🔙 بازگشت", "adm:war:home") } });
                await RenderAdminScreen(callback, (sb.ToString(), kb), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return;
            }
            if (sub == "battles")
            {
                var battles = Database.GetRecentBattles(15);
                var sb = new StringBuilder();
                sb.AppendLine("📜 **نبردهای اخیر**");
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
                foreach (var b in battles)
                    sb.AppendLine($"• {b.AttackerName} vs {b.DefenderName} – {b.Winner} – {b.SuccessPercent}% – {b.Timestamp}");
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
                var kb = new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("🔙 بازگشت", "adm:war:home") } });
                await RenderAdminScreen(callback, (sb.ToString(), kb), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return;
            }
            if (sub == "clearlocks")
            {
                if (!CanAdmin(userId, "W_EDIT")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return; }
                try
                {
                    using var con = Database.OpenConForAdmin();
                    using var cmd = con.CreateCommand();
                    cmd.CommandText = "DELETE FROM AttackAbandonLocks";
                    cmd.ExecuteNonQuery();
                    await AnswerAdminCallback(callback, "✅ تمام قفل‌های بزن‌دررو پاک شد.", false, ct);
                }
                catch (Exception ex) { await AnswerAdminCallback(callback, $"❌ خطا: {ex.Message}", true, ct); }
                return;
            }
        }

        // ================= OPERATIONS MODULE =================
        if (action == "ops")
        {
            if (!CanAdmin(userId, "O_VIEW")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return; }
            string sub = parts.Length >= 3 ? parts[2] : "home";
            if (sub == "home")
            {
                await RenderAdminScreen(callback, BuildAdminOperationsHome(), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return;
            }
            if (sub == "transfers")
            {
                var transfers = Database.GetActiveTransfers().Take(15).ToList();
                var sb = new StringBuilder();
                sb.AppendLine("📦 **ترنسفرهای فعال**");
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
                foreach (var t in transfers)
                    sb.AppendLine($"• {t.Id}: {t.SenderId}->{t.ReceiverId} {t.ResourceType} {t.Amount} {t.ModelName} – {t.ChatId}");
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
                var rows = new List<InlineKeyboardButton[]>();
                foreach (var t in transfers.Take(8))
                    rows.Add(new[] { InlineKeyboardButton.WithCallbackData($"❌ لغو {t.Id}", $"adm:ops:canceltransfer:{t.Id}") });
                rows.Add(new[] { InlineKeyboardButton.WithCallbackData("🔙 بازگشت", "adm:ops:home") });
                await RenderAdminScreen(callback, (sb.ToString(), new InlineKeyboardMarkup(rows)), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return;
            }
            if (sub == "deployments")
            {
                var deps = Database.GetActiveDeployments().Take(15).ToList();
                var sb = new StringBuilder();
                sb.AppendLine("⚔️ **صف‌آرایی‌های فعال**");
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
                foreach (var d in deps)
                    sb.AppendLine($"• {d.Id}: {d.Type} – {d.ChatId} – {d.Tanks}🛡 {d.Soldiers}🪖 – تا {FormatAdminTime(d.EndAtMs)}");
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
                var rows = new List<InlineKeyboardButton[]>();
                foreach (var d in deps.Take(8))
                    rows.Add(new[] { InlineKeyboardButton.WithCallbackData($"❌ لغو {d.Id}", $"adm:ops:canceldep:{d.Id}") });
                rows.Add(new[] { InlineKeyboardButton.WithCallbackData("🔙 بازگشت", "adm:ops:home") });
                await RenderAdminScreen(callback, (sb.ToString(), new InlineKeyboardMarkup(rows)), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return;
            }
            if (sub == "canceltransfer" && parts.Length >= 4 && TryParseLong(parts[3], out long tId))
            {
                if (!CanAdmin(userId, "O_EDIT")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return; }
                Database.DeleteTransfer(tId);
                Database.WriteAdminAudit(userId, "TRANSFER_CANCEL", "Transfer", tId.ToString(), "", true);
                await RenderAdminScreen(callback, BuildAdminOperationsHome(), ct);
                await AnswerAdminCallback(callback, $"✅ ترنسفر {tId} لغو شد.", false, ct);
                return;
            }
            if (sub == "canceldep" && parts.Length >= 4 && TryParseLong(parts[3], out long dId))
            {
                if (!CanAdmin(userId, "O_EDIT")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return; }
                var dep = Database.GetDeploymentById(dId);
                if (dep != null) await CancelDeploymentSafely(dep, ct);
                else Database.DeleteDeployment(dId);
                Database.WriteAdminAudit(userId, "DEPLOY_CANCEL", "Deployment", dId.ToString(), "", true);
                await RenderAdminScreen(callback, BuildAdminOperationsHome(), ct);
                await AnswerAdminCallback(callback, $"✅ صف‌آرایی {dId} لغو شد.", false, ct);
                return;
            }
            if (sub == "cleartransfers")
            {
                if (!CanAdmin(userId, "O_EDIT")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return; }
                var transfers = Database.GetActiveTransfers();
                foreach (var t in transfers) Database.DeleteTransfer(t.Id);
                await RenderAdminScreen(callback, BuildAdminOperationsHome(), ct);
                await AnswerAdminCallback(callback, $"✅ {transfers.Count} ترنسفر پاک شد.", false, ct);
                return;
            }
        }

        // ================= ANNOUNCE MODULE =================
        if (action == "ann")
        {
            if (!CanAdmin(userId, "ANN")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return; }
            string sub = parts.Length >= 3 ? parts[2] : "home";
            if (sub == "home")
            {
                await RenderAdminScreen(callback, BuildAdminAnnounceHome(), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return;
            }
            if (sub == "write")
            {
                adminInputRequests[userId] = new AdminInputRequest { Kind = "announce_text", ExpiresAtMs = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeMilliseconds() };
                await RenderAdminScreen(callback, ("📝 اعلامیه\n\nمتن اعلامیه را ارسال کنید (متن، عکس، فایل). برای لغو: لغو", new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("❌ لغو", "adm:cancelinput") } })), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return;
            }
            if (sub == "groups" || sub == "private" || sub == "all")
            {
                if (!adminInputRequests.TryGetValue(userId, out var annReq) || !annReq.Kind.StartsWith("announce")) { await AnswerAdminCallback(callback, "❌ ابتدا متن را وارد کنید.", true, ct); return; }
                string payload = annReq.Extra;
                string scopeText = sub switch { "groups" => "گروه‌ها", "private" => "پیوی‌ها", _ => "همه" };
                var allCountries = Database.GetAllCountries();
                var chatIds = allCountries.Select(c => c.ChatId).Distinct().ToList();
                var ownerIds = allCountries.Select(c => c.OwnerId).Distinct().ToList();
                int sent = 0;
                bool isPhoto = payload.Contains("|PHOTO|");
                bool isDoc = payload.Contains("|DOC|");
                string fileId = "";
                string caption = payload;
                if (isPhoto) { var parts2 = payload.Split("|PHOTO|"); fileId = parts2[0]; caption = parts2.Length > 1 ? parts2[1] : ""; }
                if (isDoc) { var parts2 = payload.Split("|DOC|"); fileId = parts2[0]; caption = parts2.Length > 1 ? parts2[1] : ""; }

                List<long> targets = sub switch { "groups" => chatIds, "private" => ownerIds, _ => chatIds.Concat(ownerIds).Distinct().ToList() };

                foreach (var tgt in targets)
                {
                    try
                    {
                        if (isPhoto && !string.IsNullOrEmpty(fileId))
                            await bot.SendPhotoAsync(tgt, fileId, caption: caption, cancellationToken: ct);
                        else if (isDoc && !string.IsNullOrEmpty(fileId))
                            await bot.SendDocumentAsync(tgt, new InputOnlineFile(fileId), caption: caption, cancellationToken: ct);
                        else
                            await bot.SendTextMessageAsync(tgt, caption, cancellationToken: ct);
                        sent++;
                        await Task.Delay(50, ct);
                    }
                    catch { }
                }
                adminInputRequests.TryRemove(userId, out _);
                Database.WriteAdminAudit(userId, "ANNOUNCE", "Announce", sub, $"sent={sent}", true);
                await RenderAdminScreen(callback, ($"✅ اعلامیه به {sent} مقصد ({scopeText}) ارسال شد.", new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("🏠 خانه", "adm:home") } })), ct);
                await AnswerAdminCallback(callback, $"✅ ارسال شد: {sent}", false, ct);
                return;
            }
            if (sub == "cancel")
            {
                adminInputRequests.TryRemove(userId, out _);
                await RenderAdminScreen(callback, BuildAdminAnnounceHome(), ct);
                await AnswerAdminCallback(callback, "❌ لغو شد.", false, ct);
                return;
            }
        }

        // ================= SETTINGS MODULE =================
        if (action == "settings")
        {
            if (!CanAdmin(userId, "SET")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return; }
            string sub = parts.Length >= 3 ? parts[2] : "home";
            if (sub == "home")
            {
                await RenderAdminScreen(callback, BuildAdminSettingsHome(), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return;
            }
            if (sub == "lbchannel")
            {
                if (parts.Length >= 4 && parts[3] == "clear")
                {
                    Database.SetSetting("LeaderboardChannelId", "0");
                    Database.WriteAdminAudit(userId, "SET_LB_CHANNEL_CLEAR", "Settings", "", "", true);
                    await RenderAdminScreen(callback, BuildAdminSettingsHome(), ct);
                    await AnswerAdminCallback(callback, "✅ کانال حذف شد.", false, ct);
                    return;
                }
                adminInputRequests[userId] = new AdminInputRequest { Kind = "set_leaderboard_channel", ExpiresAtMs = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds() };
                await RenderAdminScreen(callback, ("📢 تنظیم کانال لیدربورد\n\nآیدی عددی کانال (مثلاً -1001234567890) یا @username کانال را ارسال کنید، یا یک پیام از کانال فوروارد کنید.\nبرای حذف کانال 0 بنویسید.\nلغو: لغو", new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("❌ لغو", "adm:cancelinput") } })), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return;
            }
            if (sub == "attacklock")
            {
                adminInputRequests[userId] = new AdminInputRequest { Kind = "set_attack_lock", ExpiresAtMs = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds() };
                await RenderAdminScreen(callback, ($"⏰ قفل حمله فعلی: {ATTACK_LOCK_MINUTES} دقیقه\n\nمقدار جدید را وارد کنید (0-1440):\nلغو: لغو", new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("❌ لغو", "adm:cancelinput") } })), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return;
            }
            if (sub == "shield")
            {
                adminInputRequests[userId] = new AdminInputRequest { Kind = "set_shield_hours", ExpiresAtMs = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds() };
                await RenderAdminScreen(callback, ($"🛡 سپر فعلی: {SHIELD_HOURS} ساعت\n\nمقدار جدید:\nلغو: لغو", new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("❌ لغو", "adm:cancelinput") } })), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return;
            }
            if (sub == "maxattacks")
            {
                adminInputRequests[userId] = new AdminInputRequest { Kind = "set_max_attacks", ExpiresAtMs = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds() };
                await RenderAdminScreen(callback, ($"⚔️ سقف حمله فعلی: {MAX_ATTACKS_PER_UPDATE}\n\nمقدار جدید:\nلغو: لغو", new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("❌ لغو", "adm:cancelinput") } })), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return;
            }
            if (sub == "maxtransfers")
            {
                adminInputRequests[userId] = new AdminInputRequest { Kind = "set_max_transfers", ExpiresAtMs = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds() };
                await RenderAdminScreen(callback, ($"📦 سقف ترنسفر فعلی: {MAX_TRANSFERS_PER_UPDATE}\n\nمقدار جدید:\nلغو: لغو", new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("❌ لغو", "adm:cancelinput") } })), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return;
            }
        }

        // ================= LEADERBOARD ACTIONS =================
        if (action == "lb")
        {
            string sub = parts.Length >= 3 ? parts[2] : "";
            if (sub == "now")
            {
                if (!CanAdminAny(userId, "ANN", "SET")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return; }
                await AnswerAdminCallback(callback, "⏳ در حال ارسال لیدربورد...", false, ct);
                try { await SendNightlyLeaderboards(ct); await AnswerAdminCallback(callback, "✅ لیدربوردها ارسال شد.", false, ct); }
                catch (Exception ex) { await AnswerAdminCallback(callback, $"❌ خطا: {ex.Message}", true, ct); }
                return;
            }
            if (sub == "topplayers")
            {
                string txt = await BuildTopPlayersManpowerText(ct);
                await RenderAdminScreen(callback, (txt, new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("🔙 بازگشت", "adm:players:home"), InlineKeyboardButton.WithCallbackData("🏠 خانه", "adm:home") } })), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return;
            }
            if (sub == "topgroups" && parts.Length >= 4)
            {
                string type = parts[3];
                string txt = type == "count" ? await BuildTopGroupsByMembersText(ct) : await BuildTopGroupsByManpowerText(ct);
                await RenderAdminScreen(callback, (txt, new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("🔙 بازگشت", "adm:groups:home"), InlineKeyboardButton.WithCallbackData("🏠 خانه", "adm:home") } })), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return;
            }
        }

        // ================= BACKUP / MAINTENANCE =================
        if (action == "backup")
        {
            if (!CanAdminAny(userId, "BACKUP", "RESTORE")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return; }
            string sub = parts.Length >= 3 ? parts[2] : "";
            if (sub == "get")
            {
                string backupPath = $"gamedata_backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{userId}.db";
                try
                {
                    Database.CreateConsistentBackup(backupPath);
                    using var backupStream = System.IO.File.OpenRead(backupPath);
                    await bot.SendDocumentAsync(userId, new InputOnlineFile(backupStream, System.IO.Path.GetFileName(backupPath)), caption: "📦 بکاپ دیتابیس", cancellationToken: ct);
                    Database.WriteAdminAudit(userId, "BACKUP_GET", "Maintenance", "", "", true);
                    await AnswerAdminCallback(callback, "✅ بکاپ ارسال شد.", false, ct);
                }
                catch (Exception ex) { await AnswerAdminCallback(callback, $"❌ خطا: {ex.Message}", true, ct); }
                finally { TryDeleteSqliteSidecar(backupPath); }
                return;
            }
            if (sub == "upload")
            {
                adminInputRequests[userId] = new AdminInputRequest { Kind = "awaiting_db_file", ExpiresAtMs = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeMilliseconds() };
                await RenderAdminScreen(callback, ("📤 آپلود بکاپ\n\nفایل gamedata.db را ارسال کنید.\nلغو: لغو", new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("❌ لغو", "adm:cancelinput") } })), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return;
            }
        }

        if (action == "maintenance" && parts.Length >= 3)
        {
            string sub = parts[2];
            if (sub == "home")
            {
                if (!CanAdminAny(userId, "BACKUP", "RESTORE", "SET")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return; }
                await RenderAdminScreen(callback, BuildAdminMaintenanceHome(), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return;
            }
            if (sub == "cleanup")
            {
                if (!CanAdmin(userId, "SET")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return; }
                try
                {
                    using var con = Database.OpenConForAdmin();
                    using var cmd = con.CreateCommand();
                    cmd.CommandText = "DELETE FROM VisionMessageMap WHERE CreatedAtMs < @old; DELETE FROM VisionLogs WHERE CreatedAtMs < @old;";
                    cmd.Parameters.AddWithValue("@old", DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeMilliseconds());
                    cmd.ExecuteNonQuery();
                    await AnswerAdminCallback(callback, "✅ لاگ‌های قدیمی پاک شد.", false, ct);
                }
                catch (Exception ex) { await AnswerAdminCallback(callback, $"❌ {ex.Message}", true, ct); }
                return;
            }
        }

        if (action == "close")
        {
            adminInputRequests.TryRemove(
                userId,
                out _
            );

            await AnswerAdminCallback(
                callback,
                "پنل بسته شد.",
                false,
                ct
            );

            DeleteNow(
                callback.Message.Chat.Id,
                callback.Message.MessageId
            );

            await SendPermanent(
                userId,
                "✅ پنل مدیریت بسته شد.",
                markup: new ReplyKeyboardRemove(),
                ct: ct
            );
            return;
        }

        await AnswerAdminCallback(
            callback,
            "دستور مدیریتی ناشناخته.",
            true,
            ct
        );
    }

    static async Task RenderAdminScreen(
        CallbackQuery callback,
        (
            string Text,
            InlineKeyboardMarkup Keyboard
        ) screen,
        CancellationToken ct)
    {
        if (callback.Message == null)
            return;

        try
        {
            await bot.EditMessageTextAsync(
                callback.Message.Chat.Id,
                callback.Message.MessageId,
                screen.Text,
                replyMarkup: screen.Keyboard,
                cancellationToken: ct
            );
        }
        catch (Exception ex)
        {
            if (!ex.Message.Contains(
                    "message is not modified",
                    StringComparison.OrdinalIgnoreCase))
            {
                await SendPermanent(
                    callback.From.Id,
                    screen.Text,
                    markup: screen.Keyboard,
                    ct: ct
                );
            }
        }
    }

    static async Task AnswerAdminCallback(
        CallbackQuery callback,
        string? text,
        bool showAlert,
        CancellationToken ct)
    {
        try
        {
            await bot.AnswerCallbackQueryAsync(
                callback.Id,
                text,
                showAlert: showAlert,
                cancellationToken: ct
            );
        }
        catch
        {
        }
    }

    static async Task SendAdminDenied(
        long userId,
        CancellationToken ct)
    {
        await SendPermanent(
            userId,
            "⛔ شما دسترسی لازم برای این بخش را ندارید.",
            ct: ct
        );
    }

    static async Task SendAdminModulePending(
        long userId,
        string moduleTitle,
        string permission,
        CancellationToken ct)
    {
        if (!CanAdmin(userId, permission))
        {
            await SendAdminDenied(userId, ct);
            return;
        }

        await SendPermanent(
            userId,
            $"🧩 ماژول «{moduleTitle}» در فاز بعدی پنل فعال می‌شود.",
            ct: ct
        );
    }

    static string AdminModuleTitle(string module) =>
        module switch
        {
            "players" => "پلیرها",
            "countries" => "کشورها",
            "groups" => "گروه‌ها",
            "alliances" => "اتحادها",
            "economy" => "اقتصاد",
            "war" => "جنگ",
            "operations" => "عملیات",
            "announce" => "اعلامیه",
            "settings" => "تنظیمات",
            "maintenance" => "نگهداری",
            _ => module
        };

    static string FormatAdminTime(long unixMs)
    {
        if (unixMs <= 0)
            return "-";

        return DateTimeOffset
            .FromUnixTimeMilliseconds(unixMs)
            .UtcDateTime
            .AddHours(3.5)
            .ToString("yyyy-MM-dd HH:mm");
    }
}

// ===== FINAL MERGED MODULES =====

// ----- ACTIVITY MODULE -----
static partial class Database
{
    private const long GroupActivityWriteThrottleMs = 60_000;

    private static readonly ConcurrentDictionary<long, long>
        groupActivityLastWrite = new();

    public static void InitActivity()
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();

        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS PlayerActivity(
                UserId INTEGER PRIMARY KEY,
                LastActiveMs INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_PlayerActivity_LastActiveMs
                ON PlayerActivity(LastActiveMs);

            CREATE TABLE IF NOT EXISTS GroupActivity(
                ChatId INTEGER PRIMARY KEY,
                LastActiveMs INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_GroupActivity_LastActiveMs
                ON GroupActivity(LastActiveMs);

            CREATE INDEX IF NOT EXISTS IX_Countries_OwnerId
                ON Countries(OwnerId);
        ";

        cmd.ExecuteNonQuery();
    }

    public static void MarkPlayerActive(long userId)
    {
        if (userId == 0)
            return;

        try
        {
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            using var con = OpenCon();
            using var cmd = con.CreateCommand();

            cmd.CommandText = @"
                INSERT INTO PlayerActivity(UserId, LastActiveMs)
                VALUES($userId, $nowMs)
                ON CONFLICT(UserId) DO UPDATE SET
                    LastActiveMs = excluded.LastActiveMs
                WHERE PlayerActivity.LastActiveMs
                    < excluded.LastActiveMs - 60000;
            ";

            cmd.Parameters.AddWithValue("$userId", userId);
            cmd.Parameters.AddWithValue("$nowMs", nowMs);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PLAYER ACTIVITY ERR] {ex.Message}");
        }
    }

    public static void MarkGroupActive(long chatId)
    {
        if (chatId == 0)
            return;

        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (!ShouldWriteGroupActivity(chatId, nowMs))
            return;

        try
        {
            using var con = OpenCon();
            using var cmd = con.CreateCommand();

            cmd.CommandText = @"
                INSERT INTO GroupActivity(ChatId, LastActiveMs)
                VALUES($chatId, $nowMs)
                ON CONFLICT(ChatId) DO UPDATE SET
                    LastActiveMs = excluded.LastActiveMs;
            ";

            cmd.Parameters.AddWithValue("$chatId", chatId);
            cmd.Parameters.AddWithValue("$nowMs", nowMs);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GROUP ACTIVITY ERR] {ex.Message}");
        }
    }

    private static bool ShouldWriteGroupActivity(long chatId, long nowMs)
    {
        while (true)
        {
            if (!groupActivityLastWrite.TryGetValue(chatId, out long previous))
            {
                if (groupActivityLastWrite.TryAdd(chatId, nowMs))
                    return true;

                continue;
            }

            if (nowMs - previous < GroupActivityWriteThrottleMs)
                return false;

            if (groupActivityLastWrite.TryUpdate(chatId, nowMs, previous))
                return true;
        }
    }

    public static (
        int Players24h,
        int Groups24h,
        int Players7d,
        int Groups7d,
        int Players30d,
        int Groups30d
    ) GetActivityStats()
    {
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        long since24h = nowMs - 24L * 60L * 60L * 1000L;
        long since7d = nowMs - 7L * 24L * 60L * 60L * 1000L;
        long since30d = nowMs - 30L * 24L * 60L * 60L * 1000L;

        using var con = OpenCon();
        using var cmd = con.CreateCommand();

        cmd.CommandText = @"
            SELECT
                (
                    SELECT COUNT(*)
                    FROM PlayerActivity p
                    WHERE p.LastActiveMs >= $since24h
                      AND EXISTS(
                          SELECT 1
                          FROM Countries c
                          WHERE c.OwnerId = p.UserId
                      )
                ),
                (
                    SELECT COUNT(*)
                    FROM GroupActivity g
                    WHERE g.LastActiveMs >= $since24h
                ),
                (
                    SELECT COUNT(*)
                    FROM PlayerActivity p
                    WHERE p.LastActiveMs >= $since7d
                      AND EXISTS(
                          SELECT 1
                          FROM Countries c
                          WHERE c.OwnerId = p.UserId
                      )
                ),
                (
                    SELECT COUNT(*)
                    FROM GroupActivity g
                    WHERE g.LastActiveMs >= $since7d
                ),
                (
                    SELECT COUNT(*)
                    FROM PlayerActivity p
                    WHERE p.LastActiveMs >= $since30d
                      AND EXISTS(
                          SELECT 1
                          FROM Countries c
                          WHERE c.OwnerId = p.UserId
                      )
                ),
                (
                    SELECT COUNT(*)
                    FROM GroupActivity g
                    WHERE g.LastActiveMs >= $since30d
                );
        ";

        cmd.Parameters.AddWithValue("$since24h", since24h);
        cmd.Parameters.AddWithValue("$since7d", since7d);
        cmd.Parameters.AddWithValue("$since30d", since30d);

        using var reader = cmd.ExecuteReader();

        if (!reader.Read())
            return (0, 0, 0, 0, 0, 0);

        return (
            Convert.ToInt32(reader.GetInt64(0)),
            Convert.ToInt32(reader.GetInt64(1)),
            Convert.ToInt32(reader.GetInt64(2)),
            Convert.ToInt32(reader.GetInt64(3)),
            Convert.ToInt32(reader.GetInt64(4)),
            Convert.ToInt32(reader.GetInt64(5))
        );
    }
}

partial class Program
{
    static Timer? activityStatsTimer;

    static string BuildActivityStatsText()
    {
        var stats = Database.GetActivityStats();

        return
            "📊 آمار فعالیت\n\n" +

            "🕐 ۲۴ ساعت اخیر\n" +
            $"👤 پلیر فعال: {stats.Players24h}\n" +
            $"👥 گپ فعال: {stats.Groups24h}\n\n" +

            "📅 ۷ روز اخیر\n" +
            $"👤 پلیر فعال: {stats.Players7d}\n" +
            $"👥 گپ فعال: {stats.Groups7d}\n\n" +

            "🗓 ۳۰ روز اخیر\n" +
            $"👤 پلیر فعال: {stats.Players30d}\n" +
            $"👥 گپ فعال: {stats.Groups30d}";
    }

    static async Task SendActivityStats(
        long chatId,
        bool permanent,
        CancellationToken ct = default)
    {
        string text = BuildActivityStatsText();

        if (permanent)
        {
            await SendPermanent(chatId, text, ct: ct);
        }
        else
        {
            await SendTemp(chatId, text, ct: ct);
        }
    }

    static void StartActivityStatsTimer()
    {
        try
        {
            activityStatsTimer?.Dispose();
            activityStatsTimer = null;

            DateTime now = GetTehranNow();
            DateTime target = now.Date.AddHours(22);

            if (target <= now)
                target = target.AddDays(1);

            TimeSpan delay = target - now;

            activityStatsTimer = new Timer(async _ =>
            {
                try
                {
                    await SendActivityStats(
                        OWNER_ID,
                        permanent: true,
                        ct: CancellationToken.None
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"[ACTIVITY STATS SEND ERR] {ex.Message}"
                    );
                }
                finally
                {
                    try
                    {
                        StartActivityStatsTimer();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"[ACTIVITY STATS RESCHEDULE ERR] {ex.Message}"
                        );
                    }
                }
            }, null, delay, Timeout.InfiniteTimeSpan);

            Console.WriteLine(
                $"[ACTIVITY STATS TIMER] next report: " +
                $"{target:yyyy-MM-dd HH:mm} Tehran"
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[ACTIVITY STATS TIMER ERR] {ex.Message}"
            );

            activityStatsTimer?.Dispose();

            activityStatsTimer = new Timer(
                _ =>
                {
                    try
                    {
                        StartActivityStatsTimer();
                    }
                    catch
                    {
                    }
                },
                null,
                TimeSpan.FromMinutes(1),
                Timeout.InfiniteTimeSpan
            );
        }
    }

    // ================= LEADERBOARDS =================
    static Timer? leaderboardTimer;
    static readonly ConcurrentDictionary<long, string> groupTitleCache = new();

    static async Task<string> GetGroupTitleCached(long chatId, CancellationToken ct = default)
    {
        if (groupTitleCache.TryGetValue(chatId, out var cached) && !string.IsNullOrWhiteSpace(cached))
            return cached;

        try
        {
            var ch = await bot.GetChatAsync(chatId, ct);
            string title = ch.Title ?? ch.FirstName ?? $"گروه {chatId}";
            if (!string.IsNullOrWhiteSpace(title))
            {
                groupTitleCache[chatId] = title;
                return title;
            }
        }
        catch { }
        return $"گروه {chatId}";
    }

    static string FormatManpowerK(long mp)
    {
        if (mp >= 1000)
            return $"{mp / 1000.0:F1}K";
        return $"{mp:N0}";
    }

    static string MarkdownText(string? text) =>
        (text ?? "")
            .Replace("\\", "\\\\")
            .Replace("_", "\\_")
            .Replace("*", "\\*")
            .Replace("`", "\\`")
            .Replace("[", "\\[");

    static async Task<string> BuildTopPlayersManpowerText(CancellationToken ct = default)
    {
        var all = Database.GetAllCountries();
        var ranked = all.Select(c => new { Country = c, MP = CalcManpower(c) })
                        .OrderByDescending(x => x.MP)
                        .Take(10)
                        .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("🏆 **رده‌بندی برترین‌های مان‌پاور** 🏆");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━");

        for (int i = 0; i < ranked.Count; i++)
        {
            var item = ranked[i];
            string medal = i switch { 0 => "🥇", 1 => "🥈", 2 => "🥉", _ => $"{i + 1}." };
            string groupTitle = await GetGroupTitleCached(item.Country.ChatId, ct);

            sb.AppendLine($"{medal} **{MarkdownText(item.Country.Name)}**");
            sb.AppendLine($"👤 {MarkdownText(item.Country.OwnerName)}");
            sb.AppendLine($"⚡ {FormatManpowerK(item.MP)} مان‌پاور");
            sb.AppendLine($"🌍 {MarkdownText(groupTitle)}");
            sb.AppendLine();
        }

        if (ranked.Count == 0)
            sb.AppendLine("❌ هنوز کشوری ثبت نشده است.");

        sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
        return sb.ToString();
    }

    static async Task<string> BuildTopGroupsByMembersText(CancellationToken ct = default)
    {
        var all = Database.GetAllCountries();
        var groups = all.GroupBy(c => c.ChatId)
                        .Select(g => new { ChatId = g.Key, Count = g.Count() })
                        .OrderByDescending(x => x.Count)
                        .Take(10)
                        .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("👥 **رده‌بندی برترین‌های تعداد پلیر** 👥");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━");

        for (int i = 0; i < groups.Count; i++)
        {
            var g = groups[i];
            string medal = i switch { 0 => "🥇", 1 => "🥈", 2 => "🥉", _ => $"{i + 1}." };
            string title = await GetGroupTitleCached(g.ChatId, ct);
            sb.AppendLine($"{medal} **{MarkdownText(title)}**");
            sb.AppendLine($"👥 {g.Count} پلیر");
            sb.AppendLine();
        }

        if (groups.Count == 0)
            sb.AppendLine("❌ گروهی یافت نشد.");

        sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
        return sb.ToString();
    }

    static async Task<string> BuildTopGroupsByManpowerText(CancellationToken ct = default)
    {
        var all = Database.GetAllCountries();
        var groups = all.GroupBy(c => c.ChatId)
                        .Select(g => new
                        {
                            ChatId = g.Key,
                            TotalMP = g.Sum(c => CalcManpower(c))
                        })
                        .OrderByDescending(x => x.TotalMP)
                        .Take(10)
                        .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("⚡ **رده‌بندی برترین‌های مجموع مان‌پاور** ⚡");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━");

        for (int i = 0; i < groups.Count; i++)
        {
            var g = groups[i];
            string medal = i switch { 0 => "🥇", 1 => "🥈", 2 => "🥉", _ => $"{i + 1}." };
            string title = await GetGroupTitleCached(g.ChatId, ct);
            sb.AppendLine($"{medal} **{MarkdownText(title)}**");
            sb.AppendLine($"⚡ {FormatManpowerK(g.TotalMP)} مان‌پاور");
            sb.AppendLine();
        }

        if (groups.Count == 0)
            sb.AppendLine("❌ گروهی یافت نشد.");

        sb.AppendLine("━━━━━━━━━━━━━━━━━━━");
        return sb.ToString();
    }

    static async Task SendNightlyLeaderboards(CancellationToken ct = default)
    {
        try
        {
            string topPlayers = await BuildTopPlayersManpowerText(ct);
            string topGroupsMembers = await BuildTopGroupsByMembersText(ct);
            string topGroupsMP = await BuildTopGroupsByManpowerText(ct);

            // Send to owner private
            try { await SendPermanent(OWNER_ID, topPlayers, parseMode: ParseMode.Markdown, ct: ct); } catch { }
            try { await Task.Delay(500, ct); } catch { }
            try { await SendPermanent(OWNER_ID, topGroupsMembers, parseMode: ParseMode.Markdown, ct: ct); } catch { }
            try { await Task.Delay(500, ct); } catch { }
            try { await SendPermanent(OWNER_ID, topGroupsMP, parseMode: ParseMode.Markdown, ct: ct); } catch { }

            // Send to configured channel if exists
            long channelId = 0;
            string chStr = Database.GetSetting("LeaderboardChannelId");
            if (TryParseLong(chStr, out long parsed)) channelId = parsed;

            if (channelId != 0)
            {
                try { await bot.SendTextMessageAsync(channelId, topPlayers, parseMode: ParseMode.Markdown, cancellationToken: ct); } catch (Exception ex) { Console.WriteLine($"[LEADERBOARD CHANNEL ERR] {ex.Message}"); }
                try { await Task.Delay(500, ct); } catch { }
                try { await bot.SendTextMessageAsync(channelId, topGroupsMembers, parseMode: ParseMode.Markdown, cancellationToken: ct); } catch { }
                try { await Task.Delay(500, ct); } catch { }
                try { await bot.SendTextMessageAsync(channelId, topGroupsMP, parseMode: ParseMode.Markdown, cancellationToken: ct); } catch { }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LEADERBOARD SEND ERR] {ex.Message}");
        }
    }

    static void StartLeaderboardTimer()
    {
        try
        {
            leaderboardTimer?.Dispose();
            leaderboardTimer = null;

            DateTime now = GetTehranNow();
            // Every day at 22:00 Tehran, same time as activity stats, but +30 seconds to avoid spam collision
            DateTime target = now.Date.AddHours(22).AddSeconds(30);
            if (target <= now)
                target = target.AddDays(1);

            TimeSpan delay = target - now;

            leaderboardTimer = new Timer(async _ =>
            {
                try
                {
                    await SendNightlyLeaderboards(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LEADERBOARD TIMER ERR] {ex.Message}");
                }
                finally
                {
                    try { StartLeaderboardTimer(); } catch { }
                }
            }, null, delay, Timeout.InfiniteTimeSpan);

            Console.WriteLine($"[LEADERBOARD TIMER] next: {target:yyyy-MM-dd HH:mm:ss} Tehran");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LEADERBOARD TIMER SETUP ERR] {ex.Message}");
            leaderboardTimer?.Dispose();
            leaderboardTimer = new Timer(_ =>
            {
                try { StartLeaderboardTimer(); } catch { }
            }, null, TimeSpan.FromMinutes(1), Timeout.InfiniteTimeSpan);
        }
    }
}

// ----- ATTACK GUIDES -----
partial class Program
{
    static string GroundAttackStrategyName(int strategy) =>
        strategy == 1
            ? "هجوم منسجم"
            : "محاصره و ضربه";

    static string GroundAttackTacticName(int strategy, int tactic) =>
        (strategy, tactic) switch
        {
            (1, 1) => "حمله مستقیم به قلب خط دفاع",
            (1, 2) => "حملات سبک هدف‌دار و هجوم سنگین متمرکز",
            (2, 1) => "حلقه محاصره با حملات پراکنده و هجوم سریع",
            (2, 2) => "حلقه محاصره متحرک و ضربات سنگین",
            _ => "تاکتیک نامشخص"
        };

    static readonly string GroundAttackStrategyGuide = """
⚔️ انتخاب استراتژی حمله زمینی

1️⃣ هجوم منسجم
نیروهای مهاجم به‌شکل منظم و متمرکز وارد نبرد می‌شوند تا با ایجاد فشار مستقیم، خط دفاع دشمن را بشکنند.

2️⃣ محاصره و ضربه
نیروها برای محدود کردن تحرک و ارتباط دشمن، خطوط دفاعی را محاصره می‌کنند و سپس با ضربات هماهنگ آن‌ها را فرسوده و نابود می‌کنند.
""";

    static string GroundAttackTacticGuide(int strategy)
    {
        if (strategy == 1)
        {
            return """
⚔️ استراتژی: هجوم منسجم

1️⃣ حمله مستقیم به قلب خط دفاع
تمام سربازان و تانک‌ها در یک نقطه متمرکز می‌شوند و در قالب چند واحد منظم پیشروی می‌کنند. هدف، درگیری مستقیم و شکستن خطوط غیرمتمرکز دشمن با ضربات سنگین است.

2️⃣ حملات سبک هدف‌دار و هجوم سنگین متمرکز
نیروها تقسیم می‌شوند. گروه‌های سبک با حملات هدف‌دار نظم دشمن را برهم می‌زنند و نقاط ضعف را آشکار می‌کنند؛ سپس ارتش اصلی به‌صورت متمرکز هجوم می‌برد و خط دفاع را می‌شکند.
""";
        }

        return """
⚔️ استراتژی: محاصره و ضربه

1️⃣ حلقه محاصره با حملات پراکنده و هجوم سریع
خطوط دفاعی دشمن کاملاً محاصره می‌شوند تا قدرت تحرک آن‌ها کاهش یابد. حملات پراکنده دشمن را فرسوده می‌کند و در پایان، هجوم سریع خطوط نامنظم را درهم می‌شکند.

2️⃣ حلقه محاصره متحرک و ضربات سنگین
دشمن در حلقه‌ای بزرگ گرفتار و ارتباطش با بیرون قطع می‌شود. ارتش از تمام جهات، آهسته اما هماهنگ پیشروی می‌کند و با ضربات سنگین گروه‌های کوچک را حذف و نیروهای باقی‌مانده را متراکم و بی‌حرکت می‌کند.
""";
    }

    static string AirAttackStrategyName(int strategy) =>
        strategy == 1
            ? "برتری هوایی"
            : "بمباران راهبردی";

    static string AirAttackTacticName(int strategy, int tactic) =>
        (strategy, tactic) switch
        {
            (1, 1) => "شکار آزاد (Freie Jagd)",
            (1, 2) => "حمله به پایگاه‌ها (Counter-air Strike)",
            (2, 1) => "بمباران دقیق (Precision Bombing)",
            (2, 2) => "بمباران منطقه‌ای (Area Bombing)",
            _ => "تاکتیک نامشخص"
        };

    static readonly string AirAttackStrategyGuide = """
🛫 انتخاب استراتژی حمله هوایی

1️⃣ برتری هوایی (Air Superiority)
هدف، از بین بردن توان هوایی دشمن و به‌دست گرفتن کنترل آسمان است.

2️⃣ بمباران راهبردی (Strategic Bombing)
هدف، تضعیف توان اقتصادی، صنعتی و روحیه دشمن با حمله به اهداف مهم در عمق قلمرو اوست.
""";

    static string AirAttackTacticGuide(int strategy)
    {
        if (strategy == 1)
        {
            return """
🛫 استراتژی: برتری هوایی

1️⃣ شکار آزاد (Freie Jagd)
جنگنده‌ها به‌صورت مستقل یا در گروه‌های کوچک به پشت خطوط دشمن نفوذ می‌کنند و هواپیماهای در حال پرواز، شامل جنگنده‌ها، بمب‌افکن‌ها و هواپیماهای شناسایی را هدف می‌گیرند.

2️⃣ حمله به پایگاه‌ها (Counter-air Strike)
فرودگاه‌ها، آشیانه‌ها، برج‌های مراقبت و انبارهای سوخت دشمن به‌شکل غافلگیرانه بمباران می‌شوند تا هواپیماهای دشمن پیش از برخاستن، روی زمین منهدم شوند.
""";
        }

        return """
🛫 استراتژی: بمباران راهبردی

1️⃣ بمباران دقیق (Precision Bombing)
اهداف کوچک و حیاتی مانند کارخانه‌های تسلیحات، پالایشگاه‌ها و ایستگاه‌های راه‌آهن انتخاب می‌شوند و از ارتفاع متوسط، با تمرکز بالا بمباران می‌شوند.

2️⃣ بمباران منطقه‌ای (Area Bombing)
گروه بزرگی از بمب‌افکن‌ها یک منطقه وسیع، مانند شهر یا منطقه صنعتی، را هدف می‌گیرند تا زیرساخت‌ها به‌طور گسترده تخریب و روحیه دشمن تضعیف شود.
""";
    }
}

// ----- DEFENSE GUIDES -----
partial class Program
{
    static string GroundDefenseStrategyName(int strategy) =>
        strategy == 1
            ? "دفاع منسجم"
            : "دفاع و ضدحمله پراکنده";

    static string GroundDefenseTacticName(int strategy, int tactic) =>
        (strategy, tactic) switch
        {
            (1, 1) => "دفاع ایستا و ثابت با قوای زرهی",
            (1, 2) => "گشت متحرک با گروه‌های ترکیبی",
            (2, 1) => "استتار و ضربه به گروه‌های پیشرو",
            (2, 2) => "عقب‌نشینی تاکتیکی و تله‌گذاری مخفی",
            _ => "تاکتیک نامشخص"
        };

    static readonly string GroundDefenseStrategyGuide = """
🛡 انتخاب استراتژی دفاع زمینی

1️⃣ دفاع منسجم
نیروهای مدافع در یک ساختار هماهنگ و نسبتاً ثابت مستقر می‌شوند تا خط دفاعی قدرتمندی ایجاد کنند و مانع نفوذ مستقیم دشمن شوند.

2️⃣ دفاع و ضدحمله پراکنده
نیروها با استتار، پراکندگی و عقب‌نشینی حساب‌شده، مهاجم را به عمق منطقه می‌کشانند و سپس با ضدحمله و محاصره به او ضربه می‌زنند.
""";

    static string GroundDefenseTacticGuide(int strategy)
    {
        if (strategy == 1)
        {
            return """
🛡 استراتژی: دفاع منسجم

1️⃣ دفاع ایستا و ثابت با قوای زرهی
سربازان در سنگرها و پشت موانع طبیعی مستقر می‌شوند و تانک‌ها در خط اول قرار می‌گیرند تا با آتش مستقیم، حرکت مهاجم را متوقف کنند.

2️⃣ گشت متحرک با گروه‌های ترکیبی
گروه‌های کوچک ترکیبی، متشکل از تانک و سرباز، به‌طور مداوم در خط مقدم حرکت می‌کنند تا نیروهای پراکنده مهاجم را شناسایی و هدف قرار دهند.
""";
        }

        return """
🛡 استراتژی: دفاع و ضدحمله پراکنده

1️⃣ استتار و ضربه به گروه‌های پیشرو
سربازان در بوته‌زارها، خرابه‌ها یا پشت تپه‌ها مخفی می‌شوند و تانک‌ها در سنگرهای پنهان و ثابت قرار می‌گیرند تا نیروی پیشرو دشمن غافلگیر شود و ضربه سنگینی دریافت کند.

2️⃣ عقب‌نشینی تاکتیکی و تله‌گذاری مخفی
بخشی از خطوط دفاعی عمداً خالی گذاشته می‌شود تا دشمن وارد عمق منطقه شود. سپس مسیرهای ارتباطی او مسدود و واحدهای مهاجم در محاصره و تله‌های مختلف گرفتار می‌شوند.
""";
    }

    static string AirDefenseStrategyName(int strategy) =>
        strategy == 1
            ? "دفاع منطقه‌ای (Area Defense)"
            : "دفاع نقطه‌ای (Point Defense)";

    static string AirDefenseTacticName(int strategy, int tactic) =>
        (strategy, tactic) switch
        {
            (1, 1) => "گشت هوایی رزمی (CAP)",
            (1, 2) => "ایستگاه‌های شنود و هشدار سریع",
            (2, 1) => "آتشبند (Flak Barrage)",
            (2, 2) => "پوشش مستقیم جنگنده (Close Escort)",
            _ => "تاکتیک نامشخص"
        };

    static readonly string AirDefenseStrategyGuide = """
🛫 انتخاب استراتژی دفاع هوایی

1️⃣ دفاع منطقه‌ای (Area Defense)
هدف، حفاظت از یک منطقه وسیع مانند کشور یا جبهه بزرگ با پراکندگی نیروها و رهگیری تهدیدها پیش از رسیدن به اهداف حساس است.

2️⃣ دفاع نقطه‌ای (Point Defense)
تمرکز نیروهای دفاعی بر حفاظت از اهداف حیاتی و محدود مانند شهرها، کارخانه‌ها، پایگاه‌ها و تأسیسات مهم است.
""";

    static string AirDefenseTacticGuide(int strategy)
    {
        if (strategy == 1)
        {
            return """
🛫 استراتژی: دفاع منطقه‌ای

1️⃣ گشت هوایی رزمی (CAP)
جنگنده‌ها به‌طور مداوم در آسمان منطقه گشت می‌زنند تا هواپیماهای دشمن را پیش از رسیدن به اهداف حساس شناسایی و رهگیری کنند.

2️⃣ ایستگاه‌های شنود و هشدار سریع 🔒
رادارهای زمینی و تجهیزات شنود، حرکت دشمن را کشف می‌کنند و جنگنده‌ها را به سمت تهدید هدایت می‌کنند.

این تاکتیک در وضعیت فعلی آلیس به رادار نیاز دارد و قفل است.
""";
        }

        return """
🛫 استراتژی: دفاع نقطه‌ای

1️⃣ آتشبند (Flak Barrage)
توپ‌های ضدهوایی به‌صورت متراکم در اطراف هدف مستقر می‌شوند و با آتش متوالی و سنگین، مسیر پرواز هواپیماهای دشمن را مسدود می‌کنند.

2️⃣ پوشش مستقیم جنگنده (Close Escort)
جنگنده‌های دفاعی در مجاورت هدف حیاتی، مانند کارخانه یا پایگاه، گشت می‌زنند و در لحظه حمله مستقیماً وارد درگیری می‌شوند.
""";
    }
}
