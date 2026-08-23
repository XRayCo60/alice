using System;
using System.Collections.Generic;

static class StrategicBattleRegressionTests
{
    static int _assertions;
    static void Assert(bool condition,string message){_assertions++;if(!condition)throw new InvalidOperationException($"STRATEGIC TEST FAILED: {message}");}
    public static void Run()
    {
        Assert(Math.Abs(WarEngine.MapOperationalToStrategicAdvance(30)-40)<0.001,"30 operational km must map to 40 strategic km");
        Assert(Math.Abs(WarEngine.MapOperationalToStrategicAdvance(26.25)-35)<0.001,"heavy-victory boundary must map correctly");
        Assert(WarEngine.IsStrategicHeavyVictory(true,100,40,5000,50),"qualified victory must be heavy");
        Assert(!WarEngine.IsStrategicHeavyVictory(true,89,40,5000,50),"success below 90 must not be heavy");
        Assert(!WarEngine.IsStrategicHeavyVictory(true,100,34.9,5000,50),"advance below 35 must not be heavy");
        Assert(!WarEngine.IsStrategicHeavyVictory(true,100,40,4999,50),"insufficient ready soldiers must not be heavy");
        Assert(!WarEngine.IsStrategicHeavyVictory(true,100,40,5000,49),"insufficient ready tanks must not be heavy");
        var request=new BattleRequest{BattleId=9001,ChatId=-1,ScenarioSeed=9001,
            Attackers=new(){new(){OwnerId=1,CountryName="Attacker",Faction=Faction.USA,Soldiers=6000,Tanks=new(){new("M2 Medium",100)}}},
            Defenders=new(){new(){OwnerId=2,CountryName="Empty",Faction=Faction.Reich,Soldiers=0}},
            AttackerOrders=new(){GroundStrategy=1,GroundTactic=1},DefenderOrders=new(){GroundStrategy=1,GroundTactic=1}};
        BattleResult result=WarEngine.Resolve(request);
        Assert(result.AttackerWon,"unopposed full advance must be an attacker victory");
        Assert(result.EffectiveAdvanceKm>=35,"unopposed operational advance must reach strategic heavy threshold");
        Assert(result.AttackerHeavyVictory,"unopposed qualified force must activate city-progress heavy victory");
        Console.WriteLine($"STRATEGIC BATTLE REGRESSION TESTS PASSED — {_assertions} assertions");
    }
}
