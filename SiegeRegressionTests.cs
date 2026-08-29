using System;
using System.IO;
using System.Linq;

static class SiegeRegressionTests
{
    static int _assertions;
    static void Assert(bool condition,string message)
    {
        _assertions++;
        if(!condition)throw new InvalidOperationException($"SIEGE TEST FAILED: {message}");
    }

    static Country NewCountry(long owner,long chat,string name,int cities=4)=>new()
    {
        OwnerId=owner,ChatId=chat,Name=name,OwnerName=name,Faction=Faction.USA,
        Money=100_000,Iron=100_000,Population=100_000,Soldiers=10_000,
        Cities=cities,FactoryLevel=1,PortLevel=1,MineLevel=1,Welfare=100
    };

    public static void Run()
    {
        string db=Path.Combine(Path.GetTempPath(),$"alice-siege-test-{Guid.NewGuid():N}.db");
        Environment.SetEnvironmentVariable("ALICE_DB_PATH",db);
        try
        {
            Database.Init();
            const long chat=-94001;
            var attacker=NewCountry(1,chat,"Attacker");
            var defender=NewCountry(2,chat,"Defender");
            Database.AddCountry(attacker);Database.AddCountry(defender);
            Database.AddRoutDefeat(defender.OwnerId,chat,attacker.OwnerId,3);
            var progress=Database.GetRoutBattleProgress(attacker.OwnerId,chat).Single();
            Assert(progress.Count==3&&progress.AttackerId==attacker.OwnerId&&progress.DefenderId==defender.OwnerId,
                "ongoing battle list must expose exact city progress");

            Database.SetCities(defender.OwnerId,chat,3);
            Database.SetActiveSiege(defender.OwnerId,chat,attacker.OwnerId,3);
            Country underSiege=Database.GetCountry(defender.OwnerId,chat)!;
            Assert(underSiege.Besieged==1&&Program.HasDefaultCitySiege(underSiege),
                "siege effects must start only after a default city is lost");

            long alliance=Database.AddAlliance(new Alliance{ChatId=chat,Name="Peace",LeaderId=attacker.OwnerId,CreatedAtMs=1});
            Database.AddAllianceMember(alliance,chat,defender.OwnerId);
            Database.RepairSiegeIntegrity();
            Assert(Database.GetRoutBattleProgress(attacker.OwnerId,chat).Count==0,
                "allied countries must not retain conquest progress");
            Assert(Database.GetCountry(defender.OwnerId,chat)!.Besieged==0,
                "alliance must remove the active siege and its effects");

            const long extraChat=-94002;
            var extraAttacker=NewCountry(3,extraChat,"ExtraAttacker");
            var extraDefender=NewCountry(4,extraChat,"ExtraDefender",5);
            Database.AddCountry(extraAttacker);Database.AddCountry(extraDefender);
            Database.SetActiveSiege(extraDefender.OwnerId,extraChat,extraAttacker.OwnerId,5);
            Assert(Database.GetCountry(extraDefender.OwnerId,extraChat)!.Besieged==0,
                "losing a captured bonus city above the four defaults must not cause siege effects");

            Database.SetCities(extraDefender.OwnerId,extraChat,3);
            Database.SetActiveSiege(extraDefender.OwnerId,extraChat,extraAttacker.OwnerId,3);
            Database.DeleteCountry(extraAttacker.OwnerId,extraChat);
            Assert(Database.GetCountry(extraDefender.OwnerId,extraChat)!.Besieged==0,
                "deleting the besieger must immediately release the defender");
            Console.WriteLine($"SIEGE REGRESSION TESTS PASSED — {_assertions} assertions");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach(string suffix in new[]{"","-wal","-shm"})try{File.Delete(db+suffix);}catch{}
            Environment.SetEnvironmentVariable("ALICE_DB_PATH",null);
        }
    }
}
