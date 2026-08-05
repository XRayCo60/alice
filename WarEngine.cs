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

static partial class WarEngine
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
}
