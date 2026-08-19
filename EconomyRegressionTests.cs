using System;
using System.IO;

static class EconomyRegressionTests
{
    static int _assertions;
    static void Assert(bool condition,string message)
    {
        _assertions++;if(!condition)throw new InvalidOperationException($"ECONOMY TEST FAILED: {message}");
    }
    static Country Make(long id,long chat,string name,int mine=1)=>new()
    {
        OwnerId=id,ChatId=chat,Name=name,OwnerName=name,Faction=Faction.USA,
        Money=1_000_000,Iron=1_000_000,Population=100_000,Soldiers=10_000,
        FactoryLevel=1,PortLevel=5,MineLevel=mine,Cities=4
    };
    public static void Run()
    {
        string db=Path.Combine(Path.GetTempPath(),$"alice-economy-test-{Guid.NewGuid():N}.db");
        Environment.SetEnvironmentVariable("ALICE_DB_PATH",db);
        try
        {
            Database.Init();Database.InitNavalV2();
            const long chat=-91001;
            Database.AddCountry(Make(1,chat,"Existing6",6));
            Database.AddCountry(Make(2,chat,"Existing7",7));
            Database.AddCountry(Make(3,chat,"Upgrade",5));
            Database.Init(); // production migration must be idempotent and preserve premium levels
            Assert(Database.GetCountry(1,chat)!.MineLevel==6,"existing mine level 6 must survive initialization");
            Assert(Database.GetCountry(2,chat)!.MineLevel==7,"existing mine level 7 must survive initialization");

            Database.AddRoyalCoins(3,20);
            Assert(Database.TryUpgradeMineWithRoyal(3,chat,5,6,5),"level 5 to 6 must cost 5 royal");
            Assert(Database.GetCountry(3,chat)!.MineLevel==6,"mine must become level 6");
            Assert(Database.GetRoyalCoins(3)==15,"exactly 5 royal must be deducted");
            Assert(Database.TryUpgradeMineWithRoyal(3,chat,6,7,10),"level 6 to 7 must cost 10 royal");
            Assert(Database.GetCountry(3,chat)!.MineLevel==7,"mine must become level 7");
            Assert(Database.GetRoyalCoins(3)==5,"exactly 10 more royal must be deducted");
            Assert(!Database.TryUpgradeMineWithRoyal(3,chat,6,7,10),"stale/double upgrade must fail");
            Assert(Database.GetRoyalCoins(3)==5,"failed duplicate upgrade must not charge royal");

            var specB=WarEngineV2Core.GetBattleshipSpecByModel("Bismarck");
            Assert(specB.MainGuns==8&&specB.MainCaliber==380&&specB.Belt==320&&specB.CommandArmor==350&&specB.ReconAircraft==4,
                "Bismarck exact technical data");
            var specI=WarEngineV2Core.GetBattleshipSpecByModel("Iowa");
            Assert(specI.MainGuns==9&&specI.MainCaliber==406&&specI.DeckMin==140&&specI.ReconAircraft==3&&specI.UnitsBuilt==2,
                "Iowa exact technical data");
            var specS=WarEngineV2Core.GetBattleshipSpecByModel("Sovetsky Soyuz");
            Assert(specS.MainGuns==12&&specS.MainCaliber==305&&specS.DeckMin==50&&specS.DeckMax==75&&specS.ReconAircraft==0,
                "Soyuz exact technical data");

            var cap=Make(4,chat,"Cap");Database.AddCountry(cap);
            Assert(Database.TryPurchaseBattleship(4,chat,"Iowa",50_000,40_000),"cap purchase 1");
            Assert(Database.TryPurchaseBattleship(4,chat,"Iowa",50_000,40_000),"cap purchase 2");
            Assert(Database.TryPurchaseBattleship(4,chat,"Iowa",50_000,40_000),"cap purchase 3");
            Assert(!Database.TryPurchaseBattleship(4,chat,"Iowa",50_000,40_000),"cap purchase 4 must fail");
            Country persisted=Database.GetCountry(4,chat)!;
            Assert(persisted.Battleships+persisted.BattleshipsAtSea==3,"persisted battleship count must stay at 3");

            persisted.Boats=100;persisted.BoatsAtSea=100;persisted.Submarines=13;persisted.SubmarinesAtSea=20;
            Database.UpdateCountryFull(persisted);
            string summary=Program.BuildNavalInventorySummary(Database.GetCountry(4,chat)!);
            Assert(summary.Contains("قایق: کل 200")&&summary.Contains("آماده 100")&&summary.Contains("مأموریت 100"),
                "boat summary must clearly show total/ready/mission");
            Assert(summary.Contains("زیردریایی: کل 33")&&summary.Contains("آماده 13")&&summary.Contains("مأموریت 20"),
                "submarine summary must clearly show total/ready/mission");
            Assert(summary.Contains("نبردناو: کل 3/3"),"battleship summary must show true total against cap");
            Console.WriteLine($"ECONOMY/TECHNICAL REGRESSION TESTS PASSED — {_assertions} assertions");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach(string suffix in new[]{"","-wal","-shm"})try{File.Delete(db+suffix);}catch{}
            Environment.SetEnvironmentVariable("ALICE_DB_PATH",null);
        }
    }
}
