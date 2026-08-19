using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

static class NavalRegressionTests
{
    static int _assertions;
    static void Assert(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException($"NAVAL TEST FAILED: {message}");
    }

    static Country Country(long owner, long chat, string name, Faction faction) => new()
    {
        OwnerId = owner, ChatId = chat, Name = name, OwnerName = name,
        Faction = faction, Money = 2_000_000, Iron = 2_000_000,
        Population = 100_000, Soldiers = 10_000, PortLevel = 5,
        FactoryLevel = 1, MineLevel = 1, Cities = 4
    };

    public static void Run()
    {
        string db = Path.Combine(Path.GetTempPath(), $"alice-naval-test-{Guid.NewGuid():N}.db");
        Environment.SetEnvironmentVariable("ALICE_DB_PATH", db);
        try
        {
            Database.Init();
            Database.InitNavalV2();
            TestPurchaseCapacityTransferAndScrap();
            TestTransferCancellationAndReceiverDeletion();
            TestSmallCraftTransfers();
            TestRepairPricingAndDamagePreservation();
            TestSyncIsStableAndFast();
            TestEngineDeterminismAndBounds();
            Console.WriteLine($"NAVAL REGRESSION TESTS PASSED — {_assertions} assertions");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (string suffix in new[] { "", "-wal", "-shm" })
                try { File.Delete(db + suffix); } catch { }
            Environment.SetEnvironmentVariable("ALICE_DB_PATH", null);
        }
    }

    static void TestPurchaseCapacityTransferAndScrap()
    {
        const long chat = -90001;
        var sender = Country(101, chat, "Sender", Faction.USA);
        var receiver = Country(102, chat, "Receiver", Faction.USA);
        Database.AddCountry(sender); Database.AddCountry(receiver);
        for (int i = 0; i < 3; i++)
            Assert(Database.TryPurchaseBattleship(sender.OwnerId, chat, "Iowa", 50_000, 40_000), "sender purchase should succeed");
        Assert(!Database.TryPurchaseBattleship(sender.OwnerId, chat, "Iowa", 50_000, 40_000), "fourth battleship must be rejected");
        for (int i = 0; i < 2; i++)
            Assert(Database.TryPurchaseBattleship(receiver.OwnerId, chat, "Iowa", 50_000, 40_000), "receiver purchase should succeed");
        Assert(Database.GetBattleshipCapacityUsed(sender.OwnerId, chat) == 3, "sender capacity must be exactly 3");

        var transferable = Database.GetNavalTransferableModels(Database.GetCountry(sender.OwnerId, chat)!, "battleships");
        Assert(transferable.Sum(x => x.Count) == 2, "20 percent defense must reserve one of three battleships");
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Assert(Database.TryCreateTransfers(sender.OwnerId, chat, 1, receiver.OwnerId, "battleships",
            new List<(string, long)> { ("Iowa", 1) }, now), "first battleship transfer must be created");
        Assert(Database.GetBattleshipCapacityUsed(receiver.OwnerId, chat) == 3, "pending incoming ship must reserve receiver capacity");
        Assert(!Database.TryCreateTransfers(sender.OwnerId, chat, 1, receiver.OwnerId, "battleships",
            new List<(string, long)> { ("Iowa", 1) }, now), "second transfer exceeding cap must fail");

        Transfer transfer = Database.GetActiveTransfers().Single(x => x.ReceiverId == receiver.OwnerId && x.ResourceType == "battleships");
        Assert(Database.CompleteTransfer(transfer, "Iowa") == "delivered", "battleship must be delivered");
        Assert(Database.GetCountry(receiver.OwnerId, chat)!.Battleships == 3, "receiver aggregate must become 3");
        Assert(Database.GetBattleshipUnits(receiver.OwnerId, chat, false).Count == 3, "receiver must own three ship instances");
        Assert(!Database.TryPurchaseBattleship(receiver.OwnerId, chat, "Iowa", 50_000, 40_000), "purchase over cap after transfer must fail");

        long unit = Database.GetBattleshipUnits(receiver.OwnerId, chat, false).First().UnitId;
        Country before = Database.GetCountry(receiver.OwnerId, chat)!;
        Assert(Database.ScrapBattleshipUnit(unit, receiver.OwnerId, chat, out string model, out long money, out long iron), "scrap should succeed");
        Country after = Database.GetCountry(receiver.OwnerId, chat)!;
        Assert(model == "Iowa" && money == 25_000 && iron == 20_000, "Iowa scrap refund must be exactly 50 percent");
        Assert(after.Money - before.Money == 25_000 && after.Iron - before.Iron == 20_000, "scrap resources must be credited");
        Assert(after.Battleships == 2 && Database.GetBattleshipCapacityUsed(receiver.OwnerId, chat) == 2, "scrap must free one capacity slot");
    }

    static void TestTransferCancellationAndReceiverDeletion()
    {
        const long chat = -90002;
        var sender = Country(201, chat, "CancelSender", Faction.Reich);
        var receiver = Country(202, chat, "CancelReceiver", Faction.Reich);
        Database.AddCountry(sender); Database.AddCountry(receiver);
        for (int i = 0; i < 2; i++) Assert(Database.TryPurchaseBattleship(sender.OwnerId, chat, "Bismarck", 50_000, 30_000), "Bismarck purchase");
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Assert(Database.TryCreateTransfers(sender.OwnerId, chat, 1, receiver.OwnerId, "battleships",
            new List<(string, long)> { ("Bismarck", 1) }, now + 60_000), "cancel test transfer create");
        Transfer pending = Database.GetActiveTransfers().Single(x => x.ChatId == chat);
        Assert(Database.DeleteTransfer(pending.Id), "cancel must succeed");
        Assert(Database.GetCountry(sender.OwnerId, chat)!.Battleships == 2, "cancel must restore aggregate battleship");
        Assert(Database.GetBattleshipUnits(sender.OwnerId, chat, false).Count == 2, "cancel must restore ship instance");

        sender = Database.GetCountry(sender.OwnerId, chat)!;
        sender.Boats = 10; Database.UpdateCountryFull(sender);
        Database.AddEquipmentModel(sender.OwnerId, chat, "Boats", "S-Boot", 10);
        Assert(Database.TryCreateTransfers(sender.OwnerId, chat, 1, receiver.OwnerId, "boats",
            new List<(string, long)> { ("S-Boot", 3) }, now), "boat transfer before receiver deletion");
        Database.DeleteCountry(receiver.OwnerId, chat);
        Transfer returning = Database.GetActiveTransfers().Single(x => x.ChatId == chat);
        Assert(Database.CompleteTransfer(returning, "S-Boot") == "returned", "deleted receiver must return shipment");
        Assert(Database.GetCountry(sender.OwnerId, chat)!.Boats == 10, "returned boats must not disappear");
    }

    static void TestSmallCraftTransfers()
    {
        const long chat = -90003;
        var a = Country(301, chat, "FleetA", Faction.USSR);
        var b = Country(302, chat, "FleetB", Faction.USSR);
        a.Boats = 20; a.Submarines = 10;
        Database.AddCountry(a); Database.AddCountry(b);
        Database.AddEquipmentModel(a.OwnerId, chat, "Boats", "G-5", 20);
        Database.AddEquipmentModel(a.OwnerId, chat, "Submarines", "S-class", 10);
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Assert(Database.TryCreateTransfers(a.OwnerId, chat, 1, b.OwnerId, "boats",
            new List<(string, long)> { ("G-5", 4) }, now), "boat transfer create");
        Assert(Database.TryCreateTransfers(a.OwnerId, chat, 1, b.OwnerId, "submarines",
            new List<(string, long)> { ("S-class", 2) }, now), "sub transfer create");
        foreach (Transfer t in Database.GetActiveTransfers().Where(x => x.ChatId == chat).ToList())
            Assert(Database.CompleteTransfer(t, t.ModelName) == "delivered", "small craft delivery");
        Country got = Database.GetCountry(b.OwnerId, chat)!;
        Assert(got.Boats == 4 && got.Submarines == 2, "receiver naval aggregates must match shipments");
        Assert(Database.GetEquipmentModels(b.OwnerId, chat, "Boats").Sum(x => x.Count) == 4, "boat model ledger must match");
        Assert(Database.GetEquipmentModels(b.OwnerId, chat, "Submarines").Sum(x => x.Count) == 2, "sub model ledger must match");
    }

    static void TestRepairPricingAndDamagePreservation()
    {
        const long chat = -90004;
        var a = Country(401, chat, "DamageA", Faction.USA);
        var b = Country(402, chat, "DamageB", Faction.USA);
        Database.AddCountry(a); Database.AddCountry(b);
        Assert(Database.TryPurchaseBattleship(a.OwnerId, chat, "Iowa", 50_000, 40_000), "damage ship purchase");
        long id = Database.GetBattleshipUnits(a.OwnerId, chat, false).Single().UnitId;
        Assert(Database.SetBattleshipDamageForTest(id, 40), "set test damage");
        Assert(Database.GetBattleshipRepairQuote(id, a.OwnerId, chat, out _, out int damage, out long money, out long iron), "repair quote");
        Assert(damage == 40 && money == 20_000 && iron == 16_000, "repair quote must use real damage percentage");
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Assert(Database.TryCreateTransfers(a.OwnerId, chat, 1, b.OwnerId, "battleships",
            new List<(string, long)> { ("Iowa", 1) }, now), "damaged ship transfer");
        Transfer t = Database.GetActiveTransfers().Single(x => x.ChatId == chat);
        Assert(Database.CompleteTransfer(t, "Iowa") == "delivered", "damaged ship delivery");
        NavalBattleshipState moved = Database.GetBattleshipUnits(b.OwnerId, chat, false).Single();
        Assert(moved.DamagePercent == 40, "transfer must preserve per-ship damage");
        Assert(Database.RepairBattleshipUnit(moved.UnitId, b.OwnerId, chat, out money, out iron), "repair transferred ship");
        Assert(money == 20_000 && iron == 16_000, "repair debit must match quote");
        Assert(Database.GetBattleshipUnits(b.OwnerId, chat, false).Single().DamagePercent == 0, "repair must clear damage");
    }

    static void TestSyncIsStableAndFast()
    {
        const long chat = -90005;
        var c = Country(501, chat, "Sync", Faction.Reich);
        Database.AddCountry(c);
        Assert(Database.TryPurchaseBattleship(c.OwnerId, chat, "Bismarck", 50_000, 30_000), "sync ship purchase");
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++) Database.SyncBattleshipUnits(c.OwnerId, chat);
        sw.Stop();
        Assert(Database.GetBattleshipUnits(c.OwnerId, chat, false).Count == 1, "sync must be idempotent and never duplicate units");
        Assert(sw.Elapsed < TimeSpan.FromSeconds(10), $"100 syncs took too long: {sw.Elapsed}");
    }

    static void TestEngineDeterminismAndBounds()
    {
        var request = new NavalBattleRequest
        {
            OperationId = 1, Seed = 0xC0FFEE, AttackerName = "A", DefenderName = "D",
            AttackerTactic = 1, DefenderStrategy = 2, DefenderTactic = 2,
            DefenderPortLevel = 4, DefenderMoney = 500_000, DefenderIron = 200_000,
            AttackerBoats = new() { new("PT Boat", 30), new("S-Boot", 10) },
            AttackerSubmarines = new() { new("Gato", 8) },
            AttackerBattleships = new() { new() { UnitId = 1, Model = "Iowa", DamagePercent = 25 } },
            DefenderBoats = new() { new("G-5", 20) },
            DefenderSubmarines = new() { new("Type VIIC", 12) },
            DefenderBattleships = new() { new() { UnitId = 2, Model = "Bismarck", DamagePercent = 50 } }
        };
        NavalBattleResult x = NavalEngine.Resolve(request);
        NavalBattleResult y = NavalEngine.Resolve(request);
        Assert(x.Outcome == y.Outcome && x.SuccessPercent == y.SuccessPercent && x.LootMoney == y.LootMoney,
            "same naval seed must produce same aggregate result");
        Assert(x.AttackerBoatLosses.SequenceEqual(y.AttackerBoatLosses), "same seed boat losses must be deterministic");
        Assert(x.SuccessPercent is >= 0 and <= 100, "success must stay in 0..100");
        Assert(x.AttackerBoatLosses.Values.Sum() <= 40 && x.AttackerSubmarineLosses.Values.Sum() <= 8,
            "attacker losses must not exceed deployed units");
        Assert(x.DefenderBoatLosses.Values.Sum() <= 20 && x.DefenderSubmarineLosses.Values.Sum() <= 12,
            "defender losses must not exceed deployed units");
        Assert(x.LootMoney <= request.DefenderMoney && x.LootIron <= request.DefenderIron,
            "loot must not exceed defender resources");
        if (x.AttackerWon)
        {
            long expectedMoney=Math.Min(request.DefenderMoney,(long)Math.Round(request.DefenderMoney*0.15*(x.SuccessPercent/100.0)*2.5));
            long expectedIron=Math.Min(request.DefenderIron,(long)Math.Round(request.DefenderIron*0.10*(x.SuccessPercent/100.0)*2.5));
            Assert(x.LootMoney==expectedMoney&&x.LootIron==expectedIron,"naval loot must be exactly 2.5x ground formula");
        }
        Assert(x.AttackerReport.Contains("Iowa #1") && x.AttackerReport.Contains("9×406mm") &&
               x.AttackerReport.Contains("زره 305/140-140/406mm"),
            "attacker report must expose exact battleship technical data");
        Assert(x.AttackerReport.Contains("خسارات مدل‌به‌مدل") && x.DefenderReport.Contains("وضعیت نبردناوهای شما"),
            "naval reports must contain model losses and per-ship damage");

        var empty = new NavalBattleRequest
        {
            Seed = 7, AttackerName = "A", DefenderName = "Empty",
            DefenderMoney = 100_000, DefenderIron = 50_000,
            AttackerBoats = new() { new("PT Boat", 5) }
        };
        NavalBattleResult emptyResult = NavalEngine.Resolve(empty);
        Assert(emptyResult.Outcome == NavalOutcomeKind.EmptyBaseVictory && emptyResult.AttackerWon,
            "empty base must count as attacker victory");
        Assert(emptyResult.LootMoney == 18_750 && emptyResult.LootIron == 6_250,
            "empty base loot must be half of the 2.5x naval rate");

        var fatalDamage = new NavalBattleRequest
        {
            Seed=99,AttackerName="Finisher",DefenderName="Critical",
            AttackerTactic=2,DefenderStrategy=1,DefenderTactic=1,
            AttackerBoats=new(){new("PT Boat",1)},
            DefenderBattleships=new(){new(){UnitId=77,Model="Bismarck",DamagePercent=80}}
        };
        NavalBattleResult fatal=NavalEngine.Resolve(fatalDamage);
        Assert(fatal.DefenderBattleships.Single().Sunk&&fatal.DefenderBattleships.Single().FinalDamage==100,
            "a battleship whose cumulative damage rises above 80 percent must sink unconditionally");
        bool locked=false;
        try
        {
            WarEngineV2Core.RunNavalBattleAdvanced(new Country(),new Country(),new(),new(),new(),new(),new(),new(),2,1,1,1);
        }
        catch(NotSupportedException){locked=true;}
        Assert(locked,"legacy amphibious/ground-advance naval strategy must remain locked server-side");
    }
}
