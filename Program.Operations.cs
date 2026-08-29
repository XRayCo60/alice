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
{    static void StartAssetUpdateTimer()
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
}
