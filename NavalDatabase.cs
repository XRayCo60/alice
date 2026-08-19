using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;

static partial class Database
{
    public static void InitNavalV2()
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS NavalDefenseOrders(
 OwnerId INTEGER NOT NULL, ChatId INTEGER NOT NULL, Strategy INTEGER NOT NULL DEFAULT 1,
 Tactic INTEGER NOT NULL DEFAULT 1, PRIMARY KEY(OwnerId,ChatId));
CREATE TABLE IF NOT EXISTS NavalDefenseModels(
 OwnerId INTEGER NOT NULL, ChatId INTEGER NOT NULL, Category TEXT NOT NULL,
 ModelName TEXT NOT NULL, Count INTEGER NOT NULL DEFAULT 0,
 PRIMARY KEY(OwnerId,ChatId,Category,ModelName));
CREATE TABLE IF NOT EXISTS BattleshipUnits(
 Id INTEGER PRIMARY KEY AUTOINCREMENT, OwnerId INTEGER NOT NULL, ChatId INTEGER NOT NULL,
 ModelName TEXT NOT NULL, DamagePercent INTEGER NOT NULL DEFAULT 0,
 OperationId INTEGER NULL, Status TEXT NOT NULL DEFAULT 'Ready');
CREATE TABLE IF NOT EXISTS TransferBattleships(
 TransferId INTEGER NOT NULL, UnitId INTEGER NOT NULL UNIQUE,
 PRIMARY KEY(TransferId,UnitId));
CREATE TABLE IF NOT EXISTS NavalRivalryWins(
 AttackerId INTEGER NOT NULL, DefenderId INTEGER NOT NULL, ChatId INTEGER NOT NULL,
 Wins INTEGER NOT NULL DEFAULT 0, PRIMARY KEY(AttackerId,DefenderId,ChatId));
CREATE TABLE IF NOT EXISTS NavalBattleHistory(
 Id INTEGER PRIMARY KEY AUTOINCREMENT, OperationId INTEGER NOT NULL UNIQUE,
 Timestamp TEXT NOT NULL, ChatId INTEGER NOT NULL, AttackerId INTEGER NOT NULL,
 DefenderId INTEGER NOT NULL, Outcome TEXT NOT NULL, SuccessPercent INTEGER NOT NULL,
 LootMoney INTEGER NOT NULL DEFAULT 0, LootIron INTEGER NOT NULL DEFAULT 0,
 Report TEXT NOT NULL DEFAULT '');
CREATE INDEX IF NOT EXISTS IX_BattleshipUnits_Owner ON BattleshipUnits(OwnerId,ChatId,Status);
CREATE TRIGGER IF NOT EXISTS TR_Countries_BattleshipCap_Update
BEFORE UPDATE OF Battleships,BattleshipsAtSea ON Countries
WHEN (NEW.Battleships+NEW.BattleshipsAtSea)>3
 AND (NEW.Battleships+NEW.BattleshipsAtSea)>(OLD.Battleships+OLD.BattleshipsAtSea)
BEGIN SELECT RAISE(ABORT,'battleship capacity exceeds 3'); END;
CREATE TRIGGER IF NOT EXISTS TR_BattleshipUnits_DamageRange
BEFORE UPDATE OF DamagePercent ON BattleshipUnits
WHEN NEW.DamagePercent<0 OR NEW.DamagePercent>100
BEGIN SELECT RAISE(ABORT,'battleship damage outside 0..100'); END;
";
        cmd.ExecuteNonQuery();
        EnsureColumn(con, "NavalInvasions", "ScenarioSeed", "INTEGER DEFAULT 0");
        EnsureColumn(con, "NavalInvasions", "ResultJson", "TEXT DEFAULT ''");
        EnsureColumn(con, "NavalInvasions", "Status", "TEXT DEFAULT 'Pending'");
        foreach (var c in GetAllCountries()) SyncBattleshipUnits(c.OwnerId, c.ChatId);
    }

    public static bool TryPurchaseBattleship(long ownerId,long chatId,string model,long moneyCost,long ironCost)
    {
        using var con=OpenCon();using var tx=con.BeginTransaction();
        long used;
        using(var capacity=con.CreateCommand())
        {
            capacity.Transaction=tx;capacity.CommandText=@"SELECT Battleships+BattleshipsAtSea+
 COALESCE((SELECT SUM(Amount) FROM Transfers WHERE ReceiverId=@o AND ChatId=@c AND ResourceType='battleships'),0)
 FROM Countries WHERE OwnerId=@o AND ChatId=@c AND PortLevel>=4";
            capacity.Parameters.AddWithValue("@o",ownerId);capacity.Parameters.AddWithValue("@c",chatId);
            object? value=capacity.ExecuteScalar();if(value==null||value==DBNull.Value)return false;used=Convert.ToInt64(value);
        }
        if(used>=3)return false;
        using(var country=con.CreateCommand())
        {
            country.Transaction=tx;country.CommandText=@"UPDATE Countries SET Money=Money-@money,Iron=Iron-@iron,Battleships=Battleships+1
 WHERE OwnerId=@o AND ChatId=@c AND PortLevel>=4 AND Money>=@money AND Iron>=@iron";
            country.Parameters.AddWithValue("@money",moneyCost);country.Parameters.AddWithValue("@iron",ironCost);
            country.Parameters.AddWithValue("@o",ownerId);country.Parameters.AddWithValue("@c",chatId);
            if(country.ExecuteNonQuery()!=1)return false;
        }
        using(var equipment=con.CreateCommand())
        {
            equipment.Transaction=tx;equipment.CommandText=@"INSERT INTO EquipmentModels(OwnerId,ChatId,Category,ModelName,Count)
 VALUES(@o,@c,'Battleships',@model,1) ON CONFLICT(OwnerId,ChatId,Category,ModelName) DO UPDATE SET Count=Count+1";
            equipment.Parameters.AddWithValue("@o",ownerId);equipment.Parameters.AddWithValue("@c",chatId);equipment.Parameters.AddWithValue("@model",model);equipment.ExecuteNonQuery();
        }
        using(var unit=con.CreateCommand())
        {
            unit.Transaction=tx;unit.CommandText=@"INSERT INTO BattleshipUnits(OwnerId,ChatId,ModelName,DamagePercent,Status)
 VALUES(@o,@c,@model,0,'Ready')";unit.Parameters.AddWithValue("@o",ownerId);unit.Parameters.AddWithValue("@c",chatId);
            unit.Parameters.AddWithValue("@model",model);unit.ExecuteNonQuery();
        }
        tx.Commit();return true;
    }

    public static void RegisterBattleshipUnit(long ownerId, long chatId, string model)
    {
        using var con = OpenCon();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"INSERT INTO BattleshipUnits(OwnerId,ChatId,ModelName,DamagePercent,Status)
                            VALUES(@owner,@chat,@model,0,'Ready')";
        cmd.Parameters.AddWithValue("@owner", ownerId);
        cmd.Parameters.AddWithValue("@chat", chatId);
        cmd.Parameters.AddWithValue("@model", model);
        cmd.ExecuteNonQuery();
    }

    public static void SyncBattleshipUnits(long ownerId, long chatId)
    {
        var c = GetCountry(ownerId, chatId); if (c == null) return;
        var desired = GetEquipmentBreakdownForReconcile(c, "battleships")
            .ToDictionary(x => x.ModelName, x => x.Count, StringComparer.OrdinalIgnoreCase);
        using var con = OpenCon();
        using var transaction = con.BeginTransaction();
        foreach (var item in desired)
        {
            long have;
            using (var count = con.CreateCommand())
            {
                count.Transaction = transaction;
                count.CommandText = @"SELECT COUNT(*) FROM BattleshipUnits
                                      WHERE OwnerId=@owner AND ChatId=@chat AND ModelName=@model
                                        AND Status!='Sunk'";
                count.Parameters.AddWithValue("@owner", ownerId);
                count.Parameters.AddWithValue("@chat", chatId);
                count.Parameters.AddWithValue("@model", item.Key);
                have = Convert.ToInt64(count.ExecuteScalar());
            }
            for (long i = have; i < item.Value; i++)
            {
                using var insert = con.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = @"INSERT INTO BattleshipUnits
                    (OwnerId,ChatId,ModelName,DamagePercent,OperationId,Status)
                    VALUES(@owner,@chat,@model,0,NULL,'Ready')";
                insert.Parameters.AddWithValue("@owner", ownerId);
                insert.Parameters.AddWithValue("@chat", chatId);
                insert.Parameters.AddWithValue("@model", item.Key);
                insert.ExecuteNonQuery();
            }
        }

        var units = new List<(long Id,int Damage)>();
        using (var readUnits = con.CreateCommand())
        {
            readUnits.Transaction = transaction;
            readUnits.CommandText = @"SELECT Id,DamagePercent FROM BattleshipUnits
                WHERE OwnerId=@owner AND ChatId=@chat AND Status!='Sunk' ORDER BY Id";
            readUnits.Parameters.AddWithValue("@owner", ownerId);
            readUnits.Parameters.AddWithValue("@chat", chatId);
            using var reader = readUnits.ExecuteReader();
            while (reader.Read()) units.Add((reader.GetInt64(0), reader.GetInt32(1)));
        }
        int actualDamage = units.Sum(x => Math.Clamp(x.Damage,0,80));
        // One-time migration from the old aggregate column. Unit rows are authoritative
        // afterwards; migration only runs when every unit is still at zero damage.
        if (units.Count > 0 && actualDamage == 0 && c.BattleshipDamage > 0)
        {
            int distributable = (int)Math.Min(c.BattleshipDamage, units.Count * 80L);
            int each = distributable / units.Count, remainder = distributable % units.Count;
            for (int i = 0; i < units.Count; i++)
            {
                int damage = each + (i < remainder ? 1 : 0);
                using var migrate = con.CreateCommand();
                migrate.Transaction = transaction;
                migrate.CommandText = "UPDATE BattleshipUnits SET DamagePercent=@damage WHERE Id=@id";
                migrate.Parameters.AddWithValue("@damage", damage);
                migrate.Parameters.AddWithValue("@id", units[i].Id);
                migrate.ExecuteNonQuery();
            }
            actualDamage = distributable;
        }
        using (var syncLegacy = con.CreateCommand())
        {
            syncLegacy.Transaction = transaction;
            syncLegacy.CommandText = "UPDATE Countries SET BattleshipDamage=@damage WHERE OwnerId=@owner AND ChatId=@chat";
            syncLegacy.Parameters.AddWithValue("@damage", actualDamage);
            syncLegacy.Parameters.AddWithValue("@owner", ownerId);
            syncLegacy.Parameters.AddWithValue("@chat", chatId);
            syncLegacy.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public static List<NavalBattleshipState> GetBattleshipUnits(long ownerId, long chatId,
        bool onlyCombatReady, long? operationId = null)
    {
        var list = new List<NavalBattleshipState>();
        using var con = OpenCon(); using var cmd = con.CreateCommand();
        cmd.CommandText = @"SELECT Id,ModelName,DamagePercent FROM BattleshipUnits
                            WHERE OwnerId=@owner AND ChatId=@chat
                              AND ((@operation IS NOT NULL AND OperationId=@operation AND Status='AtSea')
                                OR (@operation IS NULL AND OperationId IS NULL AND Status='Ready'))" +
                          (onlyCombatReady ? " AND DamagePercent<=50" : "") + " ORDER BY Id";
        cmd.Parameters.AddWithValue("@owner", ownerId); cmd.Parameters.AddWithValue("@chat", chatId);
        cmd.Parameters.AddWithValue("@operation", operationId.HasValue ? (object)operationId.Value : DBNull.Value);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new NavalBattleshipState
            { UnitId = r.GetInt64(0), Model = r.GetString(1), DamagePercent = r.GetInt32(2) });
        return list;
    }

    public static (int Strategy, int Tactic) GetNavalDefenseOrders(long ownerId, long chatId)
    {
        using var con = OpenCon(); using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT Strategy,Tactic FROM NavalDefenseOrders WHERE OwnerId=@o AND ChatId=@c";
        cmd.Parameters.AddWithValue("@o", ownerId); cmd.Parameters.AddWithValue("@c", chatId);
        using var r = cmd.ExecuteReader(); return r.Read() ? (r.GetInt32(0), r.GetInt32(1)) : (1, 1);
    }

    public static void SetNavalDefenseOrders(long ownerId, long chatId, int strategy, int tactic)
    {
        using var con = OpenCon(); using var cmd = con.CreateCommand();
        cmd.CommandText = @"INSERT INTO NavalDefenseOrders(OwnerId,ChatId,Strategy,Tactic)
 VALUES(@o,@c,@s,@t) ON CONFLICT(OwnerId,ChatId) DO UPDATE SET Strategy=@s,Tactic=@t";
        cmd.Parameters.AddWithValue("@o", ownerId); cmd.Parameters.AddWithValue("@c", chatId);
        cmd.Parameters.AddWithValue("@s", Math.Clamp(strategy,1,2)); cmd.Parameters.AddWithValue("@t", Math.Clamp(tactic,1,2));
        cmd.ExecuteNonQuery();
    }

    public static void ReplaceNavalDefenseModels(long ownerId,long chatId,string category,
        IReadOnlyDictionary<string,long> amounts)
    {
        using var con=OpenCon();using var tx=con.BeginTransaction();
        using(var del=con.CreateCommand()){del.Transaction=tx;del.CommandText="DELETE FROM NavalDefenseModels WHERE OwnerId=@o AND ChatId=@c AND Category=@cat";
            del.Parameters.AddWithValue("@o",ownerId);del.Parameters.AddWithValue("@c",chatId);del.Parameters.AddWithValue("@cat",category);del.ExecuteNonQuery();}
        foreach(var x in amounts.Where(x=>x.Value>0)){using var ins=con.CreateCommand();ins.Transaction=tx;
            ins.CommandText="INSERT INTO NavalDefenseModels(OwnerId,ChatId,Category,ModelName,Count) VALUES(@o,@c,@cat,@m,@n)";
            ins.Parameters.AddWithValue("@o",ownerId);ins.Parameters.AddWithValue("@c",chatId);ins.Parameters.AddWithValue("@cat",category);
            ins.Parameters.AddWithValue("@m",x.Key);ins.Parameters.AddWithValue("@n",x.Value);ins.ExecuteNonQuery();}
        tx.Commit();
    }

    public static List<NavalModelAmount> GetNavalDefenseModels(Country c, string resourceType)
    {
        string category = resourceType switch { "boats" => "Boats", "submarines" => "Submarines", _ => "Battleships" };
        string defaultModel = resourceType switch
        {
            "boats" => GetDefaultBoatModel(c.Faction), "submarines" => GetDefaultSubModel(c.Faction),
            _ => GetDefaultBattleshipModel(c.Faction)
        };
        var inventory = GetEquipmentBreakdownForReconcile(c, resourceType);
        if(resourceType=="battleships")
        {
            var ready=GetBattleshipUnits(c.OwnerId,c.ChatId,true)
                .GroupBy(x=>x.Model,StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x=>x.Key,x=>(long)x.Count(),StringComparer.OrdinalIgnoreCase);
            inventory=inventory.Select(x=>(x.ModelName,Count:Math.Min(x.Count,ready.GetValueOrDefault(x.ModelName))))
                .Where(x=>x.Count>0).ToList();
        }
        long mandatory = (long)Math.Ceiling(inventory.Sum(x => x.Count) * 0.20);
        var saved = new Dictionary<string,long>(StringComparer.OrdinalIgnoreCase);
        using (var con = OpenCon()) using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = @"SELECT ModelName,Count FROM NavalDefenseModels
                                WHERE OwnerId=@o AND ChatId=@c AND Category=@cat";
            cmd.Parameters.AddWithValue("@o", c.OwnerId); cmd.Parameters.AddWithValue("@c", c.ChatId);
            cmd.Parameters.AddWithValue("@cat", category); using var r = cmd.ExecuteReader();
            while (r.Read()) saved[r.GetString(0)] = r.GetInt64(1);
        }
        var chosen = inventory.Select(x => Math.Min(x.Count, saved.GetValueOrDefault(x.ModelName))).ToArray();
        if (saved.Count == 0 || chosen.Sum() < mandatory)
        {
            Array.Clear(chosen); long left = mandatory;
            foreach (int i in Enumerable.Range(0, inventory.Count)
                         .OrderBy(i => inventory[i].ModelName.Equals(defaultModel, StringComparison.OrdinalIgnoreCase) ? 0 : 1))
            { long take = Math.Min(left, inventory[i].Count); chosen[i] = take; left -= take; if (left == 0) break; }
        }
        return inventory.Select((x,i) => new NavalModelAmount(x.ModelName, chosen[i]))
            .Where(x => x.Count > 0).ToList();
    }

    public static List<NavalModelAmount> GetNavalTransferableModels(Country c, string resourceType)
    {
        if(resourceType!="battleships")return GetNavalAttackableModels(c,resourceType);
        var inventory=GetEquipmentBreakdownForReconcile(c,"battleships");
        var defense=GetNavalDefenseModels(c,"battleships")
            .ToDictionary(x=>x.Model,x=>x.Count,StringComparer.OrdinalIgnoreCase);
        var ready=GetBattleshipUnits(c.OwnerId,c.ChatId,false)
            .GroupBy(x=>x.Model,StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x=>x.Key,x=>(long)x.Count(),StringComparer.OrdinalIgnoreCase);
        return inventory.Select(x=>new NavalModelAmount(x.ModelName,
                Math.Max(0,Math.Min(x.Count,ready.GetValueOrDefault(x.ModelName))-defense.GetValueOrDefault(x.ModelName))))
            .Where(x=>x.Count>0).ToList();
    }

    public static List<NavalModelAmount> GetNavalAttackableModels(Country c, string resourceType)
    {
        var inventory = GetEquipmentBreakdownForReconcile(c, resourceType);
        var defense = GetNavalDefenseModels(c, resourceType)
            .ToDictionary(x => x.Model, x => x.Count, StringComparer.OrdinalIgnoreCase);
        if (resourceType == "battleships")
        {
            var ready = GetBattleshipUnits(c.OwnerId, c.ChatId, true)
                .GroupBy(x => x.Model, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => (long)x.Count(), StringComparer.OrdinalIgnoreCase);
            return inventory.Select(x => new NavalModelAmount(x.ModelName,
                    Math.Max(0, Math.Min(x.Count, ready.GetValueOrDefault(x.ModelName)) - defense.GetValueOrDefault(x.ModelName))))
                .Where(x => x.Count > 0).ToList();
        }
        return inventory.Select(x => new NavalModelAmount(x.ModelName,
                Math.Max(0, x.Count - defense.GetValueOrDefault(x.ModelName))))
            .Where(x => x.Count > 0).ToList();
    }

    public static long CreateNavalOperation(Country attacker, Country defender,
        IReadOnlyList<NavalModelAmount> boats, IReadOnlyList<NavalModelAmount> subs,
        IReadOnlyList<NavalModelAmount> battleships, int tactic, long nowMs, int travelMinutes)
    {
        bool Fits(IReadOnlyList<NavalModelAmount> selected,string resource)
        {
            var available=GetNavalAttackableModels(attacker,resource)
                .ToDictionary(x=>x.Model,x=>x.Count,StringComparer.OrdinalIgnoreCase);
            return selected.GroupBy(x=>x.Model,StringComparer.OrdinalIgnoreCase)
                .All(x=>x.Sum(y=>y.Count)<=available.GetValueOrDefault(x.Key));
        }
        if(!Fits(boats,"boats")||!Fits(subs,"submarines")||!Fits(battleships,"battleships"))
            throw new InvalidOperationException("Compulsory naval defense or model inventory changed.");
        long totalB = boats.Sum(x => x.Count), totalS = subs.Sum(x => x.Count), totalBS = battleships.Sum(x => x.Count);
        using var con = OpenCon(); using var tx = con.BeginTransaction();
        long operationId;
        using (var insert = con.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText = @"INSERT INTO NavalInvasions
(ChatId,AttackerId,DefenderId,Boats,Submarines,Battleships,BoatModels,SubModels,BattleshipModels,
 Strategy,Tactic,CreatedAtMs,ArriveAtMs,Processed,AttackerName,DefenderName,ScenarioSeed,Status)
VALUES(@chat,@a,@d,@b,@s,@bs,@bm,@sm,@bsm,1,@t,@now,@arrive,0,@an,@dn,@seed,'Pending');
SELECT last_insert_rowid();";
            insert.Parameters.AddWithValue("@chat", attacker.ChatId); insert.Parameters.AddWithValue("@a", attacker.OwnerId);
            insert.Parameters.AddWithValue("@d", defender.OwnerId); insert.Parameters.AddWithValue("@b", totalB);
            insert.Parameters.AddWithValue("@s", totalS); insert.Parameters.AddWithValue("@bs", totalBS);
            insert.Parameters.AddWithValue("@bm", Encode(boats)); insert.Parameters.AddWithValue("@sm", Encode(subs));
            insert.Parameters.AddWithValue("@bsm", Encode(battleships)); insert.Parameters.AddWithValue("@t", tactic);
            insert.Parameters.AddWithValue("@now", nowMs); insert.Parameters.AddWithValue("@arrive", nowMs + travelMinutes * 60000L);
            insert.Parameters.AddWithValue("@an", attacker.Name); insert.Parameters.AddWithValue("@dn", defender.Name);
            insert.Parameters.AddWithValue("@seed", unchecked((long)((ulong)nowMs ^ (ulong)attacker.OwnerId << 17 ^ (ulong)defender.OwnerId)));
            operationId = Convert.ToInt64(insert.ExecuteScalar());
        }
        using (var deduct = con.CreateCommand())
        {
            deduct.Transaction = tx;
            deduct.CommandText = @"UPDATE Countries SET Boats=Boats-@b,Submarines=Submarines-@s,
 Battleships=Battleships-@bs,BoatsAtSea=BoatsAtSea+@b,SubmarinesAtSea=SubmarinesAtSea+@s,
 BattleshipsAtSea=BattleshipsAtSea+@bs WHERE OwnerId=@o AND ChatId=@c
 AND Boats>=@b AND Submarines>=@s AND Battleships>=@bs";
            deduct.Parameters.AddWithValue("@b", totalB); deduct.Parameters.AddWithValue("@s", totalS);
            deduct.Parameters.AddWithValue("@bs", totalBS); deduct.Parameters.AddWithValue("@o", attacker.OwnerId);
            deduct.Parameters.AddWithValue("@c", attacker.ChatId);
            if (deduct.ExecuteNonQuery() != 1) throw new InvalidOperationException("Naval inventory changed.");
        }
        DeductModels(con, tx, attacker.OwnerId, attacker.ChatId, "Boats", boats);
        DeductModels(con, tx, attacker.OwnerId, attacker.ChatId, "Submarines", subs);
        DeductModels(con, tx, attacker.OwnerId, attacker.ChatId, "Battleships", battleships);
        foreach (var item in battleships)
        {
            using var mark = con.CreateCommand(); mark.Transaction = tx;
            mark.CommandText = @"UPDATE BattleshipUnits SET OperationId=@op,Status='AtSea'
 WHERE Id IN (SELECT Id FROM BattleshipUnits WHERE OwnerId=@o AND ChatId=@c AND ModelName=@m
 AND OperationId IS NULL AND Status='Ready' AND DamagePercent<=50 ORDER BY DamagePercent ASC,Id LIMIT @n)";
            mark.Parameters.AddWithValue("@op", operationId); mark.Parameters.AddWithValue("@o", attacker.OwnerId);
            mark.Parameters.AddWithValue("@c", attacker.ChatId); mark.Parameters.AddWithValue("@m", item.Model);
            mark.Parameters.AddWithValue("@n", item.Count);
            if (mark.ExecuteNonQuery() != item.Count) throw new InvalidOperationException("Selected battleship is unavailable or too damaged.");
        }
        tx.Commit(); return operationId;
    }

    static void DeductModels(SqliteConnection con, SqliteTransaction tx, long owner, long chat,
        string category, IReadOnlyList<NavalModelAmount> models)
    {
        foreach (var item in models)
        {
            using var cmd = con.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = @"UPDATE EquipmentModels SET Count=MAX(0,Count-@n)
 WHERE OwnerId=@o AND ChatId=@c AND Category=@cat AND ModelName=@m";
            cmd.Parameters.AddWithValue("@n", item.Count); cmd.Parameters.AddWithValue("@o", owner);
            cmd.Parameters.AddWithValue("@c", chat); cmd.Parameters.AddWithValue("@cat", category);
            cmd.Parameters.AddWithValue("@m", item.Model); cmd.ExecuteNonQuery();
        }
    }

    public static List<NavalModelAmount> DecodeNavalModels(string encoded)
    {
        var result = new List<NavalModelAmount>();
        foreach (string part in (encoded ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int split = part.LastIndexOf(':');
            if (split > 0 && long.TryParse(part[(split+1)..], out long count) && count > 0)
                result.Add(new NavalModelAmount(part[..split], count));
        }
        return result;
    }
    static string Encode(IEnumerable<NavalModelAmount> models) => string.Join(';', models.Where(x => x.Count > 0).Select(x => $"{x.Model}:{x.Count}"));

    public static int GetNavalRivalryWins(long attacker, long defender, long chat)
    {
        using var con = OpenCon(); using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT Wins FROM NavalRivalryWins WHERE AttackerId=@a AND DefenderId=@d AND ChatId=@c";
        cmd.Parameters.AddWithValue("@a", attacker); cmd.Parameters.AddWithValue("@d", defender); cmd.Parameters.AddWithValue("@c", chat);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    public static bool SettleNavalOperation(NavalInvasion inv, NavalBattleResult result,
        IReadOnlyList<NavalModelAmount> attackerBoats, IReadOnlyList<NavalModelAmount> attackerSubs,
        IReadOnlyList<NavalModelAmount> defenderBoats, IReadOnlyList<NavalModelAmount> defenderSubs)
    {
        if(result.AttackerBattleships.Count!=inv.Battleships)
            throw new InvalidOperationException($"Battleship settlement mismatch for operation {inv.Id}: sent={inv.Battleships}, outcomes={result.AttackerBattleships.Count}.");
        using var con = OpenCon(); using var tx = con.BeginTransaction();
        using (var claim = con.CreateCommand())
        {
            claim.Transaction = tx; claim.CommandText = "UPDATE NavalInvasions SET Processed=1,Status='Settled' WHERE Id=@id AND Processed=0";
            claim.Parameters.AddWithValue("@id", inv.Id); if (claim.ExecuteNonQuery() != 1) return false;
        }
        long aBoatLoss = result.AttackerBoatLosses.Values.Sum(), aSubLoss = result.AttackerSubmarineLosses.Values.Sum();
        long aBsLost = result.AttackerBattleships.Count(x => x.Sunk);
        long aBoatReturn = Math.Max(0, attackerBoats.Sum(x => x.Count)-aBoatLoss);
        long aSubReturn = Math.Max(0, attackerSubs.Sum(x => x.Count)-aSubLoss);
        long aBsReturn = Math.Max(0, inv.Battleships-aBsLost);
        using (var update = con.CreateCommand())
        {
            update.Transaction=tx; update.CommandText=@"UPDATE Countries SET Boats=Boats+@br,Submarines=Submarines+@sr,
 Battleships=Battleships+@bsr,BoatsAtSea=MAX(0,BoatsAtSea-@b),SubmarinesAtSea=MAX(0,SubmarinesAtSea-@s),
 BattleshipsAtSea=MAX(0,BattleshipsAtSea-@bs),Money=Money+@lm,Iron=Iron+@li WHERE OwnerId=@o AND ChatId=@c";
            update.Parameters.AddWithValue("@br",aBoatReturn); update.Parameters.AddWithValue("@sr",aSubReturn);
            update.Parameters.AddWithValue("@bsr",aBsReturn); update.Parameters.AddWithValue("@b",inv.Boats);
            update.Parameters.AddWithValue("@s",inv.Submarines); update.Parameters.AddWithValue("@bs",inv.Battleships);
            update.Parameters.AddWithValue("@lm",result.LootMoney); update.Parameters.AddWithValue("@li",result.LootIron);
            update.Parameters.AddWithValue("@o",inv.AttackerId); update.Parameters.AddWithValue("@c",inv.ChatId); update.ExecuteNonQuery();
        }
        AddSurvivorModels(con,tx,inv.AttackerId,inv.ChatId,"Boats",attackerBoats,result.AttackerBoatLosses);
        AddSurvivorModels(con,tx,inv.AttackerId,inv.ChatId,"Submarines",attackerSubs,result.AttackerSubmarineLosses);
        var sentBattleships=DecodeNavalModels(inv.BattleshipModels);
        var sunkBattleships=result.AttackerBattleships.Where(x=>x.Sunk)
            .GroupBy(x=>x.Model,StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x=>x.Key,x=>(long)x.Count(),StringComparer.OrdinalIgnoreCase);
        AddSurvivorModels(con,tx,inv.AttackerId,inv.ChatId,"Battleships",sentBattleships,sunkBattleships);
        foreach(var bs in result.AttackerBattleships) ApplyBattleshipOutcome(con,tx,bs);

        long dBoatLoss=result.DefenderBoatLosses.Values.Sum(), dSubLoss=result.DefenderSubmarineLosses.Values.Sum();
        long dBsLost=result.DefenderBattleships.Count(x=>x.Sunk);
        using(var update=con.CreateCommand())
        {
            update.Transaction=tx; update.CommandText=@"UPDATE Countries SET Boats=MAX(0,Boats-@b),Submarines=MAX(0,Submarines-@s),
 Battleships=MAX(0,Battleships-@bs),Money=MAX(0,Money-@lm),Iron=MAX(0,Iron-@li)
 WHERE OwnerId=@o AND ChatId=@c";
            update.Parameters.AddWithValue("@b",dBoatLoss); update.Parameters.AddWithValue("@s",dSubLoss);
            update.Parameters.AddWithValue("@bs",dBsLost); update.Parameters.AddWithValue("@lm",result.LootMoney);
            update.Parameters.AddWithValue("@li",result.LootIron); update.Parameters.AddWithValue("@o",inv.DefenderId);
            update.Parameters.AddWithValue("@c",inv.ChatId); update.ExecuteNonQuery();
        }
        DeductModels(con,tx,inv.DefenderId,inv.ChatId,"Boats",result.DefenderBoatLosses.Select(x=>new NavalModelAmount(x.Key,x.Value)).ToList());
        DeductModels(con,tx,inv.DefenderId,inv.ChatId,"Submarines",result.DefenderSubmarineLosses.Select(x=>new NavalModelAmount(x.Key,x.Value)).ToList());
        var defenderSunk=result.DefenderBattleships.Where(x=>x.Sunk)
            .GroupBy(x=>x.Model,StringComparer.OrdinalIgnoreCase)
            .Select(x=>new NavalModelAmount(x.Key,x.Count())).ToList();
        DeductModels(con,tx,inv.DefenderId,inv.ChatId,"Battleships",defenderSunk);
        foreach(var bs in result.DefenderBattleships) ApplyBattleshipOutcome(con,tx,bs);
        UpdateLegacyBattleshipDamage(con,tx,inv.AttackerId,inv.ChatId);
        UpdateLegacyBattleshipDamage(con,tx,inv.DefenderId,inv.ChatId);

        int wins;
        using(var readWins=con.CreateCommand())
        {
            readWins.Transaction=tx;
            readWins.CommandText="SELECT Wins FROM NavalRivalryWins WHERE AttackerId=@a AND DefenderId=@d AND ChatId=@c";
            readWins.Parameters.AddWithValue("@a",inv.AttackerId);readWins.Parameters.AddWithValue("@d",inv.DefenderId);
            readWins.Parameters.AddWithValue("@c",inv.ChatId);wins=Convert.ToInt32(readWins.ExecuteScalar()??0);
        }
        if(result.AttackerWon && !result.EmptyBase) wins++;
        else if(!result.AttackerWon && result.Outcome==NavalOutcomeKind.DefenderNavalVictory) wins=Math.Max(0,wins-1);
        if(wins>=3)
        {
            using var port=con.CreateCommand(); port.Transaction=tx;
            port.CommandText="UPDATE Countries SET PortLevel=PortLevel-1 WHERE OwnerId=@o AND ChatId=@c AND PortLevel>1";
            port.Parameters.AddWithValue("@o",inv.DefenderId); port.Parameters.AddWithValue("@c",inv.ChatId);
            result.PortLevelDamaged=port.ExecuteNonQuery()==1; wins=0;
        }
        result.RivalryWinsAfter=wins;
        using(var rivalry=con.CreateCommand())
        {
            rivalry.Transaction=tx; rivalry.CommandText=@"INSERT INTO NavalRivalryWins(AttackerId,DefenderId,ChatId,Wins)
 VALUES(@a,@d,@c,@w) ON CONFLICT(AttackerId,DefenderId,ChatId) DO UPDATE SET Wins=@w";
            rivalry.Parameters.AddWithValue("@a",inv.AttackerId); rivalry.Parameters.AddWithValue("@d",inv.DefenderId);
            rivalry.Parameters.AddWithValue("@c",inv.ChatId); rivalry.Parameters.AddWithValue("@w",wins); rivalry.ExecuteNonQuery();
        }
        using(var persistResult=con.CreateCommand())
        {
            persistResult.Transaction=tx;
            persistResult.CommandText="UPDATE NavalInvasions SET ResultJson=@result WHERE Id=@id";
            persistResult.Parameters.AddWithValue("@result",JsonSerializer.Serialize(result));
            persistResult.Parameters.AddWithValue("@id",inv.Id);
            persistResult.ExecuteNonQuery();
        }
        using(var hist=con.CreateCommand())
        {
            hist.Transaction=tx; hist.CommandText=@"INSERT OR IGNORE INTO NavalBattleHistory
(OperationId,Timestamp,ChatId,AttackerId,DefenderId,Outcome,SuccessPercent,LootMoney,LootIron,Report)
VALUES(@op,@ts,@c,@a,@d,@out,@success,@lm,@li,@report)";
            hist.Parameters.AddWithValue("@op",inv.Id); hist.Parameters.AddWithValue("@ts",DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
            hist.Parameters.AddWithValue("@c",inv.ChatId); hist.Parameters.AddWithValue("@a",inv.AttackerId);
            hist.Parameters.AddWithValue("@d",inv.DefenderId); hist.Parameters.AddWithValue("@out",result.Outcome.ToString());
            hist.Parameters.AddWithValue("@success",result.SuccessPercent); hist.Parameters.AddWithValue("@lm",result.LootMoney);
            hist.Parameters.AddWithValue("@li",result.LootIron); hist.Parameters.AddWithValue("@report",result.AttackerReport); hist.ExecuteNonQuery();
        }
        tx.Commit(); return true;
    }

    static void AddSurvivorModels(SqliteConnection con,SqliteTransaction tx,long owner,long chat,string category,
        IReadOnlyList<NavalModelAmount> sent,IReadOnlyDictionary<string,long> losses)
    {
        foreach(var item in sent)
        {
            long count=Math.Max(0,item.Count-losses.GetValueOrDefault(item.Model)); if(count<=0)continue;
            using var cmd=con.CreateCommand();cmd.Transaction=tx;cmd.CommandText=@"INSERT INTO EquipmentModels
(OwnerId,ChatId,Category,ModelName,Count) VALUES(@o,@c,@cat,@m,@n)
ON CONFLICT(OwnerId,ChatId,Category,ModelName) DO UPDATE SET Count=Count+@n";
            cmd.Parameters.AddWithValue("@o",owner);cmd.Parameters.AddWithValue("@c",chat);cmd.Parameters.AddWithValue("@cat",category);
            cmd.Parameters.AddWithValue("@m",item.Model);cmd.Parameters.AddWithValue("@n",count);cmd.ExecuteNonQuery();
        }
    }
    static void UpdateLegacyBattleshipDamage(SqliteConnection con,SqliteTransaction tx,long ownerId,long chatId)
    {
        using var cmd=con.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText=@"UPDATE Countries SET BattleshipDamage=COALESCE((SELECT SUM(DamagePercent)
 FROM BattleshipUnits WHERE OwnerId=@owner AND ChatId=@chat AND Status!='Sunk'),0)
 WHERE OwnerId=@owner AND ChatId=@chat";
        cmd.Parameters.AddWithValue("@owner",ownerId);cmd.Parameters.AddWithValue("@chat",chatId);cmd.ExecuteNonQuery();
    }

    static void ApplyBattleshipOutcome(SqliteConnection con,SqliteTransaction tx,NavalBattleshipOutcome outcome)
    {
        using var cmd=con.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText=outcome.Sunk?"UPDATE BattleshipUnits SET DamagePercent=100,Status='Sunk',OperationId=NULL WHERE Id=@id":
            "UPDATE BattleshipUnits SET DamagePercent=@damage,Status='Ready',OperationId=NULL WHERE Id=@id";
        cmd.Parameters.AddWithValue("@id",outcome.UnitId);cmd.Parameters.AddWithValue("@damage",outcome.FinalDamage);
        if(cmd.ExecuteNonQuery()!=1)throw new InvalidOperationException($"Battleship unit {outcome.UnitId} was not settled.");
    }

    internal static bool SetBattleshipDamageForTest(long unitId,int damage)
    {
        using var con=OpenCon();using var cmd=con.CreateCommand();
        cmd.CommandText="UPDATE BattleshipUnits SET DamagePercent=@damage WHERE Id=@id AND Status='Ready'";
        cmd.Parameters.AddWithValue("@damage",Math.Clamp(damage,0,99));cmd.Parameters.AddWithValue("@id",unitId);
        return cmd.ExecuteNonQuery()==1;
    }

    public static bool GetBattleshipScrapQuote(long unitId,long ownerId,long chatId,out string model,out int damage,out long money,out long iron)
    {
        model="";damage=0;money=iron=0;using var con=OpenCon();using var cmd=con.CreateCommand();
        cmd.CommandText=@"SELECT ModelName,DamagePercent FROM BattleshipUnits
 WHERE Id=@id AND OwnerId=@o AND ChatId=@c AND Status='Ready' AND OperationId IS NULL";
        cmd.Parameters.AddWithValue("@id",unitId);cmd.Parameters.AddWithValue("@o",ownerId);cmd.Parameters.AddWithValue("@c",chatId);
        using var r=cmd.ExecuteReader();if(!r.Read())return false;model=r.GetString(0);damage=r.GetInt32(1);
        (long baseMoney,long baseIron)=model.ToLowerInvariant() switch
        {var m when m.Contains("iowa")=>(50000,40000),var m when m.Contains("soyuz")=>(45000,25000),_=>(50000,30000)};
        money=baseMoney/2;iron=baseIron/2;return true;
    }

    public static bool ScrapBattleshipUnit(long unitId,long ownerId,long chatId,out string model,out long money,out long iron)
    {
        model="";money=iron=0;using var con=OpenCon();using var tx=con.BeginTransaction();
        using(var get=con.CreateCommand())
        {
            get.Transaction=tx;get.CommandText=@"SELECT ModelName FROM BattleshipUnits
 WHERE Id=@id AND OwnerId=@o AND ChatId=@c AND Status='Ready' AND OperationId IS NULL";
            get.Parameters.AddWithValue("@id",unitId);get.Parameters.AddWithValue("@o",ownerId);get.Parameters.AddWithValue("@c",chatId);
            object? value=get.ExecuteScalar();if(value==null||value==DBNull.Value)return false;model=Convert.ToString(value)??"";
        }
        (long baseMoney,long baseIron)=model.ToLowerInvariant() switch
        {var m when m.Contains("iowa")=>(50000,40000),var m when m.Contains("soyuz")=>(45000,25000),_=>(50000,30000)};
        money=(long)Math.Floor(baseMoney*0.50);iron=(long)Math.Floor(baseIron*0.50);
        using(var country=con.CreateCommand())
        {
            country.Transaction=tx;country.CommandText=@"UPDATE Countries SET Battleships=Battleships-1,
 Money=Money+@money,Iron=Iron+@iron WHERE OwnerId=@o AND ChatId=@c AND Battleships>=1";
            country.Parameters.AddWithValue("@money",money);country.Parameters.AddWithValue("@iron",iron);
            country.Parameters.AddWithValue("@o",ownerId);country.Parameters.AddWithValue("@c",chatId);
            if(country.ExecuteNonQuery()!=1)return false;
        }
        using(var equipment=con.CreateCommand())
        {
            equipment.Transaction=tx;equipment.CommandText=@"UPDATE EquipmentModels SET Count=MAX(0,Count-1)
 WHERE OwnerId=@o AND ChatId=@c AND Category='Battleships' AND ModelName=@model;
 DELETE FROM EquipmentModels WHERE OwnerId=@o AND ChatId=@c AND Category='Battleships' AND ModelName=@model AND Count<=0";
            equipment.Parameters.AddWithValue("@o",ownerId);equipment.Parameters.AddWithValue("@c",chatId);equipment.Parameters.AddWithValue("@model",model);
            equipment.ExecuteNonQuery();
        }
        using(var delete=con.CreateCommand()){delete.Transaction=tx;delete.CommandText="DELETE FROM BattleshipUnits WHERE Id=@id";
            delete.Parameters.AddWithValue("@id",unitId);if(delete.ExecuteNonQuery()!=1)return false;}
        UpdateLegacyBattleshipDamage(con,tx,ownerId,chatId);
        tx.Commit();return true;
    }

    public static bool GetBattleshipRepairQuote(long unitId,long ownerId,long chatId,
        out string model,out int damage,out long money,out long iron)
    {
        model="";damage=0;money=iron=0;using var con=OpenCon();using var cmd=con.CreateCommand();
        cmd.CommandText=@"SELECT ModelName,DamagePercent FROM BattleshipUnits
 WHERE Id=@id AND OwnerId=@o AND ChatId=@c AND Status='Ready' AND OperationId IS NULL";
        cmd.Parameters.AddWithValue("@id",unitId);cmd.Parameters.AddWithValue("@o",ownerId);cmd.Parameters.AddWithValue("@c",chatId);
        using var r=cmd.ExecuteReader();if(!r.Read())return false;model=r.GetString(0);damage=r.GetInt32(1);if(damage<=0)return false;
        (long baseMoney,long baseIron)=model.ToLowerInvariant() switch
        {var m when m.Contains("iowa")=>(50000,40000),var m when m.Contains("soyuz")=>(45000,25000),_=>(50000,30000)};
        money=(long)Math.Ceiling(baseMoney*damage/100.0);iron=(long)Math.Ceiling(baseIron*damage/100.0);return true;
    }

    public static bool RepairBattleshipUnit(long unitId,long ownerId,long chatId,out long money,out long iron)
    {
        money=iron=0;using var con=OpenCon();using var tx=con.BeginTransaction();string model;int damage;
        using(var get=con.CreateCommand())
        {get.Transaction=tx;get.CommandText=@"SELECT ModelName,DamagePercent FROM BattleshipUnits
 WHERE Id=@id AND OwnerId=@o AND ChatId=@c AND Status='Ready' AND OperationId IS NULL";
         get.Parameters.AddWithValue("@id",unitId);get.Parameters.AddWithValue("@o",ownerId);get.Parameters.AddWithValue("@c",chatId);
         using var r=get.ExecuteReader();if(!r.Read())return false;model=r.GetString(0);damage=r.GetInt32(1);}
        (long baseMoney,long baseIron)=model.ToLowerInvariant() switch
        {var m when m.Contains("iowa")=>(50000,40000),var m when m.Contains("soyuz")=>(45000,25000),_=>(50000,30000)};
        money=(long)Math.Ceiling(baseMoney*damage/100.0);iron=(long)Math.Ceiling(baseIron*damage/100.0);
        using(var pay=con.CreateCommand())
        {pay.Transaction=tx;pay.CommandText="UPDATE Countries SET Money=Money-@m,Iron=Iron-@i WHERE OwnerId=@o AND ChatId=@c AND Money>=@m AND Iron>=@i";
         pay.Parameters.AddWithValue("@m",money);pay.Parameters.AddWithValue("@i",iron);pay.Parameters.AddWithValue("@o",ownerId);pay.Parameters.AddWithValue("@c",chatId);
         if(pay.ExecuteNonQuery()!=1)return false;}
        using(var fix=con.CreateCommand()){fix.Transaction=tx;fix.CommandText="UPDATE BattleshipUnits SET DamagePercent=0 WHERE Id=@id";fix.Parameters.AddWithValue("@id",unitId);fix.ExecuteNonQuery();}
        UpdateLegacyBattleshipDamage(con,tx,ownerId,chatId);
        tx.Commit();return true;
    }
}
