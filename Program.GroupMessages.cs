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
        if (await TryHandleGroupCountryCommands(msg, user, chat, uid, txt, ct) ||
            await TryHandleGroupAllianceCommands(msg, user, chat, uid, txt, ct) ||
            await TryHandleGroupEconomyCommands(msg, user, chat, uid, txt, ct) ||
            await TryHandleGroupCombatCommands(msg, user, chat, uid, txt, ct))
            return;
    }

    static async Task<bool> TryHandleGroupCountryCommands(Message msg,User user,Chat chat,long uid,string txt,CancellationToken ct)
    {
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
                return true;
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
            return true;
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
                return true;
            }
            long nowRush = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1000L;
            foreach (var d in activeDeps) Database.UpdateDeploymentEndMs(d.Id, nowRush);
            await SendTemp(chat.Id, $"⚡ دستور عجله تهاجمی اعمال شد! {activeDeps.Count} مورد", replyTo: msg.MessageId, ct: ct);
            try { await ProcessActiveDeployments(ct); } catch { }
            return true;
        }
        if (uid == OWNER_ID && (txt == "عجله دفاع" || txt == "عجله دفاعی"))
        {
            var activeDeps = Database.GetActiveDeployments().Where(d => d.ChatId == chat.Id && d.Type == "Defensive").ToList();
            if (activeDeps.Count == 0)
            {
                await SendTemp(chat.Id, "❌ هیچ صفآرایی دفاعی فعالی در این گروه وجود ندارد.", replyTo: msg.MessageId, ct: ct);
                return true;
            }
            long nowRush = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1000L;
            foreach (var d in activeDeps) Database.UpdateDeploymentEndMs(d.Id, nowRush);
            await SendTemp(chat.Id, $"🛡 دستور عجله دفاعی اعمال شد! {activeDeps.Count} مورد", replyTo: msg.MessageId, ct: ct);
            try { await ProcessActiveDeployments(ct); } catch { }
            return true;
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
            return true;
        }

        if (uid == OWNER_ID && (txt == "لغو معافیت کامل" || txt == "حذف معافیت کامل"))
        {
            Database.SetGroupLockExemption(chat.Id, false);
            await SendTemp(chat.Id, "⛔ **معافیت کامل لغو شد.**\n\nقفل ۳۰ دقیقه‌ای ابتدای آپدیت مجدداً برای این گروه فعال شد.", replyTo: msg.MessageId, ct: ct);
            return true;
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
            return true;
        }

        if (txt == "لغو")
        {
            if (sessions.ContainsKey(uid)) { EndSession(uid); await SendTemp(chat.Id, "✅ عملیات لغو شد.", ct: ct); }
            else await SendTemp(chat.Id, "عملیات فعالی وجود ندارد.", ct: ct);
            return true;
        }

        if (txt == "انتخاب کشور")
        {
            if (Database.IsUserBanned(uid))
            {
                await SendTemp(chat.Id, "🚫 شما از بازی بن شده‌اید. لطفاً با ادمین تماس بگیرید.", ct: ct);
                return true;
            }
            if (Database.CountryExists(uid, chat.Id))
            {
                await SendTemp(chat.Id, "شما قبلاً کشور دارید", ct: ct);
                return true;
            }
            long remainMs = Database.GetLeaveCooldownRemainingMs(uid, chat.Id);
            if (remainMs > 0)
            {
                await SendTemp(chat.Id, $"⛔ شما اخیراً در این گروه انصراف داده‌اید.\n⏳ تا {FormatRemaining(remainMs)} دیگر نمی‌توانید کشور جدید بسازید.", ct: ct);
                return true;
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
            return true;
        }

        if (txt == "ارتقاع ساختمان" || txt == "ساختمان" || txt == "ارتقا اقتصاد" || txt == "اقتصاد")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return true; }
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("🏭 کارخانه", $"build_menu:{uid}:factory") },
                new[] { InlineKeyboardButton.WithCallbackData("⚓ بندر", $"build_menu:{uid}:port") },
                new[] { InlineKeyboardButton.WithCallbackData("⛏️ معدن", $"build_menu:{uid}:mine") }
            });
            await SendTemp(chat.Id, "ساختمان مورد نظر را انتخاب کنید:", markup: keyboard, ct: ct);
            return true;
        }

        if (txt == "انصراف")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return true; }
            sessions[uid] = new UserSession { Step = SessionStep.WaitingDeleteConfirm, ChatId = chat.Id };
            await SendPrompt(uid, chat.Id,
                "⚠️ در صورت انصراف تمامی اطلاعات شما در این گپ پاک میشود و این عمل غیر قابل بازگشت است.\n\nمطمئن هستید؟\nاگر بلی بنویسید بلی\nدر غیر این صورت بنویسید خیر",
                ct: ct);
            return true;
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
                return true;
            }
            if (txt == "خیر")
            {
                EndSession(uid);
                await SendTemp(chat.Id, "عملیات لغو شد.", ct: ct);
                return true;
            }
        }

        if (txt == "دارایی" || txt == "داراییم" || txt == "کشورم" || txt == "کشور من")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return true; }
            await SendCountryInfo(chat.Id, country, ct);
            return true;
        }

        if (txt == "مان پاور" || txt == "مان‌پاور" || txt == "مانپاور" || txt == "قدرت نظامی" || txt == "قدرت")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return true; }
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
            return true;
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
                return true;
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
            return true;
        }
        return false;
    }

    static async Task<bool> TryHandleGroupAllianceCommands(Message msg,User user,Chat chat,long uid,string txt,CancellationToken ct)
    {
        if (txt == "ساخت اتحاد" || txt == "ایجاد اتحاد" || txt == "تاسیس اتحاد")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return true; }
            long curAid = Database.GetUserAllianceId(chat.Id, uid);
            if (curAid > 0)
            {
                await SendTemp(chat.Id, "❌ شما در حال حاضر در یک اتحاد عضو هستید! برای ساخت اتحاد جدید ابتدا با دستور «خروج از اتحاد» یا «انحلال اتحاد» از اتحاد فعلی خارج شوید.", ct: ct);
                return true;
            }
            int totalPlayers = Database.GetCountriesByChatId(chat.Id).Count;
            int maxAlliances = Math.Max(1, totalPlayers / 2);
            var alliancesInChat = Database.GetAlliancesByChatId(chat.Id);
            if (alliancesInChat.Count >= maxAlliances)
            {
                await SendTemp(chat.Id, $"⛔ سقف تعداد اتحادهای مجاز در این گروه پر شده است!\n\n👥 تعداد بازیکنان گروه: {totalPlayers} نفر\n🏛 سقف مجاز اتحادها: {maxAlliances} اتحاد (به ازای هر ۲ بازیکن ۱ اتحاد)\n\n💡 برای ساخت اتحاد جدید، یا باید تعداد بازیکنان گروه بیشتر شود و یا یکی از اتحادهای فعلی منحل گردد.", ct: ct);
                return true;
            }
            sessions[uid] = new UserSession { Step = SessionStep.WaitingAllianceName, AllianceChatId = chat.Id };
            await SendPrompt(uid, chat.Id, "🏛 نام اتحاد خود را ارسال کنید:", ct: ct);
            return true;
        }

        if (txt == "ایجاد درخواست عضویت" || txt == "درخواست عضویت" || txt == "دعوت به اتحاد" || txt == "دعوت")
        {
            var leaderCountry = Database.GetCountry(uid, chat.Id);
            if (leaderCountry == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return true; }
            long aid = Database.GetUserAllianceId(chat.Id, uid);
            if (aid == 0)
            {
                await SendTemp(chat.Id, "❌ شما در هیچ اتحادی عضو نیستید. ابتدا با دستور «ساخت اتحاد»، اتحاد خود را بسازید.", replyTo: msg.MessageId, ct: ct);
                return true;
            }
            var alliance = Database.GetAllianceById(aid);
            if (alliance == null || alliance.LeaderId != uid)
            {
                await SendTemp(chat.Id, "❌ فقط رهبر اتحاد می‌تواند درخواست عضویت ارسال کند!", replyTo: msg.MessageId, ct: ct);
                return true;
            }
            if (msg.ReplyToMessage == null || msg.ReplyToMessage.From == null || msg.ReplyToMessage.From.IsBot)
            {
                await SendTemp(chat.Id, "❌ برای ارسال دعوت، باید روی پیام بازیکن مورد نظر ریپلای کنید.", replyTo: msg.MessageId, ct: ct);
                return true;
            }
            long tgtId = msg.ReplyToMessage.From.Id;
            if (tgtId == uid) { await SendTemp(chat.Id, "❌ نمی‌توانید خودتان را دعوت کنید!", replyTo: msg.MessageId, ct: ct); return true; }
            var tgtCountry = Database.GetCountry(tgtId, chat.Id);
            if (tgtCountry == null) { await SendTemp(chat.Id, "❌ بازیکن مورد نظر در این گپ کشوری ندارد.", replyTo: msg.MessageId, ct: ct); return true; }
            if (Database.GetUserAllianceId(chat.Id, tgtId) > 0) { await SendTemp(chat.Id, "❌ این بازیکن در حال حاضر در یک اتحاد دیگر عضو است!", replyTo: msg.MessageId, ct: ct); return true; }
            int totPlayers = Database.GetCountriesByChatId(chat.Id).Count;
            int maxMembers = Math.Max(2, totPlayers / 2);
            if (Database.GetAllianceMembers(aid).Count >= maxMembers)
            {
                await SendTemp(chat.Id, $"⛔ ظرفیت اتحاد تکمیل است! سقف: {maxMembers} نفر", replyTo: msg.MessageId, ct: ct);
                return true;
            }
            if (IsSuperpowerCollision(chat.Id, uid, tgtId, out string reason))
            {
                await SendTemp(chat.Id, reason, replyTo: msg.MessageId, ct: ct);
                return true;
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
            return true;
        }

        if (txt == "ترنسفر" || txt == "انتقال" || txt == "ارسال محموله" || txt == "ارسال منابع")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return true; }
            if (country.PortLevel < 3) { await SendTemp(chat.Id, "⚓ سطح بندر شما برای این عملیات کافی نیست! (حداقل سطح مورد نیاز: ۳)", replyTo: msg.MessageId, ct: ct); return true; }
            await SendTemp(chat.Id, "📦 برای ارسال محموله اقتصادی و نظامی به متحدان خود، به پیوی ربات مراجعه کنید.", replyTo: msg.MessageId, ct: ct);
            try
            {
                long aid = Database.GetUserAllianceId(chat.Id, uid);
                if (aid == 0) { await bot.SendTextMessageAsync(uid, "❌ شما در آن گروه عضو هیچ اتحادی نیستید.", cancellationToken: ct); return true; }
                var mems = Database.GetAllianceMembers(aid).Where(m => m != uid).ToList();
                if (mems.Count == 0) { await bot.SendTextMessageAsync(uid, "❌ اتحاد شما عضو دیگری ندارد.", cancellationToken: ct); return true; }
                if (GetTransferCount(chat.Id, uid) >= MAX_TRANSFERS_PER_UPDATE && !Database.HasGroupLockExemption(chat.Id))
                { await bot.SendTextMessageAsync(uid, $"⛔ سهمیه ترنسفر تمام شد ({MAX_TRANSFERS_PER_UPDATE}).", cancellationToken: ct); return true; }
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
            return true;
        }

        if (txt == "صف آرایی تهاجمی" || txt == "صف آرایی دفاعی" || txt == "صف‌آرایی تهاجمی" || txt == "صف‌آرایی دفاعی")
        {
            bool isOff = txt.Contains("تهاجمی");
            long cid = chat.Id;
            var sc = Database.GetCountry(uid, cid);
            if (sc == null) { await SendTemp(cid, MsgNoCountryGuide, ct: ct); return true; }
            if (sc.PortLevel < 3) { await SendTemp(cid, "⚓ سطح بندر شما برای این عملیات کافی نیست! (حداقل سطح مورد نیاز: ۳)", ct: ct); return true; }
            long aid = Database.GetUserAllianceId(cid, uid);
            if (aid == 0) { await SendTemp(cid, "❌ فقط اعضای اتحاد می‌توانند صف‌آرایی کنند.", ct: ct); return true; }
            var mems = Database.GetAllianceMembers(aid);
            int dailyLimit = mems.Count <= 5 ? 1 : (mems.Count <= 10 ? 2 : (mems.Count <= 20 ? 3 : 5));
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (Database.GetRecentAllianceDeploymentsCount(aid, nowMs - 86400000L) >= dailyLimit && !Database.HasGroupLockExemption(cid))
            { await SendTemp(cid, $"⛔ سقف روزانه صف‌آرایی ({dailyLimit}) پر شد.", ct: ct); return true; }
            var tgts = isOff ? Database.GetAttackableTargets(cid, uid) : mems.Select(m => Database.GetCountry(m, cid)).Where(c => c != null).ToList()!;
            if (tgts.Count == 0) { await SendTemp(cid, isOff ? "❌ هیچ هدفی خارج از اتحاد وجود ندارد." : "❌ عضو معتبری برای دفاع وجود ندارد.", ct: ct); return true; }
            await SendTemp(cid, "⚔️ برای تنظیم اسکجولر به پی‌وی ربات مراجعه کنید.", replyTo: msg.MessageId, ct: ct);
            var tkb = tgts.Select(t => new[] { InlineKeyboardButton.WithCallbackData($"🏳️ {t!.Name} ({t.OwnerName})", $"dep_target:{cid}:{aid}:{(isOff ? "Off" : "Def")}:{t.OwnerId}") }).ToArray();
            try { await SendPrompt(uid, uid, $"⚔️ **اعلام صف‌آرایی {(isOff ? "تهاجمی" : "دفاعی")}**\n\n🎯 کشور مورد نظر:", new InlineKeyboardMarkup(tkb), ct); } catch { }
            return true;
        }

        if (txt == "لغو صف آرایی" || txt == "لغو صف‌آرایی" || txt == "حذف صف آرایی" || txt == "حذف صف‌آرایی")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return true; }
            long aid = Database.GetUserAllianceId(chat.Id, uid);
            if (aid == 0) { await SendTemp(chat.Id, "❌ شما عضو هیچ اتحادی نیستید.", ct: ct); return true; }
            var alliance = Database.GetAllianceById(aid);
            if (alliance == null) return true;
            var deps = Database.GetActiveDeployments().Where(d => d.ChatId == chat.Id && d.AllianceId == aid).ToList();
            if (deps.Count == 0) { await SendTemp(chat.Id, "❌ هیچ صف‌آرایی فعالی از اتحاد شما نیست.", ct: ct); return true; }
            var myDeps = deps.Where(d => d.InitiatorId == uid || alliance.LeaderId == uid).ToList();
            if (myDeps.Count == 0) { await SendTemp(chat.Id, "❌ شما دسترسی لغو این صف‌آرایی‌ها را ندارید.", ct: ct); return true; }
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
            return true;
        }

        if (txt == "اعزام نیرو" || txt == "مشارکت" || txt == "مشارکت در صف آرایی" || txt == "مشارکت در صف‌آرایی" || txt == "اعزام" || txt == "اعزام نیرو ها" || txt == "اعزام نیروها")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return true; }
            if (country.PortLevel < 3) { await SendTemp(chat.Id, "⚓ سطح بندر شما برای این عملیات کافی نیست! (حداقل سطح مورد نیاز: ۳)", ct: ct); return true; }
            long aid = Database.GetUserAllianceId(chat.Id, uid);
            if (aid == 0) { await SendTemp(chat.Id, "❌ شما عضو هیچ اتحادی نیستید.", ct: ct); return true; }
            var deps = Database.GetActiveDeployments().Where(d => d.ChatId == chat.Id && d.AllianceId == aid).ToList();
            if (deps.Count == 0) { await SendTemp(chat.Id, "❌ هیچ صف‌آرایی فعالی از اتحاد شما نیست.", ct: ct); return true; }
            var kb = deps.Select(d => { var tc = Database.GetCountry(d.TargetUserId, chat.Id); string tn = tc?.Name ?? $"کاربر {d.TargetUserId}"; return new[] { InlineKeyboardButton.WithCallbackData($"⚔️ {(d.Type == "Offensive" ? "حمله" : "دفاع")} {tn}", $"dep_join:{d.Id}") }; }).ToArray();
            await SendTemp(chat.Id, "⚔️ صف‌آرایی‌های فعال:", markup: new InlineKeyboardMarkup(kb), ct: ct);
            return true;
        }

        if (txt == "لیست اتحاد ها" || txt == "لیست اتحادها" || txt == "اتحاد ها" || txt == "اتحادها")
        {
            var alliances = Database.GetAlliancesByChatId(chat.Id);
            if (alliances.Count == 0) { await SendTemp(chat.Id, "هنوز هیچ اتحادی در این گروه نیست.", ct: ct); return true; }
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
            return true;
        }

        if (txt == "وضعیت اتحاد")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return true; }
            long aid = Database.GetUserAllianceId(chat.Id, uid);
            if (aid == 0) { await SendTemp(chat.Id, "❌ شما در هیچ اتحادی عضو نیستید.", ct: ct); return true; }
            var alliance = Database.GetAllianceById(aid);
            if (alliance == null) { await SendTemp(chat.Id, "❌ اطلاعات اتحاد یافت نشد.", ct: ct); return true; }
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
            return true;
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
                        if (rank == 1) { await SendTemp(chat.Id, "❌ نمی‌توانید خودتان را اخراج کنید!", ct: ct); return true; }
                        if (rank < 1 || rank > memCountries.Count) { await SendTemp(chat.Id, $"❌ شماره نامعتبر (۱ تا {memCountries.Count})", ct: ct); return true; }
                        var tc = memCountries[rank - 1]!;
                        Database.RemoveAllianceMember(alliance.Id, chat.Id, tc.OwnerId);
                        await SendTemp(chat.Id, $"🚫 {tc.OwnerName} از اتحاد اخراج شد!", ct: ct);
                        try { await bot.SendTextMessageAsync(tc.OwnerId, $"🚫 شما از اتحاد «{alliance.Name}» اخراج شدید.", cancellationToken: ct); } catch { }
                        return true;
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
                    return true;
                }
                else if (alliance != null) { await SendTemp(chat.Id, "❌ فقط رهبر می‌تواند اتحاد را منحل کند.", ct: ct); return true; }
            }
        }

        if (txt == "خروج از اتحاد" || txt == "ترک اتحاد")
        {
            long aid = Database.GetUserAllianceId(chat.Id, uid);
            if (aid > 0)
            {
                var alliance = Database.GetAllianceById(aid);
                if (alliance != null && alliance.LeaderId == uid) { await SendTemp(chat.Id, "❌ شما رهبر هستید. از «انحلال اتحاد» استفاده کنید.", ct: ct); return true; }
                else if (alliance != null)
                {
                    var c = Database.GetCountry(uid, chat.Id);
                    Database.RemoveAllianceMember(alliance.Id, chat.Id, uid);
                    await SendTemp(chat.Id, $"👋 {c?.OwnerName ?? $"کاربر {uid}"} از اتحاد خارج شد!", ct: ct);
                    try { await bot.SendTextMessageAsync(alliance.LeaderId, $"👋 {c?.OwnerName ?? $"کاربر {uid}"} از اتحاد خارج شد.", cancellationToken: ct); } catch { }
                    return true;
                }
            }
        }
        return false;
    }

    static async Task<bool> TryHandleGroupEconomyCommands(Message msg,User user,Chat chat,long uid,string txt,CancellationToken ct)
    {
        if (txt == "راهنما")
        {
            // FIX(4): راهنمای کامل (در گروه)
            await SendTemp(chat.Id, HelpText, parseMode: ParseMode.Html, ct: ct);
            return true;
        }

        if (txt == "خرید تانک" || txt == "ساخت تانک")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return true; }
            InlineKeyboardMarkup tk = country.Faction switch
            {
                Faction.USA => new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("🇺🇸 M2 Medium", $"tank_info:{uid}:M2Medium") } }),
                Faction.USSR => new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("🇷🇺 T-28", $"tank_info:{uid}:T28") } }),
                _ => new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("🇩🇪 Panzer III", $"tank_info:{uid}:PanzerIII") } })
            };
            await SendTemp(chat.Id, "🛡️ تانک:", markup: tk, ct: ct);
            return true;
        }

        if (txt == "خرید هواپیما" || txt == "ساخت هواپیما" || txt == "خرید جنگنده" || txt == "ساخت جنگنده")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return true; }
            var fb = country.Faction switch
            {
                Faction.USA => new[] { InlineKeyboardButton.WithCallbackData("🇺🇸 P-36", $"plane_info:{uid}:P36"), InlineKeyboardButton.WithCallbackData("🇺🇸 B-17", $"bomber_info:{uid}:B17") },
                Faction.USSR => new[] { InlineKeyboardButton.WithCallbackData("🇷🇺 I-16", $"plane_info:{uid}:I16"), InlineKeyboardButton.WithCallbackData("🇷🇺 DB-3", $"bomber_info:{uid}:DB3") },
                _ => new[] { InlineKeyboardButton.WithCallbackData("🇩🇪 Bf 109", $"plane_info:{uid}:Bf109"), InlineKeyboardButton.WithCallbackData("🇩🇪 He 111", $"bomber_info:{uid}:He111") }
            };
            await SendTemp(chat.Id, "🛩️ نیروی هوایی:", markup: new InlineKeyboardMarkup(new[] { new[] { fb[0] }, new[] { fb[1] } }), ct: ct);
            return true;
        }

        if (txt == "خرید بمب افکن" || txt == "ساخت بمب افکن" || txt == "خرید بمب‌افکن" || txt == "ساخت بمب‌افکن")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return true; }
            var bk = country.Faction switch
            {
                Faction.USA => new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("🇺🇸 B-17", $"bomber_info:{uid}:B17") } }),
                Faction.USSR => new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("🇷🇺 DB-3", $"bomber_info:{uid}:DB3") } }),
                _ => new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("🇩🇪 He 111", $"bomber_info:{uid}:He111") } })
            };
            await SendTemp(chat.Id, "🛩️ بمب‌افکن:", markup: bk, ct: ct);
            return true;
        }

        if (txt == "پدافند" || txt == "خرید پدافند" || txt == "ساخت پدافند" || txt == "ضدهوایی" || txt == "ضد هوایی" || txt == "خرید ضد هوایی" || txt == "ساخت ضد هوایی")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return true; }
            await SendTemp(chat.Id, "🎯 پدافند:", markup: new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("🎯 توپ ۷۶ میلی‌متری", $"aa_info:{uid}:AA76") } }), ct: ct);
            return true;
        }

        if (txt == "خرید ناو" || txt == "ساخت ناو" || txt == "خرید کشتی" || txt == "ساخت کشتی" || txt == "خرید قایق" || txt == "ساخت قایق" || txt == "نیروی دریایی" || txt == "ناوگان")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return true; }
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
            return true;
        }

        if (txt == "تعمیر ناو" || txt == "تعمیر ناوگان" || txt == "تعمیر کشتی" || txt == "تعمیر ناو جنگی" || txt == "تعمیرات ناو")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return true; }
            Database.SyncBattleshipUnits(uid, chat.Id);
            var damaged = Database.GetBattleshipUnits(uid, chat.Id, onlyCombatReady: false).Where(x => x.DamagePercent > 0).ToList();
            if (damaged.Count == 0) { await SendTemp(chat.Id, "✅ نبردناو آسیب‌دیده‌ای ندارید.", ct: ct); return true; }
            var rows = damaged.Select(x => new[] { InlineKeyboardButton.WithCallbackData($"🔧 {x.Model} شماره {x.ShipNumber} — {x.DamagePercent}٪", $"battleship_repair_quote:{x.UnitId}") }).ToList();
            rows.Add(new[] { InlineKeyboardButton.WithCallbackData("❌ لغو", "cancel") });
            await SendTemp(chat.Id, "🔧 نبردناو موردنظر را انتخاب کنید. هزینه تعمیر برابر درصد واقعی آسیب از قیمت پول و آهن همان مدل است.", markup: new InlineKeyboardMarkup(rows), ct: ct);
            return true;
        }

        if (txt == "اوراق نبردناو" || txt == "اوراق ناو" || txt == "اسقاط نبردناو" || txt == "اسقاط ناو")
        {
            var country=Database.GetCountry(uid,chat.Id);
            if(country==null){await SendTemp(chat.Id,MsgNoCountryGuide,ct:ct);return true;}
            Database.SyncBattleshipUnits(uid,chat.Id);
            var ships=Database.GetBattleshipUnits(uid,chat.Id,onlyCombatReady:false);
            if(ships.Count==0){await SendTemp(chat.Id,"❌ نبردناو آماده‌ای برای اوراق ندارید. ناوهای در مأموریت یا انتقال قابل اوراق نیستند.",ct:ct);return true;}
            var rows=ships.Select(x=>new[]{InlineKeyboardButton.WithCallbackData($"♻️ {x.Model} شماره {x.ShipNumber} — آسیب {x.DamagePercent}٪",$"battleship_scrap:{x.UnitId}")}).ToList();
            rows.Add(new[]{InlineKeyboardButton.WithCallbackData("❌ لغو","cancel")});
            await SendTemp(chat.Id,"♻️ نبردناو موردنظر را انتخاب کنید. پس از تأیید، ۵۰٪ قیمت ساخت پول و آهن همان مدل برمی‌گردد.",markup:new InlineKeyboardMarkup(rows),ct:ct);
            return true;
        }

        if (txt == "تغییر اسم" || txt == "تعویض اسم" || txt == "تغییر اسم کشور" || txt == "تعویض اسم کشور")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return true; }
            sessions[uid] = new UserSession { Step = SessionStep.WaitingNewName, ChatId = chat.Id };
            await SendPrompt(uid, chat.Id, "اسم جدید را ارسال کنید.", ct: ct);
            return true;
        }

        if (txt == "ترید")
        {
            long r = Database.GetRoyalCoins(uid);
            sessions[uid] = new UserSession { Step = SessionStep.WaitingTradeAmount, ChatId = chat.Id };
            await SendPrompt(uid, chat.Id, $"💎 رویال: {r}\n\nچند رویال تبدیل کنید? (هر رویال = 10K)", ct: ct);
            return true;
        }

        if (txt == "آموزش سرباز" || txt == "نرخ سرباز گیری" || txt == "نرخ سربازگیری")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return true; }
            sessions[uid] = new UserSession { Step = SessionStep.WaitingRecruitmentRate, ChatId = chat.Id };
            await SendPrompt(uid, chat.Id, $"🎯 نرخ فعلی: {country.RecruitmentRate}\nعدد 0 تا 10:", ct: ct);
            return true;
        }

        if (txt == "مالیات" || txt == "نرخ مالیات")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return true; }
            sessions[uid] = new UserSession { Step = SessionStep.WaitingTaxRate, ChatId = chat.Id };
            long est = CalcTaxIncome(country);
            await SendPrompt(uid, chat.Id, $"💰 نرخ فعلی: {country.TaxRate}%\n📈 برآورد: {est / 1000.0:F1}K\nعدد 0 تا 100:", ct: ct);
            return true;
        }

        if (txt == "تغییر پرچم" || txt == "تعویض پرچم" || txt == "پرچم")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return true; }
            sessions[uid] = new UserSession { Step = SessionStep.WaitingNewFlag, ChatId = chat.Id };
            await SendPrompt(uid, chat.Id, "عکس پرچم جدید را ارسال کنید.", ct: ct);
            return true;
        }

        if (sessions.TryGetValue(uid, out var renameSess) && renameSess != null && renameSess.Step == SessionStep.WaitingNewName)
        {
            if (string.IsNullOrWhiteSpace(txt)) { await SendPrompt(uid, chat.Id, "اسم معتبر بفرستید.", ct: ct); return true; }
            if (Database.CountryNameExists(txt)) { await SendPrompt(uid, chat.Id, "این اسم قبلاً استفاده شده.", ct: ct); return true; }
            // Name similarity check >90% within same chat
            var existingCountryNames = Database.GetCountriesByChatId(chat.Id).Where(c => c.OwnerId != uid).Select(c => c.Name);
            if (IsNameTooSimilar(txt, existingCountryNames, 0.9))
            {
                await SendPrompt(uid, chat.Id, "❌ این نام خیلی شبیه به نام موجود است!! لطفاً نام دیگری انتخاب کنید.", ct: ct);
                return true;
            }
            Database.UpdateCountryName(uid, chat.Id, txt);
            EndSession(uid);
            await SendTemp(chat.Id, $"✅ نام کشور به {txt} تغییر یافت.", ct: ct);
            return true;
        }

        if (sessions.TryGetValue(uid, out var recSess) && recSess != null && recSess.Step == SessionStep.WaitingRecruitmentRate)
        {
            if (!TryParseInt(txt, out int rate) || rate < 0 || rate > 10) { await SendPrompt(uid, chat.Id, "❌ عدد 0 تا 10:", ct: ct); return true; }
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { EndSession(uid); return true; }
            country.RecruitmentRate = rate;
            Database.UpdateCountryFull(country);
            EndSession(uid);
            await SendTemp(chat.Id, $"✅ نرخ سربازگیری: {rate}\n🏥 هدف رفاه: {WelfareTarget(country):F0}%", ct: ct);
            return true;
        }

        if (sessions.TryGetValue(uid, out var taxSess) && taxSess != null && taxSess.Step == SessionStep.WaitingTaxRate)
        {
            if (!TryParseInt(txt, out int tx) || tx < 0 || tx > 100) { await SendPrompt(uid, chat.Id, "❌ عدد 0 تا 100:", ct: ct); return true; }
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { EndSession(uid); return true; }
            country.TaxRate = tx;
            Database.UpdateCountryFull(country);
            EndSession(uid);
            long est = CalcTaxIncome(country);
            await SendTemp(chat.Id, $"✅ نرخ مالیات: {tx}%\n💰 برآورد: {est / 1000.0:F1}K\n🏥 هدف رفاه: {WelfareTarget(country):F0}%", ct: ct);
            return true;
        }

        if (sessions.TryGetValue(uid, out var tradeSess) && tradeSess != null && tradeSess.Step == SessionStep.WaitingTradeAmount)
        {
            if (!TryParseLong(txt, out long ta) || ta <= 0) { await SendPrompt(uid, chat.Id, "عدد معتبر.", ct: ct); return true; }
            long r = Database.GetRoyalCoins(uid);
            if (ta > r) { await SendPrompt(uid, chat.Id, $"رویال کافی نیست. موجودی: {r}", ct: ct); return true; }
            var ctry = Database.GetCountry(uid, chat.Id);
            if (ctry == null) { EndSession(uid); return true; }
            Database.AddRoyalCoins(uid, -ta);
            ctry.Money += ta * 10000L;
            Database.UpdateCountryResources(uid, chat.Id, ctry.Money, ctry.Iron, ctry.Tanks);
            EndSession(uid);
            await SendPermanent(chat.Id, $"✅ {ta} رویال تبدیل شد 💰 +{ta * 10}K", ct: ct);
            return true;
        }

        if (sessions.TryGetValue(uid, out var flagSess) && flagSess != null && flagSess.Step == SessionStep.WaitingNewFlag)
        {
            if (msg.Photo == null || msg.Photo.Length == 0) { await SendPrompt(uid, chat.Id, "لطفاً عکس ارسال کنید.", ct: ct); return true; }
            string fid = msg.Photo.Last().FileId;
            Database.UpdateCountryFlag(uid, chat.Id, fid);
            EndSession(uid);
            await SendTemp(chat.Id, "✅ پرچم تغییر کرد.", ct: ct);
            var country = Database.GetCountry(uid, chat.Id);
            if (country != null) await SendCountryInfo(chat.Id, country, ct);
            return true;
        }

        if (sessions.TryGetValue(uid, out var allyFlagSess) && allyFlagSess != null && allyFlagSess.Step == SessionStep.WaitingAllianceFlag)
        {
            if (msg.Photo == null || msg.Photo.Length == 0) { await SendPrompt(uid, chat.Id, "عکس ارسال کنید.", ct: ct); return true; }
            string fid = msg.Photo.Last().FileId;
            var al = new Alliance { ChatId = allyFlagSess.AllianceChatId, Name = allyFlagSess.AllianceName, FlagFileId = fid, LeaderId = uid, CreatedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
            long aid = Database.AddAlliance(al);
            EndSession(uid);
            await SendTemp(chat.Id, $"🎉 اتحاد «{al.Name}» تاسیس شد!\n👑 رهبر: {FullName(user)}", ct: ct);
            return true;
        }

        if (sessions.TryGetValue(uid, out var allyNameSess) && allyNameSess != null && allyNameSess.Step == SessionStep.WaitingAllianceName)
        {
            if (string.IsNullOrWhiteSpace(txt)) { await SendPrompt(uid, chat.Id, "نام معتبر.", ct: ct); return true; }
            if (Database.AllianceNameExists(chat.Id, txt)) { await SendPrompt(uid, chat.Id, "این نام قبلاً ثبت شده.", ct: ct); return true; }
            var existingAllianceNames = Database.GetAlliancesByChatId(chat.Id).Select(a => a.Name);
            if (IsNameTooSimilar(txt, existingAllianceNames, 0.9))
            {
                await SendPrompt(uid, chat.Id, "❌ این نام خیلی شبیه به نام اتحاد موجود است!! لطفاً نام دیگری انتخاب کنید.", ct: ct);
                return true;
            }
            allyNameSess.Step = SessionStep.WaitingAllianceFlag;
            allyNameSess.AllianceName = txt;
            await SendPrompt(uid, chat.Id, $"✅ نام: «{txt}»\n🚩 عکس پرچم را ارسال کنید:", ct: ct);
            return true;
        }

        if (sessions.TryGetValue(uid, out var sess) && sess != null && sess.Step == SessionStep.WaitingCountryName)
        {
            long rm = Database.GetLeaveCooldownRemainingMs(uid, chat.Id);
            if (rm > 0) { EndSession(uid); await SendTemp(chat.Id, $"⛔ تا {FormatRemaining(rm)} نمی‌توانید کشور بسازید.", ct: ct); return true; }
            if (string.IsNullOrWhiteSpace(txt)) { await SendPrompt(uid, chat.Id, "اسم معتبر.", ct: ct); return true; }
            if (Database.CountryNameExists(txt)) { await SendPrompt(uid, chat.Id, "این اسم استفاده شده.", ct: ct); return true; }
            var existingNamesForNew = Database.GetCountriesByChatId(chat.Id).Select(c => c.Name);
            if (IsNameTooSimilar(txt, existingNamesForNew, 0.9))
            {
                await SendPrompt(uid, chat.Id, "❌ این نام خیلی شبیه به نام موجود است!! لطفاً نام دیگری انتخاب کنید.", ct: ct);
                return true;
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
            try { Database.AddCountry(nc); } catch (Exception ex) { Console.WriteLine($"[AddCountry FAIL] {ex.Message}"); EndSession(uid); return true; }
            EndSession(uid);
            await SendCountryInfo(chat.Id, nc, ct);
            return true;
        }
        return false;
    }

    static async Task<bool> TryHandleGroupCombatCommands(Message msg,User user,Chat chat,long uid,string txt,CancellationToken ct)
    {
        if (txt == "حمله")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return true; }
            await SendTemp(chat.Id, "⚔️ برای مشخص‌کردن هدف به پیوی مراجعه کنید.", replyTo: msg.MessageId, ct: ct);
            var targets = Database.GetAttackableTargets(chat.Id, uid);
            if (targets.Count == 0) { await SendTemp(uid, "هیچ هدفی در این گروه وجود ندارد.", ct: ct); return true; }
            var kb = targets.Select(t => new[] { InlineKeyboardButton.WithCallbackData($"{t.Name} ({t.OwnerName})", $"attack_target:{chat.Id}:{t.OwnerId}") }).ToArray();
            sessions[uid] = new UserSession { Step = SessionStep.AttackWaitingTarget, AttackChatId = chat.Id };
            await SendPrompt(uid, uid, "🎯 هدف را انتخاب کنید:", new InlineKeyboardMarkup(kb), ct);
            return true;
        }

        if (txt == "وضعیت دفاع")
        {
            var country = Database.GetCountry(uid, chat.Id);
            if (country == null) { await SendTemp(chat.Id, MsgNoCountryGuide, ct: ct); return true; }
            await SendTemp(chat.Id, "🛡 وضعیت دفاع به پیوی ارسال شد.", replyTo: msg.MessageId, ct: ct);
            try { await SendDefenseStatus(uid, uid, chat.Id, ct); }
            catch { await SendTemp(chat.Id, "⚠️ ابتدا ربات را در پیوی استارت کنید.", replyTo: msg.MessageId, ct: ct); }
            return true;
        }
        return false;
    }
}
