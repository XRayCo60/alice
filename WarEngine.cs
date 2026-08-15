using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

enum BattleOutcomeKind
{
    AttackerRouted,
    DefenderVictory,
    Stalemate,
    AttackerLimitedVictory,
    AttackerHeavyVictory
}

readonly record struct ModelAmount(string Model, long Count);

sealed class BattleParticipant
{
    public long OwnerId { get; init; }
    public string CountryName { get; init; } = "";
    public string OwnerName { get; init; } = "";
    public Faction Faction { get; init; }
    public long Soldiers { get; init; }
    public List<ModelAmount> Tanks { get; init; } = new();
    public List<ModelAmount> Fighters { get; init; } = new();
    public List<ModelAmount> Bombers { get; init; } = new();
    public long AntiAir { get; init; }
    public bool IsHomelandDefender { get; init; }
    // دارایی قابل غارت مدافع (برای محاسبه غنیمت). صفر = بدون غنیمت.
    public long Money { get; init; }
    public long Iron { get; init; }
}

sealed class BattleOrders
{
    public int GroundStrategy { get; init; } = 1;
    public int GroundTactic { get; init; } = 1;
    public int AirStrategy { get; init; }
    public int AirTactic { get; init; } = 1;
}

sealed class BattleRequest
{
    public long BattleId { get; init; }
    public long ChatId { get; init; }
    public ulong ScenarioSeed { get; set; }
    public List<BattleParticipant> Attackers { get; init; } = new();
    public List<BattleParticipant> Defenders { get; init; } = new();
    public BattleOrders AttackerOrders { get; init; } = new();
    public BattleOrders DefenderOrders { get; init; } = new();
    public int MaximumDurationMinutes { get; init; } = 24 * 60;
}

sealed class ParticipantBattleLoss
{
    public long OwnerId { get; init; }
    public long SoldiersKilled { get; set; }
    public long SoldiersWounded { get; set; }
    public long SoldiersUnavailable => SoldiersKilled + SoldiersWounded;
    public long AntiAirLost { get; set; }
    public Dictionary<string, long> TanksDestroyed { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, long> TanksDamaged { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, long> TanksUnavailable { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, long> FightersUnavailable { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, long> BombersUnavailable { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

sealed class BattleEvent
{
    public int Minute { get; init; }
    public string Code { get; init; } = "";
    public string Text { get; init; } = "";
}

sealed class BattleResult
{
    public BattleOutcomeKind OutcomeKind;
    public double EffectiveAdvanceKm;
    public int DurationMinutes;
    public long CombatReadyReturnedSoldiers;
    public long CombatReadyReturnedTanks;
    public bool AttackerHeavyVictory;
    public bool DefenderVictory;
    public string ScenarioSummary = "";
    public string AttackerReport = "";
    public string DefenderReport = "";
    public string GroupAnnouncement = "";
    public List<BattleEvent> Events { get; init; } = new();
    public Dictionary<long, ParticipantBattleLoss> AttackerParticipantLosses { get; init; } = new();
    public Dictionary<long, ParticipantBattleLoss> DefenderParticipantLosses { get; init; } = new();

    public long AttackerTanksLost;
    public long AttackerTanksDestroyed;
    public long AttackerTanksDamaged;
    public long AttackerSoldiersLost;
    public long AttackerSoldiersKilled;
    public long AttackerSoldiersWounded;
    public long AttackerFightersLost;
    public long AttackerBombersLost;
    public long AttackerMoneyGained;
    public long AttackerIronGained;
    public double AttackerWelfareChange;
    public long DefenderTanksLost;
    public long DefenderTanksDestroyed;
    public long DefenderTanksDamaged;
    public long DefenderSoldiersLost;
    public long DefenderSoldiersKilled;
    public long DefenderSoldiersWounded;
    public long DefenderFightersLost;
    public long DefenderBombersLost;
    public long DefenderAntiAirLost;
    public long DefenderMoneyLost;
    public long DefenderIronLost;
    public double DefenderWelfareChange;
    public double PenetrationKm;
    public int SuccessPercent;
    public bool AttackerWon;
    public bool AttackerFailed;
    public double AirSuperiority;
    public double InfrastructureDamage;
}

static class WarEngine
{
    const float FRONT_KM = 40f;
    const float DEPTH_KM = 34f;
    const float WIN_DEPTH = 30f;
    const float FAIL_DEPTH = 3f;
    // شبکه: عرض ۴۰km × دامنه عمق از y=-6 تا y=+34 (روی هم ۴۰km) با سلول ۰٫۵km → ۸۰×۸۰.
    // قبلاً GRID_H=68 بود و زمین فقط تا عمق ۲۷٫۵km تولید می‌شد؛ بقیه ناحیه به آخرین ردیف گیر می‌کرد.
    const int GRID_W = 80, GRID_H = 80;
    const float CELL = 0.5f;
    const float TICK_MIN = 6f;
    const int AI_PERIOD = 4;
    const int MAX_GROUPS = 224;
    const int INF_GROUP = 100;
    const int TANK_GROUP = 10;

    // آستانه‌های شکست و پاداش — برای تراز بازی این‌ها را تنظیم کنید
    const float ATTACKER_BREAK_POWER_FRACTION = 0.13f;    // مهاجم با افت توان زیر این حد از حمله می‌شکند
    const float DEFENDER_ROUT_POWER_FRACTION = 0.25f;     // مدافع با افت توان زیر این حد + نفوذ بیش از ۶km می‌گسلد
    const float DEFENDER_COLLAPSE_POWER_FRACTION = 0.10f; // سقوط کامل مدافع (بدون قید عمق)
    const float PUSH_BOOST_DEPTH = 20f;                   // عمقی که پس از آن، موفقیت با ضریب صعودی محاسبه می‌شود
    const float HOMELAND_COMBAT_BONUS = 0.08f;            // پاداش دفاع از وطن (کشورهای ۲ شهر یا کمتر)
    const float LOOT_MONEY_FRACTION = 0.35f;              // سهم پول مدافع در پیروزی کامل مهاجم
    const float LOOT_IRON_FRACTION = 0.30f;               // سهم آهن مدافع در پیروزی کامل مهاجم
    const float LOOT_MIN_SUCCESS = 0.15f;                 // کف ضریب غنیمت

    const byte T_PLAIN = 0, T_HILL = 1, T_FOREST = 2, T_URBAN = 3, T_MARSH = 4, T_RIDGE = 5;
    static readonly float[] TerSpeed = { 1.00f, 0.72f, 0.55f, 0.60f, 0.40f, 0.65f };
    static readonly float[] TerCover = { 0.00f, 0.25f, 0.55f, 0.65f, 0.15f, 0.35f };
    static readonly float[] TerAcc = { 1.00f, 0.90f, 0.70f, 0.65f, 0.95f, 0.92f };
    static readonly float[] TerVision = { 1.00f, 1.35f, 0.55f, 0.60f, 1.00f, 1.50f };

    const byte P_ADVANCE = 0, P_ASSAULT = 1, P_DEFEND = 2, P_AMBUSH = 3,
        P_PATROL = 4, P_RETREAT = 5, P_FLANK = 6, P_HOLD = 7;

    const byte W_CLEAR = 0, W_CLOUD = 1, W_RAIN = 2, W_FOG = 3, W_SNOW = 4;
    static readonly string[] WeatherName = { "آفتابی", "ابری", "بارانی", "مه‌آلود", "برفی" };
    static readonly float[] WxVision = { 1.00f, 0.92f, 0.78f, 0.50f, 0.70f };
    static readonly float[] WxAcc = { 1.00f, 0.96f, 0.88f, 0.75f, 0.85f };
    static readonly float[] WxSpeed = { 1.00f, 0.97f, 0.82f, 0.90f, 0.70f };
    static readonly float[] WxAir = { 1.00f, 0.85f, 0.65f, 0.40f, 0.60f };
    static readonly string[] TimeName = { "سپیده‌دم", "روز", "غروب", "شب" };
    static readonly float[] TimeVision = { 0.80f, 1.00f, 0.75f, 0.45f };

    public readonly struct TankSpec
    {
        public readonly string Name;
        public readonly float Pen, He, Mg, Armor, Speed, CannonAmmo, MgAmmo, Reliab;
        public TankSpec(string n, float p, float he, float mg, float ar, float sp,
            float ca, float ma, float rel)
        {
            Name = n; Pen = p; He = he; Mg = mg; Armor = ar; Speed = sp;
            CannonAmmo = ca; MgAmmo = ma; Reliab = rel;
        }
    }

    static readonly TankSpec SpecUSA = new("M2 Medium", 46f, 0.45f, 7f, 30f, 42f, 100f, 90f, 0.95f);
    static readonly TankSpec SpecUSSR = new("T-28", 40f, 1.00f, 4f, 80f, 37f, 70f, 60f, 0.82f);
    static readonly TankSpec SpecReich = new("Panzer III", 67f, 0.55f, 3f, 60f, 40f, 84f, 55f, 0.97f);
    static TankSpec SpecOf(Faction f) => f == Faction.USA ? SpecUSA : f == Faction.USSR ? SpecUSSR : SpecReich;

    public readonly struct FighterSpec
    {
        public readonly string Name;
        public readonly float Maneuver, Firepower, Speed, Cas;
        public FighterSpec(string n, float mn, float fp, float sp, float cas)
        { Name = n; Maneuver = mn; Firepower = fp; Speed = sp; Cas = cas; }
    }

    static readonly FighterSpec FighterUSA = new("P-36", 9f, 4.5f, 500f, 0.9f);
    static readonly FighterSpec FighterUSSR = new("I-16", 9f, 4.0f, 520f, 0.8f);
    static readonly FighterSpec FighterReich = new("Bf 109", 8f, 8.0f, 570f, 1.0f);
    static FighterSpec FighterOf(Faction f) => f == Faction.USA ? FighterUSA : f == Faction.USSR ? FighterUSSR : FighterReich;

    public readonly struct BomberSpec
    {
        public readonly string Name;
        public readonly float Armor, DefMg, Bombload, Speed;
        public BomberSpec(string n, float ar, float dmg, float bl, float sp)
        { Name = n; Armor = ar; DefMg = dmg; Bombload = bl; Speed = sp; }
    }

    static readonly BomberSpec BomberUSA = new("B-17", 8f, 6f, 3600f, 460f);
    static readonly BomberSpec BomberReich = new("He 111", 5f, 4f, 2000f, 435f);
    static readonly BomberSpec BomberUSSR = new("DB-3", 3f, 3f, 1000f, 430f);
    static BomberSpec BomberOf(Faction f) => f == Faction.USA ? BomberUSA : f == Faction.USSR ? BomberUSSR : BomberReich;

    struct XorRng
    {
        ulong s0, s1;
        public XorRng(ulong seed)
        {
            s0 = seed * 0x9E3779B97F4A7C15UL + 1;
            s1 = seed ^ 0xBF58476D1CE4E5B9UL;
            if (s1 == 0) s1 = 0x94D049BB133111EBUL;
            NextU(); NextU();
        }
        public ulong NextU()
        {
            ulong x = s0, y = s1; s0 = y;
            x ^= x << 23; s1 = x ^ y ^ (x >> 17) ^ (y >> 26);
            return s1 + y;
        }
        public float NextF() => (NextU() >> 40) * (1f / 16777216f);
        public float Range(float a, float b) => a + NextF() * (b - a);
        public int Next(int max) => max <= 1 ? 0 : (int)(NextU() % (uint)max);
    }

    struct Group
    {
        public float X, Y;
        public float Units, Size0;
        public float CAmmo, MAmmo;
        public float Morale, Supp, Fatigue, Exp;
        public float TgtX, TgtY;
        public short FireTgt;
        public byte Type, Posture, Sector;
        public bool Alive, Sprung;
        public float Signature;
    }

    struct Intel { public float Level, LastX, LastY, Stale; }
    struct Evt { public short Tick; public byte Kind; public float A, B; }

    const byte E_CONTACT = 0, E_AMBUSH = 1, E_BREAK5 = 2, E_BREAK10 = 3,
        E_BREAK20 = 4, E_BREAK30 = 5, E_ENCIRCLE = 6, E_ROUT = 7,
        E_SHIFT = 8, E_HALT = 9;

    [ThreadStatic] static Group[]? _atk;
    [ThreadStatic] static Group[]? _def;
    [ThreadStatic] static Intel[]? _intelA;
    [ThreadStatic] static Intel[]? _intelD;
    [ThreadStatic] static byte[]? _terr;
    [ThreadStatic] static float[]? _elev;
    [ThreadStatic] static float[]? _threatA;
    [ThreadStatic] static float[]? _threatD;
    [ThreadStatic] static Evt[]? _evts;
    [ThreadStatic] static StringBuilder? _sb;
    [ThreadStatic] static byte _weather;
    [ThreadStatic] static byte _startWeather;
    [ThreadStatic] static byte _startTime;

    static void EnsureBuffers()
    {
        _atk ??= new Group[MAX_GROUPS];
        _def ??= new Group[MAX_GROUPS];
        _intelA ??= new Intel[MAX_GROUPS];
        _intelD ??= new Intel[MAX_GROUPS];
        _terr ??= new byte[GRID_W * GRID_H];
        _elev ??= new float[GRID_W * GRID_H];
        _threatA ??= new float[10];
        _threatD ??= new float[10];
        _evts ??= new Evt[512];
        _sb ??= new StringBuilder(4096);
    }

    struct AirOutcome
    {
        public long AtkFightersLost, AtkBombersLost;
        public long DefFightersLost, DefBombersLost, DefAntiAirLost;
        public float Superiority, CasAtk, CasDef, InfrastructureDamage;
        public bool HadAirCombat;
    }

    public static ulong CreateScenarioSeed() =>
        BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8));

    public static string CanonicalTankModel(string model, Faction faction) => GetTankSpecByModel(model, faction).Name;
    public static string CanonicalFighterModel(string model, Faction faction) => GetFighterSpecByModel(model, faction).Name;
    public static string CanonicalBomberModel(string model, Faction faction) => GetBomberSpecByModel(model, faction).Name;

    public static TankSpec GetTankSpecByModel(string modelName, Faction fallback)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return SpecOf(fallback);
        string m = modelName.ToLowerInvariant();
        if (m.Contains("t-28") || m.Contains("t28") || m.Contains("t-34") || m.Contains("t34")) return SpecUSSR;
        if (m.Contains("m2") || m.Contains("m4") || m.Contains("sherman")) return SpecUSA;
        if (m.Contains("panzer") || m.Contains("pz")) return SpecReich;
        return SpecOf(fallback);
    }

    public static FighterSpec GetFighterSpecByModel(string modelName, Faction fallback)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return FighterOf(fallback);
        string m = modelName.ToLowerInvariant();
        if (m.Contains("i-16") || m.Contains("i16") || m.Contains("yak")) return FighterUSSR;
        if (m.Contains("p-36") || m.Contains("p36") || m.Contains("mustang")) return FighterUSA;
        if (m.Contains("bf") || m.Contains("109")) return FighterReich;
        return FighterOf(fallback);
    }

    public static BomberSpec GetBomberSpecByModel(string modelName, Faction fallback)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return BomberOf(fallback);
        string m = modelName.ToLowerInvariant();
        if (m.Contains("db") || m.Contains("pe-2") || m.Contains("pe2")) return BomberUSSR;
        if (m.Contains("b-17") || m.Contains("b17")) return BomberUSA;
        if (m.Contains("he") || m.Contains("ju")) return BomberReich;
        return BomberOf(fallback);
    }

    static TankSpec BlendTankSpecs(IEnumerable<BattleParticipant> side)
    {
        var items = side.SelectMany(p => p.Tanks.Select(x => (P: p, A: x)))
            .Where(x => x.A.Count > 0).ToList();
        double total = items.Sum(x => (double)x.A.Count);
        if (total <= 0) return SpecOf(side.FirstOrDefault()?.Faction ?? Faction.USA);
        double pen = 0, he = 0, mg = 0, armor = 0, speed = 0, ca = 0, ma = 0, rel = 0;
        foreach (var item in items)
        {
            TankSpec spec = GetTankSpecByModel(item.A.Model, item.P.Faction);
            double w = item.A.Count / total;
            pen += spec.Pen * w; he += spec.He * w; mg += spec.Mg * w;
            armor += spec.Armor * w; speed += spec.Speed * w;
            ca += spec.CannonAmmo * w; ma += spec.MgAmmo * w; rel += spec.Reliab * w;
        }
        return new TankSpec($"ترکیب {items.Count} مدل", (float)pen, (float)he, (float)mg,
            (float)armor, (float)speed, (float)ca, (float)ma, (float)rel);
    }

    static FighterSpec BlendFighterSpecs(IEnumerable<BattleParticipant> side)
    {
        var items = side.SelectMany(p => p.Fighters.Select(x => (P: p, A: x)))
            .Where(x => x.A.Count > 0).ToList();
        double total = items.Sum(x => (double)x.A.Count);
        if (total <= 0) return FighterOf(side.FirstOrDefault()?.Faction ?? Faction.USA);
        double man = 0, fp = 0, speed = 0, cas = 0;
        foreach (var item in items)
        {
            FighterSpec s = GetFighterSpecByModel(item.A.Model, item.P.Faction);
            double w = item.A.Count / total;
            man += s.Maneuver * w; fp += s.Firepower * w;
            speed += s.Speed * w; cas += s.Cas * w;
        }
        return new FighterSpec("ترکیب جنگنده", (float)man, (float)fp, (float)speed, (float)cas);
    }

    static BomberSpec BlendBomberSpecs(IEnumerable<BattleParticipant> side)
    {
        var items = side.SelectMany(p => p.Bombers.Select(x => (P: p, A: x)))
            .Where(x => x.A.Count > 0).ToList();
        double total = items.Sum(x => (double)x.A.Count);
        if (total <= 0) return BomberOf(side.FirstOrDefault()?.Faction ?? Faction.USA);
        double ar = 0, mg = 0, load = 0, speed = 0;
        foreach (var item in items)
        {
            BomberSpec s = GetBomberSpecByModel(item.A.Model, item.P.Faction);
            double w = item.A.Count / total;
            ar += s.Armor * w; mg += s.DefMg * w; load += s.Bombload * w; speed += s.Speed * w;
        }
        return new BomberSpec("ترکیب بمب‌افکن", (float)ar, (float)mg, (float)load, (float)speed);
    }

    static float BlendFighterFactionQuality(IEnumerable<BattleParticipant> side)
    {
        double total = 0, quality = 0;
        foreach (var p in side)
        {
            long fighters = p.Fighters.Sum(x => Math.Max(0, x.Count));
            total += fighters;
            quality += fighters * FactionQuality(p.Faction);
        }
        return total > 0 ? (float)(quality / total) : 1f;
    }

    public static BattleResult Resolve(BattleRequest request)
    {
        Validate(request);
        EnsureBuffers();
        var rng = new XorRng(request.ScenarioSeed);
        Array.Clear(_atk!); Array.Clear(_def!); Array.Clear(_intelA!); Array.Clear(_intelD!);
        Array.Clear(_evts!);

        long aTanks = request.Attackers.Sum(x => x.Tanks.Sum(t => Math.Max(0, t.Count)));
        long aSold = request.Attackers.Sum(x => Math.Max(0, x.Soldiers));
        long aFight = request.Attackers.Sum(x => x.Fighters.Sum(t => Math.Max(0, t.Count)));
        long aBomb = request.Attackers.Sum(x => x.Bombers.Sum(t => Math.Max(0, t.Count)));
        long dTanks = request.Defenders.Sum(x => x.Tanks.Sum(t => Math.Max(0, t.Count)));
        long dSold = request.Defenders.Sum(x => Math.Max(0, x.Soldiers));
        long dFight = request.Defenders.Sum(x => x.Fighters.Sum(t => Math.Max(0, t.Count)));
        long dBomb = request.Defenders.Sum(x => x.Bombers.Sum(t => Math.Max(0, t.Count)));
        long dAA = request.Defenders.Sum(x => Math.Max(0, x.AntiAir));

        int aStrat = request.AttackerOrders.GroundStrategy == 2 ? 2 : 1;
        int aTac = request.AttackerOrders.GroundTactic == 2 ? 2 : 1;
        int dStrat = request.DefenderOrders.GroundStrategy == 2 ? 2 : 1;
        int dTac = request.DefenderOrders.GroundTactic == 2 ? 2 : 1;
        var aSpec = BlendTankSpecs(request.Attackers);
        var dSpec = BlendTankSpecs(request.Defenders);
        var aFs = BlendFighterSpecs(request.Attackers);
        var dFs = BlendFighterSpecs(request.Defenders);
        var aBs = BlendBomberSpecs(request.Attackers);
        var dBs = BlendBomberSpecs(request.Defenders);

        _weather = PickWeather(ref rng);
        _startWeather = _weather;
        _startTime = (byte)rng.Next(4);
        GenTerrain(ref rng);

        float strategyAdvantage = CalculateStrategyAdvantage(aStrat, aTac, dStrat, dTac,
            aSpec, dSpec, aTanks, dTanks, aSold, dSold, ref rng);
        AirOutcome air = RunAirPhase(request, aFight, aBomb, dFight, dBomb, dAA,
            aFs, aBs, dFs, dBs, ref rng);
        // دفاع از وطن: کشورهای در آستانه سقوط (۲ شهر یا کمتر) سخت‌تر می‌جنگند
        bool homelandDefended = request.Defenders.Any(x => x.IsHomelandDefender);
        float dHomeMul = homelandDefended ? 1f + HOMELAND_COMBAT_BONUS : 1f;

        int nA = BuildSide(_atk!, true, aTanks, aSold, aStrat, aTac, aSpec, ref rng);
        int nD = BuildSide(_def!, false, dTanks, dSold, dStrat, dTac, dSpec, ref rng);
        for (int i = 0; i < nD; i++) _intelA![i].Stale = 9999f;
        for (int i = 0; i < nA; i++) _intelD![i].Stale = 9999f;

        float aPow0 = SidePower(_atk!, nA, aSpec);
        float dPow0 = SidePower(_def!, nD, dSpec);
        float effDepth = 0, maxDepth = 0, aIntelQ = 0, dIntelQ = 0;
        bool contact = false, ambush = false, encircled = false;
        int evtN = 0, haltTicks = 0, tick = 0;
        int maxTicks = Math.Clamp(request.MaximumDurationMinutes / (int)TICK_MIN, 10, 360);

        if (nD == 0)
        {
            effDepth = WIN_DEPTH;
            maxDepth = WIN_DEPTH;
            tick = Math.Min(maxTicks, 60);
            AddEvt(ref evtN, Math.Max(1, tick / 6), E_BREAK5, 5);
            AddEvt(ref evtN, Math.Max(2, tick / 3), E_BREAK10, 10);
            AddEvt(ref evtN, Math.Max(3, tick * 2 / 3), E_BREAK20, 20);
            AddEvt(ref evtN, tick, E_BREAK30, WIN_DEPTH);
        }
        else
        for (; tick < maxTicks; tick++)
        {
            if (tick > 0 && tick % 60 == 0 && rng.NextF() < 0.35f)
            {
                byte previousWeather = _weather;
                byte nextWeather = PickWeather(ref rng);
                if (nextWeather == previousWeather)
                    nextWeather = (byte)((previousWeather + 1 + rng.Next(4)) % WeatherName.Length);
                _weather = nextWeather;
                AddEvt(ref evtN, tick, E_SHIFT, previousWeather, nextWeather);
            }
            byte time = TimeAtTick(tick);
            float vision = WxVision[_weather] * TimeVision[time];
            aIntelQ = SenseSide(_atk!, nA, _def!, nD, _intelA!, true, vision, ref rng);
            dIntelQ = SenseSide(_def!, nD, _atk!, nA, _intelD!, dStrat == 2, vision, ref rng);

            if (tick % AI_PERIOD == 0)
            {
                BuildThreatMap(_def!, nD, _intelA!, _threatA!);
                BuildThreatMap(_atk!, nA, _intelD!, _threatD!);
                bool wasEncircled = encircled;
                CommandAttacker(nA, aStrat, aTac, effDepth, aIntelQ, ref rng, ref encircled, tick);
                if (!wasEncircled && encircled) AddEvt(ref evtN, tick, E_ENCIRCLE, effDepth);
                CommandDefender(nD, dStrat, dTac, effDepth, ref rng);
            }

            MoveSide(_atk!, nA, aSpec, true);
            MoveSide(_def!, nD, dSpec, false);

            float aDuel = FireSide(_atk!, nA, aSpec, _def!, nD, dSpec, _intelA!, true,
                aStrat, aTac, dStrat, encircled, air.CasAtk * strategyAdvantage,
                WxAcc[_weather], ref rng, ref evtN, tick, ref contact, ref ambush);
            float dDuel = FireSide(_def!, nD, dSpec, _atk!, nA, aSpec, _intelD!, false,
                dStrat, dTac, aStrat, false, air.CasDef * dHomeMul,
                WxAcc[_weather], ref rng, ref evtN, tick, ref contact, ref ambush);
            _ = aDuel + dDuel;

            MoraleSide(_atk!, nA, true, ref rng, ref evtN, tick);
            MoraleSide(_def!, nD, false, ref rng, ref evtN, tick);

            float depth = EffectiveDepth(_atk!, nA);
            if (depth > effDepth + 0.05f)
            {
                float prev = effDepth; effDepth = depth; haltTicks = 0;
                if (prev < 5 && depth >= 5) AddEvt(ref evtN, tick, E_BREAK5, depth);
                if (prev < 10 && depth >= 10) AddEvt(ref evtN, tick, E_BREAK10, depth);
                if (prev < 20 && depth >= 20) AddEvt(ref evtN, tick, E_BREAK20, depth);
                if (prev < 30 && depth >= 30) AddEvt(ref evtN, tick, E_BREAK30, depth);
            }
            else if (contact) haltTicks++;
            maxDepth = Math.Max(maxDepth, depth);

            float aPow = SidePower(_atk!, nA, aSpec);
            float dPow = SidePower(_def!, nD, dSpec);
            if (effDepth >= WIN_DEPTH) { tick++; break; }
            if (aPow < aPow0 * ATTACKER_BREAK_POWER_FRACTION) { tick++; break; }
            // مدافع می‌گسلد اگر: (۱) کاملاً از هم پاشیده باشد، یا (۲) ۷۵٪ توانش را از دست داده
            // و مهاجم بیش از ۶km نفوذ کرده باشد. قبلاً شرط ۱۰٪+عمق ۶ بود و تقریباً هیچ‌وقت رخ نمی‌داد.
            bool defenderBroken = dPow < dPow0 * DEFENDER_COLLAPSE_POWER_FRACTION ||
                (dPow < dPow0 * DEFENDER_ROUT_POWER_FRACTION && effDepth > 6f);
            if (defenderBroken)
            {
                effDepth = Math.Min(WIN_DEPTH, effDepth + (WIN_DEPTH - effDepth) * 0.7f);
                AddEvt(ref evtN, tick, E_ROUT, 1); tick++; break;
            }
            if (haltTicks > 90)
            {
                AddEvt(ref evtN, tick, E_HALT, effDepth); tick++; break;
            }
        }

        long aTankLoss = 0, aSoldLoss = 0, dTankLoss = 0, dSoldLoss = 0;
        CountLosses(_atk!, nA, ref aTankLoss, ref aSoldLoss);
        CountLosses(_def!, nD, ref dTankLoss, ref dSoldLoss);
        aTankLoss = Math.Min(aTankLoss, aTanks); aSoldLoss = Math.Min(aSoldLoss, aSold);
        dTankLoss = Math.Min(dTankLoss, dTanks); dSoldLoss = Math.Min(dSoldLoss, dSold);

        if (effDepth >= PUSH_BOOST_DEPTH && effDepth < WIN_DEPTH)
            effDepth = Math.Min(WIN_DEPTH, effDepth + (WIN_DEPTH - effDepth) * 0.5f);
        else if (effDepth <= 6f && effDepth > FAIL_DEPTH)
            effDepth = Math.Max(0, effDepth - (effDepth - FAIL_DEPTH) * 0.5f);

        float fraction = Math.Clamp((effDepth - FAIL_DEPTH) / (WIN_DEPTH - FAIL_DEPTH), 0, 1);
        int success = (int)Math.Round(fraction * 100);
        double mappedDepth = Math.Clamp(effDepth / WIN_DEPTH * 40.0, 0, 40);
        long aTankReady = Math.Max(0, aTanks - aTankLoss);
        long aSoldReady = Math.Max(0, aSold - aSoldLoss);
        long aTankDestroyed = Math.Min(aTankLoss, (long)Math.Round(aTankLoss *
            Math.Clamp(0.82f - aSpec.Reliab * 0.16f, 0.62f, 0.75f)));
        long dTankDestroyed = Math.Min(dTankLoss, (long)Math.Round(dTankLoss *
            Math.Clamp(0.82f - dSpec.Reliab * 0.16f, 0.62f, 0.75f)));

        var result = new BattleResult
        {
            DurationMinutes = Math.Max(30, (int)Math.Round(tick * TICK_MIN)),
            PenetrationKm = mappedDepth,
            EffectiveAdvanceKm = mappedDepth,
            SuccessPercent = success,
            AirSuperiority = air.Superiority,
            InfrastructureDamage = air.InfrastructureDamage,
            CombatReadyReturnedSoldiers = aSoldReady,
            CombatReadyReturnedTanks = aTankReady,
            ScenarioSummary = $"{WeatherSummary()}، آغاز {TimeName[_startTime]}، شبکه ۵۰۰ متری",
            AttackerTanksLost = aTankLoss,
            AttackerTanksDestroyed = aTankDestroyed,
            AttackerTanksDamaged = aTankLoss - aTankDestroyed,
            AttackerSoldiersLost = aSoldLoss,
            AttackerFightersLost = air.AtkFightersLost,
            AttackerBombersLost = air.AtkBombersLost,
            DefenderTanksLost = dTankLoss,
            DefenderTanksDestroyed = dTankDestroyed,
            DefenderTanksDamaged = dTankLoss - dTankDestroyed,
            DefenderSoldiersLost = dSoldLoss,
            DefenderFightersLost = air.DefFightersLost,
            DefenderBombersLost = air.DefBombersLost,
            DefenderAntiAirLost = air.DefAntiAirLost
        };

        result.AttackerHeavyVictory = result.EffectiveAdvanceKm > 35 && aSoldReady >= 5000 && aTankReady >= 50;
        bool absoluteWin = effDepth >= WIN_DEPTH;
        bool absoluteFail = effDepth < FAIL_DEPTH;
        result.OutcomeKind = result.AttackerHeavyVictory ? BattleOutcomeKind.AttackerHeavyVictory :
            absoluteWin || success >= 70 ? BattleOutcomeKind.AttackerLimitedVictory :
            absoluteFail ? BattleOutcomeKind.AttackerRouted :
            success >= 20 ? BattleOutcomeKind.Stalemate : BattleOutcomeKind.DefenderVictory;
        result.AttackerWon = result.OutcomeKind is BattleOutcomeKind.AttackerHeavyVictory or BattleOutcomeKind.AttackerLimitedVictory;
        result.AttackerFailed = result.OutcomeKind is BattleOutcomeKind.AttackerRouted or BattleOutcomeKind.DefenderVictory;
        result.DefenderVictory = result.AttackerFailed;
        // غنیمت: فقط با پیروزی مهاجم؛ سهمی از پول/آهن مدافع به نسبت موفقیت (این‌ها قبلاً هیچ‌وقت پر نمی‌شدند)
        long defenderMoney = request.Defenders.Sum(x => Math.Max(0, x.Money));
        long defenderIron = request.Defenders.Sum(x => Math.Max(0, x.Iron));
        if (result.AttackerWon)
        {
            double gain = Math.Clamp(success / 100.0, LOOT_MIN_SUCCESS, 1.0);
            result.DefenderMoneyLost = (long)Math.Round(defenderMoney * gain * LOOT_MONEY_FRACTION);
            result.DefenderIronLost = (long)Math.Round(defenderIron * gain * LOOT_IRON_FRACTION);
        }
        result.AttackerMoneyGained = result.DefenderMoneyLost;
        result.AttackerIronGained = result.DefenderIronLost;
        if (request.AttackerOrders.AirStrategy != 0 && aFight + aBomb > 0)
        {
            string airText = request.AttackerOrders.AirStrategy == 2
                ? $"بمباران راهبردی با خسارت زیرساختی {air.InfrastructureDamage:F1}٪ اجرا شد"
                : $"نبرد هوایی با برتری {AirSupText(air.Superiority)} پایان یافت";
            result.Events.Add(new BattleEvent
            {
                Minute = 0,
                Code = request.AttackerOrders.AirStrategy == 2 ? "STRATEGIC_BOMBING" : "AIR_OPERATION",
                Text = airText
            });
        }

        AllocateParticipantLosses(request.Attackers, aTankLoss, aTankDestroyed, aSoldLoss,
            air.AtkFightersLost, air.AtkBombersLost, 0, result.AttackerParticipantLosses);
        AllocateParticipantLosses(request.Defenders, dTankLoss, dTankDestroyed, dSoldLoss,
            air.DefFightersLost, air.DefBombersLost, air.DefAntiAirLost, result.DefenderParticipantLosses);
        FillAggregateSplits(result);
        BuildReports(request, result, aSpec, dSpec, strategyAdvantage, evtN,
            aIntelQ, dIntelQ, effDepth);
        return result;
    }

    public static BattleResult[] ResolveBatch(IReadOnlyList<BattleRequest> requests,
        int maxDegreeOfParallelism = 0)
    {
        if (requests.Count == 0) return Array.Empty<BattleResult>();
        var output = new BattleResult[requests.Count];
        int automatic = Math.Max(1, Math.Min(4, Environment.ProcessorCount - 1));
        int degree = maxDegreeOfParallelism <= 0
            ? automatic : Math.Clamp(maxDegreeOfParallelism, 1, 4);
        Parallel.For(0, requests.Count,
            new ParallelOptions { MaxDegreeOfParallelism = degree },
            i => output[i] = Resolve(requests[i]));
        return output;
    }

    static float FactionQuality(Faction f) => f switch
    {
        Faction.Reich => 1.08f,
        Faction.USA => 1.03f,
        _ => 1.00f
    };

    static float CalculateStrategyAdvantage(int aStrat, int aTac, int dStrat, int dTac,
        TankSpec aSpec, TankSpec dSpec, long aTanks, long dTanks, long aSold, long dSold,
        ref XorRng rng)
    {
        float adv = 1f;
        double a = aTanks * 10.0 + aSold;
        double d = dTanks * 10.0 + dSold;
        double ratio = a / Math.Max(1, d);
        if (aStrat == 1)
        {
            if (dStrat == 1)
            {
                adv += ((aSpec.Armor - dSpec.Armor) + (aSpec.Pen - dSpec.Armor)) / 100f * 0.15f;
                adv += aTac == 1 ? 0.05f : 0.08f + (float)(ratio - 1) * 0.05f;
            }
            else
            {
                adv -= 0.08f;
                adv += dTac == 1 ? 0.02f : -0.03f;
                if (aSpec.Speed > dSpec.Speed) adv += (aSpec.Speed - dSpec.Speed) / 200f;
            }
        }
        else if (dStrat == 1)
        {
            adv += 0.12f + (aTac == 2 ? 0.06f : 0f) + (aSpec.Speed - 35f) / 300f;
        }
        else
        {
            adv += 0.02f;
            if (aTac == 2 && dTac == 2) adv += 0.01f;
            if (aTac == 1 && dTac == 1) adv -= 0.02f;
        }
        adv += (float)Math.Clamp((ratio - 1) * 0.08, -0.12, 0.12);
        adv += rng.Range(-0.04f, 0.04f);
        return Math.Clamp(adv, 0.75f, 1.35f);
    }

    static AirOutcome RunAirPhase(BattleRequest request, long aFight, long aBomb,
        long dFight, long dBomb, long dAA, FighterSpec aFs, BomberSpec aBs,
        FighterSpec dFs, BomberSpec dBs, ref XorRng rng)
    {
        var o = new AirOutcome { CasAtk = 1, CasDef = 1 };
        int attackStrategy = request.AttackerOrders.AirStrategy;
        int attackTactic = request.AttackerOrders.AirTactic == 2 ? 2 : 1;
        int defenseStrategy = request.DefenderOrders.AirStrategy;
        int defenseTactic = request.DefenderOrders.AirTactic == 2 ? 2 : 1;
        if (attackStrategy == 0) return o;

        float wx = WxAir[_weather];
        float aq = (aFs.Maneuver * 0.55f + aFs.Firepower * 0.45f) *
            BlendFighterFactionQuality(request.Attackers);
        float dq = (dFs.Maneuver * 0.55f + dFs.Firepower * 0.45f) *
            BlendFighterFactionQuality(request.Defenders);
        float attackFighterMul = attackStrategy == 1
            ? (attackTactic == 1 ? 1.20f : 1.08f) : (attackTactic == 1 ? 1.03f : 0.96f);
        float defenseFighterMul = defenseStrategy switch
        {
            1 => defenseTactic == 1 ? 1.25f : 1.15f,
            2 => defenseTactic == 2 ? 1.20f : 1.02f,
            _ => 0f
        };
        float aaMul = defenseStrategy == 2 && defenseTactic == 1 ? 1.35f :
            defenseStrategy == 1 && defenseTactic == 2 ? 1.15f : 1f;
        long activeDefenders = defenseStrategy == 0 ? 0 : dFight;
        float ap = aFight * aq * attackFighterMul * wx * rng.Range(0.9f, 1.1f);
        float dp = activeDefenders * dq * defenseFighterMul * wx * rng.Range(0.9f, 1.1f);
        if (aFight > 0 && activeDefenders > 0)
        {
            o.HadAirCombat = true;
            float total = ap + dp;
            o.AtkFightersLost = Math.Min(aFight, (long)Math.Round(aFight *
                Math.Clamp(dp / Math.Max(1, total) * rng.Range(0.65f, 1.02f), 0, 0.92f)));
            o.DefFightersLost = Math.Min(activeDefenders, (long)Math.Round(activeDefenders *
                Math.Clamp(ap / Math.Max(1, total) * rng.Range(0.65f, 1.02f), 0, 0.92f)));
        }

        long aFightLeft = Math.Max(0, aFight - o.AtkFightersLost);
        long dFightLeft = Math.Max(0, activeDefenders - o.DefFightersLost);
        if (aBomb > 0 && dFightLeft > 0)
        {
            o.HadAirCombat = true;
            float escort = aFightLeft * aq * attackFighterMul;
            float intercept = dFightLeft * dq * defenseFighterMul;
            float interception = intercept / Math.Max(1, intercept + escort + aBomb * aBs.DefMg);
            float protection = 1f / (1 + aBs.Armor * 0.08f + aBs.DefMg * 0.04f);
            long intercepted = (long)Math.Round(aBomb * Math.Clamp(
                (0.05f + interception * 0.45f) * protection * wx * rng.Range(0.85f, 1.15f),
                0, 0.55f));
            o.AtkBombersLost = Math.Min(aBomb, intercepted);
        }

        if (dAA > 0 && (aFightLeft > 0 || aBomb > o.AtkBombersLost))
        {
            float aa = dAA * aaMul * rng.Range(0.85f, 1.15f);
            long bomberAaLoss = (long)Math.Round(aa * 0.018f /
                (1 + aBs.Armor * 0.18f + aBs.Speed / 1800f));
            o.AtkBombersLost = Math.Min(aBomb, o.AtkBombersLost + bomberAaLoss);
            long fighterAaLoss = (long)Math.Round(Math.Min(aFightLeft, aa * 0.018f));
            o.AtkFightersLost = Math.Min(aFight, o.AtkFightersLost + fighterAaLoss);
            aFightLeft = Math.Max(0, aFight - o.AtkFightersLost);
            o.DefAntiAirLost = Math.Min(dAA,
                (long)Math.Round((aFightLeft + Math.Max(0, aBomb - o.AtkBombersLost) * 1.3f) *
                                 rng.Range(0.03f, 0.07f)));
        }

        long bombersLeft = Math.Max(0, aBomb - o.AtkBombersLost);
        if (attackStrategy == 1 && attackTactic == 2 && (aFightLeft > 0 || bombersLeft > 0))
        {
            float access = 1 - dFightLeft * dq / Math.Max(1, dFightLeft * dq + aFightLeft * aq + 20);
            float baseStrike = (aFightLeft * 0.018f + bombersLeft * 0.045f) * wx * access;
            long extraFighters = Math.Min(dFightLeft,
                (long)Math.Round(baseStrike * rng.Range(0.75f, 1.20f)));
            o.DefFightersLost = Math.Min(dFight, o.DefFightersLost + extraFighters);
            dFightLeft = Math.Max(0, activeDefenders - o.DefFightersLost);
            float bomberVulnerability = 1f / (1 + dBs.Armor * 0.08f);
            o.DefBombersLost = Math.Min(dBomb,
                (long)Math.Round(baseStrike * 0.65f * bomberVulnerability * rng.Range(0.8f, 1.2f)));
        }

        float ar = aFightLeft * aq * attackFighterMul + bombersLeft * 0.35f;
        float dr = dFightLeft * dq * Math.Max(0.5f, defenseFighterMul) +
            Math.Max(0, dAA - o.DefAntiAirLost) * 0.5f;
        o.Superiority = Math.Clamp((ar - dr) / Math.Max(1, ar + dr), -1, 1);
        if (attackStrategy == 1)
        {
            float casTactic = attackTactic == 1 ? 1.08f : 0.86f;
            o.CasAtk = 1 + Math.Clamp((aFightLeft * aFs.Cas + bombersLeft * 1.5f) *
                wx * casTactic / Math.Max(50, (aSold(request) + 1) * 0.02f), 0, 0.6f);
        }
        else if (bombersLeft > 0)
        {
            double targetScale = Math.Sqrt(Math.Max(1,
                dSold(request) + request.Defenders.Sum(x => x.Tanks.Sum(t => t.Count)) * 10.0 +
                dFight * 4.0 + dAA * 2.0));
            float access = Math.Clamp(0.65f + o.Superiority * 0.35f, 0.25f, 1f);
            float pattern = attackTactic == 1 ? 1f : 1.25f;
            double bombTons = bombersLeft * aBs.Bombload / 1000.0;
            o.InfrastructureDamage = (float)Math.Clamp(
                bombTons * pattern * wx * access / Math.Max(8, targetScale) * 3.0, 0, 100);
        }
        if (o.Superiority < -0.1f && dFightLeft > 0)
            o.CasDef = 1 + Math.Clamp(dFightLeft * dFs.Cas /
                Math.Max(50, (dSold(request) + 1) * 0.02f), 0, 0.4f);
        return o;
    }

    static long aSold(BattleRequest r) => r.Attackers.Sum(x => x.Soldiers);
    static long dSold(BattleRequest r) => r.Defenders.Sum(x => x.Soldiers);

    static byte PickWeather(ref XorRng rng)
    {
        float r = rng.NextF();
        if (r < 0.45f) return W_CLEAR;
        if (r < 0.68f) return W_CLOUD;
        if (r < 0.84f) return W_RAIN;
        if (r < 0.94f) return W_FOG;
        return W_SNOW;
    }

    static byte TimeAtTick(int tick) => (byte)((_startTime + tick / 30) & 3);

    static string WeatherSummary() => _startWeather == _weather
        ? WeatherName[_startWeather]
        : $"آغاز {WeatherName[_startWeather]}، پایان {WeatherName[_weather]}";

    static float Hash(int x, int y, uint seed)
    {
        uint h = (uint)(x * 374761393 + y * 668265263) ^ seed;
        h = (h ^ (h >> 13)) * 1274126177u;
        return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777215f;
    }

    static float Noise(float x, float y, uint seed)
    {
        int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y);
        float fx = x - xi, fy = y - yi;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        float a = Hash(xi, yi, seed), b = Hash(xi + 1, yi, seed);
        float c = Hash(xi, yi + 1, seed), d = Hash(xi + 1, yi + 1, seed);
        return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
    }

    static void GenTerrain(ref XorRng rng)
    {
        uint s1 = (uint)rng.NextU(), s2 = (uint)rng.NextU(), s3 = (uint)rng.NextU();
        for (int y = 0; y < GRID_H; y++)
        for (int x = 0; x < GRID_W; x++)
        {
            float e = Noise(x * 0.09f, y * 0.09f, s1) * 0.65f +
                      Noise(x * 0.23f, y * 0.23f, s2) * 0.35f;
            float v = Noise(x * 0.13f + 50, y * 0.13f, s3);
            int i = y * GRID_W + x;
            _elev![i] = e;
            _terr![i] = e > 0.78f ? T_RIDGE : e > 0.62f ? T_HILL :
                v > 0.72f && e > 0.3f ? T_FOREST :
                v < 0.12f && e < 0.35f ? T_MARSH :
                v > 0.62f && v <= 0.72f && e < 0.5f ? T_URBAN : T_PLAIN;
        }
    }

    static byte TerrAt(float x, float y)
    {
        int gx = Math.Clamp((int)(x / CELL), 0, GRID_W - 1);
        int gy = Math.Clamp((int)((y + 6) / CELL), 0, GRID_H - 1);
        return _terr![gy * GRID_W + gx];
    }

    static float ElevAt(float x, float y)
    {
        int gx = Math.Clamp((int)(x / CELL), 0, GRID_W - 1);
        int gy = Math.Clamp((int)((y + 6) / CELL), 0, GRID_H - 1);
        return _elev![gy * GRID_W + gx];
    }

    static int BuildSide(Group[] groups, bool attacker, long tanks, long soldiers,
        int strategy, int tactic, TankSpec tankSpec, ref XorRng rng)
    {
        long raw = tanks / TANK_GROUP + soldiers / INF_GROUP + 2;
        float scale = raw > MAX_GROUPS ? (float)raw / MAX_GROUPS : 1;
        float tg = TANK_GROUP * scale, ig = INF_GROUP * scale;
        int n = 0;
        for (long left = tanks; left > 0 && n < MAX_GROUPS; n++)
        {
            float amount = (float)Math.Min(left, (long)Math.Ceiling(tg));
            InitGroup(ref groups[n], attacker, 1, amount, strategy, tactic, tankSpec, ref rng);
            left -= (long)amount;
        }
        for (long left = soldiers; left > 0 && n < MAX_GROUPS; n++)
        {
            float amount = (float)Math.Min(left, (long)Math.Ceiling(ig));
            InitGroup(ref groups[n], attacker, 0, amount, strategy, tactic, tankSpec, ref rng);
            left -= (long)amount;
        }
        return n;
    }

    static void InitGroup(ref Group g, bool attacker, byte type, float units,
        int strategy, int tactic, TankSpec tankSpec, ref XorRng rng)
    {
        g = default;
        g.Type = type; g.Units = units; g.Size0 = units; g.Alive = true;
        g.Morale = rng.Range(0.85f, 1);
        if (type == 1)
        {
            g.CAmmo = units * Math.Clamp(tankSpec.CannonAmmo / 70f, 0.65f, 1.5f);
            g.MAmmo = units * Math.Clamp(tankSpec.MgAmmo / 60f, 0.65f, 1.5f);
        }
        else
        {
            g.CAmmo = 0;
            g.MAmmo = units;
        }
        g.Exp = rng.Range(0, 0.1f); g.FireTgt = -1;
        if (attacker)
        {
            g.Y = rng.Range(-4.5f, -1.5f);
            if (strategy == 1)
            {
                float center = tactic == 1 ? FRONT_KM * 0.5f :
                    (rng.NextF() < 0.5f ? FRONT_KM * 0.3f : FRONT_KM * 0.7f);
                g.X = Math.Clamp(center + rng.Range(-5, 5), 1, FRONT_KM - 1);
            }
            else { g.X = rng.Range(1, FRONT_KM - 1); g.Posture = P_FLANK; }
            if (g.Posture != P_FLANK) g.Posture = P_ADVANCE;
            g.TgtX = g.X; g.TgtY = 8;
        }
        else
        {
            g.X = rng.Range(1, FRONT_KM - 1);
            if (strategy == 1)
            {
                g.Y = tactic == 1 ? rng.Range(0.8f, 3.2f) : rng.Range(1.5f, 6);
                g.Posture = tactic == 1 ? P_DEFEND : P_PATROL;
                if (tactic == 1) SeekCover(ref g, ref rng);
            }
            else
            {
                g.Y = tactic == 1 ? rng.Range(2, 7) : rng.Range(4, 10);
                g.Posture = P_AMBUSH; SeekCover(ref g, ref rng);
            }
            g.TgtX = g.X; g.TgtY = g.Y;
        }
        g.Sector = (byte)Math.Clamp((int)(g.X / 4), 0, 9);
    }

    static void SeekCover(ref Group g, ref XorRng rng)
    {
        float bx = g.X, by = g.Y, best = TerCover[TerrAt(g.X, g.Y)];
        for (int i = 0; i < 6; i++)
        {
            float x = Math.Clamp(g.X + rng.Range(-2, 2), 0.5f, FRONT_KM - 0.5f);
            float y = Math.Clamp(g.Y + rng.Range(-1.5f, 1.5f), 0.3f, DEPTH_KM - 1);
            float c = TerCover[TerrAt(x, y)];
            if (c > best) { best = c; bx = x; by = y; }
        }
        g.X = bx; g.Y = by;
    }

    static float SenseSide(Group[] own, int nOwn, Group[] foe, int nFoe,
        Intel[] intel, bool reconBonus, float visEnv, ref XorRng rng)
    {
        float sum = 0; int alive = 0;
        for (int j = 0; j < nFoe; j++)
        {
            if (!foe[j].Alive) { intel[j].Level *= 0.9f; continue; }
            alive++; ref Intel it = ref intel[j]; it.Stale += TICK_MIN;
            float gain = 0, conceal = TerCover[TerrAt(foe[j].X, foe[j].Y)];
            if (foe[j].Posture == P_AMBUSH && !foe[j].Sprung) conceal = Math.Min(0.92f, conceal + 0.35f);
            for (int i = 0; i < nOwn; i++)
            {
                if (!own[i].Alive) continue;
                float dx = own[i].X - foe[j].X, dy = own[i].Y - foe[j].Y;
                float dist2 = dx * dx + dy * dy;
                if (dist2 > 36) continue;
                float dist = MathF.Sqrt(dist2);
                float vis = (own[i].Type == 1 ? 2.6f : 2.1f) *
                    TerVision[TerrAt(own[i].X, own[i].Y)] * visEnv;
                if (ElevAt(own[i].X, own[i].Y) > ElevAt(foe[j].X, foe[j].Y) + 0.12f) vis *= 1.3f;
                if (reconBonus) vis *= 1.25f;
                float move = foe[j].Posture is P_ADVANCE or P_FLANK or P_ASSAULT ? 0.25f : 0;
                float p = (1 - Math.Clamp(dist / Math.Max(0.3f, vis), 0, 1)) * (1 - conceal) +
                          foe[j].Signature + move;
                gain = Math.Max(gain, p);
            }
            if (gain > 0.04f && rng.NextF() < Math.Clamp(gain, 0, 0.95f))
            {
                it.Level = Math.Min(1, it.Level + 0.45f + gain * 0.5f);
                it.LastX = foe[j].X; it.LastY = foe[j].Y; it.Stale = 0;
            }
            else
            {
                it.Level *= it.Stale > 60 ? 0.93f : 0.985f;
                if (it.Stale > 150) it.Level *= 0.85f;
            }
            sum += it.Level;
        }
        for (int j = 0; j < nFoe; j++) foe[j].Signature *= 0.55f;
        return alive > 0 ? sum / alive : 0;
    }

    static void BuildThreatMap(Group[] foe, int n, Intel[] intel, float[] map)
    {
        Array.Clear(map);
        for (int j = 0; j < n; j++)
        {
            if (!foe[j].Alive || intel[j].Level < 0.15f) continue;
            int sector = Math.Clamp((int)(intel[j].LastX / 4), 0, 9);
            float power = foe[j].Type == 1 ? foe[j].Units * 9 : foe[j].Units * 0.8f;
            map[sector] += power * intel[j].Level;
        }
    }

    static int WeakestSector(float[] threat, ref XorRng rng)
    {
        int best = 0; float value = float.MaxValue;
        for (int s = 1; s < 9; s++)
        {
            float v = threat[s] + threat[s - 1] * 0.4f + threat[s + 1] * 0.4f + rng.NextF() * 8;
            if (v < value) { value = v; best = s; }
        }
        return best;
    }

    static void CommandAttacker(int n, int strategy, int tactic, float depth, float intel,
        ref XorRng rng, ref bool encircled, int tick)
    {
        int weak = WeakestSector(_threatA!, ref rng);
        float mainX = (weak + 0.5f) * 4;
        for (int i = 0; i < n; i++)
        {
            ref Group g = ref _atk![i];
            if (!g.Alive || g.Posture == P_RETREAT) continue;
            float ammo = (g.CAmmo + g.MAmmo) / Math.Max(0.01f, g.Size0 * 2);
            if (ammo <= 0.02f) { g.Posture = P_RETREAT; g.TgtY = -4; continue; }
            if (ammo < 0.18f || g.Morale < 0.35f) { g.Posture = P_HOLD; continue; }
            if (strategy == 1)
            {
                bool probe = tactic == 2 && tick < 40 && intel < 0.35f;
                g.Posture = probe ? P_PATROL : depth > 2 ? P_ASSAULT : P_ADVANCE;
                float spread = probe ? 14 : tactic == 1 ? 4 : 7;
                g.TgtX = Math.Clamp(mainX + rng.Range(-spread, spread), 1, FRONT_KM - 1);
                g.TgtY = g.Y + 6;
            }
            else
            {
                bool left = (i & 1) == 0;
                float arm = left ? mainX - 8 - depth * 0.3f : mainX + 8 + depth * 0.3f;
                if (tactic == 2) arm += MathF.Sin((tick + i * 7) * 0.05f) * 5;
                g.TgtX = Math.Clamp(arm + rng.Range(-3, 3), 1, FRONT_KM - 1);
                g.TgtY = g.Y + (g.Type == 1 ? 6 : 4); g.Posture = P_FLANK;
                if (depth > 8 && !encircled && intel > 0.45f) encircled = true;
            }
        }
    }

    static void CommandDefender(int n, int strategy, int tactic, float depth, ref XorRng rng)
    {
        int hot = 0; float value = -1;
        for (int s = 0; s < 10; s++) if (_threatD![s] > value) { value = _threatD[s]; hot = s; }
        float hotX = (hot + 0.5f) * 4;
        for (int i = 0; i < n; i++)
        {
            ref Group g = ref _def![i];
            if (!g.Alive || g.Posture == P_RETREAT) continue;
            float ammo = (g.CAmmo + g.MAmmo) / Math.Max(0.01f, g.Size0 * 2);
            if (ammo <= 0.02f) { g.Posture = P_RETREAT; g.TgtY = Math.Min(DEPTH_KM - 1, g.Y + 6); continue; }
            if (strategy == 1)
            {
                if (tactic == 1)
                {
                    bool reserve = i % 3 == 2;
                    if (reserve && value > 0 && depth > 1)
                    {
                        g.TgtX = Math.Clamp(hotX + rng.Range(-3, 3), 1, FRONT_KM - 1);
                        g.TgtY = Math.Max(0.8f, depth - 1); g.Posture = P_ADVANCE;
                    }
                    else g.Posture = P_DEFEND;
                }
                else
                {
                    g.TgtX = value > 0 ? Math.Clamp(hotX + rng.Range(-5, 5), 1, FRONT_KM - 1) :
                        Math.Clamp(g.X + rng.Range(-6, 6), 1, FRONT_KM - 1);
                    g.TgtY = Math.Clamp(depth + rng.Range(0, 2), 1, 8); g.Posture = value > 0 ? P_ADVANCE : P_PATROL;
                }
            }
            else if (tactic == 1 && !g.Sprung) g.Posture = P_AMBUSH;
            else
            {
                g.Posture = P_ASSAULT;
                g.TgtX = Math.Clamp(hotX + rng.Range(-5, 5), 1, FRONT_KM - 1);
                g.TgtY = Math.Max(1, depth - (tactic == 2 ? 2 : 0));
            }
        }
    }

    static void MoveSide(Group[] groups, int n, TankSpec spec, bool attacker)
    {
        float weather = WxSpeed[_weather];
        for (int i = 0; i < n; i++)
        {
            ref Group g = ref groups[i];
            if (!g.Alive || g.Posture is P_DEFEND or P_AMBUSH or P_HOLD) continue;
            float speed = g.Type == 1 ? spec.Speed * 0.32f : 4.2f;
            if (g.Posture == P_RETREAT) speed *= 1.2f;
            if (g.Supp > 0.5f) speed *= 0.45f;
            speed *= 1 - g.Fatigue * 0.3f;
            float step = speed * TerSpeed[TerrAt(g.X, g.Y)] * weather * TICK_MIN / 60f;
            float dx = g.TgtX - g.X, dy = g.TgtY - g.Y;
            float distance = MathF.Sqrt(dx * dx + dy * dy);
            if (distance < 0.15f) continue;
            float move = Math.Min(step, distance);
            g.X += dx / distance * move; g.Y += dy / distance * move;
            g.X = Math.Clamp(g.X, 0.2f, FRONT_KM - 0.2f);
            g.Y = Math.Clamp(g.Y, -6, DEPTH_KM);
            if (move > 0.5f) g.Signature = Math.Min(1, g.Signature + 0.18f);
            g.Sector = (byte)Math.Clamp((int)(g.X / 4), 0, 9);
        }
    }

    static float FireSide(Group[] own, int nOwn, TankSpec ospec, Group[] foe, int nFoe,
        TankSpec fspec, Intel[] intel, bool attacker, int strategy, int tactic,
        int foeStrategy, bool encircled, float combatMul, float accEnv, ref XorRng rng,
        ref int evtN, int tick, ref bool contact, ref bool ambushFired)
    {
        float duel = 0;
        for (int i = 0; i < nOwn; i++)
        {
            ref Group u = ref own[i];
            if (!u.Alive || u.Posture == P_RETREAT) continue;
            int best = -1; float bestScore = 0, bestDist = 99;
            float maxRange = u.Type == 1 ? 2.1f : 0.9f;
            for (int j = 0; j < nFoe; j++)
            {
                if (!foe[j].Alive || intel[j].Level < 0.2f) continue;
                float dx = foe[j].X - u.X, dy = foe[j].Y - u.Y;
                float distance = MathF.Sqrt(dx * dx + dy * dy);
                if (distance > maxRange + 0.6f) continue;
                float priority = u.Type == 1 ? (foe[j].Type == 1 ? 3 : 1.6f) :
                    (foe[j].Type == 1 ? 0.6f : 2.2f);
                float score = priority * intel[j].Level / (0.4f + distance);
                if (score > bestScore) { bestScore = score; best = j; bestDist = distance; }
            }
            u.FireTgt = (short)best;
            if (best < 0 || bestDist > maxRange) continue;
            if (!contact) { contact = true; AddEvt(ref evtN, tick, E_CONTACT, u.X); }
            float ambush = 1;
            if (u.Posture == P_AMBUSH && !u.Sprung)
            {
                u.Sprung = true; ambush = 2.6f;
                if (!ambushFired) { ambushFired = true; AddEvt(ref evtN, tick, E_AMBUSH, u.X, u.Y); }
            }
            ref Group target = ref foe[best];
            float intelQ = intel[best].Level;
            float acc = 0.62f * (0.45f + 0.55f * intelQ) * TerAcc[TerrAt(u.X, u.Y)] *
                accEnv * (1 - u.Supp * 0.5f) * (0.9f + u.Exp * 0.3f);
            if (u.Posture is P_ADVANCE or P_ASSAULT or P_FLANK) acc *= 0.78f;
            if (ElevAt(u.X, u.Y) > ElevAt(target.X, target.Y) + 0.1f) acc *= 1.18f;
            float cover = TerCover[TerrAt(target.X, target.Y)] *
                (target.Posture is P_DEFEND or P_AMBUSH or P_HOLD ? 1.25f : 0.8f);
            float ammoRatio = (u.CAmmo + u.MAmmo) / Math.Max(0.01f, u.Size0 * 2);
            float ammo = ammoRatio > 0.5f ? 1 : 0.55f + ammoRatio * 0.9f;
            float k = acc * ammo * (attacker && encircled && strategy == 2 ? 1.25f : 1) *
                (0.55f + u.Morale * 0.45f) * ambush * combatMul * (1 - u.Fatigue * 0.25f);
            if (u.Type == 1 && target.Type == 1 && u.CAmmo > 0.05f)
            {
                float range = Math.Clamp(1.25f - bestDist * 0.45f, 0.45f, 1.2f);
                float armor = fspec.Armor * (target.Posture is P_DEFEND or P_AMBUSH ? 1.3f : 1);
                float pen = 1 / (1 + MathF.Exp(-(ospec.Pen * range - armor) / 9));
                float shots = u.Units * 1.6f * k;
                float kills = shots * 0.32f * pen * rng.Range(0.9f, 1.15f);
                ApplyDamage(ref target, kills, intel, best);
                u.CAmmo = Math.Max(0, u.CAmmo - shots * 0.05f);
                u.Signature = Math.Min(1, u.Signature + 0.55f); duel += kills;
            }
            else if (u.Type == 1 && target.Type == 0 && u.MAmmo > 0.05f)
            {
                float kills = u.Units * (ospec.Mg * 1.05f * (1 - cover * 0.85f) +
                    (u.CAmmo > 0.05f ? ospec.He * 4.5f * (1 - cover * 0.55f) : 0)) * k;
                ApplyDamage(ref target, kills, intel, best);
                u.MAmmo = Math.Max(0, u.MAmmo - u.Units * 0.06f);
                u.CAmmo = Math.Max(0, u.CAmmo - u.Units * 0.04f);
                target.Supp = Math.Min(1, target.Supp + 0.3f);
            }
            else if (u.Type == 0 && target.Type == 0 && u.MAmmo > 0.05f)
            {
                float kills = u.Units * 0.045f * k * (1 - cover * 0.8f);
                ApplyDamage(ref target, kills, intel, best);
                u.MAmmo = Math.Max(0, u.MAmmo - u.Units * 0.045f);
                target.Supp = Math.Min(1, target.Supp + 0.15f);
            }
            else if (u.Type == 0 && target.Type == 1 && bestDist < 0.45f)
            {
                float kills = u.Units * 0.0045f * k * (foeStrategy == 2 ? 1.2f : 1);
                ApplyDamage(ref target, kills, intel, best); duel += kills * 0.5f;
            }
        }
        return duel;
    }

    static void ApplyDamage(ref Group target, float kills, Intel[] intel, int index)
    {
        if (kills <= 0) return;
        target.Units = Math.Max(0, target.Units - kills);
        target.Morale = Math.Max(0, target.Morale - kills / Math.Max(1, target.Size0) * 1.6f);
        if (target.Units < target.Size0 * 0.08f || target.Units < 0.5f)
        {
            target.Alive = false; intel[index].Level = 0;
        }
    }

    static void MoraleSide(Group[] groups, int n, bool attacker, ref XorRng rng,
        ref int evtN, int tick)
    {
        for (int i = 0; i < n; i++)
        {
            ref Group g = ref groups[i];
            if (!g.Alive) continue;
            g.Supp = Math.Max(0, g.Supp - 0.08f);
            g.Morale = Math.Min(1, g.Morale + 0.004f);
            bool active = g.Posture is P_ADVANCE or P_ASSAULT or P_FLANK or P_RETREAT;
            g.Fatigue = Math.Clamp(g.Fatigue + (active ? 0.006f : -0.004f), 0, 1);
            if (g.Supp > 0.1f) g.Exp = Math.Min(1, g.Exp + 0.003f);
            float loss = 1 - g.Units / Math.Max(1, g.Size0);
            if (loss > 0.5f && g.Morale < 0.3f && rng.NextF() < 0.12f)
            {
                if (g.Posture != P_RETREAT) AddEvt(ref evtN, tick, E_ROUT, attacker ? 0 : 1);
                g.Posture = P_RETREAT; g.TgtY = attacker ? -5 : Math.Min(DEPTH_KM, g.Y + 8);
            }
        }
    }

    static float EffectiveDepth(Group[] groups, int n)
    {
        // عمق مؤثر: عمیق‌ترین گروه زنده که «پشتیبانی» دارد با وزن کامل و بدون پشتیبانی با نصف وزن.
        // قبلاً گروهِ بدون جفتِ نزدیک (کمتر از ۳٫۵km) اصلاً در عمق حساب نمی‌شد؛ نتیجه این بود که
        // حتی با نابودی کامل ارتش مدافع، عمق صفر می‌ماند و مهاجم «بازنده» اعلام می‌شد.
        const float supportDist = 3.5f;
        float best = 0;
        for (int i = 0; i < n; i++)
        {
            ref Group g = ref groups[i];
            if (!g.Alive || g.Posture == P_RETREAT) continue;
            float power = g.Type == 1 ? g.Units * 10 : g.Units;
            if (power < 25) continue;
            bool supported = false;
            for (int j = 0; j < n; j++)
            {
                if (i == j || !groups[j].Alive || groups[j].Posture == P_RETREAT) continue;
                float dx = groups[i].X - groups[j].X, dy = groups[i].Y - groups[j].Y;
                if (dx * dx + dy * dy < supportDist * supportDist) { supported = true; break; }
            }
            float depth = supported ? g.Y : g.Y * 0.5f;
            if (depth > best) best = depth;
        }
        return Math.Max(0, best);
    }

    static float SidePower(Group[] groups, int n, TankSpec spec)
    {
        float power = 0;
        for (int i = 0; i < n; i++)
        {
            if (!groups[i].Alive) continue;
            float ammo = (groups[i].CAmmo + groups[i].MAmmo) / Math.Max(0.01f, groups[i].Size0 * 2);
            float availability = 0.45f + 0.55f * Math.Clamp(ammo * 1.6f, 0, 1);
            power += (groups[i].Type == 1
                ? groups[i].Units * (8 + spec.Armor * 0.04f + spec.Pen * 0.04f)
                : groups[i].Units * 0.85f) * availability;
        }
        return power;
    }

    static void CountLosses(Group[] groups, int n, ref long tanks, ref long soldiers)
    {
        double t = 0, s = 0;
        for (int i = 0; i < n; i++)
        {
            double lost = groups[i].Size0 - (groups[i].Alive ? groups[i].Units : 0);
            if (groups[i].Type == 1) t += lost; else s += lost;
        }
        tanks = (long)Math.Round(t); soldiers = (long)Math.Round(s);
    }

    static void AddEvt(ref int n, int tick, byte kind, float a, float b = 0)
    {
        if (n >= _evts!.Length) return;
        _evts[n++] = new Evt { Tick = (short)tick, Kind = kind, A = a, B = b };
    }

    static string AttackGroundDoctrine(BattleOrders o) => o.GroundStrategy == 1
        ? "هجوم منسجم" : "محاصره و ضربه";

    static string AttackGroundTactic(BattleOrders o) => o.GroundStrategy == 1
        ? (o.GroundTactic == 1 ? "حمله مستقیم به قلب خط دفاع" : "حملات سبک هدفدار و حمله سنگین متمرکز")
        : (o.GroundTactic == 1 ? "حلقهٔ محاصره با حملات پراکنده و هجوم سریع" : "حلقه محاصره متحرک و ضربات سنگین");

    static string DefenseGroundDoctrine(BattleOrders o) => o.GroundStrategy == 1
        ? "دفاع منسجم" : "دفاع و ضدحملهٔ پراکنده";

    static string DefenseGroundTactic(BattleOrders o) => o.GroundStrategy == 1
        ? (o.GroundTactic == 1 ? "دفاع ایستا و ثابت با قوای زرهی" : "گشت متحرک با گروه‌های ترکیبی")
        : (o.GroundTactic == 1 ? "استتار و ضربه به گروه‌های پیشرو" : "عقب‌نشینی تاکتیکی و تله‌گذاری مخفی");

    static string AttackAirDoctrine(BattleOrders o) => o.AirStrategy switch
    {
        1 => $"برتری هوایی / {(o.AirTactic == 2 ? "حمله به پایگاه‌ها" : "شکار آزاد")}",
        2 => $"بمباران راهبردی / {(o.AirTactic == 2 ? "بمباران منطقه‌ای" : "بمباران دقیق")}",
        _ => "بدون عملیات هوایی"
    };

    static string DefenseAirDoctrine(BattleOrders o) => o.AirStrategy switch
    {
        1 => $"دفاع منطقه‌ای / {(o.AirTactic == 2 ? "ایستگاه‌های شنود و هشدار سریع" : "گشت هوایی رزمی")}",
        2 => $"دفاع نقطه‌ای / {(o.AirTactic == 2 ? "پوشش مستقیم جنگنده" : "آتش‌بند")}",
        _ => "بدون عملیات هوایی"
    };

    static string EventCode(byte kind) => kind switch
    {
        E_CONTACT => "CONTACT", E_AMBUSH => "AMBUSH", E_BREAK5 => "BREAKTHROUGH_5",
        E_BREAK10 => "BREAKTHROUGH_10", E_BREAK20 => "BREAKTHROUGH_20",
        E_BREAK30 => "BREAKTHROUGH_30", E_ENCIRCLE => "ENCIRCLEMENT",
        E_ROUT => "ROUT", E_SHIFT => "WEATHER_SHIFT", E_HALT => "HALT", _ => "EVENT"
    };

    static string? EventText(Evt e) => e.Kind switch
    {
        E_CONTACT => $"تماس اولیه در ساعت {e.Tick * TICK_MIN / 60:F1}",
        E_AMBUSH => $"کمین مدافع در عمق {e.B:F1}km فعال شد",
        E_BREAK5 => "رخنه از ۵ کیلومتر گذشت",
        E_BREAK10 => "رخنه به ۱۰ کیلومتر رسید",
        E_BREAK20 => "ستون پیشرو وارد عمق ۲۰ کیلومتر شد",
        E_BREAK30 => "خط دفاع در عمق نهایی شکست",
        E_ENCIRCLE => $"حلقه محاصره در عمق {e.A:F1}km بسته شد",
        E_ROUT => e.A < 0.5f ? "بخشی از مهاجمان گریختند" : "بخشی از مدافعان تارومار شدند",
        E_SHIFT => $"هوا از {WeatherName[Math.Clamp((int)e.A, 0, WeatherName.Length - 1)]} به " +
                   $"{WeatherName[Math.Clamp((int)e.B, 0, WeatherName.Length - 1)]} تغییر کرد",
        E_HALT => $"عملیات در {e.A:F1}km زمین‌گیر شد",
        _ => null
    };

    static void BuildReports(BattleRequest request, BattleResult r, TankSpec aSpec,
        TankSpec dSpec, float strategyAdvantage, int evtN,
        float aIntel, float dIntel, float depth)
    {
        string attacker = request.Attackers.FirstOrDefault()?.CountryName ?? "مهاجم";
        string defender = request.Defenders.FirstOrDefault()?.CountryName ?? "مدافع";
        string outcome = r.OutcomeKind switch
        {
            BattleOutcomeKind.AttackerHeavyVictory => $"🏆 پیروزی سنگین {attacker}",
            BattleOutcomeKind.AttackerLimitedVictory => $"⚔️ پیروزی {attacker}",
            BattleOutcomeKind.AttackerRouted => $"🛡 دفاع کامل {defender}",
            BattleOutcomeKind.DefenderVictory => $"🛡 پیروزی مدافع {defender}",
            _ => $"⚖️ موفقیت {r.SuccessPercent}٪ مهاجم"
        };
        string attackDoctrine = AttackGroundDoctrine(request.AttackerOrders);
        string defenseDoctrine = DefenseGroundDoctrine(request.DefenderOrders);
        int h = r.DurationMinutes / 60, m = r.DurationMinutes % 60;
        for (int i = 0; i < evtN; i++)
        {
            Evt e = _evts![i];
            string? text = EventText(e);
            if (text != null)
                r.Events.Add(new BattleEvent
                {
                    Minute = (int)Math.Round(e.Tick * TICK_MIN),
                    Code = EventCode(e.Kind),
                    Text = text
                });
        }
        var sb = _sb!; sb.Clear();
        sb.AppendLine($"⚔️ گزارش تاکتیکی — {attacker} علیه {defender}");
        sb.AppendLine(outcome);
        sb.AppendLine($"🌦 {WeatherSummary()} | 🕓 آغاز {TimeName[_startTime]} | شبکه ۵۰۰ متری");
        sb.AppendLine($"📍 عمق نفوذ: {depth:F1}km از 30km عملیاتی ({r.EffectiveAdvanceKm:F1}/40km راهبردی)");
        sb.AppendLine($"🧠 {attackDoctrine} — {AttackGroundTactic(request.AttackerOrders)}");
        sb.AppendLine($"🛡 {defenseDoctrine} — {DefenseGroundTactic(request.DefenderOrders)}");
        sb.AppendLine($"📐 ضریب اجرای تاکتیکی: {strategyAdvantage:F2}x");
        sb.AppendLine($"✈️ {AttackAirDoctrine(request.AttackerOrders)} برابر {DefenseAirDoctrine(request.DefenderOrders)}");
        sb.AppendLine($"🔧 زرهی: {aSpec.Name} برابر {dSpec.Name}");
        if (request.Defenders.Any(x => x.IsHomelandDefender))
            sb.AppendLine($"🏠 دفاع از وطن مدافع فعال است (+{(int)(HOMELAND_COMBAT_BONUS * 100)}٪)");
        if (r.AttackerMoneyGained > 0 || r.AttackerIronGained > 0)
            sb.AppendLine($"💰 غنیمت: {r.AttackerMoneyGained:N0} پول، {r.AttackerIronGained:N0} آهن");
        for (int i = 0; i < evtN && i < 12; i++)
        {
            string? text = EventText(_evts![i]);
            if (text != null) sb.AppendLine($"• {text}");
        }
        sb.AppendLine($"🔎 کیفیت اطلاعات: مهاجم {aIntel:P0}، مدافع {dIntel:P0}");
        sb.AppendLine($"🔻 مهاجم: {r.AttackerTanksLost} تانک، {r.AttackerSoldiersLost} سرباز، " +
                      $"{r.AttackerFightersLost} جنگنده، {r.AttackerBombersLost} بمب‌افکن");
        sb.AppendLine($"🔻 مدافع: {r.DefenderTanksLost} تانک، {r.DefenderSoldiersLost} سرباز، " +
                      $"{r.DefenderFightersLost} جنگنده، {r.DefenderBombersLost} بمب‌افکن، {r.DefenderAntiAirLost} پدافند");
        sb.AppendLine($"🛫 برتری هوایی: {AirSupText(r.AirSuperiority)}");
        if (r.InfrastructureDamage > 0.05)
            sb.AppendLine($"🏭 خسارت زیرساختی بمباران: {r.InfrastructureDamage:F1}٪");
        sb.AppendLine($"⏱ مدت: {h} ساعت و {m} دقیقه");
        r.AttackerReport = sb.ToString();

        sb.Clear();
        sb.AppendLine($"🛡 گزارش دفاع — {defender} برابر {attacker}");
        sb.AppendLine(outcome);
        sb.AppendLine($"🌦 {WeatherSummary()} | 🕓 آغاز {TimeName[_startTime]}");
        sb.AppendLine($"🛡 {defenseDoctrine} — {DefenseGroundTactic(request.DefenderOrders)}");
        if (request.Defenders.Any(x => x.IsHomelandDefender))
            sb.AppendLine($"🏠 دفاع از وطن: فعال (+{(int)(HOMELAND_COMBAT_BONUS * 100)}٪)");
        sb.AppendLine($"✈️ {DefenseAirDoctrine(request.DefenderOrders)} برابر {AttackAirDoctrine(request.AttackerOrders)}");
        sb.AppendLine($"📍 نفوذ دشمن: {r.EffectiveAdvanceKm:F1}/40km");
        sb.AppendLine($"🔻 تلفات شما: {r.DefenderTanksLost} تانک، {r.DefenderSoldiersLost} سرباز، " +
                      $"{r.DefenderFightersLost} جنگنده، {r.DefenderBombersLost} بمب‌افکن، {r.DefenderAntiAirLost} پدافند");
        sb.AppendLine($"🔻 تلفات دشمن: {r.AttackerTanksLost} تانک، {r.AttackerSoldiersLost} سرباز، " +
                      $"{r.AttackerFightersLost} جنگنده، {r.AttackerBombersLost} بمب‌افکن");
        if (r.InfrastructureDamage > 0.05)
            sb.AppendLine($"🏭 خسارت زیرساختی: {r.InfrastructureDamage:F1}٪");
        if (r.DefenderMoneyLost > 0 || r.DefenderIronLost > 0)
            sb.AppendLine($"💰 غارت‌شده: {r.DefenderMoneyLost:N0} پول، {r.DefenderIronLost:N0} آهن");
        sb.AppendLine($"⏱ مدت: {h} ساعت و {m} دقیقه");
        r.DefenderReport = sb.ToString();

        r.GroupAnnouncement = $"📰 خبر جنگ!\n⚔️ {attacker} علیه {defender}\n{outcome}\n" +
            $"🎯 {attackDoctrine} ↔ {defenseDoctrine}\n" +
            $"📍 {r.EffectiveAdvanceKm:F1}km | ⏱ {h}:{m:D2}\n" +
            $"💀 مهاجم: {r.AttackerTanksLost}🛡 {r.AttackerSoldiersLost}🪖 | " +
            $"مدافع: {r.DefenderTanksLost}🛡 {r.DefenderSoldiersLost}🪖";
        if (r.AttackerMoneyGained > 0 || r.AttackerIronGained > 0)
            r.GroupAnnouncement += $"\n💰 غنیمت: {r.AttackerMoneyGained:N0} پول، {r.AttackerIronGained:N0} آهن";
        r.ScenarioSummary = $"{WeatherSummary()}، آغاز {TimeName[_startTime]}، شبکه ۵۰۰ متری";
        r.Events.Add(new BattleEvent { Minute = r.DurationMinutes, Code = "END", Text = outcome });
    }

    static string AirSupText(double value) => value switch
    {
        > 0.4 => "قاطع با مهاجم",
        > 0.12 => "نسبی با مهاجم",
        < -0.4 => "قاطع با مدافع",
        < -0.12 => "نسبی با مدافع",
        _ => "متوازن"
    };

    static void AllocateParticipantLosses(IReadOnlyList<BattleParticipant> side,
        long tankLoss, long tankDestroyed, long soldierLoss, long fighterLoss,
        long bomberLoss, long antiAirLoss, Dictionary<long, ParticipantBattleLoss> output)
    {
        foreach (var p in side) output[p.OwnerId] = new ParticipantBattleLoss { OwnerId = p.OwnerId };
        AllocateScalar(side, p => p.Soldiers, soldierLoss,
            (p, n) => { output[p.OwnerId].SoldiersKilled += n / 4; output[p.OwnerId].SoldiersWounded += n - n / 4; });
        AllocateScalar(side, p => p.AntiAir, antiAirLoss,
            (p, n) => output[p.OwnerId].AntiAirLost += n);
        long remainingTankLoss = Math.Max(0, tankLoss);
        long remainingDestroyed = Math.Min(remainingTankLoss, Math.Max(0, tankDestroyed));
        AllocateModels(side, p => p.Tanks, tankLoss, ModelKind.Tank,
            (p, model, n) =>
            {
                long destroyed = remainingTankLoss == n ? remainingDestroyed :
                    Math.Min(n, (long)Math.Round((double)remainingDestroyed * n /
                                                Math.Max(1, remainingTankLoss)));
                Add(output[p.OwnerId].TanksDestroyed, model, destroyed);
                Add(output[p.OwnerId].TanksDamaged, model, n - destroyed);
                Add(output[p.OwnerId].TanksUnavailable, model, n);
                remainingDestroyed -= destroyed;
                remainingTankLoss -= n;
            });
        AllocateModels(side, p => p.Fighters, fighterLoss, ModelKind.Fighter,
            (p, model, n) => Add(output[p.OwnerId].FightersUnavailable, model, n));
        AllocateModels(side, p => p.Bombers, bomberLoss, ModelKind.Bomber,
            (p, model, n) => Add(output[p.OwnerId].BombersUnavailable, model, n));
    }

    enum ModelKind { Tank, Fighter, Bomber }

    static void AllocateScalar(IReadOnlyList<BattleParticipant> side,
        Func<BattleParticipant, long> selector, long loss, Action<BattleParticipant, long> assign)
    {
        long total = side.Sum(selector); if (total <= 0 || loss <= 0) return;
        long remaining = Math.Min(loss, total), capacity = total;
        for (int i = 0; i < side.Count; i++)
        {
            long available = selector(side[i]);
            long amount = i == side.Count - 1 ? remaining :
                Math.Min(available, (long)Math.Round((double)remaining * available / Math.Max(1, capacity)));
            amount = Math.Min(amount, remaining); assign(side[i], amount);
            remaining -= amount; capacity -= available;
        }
    }

    static void AllocateModels(IReadOnlyList<BattleParticipant> side,
        Func<BattleParticipant, List<ModelAmount>> selector, long requested, ModelKind kind,
        Action<BattleParticipant, string, long> assign)
    {
        long total = side.Sum(p => selector(p).Sum(x => Math.Max(0, x.Count)));
        long loss = Math.Min(Math.Max(0, requested), total);
        if (loss <= 0 || total <= 0) return;
        long remaining = loss, capacity = total;
        foreach (var p in side)
        foreach (var item in selector(p).Where(x => x.Count > 0))
        {
            long amount = capacity == item.Count ? remaining :
                Math.Min(item.Count, (long)Math.Round((double)remaining * item.Count / Math.Max(1, capacity)));
            amount = Math.Min(amount, remaining);
            string model = kind switch
            {
                ModelKind.Fighter => CanonicalFighterModel(item.Model, p.Faction),
                ModelKind.Bomber => CanonicalBomberModel(item.Model, p.Faction),
                _ => CanonicalTankModel(item.Model, p.Faction)
            };
            assign(p, model, amount); remaining -= amount; capacity -= item.Count;
        }
    }

    static void FillAggregateSplits(BattleResult r)
    {
        r.AttackerSoldiersKilled = r.AttackerParticipantLosses.Values.Sum(x => x.SoldiersKilled);
        r.AttackerSoldiersWounded = r.AttackerParticipantLosses.Values.Sum(x => x.SoldiersWounded);
        r.DefenderSoldiersKilled = r.DefenderParticipantLosses.Values.Sum(x => x.SoldiersKilled);
        r.DefenderSoldiersWounded = r.DefenderParticipantLosses.Values.Sum(x => x.SoldiersWounded);
    }

    static void Add(Dictionary<string, long> d, string model, long value)
    {
        if (value > 0) d[model] = d.GetValueOrDefault(model) + value;
    }

    static void Validate(BattleRequest request)
    {
        if (request.Attackers.Count == 0 || request.Defenders.Count == 0)
            throw new ArgumentException("Both sides are required.");
        ValidateOrders(request.AttackerOrders);
        ValidateOrders(request.DefenderOrders);
        foreach (var p in request.Attackers.Concat(request.Defenders))
            if (p.Soldiers < 0 || p.AntiAir < 0 || p.Money < 0 || p.Iron < 0 ||
                p.Tanks.Any(x => x.Count < 0) ||
                p.Fighters.Any(x => x.Count < 0) || p.Bombers.Any(x => x.Count < 0))
                throw new ArgumentException("Negative force count.");
        if (request.Attackers.Sum(x => x.Soldiers + x.Tanks.Sum(t => t.Count)) <= 0)
            throw new ArgumentException("Attacker ground force is required.");
    }

    static void ValidateOrders(BattleOrders orders)
    {
        if (orders.GroundStrategy is < 1 or > 2 || orders.GroundTactic is < 1 or > 2)
            throw new ArgumentException("Ground strategy and tactic must be 1 or 2.");
        if (orders.AirStrategy is < 0 or > 2 || orders.AirTactic is < 1 or > 2)
            throw new ArgumentException("Air strategy must be 0..2 and tactic 1..2.");
    }

    // ===================== خودآزمایی موتور نبرد =====================
    // اجرا: dotnet run -- selftest [تعداد seed]
    // بدون نیاز به تلگرام اجرا می‌شود: توزیع نتایج، صحت‌سنجی نامتغیرها و تعیین‌پذیری را چک می‌کند.
    public static void RunSelfTest(int seedsPerCase = 20)
    {
        Console.WriteLine("=== WarEngine خودآزمایی ===");
        Console.WriteLine($"پردازنده‌ها: {Environment.ProcessorCount} | seed در هر سناریو: {seedsPerCase}");
        Console.WriteLine();

        var scenarios = new (string Name, Func<BattleRequest> Build)[]
        {
            ("متوازن ۱:۱", () => Scenario(20000, 2000, 100, 0, 20000, 2000, 100, 200)),
            ("حمله ۲:۱", () => Scenario(40000, 4000, 200, 0, 20000, 2000, 100, 200)),
            ("حمله ۳:۱", () => Scenario(60000, 6000, 300, 0, 20000, 2000, 100, 200)),
            ("حمله ۵:۱", () => Scenario(100000, 10000, 500, 0, 20000, 2000, 100, 200)),
            ("حمله ضعیف ۱:۳", () => Scenario(10000, 1000, 50, 0, 30000, 3000, 150, 300)),
            ("زرهی رایش در دفاع", () => Scenario(40000, 4000, 200, 0, 20000, 2000, 100, 200,
                Faction.Reich, Faction.Reich)),
            ("برتری هوایی کامل", () => Scenario(40000, 4000, 800, 200, 20000, 2000, 100, 0)),
            ("بمباران راهبردی", () => Scenario(40000, 4000, 100, 300, 20000, 2000, 100, 300,
                Faction.USSR, Faction.USSR, false, 2)),
            ("دفاع از وطن (۲ شهر)", () => Scenario(40000, 4000, 200, 0, 20000, 2000, 100, 200,
                Faction.USSR, Faction.USSR, true)),
            ("نیروی تک‌گروهی", () => Scenario(90, 0, 0, 0, 90, 0, 0, 0)),
            ("بدون مدافع", () => Scenario(5000, 500, 0, 0, 0, 0, 0, 0)),
        };

        long totalMs = 0;
        Console.WriteLine($"{"سناریو",-26} {"برد",5} {"سنگین",5} {"بن‌بست",5} {"شکست",5} {"پیشروی",7} " +
                          $"{"تلفات مهاجم (ت/س)",15} {"تلفات مدافع (ت/س)",15} {"لوت",8} {"مدت",5}");
        for (int idx = 0; idx < scenarios.Length; idx++)
        {
            var (name, build) = scenarios[idx];
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var st = new CaseStats();
            for (int s = 0; s < seedsPerCase; s++)
            {
                ulong seed = 0x9E3779B97F4A7C15UL * (ulong)(idx + 1) + (ulong)(s + 1) + 0x1234567UL;
                var request = build();
                request.ScenarioSeed = seed;
                try
                {
                    var r = Resolve(request);
                    st.Add(r);
                    if (!VerifyInvariants(request, r)) st.InvariantsBroken++;
                }
                catch (Exception ex)
                {
                    st.Exceptions++;
                    Console.WriteLine($"  [EX] {name}: {ex.GetType().Name}: {ex.Message}");
                }
            }
            sw.Stop();
            totalMs += sw.ElapsedMilliseconds;
            Console.WriteLine(st.FormatLine(name));
        }

        // آزمون تعیین‌پذیری: همان درخواست باید روی نخ‌های مختلف نتیجه یکسان بدهد
        Console.WriteLine();
        Console.WriteLine("آزمون تعیین‌پذیری (یک درخواست روی ۸ نخ همزمان):");
        var baseRequest = Scenario(40000, 4000, 200, 0, 20000, 2000, 100, 200);
        baseRequest.ScenarioSeed = 0xC0FFEEUL;
        string baseKey = Summarize(Resolve(baseRequest));
        var threadResults = new string[8];
        Parallel.For(0, 8, i => threadResults[i] = Summarize(Resolve(baseRequest)));
        bool deterministic = threadResults.All(x => x == baseKey);
        Console.WriteLine(deterministic
            ? "  ✅ همه نخ‌ها نتیجه یکسان تولید کردند"
            : "  ❌ نتایج بین نخ‌ها ناهماهنگ است (باگ هم‌رشته‌ای!)");
        if (!deterministic)
            foreach (var t in threadResults) Console.WriteLine("  " + t);

        // آزمون صف اجرا
        Console.WriteLine();
        Console.WriteLine("آزمون صف اجرا (BattleExecutionScheduler):");
        try
        {
            var queued = Scenario(40000, 4000, 200, 0, 20000, 2000, 100, 200);
            queued.ScenarioSeed = 0x5EEDUL;
            var r = BattleExecutionScheduler.EnqueueAsync(queued).GetAwaiter().GetResult();
            Console.WriteLine($"  ✅ صف کار می‌کند — {r.OutcomeKind} در {r.DurationMinutes} دقیقه");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ صف اجرا خطا داد: {ex.GetType().Name}: {ex.Message}");
        }

        Console.WriteLine();
        Console.WriteLine($"مجموع زمان شبیه‌سازی: {totalMs}ms");
        Console.WriteLine("=== پایان خودآزمایی ===");
    }

    static BattleRequest Scenario(long atkSold, long atkTanks, long atkFight, long atkBomb,
        long defSold, long defTanks, long defFight, long defAA,
        Faction atkFaction = Faction.USSR, Faction defFaction = Faction.USSR,
        bool homeland = false, int atkAirStrategy = 1,
        long defMoney = 500000, long defIron = 200000)
    {
        static List<ModelAmount> Models(long count) => count > 0
            ? new List<ModelAmount> { new ModelAmount("", count) } : new List<ModelAmount>();
        return new BattleRequest
        {
            BattleId = 1,
            ChatId = -1001,
            ScenarioSeed = 0,
            Attackers = new List<BattleParticipant>
            {
                new()
                {
                    OwnerId = 10, CountryName = "مهاجم", OwnerName = "مهاجم",
                    Faction = atkFaction, Soldiers = atkSold,
                    Tanks = Models(atkTanks), Fighters = Models(atkFight), Bombers = Models(atkBomb)
                }
            },
            Defenders = new List<BattleParticipant>
            {
                new()
                {
                    OwnerId = 20, CountryName = "مدافع", OwnerName = "مدافع",
                    Faction = defFaction, Soldiers = defSold,
                    Tanks = Models(defTanks), Fighters = Models(defFight),
                    AntiAir = defAA, IsHomelandDefender = homeland,
                    Money = defMoney, Iron = defIron
                }
            },
            AttackerOrders = new BattleOrders { GroundStrategy = 1, GroundTactic = 1, AirStrategy = atkAirStrategy, AirTactic = 1 },
            DefenderOrders = new BattleOrders { GroundStrategy = 1, GroundTactic = 1, AirStrategy = 1, AirTactic = 1 }
        };
    }

    // نامتغیرهای حسابداری: تلفات تفکیک‌شده باید با تلفات کل و ظرفیت اعزام‌شده یکی باشد
    static bool VerifyInvariants(BattleRequest request, BattleResult r)
    {
        bool ok = true;
        ok &= r.AttackerTanksDestroyed + r.AttackerTanksDamaged == r.AttackerTanksLost;
        ok &= r.DefenderTanksDestroyed + r.DefenderTanksDamaged == r.DefenderTanksLost;
        ok &= r.AttackerSoldiersKilled + r.AttackerSoldiersWounded == r.AttackerSoldiersLost;
        ok &= r.DefenderSoldiersKilled + r.DefenderSoldiersWounded == r.DefenderSoldiersLost;
        ok &= r.AttackerParticipantLosses.Values.Sum(x => x.TanksUnavailable.Values.Sum()) == r.AttackerTanksLost;
        ok &= r.DefenderParticipantLosses.Values.Sum(x => x.TanksUnavailable.Values.Sum()) == r.DefenderTanksLost;
        ok &= r.AttackerParticipantLosses.Values.Sum(x => x.SoldiersUnavailable) == r.AttackerSoldiersLost;
        ok &= r.DefenderParticipantLosses.Values.Sum(x => x.SoldiersUnavailable) == r.DefenderSoldiersLost;
        ok &= r.AttackerTanksLost <= request.Attackers.Sum(p => p.Tanks.Sum(t => t.Count));
        ok &= r.DefenderTanksLost <= request.Defenders.Sum(p => p.Tanks.Sum(t => t.Count));
        ok &= r.AttackerSoldiersLost <= request.Attackers.Sum(p => p.Soldiers);
        ok &= r.DefenderSoldiersLost <= request.Defenders.Sum(p => p.Soldiers);
        ok &= r.AttackerMoneyGained == r.DefenderMoneyLost;
        ok &= r.AttackerMoneyGained <= request.Defenders.Sum(p => Math.Max(0, p.Money));
        return ok;
    }

    static string Summarize(BattleResult r) =>
        $"{r.OutcomeKind}|{r.SuccessPercent}|{r.EffectiveAdvanceKm:F2}|{r.AttackerTanksLost}|" +
        $"{r.AttackerSoldiersLost}|{r.DefenderTanksLost}|{r.DefenderSoldiersLost}|" +
        $"{r.AttackerFightersLost}|{r.DefenderFightersLost}|{r.DurationMinutes}|{r.AttackerMoneyGained}|" +
        $"{r.AirSuperiority:F3}|{r.AttackerParticipantLosses.Values.Sum(x => x.TanksUnavailable.Values.Sum())}";

    struct CaseStats
    {
        public int Total, Wins, Heavy, Stalemate, DefenderWins, Exceptions, InvariantsBroken;
        public long AtkTankLoss, AtkSoldLoss, DefTankLoss, DefSoldLoss, DurationMinutes, MoneyLoot;
        public double AdvanceKm;

        public void Add(BattleResult r)
        {
            Total++;
            if (r.AttackerHeavyVictory) Heavy++;
            if (r.AttackerWon) Wins++;
            if (r.OutcomeKind == BattleOutcomeKind.Stalemate) Stalemate++;
            if (!r.AttackerWon && !r.AttackerFailed) DefenderWins++;
            AtkTankLoss += r.AttackerTanksLost; AtkSoldLoss += r.AttackerSoldiersLost;
            DefTankLoss += r.DefenderTanksLost; DefSoldLoss += r.DefenderSoldiersLost;
            DurationMinutes += r.DurationMinutes;
            AdvanceKm += r.EffectiveAdvanceKm;
            MoneyLoot += r.AttackerMoneyGained;
        }

        public string FormatLine(string name)
        {
            int n = Math.Max(1, Total);
            return $"{name,-26} {Wins * 100 / n,4}٪ {Heavy * 100 / n,4}٪ {Stalemate * 100 / n,4}٪ " +
                   $"{DefenderWins * 100 / n,4}٪ {AdvanceKm / n,6:F1}km " +
                   $"{AtkTankLoss / n,6}/{AtkSoldLoss / n,7} " +
                   $"{DefTankLoss / n,6}/{DefSoldLoss / n,7} " +
                   $"{MoneyLoot / n,8:N0} {DurationMinutes / n,5}m" +
                   (Exceptions > 0 ? $" EX={Exceptions}" : "") +
                   (InvariantsBroken > 0 ? $" INV={InvariantsBroken}" : "");
        }
    }
}

static class BattleExecutionScheduler
{
    sealed class Job
    {
        public required BattleRequest Request { get; init; }
        public required TaskCompletionSource<BattleResult> Completion { get; init; }
        public required CancellationToken CancellationToken { get; init; }
    }

    static readonly Channel<Job> Queue = Channel.CreateUnbounded<Job>(new UnboundedChannelOptions
    {
        SingleWriter = false,
        SingleReader = false,
        AllowSynchronousContinuations = false
    });

    static BattleExecutionScheduler()
    {
        int automatic = Math.Max(1, Math.Min(4, Environment.ProcessorCount - 1));
        int workers = int.TryParse(Environment.GetEnvironmentVariable("ALICE_BATTLE_WORKERS"), out int configured)
            ? Math.Clamp(configured, 1, 4) : automatic;
        for (int i = 0; i < workers; i++) _ = Task.Run(WorkerLoop);
    }

    public static async Task<BattleResult> EnqueueAsync(BattleRequest request, CancellationToken ct = default)
    {
        var completion = new TaskCompletionSource<BattleResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        await Queue.Writer.WriteAsync(new Job
        {
            Request = request,
            Completion = completion,
            CancellationToken = ct
        }, ct);
        return await completion.Task.WaitAsync(ct);
    }

    static async Task WorkerLoop()
    {
        await foreach (var job in Queue.Reader.ReadAllAsync())
        {
            if (job.CancellationToken.IsCancellationRequested)
            {
                job.Completion.TrySetCanceled(job.CancellationToken);
                continue;
            }
            try { job.Completion.TrySetResult(WarEngine.Resolve(job.Request)); }
            catch (Exception ex) { job.Completion.TrySetException(ex); }
        }
    }
}
