using System;
using System.Collections.Generic;
using System.Linq;

readonly record struct NavalModelAmount(string Model, long Count);

sealed class NavalBattleshipState
{
    public long UnitId { get; init; }
    public string Model { get; init; } = "";
    public int DamagePercent { get; init; }
}

enum NavalOutcomeKind
{
    DefenderNavalVictory,
    Stalemate,
    AttackerNavalVictory,
    AttackerDecisiveNavalVictory,
    EmptyBaseVictory
}

sealed class NavalBattleRequest
{
    public long OperationId { get; init; }
    public ulong Seed { get; init; }
    public string AttackerName { get; init; } = "مهاجم";
    public string DefenderName { get; init; } = "مدافع";
    public int AttackerTactic { get; init; } = 1;
    public int DefenderStrategy { get; init; } = 1;
    public int DefenderTactic { get; init; } = 1;
    public int DefenderPortLevel { get; init; } = 1;
    public long DefenderMoney { get; init; }
    public long DefenderIron { get; init; }
    public List<NavalModelAmount> AttackerBoats { get; init; } = new();
    public List<NavalModelAmount> AttackerSubmarines { get; init; } = new();
    public List<NavalBattleshipState> AttackerBattleships { get; init; } = new();
    public List<NavalModelAmount> DefenderBoats { get; init; } = new();
    public List<NavalModelAmount> DefenderSubmarines { get; init; } = new();
    public List<NavalBattleshipState> DefenderBattleships { get; init; } = new();
}

sealed class NavalBattleshipOutcome
{
    public long UnitId { get; init; }
    public string Model { get; init; } = "";
    public int PreviousDamage { get; init; }
    public int FinalDamage { get; init; }
    public bool Sunk { get; init; }
}

sealed class NavalBattleResult
{
    public NavalOutcomeKind Outcome { get; set; }
    public int SuccessPercent { get; set; }
    public bool AttackerWon { get; set; }
    public bool EmptyBase { get; set; }
    public bool SurpriseSucceeded { get; set; }
    public bool PortLevelDamaged { get; set; }
    public int RivalryWinsAfter { get; set; }
    public long LootMoney { get; set; }
    public long LootIron { get; set; }
    public Dictionary<string, long> AttackerBoatLosses { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, long> AttackerSubmarineLosses { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, long> DefenderBoatLosses { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, long> DefenderSubmarineLosses { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<NavalBattleshipOutcome> AttackerBattleships { get; set; } = new();
    public List<NavalBattleshipOutcome> DefenderBattleships { get; set; } = new();
    public string AttackerReport { get; set; } = "";
    public string DefenderReport { get; set; } = "";
    public string GroupAnnouncement { get; set; } = "";
}

static class NavalEngine
{
    struct Rng
    {
        ulong _state;
        public Rng(ulong seed) => _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
        public ulong NextU()
        {
            _state ^= _state >> 12; _state ^= _state << 25; _state ^= _state >> 27;
            return _state * 2685821657736338717UL;
        }
        public double NextD() => (NextU() >> 11) * (1.0 / (1UL << 53));
        public double Range(double min, double max) => min + NextD() * (max - min);
    }

    public static NavalBattleResult Resolve(NavalBattleRequest request)
    {
        var rng = new Rng(request.Seed);
        double attackerBoatPower = request.AttackerBoats.Sum(x =>
            x.Count * WarEngineV2Core.GetBoatSpecByModel(x.Model).Power);
        double defenderBoatPower = request.DefenderBoats.Sum(x =>
            x.Count * WarEngineV2Core.GetBoatSpecByModel(x.Model).Power);
        double attackerSubPower = request.AttackerSubmarines.Sum(x =>
            x.Count * WarEngineV2Core.GetSubSpecByModel(x.Model).Power);
        double defenderSubPower = request.DefenderSubmarines.Sum(x =>
            x.Count * WarEngineV2Core.GetSubSpecByModel(x.Model).Power);
        double attackerBsPower = request.AttackerBattleships.Sum(BattleshipPower);
        double defenderBsPower = request.DefenderBattleships.Sum(BattleshipPower);
        double attackerInitial = attackerBoatPower + attackerSubPower + attackerBsPower;
        double defenderInitial = defenderBoatPower + defenderSubPower + defenderBsPower;

        var result = new NavalBattleResult { EmptyBase = defenderInitial <= 0.001 };
        if (result.EmptyBase)
        {
            result.Outcome = NavalOutcomeKind.EmptyBaseVictory;
            result.AttackerWon = true;
            result.SuccessPercent = 100;
            result.SurpriseSucceeded = request.AttackerTactic == 1;
            result.LootMoney = (long)Math.Round(request.DefenderMoney * 0.15 * 0.5);
            result.LootIron = (long)Math.Round(request.DefenderIron * 0.10 * 0.5);
            BuildReports(request, result, attackerInitial, defenderInitial);
            return result;
        }

        double surpriseChance = request.AttackerTactic == 1 ? 0.58 : 0.18;
        if (request.DefenderStrategy == 1 && request.DefenderTactic == 1) surpriseChance -= 0.28;
        if (request.DefenderStrategy == 1 && request.DefenderTactic == 2) surpriseChance -= 0.13;
        if (request.DefenderStrategy == 2 && request.DefenderTactic == 2) surpriseChance -= 0.05;
        surpriseChance -= Math.Min(0.12, request.DefenderPortLevel * 0.02);
        result.SurpriseSucceeded = rng.NextD() < Math.Clamp(surpriseChance, 0.05, 0.85);

        double attackerModifier = request.AttackerTactic == 1
            ? (result.SurpriseSucceeded ? 1.24 : 0.94) : 1.10;
        double defenderModifier = 1.0;
        double attackerLossBias = 0, defenderLossBias = 0;
        switch (request.DefenderStrategy, request.DefenderTactic)
        {
            case (1, 1):
                defenderModifier *= 1.18 + Math.Min(0.10, request.DefenderPortLevel * 0.02);
                attackerModifier *= 0.90; attackerLossBias += 0.08; defenderLossBias -= 0.04;
                break;
            case (1, 2):
                defenderModifier *= 1.10; attackerLossBias += 0.04; defenderLossBias += 0.04;
                break;
            case (2, 1):
                defenderModifier *= 1.03; attackerModifier *= 0.92; defenderLossBias -= 0.10;
                break;
            case (2, 2):
                defenderModifier *= 1.0 + Math.Min(0.25, defenderSubPower / Math.Max(1, defenderInitial) * 0.35);
                attackerLossBias += 0.11; defenderLossBias += result.SurpriseSucceeded ? 0.04 : -0.03;
                break;
        }
        if (request.AttackerTactic == 2) { attackerLossBias += 0.04; defenderLossBias += 0.06; }
        if (result.SurpriseSucceeded) defenderLossBias += 0.10;

        double attackerEffective = attackerInitial * attackerModifier * rng.Range(0.92, 1.08);
        double defenderEffective = defenderInitial * defenderModifier * rng.Range(0.92, 1.08);
        double attackerShare = attackerEffective / Math.Max(1, attackerEffective + defenderEffective);
        result.SuccessPercent = (int)Math.Round(attackerShare * 100);
        result.AttackerWon = result.SuccessPercent >= 55;
        result.Outcome = result.SuccessPercent >= 78
            ? NavalOutcomeKind.AttackerDecisiveNavalVictory
            : result.SuccessPercent >= 55 ? NavalOutcomeKind.AttackerNavalVictory
            : result.SuccessPercent >= 46 ? NavalOutcomeKind.Stalemate
            : NavalOutcomeKind.DefenderNavalVictory;

        double attackerLossFraction = Math.Clamp((1 - attackerShare) * 0.68 + attackerLossBias, 0.02, 0.88);
        double defenderLossFraction = Math.Clamp(attackerShare * 0.72 + defenderLossBias, 0.02, 0.94);
        AllocateLosses(request.AttackerBoats, attackerLossFraction, false, result.AttackerBoatLosses, ref rng);
        AllocateLosses(request.AttackerSubmarines, attackerLossFraction * 0.90, true, result.AttackerSubmarineLosses, ref rng);
        AllocateLosses(request.DefenderBoats, defenderLossFraction, false, result.DefenderBoatLosses, ref rng);
        AllocateLosses(request.DefenderSubmarines, defenderLossFraction * 0.90, true, result.DefenderSubmarineLosses, ref rng);

        ResolveBattleships(request.AttackerBattleships, defenderEffective, attackerEffective,
            result.SurpriseSucceeded && request.DefenderStrategy == 2 && request.DefenderTactic == 2,
            result.AttackerBattleships, ref rng);
        ResolveBattleships(request.DefenderBattleships, attackerEffective, defenderEffective,
            result.SurpriseSucceeded, result.DefenderBattleships, ref rng);

        if (result.AttackerWon)
        {
            double success = result.SuccessPercent / 100.0;
            result.LootMoney = Math.Min(request.DefenderMoney,
                (long)Math.Round(request.DefenderMoney * 0.15 * success));
            result.LootIron = Math.Min(request.DefenderIron,
                (long)Math.Round(request.DefenderIron * 0.10 * success));
        }
        BuildReports(request, result, attackerInitial, defenderInitial);
        return result;
    }

    static double BattleshipPower(NavalBattleshipState unit)
    {
        var spec = WarEngineV2Core.GetBattleshipSpecByModel(unit.Model);
        double d = Math.Clamp(unit.DamagePercent / 100.0, 0, 1);
        double fire = 1 - 0.70 * d;
        double armor = 1 - 0.35 * d;
        double speed = 1 - 0.50 * d;
        return spec.Power * (fire * 0.55 + armor * 0.30 + speed * 0.15);
    }

    static void AllocateLosses(IEnumerable<NavalModelAmount> units, double fraction, bool submarine,
        Dictionary<string, long> output, ref Rng rng)
    {
        foreach (var unit in units.Where(x => x.Count > 0))
        {
            double survivability;
            if(submarine)
            {
                var spec=WarEngineV2Core.GetSubSpecByModel(unit.Model);
                survivability=Math.Clamp(1.22-spec.Stealth/260.0-spec.Armor/180.0,0.68,1.10);
            }
            else
            {
                var spec=WarEngineV2Core.GetBoatSpecByModel(unit.Model);
                survivability=Math.Clamp(1.18-spec.Speed/260.0-spec.Armor/55.0,0.70,1.12);
            }
            double varied = Math.Clamp(fraction * survivability * rng.Range(0.86, 1.14), 0, 0.98);
            long loss = Math.Min(unit.Count, (long)Math.Round(unit.Count * varied));
            if (loss > 0) output[unit.Model] = output.GetValueOrDefault(unit.Model) + loss;
        }
    }

    static void ResolveBattleships(IEnumerable<NavalBattleshipState> units,
        double enemyPower, double ownPower, bool tacticalTrap,
        List<NavalBattleshipOutcome> output, ref Rng rng)
    {
        double ratio = enemyPower / Math.Max(1, ownPower);
        foreach (var unit in units)
        {
            double pressure = Math.Clamp(ratio, 0.15, 5.0);
            int added = (int)Math.Round(rng.Range(7, 22) * Math.Sqrt(pressure) * (tacticalTrap ? 1.25 : 1));
            int finalDamage = Math.Clamp(unit.DamagePercent + added, 0, 99);
            bool sinkEligible = ratio >= 3.0 && tacticalTrap && unit.DamagePercent >= 50;
            bool sunk = sinkEligible && unit.DamagePercent + added >= 100 &&
                        rng.NextD() < Math.Clamp(0.20 + (ratio - 3) * 0.15, 0.20, 0.75);
            output.Add(new NavalBattleshipOutcome
            {
                UnitId = unit.UnitId,
                Model = unit.Model,
                PreviousDamage = unit.DamagePercent,
                FinalDamage = sunk ? 100 : finalDamage,
                Sunk = sunk
            });
        }
    }

    static void BuildReports(NavalBattleRequest q, NavalBattleResult r,
        double attackPower, double defensePower)
    {
        string outcome = r.Outcome switch
        {
            NavalOutcomeKind.EmptyBaseVictory => "⚓ پیروزی در حمله به پایگاه خالی",
            NavalOutcomeKind.AttackerDecisiveNavalVictory => $"🏆 پیروزی دریایی قاطع {q.AttackerName}",
            NavalOutcomeKind.AttackerNavalVictory => $"⚓ پیروزی دریایی {q.AttackerName}",
            NavalOutcomeKind.Stalemate => "⚖️ نبرد دریایی بدون برنده قاطع",
            _ => $"🛡 پیروزی دریایی {q.DefenderName}"
        };
        string tactic = q.AttackerTactic == 1
            ? "حمله غافلگیرانه به پایگاه دریایی" : "نبرد تعیین‌کننده";
        string defense = (q.DefenderStrategy, q.DefenderTactic) switch
        {
            (1, 1) => "استحکامات، توپخانه ساحلی و میدان مین",
            (1, 2) => "خروج سریع ناوگان و ضدحمله",
            (2, 1) => "حمله و عقب‌نشینی",
            _ => "کمین دریایی"
        };
        long aBoat = r.AttackerBoatLosses.Values.Sum(), aSub = r.AttackerSubmarineLosses.Values.Sum();
        long dBoat = r.DefenderBoatLosses.Values.Sum(), dSub = r.DefenderSubmarineLosses.Values.Sum();
        int aSunk = r.AttackerBattleships.Count(x => x.Sunk), dSunk = r.DefenderBattleships.Count(x => x.Sunk);
        string surprise = r.SurpriseSucceeded ? "موفق" : "ناموفق";
        r.AttackerReport = $"⚔️ گزارش نبرد دریایی — {q.AttackerName} علیه {q.DefenderName}\n{outcome}\n" +
            $"🎯 تاکتیک: {tactic}\n🛡 دفاع دشمن: {defense}\n🔎 غافلگیری: {surprise}\n" +
            $"📊 موفقیت: {r.SuccessPercent}٪ | قدرت آغازین: {attackPower:F0} برابر {defensePower:F0}\n" +
            $"🔻 تلفات شما: {aBoat:N0} قایق، {aSub:N0} زیردریایی، {aSunk} نبردناو\n" +
            $"🔻 تلفات دشمن: {dBoat:N0} قایق، {dSub:N0} زیردریایی، {dSunk} نبردناو\n" +
            $"💰 غنیمت: {r.LootMoney:N0} پول، {r.LootIron:N0} آهن";
        r.DefenderReport = $"🛡 گزارش دفاع دریایی — {q.DefenderName}\n{outcome}\n" +
            $"🎯 حمله دشمن: {tactic}\n🛡 آرایش شما: {defense}\n" +
            $"🔻 تلفات شما: {dBoat:N0} قایق، {dSub:N0} زیردریایی، {dSunk} نبردناو\n" +
            $"🔻 تلفات دشمن: {aBoat:N0} قایق، {aSub:N0} زیردریایی، {aSunk} نبردناو\n" +
            $"💸 منابع ازدست‌رفته: {r.LootMoney:N0} پول، {r.LootIron:N0} آهن";
        r.GroupAnnouncement = $"📰 نبرد دریایی!\n⚓ {q.AttackerName} علیه {q.DefenderName}\n{outcome}\n" +
            $"📊 موفقیت مهاجم: {r.SuccessPercent}٪\n" +
            $"💀 مهاجم: {aBoat}🚤 {aSub}⚓ {aSunk}🚢 | مدافع: {dBoat}🚤 {dSub}⚓ {dSunk}🚢";
    }
}
