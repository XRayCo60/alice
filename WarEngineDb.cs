// ============================================================================
//  WarEngineDb.cs — ثبت نبردها در دیتابیس
// ============================================================================

using System;
using System.Threading;
using Microsoft.Data.Sqlite;

static partial class WarEngine
{
    static int _dbReady;

    static void SaveBattle(Country atk, Country def, BattleResult r)
    {
        try
        {
            using var con = new SqliteConnection("Data Source=gamedata.db");
            con.Open();
            if (Interlocked.CompareExchange(ref _dbReady, 1, 0) == 0)
            {
                using var init = con.CreateCommand();
                init.CommandText = @"
                CREATE TABLE IF NOT EXISTS WarBattles(
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Timestamp TEXT NOT NULL,
                    ChatId INTEGER, AttackerId INTEGER, DefenderId INTEGER,
                    AttackerName TEXT, DefenderName TEXT,
                    Winner TEXT, PenetrationKm REAL, SuccessPercent INTEGER,
                    AtkTankLoss INTEGER, AtkSoldierLoss INTEGER,
                    DefTankLoss INTEGER, DefSoldierLoss INTEGER,
                    LootMoney INTEGER, LootIron INTEGER,
                    DurationMinutes INTEGER, Report TEXT
                );";
                init.ExecuteNonQuery();
            }
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"INSERT INTO WarBattles
                (Timestamp,ChatId,AttackerId,DefenderId,AttackerName,DefenderName,Winner,
                 PenetrationKm,SuccessPercent,AtkTankLoss,AtkSoldierLoss,DefTankLoss,DefSoldierLoss,
                 LootMoney,LootIron,DurationMinutes,Report)
                VALUES (@ts,@chat,@aid,@did,@an,@dn,@w,@pen,@sp,@atl,@asl,@dtl,@dsl,@lm,@li,@dur,@rep)";
            cmd.Parameters.AddWithValue("@ts", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@chat", atk.ChatId);
            cmd.Parameters.AddWithValue("@aid", atk.OwnerId);
            cmd.Parameters.AddWithValue("@did", def.OwnerId);
            cmd.Parameters.AddWithValue("@an", atk.Name);
            cmd.Parameters.AddWithValue("@dn", def.Name);
            cmd.Parameters.AddWithValue("@w", r.AttackerWon ? atk.Name : r.AttackerFailed ? def.Name : $"نسبی {r.SuccessPercent}%");
            cmd.Parameters.AddWithValue("@pen", r.PenetrationKm);
            cmd.Parameters.AddWithValue("@sp", r.SuccessPercent);
            cmd.Parameters.AddWithValue("@atl", r.AttackerTanksLost);
            cmd.Parameters.AddWithValue("@asl", r.AttackerSoldiersLost);
            cmd.Parameters.AddWithValue("@dtl", r.DefenderTanksLost);
            cmd.Parameters.AddWithValue("@dsl", r.DefenderSoldiersLost);
            cmd.Parameters.AddWithValue("@lm", r.AttackerMoneyGained);
            cmd.Parameters.AddWithValue("@li", r.AttackerIronGained);
            cmd.Parameters.AddWithValue("@dur", r.DurationMinutes);
            cmd.Parameters.AddWithValue("@rep", r.AttackerReport ?? "");
            cmd.ExecuteNonQuery();
        }
        catch { }
    }
}
