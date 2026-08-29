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

partial class Program
{    static async Task HandleRevengeCallback(CallbackQuery cb,string[] parts,CancellationToken ct)
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
}
