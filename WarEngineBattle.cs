// ============================================================================
//  WarEngineBattle.cs — حلقه‌ی اصلی نبرد زمینی + جمع‌بندی تلفات + غنیمت
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

static partial class WarEngine
{
    // ═════════════════════════════ API عمومی ════════════════════════════════
    public static BattleResult RunBattle(Country attacker, Country defender,
        long tanks, long soldiers, int strategy, int tactic)
        => RunBattle(attacker, defender, tanks, soldiers, 0, 0, strategy, tactic, 0, 0);

    public static BattleResult RunBattle(Country attacker, Country defender,
        long tanks, long soldiers, long fighters, long bombers,
        int strategy, int tactic, int airStrategy, int airTactic)
    {
        var atkTanks = new List<(string Model, long Count)>();
        if (tanks > 0) atkTanks.Add((DefaultTankModel(attacker.Faction), tanks));
        var atkFighters = new List<(string Model, long Count)>();
        if (fighters > 0) atkFighters.Add((DefaultFighterModel(attacker.Faction), fighters));
        var atkBombers = new List<(string Model, long Count)>();
        if (bombers > 0) atkBombers.Add((DefaultBomberModel(attacker.Faction), bombers));
        return RunBattleAdvanced(attacker, defender, atkTanks, soldiers, atkFighters, atkBombers,
            null, 0, null, strategy, tactic, airStrategy, airTactic);
    }

    public struct BattleOrder
    {
        public Country Attacker, Defender;
        public long Tanks, Soldiers, Fighters, Bombers;
        public int Strategy, Tactic, AirStrategy, AirTactic;
    }

    public static BattleResult[] RunBattlesParallel(BattleOrder[] orders)
    {
        var results = new BattleResult[orders.Length];
        Parallel.For(0, orders.Length, i =>
        {
            var o = orders[i];
            results[i] = RunBattle(o.Attacker, o.Defender, o.Tanks, o.Soldiers, o.Fighters, o.Bombers,
                                   o.Strategy, o.Tactic, o.AirStrategy, o.AirTactic);
        });
        return results;
    }

    // سازگاری عقب‌رو با امضای قدیمی
    public static BattleResult RunBattleSeeded(Country attacker, Country defender,
        long reqTanks, long reqSoldiers, long reqFighters, long reqBombers,
        int strategy, int tactic, int airStrategy, int airTactic, ulong seed)
    {
        var atkTanks = new List<(string Model, long Count)>();
        if (reqTanks > 0) atkTanks.Add((DefaultTankModel(attacker.Faction), reqTanks));
        var atkFighters = new List<(string Model, long Count)>();
        if (reqFighters > 0) atkFighters.Add((DefaultFighterModel(attacker.Faction), reqFighters));
        var atkBombers = new List<(string Model, long Count)>();
        if (reqBombers > 0) atkBombers.Add((DefaultBomberModel(attacker.Faction), reqBombers));
        return RunBattleAdvancedSeeded(attacker, defender, atkTanks, reqSoldiers, atkFighters, atkBombers,
            null, 0, null, strategy, tactic, airStrategy, airTactic, seed);
    }

    static string DefaultTankModel(Faction f) => f switch { Faction.USA => "M2 Medium", Faction.USSR => "T-28", _ => "Panzer III" };
    static string DefaultFighterModel(Faction f) => f switch { Faction.USA => "P-36", Faction.USSR => "I-16", _ => "Bf 109" };
    static string DefaultBomberModel(Faction f) => f switch { Faction.USA => "B-17", Faction.USSR => "DB-3", _ => "He 111" };

    public static BattleResult RunBattleAdvanced(
        Country attacker, Country defender,
        List<(string Model, long Count)> attTankBreakdown, long attSoldiers,
        List<(string Model, long Count)> attFighterBreakdown, List<(string Model, long Count)> attBomberBreakdown,
        List<(string Model, long Count)> defTankBreakdown, long defSoldiers,
        List<(string Model, long Count)> defFighterBreakdown,
        int strategy, int tactic, int airStrategy, int airTactic)
    {
        ulong seed = (ulong)Interlocked.Increment(ref _seedCounter)
                   ^ ((ulong)attacker.OwnerId << 20) ^ (ulong)DateTime.UtcNow.Ticks;
        return RunBattleAdvancedSeeded(attacker, defender,
            attTankBreakdown, attSoldiers, attFighterBreakdown, attBomberBreakdown,
            defTankBreakdown, defSoldiers, defFighterBreakdown,
            strategy, tactic, airStrategy, airTactic, seed);
    }

    // ═════════════════════════ هسته‌ی نبرد ══════════════════════════════════
    public static BattleResult RunBattleAdvancedSeeded(
        Country attacker, Country defender,
        List<(string Model, long Count)> attTankBreakdown, long attSoldiers,
        List<(string Model, long Count)> attFighterBreakdown, List<(string Model, long Count)> attBomberBreakdown,
        List<(string Model, long Count)> defTankBreakdown, long defSoldiers,
        List<(string Model, long Count)> defFighterBreakdown,
        int strategy, int tactic, int airStrategy, int airTactic, ulong seed)
    {
        var rng = new XorRng(seed);
        var res = new BattleResult();
        var log = new BattleLog();

        // ── نیروهای اعزامی ───────────────────────────────────────────────────
        var aTankList = Normalize(attTankBreakdown, attacker.Tanks);
        long aTanks = aTankList.Sum(x => x.Count);
        long aSold = Math.Max(0, Math.Min(attSoldiers, attacker.Soldiers));

        var aFighterList = Normalize(attFighterBreakdown, attacker.Planes);
        long aFight = aFighterList.Sum(x => x.Count);
        var aBomberList = Normalize(attBomberBreakdown, attacker.Bombers);
        long aBomb = aBomberList.Sum(x => x.Count);

        // مدافع ممکن است نیروی «صف‌آرایی» متحدان را هم داشته باشد که در دارایی خودش نیست → سقف نمی‌گذاریم
        var dTankList = Normalize(defTankBreakdown, long.MaxValue);
        long dTanks = dTankList.Sum(x => x.Count);
        if (dTanks <= 0)
        {
            long auto = Math.Min(defender.Tanks, Math.Max(defender.DefenseTanks, (long)Math.Ceiling(defender.Tanks * 0.2)));
            if (auto > 0) { dTankList = new List<(string Model, long Count)> { (DefaultTankModel(defender.Faction), auto) }; dTanks = auto; }
        }
        long dSold = defSoldiers > 0
            ? defSoldiers
            : Math.Min(defender.Soldiers, Math.Max(defender.DefenseSoldiers, (long)Math.Ceiling(defender.Soldiers * 0.2)));

        var dFighterList = Normalize(defFighterBreakdown, long.MaxValue);
        long dFight = dFighterList.Sum(x => x.Count);
        if (dFight <= 0)
        {
            long autoF = Math.Min(defender.Planes, defender.DefenseFighters);
            if (autoF > 0) { dFighterList = new List<(string Model, long Count)> { (DefaultFighterModel(defender.Faction), autoF) }; dFight = autoF; }
        }
        long dAA = Math.Max(0, defender.AntiAir);

        int aStrat = strategy == 2 ? 2 : 1, aTac = tactic == 2 ? 2 : 1;
        int dStrat = defender.DefenseStrategy == 2 ? 2 : 1, dTac = defender.DefenseTactic == 2 ? 2 : 1;
        int aAirStrat = airStrategy == 2 ? 2 : (airStrategy == 1 ? 1 : 0);
        int aAirTac = airTactic == 2 ? 2 : 1;
        int dAirStrat = defender.AirDefStrategy == 2 ? 2 : 1;
        int dAirTac = defender.AirDefTactic == 2 ? 2 : 1;

        bool anyGround = (aTanks + aSold) > 0;
        bool anyAir = (aFight + aBomb) > 0;
        if (!anyGround && !anyAir)
        {
            res.AttackerReport = "⚠️ هیچ نیرویی اعزام نشد؛ حمله انجام نشد.";
            res.GroupAnnouncement = $"⚔️ حمله {attacker.Name} به {defender.Name} به دلیل نبود نیرو لغو شد.";
            res.AttackerFailed = true;
            return res;
        }

        var aProf = ProfileOf(attacker.Faction);
        var dProf = ProfileOf(defender.Faction);
        var field = GenField(ref rng);

        log.Add(0, 2, LG_ENV, $"نبرد در هوای {WeatherName[field.Weather]} و در {TimeName[field.StartTime]} آغاز شد.");

        // ── فاز هوایی ────────────────────────────────────────────────────────
        var aFs = aFighterList.Count > 0 ? GetFighterSpecByModel(aFighterList[0].Model) : FighterOf(attacker.Faction);
        var aBs = aBomberList.Count > 0 ? GetBomberSpecByModel(aBomberList[0].Model) : BomberOf(attacker.Faction);
        var dFs = dFighterList.Count > 0 ? GetFighterSpecByModel(dFighterList[0].Model) : FighterOf(defender.Faction);

        AirOutcome air = RunAirPhase(attacker, defender, field, aFight, aBomb, aAirStrat, aAirTac,
            dFight, dAA, dAirStrat, dAirTac, aFs, aBs, dFs, aProf, dProf, ref rng);

        res.AttackerFightersLost = air.AtkFightersLost;
        res.AttackerBombersLost = air.AtkBombersLost;
        res.DefenderFightersLost = air.DefFightersLost;
        res.DefenderAntiAirLost = air.DefAntiAirLost;
        res.AirSuperiority = Math.Round(air.Superiority, 2);
        if (air.Narrative != null) log.Add(2, 2, LG_AIR, air.Narrative);

        DistributeLoss(res.AttackerPlaneLossByModel, aFighterList, air.AtkFightersLost);
        DistributeLoss(res.AttackerBomberLossByModel, aBomberList, air.AtkBombersLost);
        DistributeLoss(res.DefenderPlaneLossByModel, dFighterList, air.DefFightersLost);

        // ── نبرد زمینی ───────────────────────────────────────────────────────
        bool defHasGround = (dTanks + dSold) > 0;
        float effDepth = 0f;
        int tick = 0, routsA = 0, routsD = 0;
        bool contact = false, ambushFired = false;
        Force fa = null, fd = null;
        float stratAdv = 1f;

        if (!anyGround)
        {
            tick = 30;
        }
        else if (!defHasGround)
        {
            fa = BuildForce(attacker.Faction, true, aTankList, aSold, aStrat, aTac, field, ref rng);
            float airDrag = air.Superiority < -0.15f ? Math.Clamp(-air.Superiority, 0f, 1f) * 0.25f : 0f;
            effDepth = WIN_DEPTH * (1f - airDrag);
            for (int i = 0; i < fa.N; i++)
            {
                float attrition = fa.G[i].Size0 * 0.02f;
                fa.G[i].Units = Math.Max(0f, fa.G[i].Units - attrition);
                if (fa.G[i].Type == 1) fa.ModelKnocked[fa.G[i].Model] += attrition; else fa.SoldiersKnocked += attrition;
            }
            tick = 60;
            log.Add(10, 2, LG_COMBAT, "مدافع هیچ نیروی زمینی در خط نداشت؛ ستون‌های مهاجم عملاً بی‌مقاومت پیش رفتند.");
        }
        else
        {
            fa = BuildForce(attacker.Faction, true, aTankList, aSold, aStrat, aTac, field, ref rng);
            fd = BuildForce(defender.Faction, false, dTankList, dSold, dStrat, dTac, field, ref rng);
            fd.Cmd = InitCommander(false, dStrat, dTac, ref rng);

            for (int i = 0; i < MAX_GROUPS; i++)
            {
                fa.IntelOnFoe[i] = default; fa.IntelOnFoe[i].Stale = 9999f;
                fd.IntelOnFoe[i] = default; fd.IntelOnFoe[i].Stale = 9999f;
            }

            stratAdv = DoctrineMatchup(fa, fd, field, ref rng, log);

            float aPow0 = SidePower(fa), dPow0 = SidePower(fd);
            float casA = air.CasAtk, casD = air.CasDef;
            if (air.Superiority > 0.05f) casA *= 1f + Math.Clamp(air.Superiority, 0f, 1f) * 0.45f;
            else if (air.Superiority < -0.05f) casD *= 1f + Math.Clamp(-air.Superiority, 0f, 1f) * 0.45f;
            if (air.Superiority > 0.25f) casD *= 1f - Math.Clamp(air.Superiority - 0.25f, 0f, 0.5f) * 0.4f;
            else if (air.Superiority < -0.25f) casA *= 1f - Math.Clamp(-air.Superiority - 0.25f, 0f, 0.5f) * 0.4f;
            if (defender.Cities <= 0) casD *= 1.5f;

            // ابتکار عمل مهاجم: زمان و مکان حمله را او انتخاب کرده است
            int surpriseTicks = (int)(18 + fa.Cmd.Aggression * 14);
            float prevMomentum = 0f; int haltTicks = 0; float peakDepth = 0f;
            bool loggedBreak5 = false, loggedBreak10 = false, loggedBreak20 = false, loggedCollapse = false;
            bool loggedSupply = false, loggedShift = false;

            for (tick = 0; tick < MAX_TICKS; tick++)
            {
                byte tnow = field.TimeAt(tick);
                float visEnv = WxVision[field.Weather] * TimeVision[tnow];
                float accEnv = WxAcc[field.Weather];

                SenseSide(fa, fd, field, aStrat == 1 && aTac == 2, visEnv, ref rng);
                float defVis = visEnv * (tick < surpriseTicks ? 0.62f : 1f);   // غافلگیری اولیه
                SenseSide(fd, fa, field, dStrat == 2, defVis, ref rng);

                if (tick % aProf.CommandTempo == 0)
                {
                    BuildThreatMap(fa, fd);
                    CommandAttacker(fa, fd, field, effDepth, tick, log, ref rng);
                }
                if (tick % dProf.CommandTempo == 0)
                {
                    BuildThreatMap(fd, fa);
                    CommandDefender(fd, fa, field, effDepth, tick, log, ref rng);
                }

                MoveSide(fa, fd, field, ref rng);
                MoveSide(fd, fa, field, ref rng);

                float supplyA = SupplyFactor(effDepth, aProf);
                if (!loggedSupply && supplyA < 0.85f)
                {
                    loggedSupply = true;
                    log.Add(tick, 0, LG_CRISIS, $"کشش خط تدارکات در عمق {effDepth:F1} کیلومتری خودش را نشان داد و آهنگ پیشروی کند شد.");
                }

                float surprise = tick < surpriseTicks ? 1.12f : 1f;
                float aMul = casA * stratAdv * supplyA * surprise;
                float dMul = casD * (tick < surpriseTicks ? 0.90f : 1f);

                FireSide(fa, fd, field, true, aMul, accEnv, tick, log, ref rng, ref contact, ref ambushFired);
                FireSide(fd, fa, field, false, dMul, accEnv, tick, log, ref rng, ref contact, ref ambushFired);

                int rA = 0, rD = 0;
                MoraleSide(fa, field, tick, log, ref rng, ref rA);
                MoraleSide(fd, field, tick, log, ref rng, ref rD);
                if (rA > 0 && routsA == 0) log.Add(tick, 2, LG_CRISIS, "چند گروه مهاجم پس از تلفات سنگین خط را رها کردند و عقب کشیدند.");
                if (rD > 0 && routsD == 0) log.Add(tick, 2, LG_CRISIS, "بخشی از یگان‌های مدافع تاب نیاوردند و از مواضع خود گریختند.");
                routsA += rA; routsD += rD;

                float d = EffectiveDepth(fa, fd);
                if (d > effDepth)
                {
                    float prev = effDepth; effDepth = d; haltTicks = 0;
                    if (!loggedBreak5 && prev < 5f && d >= 5f) { loggedBreak5 = true; log.Add(tick, 2, LG_BREAK, "خط اول دفاع شکست و رخنه‌ی پنج‌کیلومتری باز شد."); }
                    if (!loggedBreak10 && prev < 10f && d >= 10f) { loggedBreak10 = true; log.Add(tick, 2, LG_BREAK, "رخنه به عمق ده کیلومتر توسعه یافت و ذخیره‌ی مدافع زیر فشار رفت."); }
                    if (!loggedBreak20 && prev < 20f && d >= 20f) { loggedBreak20 = true; log.Add(tick, 2, LG_BREAK, "ستون زرهی مهاجم به عمق بیست کیلومتری رسید؛ پشت جبهه‌ی مدافع در تیررس افتاد."); }
                    if (!loggedCollapse && prev < 28f && d >= 28f) { loggedCollapse = true; log.Add(tick, 2, LG_BREAK, "عمق سی کیلومتر درنوردیده شد — جبهه‌ی مدافع فرو ریخت."); }
                }
                else haltTicks++;
                if (d > peakDepth) peakDepth = d;

                float aPow = SidePower(fa), dPow = SidePower(fd);
                float momentum = (aPow / Math.Max(1f, aPow0)) - (dPow / Math.Max(1f, dPow0));
                if (!loggedShift && tick > 20 && prevMomentum >= 0 && momentum < -0.12f)
                {
                    loggedShift = true;
                    log.Add(tick, 2, LG_CRISIS, "نقطه‌ی عطف نبرد: ابتکار عمل از دست مهاجم خارج شد.");
                }
                prevMomentum = momentum;

                if (effDepth >= WIN_DEPTH) { tick++; break; }
                if (aPow < aPow0 * 0.13f) { log.Add(tick, 2, LG_CRISIS, "توان رزمی مهاجم به آستانه‌ی فروپاشی رسید و حمله متوقف شد."); tick++; break; }
                if (dPow < dPow0 * 0.10f && effDepth > 6f)
                {
                    effDepth = Math.Min(WIN_DEPTH, effDepth + (WIN_DEPTH - effDepth) * 0.7f);
                    log.Add(tick, 2, LG_BREAK, "مقاومت سازمان‌یافته‌ی مدافع از هم پاشید و باقی‌مانده‌ی خط تار و مار شد.");
                    tick++; break;
                }
                if (haltTicks > 85 && contact)
                {
                    log.Add(tick, 2, LG_CRISIS, $"پیشروی در عمق {effDepth:F1} کیلومتری زمین‌گیر شد و جبهه به بن‌بست رسید.");
                    tick++; break;
                }
            }

            if (effDepth >= 22f && effDepth < WIN_DEPTH)
                effDepth = Math.Min(WIN_DEPTH, effDepth + (WIN_DEPTH - effDepth) * 0.5f);
            else if (effDepth <= 6f && effDepth > FAIL_DEPTH)
                effDepth = Math.Max(0f, effDepth - (effDepth - FAIL_DEPTH) * 0.5f);
        }

        // ── نتیجه ────────────────────────────────────────────────────────────
        float frac = Math.Clamp((effDepth - FAIL_DEPTH) / (WIN_DEPTH - FAIL_DEPTH), 0f, 1f);
        int success = (int)Math.Round(frac * 100);
        bool absWin = anyGround && effDepth >= WIN_DEPTH;
        bool absFail = anyGround && effDepth < FAIL_DEPTH;

        // ── بازیابی تجهیزات: هرکه میدان را نگه داشت، بخشی از خسارتش برمی‌گردد ─
        float aRecover = 0f, dRecover = 0f;
        if (anyGround && defHasGround)
        {
            aRecover = aProf.Recovery * Math.Clamp(frac * 1.15f, 0f, 1f);
            dRecover = dProf.Recovery * Math.Clamp(1f - frac * 1.15f, 0f, 1f);
        }
        else if (anyGround) aRecover = aProf.Recovery;

        long aTankLoss = 0, aSoldLoss = 0, dTankLoss = 0, dSoldLoss = 0;
        if (fa != null) FinalizeLosses(fa, aRecover, res.AttackerTankLossByModel, out aTankLoss, out aSoldLoss);
        if (fd != null) FinalizeLosses(fd, dRecover, res.DefenderTankLossByModel, out dTankLoss, out dSoldLoss);

        aTankLoss = Math.Min(aTankLoss, aTanks); aSoldLoss = Math.Min(aSoldLoss, aSold);
        dTankLoss = Math.Min(dTankLoss, dTanks); dSoldLoss = Math.Min(dSoldLoss, dSold);

        long lootMoney = (long)(defender.Money * 0.15 * frac);
        long lootIron = (long)(defender.Iron * 0.10 * frac);
        lootMoney = Math.Min(lootMoney, defender.Money);
        lootIron = Math.Min(lootIron, defender.Iron);
        long stratMoney = Math.Min(air.StratMoney, Math.Max(0, defender.Money - lootMoney));
        long stratIron = Math.Min(air.StratIron, Math.Max(0, defender.Iron - lootIron));

        res.AttackerTanksLost = aTankLoss;
        res.AttackerSoldiersLost = aSoldLoss;
        res.DefenderTanksLost = dTankLoss;
        res.DefenderSoldiersLost = dSoldLoss;
        res.AttackerMoneyGained = lootMoney;
        res.AttackerIronGained = lootIron;
        res.DefenderMoneyLost = lootMoney + stratMoney;
        res.DefenderIronLost = lootIron + stratIron;
        res.PenetrationKm = Math.Round(effDepth, 1);
        res.SuccessPercent = success;
        res.AttackerWon = absWin;
        res.AttackerFailed = absFail || (!anyGround && air.Superiority < -0.35);
        res.DurationMinutes = Math.Max(30, (int)(tick * TICK_MIN));

        double aLossR = (aTanks + aSold) > 0 ? (aTankLoss * 10.0 + aSoldLoss) / Math.Max(1.0, aTanks * 10.0 + aSold) : 0;
        double dLossR = (dTanks + dSold) > 0 ? (dTankLoss * 10.0 + dSoldLoss) / Math.Max(1.0, dTanks * 10.0 + dSold) : 0;
        res.AttackerWelfareChange = -Math.Clamp(aLossR * 2.0 + (absFail ? 1.0 : 0), 0, 3);
        res.DefenderWelfareChange = -Math.Clamp(dLossR * 2.0 + (absWin ? 1.5 : 0) + frac * 0.8 + air.StratWelfare * 0.3, 0, 4);

        BuildGroundReports(res, attacker, defender, fa, fd, field, log, air,
            aStrat, aTac, dStrat, dTac, aAirStrat, aAirTac, dAirStrat, dAirTac,
            aTankList, dTankList, aFighterList, dFighterList, aBomberList,
            aTanks, aSold, dTanks, dSold, aFight, aBomb, dFight, dAA,
            frac, effDepth, stratAdv, anyGround, defHasGround, aRecover, dRecover, routsA, routsD);

        SaveBattle(attacker, defender, res);
        return res;
    }

    static List<(string Model, long Count)> Normalize(List<(string Model, long Count)> src, long cap)
    {
        var outList = new List<(string Model, long Count)>();
        if (src == null) return outList;
        long left = Math.Max(0, cap);
        foreach (var (m, c) in src)
        {
            if (c <= 0) continue;
            long take = Math.Min(c, left);
            if (take <= 0) break;
            outList.Add((string.IsNullOrWhiteSpace(m) ? "نامشخص" : m, take));
            left -= take;
        }
        return outList;
    }

    static void DistributeLoss(Dictionary<string, long> dict, List<(string Model, long Count)> list, long total)
    {
        if (list == null || list.Count == 0 || total <= 0) return;
        long sum = list.Sum(x => x.Count);
        if (sum <= 0) return;
        long assigned = 0;
        for (int i = 0; i < list.Count; i++)
        {
            long share = i == list.Count - 1 ? total - assigned : (long)Math.Round((double)list[i].Count / sum * total);
            share = Math.Max(0, Math.Min(share, list[i].Count));
            assigned += share;
            if (share > 0) dict[list[i].Model] = dict.TryGetValue(list[i].Model, out var v) ? v + share : share;
        }
    }

    static void FinalizeLosses(Force f, float recovery, Dictionary<string, long> byModel, out long tankLoss, out long soldLoss)
    {
        tankLoss = 0; soldLoss = 0;
        for (int i = 0; i < f.ModelKnocked.Length; i++)
        {
            // تجهیزات از کار افتاده: بخشی با تعمیرگاه صحرایی برمی‌گردد
            float lost = f.ModelKnocked[i] * (1f - Math.Clamp(recovery, 0f, 0.6f));
            long L = Math.Min(f.ModelSent[i], (long)Math.Round(lost));
            f.ModelLost[i] = L;
            tankLoss += L;
            if (L > 0)
            {
                string name = f.ModelNames[i];
                byModel[name] = byModel.TryGetValue(name, out var v) ? v + L : L;
            }
        }
        // مجروحان سبک پیاده‌نظام هم بخشی برمی‌گردند، ولی کمتر از تجهیزات
        float sLost = f.SoldiersKnocked * (1f - Math.Clamp(recovery * 0.45f, 0f, 0.3f));
        soldLoss = Math.Min(f.SoldiersSent, (long)Math.Round(sLost));
        f.SoldiersLost = soldLoss;
    }

    // ═══════════ برخورد دکترین‌ها: چه کسی به چه کسی می‌خورد ═════════════════
    static float DoctrineMatchup(Force fa, Force fd, Field field, ref XorRng rng, BattleLog log)
    {
        int a = fa.Cmd.Doctrine, d = fd.Cmd.Doctrine;
        float adv = 1.0f;
        string note = null;

        switch (a)
        {
            case 11: // هجوم متمرکز
                if (d == 11) { adv = 1.10f; note = "توده‌ی متمرکز مهاجم دقیقاً به سنگین‌ترین بخش خط ثابت خورد؛ نبردی رودررو و پرتلفات."; }
                else if (d == 12) { adv = 1.16f; note = "گشت‌های پراکنده‌ی مدافع در برابر یک مشت متمرکز، فرصت جمع‌شدن پیدا نکردند."; }
                else if (d == 21) { adv = 0.86f; note = "ستون متمرکز مهاجم، هدف ایده‌آل کمین‌های زرهی مدافع شد."; }
                else { adv = 1.04f; note = "مهاجم متمرکز سریع جلو رفت و بی‌آنکه بداند، عمقِ تله‌ی مدافع را پر کرد."; }
                break;
            case 12: // اکتشاف و یورش
                if (d == 11) { adv = 1.18f; note = "اکتشاف مهاجم درز خط ثابت را پیدا کرد و یورش دقیقاً روی همان نقطه نشست."; }
                else if (d == 12) { adv = 1.02f; note = "دو طرف مدام همدیگر را می‌جستند؛ نبرد به بازی شناسایی تبدیل شد."; }
                else if (d == 21) { adv = 1.08f; note = "گروه‌های سبک اکتشافی، چند کمین را زودتر از موعد فعال کردند و ضربه‌ی اصلی سالم ماند."; }
                else { adv = 0.94f; note = "دهانه‌ی باز مدافع، به‌ظاهر همان نقطه‌ضعفی بود که شناسایی مهاجم می‌جست."; }
                break;
            case 21: // محاصره‌ی گسترده
                if (d == 11) { adv = 1.20f; note = "خط ثابت مدافع نمی‌توانست جناح‌ها را بپوشاند و دو بازوی مهاجم آزادانه باز شدند."; }
                else if (d == 12) { adv = 0.96f; note = "گشت‌های متحرک مدافع مدام جلوی بسته‌شدن حلقه را می‌گرفتند."; }
                else if (d == 21) { adv = 1.06f; note = "بازوهای پهن مهاجم از کنار بیشتر کمین‌ها رد شدند."; }
                else { adv = 0.90f; note = "عقب‌نشینی حساب‌شده‌ی مدافع، حلقه‌ای که مهاجم می‌بست را مدام خالی می‌کرد."; }
                break;
            default: // حلقه‌ی متحرک
                if (d == 11) { adv = 1.14f; note = "چرخش پیاپی محور، ذخیره‌ی خط ثابت را بین سکتورها دواند و فرسود."; }
                else if (d == 12) { adv = 1.08f; note = "سرعت چرخش مهاجم از سرعت جابه‌جایی گشت‌های مدافع بیشتر بود."; }
                else if (d == 21) { adv = 0.92f; note = "ستون‌های پرتحرک مهاجم بارها از دهانه‌ی کمین‌ها گذشتند و ضربه خوردند."; }
                else { adv = 1.02f; note = "دو فرمانده هر دو دنبال کشیدن دیگری به جیب بودند؛ نبرد سیال شد."; }
                break;
        }

        // زمین: هر دکترین در زمین متفاوتی جواب می‌دهد
        byte terr = field.DominantTerrainNear(FRONT_KM / 2f);
        if (a == 21 || a == 22) // مانوری
        {
            if (terr is T_FOREST or T_URBAN or T_MARSH) adv *= 0.90f;
            else if (terr == T_PLAIN) adv *= 1.06f;
        }
        else // هجومی
        {
            if (terr is T_URBAN or T_RIDGE) adv *= 0.92f;
            else if (terr == T_PLAIN) adv *= 1.03f;
        }

        // نسبت زره: محاصره بدون زره کافی معنا ندارد
        long aArmor = fa.ModelSent.Sum(), dArmor = fd.ModelSent.Sum();
        if ((a == 21 || a == 22) && aArmor < dArmor) adv *= 0.94f;
        if (a == 11 && aArmor > dArmor * 2) adv *= 1.05f;

        adv *= rng.Range(0.97f, 1.03f);
        adv = Math.Clamp(adv, 0.80f, 1.30f);

        if (note != null) log.Add(0, 2, LG_PLAN, note);
        return adv;
    }
}
