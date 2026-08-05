// ============================================================================
//  WarEngine.cs  —  موتور نبرد ترکیبی نسخه ۳ (Combined-Arms Battle Engine v3)
// ============================================================================
//  تغییرات نسخه ۳:
//    • مغز فرماندهی مستقل برای هر استراتژی/تاکتیک (۴ دکترین مهاجم + ۴ دکترین مدافع)
//    • محاسبه‌ی تلفات به تفکیک هر مدل تانک (نه میانگین‌گیری از مدل‌ها)
//    • لایه‌ی فکشن: کیفیت خدمه + جریمه‌ی تجهیزات خارجی + بازیابی تجهیزات
//    • گزارش‌های کاملاً بازنویسی‌شده (گروه / مهاجم / مدافع) با خط زمانی تصمیمات
//    • حذف کامل سوخت دریایی — قایق فقط نیروی پدافند ساحلی است
//
//  نقاط ورود عمومی (بدون تغییر نسبت به نسخه‌ی قبل):
//    WarEngine.RunBattle(...)
//    WarEngine.RunBattleAdvanced(...)
//    WarEngine.RunNavalBattleAdvanced(...)
//    WarEngine.RunBattlesParallel(...)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

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

    // تلفات دریایی
    public long AttackerBoatsLost;
    public long AttackerSubsLost;
    public long AttackerBattleshipsLost;
    public long AttackerBattleshipDamage;
    public long DefenderBoatsLost;
    public long DefenderSubsLost;
    public long DefenderBattleshipsLost;
    public long DefenderBattleshipDamage;
    public bool IsNavalBattle;
    public long AttackerBoatsSurvived;
    public long AttackerSubsSurvived;
    public long AttackerBattleshipsSurvived;

    // تلفات به تفکیک مدل (کلید: نام مدل)
    public Dictionary<string, long> AttackerTankLossByModel = new();
    public Dictionary<string, long> DefenderTankLossByModel = new();
    public Dictionary<string, long> AttackerPlaneLossByModel = new();
    public Dictionary<string, long> DefenderPlaneLossByModel = new();
    public Dictionary<string, long> AttackerBomberLossByModel = new();
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
    const int   MAX_TICKS = 240;
    const int   MAX_GROUPS = 224;
    const int   INF_GROUP = 100;
    const int   TANK_GROUP = 10;
    const int   SECTORS = 10;
    const float ZOC_R = 2.4f;                 // شعاع منطقه‌ی کنترل (کیلومتر)
    const float BRAKE_THR = 0.62f;            // آستانه‌ی برتری محلی برای شکستن خط
    const float BRAKE_SPAN = 0.30f;
    const float SECTOR_DOM = 0.62f;           // برتری لازم در سکتور برای تثبیت زمین
    const float GLOBAL_DOM = 0.42f;           // برتری کلی لازم برای تثبیت رخنه
    const float ENTRENCH = 0.35f;             // کاهش تلفات مدافعِ سنگرگرفته
    const float DUGIN_ACC = 1.20f;            // دقت بیشتر آتش از سنگر
    const float FALLBACK_P = 0.06f;           // احتمال عقب‌نشینی و سنگرگیری مجدد مدافع
    const float ZOC_R2 = ZOC_R * ZOC_R;
    const float SECTOR_KM = FRONT_KM / SECTORS;

    // انواع زمین
    const byte T_PLAIN = 0, T_HILL = 1, T_FOREST = 2, T_URBAN = 3, T_MARSH = 4, T_RIDGE = 5;
    static readonly string[] TerName = { "دشت باز", "تپه‌ماهور", "جنگل", "منطقه شهری", "باتلاق", "یال مرتفع" };
    static readonly float[] TerSpeed  = { 1.00f, 0.74f, 0.58f, 0.62f, 0.44f, 0.68f };
    static readonly float[] TerCover  = { 0.00f, 0.24f, 0.52f, 0.62f, 0.14f, 0.33f };
    static readonly float[] TerAcc    = { 1.00f, 0.92f, 0.72f, 0.68f, 0.95f, 0.93f };
    static readonly float[] TerVision = { 1.00f, 1.35f, 0.55f, 0.60f, 1.00f, 1.50f };

    // وضعیت‌های گروه
    const byte P_ADVANCE = 0, P_ASSAULT = 1, P_DEFEND = 2, P_AMBUSH = 3,
               P_PATROL = 4, P_RETREAT = 5, P_FLANK = 6, P_HOLD = 7, P_SCREEN = 8, P_REGROUP = 9;

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

    // ═══════════════════ مشخصات فنی واحدها (به تفکیک مدل) ═══════════════════
    public readonly struct TankSpec
    {
        public readonly string Name;
        public readonly Faction Origin;
        public readonly float Pen, He, Mg, Armor, Speed, CannonAmmo, MgAmmo, Reliab;
        public TankSpec(string n, Faction origin, float p, float he, float mg, float ar, float sp, float ca, float ma, float rel)
        { Name = n; Origin = origin; Pen = p; He = he; Mg = mg; Armor = ar; Speed = sp; CannonAmmo = ca; MgAmmo = ma; Reliab = rel; }
    }

    // M2 Medium: توپ ۳۷ سبک، مسلسل فراوان، زره نازک، سریع و بسیار قابل‌اعتماد
    static readonly TankSpec SpecUSA   = new("M2 Medium",  Faction.USA,   46f, 0.45f, 7f, 30f, 42f, 100f, 90f, 0.95f);
    // T-28: توپ ۷۶ با گلوله انفجاری قوی، زره ضخیم‌تر، کند و کم‌اعتماد
    static readonly TankSpec SpecUSSR  = new("T-28",       Faction.USSR,  40f, 1.00f, 4f, 80f, 37f,  70f, 60f, 0.82f);
    // Panzer III: توپ ۵۰ با نفوذ بالا، اپتیک عالی، متعادل
    static readonly TankSpec SpecReich = new("Panzer III", Faction.Reich, 67f, 0.55f, 3f, 60f, 40f,  84f, 55f, 0.97f);

    static TankSpec SpecOf(Faction f) => f == Faction.USA ? SpecUSA : f == Faction.USSR ? SpecUSSR : SpecReich;

    public readonly struct FighterSpec
    {
        public readonly string Name;
        public readonly Faction Origin;
        public readonly float Maneuver, Firepower, Speed, Cas;
        public FighterSpec(string n, Faction origin, float mn, float fp, float sp, float cas)
        { Name = n; Origin = origin; Maneuver = mn; Firepower = fp; Speed = sp; Cas = cas; }
    }
    static readonly FighterSpec FighterUSA   = new("P-36",   Faction.USA,   9f, 4.5f, 500f, 0.9f);
    static readonly FighterSpec FighterUSSR  = new("I-16",   Faction.USSR,  9f, 4.0f, 520f, 0.8f);
    static readonly FighterSpec FighterReich = new("Bf 109", Faction.Reich, 8f, 8.0f, 570f, 1.0f);
    static FighterSpec FighterOf(Faction f) => f == Faction.USA ? FighterUSA : f == Faction.USSR ? FighterUSSR : FighterReich;

    public readonly struct BomberSpec
    {
        public readonly string Name;
        public readonly Faction Origin;
        public readonly float Armor, DefMg, Bombload, Speed;
        public BomberSpec(string n, Faction origin, float ar, float dmg, float bl, float sp)
        { Name = n; Origin = origin; Armor = ar; DefMg = dmg; Bombload = bl; Speed = sp; }
    }
    static readonly BomberSpec BomberUSA   = new("B-17",   Faction.USA,   8f, 6f, 3600f, 460f);
    static readonly BomberSpec BomberReich = new("He 111", Faction.Reich, 5f, 4f, 2000f, 435f);
    static readonly BomberSpec BomberUSSR  = new("DB-3",   Faction.USSR,  3f, 3f, 1000f, 430f);
    static BomberSpec BomberOf(Faction f) => f == Faction.USA ? BomberUSA : f == Faction.USSR ? BomberUSSR : BomberReich;

    // ───────────────────────── مشخصات دریایی (بدون سوخت) ─────────────────────
    public readonly struct BoatSpec
    {
        public readonly string Name;
        public readonly Faction Origin;
        public readonly float Speed, Armor, Torpedo, Mg, Crew, Power;
        public BoatSpec(string n, Faction origin, float speed, float armor, float torp, float mg, float crew, float power)
        { Name = n; Origin = origin; Speed = speed; Armor = armor; Torpedo = torp; Mg = mg; Crew = crew; Power = power; }
    }
    static readonly BoatSpec BoatGermany = new("S-Boot",  Faction.Reich, 39.5f, 5f, 18f, 4f, 22f, 12f);
    static readonly BoatSpec BoatUSA     = new("PT Boat", Faction.USA,   42f,   3f, 14f, 6f, 12f, 10f);
    static readonly BoatSpec BoatUSSR    = new("G-5",     Faction.USSR,  51f,   2f, 16f, 2f,  6f,  9f);
    static BoatSpec BoatOf(Faction f) => f == Faction.USA ? BoatUSA : f == Faction.USSR ? BoatUSSR : BoatGermany;

    public readonly struct SubSpec
    {
        public readonly string Name;
        public readonly Faction Origin;
        public readonly float SurfSpeed, SubSpeed, Torpedo, Gun, Stealth, Armor, Power;
        public SubSpec(string n, Faction origin, float surf, float sub, float torp, float gun, float stealth, float armor, float power)
        { Name = n; Origin = origin; SurfSpeed = surf; SubSpeed = sub; Torpedo = torp; Gun = gun; Stealth = stealth; Armor = armor; Power = power; }
    }
    static readonly SubSpec SubGermany = new("Type VIIC", Faction.Reich, 17.7f, 7.6f, 35f, 8f, 85f, 18f, 28f);
    static readonly SubSpec SubUSA     = new("Gato",      Faction.USA,   21f,   9f,   45f, 7f, 80f, 15f, 32f);
    static readonly SubSpec SubUSSR    = new("S-class",   Faction.USSR,  13.5f, 7.5f, 25f, 5f, 75f, 12f, 22f);
    static SubSpec SubOf(Faction f) => f == Faction.USA ? SubUSA : f == Faction.USSR ? SubUSSR : SubGermany;

    public readonly struct BattleshipSpec
    {
        public readonly string Name;
        public readonly Faction Origin;
        public readonly float Speed, Belt, Deck, Turret, MainGuns, SecGuns, AAGuns, Crew, UnitsBuilt, Power;
        public BattleshipSpec(string n, Faction origin, float speed, float belt, float deck, float turret, float mainGuns, float sec, float aa, float crew, float built, float power)
        { Name = n; Origin = origin; Speed = speed; Belt = belt; Deck = deck; Turret = turret; MainGuns = mainGuns; SecGuns = sec; AAGuns = aa; Crew = crew; UnitsBuilt = built; Power = power; }
    }
    static readonly BattleshipSpec BSGermany = new("Bismarck",        Faction.Reich, 30f, 320f, 110f, 360f,  8f, 12f, 44f, 2092f, 2f, 180f);
    static readonly BattleshipSpec BSUSA     = new("Iowa",            Faction.USA,   28f, 305f, 140f, 406f,  9f, 20f, 34f, 1800f, 6f, 195f);
    static readonly BattleshipSpec BSUSSR    = new("Sovetsky Soyuz",  Faction.USSR,  23f, 225f,  62f, 203f, 12f, 16f, 18f, 1220f, 4f, 150f);
    static BattleshipSpec BattleshipOf(Faction f) => f == Faction.USA ? BSUSA : f == Faction.USSR ? BSUSSR : BSGermany;

    // ─────────────────────── نگاشت نام مدل → مشخصات ─────────────────────────
    public static TankSpec GetTankSpecByModel(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return SpecUSA;
        string m = modelName.ToLowerInvariant();
        if (m.Contains("t-28") || m.Contains("t28") || m.Contains("t-34") || m.Contains("t34") || m.Contains("kv")) return SpecUSSR;
        if (m.Contains("m2") || m.Contains("m4") || m.Contains("sherman") || m.Contains("lee") || m.Contains("grant")) return SpecUSA;
        if (m.Contains("panzer") || m.Contains("pz") || m.Contains("tiger") || m.Contains("panther")) return SpecReich;
        if (m.Contains("usa") || m.Contains("american") || m.Contains("امریکا") || m.Contains("آمریکا")) return SpecUSA;
        if (m.Contains("ussr") || m.Contains("soviet") || m.Contains("شوروی")) return SpecUSSR;
        return SpecReich;
    }

    public static FighterSpec GetFighterSpecByModel(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return FighterUSA;
        string m = modelName.ToLowerInvariant();
        if (m.Contains("i-16") || m.Contains("i16") || m.Contains("yak") || m.Contains("lagg") || m.Contains("mig")) return FighterUSSR;
        if (m.Contains("p-36") || m.Contains("p36") || m.Contains("p-51") || m.Contains("p-40") || m.Contains("mustang")) return FighterUSA;
        if (m.Contains("bf") || m.Contains("109") || m.Contains("fw")) return FighterReich;
        return FighterReich;
    }

    public static BomberSpec GetBomberSpecByModel(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return BomberUSA;
        string m = modelName.ToLowerInvariant();
        if (m.Contains("db") || m.Contains("pe-2") || m.Contains("pe2") || m.Contains("il-4")) return BomberUSSR;
        if (m.Contains("b-17") || m.Contains("b17") || m.Contains("b-24")) return BomberUSA;
        if (m.Contains("he") || m.Contains("ju") || m.Contains("do")) return BomberReich;
        return BomberUSA;
    }

    public static BoatSpec GetBoatSpecByModel(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return BoatGermany;
        string m = modelName.ToLowerInvariant();
        if (m.Contains("pt")) return BoatUSA;
        if (m.Contains("g-5") || m.Contains("g5")) return BoatUSSR;
        return BoatGermany;
    }

    public static SubSpec GetSubSpecByModel(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return SubGermany;
        string m = modelName.ToLowerInvariant();
        if (m.Contains("gato")) return SubUSA;
        if (m.Contains("s-class") || m.Contains("s class") || m.Contains("series ix")) return SubUSSR;
        return SubGermany;
    }

    public static BattleshipSpec GetBattleshipSpecByModel(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return BSGermany;
        string m = modelName.ToLowerInvariant();
        if (m.Contains("iowa")) return BSUSA;
        if (m.Contains("sovetsky") || m.Contains("soyuz")) return BSUSSR;
        return BSGermany;
    }

    static string FactionFa(Faction f) => f == Faction.USA ? "آمریکایی" : f == Faction.USSR ? "شوروی" : "آلمانی";

    // ═════════════════ پروفایل فکشن: خدمه، تمپو، تدارکات، بازیابی ════════════
    readonly struct FactionProfile
    {
        public readonly float CrewQuality;    // کیفیت آموزش خدمه (ضریب آتش)
        public readonly int   CommandTempo;   // هر چند تیک یک‌بار فرمانده تصمیم می‌گیرد (کمتر = چابک‌تر)
        public readonly float SupplyFloor;    // کف کارایی تدارکات در عمق زیاد
        public readonly float Recovery;       // نرخ بازیابی تجهیزات از کار افتاده در صورت تسلط بر میدان
        public readonly float MoraleResist;   // مقاومت روحیه در برابر تلفات
        public readonly float ForeignAdapt;   // توان کار با تجهیزات بیگانه
        public readonly float NightSkill;     // کارایی در شب
        public readonly string Doctrine;      // توضیح فارسی
        public FactionProfile(float crew, int tempo, float supply, float rec, float mor, float foreign, float night, string doc)
        { CrewQuality = crew; CommandTempo = tempo; SupplyFloor = supply; Recovery = rec; MoraleResist = mor; ForeignAdapt = foreign; NightSkill = night; Doctrine = doc; }
    }

    static FactionProfile ProfileOf(Faction f) => f switch
    {
        Faction.Reich => new FactionProfile(1.08f, 3, 0.74f, 0.38f, 1.00f, 0.92f, 0.95f,
            "فرماندهی مأموریت‌محور (Auftragstaktik): تصمیم‌گیری سریع در سطح یگان، تمرکز ضربه، ضعف در جنگ فرسایشی طولانی"),
        Faction.USA => new FactionProfile(1.03f, 4, 0.84f, 0.46f, 1.02f, 0.94f, 0.90f,
            "برتری تدارکاتی و تعمیرگاهی: خط رسانی پایدار، بازیابی بالای تجهیزات آسیب‌دیده، هماهنگی خوب زمین و هوا"),
        _ => new FactionProfile(0.98f, 5, 0.70f, 0.30f, 1.18f, 0.88f, 1.00f,
            "عمق و پایداری: تحمل بالای تلفات، جنگ شبانه و زمستانی، اما زنجیره فرمان کند و انعطاف کم"),
    };

    // آشنایی خدمه با مدل خارجی
    static float Familiarity(Faction crew, Faction origin, FactionProfile prof)
        => crew == origin ? 1.00f : prof.ForeignAdapt;

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
        public int Next(int max) => max <= 0 ? 0 : (int)(NextU() % (uint)max);
        public bool Chance(float p) => NextF() < p;
    }

    // ───────────────────────────── ساختار گروه رزمی ─────────────────────────
    struct Group
    {
        public float X, Y;
        public float Units, Size0;
        public float Knocked;        // از کار افتاده (قابل بازیابی در صورت تسلط بر میدان)
        public float CAmmo, MAmmo;
        public float Morale, Supp;
        public float Fatigue;
        public float Exp;
        public float TgtX, TgtY;
        public float Signature;
        public short FireTgt;
        public byte Type;            // 0 = پیاده، 1 = زرهی
        public byte Model;           // اندیس مدل در Force.Specs
        public byte Posture;
        public byte Sector;
        public byte Role;            // 0 = خط اول، 1 = ذخیره، 2 = پوشش/فریب
        public bool Alive;
        public bool Sprung;
        public bool Committed;
    }

    struct Intel { public float Level, LastX, LastY, Stale; }

    // ───────────────────────── نیروی یک طرف نبرد ────────────────────────────
    sealed class Force
    {
        public Group[] G = new Group[MAX_GROUPS];
        public int N;
        public Faction Owner;
        public FactionProfile Prof;
        public TankSpec[] Specs = Array.Empty<TankSpec>();
        public string[] ModelNames = Array.Empty<string>();
        public long[] ModelSent = Array.Empty<long>();
        public float[] ModelFamiliar = Array.Empty<float>();
        public float[] ModelKnocked = Array.Empty<float>();
        public long[] ModelLost = Array.Empty<long>();
        public long[] ModelKills = Array.Empty<long>();   // تانک دشمن که این مدل زده
        public long SoldiersSent;
        public float SoldiersKnocked;
        public long SoldiersLost;
        public bool IsAttacker;
        public Intel[] IntelOnFoe = new Intel[MAX_GROUPS];
        public float[] ThreatMap = new float[SECTORS];    // برآورد تهدید دشمن در هر سکتور
        public float IntelQuality;
        public CommanderState Cmd;

        public float ForeignShare()   // سهم تجهیزات خارجی از کل زره
        {
            long tot = 0, foreign = 0;
            for (int i = 0; i < ModelSent.Length; i++)
            {
                tot += ModelSent[i];
                if (Specs[i].Origin != Owner) foreign += ModelSent[i];
            }
            return tot > 0 ? (float)foreign / tot : 0f;
        }
    }

    // ───────────────────── مغز فرماندهی (حالت ذهنی فرمانده) ──────────────────
    struct CommanderState
    {
        public int Doctrine;        // 11,12,21,22
        public int Phase;           // فاز جاری عملیات
        public int PhaseStart;
        public float Aggression;    // 0..1 شخصیت
        public float Caution;
        public float Patience;
        public int MainSector;
        public int SecondSector;
        public int FeintSector;
        public bool Committed;      // نیروی اصلی وارد شده؟
        public bool ReserveIn;      // ذخیره وارد شده؟
        public bool RingClosed;
        public bool Reorganized;
        public float LastPower;
        public float PeakDepth;
        public int LastDecisionTick;
        public int ShiftCount;      // چند بار محور را عوض کرده
    }

    // ───────────────────────────── دفتر وقایع ───────────────────────────────
    const byte LG_PLAN = 0, LG_DECISION = 1, LG_COMBAT = 2, LG_BREAK = 3, LG_CRISIS = 4, LG_AIR = 5, LG_ENV = 6;

    sealed class BattleLog
    {
        public readonly List<(int Tick, byte Side, byte Kind, string Text)> Items = new();
        public void Add(int tick, byte side, byte kind, string text)
        {
            if (Items.Count >= 80) return;
            Items.Add((tick, side, kind, text));
        }
        public IEnumerable<(int Tick, byte Side, byte Kind, string Text)> For(byte side)
            => Items.Where(x => x.Side == 2 || x.Side == side);
    }

    static string Clock(int tick)
    {
        int mm = (int)(tick * TICK_MIN);
        return $"{mm / 60:D2}:{mm % 60:D2}";
    }

    static long _seedCounter = Environment.TickCount;

    // ───────────────────────── محیط نبرد (زمین/هوا) ──────────────────────────
    sealed class Field
    {
        public byte[] Terr = new byte[GRID_W * GRID_H];
        public float[] Elev = new float[GRID_W * GRID_H];
        public byte Weather;
        public byte StartTime;
        public byte TimeAt(int tick) => (byte)((StartTime + (tick / 30)) & 3);

        public byte TerrAt(float x, float y)
        {
            int gx = Math.Clamp((int)(x / CELL), 0, GRID_W - 1);
            int gy = Math.Clamp((int)((y + 6f) / CELL), 0, GRID_H - 1);
            return Terr[gy * GRID_W + gx];
        }
        public float ElevAt(float x, float y)
        {
            int gx = Math.Clamp((int)(x / CELL), 0, GRID_W - 1);
            int gy = Math.Clamp((int)((y + 6f) / CELL), 0, GRID_H - 1);
            return Elev[gy * GRID_W + gx];
        }
        public byte DominantTerrainNear(float x)
        {
            Span<int> cnt = stackalloc int[6];
            for (float y = 0; y < 12f; y += 1f)
                for (float dx = -2f; dx <= 2f; dx += 1f)
                    cnt[TerrAt(Math.Clamp(x + dx, 0.2f, FRONT_KM - 0.2f), y)]++;
            int best = 0;
            for (int i = 1; i < 6; i++) if (cnt[i] > cnt[best]) best = i;
            return (byte)best;
        }
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
        public string Narrative;
    }

    // ======================================================================
    //  بخش ۲ — زمین، شناسایی، مغزهای فرماندهی، حرکت، آتش، فاز هوایی
    // ======================================================================

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

    static Field GenField(ref XorRng rng)
    {
        var f = new Field();
        uint s1 = (uint)rng.NextU(), s2 = (uint)rng.NextU(), s3 = (uint)rng.NextU();
        for (int gy = 0; gy < GRID_H; gy++)
            for (int gx = 0; gx < GRID_W; gx++)
            {
                float e = Noise(gx * 0.09f, gy * 0.09f, s1) * 0.65f + Noise(gx * 0.23f, gy * 0.23f, s2) * 0.35f;
                float v = Noise(gx * 0.13f + 50, gy * 0.13f, s3);
                int idx = gy * GRID_W + gx;
                f.Elev[idx] = e;
                byte t;
                if (e > 0.78f) t = T_RIDGE;
                else if (e > 0.62f) t = T_HILL;
                else if (v > 0.72f && e > 0.3f) t = T_FOREST;
                else if (v < 0.12f && e < 0.35f) t = T_MARSH;
                else if (v > 0.62f && v <= 0.72f && e < 0.5f) t = T_URBAN;
                else t = T_PLAIN;
                f.Terr[idx] = t;
            }

        float r = rng.NextF();
        f.Weather = r < 0.45f ? W_CLEAR : r < 0.68f ? W_CLOUD : r < 0.84f ? W_RAIN : r < 0.94f ? W_FOG : W_SNOW;
        f.StartTime = (byte)rng.Next(4);
        return f;
    }

    // ═════════════════════ ساخت نیروی یک طرف (به تفکیک مدل) ══════════════════
    static Force BuildForce(Faction owner, bool attacker,
        List<(string Model, long Count)> tankBreakdown, long soldiers,
        int strat, int tac, Field field, ref XorRng rng)
    {
        var fo = new Force { Owner = owner, Prof = ProfileOf(owner), IsAttacker = attacker };

        var models = new List<(string Name, long Count)>();
        if (tankBreakdown != null)
            foreach (var (m, c) in tankBreakdown)
                if (c > 0) models.Add((string.IsNullOrWhiteSpace(m) ? "زرهی نامشخص" : m, c));

        int nm = models.Count;
        fo.ModelNames = new string[nm];
        fo.Specs = new TankSpec[nm];
        fo.ModelSent = new long[nm];
        fo.ModelFamiliar = new float[nm];
        fo.ModelKnocked = new float[nm];
        fo.ModelLost = new long[nm];
        fo.ModelKills = new long[nm];
        for (int i = 0; i < nm; i++)
        {
            fo.ModelNames[i] = models[i].Name;
            fo.Specs[i] = GetTankSpecByModel(models[i].Name);
            fo.ModelSent[i] = models[i].Count;
            fo.ModelFamiliar[i] = Familiarity(owner, fo.Specs[i].Origin, fo.Prof);
        }
        fo.SoldiersSent = Math.Max(0, soldiers);

        long totalTanks = fo.ModelSent.Sum();
        long rawGroups = totalTanks / TANK_GROUP + fo.SoldiersSent / INF_GROUP + 2;
        float scale = rawGroups > MAX_GROUPS ? (float)rawGroups / MAX_GROUPS : 1f;
        float tankGrp = TANK_GROUP * scale, infGrp = INF_GROUP * scale;

        int n = 0;
        for (int mi = 0; mi < nm && n < MAX_GROUPS; mi++)
        {
            long left = fo.ModelSent[mi];
            while (left > 0 && n < MAX_GROUPS)
            {
                float u = Math.Min(left, (long)Math.Ceiling(tankGrp));
                InitGroup(ref fo.G[n], attacker, 1, (byte)mi, u, strat, tac, field, ref rng);
                left -= (long)u; n++;
            }
        }
        long sLeft = fo.SoldiersSent;
        while (sLeft > 0 && n < MAX_GROUPS)
        {
            float u = Math.Min(sLeft, (long)Math.Ceiling(infGrp));
            InitGroup(ref fo.G[n], attacker, 0, 0, u, strat, tac, field, ref rng);
            sLeft -= (long)u; n++;
        }
        fo.N = n;

        // نقش‌ها: خط اول / ذخیره / پوششی — نسبت‌ها بسته به دکترین بعداً تنظیم می‌شود
        for (int i = 0; i < n; i++)
            fo.G[i].Role = (byte)(i % 4 == 3 ? 1 : 0);

        fo.Cmd = InitCommander(attacker, strat, tac, ref rng);
        return fo;
    }

    static void InitGroup(ref Group gr, bool atk, byte type, byte model, float units,
        int strat, int tac, Field field, ref XorRng rng)
    {
        gr = default;
        gr.Type = type; gr.Model = model; gr.Units = units; gr.Size0 = units; gr.Alive = true;
        gr.Morale = rng.Range(0.86f, 1f);
        gr.CAmmo = units; gr.MAmmo = units;
        gr.Exp = rng.Range(0f, 0.1f);
        gr.FireTgt = -1;

        if (atk)
        {
            gr.Y = rng.Range(-4.5f, -1.5f);
            gr.X = rng.Range(1f, FRONT_KM - 1);
            gr.Posture = P_ADVANCE;
            gr.TgtX = gr.X; gr.TgtY = 6f;
        }
        else
        {
            gr.X = rng.Range(1f, FRONT_KM - 1);
            if (strat == 1)
            {
                gr.Y = tac == 1 ? rng.Range(0.8f, 3.2f) : rng.Range(1.5f, 6f);
                gr.Posture = tac == 1 ? P_DEFEND : P_PATROL;
                if (tac == 1) SeekCover(ref gr, field, ref rng);
            }
            else
            {
                gr.Y = tac == 1 ? rng.Range(2f, 7f) : rng.Range(4f, 11f);
                gr.Posture = P_AMBUSH;
                SeekCover(ref gr, field, ref rng);
            }
            gr.TgtX = gr.X; gr.TgtY = gr.Y;
        }
        gr.Sector = (byte)Math.Clamp((int)(gr.X / SECTOR_KM), 0, SECTORS - 1);
    }

    static void SeekCover(ref Group gr, Field field, ref XorRng rng)
    {
        float bx = gr.X, by = gr.Y, best = TerCover[field.TerrAt(gr.X, gr.Y)];
        for (int i = 0; i < 6; i++)
        {
            float x = Math.Clamp(gr.X + rng.Range(-2f, 2f), 0.5f, FRONT_KM - 0.5f);
            float y = Math.Clamp(gr.Y + rng.Range(-1.5f, 1.5f), 0.3f, DEPTH_KM - 1);
            float c = TerCover[field.TerrAt(x, y)];
            if (c > best) { best = c; bx = x; by = y; }
        }
        gr.X = bx; gr.Y = by;
    }

    // ═══════════════════ مه جنگ: شناسایی و به‌روزرسانی اطلاعات ════════════════
    static float SenseSide(Force own, Force foe, Field field, bool reconBonus, float visEnv, ref XorRng rng)
    {
        float sum = 0f; int alive = 0;
        for (int j = 0; j < foe.N; j++)
        {
            if (!foe.G[j].Alive) { own.IntelOnFoe[j].Level *= 0.9f; continue; }
            alive++;
            ref Intel it = ref own.IntelOnFoe[j];
            it.Stale += TICK_MIN;

            byte ft = field.TerrAt(foe.G[j].X, foe.G[j].Y);
            float conceal = TerCover[ft];
            if (foe.G[j].Posture == P_AMBUSH && !foe.G[j].Sprung) conceal = Math.Min(0.93f, conceal + 0.35f);
            float sig = foe.G[j].Signature;
            float bestGain = 0f;

            for (int i = 0; i < own.N; i++)
            {
                if (!own.G[i].Alive) continue;
                float dx = own.G[i].X - foe.G[j].X, dy = own.G[i].Y - foe.G[j].Y;
                float dist2 = dx * dx + dy * dy;
                if (dist2 > 36f) continue;
                float dist = MathF.Sqrt(dist2);
                float vis = (own.G[i].Type == 1 ? 2.6f : 2.1f) * TerVision[field.TerrAt(own.G[i].X, own.G[i].Y)] * visEnv;
                if (field.ElevAt(own.G[i].X, own.G[i].Y) > field.ElevAt(foe.G[j].X, foe.G[j].Y) + 0.12f) vis *= 1.3f;
                if (reconBonus) vis *= 1.28f;
                float moveSig = foe.G[j].Posture is P_ADVANCE or P_FLANK or P_ASSAULT ? 0.25f : 0f;
                float p = (1f - Math.Clamp(dist / Math.Max(0.3f, vis), 0f, 1f)) * (1f - conceal) + sig + moveSig;
                if (p > bestGain) bestGain = p;
            }

            if (bestGain > 0.04f && rng.NextF() < Math.Clamp(bestGain, 0f, 0.95f))
            {
                it.Level = Math.Min(1f, it.Level + 0.45f + bestGain * 0.5f);
                it.LastX = foe.G[j].X; it.LastY = foe.G[j].Y; it.Stale = 0f;
            }
            else
            {
                it.Level *= it.Stale > 60f ? 0.93f : 0.985f;
                if (it.Stale > 150f) it.Level *= 0.85f;
            }
            sum += it.Level;
        }
        for (int j = 0; j < foe.N; j++) foe.G[j].Signature *= 0.55f;
        own.IntelQuality = alive > 0 ? sum / alive : 0f;
        return own.IntelQuality;
    }

    static void BuildThreatMap(Force own, Force foe)
    {
        Array.Clear(own.ThreatMap, 0, SECTORS);
        for (int j = 0; j < foe.N; j++)
        {
            if (!foe.G[j].Alive) continue;
            float lvl = own.IntelOnFoe[j].Level;
            if (lvl < 0.15f) continue;
            int s = Math.Clamp((int)(own.IntelOnFoe[j].LastX / SECTOR_KM), 0, SECTORS - 1);
            float pw = foe.G[j].Type == 1
                ? foe.G[j].Units * (6f + foe.Specs[foe.G[j].Model].Armor * 0.03f + foe.Specs[foe.G[j].Model].Pen * 0.04f)
                : foe.G[j].Units * 0.8f;
            own.ThreatMap[s] += pw * lvl;
        }
    }

    static int WeakestSector(float[] threat, ref XorRng rng, float noise = 8f)
    {
        int best = 1; float bv = float.MaxValue;
        for (int s = 1; s < SECTORS - 1; s++)
        {
            float v = threat[s] + threat[s - 1] * 0.4f + threat[s + 1] * 0.4f + rng.NextF() * noise;
            if (v < bv) { bv = v; best = s; }
        }
        return best;
    }

    static int StrongestSector(float[] threat)
    {
        int hot = 0; float hv = -1f;
        for (int s = 0; s < SECTORS; s++) if (threat[s] > hv) { hv = threat[s]; hot = s; }
        return hot;
    }

    static float SectorX(int s) => (s + 0.5f) * SECTOR_KM;

    // ═════════════════ مغز فرماندهی — شخصیت و برنامه‌ی اولیه ═════════════════
    static CommanderState InitCommander(bool attacker, int strat, int tac, ref XorRng rng)
    {
        var c = new CommanderState();
        c.Doctrine = strat * 10 + tac;
        c.Phase = 0;
        c.PhaseStart = 0;
        c.MainSector = -1; c.SecondSector = -1; c.FeintSector = -1;

        // شخصیت فرمانده: در هر نبرد کمی متفاوت، ولی حول محور دکترین
        if (attacker)
        {
            switch (c.Doctrine)
            {
                case 11: c.Aggression = rng.Range(0.72f, 0.95f); c.Caution = rng.Range(0.10f, 0.30f); c.Patience = rng.Range(0.15f, 0.40f); break;
                case 12: c.Aggression = rng.Range(0.45f, 0.70f); c.Caution = rng.Range(0.30f, 0.55f); c.Patience = rng.Range(0.45f, 0.75f); break;
                case 21: c.Aggression = rng.Range(0.35f, 0.60f); c.Caution = rng.Range(0.40f, 0.65f); c.Patience = rng.Range(0.60f, 0.90f); break;
                default: c.Aggression = rng.Range(0.55f, 0.80f); c.Caution = rng.Range(0.25f, 0.50f); c.Patience = rng.Range(0.40f, 0.70f); break;
            }
        }
        else
        {
            switch (c.Doctrine)
            {
                case 11: c.Aggression = rng.Range(0.10f, 0.30f); c.Caution = rng.Range(0.65f, 0.90f); c.Patience = rng.Range(0.70f, 0.95f); break;
                case 12: c.Aggression = rng.Range(0.35f, 0.60f); c.Caution = rng.Range(0.40f, 0.65f); c.Patience = rng.Range(0.40f, 0.70f); break;
                case 21: c.Aggression = rng.Range(0.25f, 0.50f); c.Caution = rng.Range(0.55f, 0.80f); c.Patience = rng.Range(0.75f, 0.95f); break;
                default: c.Aggression = rng.Range(0.45f, 0.75f); c.Caution = rng.Range(0.30f, 0.55f); c.Patience = rng.Range(0.55f, 0.85f); break;
            }
        }
        return c;
    }

    static readonly string[] AtkDoctrineName = { "هجوم منسجم — حمله مستقیم متمرکز", "هجوم منسجم — اکتشاف سبک و یورش اصلی",
                                                 "محاصره و ضربه — حلقه‌ی گسترده و فرسایش", "محاصره و ضربه — حلقه‌ی متحرک" };
    static readonly string[] DefDoctrineName = { "دفاع منسجم — خط ثابت زرهی", "دفاع منسجم — گشت متحرک ترکیبی",
                                                 "ضدحمله پراکنده — استتار و کمین", "ضدحمله پراکنده — عقب‌نشینی و تله" };

    static string AtkDoctrineText(int doctrine) => doctrine switch
    {
        11 => AtkDoctrineName[0], 12 => AtkDoctrineName[1], 21 => AtkDoctrineName[2], _ => AtkDoctrineName[3]
    };
    static string DefDoctrineText(int doctrine) => doctrine switch
    {
        11 => DefDoctrineName[0], 12 => DefDoctrineName[1], 21 => DefDoctrineName[2], _ => DefDoctrineName[3]
    };

    // ═══════════════ مغز فرمانده‌ی مهاجم — چهار دستگاه فکری مجزا ═════════════
    static void CommandAttacker(Force me, Force foe, Field field, float depth, int tick,
        BattleLog log, ref XorRng rng)
    {
        me.Cmd.LastDecisionTick = tick;
        switch (me.Cmd.Doctrine)
        {
            case 11: BrainSchwerpunkt(me, foe, field, depth, tick, log, ref rng); break;
            case 12: BrainProbeAndPunch(me, foe, field, depth, tick, log, ref rng); break;
            case 21: BrainWideEncirclement(me, foe, field, depth, tick, log, ref rng); break;
            default: BrainRollingPocket(me, foe, field, depth, tick, log, ref rng); break;
        }
    }

    // ── دکترین ۱-۱: هجوم منسجم / حمله‌ی مستقیم متمرکز ────────────────────────
    //    منطق: انتخاب یک محور و کوبیدن مداوم؛ در صورت گیر کردن، محور را با
    //    هزینه‌ی زمان جابه‌جا می‌کند؛ ذخیره را زود وارد می‌کند.
    static void BrainSchwerpunkt(Force me, Force foe, Field field, float depth, int tick, BattleLog log, ref XorRng rng)
    {
        ref CommanderState c = ref me.Cmd;

        if (c.MainSector < 0)
        {
            c.MainSector = WeakestSector(me.ThreatMap, ref rng, 10f);
            byte ter = field.DominantTerrainNear(SectorX(c.MainSector));
            log.Add(tick, 0, LG_PLAN, $"ستاد مهاجم محور اصلی را روی سکتور {c.MainSector + 1} (کیلومتر {SectorX(c.MainSector):F0}، {TerName[ter]}) گذاشت و همه‌ی توان را همان‌جا متمرکز کرد.");
        }

        // ارزیابی: اگر محور اصلی کند شد و ما صبر کم داریم، محور را عوض کن
        bool stalled = tick - c.PhaseStart > 40 && depth - c.PeakDepth < 1.2f;
        if (stalled && c.ShiftCount < 2 && rng.Chance(0.55f + me.Cmd.Aggression * 0.3f))
        {
            int alt = WeakestSector(me.ThreatMap, ref rng, 4f);
            if (alt != c.MainSector)
            {
                c.MainSector = alt; c.PhaseStart = tick; c.ShiftCount++;
                log.Add(tick, 0, LG_DECISION, $"محور حمله در سکتور قبلی قفل شد؛ فرمانده ثقل ضربه را به سکتور {alt + 1} منتقل کرد.");
            }
        }
        if (depth > c.PeakDepth) { c.PeakDepth = depth; c.PhaseStart = tick; }

        if (!c.ReserveIn && (depth > 6f || tick > 55))
        {
            c.ReserveIn = true;
            log.Add(tick, 0, LG_DECISION, "فرمانده ذخیره‌ی زرهی را برای پهن‌کردن رخنه وارد خط کرد.");
        }

        float mainX = SectorX(c.MainSector);
        for (int i = 0; i < me.N; i++)
        {
            ref Group g = ref me.G[i];
            if (!g.Alive) continue;
            if (!TriageGroup(ref g, me, true, tick, log, ref rng)) continue;

            bool reserve = g.Role == 1 && !c.ReserveIn;
            if (reserve) { g.Posture = P_HOLD; g.TgtX = mainX; g.TgtY = Math.Max(-2f, g.Y); continue; }

            g.Posture = depth > 2f ? P_ASSAULT : P_ADVANCE;
            float spread = 3.5f + (1f - c.Aggression) * 4f;
            g.TgtX = Math.Clamp(mainX + rng.Range(-spread, spread), 1f, FRONT_KM - 1);
            g.TgtY = g.Y + (g.Type == 1 ? 6.5f : 4.5f);
            g.Committed = true;
        }
    }

    // ── دکترین ۱-۲: اکتشاف سبک، سپس یورش سنگین ──────────────────────────────
    //    منطق: فاز ۱ گشت پراکنده برای کشف ضعف (بدون درگیر کردن توده)،
    //    فاز ۲ تمرکز ناگهانی روی ضعیف‌ترین نقطه‌ی کشف‌شده.
    static void BrainProbeAndPunch(Force me, Force foe, Field field, float depth, int tick, BattleLog log, ref XorRng rng)
    {
        ref CommanderState c = ref me.Cmd;

        if (c.Phase == 0)
        {
            if (c.MainSector < 0)
            {
                c.MainSector = SECTORS / 2;
                log.Add(tick, 0, LG_PLAN, "مهاجم فاز اکتشاف را آغاز کرد: گروه‌های سبک روی کل جبهه پخش شدند تا خط دفاع را بشناسند و توده‌ی اصلی عقب ماند.");
            }
            bool enoughIntel = me.IntelQuality > 0.34f + c.Caution * 0.2f;
            bool outOfPatience = tick > (int)(30 + c.Patience * 45);
            if (enoughIntel || outOfPatience)
            {
                c.MainSector = WeakestSector(me.ThreatMap, ref rng, 3f);
                c.SecondSector = StrongestSector(me.ThreatMap);
                c.Phase = 1; c.PhaseStart = tick; c.Committed = true;
                byte ter = field.DominantTerrainNear(SectorX(c.MainSector));
                log.Add(tick, 0, LG_DECISION, enoughIntel
                    ? $"شناسایی جواب داد: نازک‌ترین بخش خط، سکتور {c.MainSector + 1} ({TerName[ter]}) تشخیص داده شد و یورش اصلی همان‌جا شکل گرفت."
                    : $"صبر فرمانده تمام شد؛ بدون تصویر کامل، یورش را روی سکتور {c.MainSector + 1} آغاز کرد.");
            }
        }
        else if (c.Phase == 1)
        {
            if (tick - c.PhaseStart > 45 && depth - c.PeakDepth < 1f && c.ShiftCount < 1)
            {
                int alt = WeakestSector(me.ThreatMap, ref rng, 2f);
                if (alt != c.MainSector)
                {
                    c.MainSector = alt; c.PhaseStart = tick; c.ShiftCount++;
                    log.Add(tick, 0, LG_DECISION, $"یورش اول جواب نداد؛ گروه‌های اکتشافی نقطه‌ی جدیدی در سکتور {alt + 1} یافتند و ضربه‌ی دوم آنجا زده شد.");
                }
            }
            if (depth > c.PeakDepth) { c.PeakDepth = depth; c.PhaseStart = tick; }
        }

        float mainX = SectorX(Math.Max(0, c.MainSector));
        for (int i = 0; i < me.N; i++)
        {
            ref Group g = ref me.G[i];
            if (!g.Alive) continue;
            if (!TriageGroup(ref g, me, true, tick, log, ref rng)) continue;

            bool prober = (i % 5) == 0;   // یک‌پنجم نیرو نقش اکتشاف دارد
            if (c.Phase == 0)
            {
                if (prober)
                {
                    g.Posture = P_PATROL;
                    g.TgtX = Math.Clamp(SectorX(i % SECTORS) + rng.Range(-2f, 2f), 1f, FRONT_KM - 1);
                    g.TgtY = Math.Min(6f, g.Y + 3f);
                }
                else
                {
                    g.Posture = P_HOLD;
                    g.TgtX = Math.Clamp(g.X + rng.Range(-1f, 1f), 1f, FRONT_KM - 1);
                }
            }
            else
            {
                g.Posture = depth > 2f ? P_ASSAULT : P_ADVANCE;
                float spread = prober ? 9f : 4.5f;
                g.TgtX = Math.Clamp(mainX + rng.Range(-spread, spread), 1f, FRONT_KM - 1);
                g.TgtY = g.Y + (g.Type == 1 ? 6f : 4.2f);
                g.Committed = true;
            }
        }
    }

    // ── دکترین ۲-۱: محاصره‌ی گسترده و فرسایش ────────────────────────────────
    //    منطق: دو بازو از دو جناح، فشار آهسته و کنترل مسیرها؛ فقط وقتی حلقه
    //    بسته شد به مرکز ضربه می‌زند. تلفات کم، زمان زیاد.
    static void BrainWideEncirclement(Force me, Force foe, Field field, float depth, int tick, BattleLog log, ref XorRng rng)
    {
        ref CommanderState c = ref me.Cmd;

        if (c.MainSector < 0)
        {
            c.MainSector = 1; c.SecondSector = SECTORS - 2;
            c.FeintSector = StrongestSector(me.ThreatMap);
            log.Add(tick, 0, LG_PLAN, "طرح محاصره: دو بازوی زرهی از جناح چپ و راست باز شدند و مرکز فقط با آتش تثبیتی درگیر ماند.");
        }

        if (!c.RingClosed && depth > 7f && me.IntelQuality > 0.4f && tick > 40)
        {
            c.RingClosed = true; c.PhaseStart = tick;
            log.Add(tick, 0, LG_DECISION, "دو بازو در عمق به هم نزدیک شدند و حلقه‌ی محاصره بسته شد؛ فشار از سه جهت روی مدافع افتاد.");
        }
        if (c.RingClosed && !c.ReserveIn && tick - c.PhaseStart > 25)
        {
            c.ReserveIn = true;
            log.Add(tick, 0, LG_DECISION, "پس از تثبیت حلقه، فرمانده ضربه‌ی نهایی به مرکز جیب را صادر کرد.");
        }

        float leftX = SectorX(c.MainSector), rightX = SectorX(c.SecondSector);
        float centerX = SectorX(c.FeintSector < 0 ? SECTORS / 2 : c.FeintSector);

        for (int i = 0; i < me.N; i++)
        {
            ref Group g = ref me.G[i];
            if (!g.Alive) continue;
            if (!TriageGroup(ref g, me, true, tick, log, ref rng)) continue;

            bool pinning = (i % 4) == 0;         // یک‌چهارم نیرو مرکز را تثبیت می‌کند
            if (pinning && !c.ReserveIn)
            {
                g.Posture = P_SCREEN;
                g.TgtX = Math.Clamp(centerX + rng.Range(-5f, 5f), 1f, FRONT_KM - 1);
                g.TgtY = Math.Min(depth + 1.5f, g.Y + 1.5f);
                continue;
            }

            bool leftArm = (i & 1) == 0;
            float armX = leftArm ? leftX : rightX;
            if (c.RingClosed) armX = centerX + (leftArm ? -3f : 3f);
            g.Posture = c.RingClosed ? P_ASSAULT : P_FLANK;
            g.TgtX = Math.Clamp(armX + rng.Range(-3f, 3f), 1f, FRONT_KM - 1);
            g.TgtY = g.Y + (g.Type == 1 ? 5.5f : 3.8f);
            g.Committed = c.RingClosed;
        }
    }

    // ── دکترین ۲-۲: حلقه‌ی متحرک ────────────────────────────────────────────
    //    منطق: محور حمله مدام می‌چرخد تا مدافع نتواند ذخیره‌اش را جا بدهد؛
    //    پرتحرک، پرمصرف و در برابر کمین آسیب‌پذیر.
    static void BrainRollingPocket(Force me, Force foe, Field field, float depth, int tick, BattleLog log, ref XorRng rng)
    {
        ref CommanderState c = ref me.Cmd;

        if (c.MainSector < 0)
        {
            c.MainSector = WeakestSector(me.ThreatMap, ref rng, 9f);
            log.Add(tick, 0, LG_PLAN, "طرح حلقه‌ی متحرک: ستون‌های زرهی قرار شد بدون توقف محور ضربه را بچرخانند تا دفاع نتواند تمرکز کند.");
        }

        int period = (int)(18 + c.Patience * 22);
        if (tick - c.PhaseStart >= period)
        {
            int next = WeakestSector(me.ThreatMap, ref rng, 5f);
            if (next == c.MainSector) next = (next + 2 + rng.Next(3)) % SECTORS;
            c.SecondSector = c.MainSector;
            c.MainSector = next;
            c.PhaseStart = tick; c.ShiftCount++;
            log.Add(tick, 0, LG_DECISION, $"محور ضربه چرخید: فشار از سکتور {c.SecondSector + 1} برداشته و روی سکتور {c.MainSector + 1} انداخته شد.");
        }
        if (!c.RingClosed && depth > 9f && c.ShiftCount >= 2)
        {
            c.RingClosed = true;
            log.Add(tick, 0, LG_DECISION, "چرخش پیاپی محور، ذخیره‌ی مدافع را فرسود و جیب متحرک شکل گرفت.");
        }

        float mainX = SectorX(c.MainSector);
        float prevX = SectorX(c.SecondSector < 0 ? c.MainSector : c.SecondSector);

        for (int i = 0; i < me.N; i++)
        {
            ref Group g = ref me.G[i];
            if (!g.Alive) continue;
            if (!TriageGroup(ref g, me, true, tick, log, ref rng)) continue;

            bool holdOld = (i % 3) == 0;   // یک‌سوم نیرو محور قبلی را رها نمی‌کند
            float tx = holdOld ? prevX : mainX;
            g.Posture = depth > 3f ? P_ASSAULT : P_FLANK;
            float wobble = MathF.Sin((tick + i * 7) * 0.05f) * 3.5f;
            g.TgtX = Math.Clamp(tx + wobble + rng.Range(-2.5f, 2.5f), 1f, FRONT_KM - 1);
            g.TgtY = g.Y + (g.Type == 1 ? 6.2f : 4.2f);
            g.Committed = true;
        }
    }

    // ═══════════════ مغز فرمانده‌ی مدافع — چهار دستگاه فکری مجزا ═════════════
    static void CommandDefender(Force me, Force foe, Field field, float depth, int tick,
        BattleLog log, ref XorRng rng)
    {
        me.Cmd.LastDecisionTick = tick;
        switch (me.Cmd.Doctrine)
        {
            case 11: BrainStaticLine(me, foe, field, depth, tick, log, ref rng); break;
            case 12: BrainMobileScreen(me, foe, field, depth, tick, log, ref rng); break;
            case 21: BrainAmbushNet(me, foe, field, depth, tick, log, ref rng); break;
            default: BrainElasticTrap(me, foe, field, depth, tick, log, ref rng); break;
        }
    }

    // ── دفاع ۱-۱: خط ثابت زرهی ──────────────────────────────────────────────
    static void BrainStaticLine(Force me, Force foe, Field field, float depth, int tick, BattleLog log, ref XorRng rng)
    {
        ref CommanderState c = ref me.Cmd;
        int hot = StrongestSector(me.ThreatMap);
        float hv = me.ThreatMap[hot];
        float hotX = SectorX(hot);

        if (c.MainSector < 0 && hv > 0f)
        {
            c.MainSector = hot;
            log.Add(tick, 1, LG_PLAN, $"مدافع فشار اصلی را در سکتور {hot + 1} تشخیص داد و خط سنگرها را همان‌جا سنگین کرد.");
        }
        else if (hv > 0 && hot != c.MainSector && depth > 2f && rng.Chance(0.5f))
        {
            log.Add(tick, 1, LG_DECISION, $"ثقل حمله جابه‌جا شد؛ مدافع آتش و ذخیره را از سکتور {c.MainSector + 1} به {hot + 1} منتقل کرد.");
            c.MainSector = hot;
        }

        if (!c.ReserveIn && depth > 4f)
        {
            c.ReserveIn = true;
            log.Add(tick, 1, LG_DECISION, "با عمیق‌شدن رخنه، ذخیره‌ی زرهی مدافع برای بستن شکاف وارد شد.");
        }

        for (int i = 0; i < me.N; i++)
        {
            ref Group g = ref me.G[i];
            if (!g.Alive) continue;
            if (!TriageGroup(ref g, me, false, tick, log, ref rng)) continue;

            bool reserve = i % 3 == 2;
            if (reserve && c.ReserveIn && hv > 0)
            {
                g.TgtX = Math.Clamp(hotX + rng.Range(-3f, 3f), 1f, FRONT_KM - 1);
                g.TgtY = Math.Max(0.8f, depth - 0.8f);
                g.Posture = P_ADVANCE;
            }
            else g.Posture = P_DEFEND;
        }
    }

    // ── دفاع ۱-۲: گشت متحرک ترکیبی ──────────────────────────────────────────
    static void BrainMobileScreen(Force me, Force foe, Field field, float depth, int tick, BattleLog log, ref XorRng rng)
    {
        ref CommanderState c = ref me.Cmd;
        int hot = StrongestSector(me.ThreatMap);
        float hv = me.ThreatMap[hot];
        float hotX = SectorX(hot);

        if (c.Phase == 0 && hv > 0f && me.IntelQuality > 0.3f)
        {
            c.Phase = 1; c.MainSector = hot; c.PhaseStart = tick;
            log.Add(tick, 1, LG_DECISION, $"گشت‌های متحرک، ستون اصلی مهاجم را در سکتور {hot + 1} پیدا کردند و گروه‌های ترکیبی به آن سمت جمع شدند.");
        }
        if (c.Phase == 1 && hot != c.MainSector && rng.Chance(0.6f))
        {
            c.MainSector = hot;
            log.Add(tick, 1, LG_DECISION, $"گشت‌ها محور جدید فشار را در سکتور {hot + 1} گزارش کردند و خط پوششی دوباره چید شد.");
        }

        for (int i = 0; i < me.N; i++)
        {
            ref Group g = ref me.G[i];
            if (!g.Alive) continue;
            if (!TriageGroup(ref g, me, false, tick, log, ref rng)) continue;

            if (c.Phase == 0)
            {
                g.Posture = P_PATROL;
                g.TgtX = Math.Clamp(g.X + rng.Range(-6f, 6f), 1f, FRONT_KM - 1);
                g.TgtY = Math.Clamp(g.Y + rng.Range(-1f, 1.5f), 0.8f, 8f);
            }
            else
            {
                bool screen = i % 3 == 0;
                g.Posture = screen ? P_SCREEN : P_ADVANCE;
                g.TgtX = Math.Clamp(hotX + rng.Range(-5f, 5f), 1f, FRONT_KM - 1);
                g.TgtY = Math.Clamp(depth + rng.Range(-0.5f, 1.5f), 1f, 9f);
            }
        }
    }

    // ── دفاع ۲-۱: شبکه‌ی کمین ────────────────────────────────────────────────
    static void BrainAmbushNet(Force me, Force foe, Field field, float depth, int tick, BattleLog log, ref XorRng rng)
    {
        ref CommanderState c = ref me.Cmd;
        int hot = StrongestSector(me.ThreatMap);
        float hotX = SectorX(hot);

        if (c.Phase == 0)
        {
            log.Add(tick, 1, LG_PLAN, "مدافع خط مقدم را عمداً رقیق گذاشت و تانک‌ها را در سنگرهای پنهان و پشت پوشش طبیعی مستقر کرد.");
            c.Phase = 1;
        }

        int sprung = 0;
        for (int i = 0; i < me.N; i++) if (me.G[i].Alive && me.G[i].Sprung) sprung++;
        if (!c.Committed && sprung > me.N / 4 && sprung > 0)
        {
            c.Committed = true;
            log.Add(tick, 1, LG_DECISION, "بیشتر کمین‌ها فعال شدند؛ مدافع از حالت پنهان بیرون آمد و به ضدحمله‌ی موضعی روی آورد.");
        }

        for (int i = 0; i < me.N; i++)
        {
            ref Group g = ref me.G[i];
            if (!g.Alive) continue;
            if (!TriageGroup(ref g, me, false, tick, log, ref rng)) continue;

            if (!g.Sprung && !c.Committed) { g.Posture = P_AMBUSH; continue; }
            if (c.Committed)
            {
                g.Posture = P_ASSAULT;
                g.TgtX = Math.Clamp(hotX + rng.Range(-4f, 4f), 1f, FRONT_KM - 1);
                g.TgtY = Math.Max(1f, depth - 0.5f);
            }
            else g.Posture = P_DEFEND;
        }
    }

    // ── دفاع ۲-۲: عقب‌نشینی کشسان و تله ─────────────────────────────────────
    static void BrainElasticTrap(Force me, Force foe, Field field, float depth, int tick, BattleLog log, ref XorRng rng)
    {
        ref CommanderState c = ref me.Cmd;
        int hot = StrongestSector(me.ThreatMap);
        float hotX = SectorX(hot);
        float trapDepth = 9f + c.Patience * 5f;

        if (c.Phase == 0)
        {
            log.Add(tick, 1, LG_PLAN, $"مدافع بخشی از خط را عمداً باز گذاشت تا مهاجم را تا عمق حدود {trapDepth:F0} کیلومتری بکشاند.");
            c.Phase = 1;
        }
        if (c.Phase == 1 && (depth > trapDepth || tick > 140))
        {
            c.Phase = 2; c.PhaseStart = tick; c.Committed = true;
            log.Add(tick, 1, LG_DECISION, depth > trapDepth
                ? "مهاجم وارد جیب شد؛ مدافع دهانه را بست و ضدحمله‌ی هم‌زمان از دو جناح را کلید زد."
                : "مهاجم به تله نیامد؛ مدافع ناچار از پناهگاه بیرون آمد و درگیری مستقیم را پذیرفت.");
        }

        for (int i = 0; i < me.N; i++)
        {
            ref Group g = ref me.G[i];
            if (!g.Alive) continue;
            if (!TriageGroup(ref g, me, false, tick, log, ref rng)) continue;

            if (c.Phase == 1)
            {
                if (!g.Sprung) { g.Posture = P_AMBUSH; g.TgtY = Math.Min(14f, g.Y + 1.2f); }
                else g.Posture = P_DEFEND;
            }
            else
            {
                g.Posture = P_ASSAULT;
                bool leftJaw = (i & 1) == 0;
                g.TgtX = Math.Clamp(hotX + (leftJaw ? -4f : 4f) + rng.Range(-2f, 2f), 1f, FRONT_KM - 1);
                g.TgtY = Math.Max(1f, depth - 2f);
            }
        }
    }

    // ── وضعیت اضطراری گروه (مهمات/روحیه) — مشترک بین همه‌ی دکترین‌ها ─────────
    static bool TriageGroup(ref Group g, Force me, bool attacker, int tick, BattleLog log, ref XorRng rng)
    {
        if (g.Posture == P_RETREAT) return false;
        float ammoR = (g.CAmmo + g.MAmmo) / Math.Max(0.01f, g.Size0 * 2f);
        if (ammoR <= 0.02f)
        {
            g.Posture = P_RETREAT;
            g.TgtY = attacker ? -4f : Math.Min(DEPTH_KM - 1, g.Y + 6f);
            return false;
        }
        if (ammoR < 0.16f) { g.Posture = P_HOLD; return false; }
        float moraleFloor = 0.35f / Math.Max(0.5f, me.Prof.MoraleResist);
        if (g.Morale < moraleFloor) { g.Posture = P_REGROUP; return false; }
        return true;
    }

    // ═════════════════════════════ حرکت ══════════════════════════════════════
    // حرکت با در نظر گرفتن «منطقه‌ی کنترل» دشمن: نمی‌شود از کنار خط دشمن رد شد.
    //  – هر یگان دشمنِ سالم در نزدیکی، پیشروی را کند و در نهایت متوقف می‌کند.
    static void MoveSide(Force f, Force foe, Field field, ref XorRng rng)
    {
        float wxSpd = WxSpeed[field.Weather];
        for (int i = 0; i < f.N; i++)
        {
            ref Group u = ref f.G[i];
            if (!u.Alive) continue;
            if (u.Posture is P_DEFEND or P_AMBUSH or P_HOLD or P_REGROUP) continue;

            float baseKmH = u.Type == 1 ? f.Specs[u.Model].Speed * 0.32f : 4.2f;
            if (u.Posture == P_RETREAT) baseKmH *= 1.2f;
            if (u.Posture == P_SCREEN) baseKmH *= 0.7f;
            if (u.Supp > 0.5f) baseKmH *= 0.45f;
            baseKmH *= (1f - u.Fatigue * 0.3f);

            float ter = TerSpeed[field.TerrAt(u.X, u.Y)];
            float step = baseKmH * ter * wxSpd * (TICK_MIN / 60f);

            // ── منطقه‌ی کنترل (ZoC) ──
            if (u.Posture != P_RETREAT)
            {
                float zoc = 0f, own = 0f;
                for (int j = 0; j < foe.N; j++)
                {
                    ref Group e = ref foe.G[j];
                    if (!e.Alive || e.Posture == P_RETREAT) continue;
                    float dx2 = e.X - u.X, dy2 = e.Y - u.Y;
                    float d2 = dx2 * dx2 + dy2 * dy2;
                    if (d2 > ZOC_R2) continue;
                    // یگان کمین‌نکرده‌ی مخفی هنوز جلوی حرکت را نمی‌گیرد
                    if (e.Posture == P_AMBUSH && !e.Sprung) continue;
                    float w = 1f - MathF.Sqrt(d2) / ZOC_R;
                    zoc += w * (e.Type == 1 ? e.Units * 1.0f : e.Units * 0.12f);
                }
                if (zoc > 0f)
                {
                    // نیروی خودی همان حوالی، فشار مقابل را می‌شکند
                    for (int j = 0; j < f.N; j++)
                    {
                        ref Group a = ref f.G[j];
                        if (!a.Alive || a.Posture is P_RETREAT or P_REGROUP) continue;
                        float dx2 = a.X - u.X, dy2 = a.Y - u.Y;
                        float d2 = dx2 * dx2 + dy2 * dy2;
                        if (d2 > ZOC_R2) continue;
                        float w = 1f - MathF.Sqrt(d2) / ZOC_R;
                        own += w * (a.Type == 1 ? a.Units * 1.0f : a.Units * 0.12f);
                    }
                    float pressure = own / Math.Max(0.001f, own + zoc);       // 0..1
                    float brake = Math.Clamp((pressure - BRAKE_THR) / BRAKE_SPAN, 0f, 1f);
                    // پوشش زمین به مدافع کمک می‌کند خط را نگه دارد
                    brake *= 1f - TerCover[field.TerrAt(u.X, u.Y)] * 0.35f;
                    step *= brake;
                    if (step < 0.02f) continue;   // زمین‌گیر شد
                }
            }

            float dx = u.TgtX - u.X, dy = u.TgtY - u.Y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            if (dist < 0.15f) continue;
            float mv = Math.Min(step, dist);
            u.X += dx / dist * mv; u.Y += dy / dist * mv;
            u.X = Math.Clamp(u.X, 0.2f, FRONT_KM - 0.2f);
            u.Y = Math.Clamp(u.Y, -6f, DEPTH_KM);
            if (mv > 0.5f) u.Signature = Math.Min(1f, u.Signature + 0.18f);
            u.Sector = (byte)Math.Clamp((int)(u.X / SECTOR_KM), 0, SECTORS - 1);
        }
    }

    // ═══════════ آتش: نفوذ/زره واقعی هر مدل در برابر مدل مقابل ══════════════
    static float FireSide(Force own, Force foe, Field field, bool attacker,
        float combatMul, float accEnv, int tick, BattleLog log,
        ref XorRng rng, ref bool contact, ref bool ambushFired)
    {
        float duel = 0f;
        byte tnow = field.TimeAt(tick);
        float nightPenalty = tnow == TM_NIGHT ? (0.72f + own.Prof.NightSkill * 0.25f) : 1f;

        for (int i = 0; i < own.N; i++)
        {
            ref Group u = ref own.G[i];
            if (!u.Alive || u.Posture is P_RETREAT or P_REGROUP) continue;

            var ospec = own.Specs.Length > 0 ? own.Specs[u.Model] : SpecUSA;
            float famil = own.ModelFamiliar.Length > 0 ? own.ModelFamiliar[u.Model] : 1f;

            int best = -1; float bestScore = 0f, bestDist = 99f;
            float maxRange = u.Type == 1 ? 2.1f : 0.9f;

            for (int j = 0; j < foe.N; j++)
            {
                if (!foe.G[j].Alive) continue;
                float lvl = own.IntelOnFoe[j].Level;
                if (lvl < 0.2f) continue;
                float dx = foe.G[j].X - u.X, dy = foe.G[j].Y - u.Y;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist > maxRange + 0.6f) continue;
                float pri = u.Type == 1 ? (foe.G[j].Type == 1 ? 3f : 1.6f) : (foe.G[j].Type == 1 ? 0.6f : 2.2f);
                pri *= 1f + (1f - foe.G[j].Units / Math.Max(1f, foe.G[j].Size0)) * 0.8f;
                float score = pri * lvl / (0.4f + dist);
                if (score > bestScore) { bestScore = score; best = j; bestDist = dist; }
            }

            u.FireTgt = (short)best;
            if (best < 0 || bestDist > maxRange) continue;
            if (!contact)
            {
                contact = true;
                byte ter = field.TerrAt(u.X, u.Y);
                log.Add(tick, 2, LG_COMBAT, $"نخستین تبادل آتش در کیلومتر {u.X:F0} جبهه، روی {TerName[ter]} رخ داد.");
            }

            float ambushMul = 1f;
            if (u.Posture == P_AMBUSH && !u.Sprung)
            {
                u.Sprung = true; ambushMul = 2.6f;
                if (!ambushFired)
                {
                    ambushFired = true;
                    log.Add(tick, 2, LG_COMBAT, $"کمین مدافع در عمق {u.Y:F1} کیلومتری فعال شد و ستون پیشرو را از پهلو درو کرد.");
                }
            }

            ref Group t = ref foe.G[best];
            var fspec = foe.Specs.Length > 0 ? foe.Specs[t.Model] : SpecUSA;
            float intelQ = own.IntelOnFoe[best].Level;
            byte tt = field.TerrAt(t.X, t.Y);

            float acc = 0.62f * (0.45f + 0.55f * intelQ) * TerAcc[field.TerrAt(u.X, u.Y)] * accEnv
                        * (1f - u.Supp * 0.5f) * nightPenalty;
            acc *= (0.9f + u.Exp * 0.3f);
            acc *= own.Prof.CrewQuality * famil;
            if (u.Posture is P_ADVANCE or P_ASSAULT or P_FLANK) acc *= 0.80f;
        if (!own.IsAttacker && u.Posture is P_DEFEND or P_AMBUSH or P_HOLD) acc *= DUGIN_ACC;   // آتش از سنگر آماده
            if (field.ElevAt(u.X, u.Y) > field.ElevAt(t.X, t.Y) + 0.1f) acc *= 1.18f;

            float cover = TerCover[tt] * (t.Posture is P_DEFEND or P_AMBUSH or P_HOLD ? 1.25f : 0.8f);
            float ammoR = (u.CAmmo + u.MAmmo) / Math.Max(0.01f, u.Size0 * 2f);
            float ammoMul = ammoR > 0.5f ? 1f : 0.55f + ammoR * 0.9f;
            float morale = 0.55f + u.Morale * 0.45f;
            float reliab = 0.88f + ospec.Reliab * 0.12f;

            float k = acc * ammoMul * morale * ambushMul * combatMul * reliab
                      * (1f - u.Fatigue * 0.25f) * (TICK_MIN / 6f);

            if (u.Type == 1)
            {
                float rangeMul = Math.Clamp(1.25f - bestDist * 0.45f, 0.45f, 1.2f);
                if (t.Type == 1)
                {
                    if (u.CAmmo > 0.05f)
                    {
                        // نفوذ واقعی این مدل در برابر زره واقعی مدل هدف
                        float effArmor = fspec.Armor * (t.Posture is P_DEFEND or P_AMBUSH ? 1.30f : 1f);
                        float pen = 1f / (1f + MathF.Exp(-(ospec.Pen * rangeMul - effArmor) / 9f));
                        float shots = u.Units * 1.6f * k;
                        float kills = shots * 0.32f * pen * (0.9f + rng.NextF() * 0.25f);
                        ApplyDamage(foe, best, kills, own, u.Model, true);
                        u.CAmmo = Math.Max(0f, u.CAmmo - shots * 0.05f);
                        u.Signature = Math.Min(1f, u.Signature + 0.55f);
                        duel += kills;
                        t.Supp = Math.Min(1f, t.Supp + 0.12f);
                    }
                }
                else if (u.MAmmo > 0.05f)
                {
                    float mgKill = u.Units * ospec.Mg * 1.05f * k * (1f - cover * 0.85f);
                    float heKill = 0f;
                    if (u.CAmmo > 0.05f)
                    {
                        heKill = u.Units * ospec.He * 4.5f * k * (1f - cover * 0.55f);
                        u.CAmmo = Math.Max(0f, u.CAmmo - u.Units * 0.04f);
                        u.Signature = Math.Min(1f, u.Signature + 0.5f);
                    }
                    ApplyDamage(foe, best, mgKill + heKill, own, u.Model, false);
                    u.MAmmo = Math.Max(0f, u.MAmmo - u.Units * 0.06f);
                    u.Signature = Math.Min(1f, u.Signature + 0.22f);
                    t.Supp = Math.Min(1f, t.Supp + 0.3f);
                }
            }
            else
            {
                if (t.Type == 0)
                {
                    if (u.MAmmo > 0.05f)
                    {
                        float kills = u.Units * 0.045f * k * (1f - cover * 0.8f);
                        ApplyDamage(foe, best, kills, own, u.Model, false);
                        u.MAmmo = Math.Max(0f, u.MAmmo - u.Units * 0.045f);
                        u.Signature = Math.Min(1f, u.Signature + 0.16f);
                        t.Supp = Math.Min(1f, t.Supp + 0.15f);
                    }
                }
                else if (bestDist < 0.45f)
                {
                    // پیاده در برابر زره: فقط در فاصله‌ی خیلی نزدیک و در برابر زره نازک مؤثر
                    float armorResist = 1f / (1f + fspec.Armor / 45f);
                    float kills = u.Units * 0.0055f * k * armorResist;
                    ApplyDamage(foe, best, kills, own, u.Model, true);
                    u.MAmmo = Math.Max(0f, u.MAmmo - u.Units * 0.02f);
                    duel += kills * 0.5f;
                }
            }
        }
        return duel;
    }

    static void ApplyDamage(Force target, int idx, float kills, Force shooter, byte shooterModel, bool armorKill)
    {
        if (kills <= 0f) return;
        ref Group t = ref target.G[idx];
        // مدافعِ سنگرگرفته سخت‌تر کشته می‌شود
        if (!target.IsAttacker && t.Posture is P_DEFEND or P_AMBUSH or P_HOLD) kills *= 1f - ENTRENCH;
        if (kills <= 0f) return;
        float actual = Math.Min(kills, t.Units);
        t.Units = Math.Max(0f, t.Units - actual);
        t.Knocked += actual;
        if (t.Type == 1 && target.ModelKnocked.Length > 0) target.ModelKnocked[t.Model] += actual;
        else if (t.Type == 0) target.SoldiersKnocked += actual;

        if (armorKill && t.Type == 1 && shooter.ModelKills.Length > shooterModel)
            shooter.ModelKills[shooterModel] += (long)Math.Round(actual);

        t.Morale = Math.Max(0f, t.Morale - actual / Math.Max(1f, t.Size0) * (1.6f / Math.Max(0.5f, target.Prof.MoraleResist)));
        if (t.Units < t.Size0 * 0.08f || t.Units < 0.5f)
        {
            t.Alive = false;
            shooter.IntelOnFoe[idx].Level = 0f;
        }
    }

    static void MoraleSide(Force f, Field field, int tick, BattleLog log, ref XorRng rng, ref int routs)
    {
        for (int i = 0; i < f.N; i++)
        {
            ref Group u = ref f.G[i];
            if (!u.Alive) continue;
            u.Supp = Math.Max(0f, u.Supp - 0.08f);
            u.Morale = Math.Min(1f, u.Morale + 0.004f * f.Prof.MoraleResist);
            bool active = u.Posture is P_ADVANCE or P_ASSAULT or P_FLANK or P_RETREAT;
            u.Fatigue = Math.Clamp(u.Fatigue + (active ? 0.006f : -0.004f), 0f, 1f);
            if (u.Supp > 0.1f) u.Exp = Math.Min(1f, u.Exp + 0.003f);
            if (u.Posture == P_REGROUP) u.Morale = Math.Min(1f, u.Morale + 0.02f * f.Prof.MoraleResist);

            float lossR = 1f - u.Units / Math.Max(1f, u.Size0);

            // مدافع تحت فشار، به‌جای مردن سر جا، کمی عقب می‌کشد و دوباره سنگر می‌گیرد
            if (!f.IsAttacker && lossR > 0.35f && u.Morale < 0.55f && u.Posture != P_RETREAT
                && rng.NextF() < FALLBACK_P)
            {
                u.Y = Math.Min(DEPTH_KM - 1f, u.Y + rng.Range(1.5f, 3.5f));
                u.TgtY = u.Y;
                u.Posture = P_DEFEND;
                u.Sprung = true;
                u.Morale = Math.Min(1f, u.Morale + 0.12f);
            }

            float breakP = 0.12f / Math.Max(0.5f, f.Prof.MoraleResist);
            if (lossR > 0.5f && u.Morale < 0.3f && rng.NextF() < breakP)
            {
                if (u.Posture != P_RETREAT) routs++;
                u.Posture = P_RETREAT;
                u.TgtY = f.IsAttacker ? -5f : Math.Min(DEPTH_KM, u.Y + 8f);
            }
        }
    }

    // عمق مؤثر = عمقی که مهاجم واقعاً «نگه داشته»، نه جایی که یک گروه تکی رسیده.
    //  شرط: در یک سکتور، نیروی قابل‌توجه مهاجم پشت‌سرِ هم و بدون مقاومت پشت‌جبهه.
    static float EffectiveDepth(Force f, Force foe)
    {
        Span<float> sectorDepth = stackalloc float[SECTORS];
        Span<float> sectorMass = stackalloc float[SECTORS];
        for (int s = 0; s < SECTORS; s++) { sectorDepth[s] = 0f; sectorMass[s] = 0f; }

        // توان مهاجم و مدافع در هر سکتور
        Span<float> atkPow = stackalloc float[SECTORS];
        Span<float> defPow = stackalloc float[SECTORS];
        for (int s = 0; s < SECTORS; s++) { atkPow[s] = 0f; defPow[s] = 0f; }

        for (int i = 0; i < f.N; i++)
        {
            ref Group g = ref f.G[i];
            if (!g.Alive || g.Posture is P_RETREAT or P_REGROUP) continue;
            int s = Math.Clamp((int)(g.X / SECTOR_KM), 0, SECTORS - 1);
            atkPow[s] += g.Type == 1 ? g.Units * 10f : g.Units;
        }
        for (int j = 0; j < foe.N; j++)
        {
            ref Group e = ref foe.G[j];
            if (!e.Alive || e.Posture == P_RETREAT) continue;
            int s = Math.Clamp((int)(e.X / SECTOR_KM), 0, SECTORS - 1);
            defPow[s] += e.Type == 1 ? e.Units * 10f : e.Units;
        }

        float best = 0f;
        for (int s = 0; s < SECTORS; s++)
        {
            if (atkPow[s] < 60f) continue;
            // برای نگه‌داشتن یک سکتور، مهاجم باید برتری محلی داشته باشد
            float dom = atkPow[s] / Math.Max(1f, atkPow[s] + defPow[s]);
            if (dom < SECTOR_DOM) continue;

            // عمقی که «توده»ی مهاجم در آن سکتور به آن رسیده (نه نوکِ تیز):
            // عمیق‌ترین Y که دست‌کم ۳۵٪ توان سکتور در آن یا جلوتر از آن است.
            float target = atkPow[s] * 0.35f;
            float d = 0f;
            for (int i = 0; i < f.N; i++)
            {
                ref Group g = ref f.G[i];
                if (!g.Alive || g.Posture is P_RETREAT or P_REGROUP) continue;
                if (Math.Clamp((int)(g.X / SECTOR_KM), 0, SECTORS - 1) != s) continue;
                if (g.Y <= d) continue;
                float massAtOrBeyond = 0f;
                for (int j = 0; j < f.N; j++)
                {
                    ref Group o = ref f.G[j];
                    if (!o.Alive || o.Posture is P_RETREAT or P_REGROUP) continue;
                    if (Math.Clamp((int)(o.X / SECTOR_KM), 0, SECTORS - 1) != s) continue;
                    if (o.Y >= g.Y) massAtOrBeyond += o.Type == 1 ? o.Units * 10f : o.Units;
                }
                if (massAtOrBeyond >= target) d = g.Y;
            }
            // برتری محلی هرچه بیشتر، تثبیت زمین بیشتر
            d *= dom > 0.85f ? 1.0f : 0.55f + Math.Clamp((dom - SECTOR_DOM) / Math.Max(0.01f, 0.85f - SECTOR_DOM), 0f, 1f) * 0.45f;
            if (d > best) best = d;
        }

        // دروازه‌ی برتری کلی: با ارتشِ فرسوده نمی‌شود عمق را نگه داشت
        float totalA = 0f, totalD = 0f;
        for (int s = 0; s < SECTORS; s++) { totalA += atkPow[s]; totalD += defPow[s]; }
        float global = totalA / Math.Max(1f, totalA + totalD);
        float cap = Math.Clamp((global - GLOBAL_DOM) / Math.Max(0.01f, 0.85f - GLOBAL_DOM), 0f, 1f);
        best *= 0.30f + 0.70f * cap;

        return Math.Max(0f, best);
    }

    static float SidePower(Force f)
    {
        float p = 0f;
        for (int i = 0; i < f.N; i++)
        {
            if (!f.G[i].Alive) continue;
            float ammoR = (f.G[i].CAmmo + f.G[i].MAmmo) / Math.Max(0.01f, f.G[i].Size0 * 2f);
            float am = 0.45f + 0.55f * Math.Clamp(ammoR * 1.6f, 0f, 1f);
            if (f.G[i].Type == 1)
            {
                var s = f.Specs[f.G[i].Model];
                p += f.G[i].Units * (8f + s.Armor * 0.04f + s.Pen * 0.04f) * am;
            }
            else p += f.G[i].Units * 0.85f * am;
        }
        return p;
    }

    static float SupplyFactor(float depth, FactionProfile prof)
    {
        if (depth <= 10f) return 1f;
        return Math.Clamp(1f - (depth - 10f) / 50f, prof.SupplyFloor, 1f);
    }

    // ═════════════════════════ فاز هوایی ════════════════════════════════════
    static AirOutcome RunAirPhase(Country atk, Country def, Field field,
        long aFight, long aBomb, int aAirStrat, int aAirTac,
        long dFight, long dAA, int dAirStrat, int dAirTac,
        FighterSpec aFs, BomberSpec aBs, FighterSpec dFs,
        FactionProfile aProf, FactionProfile dProf, ref XorRng rng)
    {
        var o = new AirOutcome { CasAtk = 1f, CasDef = 1f };
        o.AtkHadAir = (aFight + aBomb) > 0;
        o.DefHadAir = (dFight + dAA) > 0;
        if (!o.AtkHadAir && !o.DefHadAir) return o;

        float wxAir = WxAir[field.Weather] * TimeAir[field.StartTime];

        float aFamil = Familiarity(atk.Faction, aFs.Origin, aProf);
        float dFamil = Familiarity(def.Faction, dFs.Origin, dProf);
        float aQ = (aFs.Maneuver * 0.55f + aFs.Firepower * 0.45f) * aProf.CrewQuality * aFamil;
        float dQ = (dFs.Maneuver * 0.55f + dFs.Firepower * 0.45f) * dProf.CrewQuality * dFamil;

        float capBonus = (dAirStrat == 1 && dAirTac == 1) ? 1.25f : 1f;
        float flakBonus = (dAirStrat == 2 && dAirTac == 1) ? 1.35f : 1f;
        if (dAirStrat == 2 && dAirTac == 2) capBonus *= 1.1f;

        float aPow = aFight * aQ * wxAir * rng.Range(0.9f, 1.1f);
        float dPow = dFight * dQ * capBonus * rng.Range(0.9f, 1.1f);

        long aFightLost = 0, dFightLost = 0;
        if (aFight > 0 && dFight > 0)
        {
            o.HadAirCombat = true;
            float total = aPow + dPow;
            float aLossFrac = Math.Clamp(dPow / Math.Max(1f, total) * rng.Range(0.7f, 1.1f), 0f, 0.95f);
            float dLossFrac = Math.Clamp(aPow / Math.Max(1f, total) * rng.Range(0.7f, 1.1f), 0f, 0.95f);
            aFightLost = (long)Math.Round(aFight * aLossFrac);
            dFightLost = (long)Math.Round(dFight * dLossFrac);
        }

        long aBombLost = 0, dAALost = 0;
        if (aBomb > 0 && dFight > 0 && aFight == 0)
        {
            o.HadAirCombat = true;
            float intercept = dFight * dQ * capBonus * rng.Range(0.8f, 1.1f);
            long got = (long)Math.Round(Math.Min(aBomb, intercept * 0.015f / (1f + aBs.Armor * 0.3f)));
            aBombLost += Math.Min(aBomb, got);
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
        float atkRemain = aFightLeft * aQ + aBombLeft * 1.0f;
        float defRemain = dFightLeft * dQ + dAA * 0.5f;
        o.Superiority = Math.Clamp((atkRemain - defRemain) / Math.Max(1f, atkRemain + defRemain), -1f, 1f);

        if (aAirStrat == 1)
        {
            if (aAirTac == 2 && aBombLeft > 0 && dFightLeft > 0)
            {
                float raid = aBombLeft * (aBs.Bombload / 3600f) * wxAir * (0.5f + 0.5f * Math.Clamp(o.Superiority + 0.5f, 0f, 1f));
                long grounded = (long)Math.Round(Math.Min(dFightLeft, raid * rng.Range(0.6f, 1.0f)));
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
            float perBomber = aBs.Bombload / 3600f;
            float intensity = effBomb * perBomber;
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
        o.Narrative = BuildAirNarrative(o, aFight, aBomb, dFight, dAA, aAirStrat, aAirTac, aFs, aBs, dFs, field);
        return o;
    }

    static string BuildAirNarrative(AirOutcome air, long aFight, long aBomb, long dFight, long dAA,
        int aAirStrat, int aAirTac, FighterSpec aFs, BomberSpec aBs, FighterSpec dFs, Field field)
    {
        if (aFight == 0 && aBomb == 0 && dFight == 0 && dAA == 0) return null;
        var s = new StringBuilder();
        if (WxAir[field.Weather] < 0.7f)
            s.Append($"هوای {WeatherName[field.Weather]} پرواز را سخت کرد؛ ");

        if (air.HadAirCombat)
            s.Append($"{aFs.Name}های مهاجم با {dFs.Name}های مدافع درگیر شدند و {air.AtkFightersLost} در برابر {air.DefFightersLost} جنگنده سرنگون شد. ");
        else if (aFight > 0 && dFight == 0)
            s.Append($"{aFs.Name}ها بدون مقاومت هوایی آسمان را در اختیار گرفتند. ");

        if (dAA > 0 && (aBomb > 0 || aFight > 0))
            s.Append($"آتش پدافند {air.AtkBombersLost} بمب‌افکن را زد و خودش {air.DefAntiAirLost} قبضه از دست داد. ");

        if (aAirStrat == 2 && (air.StratMoney > 0 || air.StratIron > 0))
            s.Append(aAirTac == 1
                ? $"بمباران دقیق صنایع، {air.StratMoney / 1000.0:F1}K پول و {air.StratIron / 1000.0:F1}K آهن از اقتصاد دشمن را نابود کرد. "
                : $"بمباران منطقه‌ای شهرها {air.StratMoney / 1000.0:F1}K پول خسارت زد و روحیه‌ی عمومی را کوبید. ");
        else if (aAirStrat == 1 && air.Superiority > 0.15)
            s.Append("با برتری در آسمان، پشتیبانی نزدیک هوایی مستقیم روی سر مدافع کار کرد. ");
        else if (air.Superiority < -0.15)
            s.Append("آسمان دست مدافع افتاد و ستون‌های مهاجم زیر فشار هوایی حرکت کردند. ");

        return s.Length > 0 ? s.ToString().TrimEnd() : null;
    }

    // ======================================================================
    //  بخش ۳ — API عمومی، حلقه‌ی اصلی نبرد، جمع‌بندی تلفات، برخورد دکترین‌ها
    // ======================================================================

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

    // ======================================================================
    //  بخش ۴ — ساخت گزارش‌های نبرد (گروه / مهاجم / مدافع)
    // ======================================================================

    static string Bar(float frac, int color)
    {
        int filled = (int)Math.Round(Math.Clamp(frac, 0f, 1f) * 10);
        string fill = color == 1 ? "🟩" : color == 2 ? "🟥" : "🟨";
        var sb = new StringBuilder(24);
        for (int i = 0; i < 10; i++) sb.Append(i < filled ? fill : "⬜");
        return sb.ToString();
    }

    static string Num(long v) => v.ToString("N0");
    static string K(long v) => v >= 1000 ? $"{v / 1000.0:F1}K" : v.ToString();

    static string AirSupText(double sup)
    {
        if (sup > 0.4) return "قاطع با مهاجم 🟢";
        if (sup > 0.12) return "نسبی با مهاجم";
        if (sup < -0.4) return "قاطع با مدافع 🔴";
        if (sup < -0.12) return "نسبی با مدافع";
        return "متوازن ⚪";
    }

    // ─────────── خط تلفات به تفکیک مدل ───────────
    static string ModelLossLines(Force f, string indent = "   ")
    {
        if (f == null || f.ModelNames.Length == 0) return null;
        var sb = new StringBuilder();
        for (int i = 0; i < f.ModelNames.Length; i++)
        {
            if (f.ModelSent[i] <= 0) continue;
            long lost = f.ModelLost[i];
            long left = Math.Max(0, f.ModelSent[i] - lost);
            int pct = (int)Math.Round(100.0 * lost / Math.Max(1, f.ModelSent[i]));
            string origin = f.Specs[i].Origin == f.Owner ? "" : $" (تجهیز {FactionFa(f.Specs[i].Origin)})";
            sb.Append($"{indent}• {f.ModelNames[i]}{origin}: {Num(lost)} از {Num(f.ModelSent[i])} منهدم ({pct}٪) — {Num(left)} سالم");
            if (f.ModelKills[i] > 0) sb.Append($" | {Num(f.ModelKills[i])} زره دشمن زد");
            sb.Append('\n');
        }
        return sb.Length > 0 ? sb.ToString().TrimEnd('\n') : null;
    }

    // ─────────── تحلیل تقابل زره: کدام مدل مقابل کدام مدل ───────────
    static string ArmorMatchupLines(Force own, Force foe)
    {
        if (own == null || foe == null || own.ModelNames.Length == 0 || foe.ModelNames.Length == 0) return null;
        var sb = new StringBuilder();
        // شاخص‌ترین مدل هر طرف
        int oi = 0; for (int i = 1; i < own.ModelSent.Length; i++) if (own.ModelSent[i] > own.ModelSent[oi]) oi = i;
        int fi = 0; for (int i = 1; i < foe.ModelSent.Length; i++) if (foe.ModelSent[i] > foe.ModelSent[fi]) fi = i;
        if (own.ModelSent[oi] <= 0 || foe.ModelSent[fi] <= 0) return null;

        var os = own.Specs[oi]; var fs = foe.Specs[fi];
        float penGap = os.Pen - fs.Armor;
        float defGap = fs.Pen - os.Armor;

        string verdict;
        if (penGap > 12f && defGap < 0f)
            verdict = $"{os.Name} با نفوذ {os.Pen:F0} میلی‌متری، زره {fs.Armor:F0} میلی‌متری {fs.Name} را از فاصله‌ی معمول می‌درید، ولی گلوله‌ی {fs.Name} روی زره‌ی {os.Armor:F0} میلی‌متری آن کمانه می‌کرد.";
        else if (penGap < 0f && defGap > 12f)
            verdict = $"زره {fs.Armor:F0} میلی‌متری {fs.Name} در برابر نفوذ {os.Pen:F0} میلی‌متری {os.Name} تقریباً مصون بود؛ ولی توپ {fs.Name} زره‌ی نازک‌تر {os.Name} را راحت می‌شکافت.";
        else if (penGap > 0f && defGap > 0f)
            verdict = $"{os.Name} و {fs.Name} هر دو زره‌ی هم را می‌زدند؛ برنده هر تک‌درگیری، آن‌که زودتر شلیک می‌کرد.";
        else
            verdict = $"نه {os.Name} و نه {fs.Name} نمی‌توانستند به‌راحتی زره‌ی هم را بشکافند؛ نبرد زرهی به فرسایش و مانور کشید.";

        sb.Append(verdict);

        if (own.Owner != os.Origin)
            sb.Append($" ضمناً خدمه‌ی {FactionFa(own.Owner)} روی زره‌ی {FactionFa(os.Origin)} می‌جنگیدند و کارایی‌شان حدود {(int)Math.Round((1f - own.Prof.ForeignAdapt) * 100)}٪ کمتر از خدمه‌ی بومی همان تانک بود.");

        return sb.ToString();
    }

    // ─────────── تحلیل فکشن ───────────
    static string FactionAnalysis(Force fa, Force fd)
    {
        if (fa == null) return null;
        var sb = new StringBuilder();
        sb.Append($"دکترین {FactionFa(fa.Owner)} مهاجم: {fa.Prof.Doctrine}.");
        if (fd != null)
            sb.Append($"\n   دکترین {FactionFa(fd.Owner)} مدافع: {fd.Prof.Doctrine}.");
        return sb.ToString();
    }

    // ─────────── خط زمانی نبرد ───────────
    static string Timeline(BattleLog log, byte side, int max = 14)
    {
        var items = log.For(side).OrderBy(x => x.Tick).Take(max).ToList();
        if (items.Count == 0) return null;
        var sb = new StringBuilder();
        foreach (var it in items)
        {
            string icon = it.Kind switch
            {
                LG_PLAN => "🗺",
                LG_DECISION => "🧠",
                LG_COMBAT => "💥",
                LG_BREAK => "🔓",
                LG_CRISIS => "⚠️",
                LG_AIR => "🛫",
                _ => "🌦"
            };
            sb.Append($"{icon} <code>{Clock(it.Tick)}</code> — {Esc(it.Text)}\n");
        }
        return sb.ToString().TrimEnd('\n');
    }

    static string Esc(string s) => s == null ? "" : s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    // ═════════════════════════ گزارش‌های زمینی ══════════════════════════════
    static void BuildGroundReports(BattleResult r, Country atk, Country def,
        Force fa, Force fd, Field field, BattleLog log, AirOutcome air,
        int aStrat, int aTac, int dStrat, int dTac,
        int aAirStrat, int aAirTac, int dAirStrat, int dAirTac,
        List<(string Model, long Count)> aTankList, List<(string Model, long Count)> dTankList,
        List<(string Model, long Count)> aFighterList, List<(string Model, long Count)> dFighterList,
        List<(string Model, long Count)> aBomberList,
        long aTanks, long aSold, long dTanks, long dSold,
        long aFight, long aBomb, long dFight, long dAA,
        float frac, float depth, float stratAdv,
        bool anyGround, bool defHasGround, float aRecover, float dRecover,
        int routsA, int routsD)
    {
        string aDoc = AtkDoctrineText(aStrat * 10 + aTac);
        string dDoc = DefDoctrineText(dStrat * 10 + dTac);
        string aAirName = aAirStrat == 1 ? "برتری هوایی" : aAirStrat == 2 ? "بمباران راهبردی" : "بدون عملیات هوایی";
        string aAirTacName = aAirStrat == 1 ? (aAirTac == 1 ? "شکار آزاد" : "حمله به پایگاه‌ها")
                           : aAirStrat == 2 ? (aAirTac == 1 ? "بمباران دقیق" : "بمباران منطقه‌ای") : "—";
        string dAirName = dAirStrat == 1 ? "دفاع منطقه‌ای" : "دفاع نقطه‌ای";
        string dAirTacName = dAirStrat == 1 ? (dAirTac == 1 ? "گشت هوایی رزمی" : "ایستگاه شنود")
                                            : (dAirTac == 1 ? "آتشبند" : "پوشش مستقیم جنگنده");

        string outcome;
        if (!anyGround)
            outcome = air.Superiority > 0.12 ? $"🛫 عملیات هوایی موفق {Esc(atk.Name)}"
                    : air.Superiority < -0.12 ? $"🛫 عملیات هوایی ناکام — آسمان با {Esc(def.Name)}"
                    : "🛫 عملیات هوایی بی‌نتیجه";
        else if (r.AttackerWon) outcome = $"🏆 پیروزی قاطع {Esc(atk.Name)} — جبهه شکست";
        else if (r.AttackerFailed) outcome = $"🛡 دفاع کامل {Esc(def.Name)} — حمله خنثی شد";
        else if (r.SuccessPercent >= 60) outcome = $"⚔️ رخنه‌ی جدی مهاجم ({r.SuccessPercent}٪)";
        else if (r.SuccessPercent >= 30) outcome = $"⚖️ نبرد فرسایشی بی‌نتیجه ({r.SuccessPercent}٪)";
        else outcome = $"🛡 مهاجم زمین‌گیر شد ({r.SuccessPercent}٪)";

        int h = r.DurationMinutes / 60, m = r.DurationMinutes % 60;
        byte terr = field.DominantTerrainNear(FRONT_KM / 2f);
        string env = $"🌦 {WeatherName[field.Weather]} | 🕓 شروع در {TimeName[field.StartTime]} | 🏞 زمین غالب: {TerName[terr]}";

        string advText = stratAdv > 1.12f ? $"استراتژی مهاجم پادزهر انتخاب مدافع بود (مزیت {stratAdv:F2}×)"
                       : stratAdv < 0.92f ? $"انتخاب مدافع دقیقاً نقطه‌ضعف طرح مهاجم را گرفت (مزیت {stratAdv:F2}× به ضرر مهاجم)"
                       : $"دو طرح تقریباً هم‌وزن بودند ({stratAdv:F2}×)";

        string armorMatch = ArmorMatchupLines(fa, fd);
        string aModels = ModelLossLines(fa);
        string dModels = ModelLossLines(fd);
        string factionText = FactionAnalysis(fa, fd);

        string why = r.AttackerWon
            ? "تمرکز به‌موقع قوا روی نازک‌ترین بخش خط و توسعه‌ی سریع رخنه، کار دفاع را تمام کرد."
            : r.AttackerFailed
            ? "آتش دفاعی سازمان‌یافته و زمین مساعد، حمله را پیش از شکل‌گیری رخنه خفه کرد."
            : "هیچ طرف نتوانست ضربه‌ی قاطع بزند؛ نبرد به فرسایش کشید و جبهه تقریباً سرجایش ماند.";

        string intelText = fa != null && fd != null
            ? (fa.IntelQuality > fd.IntelQuality + 0.12f ? "برتری شناسایی با مهاجم بود و آتشش دقیق‌تر نشست."
             : fd.IntelQuality > fa.IntelQuality + 0.12f ? "مه جنگ به سود مدافع کار کرد؛ مهاجم بارها کورکورانه شلیک کرد."
             : "هیچ طرفی برتری اطلاعاتی قاطع نداشت.")
            : null;

        // ═══════════════════ گزارش مهاجم ═══════════════════
        var sb = new StringBuilder(3000);
        sb.Append($"⚔️ <b>گزارش نبرد — {Esc(atk.Name)} علیه {Esc(def.Name)}</b>\n");
        sb.Append($"{outcome}\n");
        sb.Append($"{env}\n");
        if (anyGround)
        {
            sb.Append($"📊 پیشروی: {Bar(frac, r.AttackerWon ? 1 : r.AttackerFailed ? 2 : 0)} <b>{r.SuccessPercent}٪</b>\n");
            sb.Append($"📍 نفوذ مؤثر: <b>{r.PenetrationKm:F1}</b> کیلومتر از {WIN_DEPTH:F0} | ⏱ {h} ساعت و {m} دقیقه\n");
        }
        else sb.Append($"⏱ مدت عملیات: {h} ساعت و {m} دقیقه\n");

        sb.Append("\n<b>🎯 طرح عملیات</b>\n");
        sb.Append($"• طرح شما: {Esc(aDoc)}\n");
        sb.Append($"• طرح دشمن: {Esc(dDoc)}\n");
        if (anyGround && defHasGround) sb.Append($"• {Esc(advText)}\n");
        if (aFight > 0 || aBomb > 0) sb.Append($"• هوایی: {Esc(aAirName)} / {Esc(aAirTacName)}\n");

        string tlA = Timeline(log, 0);
        if (tlA != null)
        {
            sb.Append("\n<b>📜 خط زمانی نبرد</b>\n");
            sb.Append(tlA).Append('\n');
        }

        if (anyGround && defHasGround)
        {
            sb.Append("\n<b>🛡 تقابل زرهی</b>\n");
            if (armorMatch != null) sb.Append($"• {Esc(armorMatch)}\n");
            if (intelText != null) sb.Append($"• {Esc(intelText)}\n");
            if (routsD > 0) sb.Append($"• {routsD} یگان مدافع در جریان نبرد از هم پاشید.\n");
            if (routsA > 0) sb.Append($"• {routsA} یگان خودی زیر فشار عقب کشید.\n");
        }

        if (factionText != null)
        {
            sb.Append("\n<b>🏭 عامل فکشن</b>\n");
            sb.Append($"• {Esc(factionText)}\n");
            if (fa != null && aRecover > 0.02f)
                sb.Append($"• تعمیرگاه‌های صحرایی شما حدود {(int)Math.Round(Math.Clamp(aRecover, 0f, 0.6f) * 100)}٪ از تجهیزات از کار افتاده را به خط برگرداندند.\n");
            if (fa != null && fa.ForeignShare() > 0.05f)
                sb.Append($"• {(int)Math.Round(fa.ForeignShare() * 100)}٪ از زره شما ساخت فکشن دیگری بود؛ خدمه با آن کندتر کار کردند.\n");
        }

        sb.Append("\n<b>💀 تلفات شما</b>\n");
        sb.Append($"   🛡 تانک: {Num(r.AttackerTanksLost)} از {Num(aTanks)} | 🪖 سرباز: {Num(r.AttackerSoldiersLost)} از {Num(aSold)}\n");
        if (aModels != null) sb.Append(aModels).Append('\n');
        if (aFight > 0 || aBomb > 0)
            sb.Append($"   ✈️ جنگنده: {Num(r.AttackerFightersLost)} از {Num(aFight)} | 🛩 بمب‌افکن: {Num(r.AttackerBombersLost)} از {Num(aBomb)}\n");

        sb.Append("\n<b>💀 تلفات دشمن</b>\n");
        sb.Append($"   🛡 تانک: {Num(r.DefenderTanksLost)} از {Num(dTanks)} | 🪖 سرباز: {Num(r.DefenderSoldiersLost)} از {Num(dSold)}\n");
        if (dModels != null) sb.Append(dModels).Append('\n');
        if (dFight > 0 || dAA > 0)
            sb.Append($"   ✈️ جنگنده: {Num(r.DefenderFightersLost)} از {Num(dFight)} | 🎯 پدافند: {Num(r.DefenderAntiAirLost)} از {Num(dAA)}\n");

        if (aFight > 0 || aBomb > 0 || dFight > 0 || dAA > 0)
            sb.Append($"\n🛫 برتری هوایی: {AirSupText(air.Superiority)}\n");

        sb.Append("\n<b>💰 نتیجه‌ی اقتصادی</b>\n");
        if (r.AttackerMoneyGained > 0 || r.AttackerIronGained > 0)
            sb.Append($"   غنیمت: {K(r.AttackerMoneyGained)} پول، {K(r.AttackerIronGained)} آهن\n");
        else
            sb.Append("   غنیمتی به دست نیامد (غارت فقط با پیشروی زمینی ممکن است)\n");
        if (air.StratMoney > 0 || air.StratIron > 0)
            sb.Append($"   خسارت بمباران به اقتصاد دشمن: {K(air.StratMoney)} پول، {K(air.StratIron)} آهن (نابود شد، غنیمت نیست)\n");

        sb.Append($"\n<b>🧠 جمع‌بندی:</b> {Esc(why)}");
        r.AttackerReport = sb.ToString();

        // ═══════════════════ گزارش مدافع ═══════════════════
        sb.Clear();
        sb.Append($"🛡 <b>گزارش دفاع — {Esc(def.Name)} در برابر {Esc(atk.Name)}</b>\n");
        sb.Append($"{outcome}\n");
        sb.Append($"{env}\n");
        if (anyGround)
        {
            sb.Append($"📊 پیشروی دشمن: {Bar(frac, r.AttackerFailed ? 1 : r.AttackerWon ? 2 : 0)} <b>{r.SuccessPercent}٪</b>\n");
            sb.Append($"📍 نفوذ دشمن: <b>{r.PenetrationKm:F1}</b> کیلومتر | ⏱ {h} ساعت و {m} دقیقه\n");
        }

        sb.Append("\n<b>🎯 طرح‌ها</b>\n");
        sb.Append($"• دفاع شما: {Esc(dDoc)}\n");
        sb.Append($"• حمله‌ی دشمن: {Esc(aDoc)}\n");
        if (dFight > 0 || dAA > 0) sb.Append($"• پدافند هوایی شما: {Esc(dAirName)} / {Esc(dAirTacName)}\n");

        string tlD = Timeline(log, 1);
        if (tlD != null)
        {
            sb.Append("\n<b>📜 خط زمانی نبرد</b>\n");
            sb.Append(tlD).Append('\n');
        }

        if (anyGround && defHasGround)
        {
            sb.Append("\n<b>🛡 تقابل زرهی</b>\n");
            string armorMatchD = ArmorMatchupLines(fd, fa);
            if (armorMatchD != null) sb.Append($"• {Esc(armorMatchD)}\n");
            if (intelText != null) sb.Append($"• {Esc(intelText)}\n");
        }

        if (fd != null)
        {
            sb.Append("\n<b>🏭 عامل فکشن</b>\n");
            sb.Append($"• {Esc(fd.Prof.Doctrine)}\n");
            if (dRecover > 0.02f)
                sb.Append($"• چون میدان دست شما ماند، حدود {(int)Math.Round(Math.Clamp(dRecover, 0f, 0.6f) * 100)}٪ از تجهیزات زمین‌گیرشده بازیابی شد.\n");
            if (fd.ForeignShare() > 0.05f)
                sb.Append($"• {(int)Math.Round(fd.ForeignShare() * 100)}٪ از زره شما خارجی بود و خدمه با آن کندتر کار کردند.\n");
        }

        sb.Append("\n<b>💀 تلفات شما</b>\n");
        sb.Append($"   🛡 تانک: {Num(r.DefenderTanksLost)} از {Num(dTanks)} | 🪖 سرباز: {Num(r.DefenderSoldiersLost)} از {Num(dSold)}\n");
        if (dModels != null) sb.Append(dModels).Append('\n');
        if (dFight > 0 || dAA > 0)
            sb.Append($"   ✈️ جنگنده: {Num(r.DefenderFightersLost)} از {Num(dFight)} | 🎯 پدافند: {Num(r.DefenderAntiAirLost)} از {Num(dAA)}\n");

        sb.Append("\n<b>💀 تلفات دشمن</b>\n");
        sb.Append($"   🛡 تانک: {Num(r.AttackerTanksLost)} از {Num(aTanks)} | 🪖 سرباز: {Num(r.AttackerSoldiersLost)} از {Num(aSold)}\n");
        if (aModels != null) sb.Append(aModels).Append('\n');
        if (aFight > 0 || aBomb > 0)
            sb.Append($"   ✈️ جنگنده: {Num(r.AttackerFightersLost)} از {Num(aFight)} | 🛩 بمب‌افکن: {Num(r.AttackerBombersLost)} از {Num(aBomb)}\n");

        if (aFight > 0 || aBomb > 0 || dFight > 0 || dAA > 0)
            sb.Append($"\n🛫 برتری هوایی: {AirSupText(air.Superiority)}\n");

        sb.Append($"\n💸 خسارت اقتصادی شما: {K(r.DefenderMoneyLost)} پول، {K(r.DefenderIronLost)} آهن\n");
        sb.Append($"\n<b>🧠 جمع‌بندی:</b> {Esc(why)}");
        r.DefenderReport = sb.ToString();

        // ═══════════════════ اعلامیه‌ی گروه ═══════════════════
        sb.Clear();
        sb.Append("📰 <b>خبر جنگ</b>\n");
        sb.Append("━━━━━━━━━━━━━━━\n");
        sb.Append($"⚔️ <b>{Esc(atk.Name)}</b> به <b>{Esc(def.Name)}</b> حمله کرد\n");
        sb.Append($"{outcome}\n");
        sb.Append($"{env}\n");
        if (anyGround)
        {
            sb.Append($"\n{Bar(frac, r.AttackerWon ? 1 : r.AttackerFailed ? 2 : 0)} <b>{r.SuccessPercent}٪</b>\n");
            sb.Append($"📍 نفوذ: {r.PenetrationKm:F1} کیلومتر | ⏱ {h}:{m:D2}\n");
        }
        else sb.Append($"\n⏱ {h}:{m:D2}\n");

        sb.Append($"\n🎯 <b>{Esc(atk.Name)}</b>: {Esc(aDoc)}\n");
        sb.Append($"🛡 <b>{Esc(def.Name)}</b>: {Esc(dDoc)}\n");

        // مهم‌ترین لحظه‌ی نبرد برای گروه
        var highlight = log.Items
            .Where(x => x.Kind is LG_BREAK or LG_CRISIS or LG_COMBAT)
            .OrderByDescending(x => x.Kind == LG_BREAK ? 2 : x.Kind == LG_CRISIS ? 1 : 0)
            .ThenBy(x => x.Tick)
            .FirstOrDefault();
        if (highlight.Text != null)
            sb.Append($"\n💥 <code>{Clock(highlight.Tick)}</code> {Esc(highlight.Text)}\n");

        sb.Append("\n<b>💀 تلفات</b>\n");
        sb.Append($"مهاجم: {Num(r.AttackerTanksLost)}🛡 {Num(r.AttackerSoldiersLost)}🪖");
        if (aFight > 0 || aBomb > 0) sb.Append($" {Num(r.AttackerFightersLost)}✈️ {Num(r.AttackerBombersLost)}🛩");
        sb.Append('\n');
        sb.Append($"مدافع: {Num(r.DefenderTanksLost)}🛡 {Num(r.DefenderSoldiersLost)}🪖");
        if (dFight > 0 || dAA > 0) sb.Append($" {Num(r.DefenderFightersLost)}✈️ {Num(r.DefenderAntiAirLost)}🎯");
        sb.Append('\n');

        if (fa != null && fa.ModelNames.Length > 1)
        {
            int worst = 0;
            for (int i = 1; i < fa.ModelLost.Length; i++) if (fa.ModelLost[i] > fa.ModelLost[worst]) worst = i;
            if (fa.ModelLost[worst] > 0)
                sb.Append($"🔧 سنگین‌ترین تلفات زرهی مهاجم روی {Esc(fa.ModelNames[worst])} بود ({Num(fa.ModelLost[worst])} دستگاه)\n");
        }

        if (aFight > 0 || aBomb > 0 || dFight > 0 || dAA > 0)
            sb.Append($"🛫 آسمان: {AirSupText(air.Superiority)}\n");
        if (r.AttackerMoneyGained > 0 || r.AttackerIronGained > 0)
            sb.Append($"💰 غنیمت: {K(r.AttackerMoneyGained)} پول، {K(r.AttackerIronGained)} آهن\n");
        sb.Append("━━━━━━━━━━━━━━━");
        r.GroupAnnouncement = sb.ToString();
    }

    // ======================================================================
    //  بخش ۵ — نبرد دریایی (بدون سوخت؛ قایق فقط دفاع ساحلی)
    // ======================================================================

    public static BattleResult RunNavalBattle(
        Country attacker, Country defender,
        long attBoats, long attSubs, long attBattleships,
        long defBoats, long defSubs, long defBattleships,
        int attStrategy, int attTactic)
    {
        List<(string Model, long Count)> L(string m, long c) => c > 0 ? new List<(string Model, long Count)> { (m, c) } : new List<(string Model, long Count)>();
        return RunNavalBattleAdvanced(attacker, defender,
            L(GetDefaultBoatModel(attacker.Faction), attBoats),
            L(GetDefaultSubModel(attacker.Faction), attSubs),
            L(GetDefaultBattleshipModel(attacker.Faction), attBattleships),
            L(GetDefaultBoatModel(defender.Faction), defBoats),
            L(GetDefaultSubModel(defender.Faction), defSubs),
            L(GetDefaultBattleshipModel(defender.Faction), defBattleships),
            attStrategy, attTactic, 1, 1);
    }

    static string GetDefaultBoatModel(Faction f) => f switch { Faction.USA => "PT Boat", Faction.USSR => "G-5", _ => "S-Boot" };
    static string GetDefaultSubModel(Faction f) => f switch { Faction.USA => "Gato", Faction.USSR => "S-class", _ => "Type VIIC" };
    static string GetDefaultBattleshipModel(Faction f) => f switch { Faction.USA => "Iowa", Faction.USSR => "Sovetsky Soyuz", _ => "Bismarck" };

    // آیا این ترکیب ناوگان اصلاً می‌تواند حمله کند؟ (قایق تنها = خیر)
    public static bool CanNavalAttack(long subs, long battleships) => (subs + battleships) > 0;

    sealed class NavalSide
    {
        public Faction Owner;
        public FactionProfile Prof;
        public string[] BoatModels = Array.Empty<string>();
        public long[] BoatCount = Array.Empty<long>();
        public BoatSpec[] BoatSpecs = Array.Empty<BoatSpec>();
        public string[] SubModels = Array.Empty<string>();
        public long[] SubCount = Array.Empty<long>();
        public SubSpec[] SubSpecs = Array.Empty<SubSpec>();
        public string[] BSModels = Array.Empty<string>();
        public long[] BSCount = Array.Empty<long>();
        public BattleshipSpec[] BSSpecs = Array.Empty<BattleshipSpec>();
        public long[] BoatLost = Array.Empty<long>();
        public long[] SubLost = Array.Empty<long>();
        public long[] BSLost = Array.Empty<long>();

        public long Boats => BoatCount.Sum();
        public long Subs => SubCount.Sum();
        public long BS => BSCount.Sum();
        public long BoatsLost => BoatLost.Sum();
        public long SubsLost => SubLost.Sum();
        public long BSLostTotal => BSLost.Sum();

        public float StrikePower(bool attacking)
        {
            // قدرت ضربه: نبردناو + زیردریایی. قایق فقط در دفاع می‌شمارد.
            float p = 0f;
            for (int i = 0; i < BSCount.Length; i++) p += BSCount[i] * BSSpecs[i].Power;
            for (int i = 0; i < SubCount.Length; i++) p += SubCount[i] * SubSpecs[i].Power;
            if (!attacking)
                for (int i = 0; i < BoatCount.Length; i++) p += BoatCount[i] * BoatSpecs[i].Power;
            else
                for (int i = 0; i < BoatCount.Length; i++) p += BoatCount[i] * BoatSpecs[i].Power * 0.15f; // فقط اسکورت
            return p * Prof.CrewQuality;
        }

        public float Familiar(Faction origin) => Familiarity(Owner, origin, Prof);
    }

    static NavalSide MakeSide(Faction owner,
        List<(string Model, long Count)> boats, List<(string Model, long Count)> subs, List<(string Model, long Count)> bs,
        long capBoats, long capSubs, long capBS)
    {
        var s = new NavalSide { Owner = owner, Prof = ProfileOf(owner) };

        List<(string M, long C)> Clean(List<(string Model, long Count)> src, long cap)
        {
            var o = new List<(string M, long C)>();
            long left = Math.Max(0, cap);
            if (src != null)
                foreach (var (m, c) in src)
                {
                    if (c <= 0 || left <= 0) continue;
                    long take = Math.Min(c, left);
                    o.Add((string.IsNullOrWhiteSpace(m) ? "نامشخص" : m, take));
                    left -= take;
                }
            return o;
        }

        var b = Clean(boats, capBoats);
        s.BoatModels = b.Select(x => x.M).ToArray();
        s.BoatCount = b.Select(x => x.C).ToArray();
        s.BoatSpecs = b.Select(x => GetBoatSpecByModel(x.M)).ToArray();
        s.BoatLost = new long[b.Count];

        var u = Clean(subs, capSubs);
        s.SubModels = u.Select(x => x.M).ToArray();
        s.SubCount = u.Select(x => x.C).ToArray();
        s.SubSpecs = u.Select(x => GetSubSpecByModel(x.M)).ToArray();
        s.SubLost = new long[u.Count];

        var w = Clean(bs, capBS);
        s.BSModels = w.Select(x => x.M).ToArray();
        s.BSCount = w.Select(x => x.C).ToArray();
        s.BSSpecs = w.Select(x => GetBattleshipSpecByModel(x.M)).ToArray();
        s.BSLost = new long[w.Count];

        return s;
    }

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
        ulong seed = (ulong)Interlocked.Increment(ref _seedCounter) ^ ((ulong)attacker.OwnerId << 20) ^ (ulong)DateTime.UtcNow.Ticks;
        var rng = new XorRng(seed);
        var res = new BattleResult { IsNavalBattle = true };
        var log = new BattleLog();

        var A = MakeSide(attacker.Faction, attBoatBreakdown, attSubBreakdown, attBattleshipBreakdown,
            attacker.Boats + attacker.BoatsAtSea, attacker.Submarines + attacker.SubmarinesAtSea, attacker.Battleships + attacker.BattleshipsAtSea);
        var D = MakeSide(defender.Faction, defBoatBreakdown, defSubBreakdown, defBattleshipBreakdown,
            defender.Boats, defender.Submarines, defender.Battleships);

        if (A.Boats + A.Subs + A.BS == 0)
        {
            res.AttackerReport = "⚓ هیچ نیروی دریایی برای حمله ندارید.";
            res.DefenderReport = $"⚓ {defender.Name}: حمله‌ی دریایی {attacker.Name} بدون ناوگان انجام شد و خنثی گردید.";
            res.GroupAnnouncement = $"⚓ {attacker.Name} تلاش ناموفقی برای حمله‌ی دریایی به {defender.Name} داشت.";
            res.AttackerFailed = true;
            return res;
        }

        // قانون جدید: قایق نیروی تهاجمی نیست
        if (A.Subs + A.BS == 0)
        {
            res.AttackerBoatsSurvived = A.Boats;
            res.AttackerReport =
                "🚤 <b>عملیات لغو شد</b>\n" +
                "قایق‌های تندرو واحد گشت ساحلی‌اند، نه نیروی تهاجم دریایی. برد و ظرفیت دریانوردی آن‌ها اجازه‌ی حمله به سواحل دشمن را نمی‌دهد.\n" +
                "برای حمله‌ی دریایی حداقل یک <b>زیردریایی</b> یا <b>نبردناو</b> لازم است. قایق‌ها فقط می‌توانند ناوگان اصلی را اسکورت کنند یا در دفاع از بندر خودتان بجنگند.";
            res.DefenderReport = $"⚓ گشتی‌های {defender.Name} چند قایق تندروی {attacker.Name} را در آب‌های ساحلی دیدند که بدون ناوگان اصلی بازگشتند.";
            res.GroupAnnouncement = $"⚓ ناوگان قایقی {attacker.Name} بدون پشتیبانی نبردناو یا زیردریایی نتوانست به {defender.Name} حمله کند.";
            res.AttackerFailed = true;
            res.SuccessPercent = 0;
            return res;
        }

        float aPower = A.StrikePower(true);
        float dPower = Math.Max(1f, D.StrikePower(false) + defender.PortLevel * 14f);

        float stratAdv = NavalDoctrine(A, D, attStrategy, attTactic, defStrategy, defTactic, defender.PortLevel, log, ref rng);
        float ratio = aPower / dPower;
        float eff = ratio * stratAdv;

        int success;
        if (eff > 2.0f) success = 92 + rng.Next(9);
        else if (eff > 1.5f) success = 72 + rng.Next(21);
        else if (eff > 1.0f) success = 52 + rng.Next(21);
        else if (eff > 0.7f) success = 28 + rng.Next(25);
        else success = rng.Next(28);

        bool attackerWon = success >= 88 || (eff > 1.25f && success >= 70);
        bool attackerFailed = success < 15 || eff < 0.42f;

        // ── ضرایب تلفات ──────────────────────────────────────────────────────
        double attLoss = 0.15 + (1.0 - Math.Clamp(eff, 0, 2) / 2.0) * 0.35;
        double defLoss = 0.15 + Math.Clamp(eff, 0, 2) / 2.0 * 0.45;

        if (attStrategy == 1 && attTactic == 1) { defLoss += 0.10; attLoss -= 0.05; }
        else if (attStrategy == 1 && attTactic == 2) { if (A.BS >= D.BS) { defLoss += 0.08; attLoss -= 0.03; } }
        else if (attStrategy == 2 && attTactic == 1) { attLoss = Math.Max(0.08, attLoss - 0.07); defLoss += 0.12; }
        else if (attStrategy == 2 && attTactic == 2) { attLoss *= 0.85; defLoss *= 0.95; }

        if (defStrategy == 1) { attLoss += 0.07; defLoss -= 0.05; }
        else if (defStrategy == 2 && defTactic == 2 && D.Subs > 0) attLoss += 0.10;
        // قایق‌های مدافع در آب‌های خودی بسیار مؤثرند
        if (D.Boats > 0 && defStrategy == 2 && defTactic == 1) attLoss += 0.06;

        // بازیابی: کشتی آسیب‌دیده در آب‌های خودی راحت‌تر نجات پیدا می‌کند
        attLoss *= 1f - Math.Clamp(A.Prof.Recovery * 0.25f, 0f, 0.2f);
        defLoss *= 1f - Math.Clamp(D.Prof.Recovery * 0.35f, 0f, 0.28f);

        // ── اعمال تلفات به تفکیک مدل ─────────────────────────────────────────
        void ApplyBoat(NavalSide s, double lf, float capMax)
        {
            for (int i = 0; i < s.BoatCount.Length; i++)
            {
                float durability = 1f / (1f + s.BoatSpecs[i].Armor * 0.05f + s.BoatSpecs[i].Speed * 0.006f);
                double p = Math.Clamp(lf * (0.8 + rng.Range(0f, 0.4f)) * durability * 1.6, 0.02, capMax);
                s.BoatLost[i] = (long)Math.Round(s.BoatCount[i] * p);
                s.BoatLost[i] = Math.Min(s.BoatLost[i], s.BoatCount[i]);
            }
        }
        void ApplySub(NavalSide s, double lf, float capMax)
        {
            for (int i = 0; i < s.SubCount.Length; i++)
            {
                float survive = 1f - Math.Clamp((s.SubSpecs[i].Stealth - 60f) / 120f, 0f, 0.35f);
                double p = Math.Clamp(lf * (0.8 + rng.Range(0f, 0.4f)) * survive, 0.02, capMax);
                s.SubLost[i] = Math.Min(s.SubCount[i], (long)Math.Round(s.SubCount[i] * p));
            }
        }

        ApplyBoat(A, attLoss, 0.90f);
        ApplyBoat(D, defLoss, 0.95f);
        ApplySub(A, attLoss, 0.85f);
        ApplySub(D, defLoss, 0.90f);

        bool oneSided = eff > 2.5f || eff < 0.40f;
        long attBSDamage = 0, defBSDamage = 0;

        for (int i = 0; i < A.BSCount.Length; i++)
        {
            if (A.BSCount[i] <= 0) continue;
            double dmgPer = attLoss * 60.0 * (1f - Math.Clamp((A.BSSpecs[i].Belt - 200f) / 700f, 0f, 0.25f));
            if (attStrategy == 2 && attTactic == 1) dmgPer *= 1.2;
            long total = (long)(A.BSCount[i] * dmgPer);
            if (oneSided && eff < 0.5f)
            {
                A.BSLost[i] = Math.Min(A.BSCount[i], (long)Math.Ceiling(total / 100.0 * 0.5));
                attBSDamage += Math.Max(0, total - A.BSLost[i] * 100);
            }
            else attBSDamage += total;
        }
        for (int i = 0; i < D.BSCount.Length; i++)
        {
            if (D.BSCount[i] <= 0) continue;
            double dmgPer = defLoss * 65.0 * (1f - Math.Clamp((D.BSSpecs[i].Belt - 200f) / 700f, 0f, 0.25f));
            long total = (long)(D.BSCount[i] * dmgPer);
            if (oneSided && eff > 2.5f)
            {
                D.BSLost[i] = Math.Min(D.BSCount[i], (long)Math.Ceiling(total / 100.0 * 0.6));
                defBSDamage += Math.Max(0, total - D.BSLost[i] * 100);
            }
            else defBSDamage += total;
        }

        float frac = success / 100f;
        long lootMoney = Math.Min(defender.Money, (long)(defender.Money * 0.15 * frac * 1.5));
        long lootIron = Math.Min(defender.Iron, (long)(defender.Iron * 0.10 * frac * 1.5));

        BuildNavalReports(res, attacker, defender, A, D, log,
            attStrategy, attTactic, defStrategy, defTactic,
            ratio, stratAdv, eff, success, attackerWon, attackerFailed, oneSided,
            attBSDamage, defBSDamage, lootMoney, lootIron, defender.PortLevel);

        res.AttackerBoatsLost = A.BoatsLost;
        res.AttackerSubsLost = A.SubsLost;
        res.AttackerBattleshipsLost = A.BSLostTotal;
        res.AttackerBattleshipDamage = attBSDamage;
        res.DefenderBoatsLost = D.BoatsLost;
        res.DefenderSubsLost = D.SubsLost;
        res.DefenderBattleshipsLost = D.BSLostTotal;
        res.DefenderBattleshipDamage = defBSDamage;
        res.AttackerMoneyGained = lootMoney;
        res.AttackerIronGained = lootIron;
        res.DefenderMoneyLost = lootMoney;
        res.DefenderIronLost = lootIron;
        res.SuccessPercent = success;
        res.AttackerWon = attackerWon;
        res.AttackerFailed = attackerFailed;
        res.PenetrationKm = success;
        res.DurationMinutes = (int)(15 + eff * 20);
        res.AttackerBoatsSurvived = A.Boats - A.BoatsLost;
        res.AttackerSubsSurvived = A.Subs - A.SubsLost;
        res.AttackerBattleshipsSurvived = A.BS - A.BSLostTotal;

        SaveBattle(attacker, defender, res);
        return res;
    }

    static float NavalDoctrine(NavalSide A, NavalSide D, int aStrat, int aTac, int dStrat, int dTac,
        int portLevel, BattleLog log, ref XorRng rng)
    {
        float adv = 1.0f;
        string note;

        if (aStrat == 1 && aTac == 1)
        {
            adv += 0.15f;
            if (A.Subs > D.Subs) adv += 0.08f;
            if (portLevel >= 4) adv -= 0.06f;
            note = A.Subs > 0
                ? "زیردریایی‌ها شبانه به دهانه‌ی بندر نفوذ کردند و پیش از به‌حرکت‌درآمدن ناوگان مدافع اژدر زدند."
                : "نبردناوها در سپیده‌دم آتش را روی لنگرگاه باز کردند.";
        }
        else if (aStrat == 1 && aTac == 2)
        {
            adv += 0.12f;
            if (A.BS >= D.BS) adv += 0.07f;
            note = "مهاجم با مانور فریب، ناوگان مدافع را از پوشش ساحلی به آب‌های آزاد کشاند و آنجا درگیر شد.";
        }
        else if (aStrat == 2 && aTac == 1)
        {
            adv += 0.10f;
            if (A.BS == 0) { adv -= 0.18f; }
            if (portLevel >= 3) adv -= 0.05f;
            note = A.BS > 0
                ? "نبردناوها با توپ‌های اصلی، مواضع ساحلی را پیش از پیاده‌سازی کوبیدند."
                : "بدون نبردناو، بمباران ساحلی عملاً بی‌اثر ماند و زیردریایی‌ها ناچار سطحی جنگیدند.";
        }
        else
        {
            adv += 0.06f;
            note = "پیاده‌سازی موجی: هر موج جای پای موج قبلی را محکم کرد.";
        }

        // پاسخ مدافع
        if (dStrat == 1)
        {
            adv -= portLevel >= 4 ? 0.12f : 0.06f;
            log.Add(1, 1, LG_PLAN, $"مدافع روی مین‌ها، توپخانه‌ی ساحلی و موانع بندر سطح {portLevel} تکیه کرد.");
        }
        else if (dTac == 1)
        {
            if (D.Boats > A.Boats) adv -= 0.09f;
            log.Add(1, 1, LG_PLAN, D.Boats > 0
                ? "دسته‌های قایق تندروی مدافع از پناه ساحل بیرون زدند و ضدحمله‌ی برق‌آسا اجرا کردند."
                : "مدافع قصد ضدحمله‌ی سریع داشت، ولی قایق کافی برای اجرای آن نداشت.");
        }
        else
        {
            if (D.Subs > A.Subs) adv -= 0.09f;
            adv -= D.SubSpecs.Length > 0 ? (D.SubSpecs.Max(x => x.Stealth) - 70f) / 800f : 0f;
            log.Add(1, 1, LG_PLAN, "زیردریایی‌های مدافع در تنگه‌های کم‌عمق کمین کردند.");
        }

        double ratio = (A.Subs + A.BS * 10.0) / Math.Max(1.0, D.Subs + D.BS * 10.0 + D.Boats * 0.3);
        adv += (float)Math.Clamp((ratio - 1.0) * 0.10, -0.15, 0.15);
        adv += rng.Range(-0.04f, 0.04f);

        log.Add(0, 0, LG_PLAN, note);
        return Math.Clamp(adv, 0.70f, 1.40f);
    }

    static string NavalModelLines(NavalSide s, string indent = "   ")
    {
        var sb = new StringBuilder();
        for (int i = 0; i < s.BoatModels.Length; i++)
            if (s.BoatCount[i] > 0)
                sb.Append($"{indent}🚤 {s.BoatModels[i]}: {Num(s.BoatLost[i])} از {Num(s.BoatCount[i])} غرق\n");
        for (int i = 0; i < s.SubModels.Length; i++)
            if (s.SubCount[i] > 0)
                sb.Append($"{indent}⚓ {s.SubModels[i]}: {Num(s.SubLost[i])} از {Num(s.SubCount[i])} غرق\n");
        for (int i = 0; i < s.BSModels.Length; i++)
            if (s.BSCount[i] > 0)
                sb.Append($"{indent}🚢 {s.BSModels[i]}: {Num(s.BSLost[i])} از {Num(s.BSCount[i])} منهدم\n");
        return sb.Length > 0 ? sb.ToString().TrimEnd('\n') : null;
    }

    static void BuildNavalReports(BattleResult r, Country atk, Country def, NavalSide A, NavalSide D, BattleLog log,
        int aStrat, int aTac, int dStrat, int dTac,
        float ratio, float stratAdv, float eff, int success,
        bool won, bool failed, bool oneSided,
        long attBSDamage, long defBSDamage, long lootMoney, long lootIron, int portLevel)
    {
        string aStratName = aStrat == 1 ? "نابودی ناوگان اصلی دشمن" : "عملیات آبی‌خاکی و تهاجم ساحلی";
        string aTacName = (aStrat, aTac) switch
        {
            (1, 1) => "حمله‌ی غافلگیرانه به پایگاه‌های دریایی",
            (1, 2) => "کشاندن ناوگان دشمن به نبرد تعیین‌کننده",
            (2, 1) => "بمباران دریایی مواضع ساحلی",
            _ => "پیاده‌سازی موجی نیروها"
        };
        string dStratName = dStrat == 1 ? "استحکامات و موانع ساحلی" : "دفاع متحرک دریایی";
        string dTacName = (dStrat, dTac) switch
        {
            (1, 1) => "میدان مین و توپخانه‌ی ساحلی",
            (1, 2) => "بمباران متقابل ساحلی",
            (2, 1) => "ضدحمله‌ی سریع با قایق‌های تندرو",
            _ => "کمین زیردریایی"
        };

        string outcome = won ? (success >= 90 ? $"🏆 پیروزی دریایی قاطع {Esc(atk.Name)} — بندر دشمن در آستانه‌ی سقوط" : $"⚓ پیروزی دریایی {Esc(atk.Name)}")
                       : failed ? $"🛡 دفاع دریایی کامل {Esc(def.Name)}"
                       : $"⚖️ نبرد دریایی بی‌نتیجه — موفقیت {success}٪";

        string advText = stratAdv > 1.12f ? $"طرح مهاجم پادزهر انتخاب مدافع بود ({stratAdv:F2}×)"
                       : stratAdv < 0.92f ? $"طرح مدافع نقطه‌ضعف حمله را گرفت ({stratAdv:F2}× به ضرر مهاجم)"
                       : $"دو طرح تقریباً هم‌وزن بودند ({stratAdv:F2}×)";

        string aModels = NavalModelLines(A);
        string dModels = NavalModelLines(D);
        string tl = Timeline(log, 0);
        string tlD = Timeline(log, 1);

        int dur = (int)(15 + eff * 20);
        float frac = success / 100f;

        var sb = new StringBuilder(2500);
        sb.Append($"⚓ <b>گزارش نبرد دریایی — {Esc(atk.Name)} علیه {Esc(def.Name)}</b>\n");
        sb.Append($"{outcome}\n");
        sb.Append($"{Bar(frac, won ? 1 : failed ? 2 : 0)} <b>{success}٪</b> | ⏱ {dur} دقیقه\n");

        sb.Append("\n<b>🎯 طرح عملیات</b>\n");
        sb.Append($"• طرح شما: {Esc(aStratName)} / {Esc(aTacName)}\n");
        sb.Append($"• طرح دشمن: {Esc(dStratName)} / {Esc(dTacName)}\n");
        sb.Append($"• {Esc(advText)} | نسبت قدرت ضربه: {ratio:F2}\n");
        sb.Append($"• ترکیب شما: {Num(A.BS)}🚢 نبردناو، {Num(A.Subs)}⚓ زیردریایی، {Num(A.Boats)}🚤 اسکورت\n");
        sb.Append($"• ترکیب دشمن: {Num(D.BS)}🚢، {Num(D.Subs)}⚓، {Num(D.Boats)}🚤 (بندر سطح {portLevel})\n");
        if (A.Boats > 0)
            sb.Append("• یادآوری: قایق‌های تندرو فقط اسکورت‌اند؛ سهم آن‌ها در ضربه‌ی اصلی ناچیز است.\n");

        if (tl != null) { sb.Append("\n<b>📜 روند نبرد</b>\n").Append(tl).Append('\n'); }

        sb.Append("\n<b>💀 تلفات شما</b>\n");
        if (aModels != null) sb.Append(aModels).Append('\n');
        if (attBSDamage > 0) sb.Append($"   🔧 آسیب مجموع نبردناوها: {attBSDamage}٪ (نیاز به حوضچه‌ی خشک)\n");

        sb.Append("\n<b>💀 تلفات دشمن</b>\n");
        if (dModels != null) sb.Append(dModels).Append('\n');
        if (defBSDamage > 0) sb.Append($"   🔧 آسیب مجموع نبردناوهای دشمن: {defBSDamage}٪\n");

        sb.Append($"\n💰 غنیمت دریایی: {K(lootMoney)} پول، {K(lootIron)} آهن\n");
        if (success >= 90) sb.Append($"⚠️ بندر {Esc(def.Name)} یک سطح سقوط می‌کند!\n");
        sb.Append(oneSided && won
            ? "🧠 نبرد یک‌طرفه بود؛ نبردناوهای دشمن نه‌فقط آسیب دیدند، بلکه به قعر رفتند."
            : "🧠 نبردناوها معمولاً غرق نمی‌شوند؛ با «تعمیر ناو» آن‌ها را به ۱۰۰٪ برگردانید.");
        r.AttackerReport = sb.ToString();

        sb.Clear();
        sb.Append($"🛡 <b>گزارش دفاع دریایی — {Esc(def.Name)} در برابر {Esc(atk.Name)}</b>\n");
        sb.Append($"{outcome}\n");
        sb.Append($"{Bar(frac, failed ? 1 : won ? 2 : 0)} <b>{success}٪</b> | ⏱ {dur} دقیقه\n");
        sb.Append("\n<b>🎯 طرح‌ها</b>\n");
        sb.Append($"• دفاع شما: {Esc(dStratName)} / {Esc(dTacName)} (بندر سطح {portLevel})\n");
        sb.Append($"• حمله‌ی دشمن: {Esc(aStratName)} / {Esc(aTacName)}\n");
        if (tlD != null) { sb.Append("\n<b>📜 روند نبرد</b>\n").Append(tlD).Append('\n'); }
        sb.Append("\n<b>💀 تلفات شما</b>\n");
        if (dModels != null) sb.Append(dModels).Append('\n');
        if (defBSDamage > 0) sb.Append($"   🔧 آسیب نبردناوها: {defBSDamage}٪\n");
        sb.Append("\n<b>💀 تلفات دشمن</b>\n");
        if (aModels != null) sb.Append(aModels).Append('\n');
        sb.Append($"\n💸 خسارت: {K(lootMoney)} پول، {K(lootIron)} آهن\n");
        if (success >= 90) sb.Append("🆘 بندر شما به دلیل شکست سنگین یک سطح سقوط کرد!\n");
        sb.Append("🚤 قایق‌های بازمانده‌ی شما در گشت ساحلی باقی ماندند.");
        r.DefenderReport = sb.ToString();

        sb.Clear();
        sb.Append("📰 <b>خبر جنگ دریایی</b>\n");
        sb.Append("━━━━━━━━━━━━━━━\n");
        sb.Append($"⚓ <b>{Esc(atk.Name)}</b> ناوگانش را به سواحل <b>{Esc(def.Name)}</b> فرستاد\n");
        sb.Append($"{outcome}\n");
        sb.Append($"\n{Bar(frac, won ? 1 : failed ? 2 : 0)} <b>{success}٪</b> | ⏱ {dur} دقیقه\n");
        sb.Append($"🎯 {Esc(aStratName)} / {Esc(aTacName)}\n");
        sb.Append($"🛡 {Esc(dStratName)} / {Esc(dTacName)}\n");
        sb.Append($"\n💀 مهاجم: {Num(A.BoatsLost)}🚤 {Num(A.SubsLost)}⚓ {Num(A.BSLostTotal)}🚢");
        if (attBSDamage > 0) sb.Append($" (+{attBSDamage}٪ آسیب)");
        sb.Append('\n');
        sb.Append($"💀 مدافع: {Num(D.BoatsLost)}🚤 {Num(D.SubsLost)}⚓ {Num(D.BSLostTotal)}🚢");
        if (defBSDamage > 0) sb.Append($" (+{defBSDamage}٪ آسیب)");
        sb.Append('\n');
        if (lootMoney > 0 || lootIron > 0)
            sb.Append($"💰 غنیمت: {K(lootMoney)} پول، {K(lootIron)} آهن\n");
        if (success >= 90) sb.Append($"⚓ بندر {Esc(def.Name)} یک سطح کاهش یافت!\n");
        sb.Append("━━━━━━━━━━━━━━━");
        r.GroupAnnouncement = sb.ToString();
    }

    // ======================================================================
    //  بخش ۶ — ثبت نبردها در دیتابیس
    // ======================================================================

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
            cmd.Parameters.AddWithValue("@rep", r.AttackerReport ?? "");
            cmd.ExecuteNonQuery();
        }
        catch { }
    }
}
