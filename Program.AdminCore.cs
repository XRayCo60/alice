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
