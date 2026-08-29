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


// ============================================================
//  Battle orchestration — 1939 ground and air engine
// ============================================================

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


// ===== MERGED ADMIN PANEL =====
sealed class AdminInputRequest
{
    public string Kind { get; set; } = "";
    public long ExpiresAtMs { get; set; }
    public long TargetId { get; set; } = 0;
    public long ChatId { get; set; } = 0;
    public string Extra { get; set; } = "";
}


// ===== FINAL MERGED MODULES =====

// ----- ACTIVITY MODULE -----


// ----- ATTACK GUIDES -----

// ----- DEFENSE GUIDES -----
