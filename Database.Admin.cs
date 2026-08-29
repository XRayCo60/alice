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
    public static void InitAdminPanel(long ownerId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();

        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS AdminUsers(
                AdminId INTEGER PRIMARY KEY,
                DisplayName TEXT NOT NULL DEFAULT '',
                AddedBy INTEGER NOT NULL DEFAULT 0,
                IsOwner INTEGER NOT NULL DEFAULT 0,
                IsActive INTEGER NOT NULL DEFAULT 1,
                CreatedAtMs INTEGER NOT NULL,
                LastSeenMs INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS AdminPermissions(
                AdminId INTEGER NOT NULL,
                PermissionCode TEXT NOT NULL,
                GrantedBy INTEGER NOT NULL,
                GrantedAtMs INTEGER NOT NULL,
                PRIMARY KEY(AdminId, PermissionCode)
            );

            CREATE INDEX IF NOT EXISTS IX_AdminPermissions_AdminId
                ON AdminPermissions(AdminId);

            CREATE TABLE IF NOT EXISTS AdminAudit(
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                AdminId INTEGER NOT NULL,
                Action TEXT NOT NULL,
                TargetType TEXT NOT NULL DEFAULT '',
                TargetId TEXT NOT NULL DEFAULT '',
                Details TEXT NOT NULL DEFAULT '',
                Success INTEGER NOT NULL DEFAULT 1,
                CreatedAtMs INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_AdminAudit_CreatedAtMs
                ON AdminAudit(CreatedAtMs DESC);

            CREATE INDEX IF NOT EXISTS IX_AdminAudit_AdminId
                ON AdminAudit(AdminId, CreatedAtMs DESC);

            CREATE TABLE IF NOT EXISTS AdminPendingActions(
                ActionId TEXT PRIMARY KEY,
                AdminId INTEGER NOT NULL,
                ActionType TEXT NOT NULL,
                TargetType TEXT NOT NULL DEFAULT '',
                TargetId TEXT NOT NULL DEFAULT '',
                Payload TEXT NOT NULL DEFAULT '',
                Stage INTEGER NOT NULL DEFAULT 1,
                ExpiresAtMs INTEGER NOT NULL,
                CreatedAtMs INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_AdminPendingActions_Expires
                ON AdminPendingActions(ExpiresAtMs);

            CREATE TABLE IF NOT EXISTS BannedUsers(
                UserId INTEGER PRIMARY KEY,
                Reason TEXT NOT NULL DEFAULT '',
                BannedBy INTEGER NOT NULL DEFAULT 0,
                BannedAtMs INTEGER NOT NULL
            );
        ";

        cmd.ExecuteNonQuery();

        // Extra migration for BannedUsers on old DBs
        try
        {
            using var mig = con.CreateCommand();
            mig.CommandText = "CREATE TABLE IF NOT EXISTS BannedUsers(UserId INTEGER PRIMARY KEY, Reason TEXT NOT NULL DEFAULT '', BannedBy INTEGER NOT NULL DEFAULT 0, BannedAtMs INTEGER NOT NULL);";
            mig.ExecuteNonQuery();
        }
        catch { }

        long nowMs =
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        using var ownerCmd = con.CreateCommand();

        ownerCmd.CommandText = @"
            INSERT INTO AdminUsers(
                AdminId,
                DisplayName,
                AddedBy,
                IsOwner,
                IsActive,
                CreatedAtMs,
                LastSeenMs
            )
            VALUES(
                $adminId,
                'مالک اصلی آلیس',
                $adminId,
                1,
                1,
                $nowMs,
                $nowMs
            )
            ON CONFLICT(AdminId) DO UPDATE SET
                IsOwner = 1,
                IsActive = 1,
                DisplayName =
                    CASE
                        WHEN AdminUsers.DisplayName = ''
                        THEN 'مالک اصلی آلیس'
                        ELSE AdminUsers.DisplayName
                    END;
        ";

        ownerCmd.Parameters.AddWithValue(
            "$adminId",
            ownerId
        );

        ownerCmd.Parameters.AddWithValue(
            "$nowMs",
            nowMs
        );

        ownerCmd.ExecuteNonQuery();

        CleanupExpiredAdminActions();
    }

    public static bool IsAdminActive(long adminId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();

        cmd.CommandText = @"
            SELECT COUNT(*)
            FROM AdminUsers
            WHERE AdminId = $adminId
              AND IsActive = 1;
        ";

        cmd.Parameters.AddWithValue(
            "$adminId",
            adminId
        );

        return Convert.ToInt64(
            cmd.ExecuteScalar()
        ) > 0;
    }

    public static AdminAccount? GetAdmin(long adminId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();

        cmd.CommandText = @"
            SELECT
                AdminId,
                DisplayName,
                AddedBy,
                IsOwner,
                IsActive,
                CreatedAtMs,
                LastSeenMs
            FROM AdminUsers
            WHERE AdminId = $adminId;
        ";

        cmd.Parameters.AddWithValue(
            "$adminId",
            adminId
        );

        using var reader = cmd.ExecuteReader();

        if (!reader.Read())
            return null;

        return ReadAdminAccount(reader);
    }

    public static List<AdminAccount> GetAdmins()
    {
        var result = new List<AdminAccount>();

        using var con = OpenCon();
        using var cmd = con.CreateCommand();

        cmd.CommandText = @"
            SELECT
                AdminId,
                DisplayName,
                AddedBy,
                IsOwner,
                IsActive,
                CreatedAtMs,
                LastSeenMs
            FROM AdminUsers
            ORDER BY
                IsOwner DESC,
                IsActive DESC,
                CreatedAtMs DESC;
        ";

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
            result.Add(ReadAdminAccount(reader));

        return result;
    }

    private static AdminAccount ReadAdminAccount(
        SqliteDataReader reader)
    {
        return new AdminAccount
        {
            AdminId = reader.GetInt64(0),
            DisplayName = reader.GetString(1),
            AddedBy = reader.GetInt64(2),
            IsOwner = reader.GetInt64(3) != 0,
            IsActive = reader.GetInt64(4) != 0,
            CreatedAtMs = reader.GetInt64(5),
            LastSeenMs = reader.GetInt64(6)
        };
    }

    public static void AddOrReactivateAdmin(
        long adminId,
        string displayName,
        long addedBy)
    {
        long nowMs =
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        using var con = OpenCon();
        using var cmd = con.CreateCommand();

        cmd.CommandText = @"
            INSERT INTO AdminUsers(
                AdminId,
                DisplayName,
                AddedBy,
                IsOwner,
                IsActive,
                CreatedAtMs,
                LastSeenMs
            )
            VALUES(
                $adminId,
                $displayName,
                $addedBy,
                0,
                1,
                $nowMs,
                0
            )
            ON CONFLICT(AdminId) DO UPDATE SET
                DisplayName = excluded.DisplayName,
                AddedBy = excluded.AddedBy,
                IsActive = 1;
        ";

        cmd.Parameters.AddWithValue(
            "$adminId",
            adminId
        );

        cmd.Parameters.AddWithValue(
            "$displayName",
            displayName
        );

        cmd.Parameters.AddWithValue(
            "$addedBy",
            addedBy
        );

        cmd.Parameters.AddWithValue(
            "$nowMs",
            nowMs
        );

        cmd.ExecuteNonQuery();
    }

    public static void SetAdminActive(
        long adminId,
        bool active)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();

        cmd.CommandText = @"
            UPDATE AdminUsers
            SET IsActive = $active
            WHERE AdminId = $adminId
              AND IsOwner = 0;
        ";

        cmd.Parameters.AddWithValue(
            "$active",
            active ? 1 : 0
        );

        cmd.Parameters.AddWithValue(
            "$adminId",
            adminId
        );

        cmd.ExecuteNonQuery();
    }

    public static bool DeleteAdmin(long adminId)
    {
        using var con = OpenCon();
        using var transaction = con.BeginTransaction();

        using var check = con.CreateCommand();
        check.Transaction = transaction;

        check.CommandText = @"
            SELECT IsOwner
            FROM AdminUsers
            WHERE AdminId = $adminId;
        ";

        check.Parameters.AddWithValue(
            "$adminId",
            adminId
        );

        object? value = check.ExecuteScalar();

        if (value == null ||
            value == DBNull.Value ||
            Convert.ToInt64(value) != 0)
        {
            transaction.Rollback();
            return false;
        }

        using var permissions = con.CreateCommand();
        permissions.Transaction = transaction;

        permissions.CommandText = @"
            DELETE FROM AdminPermissions
            WHERE AdminId = $adminId;
        ";

        permissions.Parameters.AddWithValue(
            "$adminId",
            adminId
        );

        permissions.ExecuteNonQuery();

        using var admin = con.CreateCommand();
        admin.Transaction = transaction;

        admin.CommandText = @"
            DELETE FROM AdminUsers
            WHERE AdminId = $adminId
              AND IsOwner = 0;
        ";

        admin.Parameters.AddWithValue(
            "$adminId",
            adminId
        );

        int affected = admin.ExecuteNonQuery();

        transaction.Commit();
        return affected > 0;
    }

    public static void TouchAdmin(long adminId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();

        cmd.CommandText = @"
            UPDATE AdminUsers
            SET LastSeenMs = $nowMs
            WHERE AdminId = $adminId;
        ";

        cmd.Parameters.AddWithValue(
            "$nowMs",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );

        cmd.Parameters.AddWithValue(
            "$adminId",
            adminId
        );

        cmd.ExecuteNonQuery();
    }

    public static HashSet<string> GetAdminPermissions(
        long adminId)
    {
        var result =
            new HashSet<string>(
                StringComparer.Ordinal
            );

        using var con = OpenCon();
        using var cmd = con.CreateCommand();

        cmd.CommandText = @"
            SELECT PermissionCode
            FROM AdminPermissions
            WHERE AdminId = $adminId;
        ";

        cmd.Parameters.AddWithValue(
            "$adminId",
            adminId
        );

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
            result.Add(reader.GetString(0));

        return result;
    }

    public static bool HasAdminPermission(
        long adminId,
        string permissionCode)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();

        cmd.CommandText = @"
            SELECT COUNT(*)
            FROM AdminPermissions
            WHERE AdminId = $adminId
              AND PermissionCode = $permissionCode;
        ";

        cmd.Parameters.AddWithValue(
            "$adminId",
            adminId
        );

        cmd.Parameters.AddWithValue(
            "$permissionCode",
            permissionCode
        );

        return Convert.ToInt64(
            cmd.ExecuteScalar()
        ) > 0;
    }

    public static void SetAdminPermission(
        long adminId,
        string permissionCode,
        bool enabled,
        long grantedBy)
    {
        if (!AdminPermissionCatalog.Exists(permissionCode))
            throw new ArgumentException(
                "Unknown permission.",
                nameof(permissionCode)
            );

        using var con = OpenCon();
        using var cmd = con.CreateCommand();

        if (enabled)
        {
            cmd.CommandText = @"
                INSERT INTO AdminPermissions(
                    AdminId,
                    PermissionCode,
                    GrantedBy,
                    GrantedAtMs
                )
                VALUES(
                    $adminId,
                    $permissionCode,
                    $grantedBy,
                    $nowMs
                )
                ON CONFLICT(
                    AdminId,
                    PermissionCode
                ) DO UPDATE SET
                    GrantedBy = excluded.GrantedBy,
                    GrantedAtMs = excluded.GrantedAtMs;
            ";

            cmd.Parameters.AddWithValue(
                "$grantedBy",
                grantedBy
            );

            cmd.Parameters.AddWithValue(
                "$nowMs",
                DateTimeOffset.UtcNow
                    .ToUnixTimeMilliseconds()
            );
        }
        else
        {
            cmd.CommandText = @"
                DELETE FROM AdminPermissions
                WHERE AdminId = $adminId
                  AND PermissionCode = $permissionCode;
            ";
        }

        cmd.Parameters.AddWithValue(
            "$adminId",
            adminId
        );

        cmd.Parameters.AddWithValue(
            "$permissionCode",
            permissionCode
        );

        cmd.ExecuteNonQuery();
    }

    public static void SetAllAdminPermissions(
        long adminId,
        bool enabled,
        long grantedBy)
    {
        using var con = OpenCon();
        using var transaction = con.BeginTransaction();

        using var clear = con.CreateCommand();
        clear.Transaction = transaction;

        clear.CommandText = @"
            DELETE FROM AdminPermissions
            WHERE AdminId = $adminId;
        ";

        clear.Parameters.AddWithValue(
            "$adminId",
            adminId
        );

        clear.ExecuteNonQuery();

        if (enabled)
        {
            long nowMs =
                DateTimeOffset.UtcNow
                    .ToUnixTimeMilliseconds();

            foreach (var permission in
                     AdminPermissionCatalog.All)
            {
                using var insert = con.CreateCommand();
                insert.Transaction = transaction;

                insert.CommandText = @"
                    INSERT INTO AdminPermissions(
                        AdminId,
                        PermissionCode,
                        GrantedBy,
                        GrantedAtMs
                    )
                    VALUES(
                        $adminId,
                        $code,
                        $grantedBy,
                        $nowMs
                    );
                ";

                insert.Parameters.AddWithValue(
                    "$adminId",
                    adminId
                );

                insert.Parameters.AddWithValue(
                    "$code",
                    permission.Code
                );

                insert.Parameters.AddWithValue(
                    "$grantedBy",
                    grantedBy
                );

                insert.Parameters.AddWithValue(
                    "$nowMs",
                    nowMs
                );

                insert.ExecuteNonQuery();
            }
        }

        transaction.Commit();
    }

    public static void WriteAdminAudit(
        long adminId,
        string action,
        string targetType = "",
        string targetId = "",
        string details = "",
        bool success = true)
    {
        try
        {
            using var con = OpenCon();
            using var cmd = con.CreateCommand();

            cmd.CommandText = @"
                INSERT INTO AdminAudit(
                    AdminId,
                    Action,
                    TargetType,
                    TargetId,
                    Details,
                    Success,
                    CreatedAtMs
                )
                VALUES(
                    $adminId,
                    $action,
                    $targetType,
                    $targetId,
                    $details,
                    $success,
                    $nowMs
                );
            ";

            cmd.Parameters.AddWithValue(
                "$adminId",
                adminId
            );

            cmd.Parameters.AddWithValue(
                "$action",
                action
            );

            cmd.Parameters.AddWithValue(
                "$targetType",
                targetType
            );

            cmd.Parameters.AddWithValue(
                "$targetId",
                targetId
            );

            cmd.Parameters.AddWithValue(
                "$details",
                details
            );

            cmd.Parameters.AddWithValue(
                "$success",
                success ? 1 : 0
            );

            cmd.Parameters.AddWithValue(
                "$nowMs",
                DateTimeOffset.UtcNow
                    .ToUnixTimeMilliseconds()
            );

            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[ADMIN AUDIT ERR] {ex.Message}"
            );
        }
    }

    public static List<AdminAuditEntry> GetRecentAdminAudit(
        int limit = 20)
    {
        limit = Math.Clamp(limit, 1, 100);

        var result = new List<AdminAuditEntry>();

        using var con = OpenCon();
        using var cmd = con.CreateCommand();

        cmd.CommandText = @"
            SELECT
                Id,
                AdminId,
                Action,
                TargetType,
                TargetId,
                Details,
                Success,
                CreatedAtMs
            FROM AdminAudit
            ORDER BY CreatedAtMs DESC
            LIMIT $limit;
        ";

        cmd.Parameters.AddWithValue(
            "$limit",
            limit
        );

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            result.Add(new AdminAuditEntry
            {
                Id = reader.GetInt64(0),
                AdminId = reader.GetInt64(1),
                Action = reader.GetString(2),
                TargetType = reader.GetString(3),
                TargetId = reader.GetString(4),
                Details = reader.GetString(5),
                Success = reader.GetInt64(6) != 0,
                CreatedAtMs = reader.GetInt64(7)
            });
        }

        return result;
    }

    public static AdminDashboardStats GetAdminDashboardStats()
    {
        long nowMs =
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        using var con = OpenCon();
        using var cmd = con.CreateCommand();

        cmd.CommandText = @"
            SELECT
                (SELECT COUNT(*) FROM Countries),
                (
                    SELECT COUNT(DISTINCT OwnerId)
                    FROM Countries
                ),
                (
                    SELECT COUNT(DISTINCT ChatId)
                    FROM Countries
                ),
                (SELECT COUNT(*) FROM Alliances),
                (
                    SELECT COUNT(*)
                    FROM Transfers
                    WHERE ArriveAtMs > $nowMs
                ),
                (
                    SELECT COUNT(*)
                    FROM Deployments
                    WHERE EndAtMs > $nowMs
                ),
                (
                    SELECT COUNT(*)
                    FROM AdminUsers
                    WHERE IsActive = 1
                ),
                (SELECT COUNT(*) FROM AdminAudit);
        ";

        cmd.Parameters.AddWithValue(
            "$nowMs",
            nowMs
        );

        using var reader = cmd.ExecuteReader();

        if (!reader.Read())
            return new AdminDashboardStats();

        return new AdminDashboardStats
        {
            Countries =
                Convert.ToInt32(reader.GetInt64(0)),
            Players =
                Convert.ToInt32(reader.GetInt64(1)),
            Groups =
                Convert.ToInt32(reader.GetInt64(2)),
            Alliances =
                Convert.ToInt32(reader.GetInt64(3)),
            ActiveTransfers =
                Convert.ToInt32(reader.GetInt64(4)),
            ActiveDeployments =
                Convert.ToInt32(reader.GetInt64(5)),
            ActiveAdmins =
                Convert.ToInt32(reader.GetInt64(6)),
            AuditEntries =
                Convert.ToInt32(reader.GetInt64(7))
        };
    }

    public static void CleanupExpiredAdminActions()
    {
        try
        {
            using var con = OpenCon();
            using var cmd = con.CreateCommand();

            cmd.CommandText = @"
                DELETE FROM AdminPendingActions
                WHERE ExpiresAtMs <= $nowMs;
            ";

            cmd.Parameters.AddWithValue(
                "$nowMs",
                DateTimeOffset.UtcNow
                    .ToUnixTimeMilliseconds()
            );

            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[ADMIN ACTION CLEANUP ERR] {ex.Message}"
            );
        }
    }

    // ================= NEW ADMIN HELPERS =================
    public static List<Country> GetCountriesByOwnerId(long ownerId)
    {
        var list = new List<Country>();
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = $"SELECT {COUNTRY_COLS} FROM Countries WHERE OwnerId=@oid";
        cmd.Parameters.AddWithValue("@oid", ownerId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadCountry(r));
        return list;
    }

    public static List<Country> SearchCountriesByName(string namePart, int limit = 20)
    {
        var list = new List<Country>();
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = $"SELECT {COUNTRY_COLS} FROM Countries WHERE Name LIKE @pat LIMIT @lim";
        cmd.Parameters.AddWithValue("@pat", $"%{namePart}%");
        cmd.Parameters.AddWithValue("@lim", limit);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadCountry(r));
        return list;
    }

    public static List<Country> GetSiegedCountries(int limit = 20)
    {
        var list = new List<Country>();
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = $"SELECT {COUNTRY_COLS} FROM Countries WHERE Besieged>0 ORDER BY Besieged DESC LIMIT @lim";
        cmd.Parameters.AddWithValue("@lim", limit);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadCountry(r));
        return list;
    }

    public static void BanUser(long userId, string reason, long bannedBy)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO BannedUsers(UserId, Reason, BannedBy, BannedAtMs) VALUES(@uid, @reason, @by, @ms)";
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.Parameters.AddWithValue("@reason", reason);
        cmd.Parameters.AddWithValue("@by", bannedBy);
        cmd.Parameters.AddWithValue("@ms", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        cmd.ExecuteNonQuery();

        // Delete all countries of this user
        using var del = con.CreateCommand();
        del.CommandText = "DELETE FROM Countries WHERE OwnerId=@uid";
        del.Parameters.AddWithValue("@uid", userId);
        del.ExecuteNonQuery();
    }

    public static void UnbanUser(long userId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "DELETE FROM BannedUsers WHERE UserId=@uid";
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.ExecuteNonQuery();
    }

    public static bool IsUserBanned(long userId)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM BannedUsers WHERE UserId=@uid LIMIT 1";
        cmd.Parameters.AddWithValue("@uid", userId);
        using var r = cmd.ExecuteReader();
        return r.Read();
    }

    public static List<(long UserId, string Reason, long BannedBy, long BannedAtMs)> GetBannedUsers(int limit = 50)
    {
        var list = new List<(long, string, long, long)>();
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT UserId, Reason, BannedBy, BannedAtMs FROM BannedUsers ORDER BY BannedAtMs DESC LIMIT @lim";
        cmd.Parameters.AddWithValue("@lim", limit);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add((r.GetInt64(0), r.GetString(1), r.GetInt64(2), r.GetInt64(3)));
        }
        return list;
    }

    public static List<(long Id, string Timestamp, long ChatId, long AttackerId, long DefenderId, string AttackerName, string DefenderName, string Winner, double Penetration, int SuccessPercent)> GetRecentBattles(int limit = 20)
    {
        var list = new List<(long, string, long, long, long, string, string, string, double, int)>();
        try
        {
            using var con = OpenCon();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT Id, Timestamp, ChatId, AttackerId, DefenderId, AttackerName, DefenderName, Winner, PenetrationKm, SuccessPercent FROM WarBattles ORDER BY Id DESC LIMIT @lim";
            cmd.Parameters.AddWithValue("@lim", limit);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add((
                    r.GetInt64(0),
                    r.GetString(1),
                    r.GetInt64(2),
                    r.GetInt64(3),
                    r.GetInt64(4),
                    r.GetString(5),
                    r.GetString(6),
                    r.GetString(7),
                    r.IsDBNull(8) ? 0 : r.GetDouble(8),
                    r.IsDBNull(9) ? 0 : r.GetInt32(9)
                ));
            }
        }
        catch { }
        return list;
    }
}
