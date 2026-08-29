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
{    static void ScheduleNavalArrival(long operationId,long arriveAtMs)
    {
        _=Task.Run(async () =>
        {
            try
            {
                long wait=Math.Max(0,arriveAtMs-DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                if(wait>0)await Task.Delay(TimeSpan.FromMilliseconds(wait));
                Console.WriteLine($"[NAVAL ARRIVAL WAKE] operation={operationId}");
                await ProcessNavalInvasions(CancellationToken.None);
            }
            catch(Exception ex){Console.WriteLine($"[NAVAL ARRIVAL WAKE ERR] operation={operationId} {ex}");}
        });
    }

    static async Task ProcessNavalInvasions(CancellationToken ct)
    {
        await navalProcessorLock.WaitAsync(ct);
        try
        {
            if (Volatile.Read(ref databaseMaintenanceRunning) == 0)
                await ProcessNavalInvasionsCore(ct);
        }
        finally
        {
            navalProcessorLock.Release();
        }
    }

    internal static void RedactDefenderNavalDoctrine(NavalBattleResult result)
    {
        result.AttackerReport=string.Join("\n",(result.AttackerReport??"").Split('\n')
            .Where(line=>!line.TrimStart().StartsWith("🛡 دکترین مدافع:",StringComparison.Ordinal)
                      &&!line.TrimStart().StartsWith("🛡 دفاع دشمن:",StringComparison.Ordinal)));
        result.GroupAnnouncement=string.Join("\n",(result.GroupAnnouncement??"").Split('\n').Select(line=>
        {
            if(line.TrimStart().StartsWith("🎯",StringComparison.Ordinal)&&line.Contains('↔'))
                return line[..line.IndexOf('↔')].TrimEnd();
            return line;
        }));
    }

    static void AppendNavalStrategicProgress(NavalBattleResult result)
    {
        if (result.AttackerReport.Contains("پیشرفت تخریب بندر", StringComparison.Ordinal) ||
            result.AttackerReport.Contains("سومین پیروزی این مهاجم", StringComparison.Ordinal)) return;
        if (result.PortLevelDamaged)
        {
            string portNews = "\n⚓ پس از سومین پیروزی این مهاجم، بندر مدافع یک سطح تخریب شد.";
            result.AttackerReport += portNews;
            result.DefenderReport += portNews;
            result.GroupAnnouncement += portNews;
        }
        else if (result.AttackerWon && !result.EmptyBase)
            result.AttackerReport += $"\n📈 پیشرفت تخریب بندر برابر این مدافع: {result.RivalryWinsAfter}/3";
    }

    static async Task ProcessNavalInvasionsCore(CancellationToken ct)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var inv in Database.GetPendingNavalInvasions(now))
        {
            ct.ThrowIfCancellationRequested();
            if(!Database.IsBotGroupActive(inv.ChatId))continue;
            Console.WriteLine($"[NAVAL RESOLUTION START] operation={inv.Id} status={inv.Status} due={inv.ArriveAtMs} now={now}");
            var locks = await AcquireCountryMutationLocks(inv.ChatId,
                new[] { inv.AttackerId, inv.DefenderId }, ct);
            try
            {
                if (inv.Status.Equals("Settled", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(inv.ResultJson))
                {
                    var recovered = JsonSerializer.Deserialize<NavalBattleResult>(inv.ResultJson);
                    if (recovered == null) throw new InvalidOperationException("Stored naval result is invalid.");
                    AppendNavalStrategicProgress(recovered);
                    RedactDefenderNavalDoctrine(recovered);
                    try { await SendPermanent(inv.AttackerId, recovered.AttackerReport, ct: ct); } catch { }
                    try { await SendPermanent(inv.DefenderId, recovered.DefenderReport, ct: ct); } catch { }
                    try { await SendPermanent(inv.ChatId, recovered.GroupAnnouncement, ct: ct); } catch { }
                    Database.MarkNavalInvasionProcessed(inv.Id);
                    continue;
                }
                var attacker = Database.GetCountry(inv.AttackerId, inv.ChatId);
                var defender = Database.GetCountry(inv.DefenderId, inv.ChatId);
                if (attacker == null)
                {
                    Database.MarkNavalInvasionProcessed(inv.Id);
                    continue;
                }
                if (defender == null)
                {
                    if(Database.ReturnNavalOperationWithoutBattle(inv))
                    {
                        try{await SendPermanent(inv.AttackerId,$"↩️ عملیات دریایی #{inv.Id} لغو شد؛ کشور مقصد وجود ندارد و تمام ناوگان بازگشت.",ct:ct);}catch{}
                    }
                    continue;
                }
                Database.SyncBattleshipUnits(defender.OwnerId, defender.ChatId);
                var attackerBoats = Database.DecodeNavalModels(inv.BoatModels);
                var attackerSubs = Database.DecodeNavalModels(inv.SubModels);
                bool harborStrike = inv.Tactic == 1;
                var defenderBoats = harborStrike
                    ? Database.GetEquipmentBreakdownForReconcile(defender, "boats")
                        .Select(x => new NavalModelAmount(x.ModelName, x.Count)).ToList()
                    : Database.GetNavalDefenseModels(defender, "boats");
                var defenderSubs = harborStrike
                    ? Database.GetEquipmentBreakdownForReconcile(defender, "submarines")
                        .Select(x => new NavalModelAmount(x.ModelName, x.Count)).ToList()
                    : Database.GetNavalDefenseModels(defender, "submarines");
                var defenderBs = new List<NavalBattleshipState>();
                if (harborStrike)
                    defenderBs.AddRange(Database.GetBattleshipUnits(defender.OwnerId, defender.ChatId, false));
                else
                {
                    var defenderBsWanted = Database.GetNavalDefenseModels(defender, "battleships")
                        .ToDictionary(x => x.Model, x => x.Count, StringComparer.OrdinalIgnoreCase);
                    foreach (var group in Database.GetBattleshipUnits(defender.OwnerId, defender.ChatId, true)
                                 .GroupBy(x => x.Model, StringComparer.OrdinalIgnoreCase))
                        defenderBs.AddRange(group.Take((int)Math.Min(int.MaxValue,
                            defenderBsWanted.GetValueOrDefault(group.Key))));
                }
                var attackerBs = Database.GetBattleshipUnits(attacker.OwnerId, attacker.ChatId,
                    onlyCombatReady: false, operationId: inv.Id);
                if(attackerBs.Count!=inv.Battleships)
                {
                    Console.WriteLine($"[NAVAL LEDGER FALLBACK] operation={inv.Id} sent={inv.Battleships} linked={attackerBs.Count}; returning fleet safely");
                    Database.ReturnNavalOperationWithoutBattle(inv);
                    try{await SendPermanent(inv.AttackerId,$"↩️ عملیات دریایی #{inv.Id} به‌دلیل ناسازگاری رکورد قدیمی بدون تلفات بازگردانده شد.",ct:ct);}catch{}
                    continue;
                }
                var orders = Database.GetNavalDefenseOrders(defender.OwnerId, defender.ChatId);
                var request = new NavalBattleRequest
                {
                    OperationId = inv.Id,
                    Seed = unchecked((ulong)inv.Id * 0x9E3779B97F4A7C15UL ^ (ulong)inv.CreatedAtMs),
                    AttackerName = attacker.Name,
                    DefenderName = defender.Name,
                    AttackerTactic = inv.Tactic,
                    DefenderStrategy = orders.Strategy,
                    DefenderTactic = orders.Tactic,
                    DefenderPortLevel = defender.PortLevel,
                    DefenderMoney = Math.Max(0, defender.Money),
                    DefenderIron = Math.Max(0, defender.Iron),
                    AttackerBoats = attackerBoats,
                    AttackerSubmarines = attackerSubs,
                    AttackerBattleships = attackerBs,
                    DefenderBoats = defenderBoats,
                    DefenderSubmarines = defenderSubs,
                    DefenderBattleships = defenderBs
                };
                NavalBattleResult result = NavalEngine.Resolve(request);
                if (!Database.SettleNavalOperation(inv, result, attackerBoats, attackerSubs,
                        defenderBoats, defenderSubs)) continue;
                AppendNavalStrategicProgress(result);
                RedactDefenderNavalDoctrine(result);
                try { await SendPermanent(inv.AttackerId, result.AttackerReport, ct: ct); } catch { }
                try { await SendPermanent(inv.DefenderId, result.DefenderReport, ct: ct); } catch { }
                try { await SendPermanent(inv.ChatId, result.GroupAnnouncement, ct: ct); } catch { }
                Database.MarkNavalInvasionProcessed(inv.Id);
                Console.WriteLine($"[NAVAL RESOLUTION COMPLETED] operation={inv.Id} outcome={result.Outcome} success={result.SuccessPercent}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NAVAL RESOLUTION ERR #{inv.Id}] {ex}");
            }
            finally { ReleaseCountryMutationLocks(locks); }
        }
    }
}
