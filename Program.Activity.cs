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
