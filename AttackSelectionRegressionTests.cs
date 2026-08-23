using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

static class AttackSelectionRegressionTests
{
    static int _assertions;
    static void Assert(bool condition,string message)
    {
        _assertions++;if(!condition)throw new InvalidOperationException($"ATTACK SELECTION TEST FAILED: {message}");
    }
    public static void Run()
    {
        string db=Path.Combine(Path.GetTempPath(),$"alice-attack-test-{Guid.NewGuid():N}.db");
        Environment.SetEnvironmentVariable("ALICE_DB_PATH",db);
        try
        {
            Database.Init();
            const long chat=-92001,owner=801;
            var c=new Country{OwnerId=owner,ChatId=chat,Name="Mixed",OwnerName="Mixed",Faction=Faction.USA,
                Money=1_000_000,Iron=1_000_000,Population=100_000,Soldiers=10_000,Tanks=100,Planes=50,Bombers=12,
                DefenseSoldiers=2_000,DefenseTanks=20,DefenseFighters=10,PortLevel=5,MineLevel=1,FactoryLevel=1,Cities=4};
            Database.AddCountry(c);
            Database.AddEquipmentModel(owner,chat,"Tanks","T-28",40);
            Database.AddEquipmentModel(owner,chat,"Tanks","Panzer III",20);
            Database.AddEquipmentModel(owner,chat,"Planes","I-16",20);
            Database.AddEquipmentModel(owner,chat,"Planes","Bf 109",10);
            Database.AddEquipmentModel(owner,chat,"Bombers","DB-3",5);
            Database.AddEquipmentModel(owner,chat,"Bombers","He 111",3);
            Database.ReplaceDefenseModelAmounts(owner,chat,"Tanks",new Dictionary<string,long>{{"M2 Medium",20}});
            Database.ReplaceDefenseModelAmounts(owner,chat,"Planes",new Dictionary<string,long>{{"P-36",10}});
            c=Database.GetCountry(owner,chat)!;

            var tanks=Program.GetAttackBreakdown(c,"tanks").ToDictionary(x=>x.ModelName,x=>x.Count,StringComparer.OrdinalIgnoreCase);
            Assert(tanks.Count==3,"all three tank factions must remain separately selectable");
            Assert(tanks.GetValueOrDefault("M2 Medium")==20,"domestic tank attack count must exclude exact defense reserve");
            Assert(tanks.GetValueOrDefault("T-28")==40&&tanks.GetValueOrDefault("Panzer III")==20,"foreign tank counts must remain exact");
            Assert(tanks.Values.Sum()==80,"exactly 20 percent of 100 tanks must remain in defense");

            var fighters=Program.GetAttackBreakdown(c,"planes").ToDictionary(x=>x.ModelName,x=>x.Count,StringComparer.OrdinalIgnoreCase);
            Assert(fighters.Count==3,"all fighter factions must remain separately selectable");
            Assert(fighters.GetValueOrDefault("P-36")==10&&fighters.GetValueOrDefault("I-16")==20&&fighters.GetValueOrDefault("Bf 109")==10,
                "fighter attack capacities must be model-exact after defense");
            Assert(fighters.Values.Sum()==40,"exactly ten of fifty fighters must remain in defense");
            Assert(Program.GetAttackAvailableSoldiers(c)==8_000,"legacy implicit 100 percent must fall back to the mandatory 20 percent reserve");
            var legacyArmor=new Country{OwnerId=802,ChatId=chat,Name="LegacyArmor",OwnerName="LegacyArmor",Faction=Faction.USA,
                Money=1000,Iron=1000,Population=100_000,Soldiers=10_000,Tanks=100,Planes=50,
                DefenseTanks=100,DefenseFighters=50,DefenseSoldiers=10_000,DefTankPct=100,DefFighterPct=100,DefSoldierPct=100,Cities=4};
            Database.AddCountry(legacyArmor);
            var legacyTankAttack=Program.GetAttackBreakdown(Database.GetCountry(802,chat)!,"tanks");
            var legacyPlaneAttack=Program.GetAttackBreakdown(Database.GetCountry(802,chat)!,"planes");
            Assert(legacyTankAttack.Sum(x=>x.Count)==80,"legacy 100 percent tank default must still leave 80 percent selectable for attack");
            Assert(legacyPlaneAttack.Sum(x=>x.Count)==40,"legacy 100 percent fighter default must still leave 80 percent selectable for attack");
            c.DefenseSoldiers=c.Soldiers;c.DefSoldierPct=100;
            Assert(Program.GetAttackAvailableSoldiers(c)==8_000,"stale full-defense aggregate must not make all soldiers disappear from attack selection");
            Database.SetDefenseSoldierConfigured(owner,chat,true);
            Assert(Program.GetAttackAvailableSoldiers(c)==0,"an explicitly configured 100 percent soldier defense must remain respected");
            Database.SetDefenseSoldierConfigured(owner,chat,false);
            var weak=new Country{Population=1_000,Soldiers=10,Tanks=0,Planes=0,Welfare=100};
            Assert(!Program.PassesAttackTypePowerRule(c,weak,isNaval:true),"one-quarter rule must still block an undersized naval target");
            Assert(Program.PassesAttackTypePowerRule(c,weak,isNaval:false),"one-quarter rule must never block a ground/air attack");

            var selectedTanks=new List<ModelAmount>{new("M2 Medium",10),new("T-28",25),new("Panzer III",5)};
            Assert(Program.ModelSelectionFits(selectedTanks,Program.GetAttackBreakdown(c,"tanks")),"valid mixed tank selection must fit");
            Assert(!Program.ModelSelectionFits(new List<ModelAmount>{new("M2 Medium",21)},Program.GetAttackBreakdown(c,"tanks")),
                "selection above one model capacity must fail even when aggregate total fits");

            var exactBombers=Program.SessionModelAmounts(new(){"DB-3","He 111"},new(){4,2},new(),new(),6,"B-17");
            Assert(exactBombers.Count==2&&exactBombers.Any(x=>x.Model=="DB-3"&&x.Count==4)&&exactBombers.Any(x=>x.Model=="He 111"&&x.Count==2),
                "no-fighter path must preserve each selected bomber model instead of converting to faction default");
            var zeroSelection=Program.SessionModelAmounts(new(){"T-28","Panzer III"},new(){0,0},
                new(){"DB-3"},new(){5},5,"M2 Medium");
            Assert(zeroSelection.Count==0,"explicit all-zero tank selection must not reinterpret the current bomber working list as tanks");
            var consistentSession=new UserSession{AttackTanks=40,AttackFighters=15,AttackBombers=6,AttackSoldiers=8_000};
            Assert(Program.AttackSelectionStateIsConsistent(consistentSession,selectedTanks,
                new List<ModelAmount>{new("P-36",5),new("I-16",10)},exactBombers),"valid selection state must pass consistency check");
            consistentSession.AttackTanks=41;
            Assert(!Program.AttackSelectionStateIsConsistent(consistentSession,selectedTanks,
                new List<ModelAmount>{new("P-36",5),new("I-16",10)},exactBombers),"mismatched session/model total must be rejected");

            var stale=new UserSession{AttackTanks=99,AttackSoldiers=99,AttackFighters=99,AttackBombers=99,
                AttackTankModelNamesFinal=new(){"T-28"},AttackTankModelAmountsFinal=new(){99},
                AttackPlaneModelNamesFinal=new(){"I-16"},AttackPlaneModelAmountsFinal=new(){99},
                AttackBomberModelNamesFinal=new(){"DB-3"},AttackBomberModelAmountsFinal=new(){99},AttackAirStrategy=2,AttackAirTactic=2};
            Program.ResetAttackForceSelection(stale);
            Assert(stale.AttackTanks+stale.AttackSoldiers+stale.AttackFighters+stale.AttackBombers==0,"new attack must clear stale totals");
            Assert(stale.AttackTankModelAmountsFinal.Count==0&&stale.AttackPlaneModelAmountsFinal.Count==0&&stale.AttackBomberModelAmountsFinal.Count==0,
                "new attack must clear every stale per-model list");

            var request=new BattleRequest{BattleId=123,ChatId=chat,ScenarioSeed=123,
                Attackers=new(){new(){OwnerId=owner,CountryName="Mixed",Faction=Faction.USA,Soldiers=8_000,
                    Tanks=selectedTanks,Fighters=new(){new("P-36",5),new("I-16",10)},Bombers=exactBombers}},
                Defenders=new(){new(){OwnerId=999,CountryName="Target",Faction=Faction.Reich,Soldiers=5_000,
                    Tanks=new(){new("Panzer III",30)}}},
                AttackerOrders=new(){GroundStrategy=1,GroundTactic=1,AirStrategy=1,AirTactic=1},
                DefenderOrders=new(){GroundStrategy=1,GroundTactic=1,AirStrategy=1,AirTactic=1}};
            var random=new Random(1939);
            for(int n=0;n<30;n++)
            {
                long id=900+n,total=random.Next(1,501),foreignA=random.NextInt64(0,total+1),foreignB=random.NextInt64(0,total-foreignA+1);
                var fuzz=new Country{OwnerId=id,ChatId=chat,Name=$"Fuzz{id}",OwnerName="Fuzz",Faction=Faction.USA,
                    Money=1000,Iron=1000,Population=100_000,Soldiers=1000,Tanks=total,Planes=0,Bombers=0,Cities=4};
                Database.AddCountry(fuzz);
                if(foreignA>0)Database.AddEquipmentModel(id,chat,"Tanks","T-28",foreignA);
                if(foreignB>0)Database.AddEquipmentModel(id,chat,"Tanks","Panzer III",foreignB);
                var available=Program.GetAttackBreakdown(Database.GetCountry(id,chat)!,"tanks");
                long expected=total-(long)Math.Ceiling(total*0.20);
                Assert(available.Sum(x=>x.Count)==expected,$"fuzz {n}: attack total must equal inventory minus mandatory defense");
                Assert(available.All(x=>x.Count>=0)&&available.GroupBy(x=>x.ModelName,StringComparer.OrdinalIgnoreCase).All(x=>x.Count()==1),
                    $"fuzz {n}: model capacities must be nonnegative and unique");
            }

            BattleResult battle=WarEngine.Resolve(request);
            Assert(battle.AttackerParticipantLosses.ContainsKey(owner),"engine must retain attacker participant identity");
            Assert(battle.AttackerTanksLost<=selectedTanks.Sum(x=>x.Count)&&battle.AttackerFightersLost<=15&&battle.AttackerBombersLost<=6,
                "engine losses must never exceed selected model totals");
            Console.WriteLine($"ATTACK SELECTION REGRESSION TESTS PASSED — {_assertions} assertions");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach(string suffix in new[]{"","-wal","-shm"})try{File.Delete(db+suffix);}catch{}
            Environment.SetEnvironmentVariable("ALICE_DB_PATH",null);
        }
    }
}
