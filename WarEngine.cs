// ============================================================================
//  WarEngine.cs  —  موتور نبرد ترکیبی نسخه ۲ (Combined-Arms Battle Engine v2)
// ============================================================================
//  فایل مستقل؛ کنار Program.cs قرار می‌گیرد. هیچ تغییری در امضای عمومی لازم نیست.
//
//  نقاط ورود:
//    WarEngine.RunBattle(attacker, defender, tanks, soldiers, strategy, tactic)           ← سازگاری عقب‌رو
//    WarEngine.RunBattle(attacker, defender, tanks, soldiers, fighters, bombers,
//                        strategy, tactic, airStrategy, airTactic)                         ← نبرد ترکیبی
//    WarEngine.RunBattlesParallel(orders)                                                  ← اجرای موازی انبوه
// ============================================================================

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

// ─────────────────────────────────────────────────────────────────────────────
//  خروجی نبرد
// ─────────────────────────────────────────────────────────────────────────────
class BattleResult
{
    public long AttackerTanksLost;
    public long AttackerSoldiersLost;
    public long AttackerFightersLost;
    public long AttackerBombersLost;
    public long AttackerMoneyGained;
    public long AttackerIronGained;
    public double AttackerWelfareChange;
    public long DefenderTanksLost;
    public long DefenderSoldiersLost;
    public long DefenderFightersLost;
    public long DefenderAntiAirLost;
    public long DefenderMoneyLost;
    public long DefenderIronLost;
    public double DefenderWelfareChange;
    public string AttackerReport = "";
    public string DefenderReport = "";
    public string GroupAnnouncement = "";
    public double PenetrationKm;
    public int SuccessPercent;
    public bool AttackerWon;
    public bool AttackerFailed;
    public int DurationMinutes;
    public double AirSuperiority;
    //  – naval losses
    public long AttackerBoatsLost;
    public long AttackerSubsLost;
    public long AttackerBattleshipsLost;
    public long AttackerBattleshipDamage; // damage inflicted to attacker battleships
    public long DefenderBoatsLost;
    public long DefenderSubsLost;
    public long DefenderBattleshipsLost;
    public long DefenderBattleshipDamage;
    public bool IsNavalBattle;
    public long AttackerBoatsSurvived;
    public long AttackerSubsSurvived;
    public long AttackerBattleshipsSurvived;
}

static class WarEngine
{
    // ───────────────────────────── ثوابت میدان ─────────────────────────────
    const float FRONT_KM = 40f;
    const float DEPTH_KM = 34f;
    const float WIN_DEPTH = 30f;
    const float FAIL_DEPTH = 3f;
    const int   GRID_W = 80, GRID_H = 68;
    const float CELL = 0.5f;
    const float TICK_MIN = 6f;
    const int   MAX_TICKS = 360;
    const int   AI_PERIOD = 4;
    const int   MAX_GROUPS = 224;
    const int   INF_GROUP = 100;
    const int   TANK_GROUP = 10;
    // انواع زمین
    const byte T_PLAIN = 0, T_HILL = 1, T_FOREST = 2, T_URBAN = 3, T_MARSH = 4, T_RIDGE = 5;
    static readonly float[] TerSpeed  = { 1.00f, 0.72f, 0.55f, 0.60f, 0.40f, 0.65f };
    static readonly float[] TerCover  = { 0.00f, 0.25f, 0.55f, 0.65f, 0.15f, 0.35f };
    static readonly float[] TerAcc    = { 1.00f, 0.90f, 0.70f, 0.65f, 0.95f, 0.92f };
    static readonly float[] TerVision = { 1.00f, 1.35f, 0.55f, 0.60f, 1.00f, 1.50f };
    // وضعیت‌های گروه
    const byte P_ADVANCE = 0, P_ASSAULT = 1, P_DEFEND = 2, P_AMBUSH = 3,
               P_PATROL = 4, P_RETREAT = 5, P_FLANK = 6, P_HOLD = 7;
    // ───────────────────────── آب‌وهوا و زمان ───────────────────────────────
    const byte W_CLEAR = 0, W_CLOUD = 1, W_RAIN = 2, W_FOG = 3, W_SNOW = 4;
    static readonly string[] WeatherName = { "آفتابی", "ابری", "بارانی", "مه‌آلود", "برفی" };
    static readonly float[] WxVision = { 1.00f, 0.92f, 0.78f, 0.50f, 0.70f };
    static readonly float[] WxAcc    = { 1.00f, 0.96f, 0.88f, 0.75f, 0.85f };
    static readonly float[] WxSpeed  = { 1.00f, 0.97f, 0.82f, 0.90f, 0.70f };
    static readonly float[] WxAir    = { 1.00f, 0.85f, 0.65f, 0.40f, 0.60f };
    const byte TM_DAWN = 0, TM_DAY = 1, TM_DUSK = 2, TM_NIGHT = 3;
    static readonly string[] TimeName = { "سپیده‌دم", "روز", "غروب", "شب" };
    static readonly float[] TimeVision = { 0.80f, 1.00f, 0.75f, 0.45f };
    static readonly float[] TimeAir    = { 0.85f, 1.00f, 0.80f, 0.50f };

    // ───────────────────────────── مشخصات تانک‌ها ───────────────────────────
    public readonly struct TankSpec
    {
        public readonly string Name;
        public readonly float Pen, He, Mg, Armor, Speed, CannonAmmo, MgAmmo, Reliab;
        public TankSpec(string n, float p, float he, float mg, float ar, float sp, float ca, float ma, float rel)
        { Name = n; Pen = p; He = he; Mg = mg; Armor = ar; Speed = sp; CannonAmmo = ca; MgAmmo = ma; Reliab = rel; }
    }
    static readonly TankSpec SpecUSA   = new("M2 Medium", 46f, 0.45f, 7f, 30f, 42f, 100f, 90f, 0.95f);
    static readonly TankSpec SpecUSSR  = new("T-28",      40f, 1.00f, 4f, 80f, 37f,  70f, 60f, 0.82f);
    static readonly TankSpec SpecReich = new("Panzer III",67f, 0.55f, 3f, 60f, 40f,  84f, 55f, 0.97f);
    static TankSpec SpecOf(Faction f) => f == Faction.USA ? SpecUSA : f == Faction.USSR ? SpecUSSR : SpecReich;

    // ───────────────────────────── مشخصات هواپیماها ─────────────────────────
    public readonly struct FighterSpec
    {
        public readonly string Name;
        public readonly float Maneuver, Firepower, Speed, Cas;
        public FighterSpec(string n, float mn, float fp, float sp, float cas)
        { Name = n; Maneuver = mn; Firepower = fp; Speed = sp; Cas = cas; }
    }
    static readonly FighterSpec FighterUSA   = new("P-36",   9f, 4.5f, 500f, 0.9f);
    static readonly FighterSpec FighterUSSR  = new("I-16",   9f, 4.0f, 520f, 0.8f);
    static readonly FighterSpec FighterReich = new("Bf 109", 8f, 8.0f, 570f, 1.0f);
    static FighterSpec FighterOf(Faction f) => f == Faction.USA ? FighterUSA : f == Faction.USSR ? FighterUSSR : FighterReich;

    public readonly struct BomberSpec
    {
        public readonly string Name;
        public readonly float Armor, DefMg, Bombload, Speed;
        public BomberSpec(string n, float ar, float dmg, float bl, float sp)
        { Name = n; Armor = ar; DefMg = dmg; Bombload = bl; Speed = sp; }
    }
    static readonly BomberSpec BomberUSA   = new("B-17",   8f, 6f, 3600f, 460f);
    static readonly BomberSpec BomberReich = new("He 111", 5f, 4f, 2000f, 435f);
    static readonly BomberSpec BomberUSSR  = new("DB-3",   3f, 3f, 1000f, 430f);
    static BomberSpec BomberOf(Faction f) => f == Faction.USA ? BomberUSA : f == Faction.USSR ? BomberUSSR : BomberReich;

    // ───────────────────────────── مشخصات دریایی –  ───────────────────────────
    public readonly struct BoatSpec
    {
        public readonly string Name;
        public readonly float Speed, Armor, Torpedo, Mg, Crew;
        public readonly float Power;
        public BoatSpec(string n, float speed, float armor, float torp, float mg, float crew, float power)
        { Name=n; Speed=speed; Armor=armor; Torpedo=torp; Mg=mg; Crew=crew; Power=power; }
    }
    static readonly BoatSpec BoatGermany = new("S-Boot", 39.5f, 5f, 18f, 4f, 22f, 12f);
    static readonly BoatSpec BoatUSA     = new("PT Boat", 42f, 3f, 14f, 6f, 12f, 10f);
    static readonly BoatSpec BoatUSSR    = new("G-5", 51f, 2f, 16f, 2f, 6f, 9f);
    static BoatSpec BoatOf(Faction f) => f==Faction.USA?BoatUSA: f==Faction.USSR?BoatUSSR:BoatGermany;

    public readonly struct SubSpec
    {
        public readonly string Name;
        public readonly float SurfSpeed, SubSpeed, Torpedo, Gun, Stealth, Armor;
        public readonly float Power;
        public SubSpec(string n, float surf, float sub, float torp, float gun, float stealth, float armor, float power)
        { Name=n; SurfSpeed=surf; SubSpeed=sub; Torpedo=torp; Gun=gun; Stealth=stealth; Armor=armor; Power=power; }
    }
    static readonly SubSpec SubGermany = new("Type VIIC", 17.7f, 7.6f, 35f, 8f, 85f, 18f, 28f);
    static readonly SubSpec SubUSA     = new("Gato", 21f, 9f, 45f, 7f, 80f, 15f, 32f);
    static readonly SubSpec SubUSSR    = new("S-class", 13.5f, 7.5f, 25f, 5f, 75f, 12f, 22f);
    static SubSpec SubOf(Faction f) => f==Faction.USA?SubUSA: f==Faction.USSR?SubUSSR:SubGermany;

    public readonly struct BattleshipSpec
    {
        public readonly string Name;
        public readonly float Speed, Belt, Deck, Turret, MainGuns, SecGuns, AAGuns, Crew, UnitsBuilt;
        public readonly float Power;
        public BattleshipSpec(string n, float speed, float belt, float deck, float turret, float mainGuns, float sec, float aa, float crew, float built, float power)
        { Name=n; Speed=speed; Belt=belt; Deck=deck; Turret=turret; MainGuns=mainGuns; SecGuns=sec; AAGuns=aa; Crew=crew; UnitsBuilt=built; Power=power; }
    }
    static readonly BattleshipSpec BSGermany = new("Bismarck", 30f, 320f, 110f, 360f, 8f, 12f, 44f, 2092f, 2f, 180f);
    static readonly BattleshipSpec BSUSA     = new("Iowa", 28f, 305f, 140f, 406f, 9f, 20f, 34f, 1800f, 6f, 195f);
    static readonly BattleshipSpec BSUSSR    = new("Sovetsky Soyuz", 23f, 225f, 62f, 203f, 12f, 16f, 18f, 1220f, 4f, 150f);
    static BattleshipSpec BattleshipOf(Faction f) => f==Faction.USA?BSUSA: f==Faction.USSR?BSUSSR:BSGermany;

    // Per-model mapping for naval
    public static BoatSpec GetBoatSpecByModel(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return BoatGermany;
        string m = modelName.ToLowerInvariant();
        if (m.Contains("pt") || m.Contains("usa") && m.Contains("boat")) return BoatUSA;
        if (m.Contains("g-5") || m.Contains("g5") || m.Contains("ussr") && m.Contains("boat")) return BoatUSSR;
        if (m.Contains("s-boot") || m.Contains("sboot") || m.Contains("e-boat")) return BoatGermany;
        // Fallback by keyword
        if (m.Contains("pt")) return BoatUSA;
        if (m.Contains("g-5") || m.Contains("g5")) return BoatUSSR;
        return BoatGermany;
    }
    public static SubSpec GetSubSpecByModel(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return SubGermany;
        string m = modelName.ToLowerInvariant();
        if (m.Contains("gato") || m.Contains("usa") && m.Contains("sub")) return SubUSA;
        if (m.Contains("s-class") || m.Contains("s class") || m.Contains("series ix") ) return SubUSSR;
        if (m.Contains("viic") || m.Contains("u-boat") || m.Contains("type")) return SubGermany;
        return SubGermany;
    }
    public static BattleshipSpec GetBattleshipSpecByModel(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return BSGermany;
        string m = modelName.ToLowerInvariant();
        if (m.Contains("iowa")) return BSUSA;
        if (m.Contains("sovetsky") || m.Contains("soyuz")) return BSUSSR;
        if (m.Contains("bismarck")) return BSGermany;
        return BSGermany;
    }

    static float FactionQuality(Faction f) => f switch
    {
        Faction.Reich => 1.08f,
        Faction.USA   => 1.03f,
        _             => 1.00f,
    };

    // ───────────────────────────── RNG سبک و قطعی ───────────────────────────
    struct XorRng
    {
        ulong s0, s1;
        public XorRng(ulong seed)
        {
            s0 = seed * 0x9E3779B97F4A7C15UL + 1; s1 = seed ^ 0xBF58476D1CE4E5B9UL;
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
        public int Next(int max) => (int)(NextU() % (uint)max);
    }

    // ───────────────────────────── ساختار گروه رزمی ─────────────────────────
    struct Group
    {
        public float X, Y;
        public float Units, Size0;
        public float CAmmo, MAmmo;
        public float Morale, Supp;
        public float Fatigue;
        public float Exp;
        public float TgtX, TgtY;
        public short FireTgt;
        public byte Type;
        public byte Posture;
        public byte Sector;
        public bool Alive;
        public bool Sprung;
        public float Signature;
    }
    struct Intel { public float Level, LastX, LastY, Stale; }
    struct Evt { public short Tick; public byte Kind; public float A, B; }

    const byte E_CONTACT = 0, E_AMBUSH = 1, E_BREAK5 = 2, E_BREAK10 = 3, E_BREAK20 = 4,
               E_BREAK30 = 5, E_AMMO = 6, E_ENCIRCLE = 7, E_ROUT = 8, E_DUEL = 9,
               E_SHIFT = 10, E_HALT = 11, E_SUPPLY = 12;

    // ───────────────────────── بافرهای ThreadStatic ─────────────────────────
    [ThreadStatic] static Group[] _atk;
    [ThreadStatic] static Group[] _def;
    [ThreadStatic] static Intel[] _intelA;
    [ThreadStatic] static Intel[] _intelD;
    [ThreadStatic] static byte[]  _terr;
    [ThreadStatic] static float[] _elev;
    [ThreadStatic] static float[] _threatA;
    [ThreadStatic] static float[] _threatD;
    [ThreadStatic] static Evt[]   _evts;
    [ThreadStatic] static StringBuilder _sb;

    static void EnsureBuffers()
    {
        _atk     ??= new Group[MAX_GROUPS];
        _def     ??= new Group[MAX_GROUPS];
        _intelA  ??= new Intel[MAX_GROUPS];
        _intelD  ??= new Intel[MAX_GROUPS];
        _terr    ??= new byte[GRID_W * GRID_H];
        _elev    ??= new float[GRID_W * GRID_H];
        _threatA ??= new float[10];
        _threatD ??= new float[10];
        _evts    ??= new Evt[96];
        _sb      ??= new StringBuilder(4096);
    }

    static long _seedCounter = Environment.TickCount;
    [ThreadStatic] static byte _weather;
    [ThreadStatic] static byte _startTime;

    static byte TimeAtTick(int tick)
    {
        int phase = (_startTime + (tick / 30)) & 3;
        return (byte)phase;
    }

    struct AirOutcome
    {
        public long AtkFightersLost, AtkBombersLost;
        public long DefFightersLost, DefAntiAirLost;
        public float Superiority;
        public float CasAtk, CasDef;
        public long StratMoney, StratIron;
        public float StratWelfare;
        public bool HadAirCombat, AtkHadAir, DefHadAir;
    }

    // ═════════════════════════════ API عمومی ════════════════════════════════
    public static BattleResult RunBattle(Country attacker, Country defender,
        long tanks, long soldiers, int strategy, int tactic)
        => RunBattle(attacker, defender, tanks, soldiers, 0, 0, strategy, tactic, 0, 0);

    public static BattleResult RunBattle(Country attacker, Country defender,
        long tanks, long soldiers, long fighters, long bombers,
        int strategy, int tactic, int airStrategy, int airTactic)
    {
        ulong seed = (ulong)Interlocked.Increment(ref _seedCounter)
                   ^ ((ulong)attacker.OwnerId << 20) ^ (ulong)DateTime.UtcNow.Ticks;
        return RunBattleSeeded(attacker, defender, tanks, soldiers, fighters, bombers,
                               strategy, tactic, airStrategy, airTactic, seed);
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

    // ─────────────────────────────  – per-model support ─────────────────────────
    public static TankSpec GetTankSpecByModel(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return SpecUSA;
        string m = modelName.ToLowerInvariant();
        if (m.Contains("t-28") || m.Contains("t34") || m.Contains("t-34") || m.Contains("t28")) return SpecUSSR;
        if (m.Contains("m2") || m.Contains("m4") || m.Contains("sherman")) return SpecUSA;
        if (m.Contains("panzer") || m.Contains("pz")) return SpecReich;
        // Fallback by faction keywords
        if (m.Contains("usa") || m.Contains("american")) return SpecUSA;
        if (m.Contains("ussr") || m.Contains("soviet") || m.Contains("russian")) return SpecUSSR;
        return SpecReich;
    }

    public static FighterSpec GetFighterSpecByModel(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return FighterUSA;
        string m = modelName.ToLowerInvariant();
        if (m.Contains("i-16") || m.Contains("i16") || m.Contains("yak")) return FighterUSSR;
        if (m.Contains("p-36") || m.Contains("p36") || m.Contains("p-51") || m.Contains("mustang")) return FighterUSA;
        if (m.Contains("bf") || m.Contains("109")) return FighterReich;
        return FighterReich;
    }

    public static BomberSpec GetBomberSpecByModel(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return BomberUSA;
        string m = modelName.ToLowerInvariant();
        if (m.Contains("db") || m.Contains("pe-2") || m.Contains("pe2")) return BomberUSSR;
        if (m.Contains("b-17") || m.Contains("b17")) return BomberUSA;
        if (m.Contains("he") || m.Contains("ju")) return BomberReich;
        return BomberUSA;
    }

    static TankSpec BlendTankSpecs(List<(string Model, long Count)> breakdown)
    {
        if (breakdown == null || breakdown.Count == 0) return SpecUSA;
        double total = breakdown.Sum(x => (double)x.Count);
        if (total <= 0) return SpecUSA;
        double pen = 0, he = 0, mg = 0, armor = 0, speed = 0, ca = 0, ma = 0, rel = 0;
        foreach (var (model, cnt) in breakdown)
        {
            var spec = GetTankSpecByModel(model);
            double w = cnt / total;
            pen += spec.Pen * w;
            he += spec.He * w;
            mg += spec.Mg * w;
            armor += spec.Armor * w;
            speed += spec.Speed * w;
            ca += spec.CannonAmmo * w;
            ma += spec.MgAmmo * w;
            rel += spec.Reliab * w;
        }
        // Even one unit affects – blend already includes small weight, but ensure minimum influence 2% if present
        // If any model has count 1 but total large, its influence is tiny – boost to 2% minimum if count>0
        foreach (var (model, cnt) in breakdown)
        {
            if (cnt > 0 && cnt / total < 0.02)
            {
                var spec = GetTankSpecByModel(model);
                pen = pen * 0.98 + spec.Pen * 0.02;
                he = he * 0.98 + spec.He * 0.02;
                // etc – ensure at least 2% influence for presence
            }
        }
        return new TankSpec($"Blended({breakdown.Count} models)", (float)pen, (float)he, (float)mg, (float)armor, (float)speed, (float)ca, (float)ma, (float)rel);
    }

    static FighterSpec BlendFighterSpecs(List<(string Model, long Count)> breakdown)
    {
        if (breakdown == null || breakdown.Count == 0) return FighterUSA;
        double total = breakdown.Sum(x => (double)x.Count);
        if (total <= 0) return FighterUSA;
        double man=0, fp=0, speed=0, cas=0;
        foreach (var (model, cnt) in breakdown)
        {
            var spec = GetFighterSpecByModel(model);
            double w = cnt / total;
            man += spec.Maneuver * w;
            fp += spec.Firepower * w;
            speed += spec.Speed * w;
            cas += spec.Cas * w;
        }
        return new FighterSpec($"Blended({breakdown.Count})", (float)man, (float)fp, (float)speed, (float)cas);
    }

    static BomberSpec BlendBomberSpecs(List<(string Model, long Count)> breakdown)
    {
        if (breakdown == null || breakdown.Count == 0) return BomberUSA;
        double total = breakdown.Sum(x => (double)x.Count);
        if (total <= 0) return BomberUSA;
        double armor=0, dmg=0, bomb=0, speed=0;
        foreach (var (model, cnt) in breakdown)
        {
            var spec = GetBomberSpecByModel(model);
            double w = cnt / total;
            armor += spec.Armor * w;
            dmg += spec.DefMg * w;
            bomb += spec.Bombload * w;
            speed += spec.Speed * w;
        }
        return new BomberSpec($"Blended({breakdown.Count})", (float)armor, (float)dmg, (float)bomb, (float)speed);
    }

    // New advanced battle with per-model breakdowns
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

    public static BattleResult RunBattleAdvancedSeeded(
        Country attacker, Country defender,
        List<(string Model, long Count)> attTankBreakdown, long attSoldiers,
        List<(string Model, long Count)> attFighterBreakdown, List<(string Model, long Count)> attBomberBreakdown,
        List<(string Model, long Count)> defTankBreakdown, long defSoldiers,
        List<(string Model, long Count)> defFighterBreakdown,
        int strategy, int tactic, int airStrategy, int airTactic, ulong seed)
    {
        // Calculate totals from breakdowns
        long attTanks = attTankBreakdown?.Sum(x => x.Count) ?? 0;
        long attFighters = attFighterBreakdown?.Sum(x => x.Count) ?? 0;
        long attBombers = attBomberBreakdown?.Sum(x => x.Count) ?? 0;
        long defTanks = defTankBreakdown?.Sum(x => x.Count) ?? 0;
        long defFighters = defFighterBreakdown?.Sum(x => x.Count) ?? 0;

        EnsureBuffers();
        var rng = new XorRng(seed);
        var res = new BattleResult();

        long aTanks = Math.Max(0, Math.Min(attTanks, attacker.Tanks));
        long aSold = Math.Max(0, Math.Min(attSoldiers, attacker.Soldiers));
        long aFight = Math.Max(0, Math.Min(attFighters, attacker.Planes));
        long aBomb = Math.Max(0, Math.Min(attBombers, attacker.Bombers));
        long dTanks = defTanks > 0 ? defTanks : Math.Min(defender.Tanks, Math.Max(defender.DefenseTanks, (long)Math.Ceiling(defender.Tanks * 0.2)));
        long dSold = defSoldiers > 0 ? defSoldiers : Math.Min(defender.Soldiers, Math.Max(defender.DefenseSoldiers, (long)Math.Ceiling(defender.Soldiers * 0.2)));
        long dFight = defFighters > 0 ? defFighters : Math.Min(defender.Planes, defender.DefenseFighters);
        long dAA = defender.AntiAir;

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

        // Blend specs based on actual models present –  core
        var aTankSpec = attTankBreakdown != null && attTankBreakdown.Count > 0 ? BlendTankSpecs(attTankBreakdown) : SpecOf(attacker.Faction);
        var dTankSpec = defTankBreakdown != null && defTankBreakdown.Count > 0 ? BlendTankSpecs(defTankBreakdown) : SpecOf(defender.Faction);
        var aFighterSpec = attFighterBreakdown != null && attFighterBreakdown.Count > 0 ? BlendFighterSpecs(attFighterBreakdown) : FighterOf(attacker.Faction);
        var dFighterSpec = defFighterBreakdown != null && defFighterBreakdown.Count > 0 ? BlendFighterSpecs(defFighterBreakdown) : FighterOf(defender.Faction);
        var aBomberSpec = attBomberBreakdown != null && attBomberBreakdown.Count > 0 ? BlendBomberSpecs(attBomberBreakdown) : BomberOf(attacker.Faction);

        _weather = PickWeather(ref rng);
        _startTime = (byte)rng.Next(4);

        // New advanced strategy algorithm – replaces simple RPS
        float counterAtk = CalculateAdvancedStrategyAdvantage(aStrat, aTac, dStrat, dTac, aTankSpec, dTankSpec, aTanks, dTanks, aSold, dSold, ref rng);

        AirOutcome air = RunAirPhaseAdvanced(attacker, defender, aFight, aBomb, aAirStrat, aAirTac,
                                     dFight, dAA, dStrat, dTac, dAirStrat, dAirTac,
                                     aFighterSpec, aBomberSpec, dFighterSpec, ref rng);

        res.AttackerFightersLost = air.AtkFightersLost;
        res.AttackerBombersLost = air.AtkBombersLost;
        res.DefenderFightersLost = air.DefFightersLost;
        res.DefenderAntiAirLost = air.DefAntiAirLost;
        res.AirSuperiority = Math.Round(air.Superiority, 2);

        long aTankLoss = 0, aSoldLoss = 0, dTankLoss = 0, dSoldLoss = 0;
        float effDepth = 0f, maxDepth = 0f;
        int tick = 0, evtN = 0;
        bool contact = false, ambushFired = false, encircled = false;
        int duelPeakTick = -1;
        float aIntelQ = 0f, dIntelQ = 0f;
        bool supplyStrain = false;
        bool defHasGround = (dTanks + dSold) > 0;

        if (!anyGround)
        {
            effDepth = 0f; maxDepth = 0f; tick = 30;
        }
        else if (!defHasGround)
        {
            GenTerrain(ref rng);
            int nA0 = BuildSide(_atk, true, aTanks, aSold, aStrat, aTac, ref rng);
            float airDrag = air.Superiority < -0.15f ? Math.Clamp(-air.Superiority, 0f, 1f) * 0.25f : 0f;
            float friction = 1f - airDrag;
            effDepth = WIN_DEPTH * friction; maxDepth = effDepth;
            CountLosses(_atk, nA0, ref aTankLoss, ref aSoldLoss);
            aTankLoss = (long)(aTankLoss * 0.02f);
            aSoldLoss = (long)(aSoldLoss * 0.02f);
            tick = 60;
        }
        else
        {
            GenTerrain(ref rng);
            int nA = BuildSideAdvanced(_atk, true, attTankBreakdown, aSold, aStrat, aTac, ref rng);
            int nD = BuildSideAdvanced(_def, false, defTankBreakdown, dSold, dStrat, dTac, ref rng);
            for (int i = 0; i < nD; i++) { _intelA[i] = default; _intelA[i].Stale = 9999f; }
            for (int i = 0; i < nA; i++) { _intelD[i] = default; _intelD[i].Stale = 9999f; }

            float aQual = FactionQuality(attacker.Faction);
            float dQual = FactionQuality(defender.Faction);
            float aPow0 = SidePowerAdvanced(_atk, nA, attTankBreakdown);
            float dPow0 = SidePowerAdvanced(_def, nD, defTankBreakdown);
            float prevMomentum = 0f; int haltTicks = 0; float duelPeak = 0f;
            float casA = air.CasAtk, casD = air.CasDef;

            if (air.Superiority > 0.05f)
                casA *= 1f + Math.Clamp(air.Superiority, 0f, 1f) * 0.45f;
            else if (air.Superiority < -0.05f)
                casD *= 1f + Math.Clamp(-air.Superiority, 0f, 1f) * 0.45f;

            if (air.Superiority > 0.25f) casD *= 1f - Math.Clamp(air.Superiority - 0.25f, 0f, 0.5f) * 0.4f;
            else if (air.Superiority < -0.25f) casA *= 1f - Math.Clamp(-air.Superiority - 0.25f, 0f, 0.5f) * 0.4f;

            if (defender.Cities <= 0) casD *= 1.5f;

            for (tick = 0; tick < MAX_TICKS; tick++)
            {
                byte tnow = TimeAtTick(tick);
                float visEnv = WxVision[_weather] * TimeVision[tnow];
                float accEnv = WxAcc[_weather];
                aIntelQ = SenseSide(_atk, nA, _def, nD, _intelA, aTac == 2 && aStrat == 1, visEnv, ref rng);
                dIntelQ = SenseSide(_def, nD, _atk, nA, _intelD, dStrat == 2, visEnv, ref rng);

                if (tick % AI_PERIOD == 0)
                {
                    BuildThreatMap(_def, nD, _intelA, nD, _threatA, dTankSpec);
                    BuildThreatMap(_atk, nA, _intelD, nA, _threatD, aTankSpec);
                    CommandAttacker(nA, nD, aStrat, aTac, effDepth, aIntelQ, ref rng, ref encircled, tick);
                    CommandDefender(nD, nA, dStrat, dTac, effDepth, dIntelQ, ref rng);
                }

                MoveSideAdvanced(_atk, nA, attTankBreakdown, true, ref rng);
                MoveSideAdvanced(_def, nD, defTankBreakdown, false, ref rng);

                float supplyA = SupplyFactor(effDepth);
                if (!supplyStrain && supplyA < 0.8f) { supplyStrain = true; AddEvt(ref evtN, tick, E_SUPPLY, effDepth); }

                float aDuel = FireSideAdvanced(_atk, nA, attTankBreakdown, _def, nD, defTankBreakdown, _intelA, _intelD, true, aStrat, aTac, dStrat, encircled, casA * counterAtk * aQual * supplyA, accEnv, ref rng, ref evtN, tick, ref contact, ref ambushFired);
                float dDuel = FireSideAdvanced(_def, nD, defTankBreakdown, _atk, nA, attTankBreakdown, _intelD, _intelA, false, dStrat, dTac, aStrat, false, casD * dQual, accEnv, ref rng, ref evtN, tick, ref contact, ref ambushFired);

                float duel = aDuel + dDuel;
                if (duel > duelPeak) { duelPeak = duel; duelPeakTick = tick; }

                MoraleSide(_atk, nA, true, ref rng, ref evtN, tick);
                MoraleSide(_def, nD, false, ref rng, ref evtN, tick);

                float d = EffectiveDepth(_atk, nA);
                if (d > effDepth)
                {
                    float prev = effDepth; effDepth = d;
                    if (prev < 5f && d >= 5f) AddEvt(ref evtN, tick, E_BREAK5, d);
                    if (prev < 10f && d >= 10f) AddEvt(ref evtN, tick, E_BREAK10, d);
                    if (prev < 20f && d >= 20f) AddEvt(ref evtN, tick, E_BREAK20, d);
                    if (prev < 30f && d >= 30f) AddEvt(ref evtN, tick, E_BREAK30, d);
                    haltTicks = 0;
                }
                else haltTicks++;

                if (d > maxDepth) maxDepth = d;

                float aPow = SidePowerAdvanced(_atk, nA, attTankBreakdown);
                float dPow = SidePowerAdvanced(_def, nD, defTankBreakdown);
                float momentum = (aPow / Math.Max(1f, aPow0)) - (dPow / Math.Max(1f, dPow0));
                if (tick > 20 && prevMomentum >= 0 && momentum < -0.12f) AddEvt(ref evtN, tick, E_SHIFT, 0);
                prevMomentum = momentum;

                if (effDepth >= WIN_DEPTH) { tick++; break; }
                if (aPow < aPow0 * 0.13f) { tick++; break; }
                if (dPow < dPow0 * 0.10f && effDepth > 6f)
                {
                    effDepth = Math.Min(WIN_DEPTH, effDepth + (WIN_DEPTH - effDepth) * 0.7f);
                    AddEvt(ref evtN, tick, E_ROUT, 1); tick++; break;
                }
                if (haltTicks > 90 && contact) { AddEvt(ref evtN, tick, E_HALT, effDepth); tick++; break; }
            }

            CountLosses(_atk, nA, ref aTankLoss, ref aSoldLoss);
            CountLosses(_def, nD, ref dTankLoss, ref dSoldLoss);

            if (effDepth >= 22f && effDepth < WIN_DEPTH)
                effDepth = Math.Min(WIN_DEPTH, effDepth + (WIN_DEPTH - effDepth) * 0.5f);
            else if (effDepth <= 6f && effDepth > FAIL_DEPTH)
                effDepth = Math.Max(0f, effDepth - (effDepth - FAIL_DEPTH) * 0.5f);
        }

        aTankLoss = Math.Min(aTankLoss, aTanks); aSoldLoss = Math.Min(aSoldLoss, aSold);
        dTankLoss = Math.Min(dTankLoss, dTanks); dSoldLoss = Math.Min(dSoldLoss, dSold);

        float frac = Math.Clamp((effDepth - FAIL_DEPTH) / (WIN_DEPTH - FAIL_DEPTH), 0f, 1f);
        int success = (int)Math.Round(frac * 100);
        bool absWin = effDepth >= WIN_DEPTH;
        bool absFail = anyGround && effDepth < FAIL_DEPTH;

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
        res.AttackerFailed = absFail;
        res.DurationMinutes = Math.Max(30, (int)(tick * TICK_MIN));

        double aLossR = (aTanks + aSold) > 0 ? (aTankLoss * 10.0 + aSoldLoss) / Math.Max(1.0, aTanks * 10.0 + aSold) : 0;
        double dLossR = (dTanks + dSold) > 0 ? (dTankLoss * 10.0 + dSoldLoss) / Math.Max(1.0, dTanks * 10.0 + dSold) : 0;

        res.AttackerWelfareChange = -Math.Clamp(aLossR * 2.0 + (res.AttackerFailed ? 1.0 : 0), 0, 3);
        res.DefenderWelfareChange = -Math.Clamp(dLossR * 2.0 + (absWin ? 1.5 : 0) + frac * 0.8 + air.StratWelfare * 0.3, 0, 4);

        BuildReportsAdvanced(res, attacker, defender,
            aTankSpec, dTankSpec, aFighterSpec, dFighterSpec, aBomberSpec,
            aStrat, aTac, dStrat, dTac,
            aTanks, aSold, dTanks, dSold, aFight, aBomb, dFight, dAA,
            aAirStrat, aAirTac, dAirStrat, dAirTac, air,
            attTankBreakdown, defTankBreakdown, attFighterBreakdown, defFighterBreakdown, attBomberBreakdown,
            evtN, duelPeakTick, encircled, ambushFired, aIntelQ, dIntelQ, effDepth, frac,
            anyGround, defHasGround, supplyStrain, counterAtk);

        SaveBattle(attacker, defender, res);
        return res;
    }

    static float CalculateAdvancedStrategyAdvantage(int aStrat, int aTac, int dStrat, int dTac, TankSpec aSpec, TankSpec dSpec, long aTanks, long dTanks, long aSold, long dSold, ref XorRng rng)
    {
        // Replaces simple RPS with dynamic algorithm based on strategy, tactic, force composition, and tank specs
        float baseAdv = 1.0f;

        // Force ratio influence
        double totalA = aTanks * 10 + aSold;
        double totalD = dTanks * 10 + dSold;
        double ratio = totalA / Math.Max(1.0, totalD);

        // Strategy interaction – not RPS, but weighted with context
        if (aStrat == 1) // coherent assault
        {
            if (dStrat == 1) // vs coherent defense – frontal clash, armor and pen matter
            {
                float armorDiff = (aSpec.Armor - dSpec.Armor) / 100f;
                float penDiff = (aSpec.Pen - dSpec.Armor) / 100f;
                baseAdv += (armorDiff + penDiff) * 0.15f;
                if (aTac == 1) baseAdv += 0.05f; // direct assault benefits from coherent
                else baseAdv += 0.08f + (float)(ratio - 1.0) * 0.05f; // probing + concentrated benefits from superiority
            }
            else // vs dispersed defense – encirclement would be better, so coherent suffers slightly
            {
                baseAdv -= 0.08f;
                if (dTac == 1) baseAdv += 0.02f; // ambush vs coherent assault – defender gets small bonus, so attacker slightly worse
                else baseAdv -= 0.03f; // tactical retreat + traps is worse for coherent assault
                // But if attacker has speed advantage, mitigates
                if (aSpec.Speed > dSpec.Speed) baseAdv += (aSpec.Speed - dSpec.Speed) / 200f;
            }
        }
        else // encirclement (aStrat==2)
        {
            if (dStrat == 1) // vs coherent – encirclement excels
            {
                baseAdv += 0.12f;
                if (aTac == 2) baseAdv += 0.06f; // moving encirclement
                // Speed matters more for encirclement
                baseAdv += (aSpec.Speed - 35f) / 300f;
            }
            else // vs dispersed – both dispersed, more even, but tactic matters
            {
                baseAdv += 0.02f;
                if (aTac == 2 && dTac == 2) baseAdv += 0.01f; // both mobile traps
                if (aTac == 1 && dTac == 1) baseAdv -= 0.02f; // static encirclement vs ambush
            }
        }

        // Force ratio contributes but not linearly (diminishing returns)
        baseAdv += (float)Math.Clamp((ratio - 1.0) * 0.08, -0.12, 0.12);

        // Small random factor for unpredictability (strategy execution)
        baseAdv += rng.Range(-0.04f, 0.04f);

        return Math.Clamp(baseAdv, 0.75f, 1.35f);
    }

    static AirOutcome RunAirPhaseAdvanced(Country atk, Country def,
        long aFight, long aBomb, int aAirStrat, int aAirTac,
        long dFight, long dAA, int dStrat, int dTac, int dAirStrat, int dAirTac,
        FighterSpec aFs, BomberSpec aBs, FighterSpec dFs, ref XorRng rng)
    {
        var o = new AirOutcome { CasAtk = 1f, CasDef = 1f };
        o.AtkHadAir = (aFight + aBomb) > 0;
        o.DefHadAir = (dFight + dAA) > 0;
        if (!o.AtkHadAir && !o.DefHadAir) { o.Superiority = 0f; return o; }

        float wxAir = WxAir[_weather];
        float aFighterQ = (aFs.Maneuver * 0.55f + aFs.Firepower * 0.45f) * FactionQuality(atk.Faction);
        float dFighterQ = (dFs.Maneuver * 0.55f + dFs.Firepower * 0.45f) * FactionQuality(def.Faction);
        float capBonus = (dAirStrat == 1 && dAirTac == 1) ? 1.25f : 1f;
        float flakBonus = (dAirStrat == 2 && dAirTac == 1) ? 1.35f : 1f;
        if (dAirStrat == 2 && dAirTac == 2) capBonus *= 1.1f;

        float aFighterPow = aFight * aFighterQ * wxAir * rng.Range(0.9f, 1.1f);
        float dFighterPow = dFight * dFighterQ * capBonus * rng.Range(0.9f, 1.1f);

        long aFightLost = 0, dFightLost = 0;
        if (aFight > 0 && dFight > 0)
        {
            o.HadAirCombat = true;
            float total = aFighterPow + dFighterPow;
            float aLossFrac = Math.Clamp(dFighterPow / Math.Max(1f, total) * rng.Range(0.7f, 1.1f), 0f, 0.95f);
            float dLossFrac = Math.Clamp(aFighterPow / Math.Max(1f, total) * rng.Range(0.7f, 1.1f), 0f, 0.95f);
            aFightLost = (long)Math.Round(aFight * aLossFrac);
            dFightLost = (long)Math.Round(dFight * dLossFrac);
        }
        long aBombLost = 0, dAALost = 0;
        if (aBomb > 0 && dFight > 0 && aFight == 0)
        {
            o.HadAirCombat = true;
            float interceptPower = dFight * dFighterQ * capBonus * rng.Range(0.8f, 1.1f);
            long intercepted = (long)Math.Round(Math.Min(aBomb, interceptPower * 0.015f / (1f + aBs.Armor * 0.3f)));
            aBombLost += Math.Min(aBomb, intercepted);
        }

        long aFightLeft = aFight - aFightLost;
        long dFightLeft = dFight - dFightLost;

        if (dAA > 0 && (aFightLeft > 0 || aBomb > 0))
        {
            float aaPower = dAA * flakBonus * rng.Range(0.85f, 1.15f);
            float bomberResist = 1f / (1f + aBs.Armor * 0.25f);
            long bombHit = (long)Math.Round(Math.Min(Math.Max(0, aBomb - aBombLost), aaPower * 0.015f * bomberResist));
            aBombLost = Math.Min(aBomb, aBombLost + bombHit);

            long fightHit = (long)Math.Round(Math.Min(aFightLeft, aaPower * 0.02f));
            aFightLost += fightHit; aFightLeft -= fightHit;

            float incoming = aFightLeft + (aBomb - aBombLost) * 1.3f;
            dAALost = (long)Math.Round(Math.Min(dAA, incoming * rng.Range(0.03f, 0.07f)));
        }

        long aBombLeft = aBomb - aBombLost;
        float atkAirRemain = aFightLeft * aFighterQ + aBombLeft * 1.0f;
        float defAirRemain = dFightLeft * dFighterQ + dAA * 0.5f;
        float sup = (atkAirRemain - defAirRemain) / Math.Max(1f, atkAirRemain + defAirRemain);
        o.Superiority = Math.Clamp(sup, -1f, 1f);

        if (aAirStrat == 1)
        {
            if (aAirTac == 2 && aBombLeft > 0 && dFightLeft > 0)
            {
                float raidIntensity = aBombLeft * (aBs.Bombload / 3600f) * wxAir * (0.5f + 0.5f * Math.Clamp(o.Superiority + 0.5f, 0f, 1f));
                long grounded = (long)Math.Round(Math.Min(dFightLeft, raidIntensity * rng.Range(0.6f, 1.0f)));
                if (grounded > 0) { dFightLost += grounded; dFightLeft -= grounded; }
            }
            float casPower = (aFightLeft * aFs.Cas + aBombLeft * 1.5f) * wxAir;
            o.CasAtk = 1f + Math.Clamp(casPower / Math.Max(50f, (atk.Soldiers + 1) * 0.02f), 0f, 0.6f);
            if (o.Superiority < -0.1f)
                o.CasDef = 1f + Math.Clamp(dFightLeft * dFs.Cas / Math.Max(50f, (def.Soldiers + 1) * 0.02f), 0f, 0.4f);
        }
        else if (aAirStrat == 2)
        {
            float effBomb = aBombLeft * (0.55f + 0.45f * Math.Clamp(o.Superiority + 0.5f, 0f, 1f)) * wxAir;
            float perBomberDamage = aBs.Bombload / 3600f;
            float intensity = effBomb * perBomberDamage;
            float moneyFrac = Math.Clamp(intensity * 0.02f, 0f, aAirTac == 1 ? 0.35f : 0.30f);
            float ironFrac = Math.Clamp(intensity * 0.02f, 0f, aAirTac == 1 ? 0.40f : 0.18f);
            if (aAirTac == 1)
            {
                o.StratMoney = (long)(def.Money * moneyFrac * 0.9f);
                o.StratIron = (long)(def.Iron * ironFrac);
                o.StratWelfare = Math.Clamp(effBomb * 0.02f, 0f, 4f);
            }
            else
            {
                o.StratMoney = (long)(def.Money * moneyFrac);
                o.StratIron = (long)(def.Iron * ironFrac * 0.5f);
                o.StratWelfare = Math.Clamp(effBomb * 0.02f, 0f, 2f);
            }
            o.CasAtk = 1f + Math.Clamp(aFightLeft * aFs.Cas / Math.Max(80f, (atk.Soldiers + 1) * 0.03f), 0f, 0.3f);
        }

        o.AtkFightersLost = Math.Min(aFight, Math.Max(0, aFightLost));
        o.AtkBombersLost = Math.Min(aBomb, Math.Max(0, aBombLost));
        o.DefFightersLost = Math.Min(dFight, Math.Max(0, dFightLost));
        o.DefAntiAirLost = Math.Min(dAA, Math.Max(0, dAALost));

        return o;
    }

    static int BuildSideAdvanced(Group[] g, bool atk, List<(string Model, long Count)> tankBreakdown, long soldiers, int strat, int tac, ref XorRng rng)
    {
        // Build side considering per-model tank breakdown – each model creates its own groups with its own spec influence
        long totalTanks = tankBreakdown?.Sum(x => x.Count) ?? 0;
        long rawGroups = totalTanks / TANK_GROUP + soldiers / INF_GROUP + 2;
        float scale = rawGroups > MAX_GROUPS ? (float)rawGroups / MAX_GROUPS : 1f;
        float tankGrp = TANK_GROUP * scale, infGrp = INF_GROUP * scale;
        int n = 0;

        if (tankBreakdown != null)
        {
            foreach (var (model, cnt) in tankBreakdown)
            {
                long tLeft = cnt;
                while (tLeft > 0 && n < MAX_GROUPS)
                {
                    float u = (float)Math.Min(tLeft, (long)Math.Ceiling(tankGrp));
                    InitGroup(ref g[n], atk, 1, u, strat, tac, ref rng);
                    // Store model index in Sector extension? Use Signature to encode spec influence
                    // For simplicity, we keep group but spec blending already done
                    tLeft -= (long)u; n++;
                }
            }
        }

        long sLeft = soldiers;
        while (sLeft > 0 && n < MAX_GROUPS)
        {
            float u = (float)Math.Min(sLeft, (long)Math.Ceiling(infGrp));
            InitGroup(ref g[n], atk, 0, u, strat, tac, ref rng);
            sLeft -= (long)u; n++;
        }
        return n;
    }

    static float SidePowerAdvanced(Group[] g, int n, List<(string Model, long Count)> tankBreakdown)
    {
        // If breakdown exists, use blended spec for power, but also add small bonus for diversity
        TankSpec blended = tankBreakdown != null && tankBreakdown.Count > 0 ? BlendTankSpecs(tankBreakdown) : SpecUSA;
        float basePower = SidePower(g, n, blended);
        // Diversity bonus: having multiple models gives slight adaptability bonus
        if (tankBreakdown != null && tankBreakdown.Count > 1)
        {
            basePower *= 1f + Math.Min(0.08f, (tankBreakdown.Count - 1) * 0.025f);
        }
        return basePower;
    }

    static void MoveSideAdvanced(Group[] g, int n, List<(string Model, long Count)> tankBreakdown, bool atk, ref XorRng rng)
    {
        TankSpec blended = tankBreakdown != null && tankBreakdown.Count > 0 ? BlendTankSpecs(tankBreakdown) : SpecUSA;
        MoveSide(g, n, blended, atk, ref rng);
    }

    static float FireSideAdvanced(Group[] own, int nOwn, List<(string Model, long Count)> ownTankBreakdown,
        Group[] foe, int nFoe, List<(string Model, long Count)> foeTankBreakdown,
        Intel[] ownIntel, Intel[] foeIntel, bool atk, int strat, int tac, int foeStrat, bool encircled,
        float combatMul, float accEnv, ref XorRng rng, ref int evtN, int tick, ref bool contact, ref bool ambushFired)
    {
        TankSpec ownSpec = ownTankBreakdown != null && ownTankBreakdown.Count > 0 ? BlendTankSpecs(ownTankBreakdown) : SpecUSA;
        TankSpec foeSpec = foeTankBreakdown != null && foeTankBreakdown.Count > 0 ? BlendTankSpecs(foeTankBreakdown) : SpecUSA;
        return FireSide(own, nOwn, ownSpec, foe, nFoe, foeSpec, ownIntel, foeIntel, atk, strat, tac, foeStrat, encircled, combatMul, accEnv, ref rng, ref evtN, tick, ref contact, ref ambushFired);
    }

    static void BuildReportsAdvanced(
        BattleResult r, Country atk, Country def,
        TankSpec aSpec, TankSpec dSpec, FighterSpec aFs, FighterSpec dFs, BomberSpec aBs,
        int aStrat, int aTac, int dStrat, int dTac,
        long aTanks, long aSold, long dTanks, long dSold, long aFight, long aBomb, long dFight, long dAA,
        int aAirStrat, int aAirTac, int dAirStrat, int dAirTac, AirOutcome air,
        List<(string Model, long Count)> attTankBreakdown, List<(string Model, long Count)> defTankBreakdown,
        List<(string Model, long Count)> attFighterBreakdown, List<(string Model, long Count)> defFighterBreakdown,
        List<(string Model, long Count)> attBomberBreakdown,
        int evtN, int duelPeakTick, bool encircled, bool ambushFired,
        float aIntelQ, float dIntelQ, float depth, float frac,
        bool anyGround, bool defHasGround, bool supplyStrain, float counterAtk)
    {
        var sb = _sb; sb.Clear();
        string aStratName = aStrat == 1 ? "هجوم منسجم" : "محاصره و ضربه";
        string aTacName = aStrat == 1 ? (aTac == 1 ? "حمله مستقیم متمرکز" : "حملات سبک اکتشافی و یورش اصلی") : (aTac == 1 ? "محاصره گسترده و فرسایش" : "حلقه محاصره متحرک");
        string dStratName = dStrat == 1 ? "دفاع منسجم" : "دفاع و ضدحمله پراکنده";
        string dTacName = dStrat == 1 ? (dTac == 1 ? "دفاع ثابت در سنگرها" : "گشت متحرک ترکیبی") : (dTac == 1 ? "استتار و کمین" : "عقب‌نشینی تاکتیکی و تله");

        // Dynamic composition description
        var compDesc = new StringBuilder();
        if (attTankBreakdown != null && attTankBreakdown.Count > 0)
        {
            compDesc.Append("ترکیب زرهی مهاجم: ");
            foreach (var (model, cnt) in attTankBreakdown)
                compDesc.Append($"{model}({cnt}) ");
            compDesc.Append(" | ");
        }
        if (defTankBreakdown != null && defTankBreakdown.Count > 0)
        {
            compDesc.Append("مدافع: ");
            foreach (var (model, cnt) in defTankBreakdown)
                compDesc.Append($"{model}({cnt}) ");
        }

        string outcome;
        if (!anyGround)
        {
            outcome = air.Superiority > 0.12 ? $"🛫 عملیات هوایی موفق {atk.Name}" : air.Superiority < -0.12 ? $"🛫 برتری با {def.Name}" : "🛫 بی‌نتیجه";
        }
        else if (r.AttackerWon) outcome = $"🏆 پیروزی مطلق {atk.Name}";
        else if (r.AttackerFailed) outcome = $"🛡 دفاع کامل {def.Name}";
        else outcome = $"⚖️ موفقیت {r.SuccessPercent}٪ مهاجم";

        int h = r.DurationMinutes / 60, m = r.DurationMinutes % 60;
        string envLine = $"🌦 {WeatherName[_weather]} | 🕓 {TimeName[_startTime]} | 📐 عمق نفوذ: {depth:F1}km";
        string bar = $"{ProgressBar(frac, r.AttackerWon ? 1 : r.AttackerFailed ? 2 : 0)} {r.SuccessPercent}%";

        // Dynamic report
        sb.AppendLine($"⚔️ گزارش تاکتیکی – {atk.Name} vs {def.Name}");
        sb.AppendLine(outcome);
        sb.AppendLine(envLine);
        sb.AppendLine($"📊 پیشروی: {bar}");
        if (compDesc.Length > 0) sb.AppendLine($"🔧 ترکیب: {compDesc}");
        sb.AppendLine();
        sb.AppendLine("📜 تحلیل نبرد (هوشمند):");

        // Strategy analysis – replaces RPS
        sb.AppendLine($"• استراتژی مهاجم: {aStratName} / {aTacName} – مزیت تاکتیکی: {counterAtk:F2}x");
        sb.AppendLine($"• استراتژی مدافع: {dStratName} / {dTacName}");
        sb.AppendLine($"• کیفیت زرهی مهاجم: {aSpec.Name} (نفوذ {aSpec.Pen}، زره {aSpec.Armor}) vs مدافع {dSpec.Name}");
        if (attTankBreakdown != null && attTankBreakdown.Count > 1) sb.AppendLine($"• تنوع زرهی مهاجم ({attTankBreakdown.Count} مدل) باعث انعطاف‌پذیری +{(attTankBreakdown.Count - 1) * 2.5:F1}% شد.");
        if (defTankBreakdown != null && defTankBreakdown.Count > 1) sb.AppendLine($"• مدافع با {defTankBreakdown.Count} مدل مختلف دفاع را لایه‌بندی کرده بود.");

        string airLine = BuildAirNarrative(air, aFight, aBomb, dFight, dAA, aAirStrat, aAirTac, aFs, aBs, dFs);
        if (airLine != null) sb.AppendLine($"• هوا: {airLine}");

        // Events with more intelligence
        for (int i = 0; i < evtN; i++)
        {
            var e = _evts[i];
            string evtDesc = e.Kind switch
            {
                E_CONTACT => $"⏱ {e.Tick * TICK_MIN / 60:F1}h – تماس اولیه در جبهه {e.A:F0}km",
                E_AMBUSH => $"💥 کمین فعال در عمق {e.B:F1}km – ستون پیشرو غافلگیر شد",
                E_BREAK5 => $"🔓 رخنه 5km در {e.Tick * TICK_MIN / 60:F1}h",
                E_BREAK10 => $"🔓 رخنه 10km – توسعه عمقی",
                E_BREAK20 => $"🔓 رخنه 20km – ستون زرهی به عمق رسید",
                E_BREAK30 => $"💣 فروپاشی 30km – جبهه شکست",
                E_SHIFT => $"🔄 نقطه عطف – ابتکار جابجا شد",
                E_ROUT => e.A < 0.5f ? "🏃‍♂️ عقب‌نشینی مهاجم" : "🏃‍♂️ تارومار مدافع",
                E_HALT => $"⛔ بن‌بست در {e.A:F1}km",
                E_SUPPLY => $"🚚 کشش تدارکات در {e.A:F1}km",
                _ => null
            };
            if (evtDesc != null) sb.AppendLine($"• {evtDesc}");
        }

        if (encircled) sb.AppendLine("• حلقه محاصره بسته شد – فشار چند جهتی");
        if (supplyStrain) sb.AppendLine("• تدارکات مهاجم تحت فشار");

        sb.AppendLine($"• اطلاعات: مهاجم {aIntelQ:P0} vs مدافع {dIntelQ:P0}");

        sb.AppendLine();
        sb.AppendLine("📊 آمار نهایی:");
        sb.AppendLine($"🔻 مهاجم: {r.AttackerTanksLost} تانک، {r.AttackerSoldiersLost} سرباز، {r.AttackerFightersLost} جنگنده، {r.AttackerBombersLost} بمب‌افکن");
        sb.AppendLine($"🔻 مدافع: {r.DefenderTanksLost} تانک، {r.DefenderSoldiersLost} سرباز، {r.DefenderFightersLost} جنگنده، {r.DefenderAntiAirLost} پدافند");
        sb.AppendLine($"🛫 برتری هوایی: {AirSupText(air.Superiority)}");
        sb.AppendLine($"⏱ مدت: {h}h {m}m | 💰 غنیمت: {r.AttackerMoneyGained / 1000.0:F1}K");

        // Adaptive conclusion
        if (r.AttackerWon) sb.AppendLine("🧠 نتیجه تطبیقی: تمرکز بر ضعف سکتور + توسعه رخنه – پیروزی قاطع.");
        else if (r.AttackerFailed) sb.AppendLine("🧠 نتیجه تطبیقی: دفاع سازمان‌یافته + زمین مساعد – حمله خنثی شد.");
        else sb.AppendLine("🧠 نتیجه تطبیقی: نبرد فرسایشی – هیچ طرف ضربه نهایی نزد.");

        r.AttackerReport = sb.ToString();

        // Defender report – similar but from defender POV
        sb.Clear();
        sb.AppendLine($"🛡 گزارش دفاع – {def.Name} vs {atk.Name}");
        sb.AppendLine(outcome);
        sb.AppendLine(envLine);
        sb.AppendLine($"📊 پیشروی دشمن: {bar}");
        if (compDesc.Length > 0) sb.AppendLine($"🔧 ترکیب دشمن: {compDesc}");
        sb.AppendLine();
        sb.AppendLine("📜 تحلیل:");
        sb.AppendLine($"• استراتژی شما: {dStratName}/{dTacName} – مقاومت {counterAtk:F2}x");
        if (airLine != null) sb.AppendLine($"• هوا: {airLine}");
        sb.AppendLine($"• تلفات شما: {r.DefenderTanksLost} تانک، {r.DefenderSoldiersLost} سرباز");
        sb.AppendLine($"• تلفات دشمن: {r.AttackerTanksLost} تانک، {r.AttackerSoldiersLost} سرباز");
        sb.AppendLine($"💸 خسارت: {r.DefenderMoneyLost / 1000.0:F1}K پول");
        r.DefenderReport = sb.ToString();

        // Group announcement – concise
        sb.Clear();
        sb.AppendLine("📰 خبر جنگ – گزارش هوشمند");
        sb.AppendLine($"⚔️ {atk.Name} به {def.Name} – {outcome}");
        sb.AppendLine($"📊 {bar} | 📍 {r.PenetrationKm:F1}km | ⏱ {h}:{m:D2}");
        sb.AppendLine($"💀 مهاجم: {r.AttackerTanksLost}🛡 {r.AttackerSoldiersLost}🪖 | مدافع: {r.DefenderTanksLost}🛡 {r.DefenderSoldiersLost}🪖");
        if (attTankBreakdown != null && attTankBreakdown.Count > 1) sb.AppendLine($"🔧 تنوع زرهی: {attTankBreakdown.Count} مدل درگیر");
        r.GroupAnnouncement = sb.ToString();
    }

    // ═════════════════════════ هسته شبیه‌سازی نبرد ═══════════════════════════
    public static BattleResult RunBattleSeeded(Country attacker, Country defender,
        long reqTanks, long reqSoldiers, long reqFighters, long reqBombers,
        int strategy, int tactic, int airStrategy, int airTactic, ulong seed)
    {
        EnsureBuffers();
        var rng = new XorRng(seed);
        var res = new BattleResult();

        long aTanks = Math.Max(0, Math.Min(reqTanks, attacker.Tanks));
        long aSold  = Math.Max(0, Math.Min(reqSoldiers, attacker.Soldiers));
        long aFight = Math.Max(0, Math.Min(reqFighters, attacker.Planes));
        long aBomb  = Math.Max(0, Math.Min(reqBombers, attacker.Bombers));
        long dTanks = Math.Min(defender.Tanks, Math.Max(defender.DefenseTanks, (long)Math.Ceiling(defender.Tanks * 0.2)));
        long dSold  = Math.Min(defender.Soldiers, Math.Max(defender.DefenseSoldiers, (long)Math.Ceiling(defender.Soldiers * 0.2)));
        long dFight = Math.Min(defender.Planes, defender.DefenseFighters);
        long dAA    = defender.AntiAir;

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

        var aSpec = SpecOf(attacker.Faction);
        var dSpec = SpecOf(defender.Faction);

        _weather = PickWeather(ref rng);
        _startTime = (byte)rng.Next(4);

        float counterAtk = CalculateAdvancedStrategyAdvantage(aStrat, aTac, dStrat, dTac, aSpec, dSpec, aTanks, dTanks, aSold, dSold, ref rng);

        AirOutcome air = RunAirPhase(attacker, defender, aFight, aBomb, aAirStrat, aAirTac,
                                     dFight, dAA, dStrat, dTac, dAirStrat, dAirTac, ref rng);

        res.AttackerFightersLost = air.AtkFightersLost;
        res.AttackerBombersLost = air.AtkBombersLost;
        res.DefenderFightersLost = air.DefFightersLost;
        res.DefenderAntiAirLost = air.DefAntiAirLost;
        res.AirSuperiority = Math.Round(air.Superiority, 2);

        long aTankLoss = 0, aSoldLoss = 0, dTankLoss = 0, dSoldLoss = 0;
        float effDepth = 0f, maxDepth = 0f;
        int tick = 0, evtN = 0;
        bool contact = false, ambushFired = false, encircled = false;
        int duelPeakTick = -1;
        float aIntelQ = 0f, dIntelQ = 0f;
        bool supplyStrain = false;
        bool defHasGround = (dTanks + dSold) > 0;

        if (!anyGround)
        {
            effDepth = 0f; maxDepth = 0f; tick = 30;
        }
        else if (!defHasGround)
        {
            GenTerrain(ref rng);
            int nA0 = BuildSide(_atk, true, aTanks, aSold, aStrat, aTac, ref rng);
            float airDrag = air.Superiority < -0.15f ? Math.Clamp(-air.Superiority, 0f, 1f) * 0.25f : 0f;
            float friction = 1f - airDrag;
            effDepth = WIN_DEPTH * friction; maxDepth = effDepth;
            CountLosses(_atk, nA0, ref aTankLoss, ref aSoldLoss);
            aTankLoss = (long)(aTankLoss * 0.02f);
            aSoldLoss = (long)(aSoldLoss * 0.02f);
            tick = 60;
        }
        else
        {
            GenTerrain(ref rng);
            int nA = BuildSide(_atk, true,  aTanks, aSold, aStrat, aTac, ref rng);
            int nD = BuildSide(_def, false, dTanks, dSold, dStrat, dTac, ref rng);
            for (int i = 0; i < nD; i++) { _intelA[i] = default; _intelA[i].Stale = 9999f; }
            for (int i = 0; i < nA; i++) { _intelD[i] = default; _intelD[i].Stale = 9999f; }

            float aQual = FactionQuality(attacker.Faction);
            float dQual = FactionQuality(defender.Faction);
            float aPow0 = SidePower(_atk, nA, aSpec), dPow0 = SidePower(_def, nD, dSpec);
            float prevMomentum = 0f; int haltTicks = 0; float duelPeak = 0f;
            float casA = air.CasAtk, casD = air.CasDef;

            if (air.Superiority > 0.05f)
                casA *= 1f + Math.Clamp(air.Superiority, 0f, 1f) * 0.45f;
            else if (air.Superiority < -0.05f)
                casD *= 1f + Math.Clamp(-air.Superiority, 0f, 1f) * 0.45f;

            if (air.Superiority > 0.25f) casD *= 1f - Math.Clamp(air.Superiority - 0.25f, 0f, 0.5f) * 0.4f;
            else if (air.Superiority < -0.25f) casA *= 1f - Math.Clamp(-air.Superiority - 0.25f, 0f, 0.5f) * 0.4f;

            if (defender.Cities <= 0) casD *= 1.5f;

            for (tick = 0; tick < MAX_TICKS; tick++)
            {
                byte tnow = TimeAtTick(tick);
                float visEnv = WxVision[_weather] * TimeVision[tnow];
                float accEnv = WxAcc[_weather];
                aIntelQ = SenseSide(_atk, nA, _def, nD, _intelA, aTac == 2 && aStrat == 1, visEnv, ref rng);
                dIntelQ = SenseSide(_def, nD, _atk, nA, _intelD, dStrat == 2, visEnv, ref rng);

                if (tick % AI_PERIOD == 0)
                {
                    BuildThreatMap(_def, nD, _intelA, nD, _threatA, dSpec);
                    BuildThreatMap(_atk, nA, _intelD, nA, _threatD, aSpec);
                    CommandAttacker(nA, nD, aStrat, aTac, effDepth, aIntelQ, ref rng, ref encircled, tick);
                    CommandDefender(nD, nA, dStrat, dTac, effDepth, dIntelQ, ref rng);
                }

                MoveSide(_atk, nA, aSpec, true, ref rng);
                MoveSide(_def, nD, dSpec, false, ref rng);

                float supplyA = SupplyFactor(effDepth);
                if (!supplyStrain && supplyA < 0.8f) { supplyStrain = true; AddEvt(ref evtN, tick, E_SUPPLY, effDepth); }

                float aDuel = FireSide(_atk, nA, aSpec, _def, nD, dSpec, _intelA, _intelD, true,  aStrat, aTac, dStrat, encircled, casA * counterAtk * aQual * supplyA, accEnv, ref rng, ref evtN, tick, ref contact, ref ambushFired);
                float dDuel = FireSide(_def, nD, dSpec, _atk, nA, aSpec, _intelD, _intelA, false, dStrat, dTac, aStrat, false,     casD * dQual, accEnv, ref rng, ref evtN, tick, ref contact, ref ambushFired);

                float duel = aDuel + dDuel;
                if (duel > duelPeak) { duelPeak = duel; duelPeakTick = tick; }

                MoraleSide(_atk, nA, true,  ref rng, ref evtN, tick);
                MoraleSide(_def, nD, false, ref rng, ref evtN, tick);

                float d = EffectiveDepth(_atk, nA);
                if (d > effDepth)
                {
                    float prev = effDepth; effDepth = d;
                    if (prev < 5f && d >= 5f) AddEvt(ref evtN, tick, E_BREAK5, d);
                    if (prev < 10f && d >= 10f) AddEvt(ref evtN, tick, E_BREAK10, d);
                    if (prev < 20f && d >= 20f) AddEvt(ref evtN, tick, E_BREAK20, d);
                    if (prev < 30f && d >= 30f) AddEvt(ref evtN, tick, E_BREAK30, d);
                    haltTicks = 0;
                }
                else haltTicks++;

                if (d > maxDepth) maxDepth = d;

                float aPow = SidePower(_atk, nA, aSpec), dPow = SidePower(_def, nD, dSpec);
                float momentum = (aPow / Math.Max(1f, aPow0)) - (dPow / Math.Max(1f, dPow0));
                if (tick > 20 && prevMomentum >= 0 && momentum < -0.12f) AddEvt(ref evtN, tick, E_SHIFT, 0);
                prevMomentum = momentum;

                if (effDepth >= WIN_DEPTH) { tick++; break; }
                if (aPow < aPow0 * 0.13f) { tick++; break; }
                if (dPow < dPow0 * 0.10f && effDepth > 6f)
                {
                    effDepth = Math.Min(WIN_DEPTH, effDepth + (WIN_DEPTH - effDepth) * 0.7f);
                    AddEvt(ref evtN, tick, E_ROUT, 1); tick++; break;
                }
                if (haltTicks > 90 && contact) { AddEvt(ref evtN, tick, E_HALT, effDepth); tick++; break; }
            }

            CountLosses(_atk, nA, ref aTankLoss, ref aSoldLoss);
            CountLosses(_def, nD, ref dTankLoss, ref dSoldLoss);

            if (effDepth >= 22f && effDepth < WIN_DEPTH)
                effDepth = Math.Min(WIN_DEPTH, effDepth + (WIN_DEPTH - effDepth) * 0.5f);
            else if (effDepth <= 6f && effDepth > FAIL_DEPTH)
                effDepth = Math.Max(0f, effDepth - (effDepth - FAIL_DEPTH) * 0.5f);
        }

        aTankLoss = Math.Min(aTankLoss, aTanks); aSoldLoss = Math.Min(aSoldLoss, aSold);
        dTankLoss = Math.Min(dTankLoss, dTanks); dSoldLoss = Math.Min(dSoldLoss, dSold);

        float frac = Math.Clamp((effDepth - FAIL_DEPTH) / (WIN_DEPTH - FAIL_DEPTH), 0f, 1f);
        int success = (int)Math.Round(frac * 100);
        bool absWin = effDepth >= WIN_DEPTH;
        bool absFail = anyGround && effDepth < FAIL_DEPTH;

        long lootMoney = (long)(defender.Money * 0.15 * frac);
        long lootIron  = (long)(defender.Iron  * 0.10 * frac);
        lootMoney = Math.Min(lootMoney, defender.Money);
        lootIron  = Math.Min(lootIron, defender.Iron);

        long stratMoney = Math.Min(air.StratMoney, Math.Max(0, defender.Money - lootMoney));
        long stratIron  = Math.Min(air.StratIron,  Math.Max(0, defender.Iron  - lootIron));

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
        res.AttackerFailed = absFail;
        res.DurationMinutes = Math.Max(30, (int)(tick * TICK_MIN));

        double aLossR = (aTanks + aSold) > 0 ? (aTankLoss * 10.0 + aSoldLoss) / Math.Max(1.0, aTanks * 10.0 + aSold) : 0;
        double dLossR = (dTanks + dSold) > 0 ? (dTankLoss * 10.0 + dSoldLoss) / Math.Max(1.0, dTanks * 10.0 + dSold) : 0;

        res.AttackerWelfareChange = -Math.Clamp(aLossR * 2.0 + (res.AttackerFailed ? 1.0 : 0), 0, 3);
        res.DefenderWelfareChange = -Math.Clamp(dLossR * 2.0 + (absWin ? 1.5 : 0) + frac * 0.8 + air.StratWelfare * 0.3, 0, 4);

        BuildReports(res, attacker, defender, aSpec, dSpec, aStrat, aTac, dStrat, dTac,
            aTanks, aSold, dTanks, dSold, aFight, aBomb, dFight, dAA,
            aAirStrat, aAirTac, dAirStrat, dAirTac, air,
            evtN, duelPeakTick, encircled, ambushFired, aIntelQ, dIntelQ, effDepth, frac,
            anyGround, defHasGround, supplyStrain, counterAtk);

        SaveBattle(attacker, defender, res);
        return res;
    }

    static float StrategyCounter(int aStrat, int dStrat)
    {
        if (aStrat == 1 && dStrat == 1) return 1.08f;
        if (aStrat == 1 && dStrat == 2) return 0.92f;
        if (aStrat == 2 && dStrat == 1) return 1.12f;
        return 1.00f;
    }

    static byte PickWeather(ref XorRng rng)
    {
        float r = rng.NextF();
        if (r < 0.45f) return W_CLEAR;
        if (r < 0.68f) return W_CLOUD;
        if (r < 0.84f) return W_RAIN;
        if (r < 0.94f) return W_FOG;
        return W_SNOW;
    }

    static float SupplyFactor(float depth)
    {
        if (depth <= 10f) return 1f;
        return Math.Clamp(1f - (depth - 10f) / 50f, 0.6f, 1f);
    }

    static void AddEvt(ref int n, int tick, byte kind, float a, float b = 0)
    {
        if (n >= _evts.Length) return;
        _evts[n].Tick = (short)tick; _evts[n].Kind = kind; _evts[n].A = a; _evts[n].B = b;
        n++;
    }

    // ═════════════════════════ فاز هوایی ════════════════════════════════════
    static AirOutcome RunAirPhase(Country atk, Country def,
        long aFight, long aBomb, int aAirStrat, int aAirTac,
        long dFight, long dAA, int dStrat, int dTac, int dAirStrat, int dAirTac, ref XorRng rng)
    {
        var o = new AirOutcome { CasAtk = 1f, CasDef = 1f };
        o.AtkHadAir = (aFight + aBomb) > 0;
        o.DefHadAir = (dFight + dAA) > 0;
        if (!o.AtkHadAir && !o.DefHadAir) { o.Superiority = 0f; return o; }

        var aFs = FighterOf(atk.Faction);
        var aBs = BomberOf(atk.Faction);
        var dFs = FighterOf(def.Faction);

        float wxAir = WxAir[_weather];
        float aFighterQ = (aFs.Maneuver * 0.55f + aFs.Firepower * 0.45f) * FactionQuality(atk.Faction);
        float dFighterQ = (dFs.Maneuver * 0.55f + dFs.Firepower * 0.45f) * FactionQuality(def.Faction);
        float capBonus = (dAirStrat == 1 && dAirTac == 1) ? 1.25f : 1f;
        float flakBonus = (dAirStrat == 2 && dAirTac == 1) ? 1.35f : 1f;
        if (dAirStrat == 2 && dAirTac == 2) capBonus *= 1.1f;

        float aFighterPow = aFight * aFighterQ * wxAir * rng.Range(0.9f, 1.1f);
        float dFighterPow = dFight * dFighterQ * capBonus * rng.Range(0.9f, 1.1f);

        long aFightLost = 0, dFightLost = 0;
        if (aFight > 0 && dFight > 0)
        {
            o.HadAirCombat = true;
            float total = aFighterPow + dFighterPow;
            float aLossFrac = Math.Clamp(dFighterPow / Math.Max(1f, total) * rng.Range(0.7f, 1.1f), 0f, 0.95f);
            float dLossFrac = Math.Clamp(aFighterPow / Math.Max(1f, total) * rng.Range(0.7f, 1.1f), 0f, 0.95f);
            aFightLost = (long)Math.Round(aFight * aLossFrac);
            dFightLost = (long)Math.Round(dFight * dLossFrac);
        }
        long aBombLost = 0, dAALost = 0;
        if (aBomb > 0 && dFight > 0 && aFight == 0)
        {
            o.HadAirCombat = true;
            float interceptPower = dFight * dFighterQ * capBonus * rng.Range(0.8f, 1.1f);
            long intercepted = (long)Math.Round(Math.Min(aBomb, interceptPower * 0.015f / (1f + aBs.Armor * 0.3f)));
            aBombLost += Math.Min(aBomb, intercepted);
        }

        long aFightLeft = aFight - aFightLost;
        long dFightLeft = dFight - dFightLost;

        if (dAA > 0 && (aFightLeft > 0 || aBomb > 0))
        {
            float aaPower = dAA * flakBonus * rng.Range(0.85f, 1.15f);
            float bomberResist = 1f / (1f + aBs.Armor * 0.25f);
            long bombHit = (long)Math.Round(Math.Min(Math.Max(0, aBomb - aBombLost), aaPower * 0.015f * bomberResist));
            aBombLost = Math.Min(aBomb, aBombLost + bombHit);

            long fightHit = (long)Math.Round(Math.Min(aFightLeft, aaPower * 0.02f));
            aFightLost += fightHit; aFightLeft -= fightHit;

            float incoming = aFightLeft + (aBomb - aBombLost) * 1.3f;
            dAALost = (long)Math.Round(Math.Min(dAA, incoming * rng.Range(0.03f, 0.07f)));
        }

        long aBombLeft = aBomb - aBombLost;
        float atkAirRemain = aFightLeft * aFighterQ + aBombLeft * 1.0f;
        float defAirRemain = dFightLeft * dFighterQ + dAA * 0.5f;
        float sup = (atkAirRemain - defAirRemain) / Math.Max(1f, atkAirRemain + defAirRemain);
        o.Superiority = Math.Clamp(sup, -1f, 1f);

        if (aAirStrat == 1)
        {
            if (aAirTac == 2 && aBombLeft > 0 && dFightLeft > 0)
            {
                float raidIntensity = aBombLeft * (aBs.Bombload / 3600f) * wxAir * (0.5f + 0.5f * Math.Clamp(o.Superiority + 0.5f, 0f, 1f));
                long grounded = (long)Math.Round(Math.Min(dFightLeft, raidIntensity * rng.Range(0.6f, 1.0f)));
                if (grounded > 0) { dFightLost += grounded; dFightLeft -= grounded; }
            }
            float casPower = (aFightLeft * aFs.Cas + aBombLeft * 1.5f) * wxAir;
            o.CasAtk = 1f + Math.Clamp(casPower / Math.Max(50f, (atk.Soldiers + 1) * 0.02f), 0f, 0.6f);
            if (o.Superiority < -0.1f)
                o.CasDef = 1f + Math.Clamp(dFightLeft * dFs.Cas / Math.Max(50f, (def.Soldiers + 1) * 0.02f), 0f, 0.4f);
        }
        else if (aAirStrat == 2)
        {
            float effBomb = aBombLeft * (0.55f + 0.45f * Math.Clamp(o.Superiority + 0.5f, 0f, 1f)) * wxAir;
            float perBomberDamage = aBs.Bombload / 3600f;
            float intensity = effBomb * perBomberDamage;
            float moneyFrac = Math.Clamp(intensity * 0.02f, 0f, aAirTac == 1 ? 0.35f : 0.30f);
            float ironFrac  = Math.Clamp(intensity * 0.02f, 0f, aAirTac == 1 ? 0.40f : 0.18f);
            if (aAirTac == 1)
            {
                o.StratMoney = (long)(def.Money * moneyFrac * 0.9f);
                o.StratIron  = (long)(def.Iron * ironFrac);
                o.StratWelfare = Math.Clamp(effBomb * 0.02f, 0f, 4f);
            }
            else
            {
                o.StratMoney = (long)(def.Money * moneyFrac);
                o.StratIron  = (long)(def.Iron * ironFrac * 0.5f);
                o.StratWelfare = Math.Clamp(effBomb * 0.02f, 0f, 2f);
            }
            o.CasAtk = 1f + Math.Clamp(aFightLeft * aFs.Cas / Math.Max(80f, (atk.Soldiers + 1) * 0.03f), 0f, 0.3f);
        }

        o.AtkFightersLost = Math.Min(aFight, Math.Max(0, aFightLost));
        o.AtkBombersLost  = Math.Min(aBomb, Math.Max(0, aBombLost));
        o.DefFightersLost = Math.Min(dFight, Math.Max(0, dFightLost));
        o.DefAntiAirLost  = Math.Min(dAA, Math.Max(0, dAALost));

        return o;
    }

    // ═════════════════════════ تولید زمین ═══════════════════════════════════
    static float Hash(int x, int y, uint s)
    {
        uint h = (uint)(x * 374761393 + y * 668265263) ^ s;
        h = (h ^ (h >> 13)) * 1274126177u;
        return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777215f;
    }

    static float Noise(float x, float y, uint s)
    {
        int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y);
        float fx = x - xi, fy = y - yi;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        float a = Hash(xi, yi, s), b = Hash(xi + 1, yi, s), c = Hash(xi, yi + 1, s), d = Hash(xi + 1, yi + 1, s);
        return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
    }

    static void GenTerrain(ref XorRng rng)
    {
        uint s1 = (uint)rng.NextU(), s2 = (uint)rng.NextU(), s3 = (uint)rng.NextU();
        for (int gy = 0; gy < GRID_H; gy++)
        for (int gx = 0; gx < GRID_W; gx++)
        {
            float e = Noise(gx * 0.09f, gy * 0.09f, s1) * 0.65f + Noise(gx * 0.23f, gy * 0.23f, s2) * 0.35f;
            float v = Noise(gx * 0.13f + 50, gy * 0.13f, s3);
            int idx = gy * GRID_W + gx;
            _elev[idx] = e;
            byte t;
            if (e > 0.78f) t = T_RIDGE;
            else if (e > 0.62f) t = T_HILL;
            else if (v > 0.72f && e > 0.3f) t = T_FOREST;
            else if (v < 0.12f && e < 0.35f) t = T_MARSH;
            else if (v > 0.62f && v <= 0.72f && e < 0.5f) t = T_URBAN;
            else t = T_PLAIN;
            _terr[idx] = t;
        }
    }

    static byte TerrAt(float x, float y)
    {
        int gx = (int)(x / CELL); int gy = (int)((y + 6f) / CELL);
        if (gx < 0) gx = 0; if (gx >= GRID_W) gx = GRID_W - 1;
        if (gy < 0) gy = 0; if (gy >= GRID_H) gy = GRID_H - 1;
        return _terr[gy * GRID_W + gx];
    }

    static float ElevAt(float x, float y)
    {
        int gx = Math.Clamp((int)(x / CELL), 0, GRID_W - 1);
        int gy = Math.Clamp((int)((y + 6f) / CELL), 0, GRID_H - 1);
        return _elev[gy * GRID_W + gx];
    }

    // ═════════════════════════ ساخت گروه‌های یک طرف ═════════════════════════
    static int BuildSide(Group[] g, bool atk, long tanks, long soldiers, int strat, int tac, ref XorRng rng)
    {
        long rawGroups = tanks / TANK_GROUP + soldiers / INF_GROUP + 2;
        float scale = rawGroups > MAX_GROUPS ? (float)rawGroups / MAX_GROUPS : 1f;
        float tankGrp = TANK_GROUP * scale, infGrp = INF_GROUP * scale;
        int n = 0;
        long tLeft = tanks, sLeft = soldiers;
        while (tLeft > 0 && n < MAX_GROUPS)
        {
            float u = (float)Math.Min(tLeft, (long)Math.Ceiling(tankGrp));
            InitGroup(ref g[n], atk, 1, u, strat, tac, ref rng);
            tLeft -= (long)u; n++;
        }
        while (sLeft > 0 && n < MAX_GROUPS)
        {
            float u = (float)Math.Min(sLeft, (long)Math.Ceiling(infGrp));
            InitGroup(ref g[n], atk, 0, u, strat, tac, ref rng);
            sLeft -= (long)u; n++;
        }
        return n;
    }

    static void InitGroup(ref Group gr, bool atk, byte type, float units, int strat, int tac, ref XorRng rng)
    {
        gr = default;
        gr.Type = type; gr.Units = units; gr.Size0 = units; gr.Alive = true;
        gr.Morale = rng.Range(0.85f, 1f);
        gr.CAmmo = units; gr.MAmmo = units;
        gr.Fatigue = 0f; gr.Exp = rng.Range(0f, 0.1f);
        gr.FireTgt = -1;
        if (atk)
        {
            gr.Y = rng.Range(-4.5f, -1.5f);
            if (strat == 1)
            {
                float c = tac == 1 ? FRONT_KM * 0.5f : (rng.NextF() < 0.5f ? FRONT_KM * 0.3f : FRONT_KM * 0.7f);
                gr.X = Math.Clamp(c + rng.Range(-5f, 5f), 1f, FRONT_KM - 1);
            }
            else { gr.X = rng.Range(1f, FRONT_KM - 1); gr.Posture = P_FLANK; }
            gr.Posture = gr.Posture == P_FLANK ? P_FLANK : P_ADVANCE;
            gr.TgtX = gr.X; gr.TgtY = 8f;
        }
        else
        {
            gr.X = rng.Range(1f, FRONT_KM - 1);
            if (strat == 1)
            {
                gr.Y = tac == 1 ? rng.Range(0.8f, 3.2f) : rng.Range(1.5f, 6f);
                gr.Posture = tac == 1 ? P_DEFEND : P_PATROL;
                if (tac == 1) SeekCover(ref gr, ref rng);
            }
            else
            {
                gr.Y = tac == 1 ? rng.Range(2f, 7f) : rng.Range(4f, 10f);
                gr.Posture = P_AMBUSH;
                SeekCover(ref gr, ref rng);
            }
            gr.TgtX = gr.X; gr.TgtY = gr.Y;
        }
        gr.Sector = (byte)Math.Clamp((int)(gr.X / (FRONT_KM / 10f)), 0, 9);
    }

    static void SeekCover(ref Group gr, ref XorRng rng)
    {
        float bx = gr.X, by = gr.Y, best = TerCover[TerrAt(gr.X, gr.Y)];
        for (int i = 0; i < 6; i++)
        {
            float x = Math.Clamp(gr.X + rng.Range(-2f, 2f), 0.5f, FRONT_KM - 0.5f);
            float y = Math.Clamp(gr.Y + rng.Range(-1.5f, 1.5f), 0.3f, DEPTH_KM - 1);
            float c = TerCover[TerrAt(x, y)];
            if (c > best) { best = c; bx = x; by = y; }
        }
        gr.X = bx; gr.Y = by;
    }

    // ═════════════════ مه جنگ: شناسایی + اشتراک اطلاعات (با محیط) ════════════
    static float SenseSide(Group[] own, int nOwn, Group[] foe, int nFoe, Intel[] intel, bool reconBonus, float visEnv, ref XorRng rng)
    {
        float sum = 0f; int alive = 0;
        for (int j = 0; j < nFoe; j++)
        {
            if (!foe[j].Alive) { intel[j].Level *= 0.9f; continue; }
            alive++;
            ref Intel it = ref intel[j];
            it.Stale += TICK_MIN;
            float bestGain = 0f;
            byte ft = TerrAt(foe[j].X, foe[j].Y);
            float conceal = TerCover[ft];
            if (foe[j].Posture == P_AMBUSH && !foe[j].Sprung) conceal = Math.Min(0.92f, conceal + 0.35f);
            float sig = foe[j].Signature;
            for (int i = 0; i < nOwn; i++)
            {
                if (!own[i].Alive) continue;
                float dx = own[i].X - foe[j].X, dy = own[i].Y - foe[j].Y;
                float dist2 = dx * dx + dy * dy;
                if (dist2 > 36f) continue;
                float dist = MathF.Sqrt(dist2);
                float vis = (own[i].Type == 1 ? 2.6f : 2.1f) * TerVision[TerrAt(own[i].X, own[i].Y)] * visEnv;
                if (ElevAt(own[i].X, own[i].Y) > ElevAt(foe[j].X, foe[j].Y) + 0.12f) vis *= 1.3f;
                if (reconBonus) vis *= 1.25f;
                float moveSig = foe[j].Posture is P_ADVANCE or P_FLANK or P_ASSAULT ? 0.25f : 0f;
                float p = (1f - Math.Clamp(dist / Math.Max(0.3f, vis), 0f, 1f)) * (1f - conceal) + sig + moveSig;
                if (p > bestGain) bestGain = p;
            }
            if (bestGain > 0.04f && rng.NextF() < Math.Clamp(bestGain, 0f, 0.95f))
            {
                it.Level = Math.Min(1f, it.Level + 0.45f + bestGain * 0.5f);
                it.LastX = foe[j].X; it.LastY = foe[j].Y; it.Stale = 0f;
            }
            else
            {
                it.Level *= it.Stale > 60f ? 0.93f : 0.985f;
                if (it.Stale > 150f) it.Level *= 0.85f;
            }
            sum += it.Level;
        }
        for (int j = 0; j < nFoe; j++) { ref var f = ref foe[j]; f.Signature *= 0.55f; }
        return alive > 0 ? sum / alive : 0f;
    }

    static void BuildThreatMap(Group[] foe, int nFoe, Intel[] intel, int nIntel, float[] map, TankSpec foeSpec)
    {
        Array.Clear(map, 0, 10);
        for (int j = 0; j < nFoe; j++)
        {
            if (!foe[j].Alive || intel[j].Level < 0.15f) continue;
            int s = Math.Clamp((int)(intel[j].LastX / (FRONT_KM / 10f)), 0, 9);
            float pw = foe[j].Type == 1 ? foe[j].Units * 9f : foe[j].Units * 0.8f;
            map[s] += pw * intel[j].Level;
        }
    }

    static int WeakestSector(float[] threat, ref XorRng rng)
    {
        int best = 0; float bv = float.MaxValue;
        for (int s = 1; s < 9; s++)
        {
            float v = threat[s] * 1f + threat[s - 1] * 0.4f + threat[s + 1] * 0.4f + rng.NextF() * 8f;
            if (v < bv) { bv = v; best = s; }
        }
        return best;
    }

    static void CommandAttacker(int nA, int nD, int strat, int tac, float depth, float intelQ,
        ref XorRng rng, ref bool encircled, int tick)
    {
        int weak = WeakestSector(_threatA, ref rng);
        float mainX = (weak + 0.5f) * (FRONT_KM / 10f);
        for (int i = 0; i < nA; i++)
        {
            ref Group g = ref _atk[i];
            if (!g.Alive || g.Posture == P_RETREAT) continue;
            float ammoR = (g.CAmmo + g.MAmmo) / Math.Max(0.01f, g.Size0 * 2f);
            if (ammoR <= 0.02f) { g.Posture = P_RETREAT; g.TgtY = -4f; continue; }
            if (ammoR < 0.18f) { g.Posture = P_HOLD; continue; }
            if (g.Morale < 0.35f) { g.Posture = P_HOLD; continue; }
            if (strat == 1)
            {
                bool probing = tac == 2 && tick < 40 && intelQ < 0.35f;
                g.Posture = probing ? P_PATROL : (depth > 2f ? P_ASSAULT : P_ADVANCE);
                float spread = probing ? 14f : (tac == 1 ? 4f : 7f);
                g.TgtX = Math.Clamp(mainX + rng.Range(-spread, spread), 1f, FRONT_KM - 1);
                g.TgtY = g.Y + 6f;
            }
            else
            {
                bool leftArm = (i & 1) == 0;
                float armX = leftArm ? mainX - 8f - depth * 0.3f : mainX + 8f + depth * 0.3f;
                if (tac == 2) armX += MathF.Sin((tick + i * 7) * 0.05f) * 5f;
                g.TgtX = Math.Clamp(armX + rng.Range(-3f, 3f), 1f, FRONT_KM - 1);
                g.TgtY = g.Y + (g.Type == 1 ? 6f : 4f);
                g.Posture = P_FLANK;
                if (depth > 8f && !encircled && intelQ > 0.45f) encircled = true;
            }
        }
    }

    static void CommandDefender(int nD, int nA, int strat, int tac, float depth, float intelQ, ref XorRng rng)
    {
        int hot = 0; float hv = -1f;
        for (int s = 0; s < 10; s++) if (_threatD[s] > hv) { hv = _threatD[s]; hot = s; }
        float hotX = (hot + 0.5f) * (FRONT_KM / 10f);
        for (int i = 0; i < nD; i++)
        {
            ref Group g = ref _def[i];
            if (!g.Alive || g.Posture == P_RETREAT) continue;
            float ammoR = (g.CAmmo + g.MAmmo) / Math.Max(0.01f, g.Size0 * 2f);
            if (ammoR <= 0.02f) { g.Posture = P_RETREAT; g.TgtY = Math.Min(DEPTH_KM - 1, g.Y + 6f); continue; }
            if (strat == 1)
            {
                if (tac == 1)
                {
                    bool reserve = i % 3 == 2;
                    if (reserve && hv > 0 && depth > 1f)
                    { g.TgtX = Math.Clamp(hotX + rng.Range(-3f, 3f), 1f, FRONT_KM - 1); g.TgtY = Math.Max(0.8f, depth - 1f); g.Posture = P_ADVANCE; }
                    else g.Posture = P_DEFEND;
                }
                else
                {
                    if (hv > 0) { g.TgtX = Math.Clamp(hotX + rng.Range(-5f, 5f), 1f, FRONT_KM - 1); g.TgtY = Math.Clamp(depth + rng.Range(0f, 2f), 1f, 8f); g.Posture = P_ADVANCE; }
                    else { g.TgtX = Math.Clamp(g.X + rng.Range(-6f, 6f), 1f, FRONT_KM - 1); g.Posture = P_PATROL; }
                }
            }
            else
            {
                if (tac == 1)
                {
                    if (!g.Sprung) { g.Posture = P_AMBUSH; continue; }
                    g.Posture = P_ASSAULT;
                    g.TgtX = Math.Clamp(hotX + rng.Range(-4f, 4f), 1f, FRONT_KM - 1);
                    g.TgtY = Math.Max(1f, depth);
                }
                else
                {
                    if (depth < 8f && !g.Sprung) { g.TgtY = Math.Min(12f, g.Y + 1.5f); g.Posture = P_AMBUSH; }
                    else { g.Posture = P_ASSAULT; g.TgtX = Math.Clamp(hotX + rng.Range(-6f, 6f), 1f, FRONT_KM - 1); g.TgtY = Math.Max(1f, depth - 2f); }
                }
            }
        }
    }

    static void MoveSide(Group[] g, int n, TankSpec spec, bool atk, ref XorRng rng)
    {
        float wxSpd = WxSpeed[_weather];
        for (int i = 0; i < n; i++)
        {
            ref Group u = ref g[i];
            if (!u.Alive) continue;
            if (u.Posture is P_DEFEND or P_AMBUSH or P_HOLD) continue;
            float baseKmH = u.Type == 1 ? spec.Speed * 0.32f : 4.2f;
            if (u.Posture == P_RETREAT) baseKmH *= 1.2f;
            if (u.Supp > 0.5f) baseKmH *= 0.45f;
            baseKmH *= (1f - u.Fatigue * 0.3f);
            float ter = TerSpeed[TerrAt(u.X, u.Y)];
            float step = baseKmH * ter * wxSpd * (TICK_MIN / 60f);
            float dx = u.TgtX - u.X, dy = u.TgtY - u.Y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            if (dist < 0.15f) continue;
            float mv = Math.Min(step, dist);
            u.X += dx / dist * mv; u.Y += dy / dist * mv;
            u.X = Math.Clamp(u.X, 0.2f, FRONT_KM - 0.2f);
            u.Y = Math.Clamp(u.Y, -6f, DEPTH_KM);
            if (mv > 0.5f) u.Signature = Math.Min(1f, u.Signature + 0.18f);
            u.Sector = (byte)Math.Clamp((int)(u.X / (FRONT_KM / 10f)), 0, 9);
        }
    }

    // ═════════════ آتش: زره/مسلسل/HE + پشتیبانی هوایی + محیط + تجربه ════════
    static float FireSide(Group[] own, int nOwn, TankSpec ospec, Group[] foe, int nFoe, TankSpec fspec,
        Intel[] ownIntel, Intel[] foeIntel, bool atk, int strat, int tac, int foeStrat, bool encircled,
        float combatMul, float accEnv, ref XorRng rng, ref int evtN, int tick, ref bool contact, ref bool ambushFired)
    {
        float duel = 0f;
        for (int i = 0; i < nOwn; i++)
        {
            ref Group u = ref own[i];
            if (!u.Alive || u.Posture == P_RETREAT) continue;
            int best = -1; float bestScore = 0f; float bestDist = 99f;
            float maxRange = u.Type == 1 ? 2.1f : 0.9f;
            for (int j = 0; j < nFoe; j++)
            {
                if (!foe[j].Alive) continue;
                float lvl = ownIntel[j].Level;
                if (lvl < 0.2f) continue;
                float dx = foe[j].X - u.X, dy = foe[j].Y - u.Y;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist > maxRange + 0.6f) continue;
                float pri = u.Type == 1 ? (foe[j].Type == 1 ? 3f : 1.6f) : (foe[j].Type == 1 ? 0.6f : 2.2f);
                pri *= 1f + (1f - foe[j].Units / Math.Max(1f, foe[j].Size0)) * 0.8f;
                float score = pri * lvl / (0.4f + dist);
                if (score > bestScore) { bestScore = score; best = j; bestDist = dist; }
            }
            u.FireTgt = (short)best;
            if (best < 0) continue;
            if (bestDist > maxRange) continue;
            if (!contact) { contact = true; AddEvt(ref evtN, tick, E_CONTACT, u.X); }
            float ambushMul = 1f;
            if (u.Posture == P_AMBUSH && !u.Sprung)
            {
                u.Sprung = true; ambushMul = 2.6f;
                if (!ambushFired) { ambushFired = true; AddEvt(ref evtN, tick, E_AMBUSH, u.X, u.Y); }
            }
            ref Group t = ref foe[best];
            float intelQ = ownIntel[best].Level;
            byte tt = TerrAt(t.X, t.Y);
            float acc = 0.62f * (0.45f + 0.55f * intelQ) * TerAcc[TerrAt(u.X, u.Y)] * accEnv * (1f - u.Supp * 0.5f);
            acc *= (0.9f + u.Exp * 0.3f);
            if (u.Posture is P_ADVANCE or P_ASSAULT or P_FLANK) acc *= 0.78f;
            if (ElevAt(u.X, u.Y) > ElevAt(t.X, t.Y) + 0.1f) acc *= 1.18f;
            float cover = TerCover[tt] * (t.Posture is P_DEFEND or P_AMBUSH or P_HOLD ? 1.25f : 0.8f);
            float ammoR = (u.CAmmo + u.MAmmo) / Math.Max(0.01f, u.Size0 * 2f);
            float ammoMul = ammoR > 0.5f ? 1f : 0.55f + ammoR * 0.9f;
            float encMul = atk && encircled && strat == 2 ? 1.25f : 1f;
            float morale = 0.55f + u.Morale * 0.45f;
            float k = acc * ammoMul * encMul * morale * ambushMul * combatMul * (1f - u.Fatigue * 0.25f) * (TICK_MIN / 6f);
            if (u.Type == 1)
            {
                float rangeMul = Math.Clamp(1.25f - bestDist * 0.45f, 0.45f, 1.2f);
                if (t.Type == 1)
                {
                    if (u.CAmmo > 0.05f)
                    {
                        float effArmor = fspec.Armor * (t.Posture is P_DEFEND or P_AMBUSH ? 1.3f : 1f);
                        float pen = 1f / (1f + MathF.Exp(-(ospec.Pen * rangeMul - effArmor) / 9f));
                        float shots = u.Units * 1.6f * k;
                        float kills = shots * 0.32f * pen * (0.9f + rng.NextF() * 0.25f);
                        ApplyDamage(ref t, kills, foeIntel, best);
                        u.CAmmo = Math.Max(0f, u.CAmmo - shots * 0.05f);
                        u.Signature = Math.Min(1f, u.Signature + 0.55f);
                        duel += kills;
                        t.Supp = Math.Min(1f, t.Supp + 0.12f);
                    }
                }
                else
                {
                    if (u.MAmmo > 0.05f)
                    {
                        float mgKill = u.Units * ospec.Mg * 1.05f * k * (1f - cover * 0.85f);
                        float heKill = 0f;
                        if (u.CAmmo > 0.05f)
                        {
                            heKill = u.Units * ospec.He * 4.5f * k * (1f - cover * 0.55f);
                            u.CAmmo = Math.Max(0f, u.CAmmo - u.Units * 0.04f);
                            u.Signature = Math.Min(1f, u.Signature + 0.5f);
                        }
                        ApplyDamage(ref t, mgKill + heKill, foeIntel, best);
                        u.MAmmo = Math.Max(0f, u.MAmmo - u.Units * 0.06f);
                        u.Signature = Math.Min(1f, u.Signature + 0.22f);
                        t.Supp = Math.Min(1f, t.Supp + 0.3f);
                    }
                }
            }
            else
            {
                if (t.Type == 0)
                {
                    if (u.MAmmo > 0.05f)
                    {
                        float kills = u.Units * 0.045f * k * (1f - cover * 0.8f);
                        ApplyDamage(ref t, kills, foeIntel, best);
                        u.MAmmo = Math.Max(0f, u.MAmmo - u.Units * 0.045f);
                        u.Signature = Math.Min(1f, u.Signature + 0.16f);
                        t.Supp = Math.Min(1f, t.Supp + 0.15f);
                    }
                }
                else if (bestDist < 0.45f)
                {
                    float kills = u.Units * 0.0045f * k * (foeStrat == 2 ? 1.2f : 1f);
                    ApplyDamage(ref t, kills, foeIntel, best);
                    u.MAmmo = Math.Max(0f, u.MAmmo - u.Units * 0.02f);
                    duel += kills * 0.5f;
                }
            }
        }
        return duel;
    }

    static void ApplyDamage(ref Group t, float kills, Intel[] foeIntel, int idx)
    {
        if (kills <= 0f) return;
        t.Units = Math.Max(0f, t.Units - kills);
        t.Morale = Math.Max(0f, t.Morale - kills / Math.Max(1f, t.Size0) * 1.6f);
        if (t.Units < t.Size0 * 0.08f || t.Units < 0.5f)
        {
            t.Alive = false;
            foeIntel[idx].Level = 0f;
        }
    }

    static void MoraleSide(Group[] g, int n, bool atk, ref XorRng rng, ref int evtN, int tick)
    {
        for (int i = 0; i < n; i++)
        {
            ref Group u = ref g[i];
            if (!u.Alive) continue;
            u.Supp = Math.Max(0f, u.Supp - 0.08f);
            u.Morale = Math.Min(1f, u.Morale + 0.004f);
            bool active = u.Posture is P_ADVANCE or P_ASSAULT or P_FLANK or P_RETREAT;
            u.Fatigue = Math.Clamp(u.Fatigue + (active ? 0.006f : -0.004f), 0f, 1f);
            if (u.Supp > 0.1f) u.Exp = Math.Min(1f, u.Exp + 0.003f);
            float lossR = 1f - u.Units / Math.Max(1f, u.Size0);
            if (lossR > 0.5f && u.Morale < 0.3f && rng.NextF() < 0.12f)
            {
                if (u.Posture != P_RETREAT) AddEvt(ref evtN, tick, E_ROUT, atk ? 0 : 1);
                u.Posture = P_RETREAT;
                u.TgtY = atk ? -5f : Math.Min(DEPTH_KM, u.Y + 8f);
            }
        }
    }

    static float EffectiveDepth(Group[] a, int n)
    {
        float best = 0f;
        for (int i = 0; i < n; i++)
        {
            if (!a[i].Alive || a[i].Posture == P_RETREAT) continue;
            if (a[i].Y <= best) continue;
            float pw = a[i].Type == 1 ? a[i].Units * 10f : a[i].Units;
            if (pw < 25f) continue;
            for (int j = 0; j < n; j++)
            {
                if (j == i || !a[j].Alive || a[j].Posture == P_RETREAT) continue;
                float dx = a[i].X - a[j].X, dy = a[i].Y - a[j].Y;
                if (dx * dx + dy * dy < 12.25f) { best = a[i].Y; break; }
            }
        }
        return Math.Max(0f, best);
    }

    static float SidePower(Group[] g, int n, TankSpec spec)
    {
        float p = 0f;
        for (int i = 0; i < n; i++)
        {
            if (!g[i].Alive) continue;
            float ammoR = (g[i].CAmmo + g[i].MAmmo) / Math.Max(0.01f, g[i].Size0 * 2f);
            float am = 0.45f + 0.55f * Math.Clamp(ammoR * 1.6f, 0f, 1f);
            p += (g[i].Type == 1 ? g[i].Units * (8f + spec.Armor * 0.04f + spec.Pen * 0.04f) : g[i].Units * 0.85f) * am;
        }
        return p;
    }

    static void CountLosses(Group[] g, int n, ref long tankLoss, ref long soldLoss)
    {
        double tl = 0, sl = 0;
        for (int i = 0; i < n; i++)
        {
            double lost = g[i].Size0 - (g[i].Alive ? g[i].Units : 0);
            if (g[i].Type == 1) tl += lost; else sl += lost;
        }
        tankLoss = (long)Math.Round(tl); soldLoss = (long)Math.Round(sl);
    }

    // ═════════════════════════ ساخت گزارش فارسی ═════════════════════════════
    static string ProgressBar(float frac, int color)
    {
        int filled = (int)Math.Round(Math.Clamp(frac, 0f, 1f) * 10);
        string fill = color == 1 ? "🟩" : color == 2 ? "🟥" : "🟦";
        var sb = new StringBuilder(24);
        for (int i = 0; i < 10; i++) sb.Append(i < filled ? fill : "⬜");
        return sb.ToString();
    }

    static int AttackerColor(BattleResult r) => r.AttackerWon ? 1 : r.AttackerFailed ? 2 : 0;
    static int DefenderColor(BattleResult r) => r.AttackerFailed ? 1 : r.AttackerWon ? 2 : 0;

    static void BuildReports(BattleResult r, Country atk, Country def, TankSpec aSpec, TankSpec dSpec,
        int aStrat, int aTac, int dStrat, int dTac, long aTanks, long aSold, long dTanks, long dSold,
        long aFight, long aBomb, long dFight, long dAA,
        int aAirStrat, int aAirTac, int dAirStrat, int dAirTac, AirOutcome air,
        int evtN, int duelPeakTick, bool encircled, bool ambushFired,
        float aIntelQ, float dIntelQ, float depth, float frac,
        bool anyGround, bool defHasGround, bool supplyStrain, float counterAtk)
    {
        var sb = _sb; sb.Clear();
        var aFs = FighterOf(atk.Faction); var aBs = BomberOf(atk.Faction); var dFs = FighterOf(def.Faction);
        string aStratName = aStrat == 1 ? "هجوم منسجم" : "محاصره و ضربه";
        string aTacName = aStrat == 1
            ? (aTac == 1 ? "حمله مستقیم متمرکز" : "حملات سبک اکتشافی و یورش اصلی")
            : (aTac == 1 ? "محاصره گسترده و فرسایش" : "حلقه محاصره متحرک");
        string dStratName = dStrat == 1 ? "دفاع منسجم" : "دفاع و ضدحمله پراکنده";
        string dTacName = dStrat == 1
            ? (dTac == 1 ? "دفاع ثابت در سنگرها" : "گشت متحرک ترکیبی")
            : (dTac == 1 ? "استتار و کمین" : "عقب‌نشینی تاکتیکی و تله");
        string aAirName = aAirStrat == 1 ? "برتری هوایی" : aAirStrat == 2 ? "بمباران راهبردی" : "بدون عملیات هوایی";
        string aAirTacName = aAirStrat == 1
            ? (aAirTac == 1 ? "شکار آزاد" : "حمله به پایگاه‌ها")
            : aAirStrat == 2 ? (aAirTac == 1 ? "بمباران دقیق" : "بمباران منطقه‌ای") : "—";
        string dAirName = dAirStrat == 1 ? "دفاع منطقه‌ای" : "دفاع نقطه‌ای";
        string dAirTacName = dAirStrat == 1
            ? (dAirTac == 1 ? "گشت هوایی رزمی (CAP)" : "ایستگاه شنود")
            : (dAirTac == 1 ? "آتشبند" : "پوشش مستقیم جنگنده");
        string outcome;
        if (!anyGround)
        {
            outcome = air.Superiority > 0.12 ? $"🛫 عملیات هوایی موفق {atk.Name}"
                    : air.Superiority < -0.12 ? $"🛫 عملیات هوایی ناموفق — برتری با {def.Name}"
                    : "🛫 عملیات هوایی بی‌نتیجه";
        }
        else if (r.AttackerWon) outcome = $"🏆 پیروزی مطلق {atk.Name}";
        else if (r.AttackerFailed) outcome = $"🛡 دفاع کامل {def.Name} — شکست حمله";
        else outcome = $"⚖️ موفقیت {r.SuccessPercent}٪ مهاجم";
        int h = r.DurationMinutes / 60, m = r.DurationMinutes % 60;
        string envLine = $"🌦 آب‌وهوا: {WeatherName[_weather]} | 🕓 آغاز نبرد: {TimeName[_startTime]}";
        string barAtk = $"{ProgressBar(frac, AttackerColor(r))} {r.SuccessPercent}٪";
        string barDef = $"{ProgressBar(frac, DefenderColor(r))} {r.SuccessPercent}٪";
        string TickTime(short t) { int mm = (int)(t * TICK_MIN); return $"{mm / 60}:{mm % 60:D2}"; }
        string firstContact = anyGround && defHasGround ? "تماس آتش در ساعات نخست برقرار شد" : "نبرد زمینی شکل نگرفت";
        string ambushLine = null, breakLine = null, shiftLine = null, routLine = null, haltLine = null, supplyLine = null;
        for (int i = 0; i < evtN; i++)
        {
            var e = _evts[i];
            switch (e.Kind)
            {
                case E_CONTACT: firstContact = $"نخستین تماس آتش در ساعت {TickTime(e.Tick)} در کیلومتر {e.A:F0} جبهه رخ داد"; break;
                case E_AMBUSH: ambushLine ??= $"در ساعت {TickTime(e.Tick)} کمین مدافع در عمق {e.B:F1} کیلومتری فعال شد و ستون پیشرو را درو کرد"; break;
                case E_BREAK5: breakLine ??= $"خط اول دفاع در ساعت {TickTime(e.Tick)} شکست و رخنه ۵ کیلومتری شکل گرفت"; break;
                case E_BREAK10: breakLine = $"در ساعت {TickTime(e.Tick)} رخنه به عمق ۱۰ کیلومتر توسعه یافت"; break;
                case E_BREAK20: breakLine = $"در ساعت {TickTime(e.Tick)} ستون زرهی مهاجم به عمق ۲۰ کیلومتری رسید"; break;
                case E_BREAK30: breakLine = $"در ساعت {TickTime(e.Tick)} عمق ۳۰ کیلومتر درنوردیده شد — فروپاشی جبهه"; break;
                case E_SHIFT: shiftLine ??= $"نقطه عطف نبرد در ساعت {TickTime(e.Tick)} رقم خورد و ابتکار عمل جابه‌جا شد"; break;
                case E_ROUT: routLine ??= e.A < 0.5f ? "چند گروه مهاجم با تلفات سنگین از خط گریختند" : "بخشی از یگان‌های مدافع تار و مار شدند"; break;
                case E_HALT: haltLine ??= $"پیشروی در عمق {e.A:F1} کیلومتری زمین‌گیر شد و جبهه به بن‌بست رسید"; break;
                case E_SUPPLY: supplyLine ??= $"در ساعت {TickTime(e.Tick)} کشش خط تدارکات، آهنگ پیشروی را کند کرد"; break;
            }
        }
        string airLine = BuildAirNarrative(air, aFight, aBomb, dFight, dAA, aAirStrat, aAirTac, aFs, aBs, dFs);
        string counterLine = counterAtk > 1.05f ? "انتخاب استراتژی مهاجم برابر دفاع دشمن، برتری تاکتیکی ایجاد کرد"
                           : counterAtk < 0.95f ? "استراتژی دفاعی دشمن، نقطه‌ضعف رویکرد مهاجم را هدف گرفت" : null;
        string armorLine = aTanks > 0
            ? $"زره‌پوش‌های {aSpec.Name} ستون فقرات حمله بودند؛ {r.AttackerTanksLost} دستگاه از {aTanks} نابود شد"
            : "مهاجم بدون پشتیبانی زرهی جنگید";
        string defArmorLine = dTanks > 0
            ? $"تانک‌های {dSpec.Name} مدافع {r.DefenderTanksLost} دستگاه از دست دادند"
            : "مدافع هیچ زرهی در خط نداشت";
        string infLine = $"پیاده‌نظام سنگین‌ترین تلفات را داد ({r.AttackerSoldiersLost + r.DefenderSoldiersLost} نفر در مجموع)";
        string intelLine = aIntelQ > dIntelQ + 0.12f
            ? "برتری شناسایی با مهاجم بود و آتش او دقیق‌تر نشست"
            : dIntelQ > aIntelQ + 0.12f
            ? "مه جنگ به سود مدافع کار کرد؛ مهاجم اغلب کورکورانه شلیک می‌کرد"
            : "هیچ طرفی برتری اطلاعاتی قاطع نداشت";
        string whyLine = r.AttackerWon
            ? "تمرکز قوا روی ضعیف‌ترین سکتور و توسعه سریع رخنه، کار دفاع را تمام کرد"
            : r.AttackerFailed
            ? "آتش دفاعی سازمان‌یافته و زمینِ مساعد، حمله را پیش از شکل‌گیری رخنه خفه کرد"
            : "هیچ طرف نتوانست ضربه قاطع بزند و نبرد با نتیجه‌ای نسبی پایان یافت";

        // ───────── گزارش مهاجم ─────────
        sb.Append("⚔️ گزارش نبرد — ").Append(atk.Name).Append(" علیه ").Append(def.Name).Append('\n');
        sb.Append(outcome).Append('\n');
        sb.Append(envLine).Append('\n');
        if (anyGround) sb.Append("📊 پیشروی: ").Append(barAtk).Append('\n');
        sb.Append('\n');
        sb.Append("📜 شرح نبرد:\n");
        sb.Append("• استراتژی زمینی: ").Append(aStratName).Append(" / ").Append(aTacName).Append('\n');
        if (aFight > 0 || aBomb > 0) sb.Append("• استراتژی هوایی: ").Append(aAirName).Append(" / ").Append(aAirTacName).Append('\n');
        if (airLine != null) sb.Append("• ").Append(airLine).Append('\n');
        if (anyGround && defHasGround)
        {
            sb.Append("• ").Append(firstContact).Append('\n');
            if (counterLine != null) sb.Append("• ").Append(counterLine).Append('\n');
            if (ambushLine != null) sb.Append("• ").Append(ambushLine).Append('\n');
            if (breakLine != null) sb.Append("• ").Append(breakLine).Append('\n');
            if (encircled) sb.Append("• حلقه محاصره بسته شد و فشار از چند جهت بر مدافع وارد آمد\n");
            if (supplyLine != null) sb.Append("• ").Append(supplyLine).Append('\n');
            if (shiftLine != null) sb.Append("• ").Append(shiftLine).Append('\n');
            if (routLine != null) sb.Append("• ").Append(routLine).Append('\n');
            if (haltLine != null) sb.Append("• ").Append(haltLine).Append('\n');
            sb.Append("• ").Append(armorLine).Append('\n');
            sb.Append("• ").Append(defArmorLine).Append('\n');
            sb.Append("• ").Append(infLine).Append('\n');
            sb.Append("• ").Append(intelLine).Append('\n');
            sb.Append("• ").Append(whyLine).Append('\n');
        }
        else if (!anyGround) sb.Append("• این یک عملیات کاملاً هوایی بود؛ نیروی زمینی اعزام نشد\n");
        else if (!defHasGround) sb.Append("• مدافع نیروی زمینی در خط نداشت و ستون مهاجم تقریباً بی‌مقاومت پیش رفت\n");
        sb.Append('\n');
        sb.Append("📊 آمار نهایی:\n");
        sb.Append($"🔻 تلفات خودی: {r.AttackerTanksLost} تانک، {r.AttackerSoldiersLost} سرباز");
        if (aFight > 0 || aBomb > 0) sb.Append($"، {r.AttackerFightersLost} جنگنده، {r.AttackerBombersLost} بمب‌افکن");
        sb.Append('\n');
        sb.Append($"🔻 تلفات دشمن: {r.DefenderTanksLost} تانک، {r.DefenderSoldiersLost} سرباز");
        if (dFight > 0 || dAA > 0) sb.Append($"، {r.DefenderFightersLost} جنگنده، {r.DefenderAntiAirLost} پدافند");
        sb.Append('\n');
        if (aFight > 0 || aBomb > 0 || dFight > 0 || dAA > 0)
            sb.Append($"🛫 برتری هوایی: {AirSupText(air.Superiority)}\n");
        if (anyGround)
            sb.Append($"📍 نفوذ موثر: {r.PenetrationKm:F1} کیلومتر ({r.SuccessPercent}٪)\n");
        if (anyGround && (r.AttackerMoneyGained > 0 || r.AttackerIronGained > 0))
            sb.Append($"💰 غنیمت (از پیشروی زمینی): {r.AttackerMoneyGained / 1000.0:F1}K پول، {r.AttackerIronGained / 1000.0:F1}K آهن\n");
        else if (anyGround)
            sb.Append("💰 غنیمت: بدون غنیمت (غارت فقط با پیشروی زمینی به‌دست می‌آید)\n");
        if (air.StratMoney > 0 || air.StratIron > 0)
            sb.Append($"🏭 خسارت بمباران به اقتصاد دشمن: {air.StratMoney / 1000.0:F1}K پول، {air.StratIron / 1000.0:F1}K آهن (نابود شد، غنیمت نیست)\n");
        sb.Append($"⏱ مدت نبرد: {h} ساعت و {m} دقیقه");
        r.AttackerReport = sb.ToString();

        // ───────── گزارش مدافع ─────────
        sb.Clear();
        sb.Append("🛡 گزارش دفاع — حمله ").Append(atk.Name).Append(" به ").Append(def.Name).Append('\n');
        sb.Append(outcome).Append('\n');
        sb.Append(envLine).Append('\n');
        if (anyGround) sb.Append("📊 پیشروی دشمن: ").Append(barDef).Append('\n');
        sb.Append('\n');
        sb.Append("📜 شرح نبرد:\n");
        sb.Append("• استراتژی دفاعی شما: ").Append(dStratName).Append(" / ").Append(dTacName).Append('\n');
        if (dFight > 0 || dAA > 0) sb.Append("• دفاع هوایی شما: ").Append(dAirName).Append(" / ").Append(dAirTacName).Append('\n');
        if (airLine != null) sb.Append("• ").Append(airLine).Append('\n');
        if (anyGround && defHasGround)
        {
            sb.Append("• ").Append(firstContact).Append('\n');
            if (ambushLine != null) sb.Append("• ").Append(ambushLine).Append('\n');
            if (breakLine != null) sb.Append("• ").Append(breakLine).Append('\n');
            if (encircled) sb.Append("• دشمن موفق شد حلقه محاصره را ببندد\n");
            if (supplyLine != null) sb.Append("• ").Append(supplyLine).Append('\n');
            if (routLine != null) sb.Append("• ").Append(routLine).Append('\n');
            if (haltLine != null) sb.Append("• ").Append(haltLine).Append('\n');
            sb.Append("• ").Append(defArmorLine).Append('\n');
            sb.Append("• ").Append(infLine).Append('\n');
            sb.Append("• ").Append(intelLine).Append('\n');
            sb.Append("• ").Append(whyLine).Append('\n');
        }
        else if (!anyGround) sb.Append("• حملهٔ دشمن کاملاً هوایی بود\n");
        else if (!defHasGround) sb.Append("• شما نیروی زمینی در خط نداشتید و دشمن آزادانه نفوذ کرد\n");
        sb.Append('\n');
        sb.Append("📊 آمار نهایی:\n");
        sb.Append($"🔻 تلفات خودی: {r.DefenderTanksLost} تانک، {r.DefenderSoldiersLost} سرباز");
        if (dFight > 0 || dAA > 0) sb.Append($"، {r.DefenderFightersLost} جنگنده، {r.DefenderAntiAirLost} پدافند");
        sb.Append('\n');
        sb.Append($"🔻 تلفات دشمن: {r.AttackerTanksLost} تانک، {r.AttackerSoldiersLost} سرباز");
        if (aFight > 0 || aBomb > 0) sb.Append($"، {r.AttackerFightersLost} جنگنده، {r.AttackerBombersLost} بمب‌افکن");
        sb.Append('\n');
        if (aFight > 0 || aBomb > 0 || dFight > 0 || dAA > 0)
            sb.Append($"🛫 برتری هوایی: {AirSupText(air.Superiority)}\n");
        if (anyGround)
            sb.Append($"📍 نفوذ دشمن: {r.PenetrationKm:F1} کیلومتر ({r.SuccessPercent}٪)\n");
        sb.Append($"💸 خسارت کل: {r.DefenderMoneyLost / 1000.0:F1}K پول، {r.DefenderIronLost / 1000.0:F1}K آهن\n");
        sb.Append($"⏱ مدت نبرد: {h} ساعت و {m} دقیقه");
        r.DefenderReport = sb.ToString();

        // ───────── اعلامیه گروه ─────────
        sb.Clear();
        sb.Append("📰 خبر جنگ!\n");
        sb.Append($"⚔️ {atk.Name} به {def.Name} حمله کرد!\n");
        sb.Append(outcome).Append('\n');
        sb.Append(anyGround ? $"🌦 {WeatherName[_weather]} | 📊 {barAtk}\n" : $"🌦 {WeatherName[_weather]} | 🛫 عملیات هوایی\n");
        if (aFight > 0 || aBomb > 0 || dFight > 0 || dAA > 0)
            sb.Append($"🛫 برتری هوایی: {AirSupText(air.Superiority)}\n");
        if (anyGround)
            sb.Append($"📍 نفوذ: {r.PenetrationKm:F1} کیلومتر | ⏱ {h}:{m:D2}\n");
        else
            sb.Append($"⏱ {h}:{m:D2}\n");
        sb.Append($"💀 تلفات مهاجم: {r.AttackerTanksLost}🛡 {r.AttackerSoldiersLost}🪖 {r.AttackerFightersLost}✈️ {r.AttackerBombersLost}🛩️\n");
        sb.Append($"💀 تلفات مدافع: {r.DefenderTanksLost}🛡 {r.DefenderSoldiersLost}🪖 {r.DefenderFightersLost}✈️ {r.DefenderAntiAirLost}🎯");
        if (r.AttackerMoneyGained > 0)
            sb.Append($"\n💰 غنیمت: {r.AttackerMoneyGained / 1000.0:F1}K پول، {r.AttackerIronGained / 1000.0:F1}K آهن");
        r.GroupAnnouncement = sb.ToString();
    }

    static string AirSupText(double sup)
    {
        if (sup > 0.4) return "قاطع با مهاجم 🟢";
        if (sup > 0.12) return "نسبی با مهاجم";
        if (sup < -0.4) return "قاطع با مدافع 🔴";
        if (sup < -0.12) return "نسبی با مدافع";
        return "متوازن ⚪";
    }

    static string BuildAirNarrative(AirOutcome air, long aFight, long aBomb, long dFight, long dAA,
        int aAirStrat, int aAirTac, FighterSpec aFs, BomberSpec aBs, FighterSpec dFs)
    {
        if (aFight == 0 && aBomb == 0 && dFight == 0 && dAA == 0) return null;
        var s = new StringBuilder();
        if (air.HadAirCombat)
            s.Append($"در نبرد هوا‌به‌هوا، جنگنده‌های {aFs.Name} مهاجم با {dFs.Name} مدافع درگیر شدند؛ ")
             .Append($"{air.AtkFightersLost} جنگندهٔ مهاجم و {air.DefFightersLost} جنگندهٔ مدافع سرنگون شد. ");
        else if (aFight > 0 && dFight == 0)
            s.Append($"جنگنده‌های {aFs.Name} مهاجم بدون مقاومت هوایی، آسمان را در اختیار گرفتند. ");
        if (dAA > 0 && (aBomb > 0 || aFight > 0))
            s.Append($"آتش پدافند ضدهوایی {air.AtkBombersLost} بمب‌افکن را سرنگون کرد و خود {air.DefAntiAirLost} قبضه از دست داد. ");
        if (aAirStrat == 2 && (air.StratMoney > 0 || air.StratIron > 0))
            s.Append(aAirTac == 1
                ? $"بمباران دقیق صنایع، خسارت سنگینی به اقتصاد دشمن زد ({air.StratMoney / 1000.0:F1}K پول، {air.StratIron / 1000.0:F1}K آهن). "
                : $"بمباران فرشی شهرها، زیرساخت و روحیهٔ دشمن را درهم کوبید ({air.StratMoney / 1000.0:F1}K پول). ");
        else if (aAirStrat == 1 && air.Superiority > 0.15)
            s.Append("با کسب برتری هوایی، پشتیبانی نزدیک هوایی به نفع پیشروی زمینی وارد عمل شد. ");
        else if (air.Superiority < -0.15)
            s.Append("برتری هوایی به دست مدافع افتاد و فشار هوایی بر مهاجم سنگینی کرد. ");
        return s.Length > 0 ? s.ToString().TrimEnd() : null;
    }

    // ═════════════════════════ نبرد دریایی –  ═════════════════════════════
    static BoatSpec BlendBoatSpecs(List<(string Model, long Count)> breakdown)
    {
        if (breakdown == null || breakdown.Count == 0) return BoatGermany;
        double total = breakdown.Sum(x => (double)x.Count);
        if (total <= 0) return BoatGermany;
        double speed=0, armor=0, torp=0, mg=0, crew=0, power=0;
        foreach (var (model, cnt) in breakdown)
        {
            var spec = GetBoatSpecByModel(model);
            double w = cnt / total;
            speed+=spec.Speed*w; armor+=spec.Armor*w; torp+=spec.Torpedo*w; mg+=spec.Mg*w; crew+=spec.Crew*w; power+=spec.Power*w;
        }
        // min 2% influence
        foreach (var (model, cnt) in breakdown)
        {
            if (cnt>0 && cnt/total <0.02)
            {
                var spec = GetBoatSpecByModel(model);
                speed = speed*0.98 + spec.Speed*0.02;
                power = power*0.98 + spec.Power*0.02;
            }
        }
        return new BoatSpec($"Blended({breakdown.Count})", (float)speed, (float)armor, (float)torp, (float)mg, (float)crew, (float)power);
    }
    static SubSpec BlendSubSpecs(List<(string Model, long Count)> breakdown)
    {
        if (breakdown == null || breakdown.Count == 0) return SubGermany;
        double total = breakdown.Sum(x => (double)x.Count);
        if (total <= 0) return SubGermany;
        double surf=0, sub=0, torp=0, gun=0, stealth=0, armor=0, power=0;
        foreach (var (model, cnt) in breakdown)
        {
            var spec = GetSubSpecByModel(model);
            double w = cnt / total;
            surf+=spec.SurfSpeed*w; sub+=spec.SubSpeed*w; torp+=spec.Torpedo*w; gun+=spec.Gun*w; stealth+=spec.Stealth*w; armor+=spec.Armor*w; power+=spec.Power*w;
        }
        return new SubSpec($"Blended({breakdown.Count})", (float)surf, (float)sub, (float)torp, (float)gun, (float)stealth, (float)armor, (float)power);
    }
    static BattleshipSpec BlendBattleshipSpecs(List<(string Model, long Count)> breakdown)
    {
        if (breakdown == null || breakdown.Count == 0) return BSGermany;
        double total = breakdown.Sum(x => (double)x.Count);
        if (total <= 0) return BSGermany;
        double speed=0, belt=0, deck=0, turret=0, main=0, sec=0, aa=0, crew=0, power=0;
        foreach (var (model, cnt) in breakdown)
        {
            var spec = GetBattleshipSpecByModel(model);
            double w = cnt / total;
            speed+=spec.Speed*w; belt+=spec.Belt*w; deck+=spec.Deck*w; turret+=spec.Turret*w; main+=spec.MainGuns*w; sec+=spec.SecGuns*w; aa+=spec.AAGuns*w; crew+=spec.Crew*w; power+=spec.Power*w;
        }
        return new BattleshipSpec($"Blended({breakdown.Count})", (float)speed, (float)belt, (float)deck, (float)turret, (float)main, (float)sec, (float)aa, (float)crew, 2f, (float)power);
    }

    static float CalculateNavalStrategyAdvantage(int aStrat, int aTac, int dStrat, int dTac, float aPower, float dPower, BoatSpec aBoat, SubSpec aSub, BattleshipSpec aBS, BoatSpec dBoat, SubSpec dSub, BattleshipSpec dBS, long aBoats, long aSubs, long aBSCount, long dBoats, long dSubs, long dBSCount, int defenderPortLevel, ref XorRng rng)
    {
        float adv = 1.0f;
        double totalA = aBoats + aSubs + aBSCount*10;
        double totalD = dBoats + dSubs + dBSCount*10;
        double ratio = totalA / Math.Max(1.0, totalD);

        // Strategy 1: نابودی ناوگان اصلی دشمن
        if (aStrat == 1)
        {
            if (aTac == 1) // حمله غافلگیرانه به پایگاه‌ها
            {
                // Bonus if defender in port (high port level but low mobility)
                adv += 0.15f;
                // If attacker has subs, surprise more effective
                if (aSubs > dSubs) adv += 0.08f;
                // If defender has coastal fortifications (port high), reduces advantage
                if (defenderPortLevel >= 4) adv -= 0.05f;
                // Stealth bonus
                adv += (aSub.Stealth - 70f) / 500f;
            }
            else // کشاندن به نبرد تعیین‌کننده
            {
                adv += 0.12f;
                if (aBSCount >= dBSCount) adv += 0.07f;
                // Battleship power matters
                adv += (aBS.Power - dBS.Power) / 1000f;
                adv += (aBoat.Speed - dBoat.Speed) / 1000f;
            }

            // Defender responses
            if (dStrat == 1) // استحکامات ساحلی
            {
                adv -= 0.08f;
                if (defenderPortLevel >= 4) adv -= 0.06f;
            }
            else if (dStrat == 2) // ضدحمله سریع / کمین
            {
                if (dTac == 1) // ضدحمله سریع – boats
                {
                    if (dBoats > aBoats) adv -= 0.07f;
                }
                else // کمین دریایی – subs
                {
                    if (dSubs > aSubs) adv -= 0.09f;
                    adv -= (dSub.Stealth - 70f) / 800f;
                }
            }
        }
        else // Strategy 2: عملیات آبی‌خاکی
        {
            if (aTac == 1) // بمباران دریایی
            {
                adv += 0.10f;
                // Battleship bombardment bonus
                adv += aBS.MainGuns * 0.015f;
                if (defenderPortLevel >= 3) adv -= 0.04f; // fortified port resists bombardment
            }
            else // پیاده‌سازی موجی
            {
                adv += 0.06f;
                // Wave landing resilient but needs boats
                if (aBoats >= 5) adv += 0.05f;
                adv += (aBoat.Speed / 500f);
            }

            if (dStrat == 1) // استحکامات
            {
                if (defenderPortLevel >= 4) adv -= 0.10f;
                else adv -= 0.05f;
            }
            else // متحرک
            {
                // Rapid counterattack reduces amphibious advantage
                adv -= 0.06f;
            }
        }

        adv += (float)Math.Clamp((ratio - 1.0) * 0.10, -0.15, 0.15);
        adv += rng.Range(-0.05f, 0.05f);
        return Math.Clamp(adv, 0.70f, 1.40f);
    }

    public static BattleResult RunNavalBattle(
        Country attacker, Country defender,
        long attBoats, long attSubs, long attBattleships,
        long defBoats, long defSubs, long defBattleships,
        int attStrategy, int attTactic)
    {
        var attBoatBreakdown = new List<(string Model, long Count)> { (GetDefaultBoatModel(attacker.Faction), attBoats) };
        var attSubBreakdown = new List<(string Model, long Count)> { (GetDefaultSubModel(attacker.Faction), attSubs) };
        var attBSBreakdown = new List<(string Model, long Count)> { (GetDefaultBattleshipModel(attacker.Faction), attBattleships) };
        var defBoatBreakdown = new List<(string Model, long Count)> { (GetDefaultBoatModel(defender.Faction), defBoats) };
        var defSubBreakdown = new List<(string Model, long Count)> { (GetDefaultSubModel(defender.Faction), defSubs) };
        var defBSBreakdown = new List<(string Model, long Count)> { (GetDefaultBattleshipModel(defender.Faction), defBattleships) };
        return RunNavalBattleAdvanced(attacker, defender, attBoatBreakdown, attSubBreakdown, attBSBreakdown, defBoatBreakdown, defSubBreakdown, defBSBreakdown, attStrategy, attTactic, 1, 1);
    }

    static string GetDefaultBoatModel(Faction f) => f switch { Faction.USA => "PT Boat", Faction.USSR => "G-5", _ => "S-Boot" };
    static string GetDefaultSubModel(Faction f) => f switch { Faction.USA => "Gato", Faction.USSR => "S-class", _ => "Type VIIC" };
    static string GetDefaultBattleshipModel(Faction f) => f switch { Faction.USA => "Iowa", Faction.USSR => "Sovetsky Soyuz", _ => "Bismarck" };

    public static BattleResult RunNavalBattleAdvanced(
        Country attacker, Country defender,
        List<(string Model, long Count)> attBoatBreakdown,
        List<(string Model, long Count)> attSubBreakdown,
        List<(string Model, long Count)> attBattleshipBreakdown,
        List<(string Model, long Count)> defBoatBreakdown,
        List<(string Model, long Count)> defSubBreakdown,
        List<(string Model, long Count)> defBattleshipBreakdown,
        int attStrategy, int attTactic, int defStrategy, int defTactic)
    {
        long seed = (long)Interlocked.Increment(ref _seedCounter) ^ ((long)attacker.OwnerId << 20) ^ DateTime.UtcNow.Ticks;
        var rng = new XorRng((ulong)seed);
        var res = new BattleResult { IsNavalBattle = true };

        long attBoats = attBoatBreakdown?.Sum(x => x.Count) ?? 0;
        long attSubs = attSubBreakdown?.Sum(x => x.Count) ?? 0;
        long attBS = attBattleshipBreakdown?.Sum(x => x.Count) ?? 0;
        long defBoats = defBoatBreakdown?.Sum(x => x.Count) ?? 0;
        long defSubs = defSubBreakdown?.Sum(x => x.Count) ?? 0;
        long defBS = defBattleshipBreakdown?.Sum(x => x.Count) ?? 0;

        attBoats = Math.Max(0, Math.Min(attBoats, attacker.Boats + attacker.BoatsAtSea));
        attSubs = Math.Max(0, Math.Min(attSubs, attacker.Submarines + attacker.SubmarinesAtSea));
        attBS = Math.Max(0, Math.Min(attBS, attacker.Battleships + attacker.BattleshipsAtSea));
        defBoats = Math.Max(0, Math.Min(defBoats, defender.Boats));
        defSubs = Math.Max(0, Math.Min(defSubs, defender.Submarines));
        defBS = Math.Max(0, Math.Min(defBS, defender.Battleships));

        if (attBoats + attSubs + attBS == 0)
        {
            res.AttackerReport = "⚓ هیچ نیروی دریایی برای حمله ندارید.";
            res.DefenderReport = $"⚓ {attacker.Name} بدون ناوگان حمله کرد – حمله خنثی شد.";
            res.GroupAnnouncement = $"⚓ {attacker.Name} تلاش ناموفق حمله دریایی به {defender.Name} داشت.";
            res.AttackerFailed = true;
            res.SuccessPercent = 0;
            return res;
        }

        var aBoatSpec = BlendBoatSpecs(attBoatBreakdown);
        var aSubSpec = BlendSubSpecs(attSubBreakdown);
        var aBSSpec = BlendBattleshipSpecs(attBattleshipBreakdown);
        var dBoatSpec = BlendBoatSpecs(defBoatBreakdown);
        var dSubSpec = BlendSubSpecs(defSubBreakdown);
        var dBSSpec = BlendBattleshipSpecs(defBattleshipBreakdown);

        float aPower = (float)(attBoats * aBoatSpec.Power + attSubs * aSubSpec.Power + attBS * aBSSpec.Power);
        float dPower = (float)(defBoats * dBoatSpec.Power + defSubs * dSubSpec.Power + defBS * dBSSpec.Power);
        if (dPower < 1) dPower = 1;

        float stratAdv = CalculateNavalStrategyAdvantage(attStrategy, attTactic, defStrategy, defTactic, aPower, dPower, aBoatSpec, aSubSpec, aBSSpec, dBoatSpec, dSubSpec, dBSSpec, attBoats, attSubs, attBS, defBoats, defSubs, defBS, defender.PortLevel, ref rng);

        float powerRatio = aPower / Math.Max(1f, dPower);
        float effectiveRatio = powerRatio * stratAdv;

        int success = (int)Math.Clamp(effectiveRatio * 50f, 0, 100); // 1.0 ratio => 50%, 2.0 => 100% etc
        // But ensure 0-100
        if (effectiveRatio > 2.0f) success = 95 + rng.Next(6);
        else if (effectiveRatio > 1.5f) success = 75 + rng.Next(20);
        else if (effectiveRatio > 1.0f) success = 55 + rng.Next(20);
        else if (effectiveRatio > 0.7f) success = 30 + rng.Next(25);
        else success = rng.Next(30);

        bool attackerWon = success >= 90 || (effectiveRatio > 1.2f && success >= 70);
        bool attackerFailed = success < 15 || effectiveRatio < 0.4f;

        // Loss calculations – advanced
        double attLossFactor = 0.15 + (1.0 - Math.Clamp(effectiveRatio, 0, 2) / 2.0) * 0.35; // if winning, lower loss
        double defLossFactor = 0.15 + Math.Clamp(effectiveRatio, 0, 2) / 2.0 * 0.45; // if attacker winning, defender high loss

        // Apply tactic modifiers
        if (attStrategy == 1 && attTactic == 1) // surprise base attack – defender higher initial loss
        {
            defLossFactor += 0.10;
            attLossFactor -= 0.05;
        }
        else if (attStrategy == 1 && attTactic == 2) // decisive battle – more mutual losses but attacker bonus if more BS
        {
            if (attBS >= defBS) { defLossFactor += 0.08; attLossFactor -= 0.03; }
        }
        else if (attStrategy == 2 && attTactic == 1) // bombardment – battleship consumption, less boat loss
        {
            attLossFactor = Math.Max(0.08, attLossFactor - 0.07);
            defLossFactor += 0.12;
        }
        else if (attStrategy == 2 && attTactic == 2) // wave landing – resilient
        {
            attLossFactor *= 0.85;
            defLossFactor *= 0.95;
        }

        // Defender tactics
        if (defStrategy == 1) // coastal fortifications – attacker higher loss
        {
            attLossFactor += 0.07;
            defLossFactor -= 0.05;
        }
        else if (defStrategy == 2 && defTactic == 2) // ambush – attacker higher loss if subs
        {
            if (defSubs > 0) attLossFactor += 0.10;
        }

        long attBoatLoss = (long)Math.Round(attBoats * Math.Clamp(attLossFactor * (0.8 + rng.Range(0f,0.4f)), 0.02, 0.9));
        long attSubLoss = (long)Math.Round(attSubs * Math.Clamp(attLossFactor * (0.8 + rng.Range(0f,0.4f)), 0.02, 0.85));
        long defBoatLoss = (long)Math.Round(defBoats * Math.Clamp(defLossFactor * (0.8 + rng.Range(0f,0.4f)), 0.02, 0.95));
        long defSubLoss = (long)Math.Round(defSubs * Math.Clamp(defLossFactor * (0.8 + rng.Range(0f,0.4f)), 0.02, 0.90));

        // Battleship damage handling –  core: damage-only unless one-sided destroy
        bool oneSided = effectiveRatio > 2.5f || effectiveRatio < 0.4f;
        long attBSDamage = 0, defBSDamage = 0;
        long attBSLost = 0, defBSLost = 0;

        // Attacker battleship damage
        if (attBS > 0)
        {
            double dmgPctPerShip = attLossFactor * 60.0; // up to 60% damage per ship if losing badly
            if (attStrategy == 2 && attTactic == 1) dmgPctPerShip *= 1.2; // bombardment stresses battleships
            long totalDamage = (long)(attBS * dmgPctPerShip);
            if (oneSided && effectiveRatio < 0.5f) // attacker badly losing, some BS may be destroyed
            {
                attBSLost = Math.Min(attBS, (long)Math.Ceiling(totalDamage / 100.0 * 0.5));
                attBSDamage = totalDamage - attBSLost * 100;
                if (attBSDamage <0) attBSDamage=0;
            }
            else
            {
                attBSDamage = totalDamage;
            }
        }
        // Defender battleship damage
        if (defBS > 0)
        {
            double dmgPctPerShip = defLossFactor * 65.0;
            long totalDamage = (long)(defBS * dmgPctPerShip);
            if (oneSided && effectiveRatio > 2.5f) // attacker decisive win, defender BS may be destroyed
            {
                defBSLost = Math.Min(defBS, (long)Math.Ceiling(totalDamage / 100.0 * 0.6));
                defBSDamage = totalDamage - defBSLost * 100;
                if (defBSDamage <0) defBSDamage=0;
            }
            else
            {
                defBSDamage = totalDamage;
            }
        }

        // Loot – 1.5x ground 100% success (ground loot 15% money 10% iron * frac)
        float frac = success / 100f;
        long baseMoneyLoot = (long)(defender.Money * 0.15 * frac);
        long baseIronLoot = (long)(defender.Iron * 0.10 * frac);
        long lootMoney = (long)(baseMoneyLoot * 1.5); // 1.5x
        long lootIron = (long)(baseIronLoot * 1.5);
        lootMoney = Math.Min(lootMoney, defender.Money);
        lootIron = Math.Min(lootIron, defender.Iron);

        // Reports – dynamic intelligent Persian reports
        string attStratName = attStrategy == 1 ? "نابودی ناوگان اصلی دشمن" : "عملیات آبی‌خاکی و تهاجم ساحلی";
        string attTacName = (attStrategy, attTactic) switch
        {
            (1,1) => "حمله غافلگیرانه به پایگاه‌های دریایی",
            (1,2) => "کشاندن ناوگان دشمن به نبرد تعیین‌کننده",
            (2,1) => "بمباران دریایی",
            (2,2) => "پیاده‌سازی موجی نیروها",
            _ => "نامشخص"
        };
        string defStratName = defStrategy == 1 ? "استحکامات و موانع ساحلی" : "دفاع متحرک (ضدحمله سریع / کمین دریایی)";
        string defTacName = (defStrategy, defTactic) switch
        {
            (1,1) => "استحکامات و موانع ساحلی",
            (1,2) => "بمباران متقابل ساحلی",
            (2,1) => "ضدحمله سریع",
            (2,2) => "کمین دریایی / حمله و عقب‌نشینی",
            _ => "نامشخص"
        };

        string outcome;
        if (attackerWon) outcome = success >= 90 ? $"🏆 پیروزی دریایی قاطع {attacker.Name} – بندر دشمن در آستانه سقوط!" : $"⚓ پیروزی دریایی {attacker.Name}";
        else if (attackerFailed) outcome = $"🛡 دفاع دریایی کامل {defender.Name}";
        else outcome = $"⚖️ نبرد دریایی نیمه‌کاره – موفقیت {success}%";

        var sb = new StringBuilder();
        sb.AppendLine($"⚔️ گزارش نبرد دریایی – {attacker.Name} vs {defender.Name}");
        sb.AppendLine(outcome);
        sb.AppendLine($"🎯 استراتژی مهاجم: {attStratName} / {attTacName} – مزیت: {stratAdv:F2}x");
        sb.AppendLine($"🛡 استراتژی مدافع: {defStratName} / {defTacName}");
        sb.AppendLine($"⚖️ نسبت قدرت: {powerRatio:F2} (با تاکتیک {effectiveRatio:F2})");
        sb.AppendLine($"🔧 ترکیب مهاجم: {attBoats}🚤 {attSubs}⚓ {attBS}🚢 | مدافع: {defBoats}🚤 {defSubs}⚓ {defBS}🚢");
        if (attBoatBreakdown != null && attBoatBreakdown.Count > 1) sb.AppendLine($"• تنوع قایق مهاجم: {attBoatBreakdown.Count} مدل");
        if (attBattleshipBreakdown != null && attBattleshipBreakdown.Count > 0) sb.AppendLine($"• نبردناو مهاجم: {string.Join(", ", attBattleshipBreakdown.Select(b=>$"{b.Model}({b.Count})"))}");
        if (defBattleshipBreakdown != null && defBattleshipBreakdown.Count > 0) sb.AppendLine($"• نبردناو مدافع: {string.Join(", ", defBattleshipBreakdown.Select(b=>$"{b.Model}({b.Count})"))}");

        sb.AppendLine();
        sb.AppendLine("📜 تحلیل تاکتیکی هوشمند:");
        if (attStrategy == 1 && attTactic == 1) sb.AppendLine("• حمله غافلگیرانه در لحظه لنگر انداختن ناوگان دشمن اجرا شد – آسیب اولیه بالا");
        if (attStrategy == 1 && attTactic == 2) sb.AppendLine("• با مانور فریب، ناوگان دشمن به آب‌های آزاد کشانده شد و در نبرد متمرکز درهم کوبیده شد");
        if (attStrategy == 2 && attTactic == 1) sb.AppendLine("• نبردناوها با آتش سنگین 380-406mm مواضع ساحلی را قبل از پیاده‌سازی کوبیدند");
        if (attStrategy == 2 && attTactic == 2) sb.AppendLine("• پیاده‌سازی موجی باعث ایجاد سرپل پایدار شد – هر موج جای پای موج قبلی را محکم کرد");

        if (defStrategy == 1) sb.AppendLine("• مدافع با مین‌های دریایی، توپخانه ساحلی و موانع، پیشروی را کند کرد");
        if (defStrategy == 2 && defTactic == 1) sb.AppendLine("• قایق‌های تندرو و زیردریایی‌ها ضدحمله برق‌آسا اجرا کردند");
        if (defStrategy == 2 && defTactic == 2) sb.AppendLine("• کمین زیردریایی‌ها در تنگه‌های کم‌عمق، ستون مهاجم را غافلگیر کرد");

        if (oneSided && attackerWon) sb.AppendLine("• نبرد یک‌طرفه بود – نبردناوهای دشمن به طور کامل منهدم شدند نه فقط آسیب دیدند");
        else sb.AppendLine("• نبردناوها فقط آسیب دیدند و برای تعمیر به حوضچه خشک نیاز دارند – از دستور «تعمیر ناو» استفاده کنید");

        sb.AppendLine();
        sb.AppendLine("📊 آمار نهایی:");
        sb.AppendLine($"🔻 مهاجم: {attBoatLoss} قایق، {attSubLoss} زیردریایی، {attBSLost} نبردناو منهدم، {attBSDamage}% آسیب نبردناو");
        sb.AppendLine($"🔻 مدافع: {defBoatLoss} قایق، {defSubLoss} زیردریایی، {defBSLost} نبردناو منهدم، {defBSDamage}% آسیب نبردناو");
        sb.AppendLine($"💰 غنیمت دریایی (1.5x زمینی): {lootMoney/1000.0:F1}K پول، {lootIron/1000.0:F1}K آهن");
        if (success >= 90) sb.AppendLine($"⚠️ بندر {defender.Name} با سقوط {success}% یک سطح کاهش می‌یابد!");
        sb.AppendLine($"⏱ مدت نبرد: {(int)(15 + effectiveRatio * 20)} دقیقه | 🌊 سوخت قایق‌ها تمام شد و به بندر بازگشتند");

        res.AttackerReport = sb.ToString();

        var sbDef = new StringBuilder();
        sbDef.AppendLine($"🛡 گزارش دفاع دریایی – {defender.Name} vs {attacker.Name}");
        sbDef.AppendLine(outcome);
        sbDef.AppendLine($"🎯 مهاجم: {attStratName}/{attTacName} – مزیت {stratAdv:F2}x");
        sbDef.AppendLine($"🛡 دفاع شما: {defStratName}/{defTacName}");
        sbDef.AppendLine($"📊 تلفات شما: {defBoatLoss}🚤 {defSubLoss}⚓ {defBSLost}🚢 منهدم + {defBSDamage}% آسیب");
        sbDef.AppendLine($"📊 تلفات دشمن: {attBoatLoss}🚤 {attSubLoss}⚓ {attBSLost}🚢 + {attBSDamage}% آسیب");
        sbDef.AppendLine($"💸 خسارت: {lootMoney/1000.0:F1}K پول، {lootIron/1000.0:F1}K آهن");
        if (success >= 90) sbDef.AppendLine("🆘 هشدار: بندر شما به دلیل شکست سنگین دریایی یک سطح سقوط کرد!");
        sbDef.AppendLine("🚤 قایق‌های شما در صورت داشتن سوخت به گشت ادامه می‌دهند.");
        res.DefenderReport = sbDef.ToString();

        var sbGrp = new StringBuilder();
        sbGrp.AppendLine("📰 خبر جنگ دریایی!");
        sbGrp.AppendLine($"⚓ {attacker.Name} به {defender.Name} در دریا حمله کرد!");
        sbGrp.AppendLine(outcome);
        sbGrp.AppendLine($"📊 موفقیت: {success}% | {attStratName}");
        sbGrp.AppendLine($"💀 مهاجم: {attBoatLoss}🚤 {attSubLoss}⚓ {attBSLost}🚢 | مدافع: {defBoatLoss}🚤 {defSubLoss}⚓ {defBSLost}🚢");
        sbGrp.AppendLine($"💰 غنیمت: {lootMoney/1000.0:F1}K / {lootIron/1000.0:F1}K | 🌊 سوخت قایق اتمام");
        if (success >=90) sbGrp.AppendLine($"⚓ بندر {defender.Name} یک سطح کاهش یافت!");
        res.GroupAnnouncement = sbGrp.ToString();

        res.AttackerBoatsLost = attBoatLoss;
        res.AttackerSubsLost = attSubLoss;
        res.AttackerBattleshipsLost = attBSLost;
        res.AttackerBattleshipDamage = attBSDamage;
        res.DefenderBoatsLost = defBoatLoss;
        res.DefenderSubsLost = defSubLoss;
        res.DefenderBattleshipsLost = defBSLost;
        res.DefenderBattleshipDamage = defBSDamage;
        res.AttackerMoneyGained = lootMoney;
        res.AttackerIronGained = lootIron;
        res.DefenderMoneyLost = lootMoney;
        res.DefenderIronLost = lootIron;
        res.SuccessPercent = success;
        res.AttackerWon = attackerWon;
        res.AttackerFailed = attackerFailed;
        res.PenetrationKm = success; // reuse as naval penetration
        res.DurationMinutes = (int)(15 + effectiveRatio * 20);
        res.AttackerBoatsSurvived = attBoats - attBoatLoss;
        res.AttackerSubsSurvived = attSubs - attSubLoss;
        res.AttackerBattleshipsSurvived = attBS - attBSLost;

        SaveBattle(attacker, defender, res);
        return res;
    }

    // ═════════════════════════ ذخیره در دیتابیس ═════════════════════════════
    static int _dbReady;
    static void SaveBattle(Country atk, Country def, BattleResult r)
    {
        try
        {
            using var con = new SqliteConnection("Data Source=gamedata.db");
            con.Open();
            if (Interlocked.CompareExchange(ref _dbReady, 1, 0) == 0)
            {
                using var init = con.CreateCommand();
                init.CommandText = @"
                CREATE TABLE IF NOT EXISTS WarBattles(
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Timestamp TEXT NOT NULL,
                    ChatId INTEGER, AttackerId INTEGER, DefenderId INTEGER,
                    AttackerName TEXT, DefenderName TEXT,
                    Winner TEXT, PenetrationKm REAL, SuccessPercent INTEGER,
                    AtkTankLoss INTEGER, AtkSoldierLoss INTEGER,
                    DefTankLoss INTEGER, DefSoldierLoss INTEGER,
                    LootMoney INTEGER, LootIron INTEGER,
                    DurationMinutes INTEGER, Report TEXT
                );";
                init.ExecuteNonQuery();
            }
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"INSERT INTO WarBattles
                (Timestamp,ChatId,AttackerId,DefenderId,AttackerName,DefenderName,Winner,
                 PenetrationKm,SuccessPercent,AtkTankLoss,AtkSoldierLoss,DefTankLoss,DefSoldierLoss,
                 LootMoney,LootIron,DurationMinutes,Report)
                VALUES (@ts,@chat,@aid,@did,@an,@dn,@w,@pen,@sp,@atl,@asl,@dtl,@dsl,@lm,@li,@dur,@rep)";
            cmd.Parameters.AddWithValue("@ts", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@chat", atk.ChatId);
            cmd.Parameters.AddWithValue("@aid", atk.OwnerId);
            cmd.Parameters.AddWithValue("@did", def.OwnerId);
            cmd.Parameters.AddWithValue("@an", atk.Name);
            cmd.Parameters.AddWithValue("@dn", def.Name);
            cmd.Parameters.AddWithValue("@w", r.AttackerWon ? atk.Name : r.AttackerFailed ? def.Name : $"نسبی {r.SuccessPercent}%");
            cmd.Parameters.AddWithValue("@pen", r.PenetrationKm);
            cmd.Parameters.AddWithValue("@sp", r.SuccessPercent);
            cmd.Parameters.AddWithValue("@atl", r.AttackerTanksLost);
            cmd.Parameters.AddWithValue("@asl", r.AttackerSoldiersLost);
            cmd.Parameters.AddWithValue("@dtl", r.DefenderTanksLost);
            cmd.Parameters.AddWithValue("@dsl", r.DefenderSoldiersLost);
            cmd.Parameters.AddWithValue("@lm", r.AttackerMoneyGained);
            cmd.Parameters.AddWithValue("@li", r.AttackerIronGained);
            cmd.Parameters.AddWithValue("@dur", r.DurationMinutes);
            cmd.Parameters.AddWithValue("@rep", r.AttackerReport);
            cmd.ExecuteNonQuery();
        }
        catch { }
    }
}
