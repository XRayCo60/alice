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
}
