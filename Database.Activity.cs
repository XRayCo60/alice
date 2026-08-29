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
