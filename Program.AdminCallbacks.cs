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

        if (await TryHandleAdminManagementActions(callback, ct, userId, parts, action) ||
            await TryHandleAdminEntityActions(callback, ct, userId, parts, action) ||
            await TryHandleAdminGameActions(callback, ct, userId, parts, action) ||
            await TryHandleAdminUtilityActions(callback, ct, userId, parts, action))
            return;

        await AnswerAdminCallback(
            callback,
            "دستور مدیریتی ناشناخته.",
            true,
            ct
        );
    }

    static async Task<bool> TryHandleAdminManagementActions(CallbackQuery callback,CancellationToken ct,long userId,string[] parts,string action)
    {
        return await TryHandleAdminNavigationActions(callback,ct,userId,parts,action) ||
               await TryHandleAdminPermissionActions(callback,ct,userId,parts,action) ||
               await TryHandleAdminAuditActions(callback,ct,userId,parts,action);
    }

    static async Task<bool> TryHandleAdminNavigationActions(CallbackQuery callback,CancellationToken ct,long userId,string[] parts,string action)
    {
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
            return true;
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
                return true;
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
            return true;
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
                return true;
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
            return true;
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
                return true;
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
            return true;
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
            return true;
        }
        return false;
    }

    static async Task<bool> TryHandleAdminPermissionActions(CallbackQuery callback,CancellationToken ct,long userId,string[] parts,string action)
    {
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
                return true;
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
            return true;
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
                return true;
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
            return true;
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
                return true;
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
                return true;
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
                return true;
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
            return true;
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
                return true;
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
            return true;
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
                return true;
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
            return true;
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
                return true;
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
                return true;
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
            return true;
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
                return true;
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
                return true;
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
            return true;
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
                return true;
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
            return true;
        }
        return false;
    }

    static async Task<bool> TryHandleAdminAuditActions(CallbackQuery callback,CancellationToken ct,long userId,string[] parts,string action)
    {
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
                return true;
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
            return true;
        }

        // ================= PLAYERS MODULE =================
        return false;
    }

    static async Task<bool> TryHandleAdminEntityActions(CallbackQuery callback,CancellationToken ct,long userId,string[] parts,string action)
    {
        if (action == "players")
        {
            if (!CanAdmin(userId, "P_VIEW"))
            {
                await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct);
                return true;
            }
            string sub = parts.Length >= 3 ? parts[2] : "home";
            if (sub == "home")
            {
                await RenderAdminScreen(callback, await BuildAdminPlayersHome(ct), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return true;
            }
            if (sub == "search")
            {
                adminInputRequests[userId] = new AdminInputRequest { Kind = "search_player", ExpiresAtMs = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds() };
                await RenderAdminScreen(callback, ("🔍 جستجوی پلیر\n\nآیدی عددی پلیر را وارد کنید.\nبرای لغو بنویسید: لغو", new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("❌ لغو", "adm:cancelinput") } })), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return true;
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
                return true;
            }
        }

        if (action == "player" && parts.Length >= 4)
        {
            string sub = parts[2];
            if (!TryParseLong(parts[3], out long targetId)) { await AnswerAdminCallback(callback, "❌ آیدی نامعتبر", true, ct); return true; }
            if (sub == "view")
            {
                await RenderAdminScreen(callback, BuildAdminPlayerDetail(targetId), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return true;
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
                return true;
            }
            if (sub == "royal")
            {
                if (!CanAdmin(userId, "ROYAL")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return true; }
                long royal = Database.GetRoyalCoins(targetId);
                var kb = new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData("💰 واریز", $"adm:economy:royal:add:{targetId}"), InlineKeyboardButton.WithCallbackData("💸 کسر", $"adm:economy:royal:deduct:{targetId}") },
                    new[] { InlineKeyboardButton.WithCallbackData("🔙 بازگشت", $"adm:player:view:{targetId}") }
                });
                await RenderAdminScreen(callback, ($"💎 رویال پلیر {targetId}\nموجودی: {royal:N0}", kb), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return true;
            }
            if (sub == "banask")
            {
                if (!CanAdmin(userId, "P_BAN")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return true; }
                adminInputRequests[userId] = new AdminInputRequest { Kind = "ban_reason", TargetId = targetId, ExpiresAtMs = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds() };
                await RenderAdminScreen(callback, ($"🚫 بن پلیر {targetId}\n\nدلیل بن را بنویسید.\nلغو: لغو", new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("❌ لغو", "adm:cancelinput") } })), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return true;
            }
            if (sub == "unban")
            {
                if (!CanAdmin(userId, "P_BAN")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return true; }
                Database.UnbanUser(targetId);
                Database.WriteAdminAudit(userId, "PLAYER_UNBAN", "Player", targetId.ToString(), "", true);
                await RenderAdminScreen(callback, BuildAdminPlayerDetail(targetId), ct);
                await AnswerAdminCallback(callback, "✅ آنبن شد.", false, ct);
                return true;
            }
            if (sub == "delcountries")
            {
                if (!CanAdmin(userId, "C_DELETE")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return true; }
                var countries = Database.GetCountriesByOwnerId(targetId);
                foreach (var c in countries) Database.DeleteCountry(c.OwnerId, c.ChatId);
                Database.WriteAdminAudit(userId, "PLAYER_DELCOUNTRIES", "Player", targetId.ToString(), $"{countries.Count}", true);
                await RenderAdminScreen(callback, BuildAdminPlayerDetail(targetId), ct);
                await AnswerAdminCallback(callback, $"✅ {countries.Count} کشور حذف شد.", false, ct);
                return true;
            }
        }

        // ================= COUNTRIES MODULE =================
        if (action == "countries")
        {
            if (!CanAdmin(userId, "C_VIEW")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return true; }
            string sub = parts.Length >= 3 ? parts[2] : "home";
            if (sub == "home")
            {
                await RenderAdminScreen(callback, await BuildAdminCountriesHome(ct), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return true;
            }
            if (sub == "search")
            {
                adminInputRequests[userId] = new AdminInputRequest { Kind = "search_country", ExpiresAtMs = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds() };
                await RenderAdminScreen(callback, ("🔍 جستجوی کشور\n\nنام کشور را وارد کنید (حداقل 2 حرف).\nلغو: لغو", new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("❌ لغو", "adm:cancelinput") } })), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return true;
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
                return true;
            }
        }

        if (action == "country" && parts.Length >= 4)
        {
            string sub = parts[2];
            if (!TryParseLong(parts[3], out long ownerId)) { await AnswerAdminCallback(callback, "❌ آیدی نامعتبر", true, ct); return true; }
            long chatId = parts.Length >= 5 && TryParseLong(parts[4], out long cId) ? cId : 0;

            if (sub == "view" && chatId != 0)
            {
                await RenderAdminScreen(callback, BuildAdminCountryDetail(ownerId, chatId), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return true;
            }
            if (sub.StartsWith("edit"))
            {
                if (!CanAdmin(userId, "C_RES") && (sub == "editmoney" || sub == "editiron" || sub == "editpop")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return true; }
                if (!CanAdmin(userId, "C_ARMY") && (sub.Contains("soldiers") || sub.Contains("tanks") || sub.Contains("planes") || sub.Contains("bombers") || sub.Contains("antiair"))) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return true; }
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
                if (string.IsNullOrEmpty(kind)) { await AnswerAdminCallback(callback, "❌ نامشخص", true, ct); return true; }
                adminInputRequests[userId] = new AdminInputRequest { Kind = kind, TargetId = ownerId, ChatId = chatId, ExpiresAtMs = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds() };
                await RenderAdminScreen(callback, ($"✏️ ویرایش {sub}\n\nمقدار جدید را وارد کنید برای کشور {ownerId}:{chatId}\nلغو: لغو", new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("❌ لغو", "adm:cancelinput") } })), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return true;
            }
            if (sub == "delask")
            {
                if (!CanAdmin(userId, "C_DELETE")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return true; }
                var kb = new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData("✅ بله حذف شود", $"adm:country:del:{ownerId}:{chatId}"), InlineKeyboardButton.WithCallbackData("❌ انصراف", $"adm:country:view:{ownerId}:{chatId}") }
                });
                await RenderAdminScreen(callback, ($"⚠️ حذف کشور {ownerId}:{chatId}؟", kb), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return true;
            }
            if (sub == "del" && chatId != 0)
            {
                if (!CanAdmin(userId, "C_DELETE")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return true; }
                Database.DeleteCountry(ownerId, chatId);
                Database.WriteAdminAudit(userId, "COUNTRY_DELETE", "Country", $"{ownerId}:{chatId}", "", true);
                await RenderAdminScreen(callback, ("✅ کشور حذف شد.", new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("🏠 خانه", "adm:home") } })), ct);
                await AnswerAdminCallback(callback, "✅ حذف شد.", false, ct);
                return true;
            }
        }

        // ================= GROUPS MODULE =================
        if (action == "groups")
        {
            if (!CanAdmin(userId, "G_VIEW")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return true; }
            string sub = parts.Length >= 3 ? parts[2] : "home";
            if (sub == "home")
            {
                await RenderAdminScreen(callback, await BuildAdminGroupsHome(ct), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return true;
            }
            if (sub == "search")
            {
                adminInputRequests[userId] = new AdminInputRequest { Kind = "search_group", ExpiresAtMs = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds() };
                await RenderAdminScreen(callback, ("🔍 جستجوی گروه\n\nآیدی عددی گروه را وارد کنید.\nلغو: لغو", new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("❌ لغو", "adm:cancelinput") } })), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return true;
            }
        }

        if (action == "group" && parts.Length >= 4)
        {
            string sub = parts[2];
            if (!TryParseLong(parts[3], out long chatId)) { await AnswerAdminCallback(callback, "❌ آیدی نامعتبر", true, ct); return true; }
            if (sub == "view")
            {
                await RenderAdminScreen(callback, await BuildAdminGroupDetail(chatId, ct), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return true;
            }
            if (sub == "togglelock")
            {
                if (!CanAdmin(userId, "G_EDIT")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return true; }
                bool has = Database.HasGroupLockExemption(chatId);
                Database.SetGroupLockExemption(chatId, !has);
                Database.WriteAdminAudit(userId, has ? "GROUP_LOCK_REMOVE" : "GROUP_LOCK_ADD", "Group", chatId.ToString(), "", true);
                await RenderAdminScreen(callback, await BuildAdminGroupDetail(chatId, ct), ct);
                await AnswerAdminCallback(callback, has ? "✅ معافیت حذف شد." : "✅ معافیت افزوده شد.", false, ct);
                return true;
            }
            if (sub == "clearcd")
            {
                if (!CanAdmin(userId, "G_EDIT")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return true; }
                Database.ClearAllLeaveCooldownsInChat(chatId);
                Database.WriteAdminAudit(userId, "GROUP_CLEAR_CD", "Group", chatId.ToString(), "", true);
                await RenderAdminScreen(callback, await BuildAdminGroupDetail(chatId, ct), ct);
                await AnswerAdminCallback(callback, "✅ کول‌داون‌ها پاک شد.", false, ct);
                return true;
            }
            if (sub == "shieldall")
            {
                if (!CanAdmin(userId, "G_EDIT")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return true; }
                Database.SetAllShieldExemptionsInChat(chatId);
                Database.WriteAdminAudit(userId, "GROUP_SHIELD_ALL", "Group", chatId.ToString(), "", true);
                await RenderAdminScreen(callback, await BuildAdminGroupDetail(chatId, ct), ct);
                await AnswerAdminCallback(callback, "✅ همه معافیت سپر گرفتند.", false, ct);
                return true;
            }
            if (sub == "assetnow")
            {
                if (!CanAdmin(userId, "G_EDIT")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return true; }
                try { await RunAssetUpdate(force: true); await AnswerAdminCallback(callback, "✅ آپدیت دارایی اجرا شد.", false, ct); }
                catch (Exception ex) { await AnswerAdminCallback(callback, $"❌ خطا: {ex.Message}", true, ct); }
                return true;
            }
        }

        // ================= ALLIANCES MODULE =================
        if (action == "alliances")
        {
            if (!CanAdmin(userId, "ALLY")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return true; }
            string sub = parts.Length >= 3 ? parts[2] : "home";
            if (sub == "home")
            {
                await RenderAdminScreen(callback, BuildAdminAlliancesHome(), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return true;
            }
        }

        if (action == "alliance" && parts.Length >= 4)
        {
            if (!TryParseLong(parts[3], out long allianceId)) { await AnswerAdminCallback(callback, "❌ آیدی نامعتبر", true, ct); return true; }
            string sub = parts[2];
            if (sub == "view")
            {
                var alliance = Database.GetAllianceById(allianceId);
                if (alliance == null) { await AnswerAdminCallback(callback, "❌ اتحاد یافت نشد.", true, ct); return true; }
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
                return true;
            }
            if (sub == "del")
            {
                if (!CanAdmin(userId, "ALLY")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return true; }
                Database.DeleteAlliance(allianceId);
                Database.WriteAdminAudit(userId, "ALLIANCE_DELETE", "Alliance", allianceId.ToString(), "", true);
                await RenderAdminScreen(callback, BuildAdminAlliancesHome(), ct);
                await AnswerAdminCallback(callback, "✅ اتحاد حذف شد.", false, ct);
                return true;
            }
        }

        // ================= ECONOMY MODULE =================
        return false;
    }

    static async Task<bool> TryHandleAdminGameActions(CallbackQuery callback,CancellationToken ct,long userId,string[] parts,string action)
    {
        if (action == "economy")
        {
            if (!CanAdmin(userId, "ROYAL")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return true; }
            string sub = parts.Length >= 3 ? parts[2] : "home";
            if (sub == "home")
            {
                await RenderAdminScreen(callback, BuildAdminEconomyHome(), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return true;
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
                    return true;
                }
                else
                {
                    adminInputRequests[userId] = new AdminInputRequest { Kind = op == "add" ? "royal_add" : "royal_deduct", TargetId = targetId, ExpiresAtMs = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds() };
                    await RenderAdminScreen(callback, ($"💎 {(op == "add" ? "واریز" : "کسر")} رویال برای {targetId}\n\nمقدار را وارد کنید:\nلغو: لغو", new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("❌ لغو", "adm:cancelinput") } })), ct);
                    await AnswerAdminCallback(callback, null, false, ct);
                    return true;
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
                return true;
            }
        }

        // ================= WAR MODULE =================
        if (action == "war")
        {
            if (!CanAdmin(userId, "W_VIEW")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return true; }
            string sub = parts.Length >= 3 ? parts[2] : "home";
            if (sub == "home")
            {
                await RenderAdminScreen(callback, await BuildAdminWarHome(ct), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return true;
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
                return true;
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
                return true;
            }
            if (sub == "clearlocks")
            {
                if (!CanAdmin(userId, "W_EDIT")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return true; }
                try
                {
                    using var con = Database.OpenConForAdmin();
                    using var cmd = con.CreateCommand();
                    cmd.CommandText = "DELETE FROM AttackAbandonLocks";
                    cmd.ExecuteNonQuery();
                    await AnswerAdminCallback(callback, "✅ تمام قفل‌های بزن‌دررو پاک شد.", false, ct);
                }
                catch (Exception ex) { await AnswerAdminCallback(callback, $"❌ خطا: {ex.Message}", true, ct); }
                return true;
            }
        }

        // ================= OPERATIONS MODULE =================
        if (action == "ops")
        {
            if (!CanAdmin(userId, "O_VIEW")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return true; }
            string sub = parts.Length >= 3 ? parts[2] : "home";
            if (sub == "home")
            {
                await RenderAdminScreen(callback, BuildAdminOperationsHome(), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return true;
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
                return true;
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
                return true;
            }
            if (sub == "canceltransfer" && parts.Length >= 4 && TryParseLong(parts[3], out long tId))
            {
                if (!CanAdmin(userId, "O_EDIT")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return true; }
                Database.DeleteTransfer(tId);
                Database.WriteAdminAudit(userId, "TRANSFER_CANCEL", "Transfer", tId.ToString(), "", true);
                await RenderAdminScreen(callback, BuildAdminOperationsHome(), ct);
                await AnswerAdminCallback(callback, $"✅ ترنسفر {tId} لغو شد.", false, ct);
                return true;
            }
            if (sub == "canceldep" && parts.Length >= 4 && TryParseLong(parts[3], out long dId))
            {
                if (!CanAdmin(userId, "O_EDIT")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return true; }
                var dep = Database.GetDeploymentById(dId);
                if (dep != null) await CancelDeploymentSafely(dep, ct);
                else Database.DeleteDeployment(dId);
                Database.WriteAdminAudit(userId, "DEPLOY_CANCEL", "Deployment", dId.ToString(), "", true);
                await RenderAdminScreen(callback, BuildAdminOperationsHome(), ct);
                await AnswerAdminCallback(callback, $"✅ صف‌آرایی {dId} لغو شد.", false, ct);
                return true;
            }
            if (sub == "cleartransfers")
            {
                if (!CanAdmin(userId, "O_EDIT")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return true; }
                var transfers = Database.GetActiveTransfers();
                foreach (var t in transfers) Database.DeleteTransfer(t.Id);
                await RenderAdminScreen(callback, BuildAdminOperationsHome(), ct);
                await AnswerAdminCallback(callback, $"✅ {transfers.Count} ترنسفر پاک شد.", false, ct);
                return true;
            }
        }

        // ================= ANNOUNCE MODULE =================
        return false;
    }

    static async Task<bool> TryHandleAdminUtilityActions(CallbackQuery callback,CancellationToken ct,long userId,string[] parts,string action)
    {
        if (action == "ann")
        {
            if (!CanAdmin(userId, "ANN")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return true; }
            string sub = parts.Length >= 3 ? parts[2] : "home";
            if (sub == "home")
            {
                await RenderAdminScreen(callback, BuildAdminAnnounceHome(), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return true;
            }
            if (sub == "write")
            {
                adminInputRequests[userId] = new AdminInputRequest { Kind = "announce_text", ExpiresAtMs = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeMilliseconds() };
                await RenderAdminScreen(callback, ("📝 اعلامیه\n\nمتن اعلامیه را ارسال کنید (متن، عکس، فایل). برای لغو: لغو", new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("❌ لغو", "adm:cancelinput") } })), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return true;
            }
            if (sub == "groups" || sub == "private" || sub == "all")
            {
                if (!adminInputRequests.TryGetValue(userId, out var annReq) || !annReq.Kind.StartsWith("announce")) { await AnswerAdminCallback(callback, "❌ ابتدا متن را وارد کنید.", true, ct); return true; }
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
                return true;
            }
            if (sub == "cancel")
            {
                adminInputRequests.TryRemove(userId, out _);
                await RenderAdminScreen(callback, BuildAdminAnnounceHome(), ct);
                await AnswerAdminCallback(callback, "❌ لغو شد.", false, ct);
                return true;
            }
        }

        // ================= SETTINGS MODULE =================
        if (action == "settings")
        {
            if (!CanAdmin(userId, "SET")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return true; }
            string sub = parts.Length >= 3 ? parts[2] : "home";
            if (sub == "home")
            {
                await RenderAdminScreen(callback, BuildAdminSettingsHome(), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return true;
            }
            if (sub == "lbchannel")
            {
                if (parts.Length >= 4 && parts[3] == "clear")
                {
                    Database.SetSetting("LeaderboardChannelId", "0");
                    Database.WriteAdminAudit(userId, "SET_LB_CHANNEL_CLEAR", "Settings", "", "", true);
                    await RenderAdminScreen(callback, BuildAdminSettingsHome(), ct);
                    await AnswerAdminCallback(callback, "✅ کانال حذف شد.", false, ct);
                    return true;
                }
                adminInputRequests[userId] = new AdminInputRequest { Kind = "set_leaderboard_channel", ExpiresAtMs = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds() };
                await RenderAdminScreen(callback, ("📢 تنظیم کانال لیدربورد\n\nآیدی عددی کانال (مثلاً -1001234567890) یا @username کانال را ارسال کنید، یا یک پیام از کانال فوروارد کنید.\nبرای حذف کانال 0 بنویسید.\nلغو: لغو", new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("❌ لغو", "adm:cancelinput") } })), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return true;
            }
            if (sub == "attacklock")
            {
                adminInputRequests[userId] = new AdminInputRequest { Kind = "set_attack_lock", ExpiresAtMs = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds() };
                await RenderAdminScreen(callback, ($"⏰ قفل حمله فعلی: {ATTACK_LOCK_MINUTES} دقیقه\n\nمقدار جدید را وارد کنید (0-1440):\nلغو: لغو", new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("❌ لغو", "adm:cancelinput") } })), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return true;
            }
            if (sub == "shield")
            {
                adminInputRequests[userId] = new AdminInputRequest { Kind = "set_shield_hours", ExpiresAtMs = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds() };
                await RenderAdminScreen(callback, ($"🛡 سپر فعلی: {SHIELD_HOURS} ساعت\n\nمقدار جدید:\nلغو: لغو", new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("❌ لغو", "adm:cancelinput") } })), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return true;
            }
            if (sub == "maxattacks")
            {
                adminInputRequests[userId] = new AdminInputRequest { Kind = "set_max_attacks", ExpiresAtMs = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds() };
                await RenderAdminScreen(callback, ($"⚔️ سقف حمله فعلی: {MAX_ATTACKS_PER_UPDATE}\n\nمقدار جدید:\nلغو: لغو", new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("❌ لغو", "adm:cancelinput") } })), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return true;
            }
            if (sub == "maxtransfers")
            {
                adminInputRequests[userId] = new AdminInputRequest { Kind = "set_max_transfers", ExpiresAtMs = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds() };
                await RenderAdminScreen(callback, ($"📦 سقف ترنسفر فعلی: {MAX_TRANSFERS_PER_UPDATE}\n\nمقدار جدید:\nلغو: لغو", new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("❌ لغو", "adm:cancelinput") } })), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return true;
            }
        }

        // ================= LEADERBOARD ACTIONS =================
        if (action == "lb")
        {
            string sub = parts.Length >= 3 ? parts[2] : "";
            if (sub == "now")
            {
                if (!CanAdminAny(userId, "ANN", "SET")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return true; }
                await AnswerAdminCallback(callback, "⏳ در حال ارسال لیدربورد...", false, ct);
                try { await SendNightlyLeaderboards(ct); await AnswerAdminCallback(callback, "✅ لیدربوردها ارسال شد.", false, ct); }
                catch (Exception ex) { await AnswerAdminCallback(callback, $"❌ خطا: {ex.Message}", true, ct); }
                return true;
            }
            if (sub == "topplayers")
            {
                string txt = await BuildTopPlayersManpowerText(ct);
                await RenderAdminScreen(callback, (txt, new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("🔙 بازگشت", "adm:players:home"), InlineKeyboardButton.WithCallbackData("🏠 خانه", "adm:home") } })), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return true;
            }
            if (sub == "topgroups" && parts.Length >= 4)
            {
                string type = parts[3];
                string txt = type == "count" ? await BuildTopGroupsByMembersText(ct) : await BuildTopGroupsByManpowerText(ct);
                await RenderAdminScreen(callback, (txt, new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("🔙 بازگشت", "adm:groups:home"), InlineKeyboardButton.WithCallbackData("🏠 خانه", "adm:home") } })), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return true;
            }
        }

        // ================= BACKUP / MAINTENANCE =================
        if (action == "backup")
        {
            if (!CanAdminAny(userId, "BACKUP", "RESTORE")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return true; }
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
                return true;
            }
            if (sub == "upload")
            {
                adminInputRequests[userId] = new AdminInputRequest { Kind = "awaiting_db_file", ExpiresAtMs = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeMilliseconds() };
                await RenderAdminScreen(callback, ("📤 آپلود بکاپ\n\nفایل gamedata.db را ارسال کنید.\nلغو: لغو", new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("❌ لغو", "adm:cancelinput") } })), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return true;
            }
        }

        if (action == "maintenance" && parts.Length >= 3)
        {
            string sub = parts[2];
            if (sub == "home")
            {
                if (!CanAdminAny(userId, "BACKUP", "RESTORE", "SET")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return true; }
                await RenderAdminScreen(callback, BuildAdminMaintenanceHome(), ct);
                await AnswerAdminCallback(callback, null, false, ct);
                return true;
            }
            if (sub == "cleanup")
            {
                if (!CanAdmin(userId, "SET")) { await AnswerAdminCallback(callback, "⛔ دسترسی ندارید.", true, ct); return true; }
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
                return true;
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
            return true;
        }
        return false;
    }
}
