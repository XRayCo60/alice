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
{
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
            if (await TryHandlePrivateTransferDefenseSession(uid, txt, sess, ct) ||
                await TryHandlePrivateDeploymentSession(uid, txt, sess, ct) ||
                await TryHandlePrivateDeploymentJoinSession(uid, txt, sess, ct) ||
                await TryHandlePrivateAttackSession(uid, txt, sess, ct))
                return;
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
    static async Task<bool> TryHandlePrivateTransferDefenseSession(long uid,string txt,UserSession sess,CancellationToken ct)
    {
            if (sess.Step == SessionStep.TransferWaitingAmount)
            {
                // Single-model transfer (or fallback)
                if (!TryParseLong(txt, out long amount) || amount < 0) { await SendPrompt(uid, uid, "❌ عدد را به صورت عدد مثبت وارد کنید (0 برای لغو):", ct: ct); return true; }
                if (amount == 0) { EndSession(uid); await SendTemp(uid, "✅ انتقال لغو شد.", ct: ct); return true; }

                var c = Database.GetCountry(uid, sess.TransferChatId);
                if (c == null) { EndSession(uid); return true; }
                long myAid = Database.GetUserAllianceId(sess.TransferChatId, uid);
                if (myAid == 0) { EndSession(uid); await SendTemp(uid, "❌ شما دیگر عضو اتحاد نیستید.", ct: ct); return true; }
                long tgtAid = Database.GetUserAllianceId(sess.TransferChatId, sess.TransferTargetId);
                if (tgtAid != myAid) { EndSession(uid); await SendTemp(uid, "❌ گیرنده هم‌اتحاد شما نیست.", ct: ct); return true; }
                var recv = Database.GetCountry(sess.TransferTargetId, sess.TransferChatId);
                if (recv == null) { EndSession(uid); await SendTemp(uid, "❌ گیرنده کشوری ندارد.", ct: ct); return true; }
                if (GetTransferCount(sess.TransferChatId, uid) >= MAX_TRANSFERS_PER_UPDATE && !Database.HasGroupLockExemption(sess.TransferChatId))
                { EndSession(uid); await SendTemp(uid, $"⛔ سهمیه تمام شد ({MAX_TRANSFERS_PER_UPDATE}).", ct: ct); return true; }

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
                    return true;
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
                    return true;
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
                        return true;
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
                    return true;
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
                return true;
            }

            if (sess.Step == SessionStep.TransferWaitingModelAmount)
            {
                // Per-model amount entry
                if (!TryParseLong(txt, out long amount) || amount < 0) { await SendPrompt(uid, uid, "❌ عدد نامعتبر. لطفاً عدد مثبت (یا 0 برای رد شدن) وارد کنید:", ct: ct); return true; }

                var c = Database.GetCountry(uid, sess.TransferChatId);
                if (c == null) { EndSession(uid); return true; }

                int idx = sess.TransferModelIndex;
                if (idx < 0 || idx >= sess.TransferModelNames.Count)
                {
                    EndSession(uid);
                    await SendTemp(uid, "❌ خطای داخلی در انتقال. دوباره تلاش کنید.", ct: ct);
                    return true;
                }

                long availModel = sess.TransferModelCounts[idx];
                if (amount > availModel)
                {
                    await SendPrompt(uid, uid, $"❌ موجودی این مدل کافی نیست.\n📦 مدل: {sess.TransferModelNames[idx]}\n📊 موجودی: {availModel:N0}\nدوباره وارد کنید:", ct: ct);
                    return true;
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
                    return true;
                }

                // All models entered – finalize
                long totalAmount = sess.TransferModelAmounts.Sum();
                if (totalAmount <= 0)
                {
                    EndSession(uid);
                    await SendTemp(uid, "✅ انتقال لغو شد (مقداری انتخاب نشد).", ct: ct);
                    return true;
                }
                long currentResourceTotal = GetCountryResourceAmount(c, sess.TransferResourceType);
                if (totalAmount > currentResourceTotal)
                {
                    EndSession(uid);
                    await SendTemp(uid,
                        $"❌ موجودی در طول عملیات تغییر کرده است. انتقال ثبت نشد.\n📊 موجودی فعلی: {currentResourceTotal:N0}",
                        ct: ct);
                    return true;
                }

                long myAid = Database.GetUserAllianceId(sess.TransferChatId, uid);
                if (myAid == 0) { EndSession(uid); await SendTemp(uid, "❌ شما دیگر عضو اتحاد نیستید.", ct: ct); return true; }
                long tgtAid = Database.GetUserAllianceId(sess.TransferChatId, sess.TransferTargetId);
                if (tgtAid != myAid) { EndSession(uid); await SendTemp(uid, "❌ گیرنده هم‌اتحاد شما نیست.", ct: ct); return true; }
                var recv = Database.GetCountry(sess.TransferTargetId, sess.TransferChatId);
                if (recv == null) { EndSession(uid); await SendTemp(uid, "❌ گیرنده کشوری ندارد.", ct: ct); return true; }
                if (GetTransferCount(sess.TransferChatId, uid) >= MAX_TRANSFERS_PER_UPDATE && !Database.HasGroupLockExemption(sess.TransferChatId))
                { EndSession(uid); await SendTemp(uid, $"⛔ سهمیه تمام شد ({MAX_TRANSFERS_PER_UPDATE}).", ct: ct); return true; }

                // Battleship cap check for multi-model
                if (sess.TransferResourceType == "battleships")
                {
                    var recvCheck2 = Database.GetCountry(sess.TransferTargetId, sess.TransferChatId);
                    long usedCapacity = recvCheck2 == null ? 3 : Database.GetBattleshipCapacityUsed(recvCheck2.OwnerId, recvCheck2.ChatId);
                    if (recvCheck2 != null && usedCapacity + totalAmount > 3)
                    {
                        EndSession(uid);
                        await SendTemp(uid, $"❌ ظرفیت نبردناو گیرنده کافی نیست: {usedCapacity}/3 (ناوهای در دریا و محموله‌های در راه هم حساب می‌شوند).", ct: ct);
                        return true;
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
                    return true;
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
                return true;
            }

            if (sess.Step is SessionStep.NavalDefenseWaitingBoatModel or SessionStep.NavalDefenseWaitingSubmarineModel or SessionStep.NavalDefenseWaitingBattleshipModel)
            {
                if(!TryParseLong(txt,out long amount)||amount<0){await SendPrompt(uid,uid,"❌ تعداد معتبر وارد کنید.",ct:ct);return true;}
                int index=sess.DefenseModelIndex;if(index<0||index>=sess.DefenseModelCounts.Count){EndSession(uid);return true;}
                long minimum=sess.DefenseModelMinimums[index],available=sess.DefenseModelCounts[index];
                if(amount<minimum||amount>available){await SendPrompt(uid,uid,$"❌ مقدار مجاز بین {minimum:N0} تا {available:N0} است.",ct:ct);return true;}
                sess.DefenseModelAmounts[index]=amount;sess.DefenseModelIndex++;
                if(sess.DefenseModelIndex<sess.DefenseModelNames.Count)
                {
                    int next=sess.DefenseModelIndex;await SendPrompt(uid,uid,$"⚓ مدل {next+1}/{sess.DefenseModelNames.Count}: {sess.DefenseModelNames[next]}\n📊 موجودی: {sess.DefenseModelCounts[next]:N0}\n🔒 حداقل: {sess.DefenseModelMinimums[next]:N0}\nتعداد دفاع:",ct:ct);return true;
                }
                string category=sess.DefenseCurrentCategory=="boats"?"Boats":sess.DefenseCurrentCategory=="submarines"?"Submarines":"Battleships";
                var map=Enumerable.Range(0,sess.DefenseModelNames.Count).Where(i=>sess.DefenseModelAmounts[i]>0)
                    .ToDictionary(i=>sess.DefenseModelNames[i],i=>sess.DefenseModelAmounts[i],StringComparer.OrdinalIgnoreCase);
                Database.ReplaceNavalDefenseModels(uid,sess.AttackChatId,category,map);
                var country=Database.GetCountry(uid,sess.AttackChatId);if(country==null){EndSession(uid);return true;}
                if(sess.DefenseCurrentCategory=="boats"){await BeginNavalDefenseCategory(uid,sess,country,"submarines",ct);return true;}
                if(sess.DefenseCurrentCategory=="submarines"){await BeginNavalDefenseCategory(uid,sess,country,"battleships",ct);return true;}
                long chat=sess.AttackChatId;EndSession(uid);await SendTemp(uid,"✅ آرایش مدل‌به‌مدل دفاع دریایی ذخیره شد.",ct:ct);await SendDefenseStatus(uid,uid,chat,ct);return true;
            }

            if (sess.Step == SessionStep.DefenseWaitingTankModel)
            {
                if (!TryParseLong(txt, out long amount) || amount < 0)
                { await SendPrompt(uid, uid, "❌ یک تعداد معتبر وارد کنید.", ct: ct); return true; }
                int index = sess.DefenseModelIndex;
                if (index < 0 || index >= sess.DefenseModelCounts.Count) { EndSession(uid); return true; }
                long minimum = sess.DefenseModelMinimums[index];
                long available = sess.DefenseModelCounts[index];
                if (amount < minimum || amount > available)
                {
                    await SendPrompt(uid, uid,
                        $"❌ مقدار مجاز برای {sess.DefenseModelNames[index]} بین {minimum:N0} تا {available:N0} است.", ct: ct);
                    return true;
                }
                sess.DefenseModelAmounts[index] = amount;
                sess.DefenseModelIndex++;
                if (sess.DefenseModelIndex < sess.DefenseModelNames.Count)
                {
                    int next = sess.DefenseModelIndex;
                    await SendPrompt(uid, uid,
                        $"🛡 دفاع تانک – مدل {next + 1}/{sess.DefenseModelNames.Count}\n\n🔧 مدل: {sess.DefenseModelNames[next]}\n📊 موجودی: {sess.DefenseModelCounts[next]:N0}\n🛡 مقدار فعلی دفاع: {sess.DefenseModelAmounts[next]:N0}\n🔒 حداقل اجباری: {sess.DefenseModelMinimums[next]:N0}\n\nتعداد دقیق را وارد کنید:", ct: ct);
                    return true;
                }
                sess.DefenseTankModelNamesFinal = new List<string>(sess.DefenseModelNames);
                sess.DefenseTankModelAmountsFinal = new List<long>(sess.DefenseModelAmounts);
                sess.DefenseTanks = sess.DefenseModelAmounts.Sum();
                sess.DefTankPct = 100;
                sess.Step = SessionStep.DefenseWaitingSoldiers;
                await SendPrompt(uid, uid, $"🪖 درصد دفاع سرباز:\nکل: {Database.GetCountry(uid, sess.AttackChatId)?.Soldiers ?? 0:N0}",
                    BuildPercentKeyboard("soldier", sess.AttackChatId), ct);
                return true;
            }

            if (sess.Step == SessionStep.DefenseWaitingPlaneModel)
            {
                if (!TryParseLong(txt, out long amount) || amount < 0)
                { await SendPrompt(uid, uid, "❌ یک تعداد معتبر وارد کنید.", ct: ct); return true; }
                int index = sess.DefenseModelIndex;
                if (index < 0 || index >= sess.DefenseModelCounts.Count) { EndSession(uid); return true; }
                long minimum = sess.DefenseModelMinimums[index];
                if (amount < minimum || amount > sess.DefenseModelCounts[index])
                {
                    await SendPrompt(uid, uid,
                        $"❌ مقدار مجاز برای {sess.DefenseModelNames[index]} بین {minimum:N0} تا {sess.DefenseModelCounts[index]:N0} است.", ct: ct);
                    return true;
                }
                sess.DefenseModelAmounts[index] = amount;
                sess.DefenseModelIndex++;
                if (sess.DefenseModelIndex < sess.DefenseModelNames.Count)
                {
                    int next = sess.DefenseModelIndex;
                    await SendPrompt(uid, uid,
                        $"✈️ دفاع جنگنده – مدل {next + 1}/{sess.DefenseModelNames.Count}\n\n🔧 مدل: {sess.DefenseModelNames[next]}\n📊 موجودی: {sess.DefenseModelCounts[next]:N0}\n🛡 مقدار فعلی دفاع: {sess.DefenseModelAmounts[next]:N0}\n🔒 حداقل اجباری: {sess.DefenseModelMinimums[next]:N0}\n\nتعداد دقیق را وارد کنید:", ct: ct);
                    return true;
                }
                var country = Database.GetCountry(uid, sess.AttackChatId);
                if (country == null) { EndSession(uid); return true; }
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
                    return true;
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
                return true;
            }
        return false;
    }

    static async Task<bool> TryHandlePrivateDeploymentSession(long uid,string txt,UserSession sess,CancellationToken ct)
    {
            if (sess.Step == SessionStep.DeployWaitingTanks)
            {
                // Legacy total tanks – now redirect to per-model
                var c = Database.GetCountry(uid, sess.DeployChatId);
                if (c == null) { EndSession(uid); return true; }
                var breakdown = GetTransferBreakdown(c, "tanks");
                if (breakdown.Count == 0)
                {
                    sess.DeployTanks = 0;
                    sess.Step = SessionStep.DeployWaitingSoldiers;
                    await SendPrompt(uid, uid, $"🪖 سرباز:\nموجود: {c.Soldiers:N0}", ct: ct);
                    return true;
                }
                sess.DeployModelNames = breakdown.Select(x => x.ModelName).ToList();
                sess.DeployModelCounts = breakdown.Select(x => x.Count).ToList();
                sess.DeployModelAmounts = new List<long>(new long[breakdown.Count]);
                sess.DeployModelIndex = 0;
                sess.DeployCurrentCategory = "tanks";
                sess.Step = SessionStep.DeployWaitingTankModel;
                await SendPrompt(uid, uid, $"🛡 صف آرایی – تانک مدل 1/{breakdown.Count}: {breakdown[0].ModelName} – موجودی {breakdown[0].Count:N0}\nچند تا اعزام شود؟ (0 برای رد)", ct: ct);
                return true;
            }
            if (sess.Step == SessionStep.DeployWaitingTankModel)
            {
                if (!TryParseLong(txt, out long amt) || amt < 0) { await SendPrompt(uid, uid, "❌ عدد معتبر.", ct: ct); return true; }
                int idx = sess.DeployModelIndex;
                if (idx < 0 || idx >= sess.DeployModelCounts.Count) { EndSession(uid); return true; }
                if (amt > sess.DeployModelCounts[idx]) { await SendPrompt(uid, uid, $"❌ موجودی این مدل: {sess.DeployModelCounts[idx]:N0}", ct: ct); return true; }
                sess.DeployModelAmounts[idx] = amt;
                sess.DeployModelIndex++;
                if (sess.DeployModelIndex < sess.DeployModelNames.Count)
                {
                    await SendPrompt(uid, uid, $"🛡 مدل {sess.DeployModelIndex + 1}/{sess.DeployModelNames.Count}: {sess.DeployModelNames[sess.DeployModelIndex]} – موجودی {sess.DeployModelCounts[sess.DeployModelIndex]:N0}\nچند تا؟ (0 برای رد)", ct: ct);
                    return true;
                }
                sess.DeployTanks = sess.DeployModelAmounts.Sum();
                sess.DeployTankModelNamesFinal = new List<string>(sess.DeployModelNames);
                sess.DeployTankModelAmountsFinal = new List<long>(sess.DeployModelAmounts);
                sess.Step = SessionStep.DeployWaitingSoldiers;
                var c = Database.GetCountry(uid, sess.DeployChatId);
                await SendPrompt(uid, uid, $"🪖 سرباز:\nموجود: {c?.Soldiers ?? 0:N0}", ct: ct);
                return true;
            }
            if (sess.Step == SessionStep.DeployWaitingSoldiers)
            {
                if (!TryParseLong(txt, out long sol) || sol < 0) { await SendPrompt(uid, uid, "❌ عدد معتبر.", ct: ct); return true; }
                var c = Database.GetCountry(uid, sess.DeployChatId);
                if (c == null) { EndSession(uid); return true; }
                if (sol > c.Soldiers) { await SendPrompt(uid, uid, $"❌ موجودی: {c.Soldiers}", ct: ct); return true; }
                sess.DeploySoldiers = sol;
                // Per-model planes
                var planeBreakdown = GetTransferBreakdown(c, "planes");
                if (planeBreakdown.Count == 0)
                {
                    sess.DeployFighters = 0;
                    sess.Step = SessionStep.DeployWaitingBombers;
                    await SendPrompt(uid, uid, $"🛩 بمب‌افکن:\nموجود: {c.Bombers:N0}", ct: ct);
                    return true;
                }
                sess.DeployModelNames = planeBreakdown.Select(x => x.ModelName).ToList();
                sess.DeployModelCounts = planeBreakdown.Select(x => x.Count).ToList();
                sess.DeployModelAmounts = new List<long>(new long[planeBreakdown.Count]);
                sess.DeployModelIndex = 0;
                sess.DeployCurrentCategory = "planes";
                sess.Step = SessionStep.DeployWaitingPlaneModel;
                await SendPrompt(uid, uid, $"✈️ صف آرایی – جنگنده مدل 1/{planeBreakdown.Count}: {planeBreakdown[0].ModelName} – موجودی {planeBreakdown[0].Count:N0}\nچند تا اعزام شود؟ (0 برای رد)", ct: ct);
                return true;
            }
            if (sess.Step == SessionStep.DeployWaitingPlaneModel)
            {
                if (!TryParseLong(txt, out long amt) || amt < 0) { await SendPrompt(uid, uid, "❌ عدد معتبر.", ct: ct); return true; }
                int idx = sess.DeployModelIndex;
                if (idx < 0 || idx >= sess.DeployModelCounts.Count) { EndSession(uid); return true; }
                if (amt > sess.DeployModelCounts[idx]) { await SendPrompt(uid, uid, $"❌ موجودی این مدل: {sess.DeployModelCounts[idx]:N0}", ct: ct); return true; }
                sess.DeployModelAmounts[idx] = amt;
                sess.DeployModelIndex++;
                if (sess.DeployModelIndex < sess.DeployModelNames.Count)
                {
                    await SendPrompt(uid, uid, $"✈️ مدل {sess.DeployModelIndex + 1}/{sess.DeployModelNames.Count}: {sess.DeployModelNames[sess.DeployModelIndex]} – موجودی {sess.DeployModelCounts[sess.DeployModelIndex]:N0}\nچند تا؟ (0 برای رد)", ct: ct);
                    return true;
                }
                sess.DeployFighters = sess.DeployModelAmounts.Sum();
                sess.DeployPlaneModelNamesFinal = new List<string>(sess.DeployModelNames);
                sess.DeployPlaneModelAmountsFinal = new List<long>(sess.DeployModelAmounts);
                sess.Step = SessionStep.DeployWaitingBombers;
                var c = Database.GetCountry(uid, sess.DeployChatId);
                await SendPrompt(uid, uid, $"🛩 بمب‌افکن:\nموجود: {c?.Bombers ?? 0:N0}", ct: ct);
                return true;
            }
            if (sess.Step == SessionStep.DeployWaitingFighters)
            {
                // Legacy – redirect to per-model
                var c = Database.GetCountry(uid, sess.DeployChatId);
                if (c == null) { EndSession(uid); return true; }
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
                    return true;
                }
                if (!TryParseLong(txt, out long fig) || fig < 0) { await SendPrompt(uid, uid, "❌ عدد معتبر.", ct: ct); return true; }
                if (fig > c.Planes) { await SendPrompt(uid, uid, $"❌ موجودی: {c.Planes}", ct: ct); return true; }
                sess.DeployFighters = fig;
                sess.Step = SessionStep.DeployWaitingBombers;
                await SendPrompt(uid, uid, $"🛩 بمب‌افکن:\nموجود: {c.Bombers:N0}", ct: ct);
                return true;
            }
            if (sess.Step == SessionStep.DeployWaitingBombers)
            {
                var c = Database.GetCountry(uid, sess.DeployChatId);
                if (c == null) { EndSession(uid); return true; }
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
                    return true;
                }
                if (!TryParseLong(txt, out long bom) || bom < 0) { await SendPrompt(uid, uid, "❌ عدد معتبر.", ct: ct); return true; }
                if (bom > c.Bombers) { await SendPrompt(uid, uid, $"❌ موجودی: {c.Bombers}", ct: ct); return true; }
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
                    return true;
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
                    return true;
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
                return true;
            }

            if (sess.Step == SessionStep.DeployWaitingBomberModel)
            {
                if (!TryParseLong(txt, out long amt) || amt < 0) { await SendPrompt(uid, uid, "❌ عدد معتبر.", ct: ct); return true; }
                int idx = sess.DeployModelIndex;
                if (idx < 0 || idx >= sess.DeployModelCounts.Count) { EndSession(uid); return true; }
                if (amt > sess.DeployModelCounts[idx]) { await SendPrompt(uid, uid, $"❌ موجودی این مدل: {sess.DeployModelCounts[idx]:N0}", ct: ct); return true; }
                sess.DeployModelAmounts[idx] = amt;
                sess.DeployModelIndex++;
                if (sess.DeployModelIndex < sess.DeployModelNames.Count)
                {
                    await SendPrompt(uid, uid, $"🛩 مدل {sess.DeployModelIndex + 1}/{sess.DeployModelNames.Count}: {sess.DeployModelNames[sess.DeployModelIndex]} – موجودی {sess.DeployModelCounts[sess.DeployModelIndex]:N0}\nچند تا؟ (0 برای رد)", ct: ct);
                    return true;
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
                    return true;
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
                    return true;
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
                return true;
            }

            // DeployJoin steps
        return false;
    }

    static async Task<bool> TryHandlePrivateDeploymentJoinSession(long uid,string txt,UserSession sess,CancellationToken ct)
    {
            if (sess.Step == SessionStep.DeployJoinWaitingTanks)
            {
                var c = Database.GetCountry(uid, sess.DeployChatId);
                if (c == null) { EndSession(uid); return true; }
                var breakdown = GetTransferBreakdown(c, "tanks");
                if (breakdown.Count == 0)
                {
                    sess.DeployJoinTanks = 0;
                    sess.Step = SessionStep.DeployJoinWaitingSoldiers;
                    await SendPrompt(uid, uid, $"🪖 سرباز:\nموجود: {c.Soldiers:N0}", ct: ct);
                    return true;
                }
                sess.DeployModelNames = breakdown.Select(x => x.ModelName).ToList();
                sess.DeployModelCounts = breakdown.Select(x => x.Count).ToList();
                sess.DeployModelAmounts = new List<long>(new long[breakdown.Count]);
                sess.DeployModelIndex = 0;
                sess.Step = SessionStep.DeployJoinWaitingTankModel;
                await SendPrompt(uid, uid,
                    $"🛡 مشارکت – تانک مدل 1/{breakdown.Count}: {breakdown[0].ModelName} – موجودی {breakdown[0].Count:N0}\nچند تا اعزام شود؟ (0 برای رد)", ct: ct);
                return true;
            }
            if (sess.Step == SessionStep.DeployJoinWaitingTankModel)
            {
                if (!TryParseLong(txt, out long amount) || amount < 0)
                { await SendPrompt(uid, uid, "❌ عدد معتبر (0 برای رد).", ct: ct); return true; }
                int index = sess.DeployModelIndex;
                if (index < 0 || index >= sess.DeployModelCounts.Count) { EndSession(uid); return true; }
                if (amount > sess.DeployModelCounts[index])
                { await SendPrompt(uid, uid, $"❌ موجودی این مدل: {sess.DeployModelCounts[index]:N0}", ct: ct); return true; }
                sess.DeployModelAmounts[index] = amount;
                sess.DeployModelIndex++;
                if (sess.DeployModelIndex < sess.DeployModelNames.Count)
                {
                    int next = sess.DeployModelIndex;
                    await SendPrompt(uid, uid,
                        $"🛡 مشارکت – تانک مدل {next + 1}/{sess.DeployModelNames.Count}: {sess.DeployModelNames[next]} – موجودی {sess.DeployModelCounts[next]:N0}\nچند تا؟ (0 برای رد)", ct: ct);
                    return true;
                }
                sess.DeployJoinTanks = sess.DeployModelAmounts.Sum();
                sess.DeployTankModelNamesFinal = new List<string>(sess.DeployModelNames);
                sess.DeployTankModelAmountsFinal = new List<long>(sess.DeployModelAmounts);
                sess.Step = SessionStep.DeployJoinWaitingSoldiers;
                var c = Database.GetCountry(uid, sess.DeployChatId);
                await SendPrompt(uid, uid, $"🪖 سرباز:\nموجود: {c?.Soldiers ?? 0:N0}", ct: ct);
                return true;
            }
            if (sess.Step == SessionStep.DeployJoinWaitingSoldiers)
            {
                if (!TryParseLong(txt, out long sol) || sol < 0) { await SendPrompt(uid, uid, "❌ عدد معتبر.", ct: ct); return true; }
                var c = Database.GetCountry(uid, sess.DeployChatId);
                if (c == null) { EndSession(uid); return true; }
                if (sol > c.Soldiers) { await SendPrompt(uid, uid, $"❌ موجودی: {c.Soldiers}", ct: ct); return true; }
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
                        return true;
                    }
                    sess.DeployModelNames = bombers.Select(x => x.ModelName).ToList();
                    sess.DeployModelCounts = bombers.Select(x => x.Count).ToList();
                    sess.DeployModelAmounts = new List<long>(new long[bombers.Count]);
                    sess.DeployModelIndex = 0;
                    sess.Step = SessionStep.DeployJoinWaitingBomberModel;
                    await SendPrompt(uid, uid,
                        $"🛩 مشارکت – بمب‌افکن مدل 1/{bombers.Count}: {bombers[0].ModelName} – موجودی {bombers[0].Count:N0}\nچند تا اعزام شود؟ (0 برای رد)", ct: ct);
                    return true;
                }
                sess.DeployModelNames = planes.Select(x => x.ModelName).ToList();
                sess.DeployModelCounts = planes.Select(x => x.Count).ToList();
                sess.DeployModelAmounts = new List<long>(new long[planes.Count]);
                sess.DeployModelIndex = 0;
                sess.Step = SessionStep.DeployJoinWaitingPlaneModel;
                await SendPrompt(uid, uid,
                    $"✈️ مشارکت – جنگنده مدل 1/{planes.Count}: {planes[0].ModelName} – موجودی {planes[0].Count:N0}\nچند تا اعزام شود؟ (0 برای رد)", ct: ct);
                return true;
            }
            if (sess.Step == SessionStep.DeployJoinWaitingFighters)
            {
                var c = Database.GetCountry(uid, sess.DeployChatId);
                if (c == null) { EndSession(uid); return true; }
                var breakdown = GetTransferBreakdown(c, "planes");
                if (breakdown.Count == 0)
                {
                    sess.DeployJoinFighters = 0;
                    sess.Step = SessionStep.DeployJoinWaitingBombers;
                    await SendPrompt(uid, uid, "🛩 برای انتخاب مدل‌های بمب‌افکن یک عدد ارسال کنید.", ct: ct);
                    return true;
                }
                sess.DeployModelNames = breakdown.Select(x => x.ModelName).ToList();
                sess.DeployModelCounts = breakdown.Select(x => x.Count).ToList();
                sess.DeployModelAmounts = new List<long>(new long[breakdown.Count]);
                sess.DeployModelIndex = 0;
                sess.Step = SessionStep.DeployJoinWaitingPlaneModel;
                await SendPrompt(uid, uid,
                    $"✈️ مشارکت – جنگنده مدل 1/{breakdown.Count}: {breakdown[0].ModelName} – موجودی {breakdown[0].Count:N0}\nچند تا اعزام شود؟ (0 برای رد)", ct: ct);
                return true;
            }
            if (sess.Step == SessionStep.DeployJoinWaitingPlaneModel)
            {
                if (!TryParseLong(txt, out long amount) || amount < 0)
                { await SendPrompt(uid, uid, "❌ عدد معتبر (0 برای رد).", ct: ct); return true; }
                int index = sess.DeployModelIndex;
                if (index < 0 || index >= sess.DeployModelCounts.Count) { EndSession(uid); return true; }
                if (amount > sess.DeployModelCounts[index])
                { await SendPrompt(uid, uid, $"❌ موجودی این مدل: {sess.DeployModelCounts[index]:N0}", ct: ct); return true; }
                sess.DeployModelAmounts[index] = amount;
                sess.DeployModelIndex++;
                if (sess.DeployModelIndex < sess.DeployModelNames.Count)
                {
                    int next = sess.DeployModelIndex;
                    await SendPrompt(uid, uid,
                        $"✈️ مشارکت – جنگنده مدل {next + 1}/{sess.DeployModelNames.Count}: {sess.DeployModelNames[next]} – موجودی {sess.DeployModelCounts[next]:N0}\nچند تا؟ (0 برای رد)", ct: ct);
                    return true;
                }
                sess.DeployJoinFighters = sess.DeployModelAmounts.Sum();
                sess.DeployPlaneModelNamesFinal = new List<string>(sess.DeployModelNames);
                sess.DeployPlaneModelAmountsFinal = new List<long>(sess.DeployModelAmounts);
                var c = Database.GetCountry(uid, sess.DeployChatId);
                if (c == null) { EndSession(uid); return true; }
                var bombers = GetTransferBreakdown(c, "bombers");
                if (bombers.Count == 0)
                {
                    sess.DeployJoinBombers = 0;
                    sess.Step = SessionStep.DeployJoinWaitingBombers;
                    await SendPrompt(uid, uid, "🛩 بمب‌افکن ندارید؛ برای ثبت نهایی عدد 0 را ارسال کنید.", ct: ct);
                    return true;
                }
                sess.DeployModelNames = bombers.Select(x => x.ModelName).ToList();
                sess.DeployModelCounts = bombers.Select(x => x.Count).ToList();
                sess.DeployModelAmounts = new List<long>(new long[bombers.Count]);
                sess.DeployModelIndex = 0;
                sess.Step = SessionStep.DeployJoinWaitingBomberModel;
                await SendPrompt(uid, uid,
                    $"🛩 مشارکت – بمب‌افکن مدل 1/{bombers.Count}: {bombers[0].ModelName} – موجودی {bombers[0].Count:N0}\nچند تا اعزام شود؟ (0 برای رد)", ct: ct);
                return true;
            }
            if (sess.Step == SessionStep.DeployJoinWaitingBomberModel)
            {
                if (!TryParseLong(txt, out long amount) || amount < 0)
                { await SendPrompt(uid, uid, "❌ عدد معتبر (0 برای رد).", ct: ct); return true; }
                int index = sess.DeployModelIndex;
                if (index < 0 || index >= sess.DeployModelCounts.Count) { EndSession(uid); return true; }
                if (amount > sess.DeployModelCounts[index])
                { await SendPrompt(uid, uid, $"❌ موجودی این مدل: {sess.DeployModelCounts[index]:N0}", ct: ct); return true; }
                sess.DeployModelAmounts[index] = amount;
                sess.DeployModelIndex++;
                if (sess.DeployModelIndex < sess.DeployModelNames.Count)
                {
                    int next = sess.DeployModelIndex;
                    await SendPrompt(uid, uid,
                        $"🛩 مشارکت – بمب‌افکن مدل {next + 1}/{sess.DeployModelNames.Count}: {sess.DeployModelNames[next]} – موجودی {sess.DeployModelCounts[next]:N0}\nچند تا؟ (0 برای رد)", ct: ct);
                    return true;
                }
                sess.DeployJoinBombers = sess.DeployModelAmounts.Sum();
                sess.DeployBomberModelNamesFinal = new List<string>(sess.DeployModelNames);
                sess.DeployBomberModelAmountsFinal = new List<long>(sess.DeployModelAmounts);
                sess.Step = SessionStep.DeployJoinWaitingBombers;
                await SendPrompt(uid, uid, "✅ ترکیب نیرو کامل شد؛ برای ثبت نهایی عدد 0 را ارسال کنید.", ct: ct);
                return true;
            }
            if (sess.Step == SessionStep.DeployJoinWaitingBombers)
            {
                long bom;
                if (sess.DeployBomberModelAmountsFinal.Count > 0)
                    bom = sess.DeployBomberModelAmountsFinal.Sum();
                else if (!TryParseLong(txt, out bom) || bom < 0)
                { await SendPrompt(uid, uid, "❌ عدد معتبر.", ct: ct); return true; }
                var c = Database.GetCountry(uid, sess.DeployChatId);
                if (c == null) { EndSession(uid); return true; }
                if (bom > c.Bombers) { await SendPrompt(uid, uid, $"❌ موجودی: {c.Bombers}", ct: ct); return true; }
                sess.DeployJoinBombers = bom;
                var dep = Database.GetDeploymentById(sess.DeployJoinId);
                if (dep == null || dep.EndAtMs <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) { EndSession(uid); await SendTemp(uid, "❌ مهلت پایان یافته.", ct: ct); return true; }
                if (!HasAvailableForces(c, sess.DeployJoinTanks, sess.DeployJoinSoldiers, sess.DeployJoinFighters, sess.DeployJoinBombers))
                {
                    EndSession(uid);
                    await SendTemp(uid,
                        "❌ موجودی نیروها در طول عملیات تغییر کرده است. اعزام انجام نشد.\n\n" +
                        AvailableForcesText(c),
                        ct: ct);
                    return true;
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
                    return true;
                }
                Database.ReconcileDefense(uid, sess.DeployChatId);
                //  – defensive join no longer adds to target assets, only tracked separately
                // Refresh pinned deployment announcement to list all participants (only participating players)
                try { await RefreshDeploymentAnnouncement(dep.Id, ct); } catch { }
                EndSession(uid);
                await SendTemp(uid, "✅ نیروها اعزام شدند!", ct: ct);
                string announce = $"🚀 کشور «{c.Name}» ({c.OwnerName}) نیروی کمکی اعزام کرد: {sess.DeployJoinTanks:N0} تانک, {sess.DeployJoinSoldiers:N0} سرباز, {sess.DeployJoinFighters:N0} جنگنده, {sess.DeployJoinBombers:N0} بمب‌افکن";
                try { await SendPermanent(dep.ChatId, announce, ct: ct); } catch { }
                return true;
            }

            // Attack steps –  per-model
        return false;
    }

    static async Task<bool> TryHandlePrivateAttackSession(long uid,string txt,UserSession sess,CancellationToken ct)
    {
            if (sess.Step == SessionStep.AttackWaitingTanks)
            {
                // Legacy fallback – should not happen now, redirect to per-model
                var atk = Database.GetCountry(uid, sess.AttackChatId);
                if (atk == null) { EndSession(uid); return true; }
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
                    return true;
                }
                sess.AttackTanks = 0;
                sess.Step = SessionStep.AttackWaitingSoldiers;
                await SendPrompt(uid, uid, "🪖 تعداد سربازان اعزامی را وارد کنید.\n" + InventoryLine(GetAttackAvailableSoldiers(atk)), ct: ct);
                return true;
            }

            if (sess.Step == SessionStep.AttackWaitingModelAmount)
            {
                //  – naval per-model amount
                if (!TryParseLong(txt, out long amt) || amt < 0) { await SendPrompt(uid, uid, "❌ عدد معتبر (0 برای رد).", ct: ct); return true; }
                int idx = sess.AttackModelIndex;
                if (idx < 0 || idx >= sess.AttackModelCounts.Count) { EndSession(uid); return true; }
                if (amt > sess.AttackModelCounts[idx]) { await SendPrompt(uid, uid, $"❌ موجودی این مدل: {sess.AttackModelCounts[idx]:N0}", ct: ct); return true; }
                sess.AttackModelAmounts[idx] = amt;
                sess.AttackModelIndex++;
                if (sess.AttackModelIndex < sess.AttackModelNames.Count)
                {
                    var next = sess.AttackModelNames[sess.AttackModelIndex];
                    var nextCnt = sess.AttackModelCounts[sess.AttackModelIndex];
                    string cat = next.Contains(':') ? next.Split(':')[0] : "naval";
                    string modelOnly = next.Contains(':') ? next.Split(':',2)[1] : next;
                    await SendPrompt(uid, uid, $"⚓ حمله دریایی – مدل {sess.AttackModelIndex + 1}/{sess.AttackModelNames.Count}: {modelOnly} ({cat}) – موجودی {nextCnt:N0}\nچند تا اعزام شود؟ (0 برای رد)", ct: ct);
                    return true;
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
                    return true;
                }
                var attackerCountry = Database.GetCountry(uid, sess.AttackChatId);
                var defenderCountry = Database.GetCountry(sess.AttackTargetId, sess.AttackChatId);
                if (attackerCountry == null || defenderCountry == null) { EndSession(uid); await SendTemp(uid, "❌ کشور یافت نشد.", ct: ct); return true; }

                bool fullExemption = Database.HasGroupLockExemption(sess.AttackChatId);
                if (Database.IsAttackShieldActive(defenderCountry.OwnerId, defenderCountry.ChatId) && !fullExemption)
                {
                    long until = Database.GetAttackShieldUntilMs(defenderCountry.OwnerId, defenderCountry.ChatId);
                    long leftH = Math.Max(1, (until - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) / 3600000);
                    EndSession(uid);
                    await SendTemp(uid, $"🛡 {defenderCountry.Name} تا {leftH} ساعت دیگر سپر فعال دارد.", ct: ct);
                    return true;
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
                    return true;
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
                    return true;
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
                return true;
            }

            if (sess.Step == SessionStep.AttackWaitingTankModel)
            {
                if (!TryParseLong(txt, out long amt) || amt < 0) { await SendPrompt(uid, uid, "❌ عدد معتبر (0 برای رد).", ct: ct); return true; }
                int idx = sess.AttackModelIndex;
                if (idx < 0 || idx >= sess.AttackModelCounts.Count) { EndSession(uid); return true; }
                if (amt > sess.AttackModelCounts[idx]) { await SendPrompt(uid, uid, $"❌ موجودی این مدل: {sess.AttackModelCounts[idx]:N0}", ct: ct); return true; }
                sess.AttackModelAmounts[idx] = amt;
                sess.AttackModelIndex++;
                if (sess.AttackModelIndex < sess.AttackModelNames.Count)
                {
                    var nextName = sess.AttackModelNames[sess.AttackModelIndex];
                    var nextCount = sess.AttackModelCounts[sess.AttackModelIndex];
                    await SendPrompt(uid, uid, $"🛡 حمله – تانک مدل {sess.AttackModelIndex + 1}/{sess.AttackModelNames.Count}: {nextName} – موجودی {nextCount:N0}\nچند تا اعزام شود؟ (0 برای رد)", ct: ct);
                    return true;
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
                return true;
            }

            if (sess.Step == SessionStep.AttackWaitingSoldiers)
            {
                if (!TryParseLong(txt, out long soldiers) || soldiers < 0) { await SendPrompt(uid, uid, "❌ عدد معتبر.", ct: ct); return true; }
                var atk = Database.GetCountry(uid, sess.AttackChatId);
                if (atk == null) { EndSession(uid); return true; }
                long availableSoldiers = GetAttackAvailableSoldiers(atk);
                if (soldiers > availableSoldiers)
                { await SendPrompt(uid, uid, $"❌ قابل اعزام: {availableSoldiers:N0}؛ حداقل ۲۰٪ در دفاع می‌ماند.", ct: ct); return true; }
                sess.AttackSoldiers = soldiers;

                // Now per-model planes
                var planeBreakdown = GetAttackBreakdown(atk, "planes");
                if (planeBreakdown.Count == 0)
                {
                    sess.AttackFighters=0;sess.AttackPlaneModelNamesFinal=new();sess.AttackPlaneModelAmountsFinal=new();
                    await BeginAttackBomberSelection(uid,sess,atk,ct);
                    return true;
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
                    return true;
                }
                sess.AttackModelNames = planeBreakdown.Select(x => x.ModelName).ToList();
                sess.AttackModelCounts = planeBreakdown.Select(x => x.Count).ToList();
                sess.AttackModelAmounts = new List<long>(new long[planeBreakdown.Count]);
                sess.AttackModelIndex = 0;
                sess.AttackCurrentCategory = "planes";
                sess.Step = SessionStep.AttackWaitingPlaneModel;
                await SendPrompt(uid, uid, $"✈️ حمله – جنگنده‌ها – {planeBreakdown.Count} مدل\n🔧 مدل 1/{planeBreakdown.Count}: {planeBreakdown[0].ModelName} – {planeBreakdown[0].Count:N0}\nچند تا اعزام شود؟ (0 برای رد)", ct: ct);
                return true;
            }

            if (sess.Step == SessionStep.AttackWaitingPlaneModel)
            {
                if (!TryParseLong(txt, out long amt) || amt < 0) { await SendPrompt(uid, uid, "❌ عدد معتبر.", ct: ct); return true; }
                int idx = sess.AttackModelIndex;
                if (idx < 0 || idx >= sess.AttackModelCounts.Count) { EndSession(uid); return true; }
                if (amt > sess.AttackModelCounts[idx]) { await SendPrompt(uid, uid, $"❌ موجودی: {sess.AttackModelCounts[idx]:N0}", ct: ct); return true; }
                sess.AttackModelAmounts[idx] = amt;
                sess.AttackModelIndex++;
                if (sess.AttackModelIndex < sess.AttackModelNames.Count)
                {
                    await SendPrompt(uid, uid, $"✈️ مدل {sess.AttackModelIndex + 1}/{sess.AttackModelNames.Count}: {sess.AttackModelNames[sess.AttackModelIndex]} – موجودی {sess.AttackModelCounts[sess.AttackModelIndex]:N0}\nچند تا؟ (0 برای رد)", ct: ct);
                    return true;
                }
                sess.AttackFighters = sess.AttackModelAmounts.Sum();
                sess.AttackPlaneModelNamesFinal = new List<string>(sess.AttackModelNames);
                sess.AttackPlaneModelAmountsFinal = new List<long>(sess.AttackModelAmounts);
                var atk = Database.GetCountry(uid, sess.AttackChatId);
                if(atk==null){EndSession(uid);return true;}
                await BeginAttackBomberSelection(uid,sess,atk,ct);
                return true;
            }

            if (sess.Step == SessionStep.AttackWaitingBomberModel)
            {
                if (!TryParseLong(txt, out long amt) || amt < 0) { await SendPrompt(uid, uid, "❌ عدد معتبر.", ct: ct); return true; }
                int idx = sess.AttackModelIndex;
                if (idx < 0 || idx >= sess.AttackModelCounts.Count) { EndSession(uid); return true; }
                if (amt > sess.AttackModelCounts[idx]) { await SendPrompt(uid, uid, $"❌ موجودی: {sess.AttackModelCounts[idx]:N0}", ct: ct); return true; }
                sess.AttackModelAmounts[idx] = amt;
                sess.AttackModelIndex++;
                if (sess.AttackModelIndex < sess.AttackModelNames.Count)
                {
                    await SendPrompt(uid, uid, $"🛩 مدل {sess.AttackModelIndex + 1}/{sess.AttackModelNames.Count}: {sess.AttackModelNames[sess.AttackModelIndex]} – موجودی {sess.AttackModelCounts[sess.AttackModelIndex]:N0}\nچند تا؟ (0 برای رد)", ct: ct);
                    return true;
                }
                sess.AttackBombers = sess.AttackModelAmounts.Sum();
                sess.AttackBomberModelNamesFinal = new List<string>(sess.AttackModelNames);
                sess.AttackBomberModelAmountsFinal = new List<long>(sess.AttackModelAmounts);
                await PromptAttackAirOrRun(uid,sess,ct);
                return true;
            }

            if (sess.Step == SessionStep.AttackWaitingFighters)
            {
                var atk=Database.GetCountry(uid,sess.AttackChatId);if(atk==null){EndSession(uid);return true;}
                var planes=GetAttackBreakdown(atk,"planes");
                if(planes.Count==0){sess.AttackFighters=0;await BeginAttackBomberSelection(uid,sess,atk,ct);return true;}
                sess.AttackModelNames=planes.Select(x=>x.ModelName).ToList();sess.AttackModelCounts=planes.Select(x=>x.Count).ToList();
                sess.AttackModelAmounts=new List<long>(new long[planes.Count]);sess.AttackModelIndex=0;
                sess.AttackCurrentCategory="planes";sess.Step=SessionStep.AttackWaitingPlaneModel;
                await SendPrompt(uid,uid,$"✈️ انتخاب مدل‌به‌مدل فعال شد.\nمدل 1/{planes.Count}: {planes[0].ModelName}\nموجودی قابل اعزام: {planes[0].Count:N0}\nچند فروند؟",ct:ct);return true;
            }
            if (sess.Step == SessionStep.AttackWaitingBombers)
            {
                var atk=Database.GetCountry(uid,sess.AttackChatId);if(atk==null){EndSession(uid);return true;}
                await BeginAttackBomberSelection(uid,sess,atk,ct);return true;
            }
        return false;
    }

}
