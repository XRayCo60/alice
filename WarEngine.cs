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
    public long AttackerCrewLost;    // ملوان/خدمه‌ی از دست رفته
    public long DefenderCrewLost;
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

    // ═══════════════════ مشخصات فنی واحدها (داده‌ی تاریخی واقعی) ═══════════════
    //  همه‌ی اعداد از منابع تاریخی‌اند و مستقیم در فرمول‌های فیزیکی استفاده می‌شوند.
    //  هیچ عدد «قدرت» سرجمعی وجود ندارد — هر عدد معنای فیزیکی دارد.

    public readonly struct TankSpec
    {
        public readonly string Name;
        public readonly Faction Origin;
        public readonly float Caliber;      // میلی‌متر
        public readonly float MuzzleVel;    // متر بر ثانیه — پایه‌ی بالستیک
        public readonly float ShellMass;    // کیلوگرم
        public readonly float PenAt500;     // نفوذ در ۵۰۰ متر (میلی‌متر) — مرجع
        public readonly float HeFiller;     // کیلوگرم مواد منفجره‌ی گلوله‌ی انفجاری
        public readonly float RoF;          // گلوله در دقیقه
        public readonly float ArmorFront;   // زره جلو (میلی‌متر)
        public readonly float ArmorSide;    // زره پهلو (میلی‌متر)
        public readonly float Slope;        // ضریب مؤثر شیب زره
        public readonly float Speed;        // کیلومتر بر ساعت (جاده)
        public readonly float SpeedOff;     // کیلومتر بر ساعت (آفرود)
        public readonly int   MgCount;      // تعداد مسلسل
        public readonly int   CannonRounds; // ظرفیت مهمات توپ
        public readonly int   MgRounds;     // ظرفیت مهمات مسلسل
        public readonly float Reliab;       // قابلیت اطمینان مکانیکی ۰..۱
        public readonly float Optics;       // کیفیت اپتیک ۰..۱
        public readonly int   Crew;
        public readonly float TurretSec;    // ثانیه برای چرخش کامل برجک
        public TankSpec(string n, Faction o, float cal, float mv, float sm, float pen, float he, float rof,
            float af, float asd, float slope, float sp, float spo, int mg, int cr, int mr, float rel, float opt, int crew, float tsec)
        { Name=n; Origin=o; Caliber=cal; MuzzleVel=mv; ShellMass=sm; PenAt500=pen; HeFiller=he; RoF=rof;
          ArmorFront=af; ArmorSide=asd; Slope=slope; Speed=sp; SpeedOff=spo; MgCount=mg;
          CannonRounds=cr; MgRounds=mr; Reliab=rel; Optics=opt; Crew=crew; TurretSec=tsec; }
    }

    // M2 Medium — توپ 37mm M6، ۲۰۰ گلوله، ۷ مسلسل با ۱۲۲۵۰ فشنگ، زره ۳۲ پرچی، ۴۲ km/h
    //   نفوذ واقعی: ۴۶mm در ۴۵۷ متر روی زره سخت‌شده با شیب ۳۰°
    static readonly TankSpec SpecUSA = new("M2 Medium", Faction.USA,
        37f, 884f, 0.87f, 46f, 0.039f, 20f, 32f, 25f, 1.08f, 42f, 26f, 7, 200, 12250, 0.95f, 0.80f, 6, 20f);

    // T-28 — توپ 76.2mm L-10 با گلوله‌ی ضدزره BR-350A (APHEBC)
    //   سرعت پوزه ۵۵۵ m/s، نفوذ واقعی ۶۰mm در ۵۰۰ متر (منبع: جدول زره BR-350A)
    //   گلوله‌ی انفجاری OF-350M با ۶۲۱ گرم TNT — قوی‌ترین ضدپیاده‌ی این سه
    //   ولی آهنگ آتش پایین (۵/دقیقه)، فقط ۶۹ گلوله و اپتیک ضعیف
    static readonly TankSpec SpecUSSR = new("T-28", Faction.USSR,
        76.2f, 555f, 6.30f, 60f, 0.621f, 5f, 30f, 20f, 1.00f, 37f, 18f, 4, 69, 7938, 0.82f, 0.55f, 6, 26f);

    // Panzer III — توپ 5cm KwK 38 L/42، ۹۹ گلوله، اپتیک TZF5d عالی، زره ۶۰ (۳۰+۳۰)
    //   نفوذ واقعی: ۴۷mm در ۵۰۰ متر با Pzgr.39 (نه ۶۷ که قبلاً بود)
    static readonly TankSpec SpecReich = new("Panzer III", Faction.Reich,
        50f, 685f, 2.06f, 47f, 0.175f, 13f, 60f, 30f, 1.02f, 40f, 15f, 3, 99, 4500, 0.97f, 0.95f, 5, 33f);

    static TankSpec SpecOf(Faction f) => f == Faction.USA ? SpecUSA : f == Faction.USSR ? SpecUSSR : SpecReich;

    public readonly struct FighterSpec
    {
        public readonly string Name;
        public readonly Faction Origin;
        public readonly float SpeedKmh;     // بیشینه سرعت
        public readonly float ClimbMs;      // متر بر ثانیه صعود
        public readonly float CeilingM;     // سقف پرواز (متر)
        public readonly float TurnSec;      // ثانیه برای یک دور کامل — کوچک‌تر = چابک‌تر
        public readonly float RangeKm;      // برد رزمی
        public readonly float GunKgMin;     // وزن آتش (کیلوگرم بر دقیقه) — قدرت آتش واقعی
        public readonly float AmmoSec;      // ثانیه آتش پیوسته تا اتمام مهمات
        public readonly float Durability;   // تحمل ساختاری
        public readonly float BombKg;       // ظرفیت بمب برای پشتیبانی نزدیک
        public FighterSpec(string n, Faction o, float sp, float climb, float ceil, float turn,
            float rng, float gun, float ammo, float dur, float bomb)
        { Name=n; Origin=o; SpeedKmh=sp; ClimbMs=climb; CeilingM=ceil; TurnSec=turn;
          RangeKm=rng; GunKgMin=gun; AmmoSec=ammo; Durability=dur; BombKg=bomb; }
    }
    // P-36 Hawk — چابک ولی کم‌سرعت و کم‌سلاح (۲ مسلسل)
    static readonly FighterSpec FighterUSA = new("P-36", Faction.USA,
        500f, 10.5f, 10000f, 18.5f, 1300f, 12f, 25f, 1.05f, 0f);
    // I-16 — بسیار چابک، سقف پایین، برد کوتاه، ۲×ShVAK
    static readonly FighterSpec FighterUSSR = new("I-16", Faction.USSR,
        525f, 14.7f, 9700f, 17.0f, 700f, 28f, 12f, 0.85f, 200f);
    // Bf 109E — سریع‌ترین، صعود عالی، ۲×MG17 + ۲×MG FF 20mm
    static readonly FighterSpec FighterReich = new("Bf 109", Faction.Reich,
        570f, 15.5f, 11000f, 20.5f, 660f, 44f, 11f, 1.00f, 250f);
    static FighterSpec FighterOf(Faction f) => f == Faction.USA ? FighterUSA : f == Faction.USSR ? FighterUSSR : FighterReich;

    public readonly struct BomberSpec
    {
        public readonly string Name;
        public readonly Faction Origin;
        public readonly float SpeedKmh;
        public readonly float CeilingM;
        public readonly float RangeKm;
        public readonly float BombKg;       // بار بمب واقعی
        public readonly float DefGunKgMin;  // وزن آتش دفاعی
        public readonly int   DefPositions; // تعداد جایگاه تیرانداز — پوشش کروی
        public readonly float Durability;   // تحمل ساختاری (B-17 افسانه‌ای)
        public readonly float AccuracyCep;  // خطای دایره‌ای بمباران (متر) — کمتر = دقیق‌تر
        public readonly int   Crew;
        public BomberSpec(string n, Faction o, float sp, float ceil, float rng, float bomb,
            float defg, int defp, float dur, float cep, int crew)
        { Name=n; Origin=o; SpeedKmh=sp; CeilingM=ceil; RangeKm=rng; BombKg=bomb;
          DefGunKgMin=defg; DefPositions=defp; Durability=dur; AccuracyCep=cep; Crew=crew; }
    }
    // B-17 — سقف بالا، ۱۳ مسلسل .50، تحمل افسانه‌ای، نشانه‌روی Norden
    static readonly BomberSpec BomberUSA = new("B-17", Faction.USA,
        462f, 10850f, 3220f, 2724f, 78f, 8, 2.20f, 350f, 10);
    // He 111 — متوسط، دفاع ضعیف، دقیق‌تر در ارتفاع کم
    static readonly BomberSpec BomberReich = new("He 111", Faction.Reich,
        440f, 6500f, 2300f, 2000f, 22f, 5, 1.00f, 300f, 5);
    // DB-3 — بار کم، دفاع بسیار ضعیف، آسیب‌پذیر
    static readonly BomberSpec BomberUSSR = new("DB-3", Faction.USSR,
        439f, 8400f, 3800f, 1000f, 14f, 3, 0.75f, 420f, 4);
    static BomberSpec BomberOf(Faction f) => f == Faction.USA ? BomberUSA : f == Faction.USSR ? BomberUSSR : BomberReich;

    // ───────────────────── مشخصات دریایی (بدون هیچ عدد «قدرت») ─────────────────
    public readonly struct BoatSpec
    {
        public readonly string Name;
        public readonly Faction Origin;
        public readonly float SpeedKn;      // گره
        public readonly float LengthM;      // طول — اثر در سطح مقطع هدف
        public readonly float BeamM;        // عرض
        public readonly float DisplacementT;
        public readonly int   TorpTubes;
        public readonly float TorpWarheadKg;
        public readonly float TorpRangeKm;
        public readonly float GunMm;        // بزرگ‌ترین توپ
        public readonly float GunKgMin;     // وزن آتش سبک
        public readonly float HullArmorMm;  // عملاً صفر
        public readonly float SeaKeeping;   // تحمل دریای متلاطم ۰..۱
        public readonly int   Crew;
        public BoatSpec(string n, Faction o, float sp, float len, float beam, float disp,
            int tt, float twh, float trng, float gun, float gkg, float armor, float sea, int crew)
        { Name=n; Origin=o; SpeedKn=sp; LengthM=len; BeamM=beam; DisplacementT=disp;
          TorpTubes=tt; TorpWarheadKg=twh; TorpRangeKm=trng; GunMm=gun; GunKgMin=gkg;
          HullArmorMm=armor; SeaKeeping=sea; Crew=crew; }
    }
    // S-Boot — بدنه‌ی چوبی/فولادی، دریانوردی عالی، اژدر G7a
    static readonly BoatSpec BoatGermany = new("S-Boot", Faction.Reich,
        39.5f, 34.9f, 5.28f, 100f, 2, 280f, 7.5f, 20f, 9f, 10f, 0.85f, 24);
    // PT Boat — بدنه‌ی تخته‌ای، سریع، دریانوردی ضعیف
    static readonly BoatSpec BoatUSA = new("PT Boat", Faction.USA,
        41f, 24.4f, 6.3f, 56f, 4, 272f, 4.1f, 20f, 14f, 0f, 0.55f, 14);
    // G-5 — بسیار سریع ولی فقط برای آب آرام، دریانوردی افتضاح
    static readonly BoatSpec BoatUSSR = new("G-5", Faction.USSR,
        51f, 19.1f, 3.4f, 17f, 2, 200f, 3f, 12.7f, 4f, 7f, 0.30f, 6);
    static BoatSpec BoatOf(Faction f) => f == Faction.USA ? BoatUSA : f == Faction.USSR ? BoatUSSR : BoatGermany;

    public readonly struct SubSpec
    {
        public readonly string Name;
        public readonly Faction Origin;
        public readonly float SurfKn, SubKn;
        public readonly float TestDepthM;   // عمق ایمن غواصی
        public readonly int   TorpTubes;
        public readonly int   TorpLoad;     // تعداد اژدر حمل‌شده
        public readonly float TorpWarheadKg;
        public readonly float TorpRangeKm;
        public readonly float DeckGunMm;
        public readonly float SubEnduranceH;// ساعت زیر آب با باتری
        public readonly float DiveSec;      // ثانیه تا غواصی کامل — بقا در حمله‌ی ناگهانی
        public readonly float NoiseLevel;   // ۰..۱ — کمتر = ساکت‌تر
        public readonly int   Crew;
        public SubSpec(string n, Faction o, float sk, float bk, float depth, int tt, int tl,
            float twh, float trng, float gun, float endur, float dive, float noise, int crew)
        { Name=n; Origin=o; SurfKn=sk; SubKn=bk; TestDepthM=depth; TorpTubes=tt; TorpLoad=tl;
          TorpWarheadKg=twh; TorpRangeKm=trng; DeckGunMm=gun; SubEnduranceH=endur;
          DiveSec=dive; NoiseLevel=noise; Crew=crew; }
    }
    // Type VIIC — غواصی سریع (۲۵ ثانیه)، ساکت، ۱۴ اژدر
    static readonly SubSpec SubGermany = new("Type VIIC", Faction.Reich,
        17.7f, 7.6f, 230f, 5, 14, 280f, 7.5f, 88f, 20f, 25f, 0.30f, 48);
    // Gato — بزرگ‌تر، ۲۴ اژدر، اژدر Mk14 قوی، غواصی کندتر
    static readonly SubSpec SubUSA = new("Gato", Faction.USA,
        21f, 8.75f, 90f, 10, 24, 292f, 8.2f, 76f, 48f, 35f, 0.40f, 60);
    // S-class — کوچک، اژدر کم، غواصی کند، پرصدا
    static readonly SubSpec SubUSSR = new("S-class", Faction.USSR,
        19.5f, 8.7f, 100f, 6, 12, 300f, 4f, 100f, 15f, 48f, 0.55f, 46);
    static SubSpec SubOf(Faction f) => f == Faction.USA ? SubUSA : f == Faction.USSR ? SubUSSR : SubGermany;

    public readonly struct BattleshipSpec
    {
        public readonly string Name;
        public readonly Faction Origin;
        public readonly float SpeedKn;
        public readonly float LengthM, BeamM;
        public readonly float DisplacementT;
        public readonly float BeltMm, DeckMm, TurretMm, ConningMm;
        public readonly float MainMm;        // قطر توپ اصلی
        public readonly int   MainGuns;      // تعداد لوله
        public readonly float MainShellKg;   // وزن گلوله
        public readonly float MainMuzzleMs;  // سرعت پوزه
        public readonly float MainRangeKm;   // برد بیشینه
        public readonly float MainRpm;       // گلوله بر دقیقه هر لوله
        public readonly float SecMm;
        public readonly int   SecGuns;
        public readonly float AaKgMin;       // وزن آتش ضدهوایی
        public readonly float FireControl;   // کنترل آتش ۰..۱ — در ۱۹۳۹ عمدتاً اپتیکی (فاصله‌یاب استریوسکوپی)
        public readonly int   Crew;
        public BattleshipSpec(string n, Faction o, float sp, float len, float beam, float disp,
            float belt, float deck, float turret, float conning, float mainMm, int mainN,
            float shellKg, float muzzle, float rangeKm, float rpm, float secMm, int secN,
            float aa, float fc, int crew)
        { Name=n; Origin=o; SpeedKn=sp; LengthM=len; BeamM=beam; DisplacementT=disp;
          BeltMm=belt; DeckMm=deck; TurretMm=turret; ConningMm=conning; MainMm=mainMm; MainGuns=mainN;
          MainShellKg=shellKg; MainMuzzleMs=muzzle; MainRangeKm=rangeKm; MainRpm=rpm;
          SecMm=secMm; SecGuns=secN; AaKgMin=aa; FireControl=fc; Crew=crew; }
    }
    // Bismarck — ۸×۳۸cm، کمربند ۳۲۰، عرشه ضعیف ۱۲۰، کنترل آتش عالی ولی رادار ابتدایی
    static readonly BattleshipSpec BSGermany = new("Bismarck", Faction.Reich,
        30.1f, 251f, 36f, 50300f, 320f, 120f, 360f, 350f,
        380f, 8, 800f, 820f, 36.5f, 2.3f, 150f, 12, 190f, 0.88f, 2092);   // فاصله‌یاب Zeiss — بهترین اپتیک ۱۹۳۹
    // Iowa — ۹×۴۰۶cm، سریع‌ترین، عرشه ۱۵۲، رادار Mk8 → بهترین کنترل آتش جنگ
    static readonly BattleshipSpec BSUSA = new("Iowa", Faction.USA,
        32.5f, 270f, 33f, 57540f, 307f, 152f, 495f, 440f,
        406f, 9, 1225f, 762f, 38.7f, 2.0f, 127f, 20, 900f, 0.80f, 2700);   // ۱۹۳۹: هنوز رادار Mk8 نیامده
    // Sovetsky Soyuz — هرگز کامل نشد؛ زره متوسط، ضدهوایی ضعیف، بدون رادار
    static readonly BattleshipSpec BSUSSR = new("Sovetsky Soyuz", Faction.USSR,
        28f, 269f, 38.9f, 59150f, 375f, 155f, 495f, 425f,
        406f, 9, 1108f, 830f, 45.6f, 2.0f, 152f, 12, 120f, 0.58f, 1664);   // اپتیک ضعیف‌تر، آموزش کمتر
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
        public float CAmmo0, MAmmo0;   // بار اولیه‌ی مهمات برای محاسبه‌ی نسبت
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
        public byte Role;            // ۳۶: 0 = خط اول، 1 = ذخیره‌ی تاکتیکی، 2 = ذخیره‌ی عملیاتی
        public bool Deployed;        // ۳۶: از ذخیره رسیده و وارد خط شده
        public bool Alive;
        public bool Sprung;
        //  لِین حرکت: جای ثابت این یگان نسبت به محور، در بازه‌ی ‎-1..+1‎.
        //   یک بار در آغاز نبرد قرعه می‌خورد و تا پایان عوض نمی‌شود، پس
        //   یگان یک مسیر پیوسته می‌رود و هر چرخه‌ی فرمان دوباره قرعه نمی‌خورد.
        public float Lane;
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
        public bool ReserveIn;      // ذخیره‌ی تاکتیکی وارد شده؟
        public bool DeepReserveIn;  // ۳۶: ذخیره‌ی عملیاتی هم آزاد شده؟
        public int  TacReserveTick;  // تیک آزادسازی ذخیره‌ی تاکتیکی
        public int  OpReserveTick;   // تیک آزادسازی ذخیره‌ی عملیاتی
        public bool RingClosed;
        public float PeakDepth;     // عمیق‌ترین پیشرویِ تثبیت‌شده تا کنون
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
        public byte MapType;            // ۳۱: تیپ نقشه
        public int  WeatherShiftTick;   // ۳۲: تیک تغییر آب‌وهوا
        public byte WeatherNext;        // ۳۲: آب‌وهوای بعدی
        public float CloudBaseM;        // ۵۸: کف لایه‌ی ابر

        // ── چرخه‌ی شبانه‌روز: دقیقاً یک بار در طول نبرد ──
        //  نبرد حداکثر ۲۴۰ تیک (۲۴ ساعت) طول می‌کشد و شبانه‌روز هم ۲۴۰ تیک است،
        //  پس هیچ نبردی دو بار شب نمی‌بیند. طول فازها هم واقعی است، نه مساوی:
        //     سپیده‌دم ۲ ساعت | روز ۱۰ ساعت | غروب ۲ ساعت | شب ۱۰ ساعت
        const int PH_DAWN = 20, PH_DAY = 100, PH_DUSK = 20;   // بقیه شب است
        const int DAY_TICKS = 240;

        //  ساعت شروع نبرد از StartTime می‌آید: ابتدای همان فاز
        static int StartTickOf(byte t) => t switch
        {
            TM_DAWN => 0,
            TM_DAY  => PH_DAWN,
            TM_DUSK => PH_DAWN + PH_DAY,
            _       => PH_DAWN + PH_DAY + PH_DUSK,
        };

        public byte TimeAt(int tick)
        {
            int t = (StartTickOf(StartTime) + tick) % DAY_TICKS;
            if (t < PH_DAWN) return TM_DAWN;
            if (t < PH_DAWN + PH_DAY) return TM_DAY;
            if (t < PH_DAWN + PH_DAY + PH_DUSK) return TM_DUSK;
            return TM_NIGHT;
        }

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
        public float BombTonsOnTarget;   // تناژ واقعی بمب روی هدف
        public long AircrewLost;         // خدمه‌ی پرواز از دست رفته
        public float EscortAltM, CapAltM; // ارتفاع نبرد — برای گزارش
        public string? Narrative;
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

    // ── ۳۱: تیپ‌های نقشه ──
    //  هر تیپ ساختار زمینی کاملاً متفاوتی می‌سازد، پس دکترین‌ها در نقشه‌های
    //  مختلف نتیجه‌ی متفاوت می‌دهند: محاصره در دشت عالی است و در گذرگاه فاجعه.
    const byte MAP_PLAINS = 0, MAP_HILLS = 1, MAP_FOREST = 2, MAP_INDUSTRIAL = 3,
               MAP_MARSH = 4, MAP_PASS = 5, MAP_RIVER = 6, MAP_STEPPE = 7;
    static readonly string[] MapName =
    { "دشت باز", "تپه‌ماهور", "جنگل انبوه", "منطقه‌ی صنعتی", "مرداب", "گذرگاه کوهستانی", "خط رودخانه", "استپ وسیع" };
    static readonly string[] MapNote =
    {
        "زمین باز و بی‌پناه — بهشت زره و جهنم پیاده‌ی بی‌سنگر",
        "برجستگی‌های پیاپی؛ هر تپه یک موضع دید و آتش است",
        "دید کوتاه، کمین آسان، هماهنگی سخت",
        "خرابه و کارخانه؛ تانک کور می‌شود و پیاده حاکم است",
        "زمین نرم؛ زره در گِل می‌ماند و مسیرها محدودند",
        "فقط چند دهانه‌ی عبور — مدافع با نیروی کم هم می‌تواند ببندد",
        "خط آبی که فقط از چند گدار عبور دارد",
        "پهنه‌ی بی‌انتها؛ جناح‌ها باز و مانور آزاد است"
    };

    static Field GenField(ref XorRng rng)
    {
        var f = new Field();
        uint s1 = (uint)rng.NextU(), s2 = (uint)rng.NextU(), s3 = (uint)rng.NextU();

        // انتخاب تیپ نقشه
        float mr = rng.NextF();
        f.MapType = mr < 0.20f ? MAP_PLAINS : mr < 0.36f ? MAP_HILLS : mr < 0.50f ? MAP_FOREST
                  : mr < 0.62f ? MAP_INDUSTRIAL : mr < 0.71f ? MAP_MARSH : mr < 0.81f ? MAP_PASS
                  : mr < 0.91f ? MAP_RIVER : MAP_STEPPE;

        for (int gy = 0; gy < GRID_H; gy++)
            for (int gx = 0; gx < GRID_W; gx++)
            {
                float e = Noise(gx * 0.09f, gy * 0.09f, s1) * 0.65f + Noise(gx * 0.23f, gy * 0.23f, s2) * 0.35f;
                float v = Noise(gx * 0.13f + 50, gy * 0.13f, s3);
                int idx = gy * GRID_W + gx;
                byte t;

                switch (f.MapType)
                {
                    case MAP_PLAINS:
                        e *= 0.55f;
                        t = e > 0.52f ? T_HILL : v > 0.86f ? T_FOREST : T_PLAIN;
                        break;

                    case MAP_HILLS:
                        e = 0.30f + e * 0.70f;
                        t = e > 0.74f ? T_RIDGE : e > 0.55f ? T_HILL : v > 0.78f ? T_FOREST : T_PLAIN;
                        break;

                    case MAP_FOREST:
                        t = v > 0.30f ? T_FOREST : e > 0.70f ? T_HILL : v < 0.10f ? T_MARSH : T_PLAIN;
                        break;

                    case MAP_INDUSTRIAL:
                        // خوشه‌های شهری در میانه‌ی نقشه
                        t = (v > 0.42f && v < 0.72f) ? T_URBAN : e > 0.72f ? T_HILL : v > 0.88f ? T_FOREST : T_PLAIN;
                        break;

                    case MAP_MARSH:
                        t = v < 0.46f ? T_MARSH : v > 0.84f ? T_FOREST : T_PLAIN;
                        break;

                    case MAP_PASS:
                    {
                        // دیواره‌ی کوه در دو طرف، فقط چند دهانه در میانه
                        float cx = gx / (float)GRID_W;
                        float wall = MathF.Abs(cx - 0.5f) * 2f;                 // ۰ در مرکز، ۱ در لبه
                        float gate = Noise(gy * 0.35f, 11f, s3);                // دهانه‌های پراکنده
                        if (wall > 0.42f && gate < 0.72f) { t = T_RIDGE; e = 0.85f; }
                        else if (wall > 0.30f) { t = T_HILL; e = 0.66f; }
                        else t = v > 0.75f ? T_FOREST : T_PLAIN;
                        break;
                    }

                    case MAP_RIVER:
                    {
                        // نوار مرداب افقی به‌جای رودخانه، با چند گدار
                        float band = MathF.Abs(gy / (float)GRID_H - 0.45f);
                        float ford = Noise(gx * 0.30f, 5f, s2);
                        if (band < 0.055f && ford < 0.70f) { t = T_MARSH; e = 0.18f; }
                        else t = e > 0.70f ? T_HILL : v > 0.80f ? T_FOREST : T_PLAIN;
                        break;
                    }

                    default: // MAP_STEPPE
                        e *= 0.42f;
                        t = v > 0.93f ? T_FOREST : T_PLAIN;
                        break;
                }

                f.Elev[idx] = e;
                f.Terr[idx] = t;
            }

        // ── آب‌وهوای وابسته به نقشه ──
        float r = rng.NextF();
        if (f.MapType == MAP_MARSH) r *= 0.80f;              // مرداب مه‌آلودتر
        f.Weather = r < 0.45f ? W_CLEAR : r < 0.68f ? W_CLOUD : r < 0.84f ? W_RAIN : r < 0.94f ? W_FOG : W_SNOW;
        f.StartTime = (byte)rng.Next(4);

        // ── ۳۲: آب‌وهوا در طول نبرد عوض می‌شود ──
        //  یک زمان تغییر و یک وضعیت بعدی از قبل قرعه می‌خورد.
        f.WeatherShiftTick = 40 + rng.Next(120);
        float r2 = rng.NextF();
        byte next = r2 < 0.40f ? W_CLEAR : r2 < 0.62f ? W_CLOUD : r2 < 0.82f ? W_RAIN : r2 < 0.93f ? W_FOG : W_SNOW;
        f.WeatherNext = next;

        // ── ۵۸: لایه‌ی ابر ──
        //  بالای لایه آفتابی است، زیرش کور. بمب‌افکن بالای ابر امن ولی نابیناست.
        f.CloudBaseM = f.Weather switch
        {
            W_CLEAR => 9999f,
            W_CLOUD => 2200f + rng.Range(0f, 1800f),
            W_RAIN  => 1200f + rng.Range(0f, 1200f),
            W_FOG   => 300f + rng.Range(0f, 500f),
            _       => 900f + rng.Range(0f, 1400f),
        };
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
                InitGroup(ref fo.G[n], attacker, 1, (byte)mi, u, fo.Specs[mi], strat, tac, field, ref rng);
                left -= (long)u; n++;
            }
        }
        long sLeft = fo.SoldiersSent;
        while (sLeft > 0 && n < MAX_GROUPS)
        {
            float u = Math.Min(sLeft, (long)Math.Ceiling(infGrp));
            InitGroup(ref fo.G[n], attacker, 0, 0, u, nm > 0 ? fo.Specs[0] : SpecUSA, strat, tac, field, ref rng);
            sLeft -= (long)u; n++;
        }
        fo.N = n;

        // ── ۳۶: ذخیره‌ی دولایه ──
        //  خط اول (۶۰٪) از تیک صفر می‌جنگد.
        //  ذخیره‌ی تاکتیکی (۲۵٪) نزدیک جبهه منتظر است و سریع می‌رسد.
        //  ذخیره‌ی عملیاتی (۱۵٪) در عمق است؛ دیر آزاد می‌شود و دیرتر می‌رسد،
        //  ولی وقتی برسد وزن سنگینی به یک نقطه اضافه می‌کند.
        for (int i = 0; i < n; i++)
        {
            int m = i % 20;
            fo.G[i].Role = (byte)(m < 12 ? 0 : m < 17 ? 1 : 2);
            fo.G[i].Deployed = fo.G[i].Role == 0;   // خط اول از تیک صفر در خط است
            // ذخیره‌ی عملیاتی از عقب‌تر شروع می‌کند
            if (fo.G[i].Role == 2 && attacker) fo.G[i].Y -= 4.5f;
        }

        fo.Cmd = InitCommander(attacker, strat, tac, ref rng);
        return fo;
    }

    static void InitGroup(ref Group gr, bool atk, byte type, byte model, float units,
        in TankSpec spec, int strat, int tac, Field field, ref XorRng rng)
    {
        gr = default;
        gr.Type = type; gr.Model = model; gr.Units = units; gr.Size0 = units; gr.Alive = true;
        gr.Morale = rng.Range(0.86f, 1f);
        // مهمات از ظرفیت واقعی همان مدل می‌آید، نه یک عدد قراردادی.
        //  T-28 فقط ۶۹ گلوله دارد، Panzer III نود و نه، M2 دویست.
        if (type == 1)
        {
            gr.CAmmo = units * spec.CannonRounds;
            gr.MAmmo = units * spec.MgRounds;
        }
        else
        {
            gr.CAmmo = 0f;
            gr.MAmmo = units * 120f;          // فشنگ هر سرباز
        }
        gr.CAmmo0 = Math.Max(1f, gr.CAmmo);
        gr.MAmmo0 = Math.Max(1f, gr.MAmmo);
        gr.Exp = rng.Range(0f, 0.1f);
        gr.FireTgt = -1;
        gr.Lane = rng.Range(-1f, 1f);   // جای ثابت این یگان در عرض محور

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
                ? foe.G[j].Units * (6f + foe.Specs[foe.G[j].Model].ArmorFront * 0.03f + foe.Specs[foe.G[j].Model].PenAt500 * 0.04f)
                : foe.G[j].Units * 0.8f;
            own.ThreatMap[s] += pw * lvl;
        }
    }

    //  ضعیف‌ترین سکتور: کل جبهه بررسی می‌شود، از سکتور صفر تا آخر.
    //   لبه‌ها حذف نمی‌شوند — حمله از لبه‌ی جبهه ممکن است — ولی چون یک طرفشان
    //   خارج از نقشه است، پشتیبانی جناحی کمتری دارند و فرمانده این را می‌داند.
    //   پس فقط یک جریمه‌ی وزنی می‌خورند، نه حذف کامل.
    static int WeakestSector(float[] threat, ref XorRng rng, float noise = 8f)
    {
        int best = 0; float bv = float.MaxValue;
        //  میانگین تهدید جبهه، مبنای جریمه‌ی لبه
        float avg = 0f;
        for (int s = 0; s < SECTORS; s++) avg += threat[s];
        avg /= SECTORS;

        for (int s = 0; s < SECTORS; s++)
        {
            //  همسایه‌ی بیرون نقشه = تهدید صفر، ولی جای مانور هم نیست
            float left  = s > 0 ? threat[s - 1] : threat[s];
            float right = s < SECTORS - 1 ? threat[s + 1] : threat[s];
            float v = threat[s] + left * 0.4f + right * 0.4f + rng.NextF() * noise;
            //  جریمه‌ی لبه: حمله از گوشه‌ی جبهه امکان توسعه‌ی جانبی ندارد
            if (s == 0 || s == SECTORS - 1) v += avg * 0.35f + noise * 0.5f;
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

        //  – ۳۶: ذخیره‌ی دولایه. بحران = پیشروی قفل شده و تلفات بالا رفته.
        bool crisis11 = stalled && tick > 60;
        ReleaseReserves(me, depth, tick, true, crisis11, log);

        float mainX = SectorX(c.MainSector);
        for (int i = 0; i < me.N; i++)
        {
            ref Group g = ref me.G[i];
            if (!g.Alive) continue;
            if (!TriageGroup(ref g, me, true, tick, log, ref rng)) continue;
            if (HoldingInReserve(ref g, c, tick, mainX)) continue;

            g.Posture = depth > 2f ? P_ASSAULT : P_ADVANCE;
            float spread = 3.5f + (1f - c.Aggression) * 4f;
            g.TgtX = Math.Clamp(mainX + g.Lane * spread, 1f, FRONT_KM - 1);
            g.TgtY = MathF.Min(WIN_DEPTH + 1f, g.Y + (g.Type == 1 ? 6.5f : 4.5f));
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
        //  – ۳۶: در این دکترین ذخیره تا شروع یورش اصلی دست‌نخورده می‌ماند
        ReleaseReserves(me, c.Phase == 1 ? Math.Max(depth, 6.5f) : depth, tick, true,
                        c.Phase == 1 && tick - c.PhaseStart > 50, log);

        for (int i = 0; i < me.N; i++)
        {
            ref Group g = ref me.G[i];
            if (!g.Alive) continue;
            if (!TriageGroup(ref g, me, true, tick, log, ref rng)) continue;
            if (HoldingInReserve(ref g, c, tick, mainX)) continue;

            bool prober = (i % 5) == 0;   // یک‌پنجم نیرو نقش اکتشاف دارد
            if (c.Phase == 0)
            {
                if (prober)
                {
                    g.Posture = P_PATROL;
                    g.TgtX = Math.Clamp(SectorX(i % SECTORS) + g.Lane * 2f, 1f, FRONT_KM - 1);
                    g.TgtY = Math.Min(6f, g.Y + 3f);
                }
                else
                {
                    g.Posture = P_HOLD;
                    g.TgtX = g.X;
                }
            }
            else
            {
                g.Posture = depth > 2f ? P_ASSAULT : P_ADVANCE;
                float spread = prober ? 9f : 4.5f;
                g.TgtX = Math.Clamp(mainX + g.Lane * spread, 1f, FRONT_KM - 1);
                g.TgtY = MathF.Min(WIN_DEPTH + 1f, g.Y + (g.Type == 1 ? 6f : 4.2f));
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
        //  – ۳۶: حلقه که بست، ذخیره برای خرد کردن جیب آزاد می‌شود
        ReleaseReserves(me, c.RingClosed ? Math.Max(depth, 13f) : depth, tick, true,
                        tick > 150 && !c.RingClosed, log);

        float leftX = SectorX(c.MainSector), rightX = SectorX(c.SecondSector);
        float centerX = SectorX(c.FeintSector < 0 ? SECTORS / 2 : c.FeintSector);

        for (int i = 0; i < me.N; i++)
        {
            ref Group g = ref me.G[i];
            if (!g.Alive) continue;
            if (!TriageGroup(ref g, me, true, tick, log, ref rng)) continue;

            if (HoldingInReserve(ref g, c, tick, centerX)) continue;

            bool pinning = (i % 4) == 0;         // یک‌چهارم نیرو مرکز را تثبیت می‌کند
            if (pinning && !c.DeepReserveIn)
            {
                g.Posture = P_SCREEN;
                g.TgtX = Math.Clamp(centerX + g.Lane * 5f, 1f, FRONT_KM - 1);
                g.TgtY = Math.Min(depth + 1.5f, g.Y + 1.5f);
                continue;
            }

            bool leftArm = (i & 1) == 0;
            float armX = leftArm ? leftX : rightX;
            if (c.RingClosed) armX = centerX + (leftArm ? -3f : 3f);
            g.Posture = c.RingClosed ? P_ASSAULT : P_FLANK;
            g.TgtX = Math.Clamp(armX + g.Lane * 3f, 1f, FRONT_KM - 1);
            g.TgtY = MathF.Min(WIN_DEPTH + 1f, g.Y + (g.Type == 1 ? 5.5f : 3.8f));
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
        //  – ۳۶: چرخش محور نیروی تازه می‌خواهد، پس ذخیره زودتر وارد می‌شود
        ReleaseReserves(me, depth, tick, true, c.ShiftCount >= 3 && depth < 8f, log);

        for (int i = 0; i < me.N; i++)
        {
            ref Group g = ref me.G[i];
            if (!g.Alive) continue;
            if (!TriageGroup(ref g, me, true, tick, log, ref rng)) continue;
            if (HoldingInReserve(ref g, c, tick, mainX)) continue;

            bool holdOld = (i % 3) == 0;   // یک‌سوم نیرو محور قبلی را رها نمی‌کند
            float tx = holdOld ? prevX : mainX;
            g.Posture = depth > 3f ? P_ASSAULT : P_FLANK;
            float wobble = MathF.Sin((tick + i * 7) * 0.05f) * 3.5f;
            g.TgtX = Math.Clamp(tx + wobble + g.Lane * 2.5f, 1f, FRONT_KM - 1);
            g.TgtY = MathF.Min(WIN_DEPTH + 1f, g.Y + (g.Type == 1 ? 6.2f : 4.2f));
        }
    }

    // ═══════════════ مغز فرمانده‌ی مدافع — چهار دستگاه فکری مجزا ═════════════
    static void CommandDefender(Force me, Force foe, Field field, float depth, int tick,
        BattleLog log, ref XorRng rng)
    {
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

        //  – ۳۶: خط ثابت ذخیره را برای بستن شکاف نگه می‌دارد
        ReleaseReserves(me, depth, tick, false, depth > 14f, log);

        for (int i = 0; i < me.N; i++)
        {
            ref Group g = ref me.G[i];
            if (!g.Alive) continue;
            if (!TriageGroup(ref g, me, false, tick, log, ref rng)) continue;
            if (HoldingInReserve(ref g, c, tick, hotX)) continue;

            if (g.Role != 0 && hv > 0)
            {
                // ذخیره‌ی رسیده به سمت نقطه‌ی داغ پاتک می‌زند
                g.TgtX = Math.Clamp(hotX + g.Lane * 3f, 1f, FRONT_KM - 1);
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

        //  – ۳۶: تا محور دشمن کشف نشود، ذخیره خرج نمی‌شود
        ReleaseReserves(me, c.Phase == 1 ? Math.Max(depth, 5f) : 0f, tick, false, depth > 13f, log);

        for (int i = 0; i < me.N; i++)
        {
            ref Group g = ref me.G[i];
            if (!g.Alive) continue;
            if (!TriageGroup(ref g, me, false, tick, log, ref rng)) continue;
            if (HoldingInReserve(ref g, c, tick, hotX)) continue;

            if (c.Phase == 0)
            {
                //  گشت: مسیر رفت‌وبرگشتی آرام حول جای اولیه، نه پرش تصادفی
                g.Posture = P_PATROL;
                float beat = MathF.Sin((tick + i * 11) * 0.04f);
                g.TgtX = Math.Clamp(g.X + beat * 6f, 1f, FRONT_KM - 1);
                g.TgtY = Math.Clamp(g.Y + g.Lane * 1.2f, 0.8f, 8f);
            }
            else
            {
                bool screen = i % 3 == 0;
                g.Posture = screen ? P_SCREEN : P_ADVANCE;
                g.TgtX = Math.Clamp(hotX + g.Lane * 5f, 1f, FRONT_KM - 1);
                g.TgtY = Math.Clamp(depth + 0.5f + g.Lane * 1f, 1f, 9f);
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

        //  – ۳۶: ذخیره تا فعال‌شدن کمین‌ها پنهان می‌ماند
        ReleaseReserves(me, c.Committed ? Math.Max(depth, 11f) : 0f, tick, false, depth > 15f, log);

        for (int i = 0; i < me.N; i++)
        {
            ref Group g = ref me.G[i];
            if (!g.Alive) continue;
            if (!TriageGroup(ref g, me, false, tick, log, ref rng)) continue;
            if (HoldingInReserve(ref g, c, tick, hotX)) continue;

            if (!g.Sprung && !c.Committed) { g.Posture = P_AMBUSH; continue; }
            if (c.Committed)
            {
                g.Posture = P_ASSAULT;
                g.TgtX = Math.Clamp(hotX + g.Lane * 4f, 1f, FRONT_KM - 1);
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

        //  – ۳۶: در دکترین تله، ذخیره دقیقاً همان فکِ بسته‌شدن دهانه است
        ReleaseReserves(me, c.Phase == 2 ? Math.Max(depth, 12f) : 0f, tick, false, c.Phase == 2, log);

        for (int i = 0; i < me.N; i++)
        {
            ref Group g = ref me.G[i];
            if (!g.Alive) continue;
            if (!TriageGroup(ref g, me, false, tick, log, ref rng)) continue;
            if (HoldingInReserve(ref g, c, tick, hotX)) continue;

            if (c.Phase == 1)
            {
                if (!g.Sprung) { g.Posture = P_AMBUSH; g.TgtY = Math.Min(14f, g.Y + 1.2f); }
                else g.Posture = P_DEFEND;
            }
            else
            {
                g.Posture = P_ASSAULT;
                bool leftJaw = (i & 1) == 0;
                g.TgtX = Math.Clamp(hotX + (leftJaw ? -4f : 4f) + g.Lane * 2f, 1f, FRONT_KM - 1);
                g.TgtY = Math.Max(1f, depth - 2f);
            }
        }
    }

    // ═══════════ ۳۶: مدیریت ذخیره‌ی دولایه ═══════════
    //  فرمانده دو ورق در آستین دارد و باید بداند کِی کدام را رو کند:
    //
    //   ذخیره‌ی تاکتیکی (۲۵٪ نیرو) — نزدیک جبهه، ۴ تیک تا رسیدن.
    //      برای بستن یک شکاف یا پهن‌کردن رخنه‌ی تازه. ارزان و سریع.
    //
    //   ذخیره‌ی عملیاتی (۱۵٪ نیرو) — در عمق، ۱۴ تیک تا رسیدن.
    //      ضربه‌ی نهایی. اگر زود خرجش کنی، وقتی واقعاً لازم شد چیزی نداری؛
    //      اگر دیر خرجش کنی، نبرد تمام شده است.
    //
    //  شخصیت فرمانده تعیین می‌کند: جسور زود می‌فرستد، صبور نگه می‌دارد.
    const int TAC_RESERVE_MARCH = 4;    // تیک تا رسیدن ذخیره‌ی تاکتیکی
    const int OP_RESERVE_MARCH = 14;    // تیک تا رسیدن ذخیره‌ی عملیاتی

    static void ReleaseReserves(Force me, float depth, int tick, bool attacker,
        bool crisis, BattleLog log)
    {
        ref CommanderState c = ref me.Cmd;

        //  یگان ذخیره‌ای که مدت راهش تمام شده، رسیده — حتی اگر همان تیک به دلیل
        //  روحیه یا مهمات در حالت بازسازی باشد و از HoldingInReserve رد نشود.
        //  بدون این، یک گروه می‌تواند تا آخر نبرد Deployed نشود و هرگز در
        //  محاسبه‌ی عمق مؤثر شمرده نشود، حتی وقتی سرِ جای خودش ایستاده.
        for (int i = 0; i < me.N; i++)
        {
            ref Group g = ref me.G[i];
            if (g.Deployed || g.Role == 0) continue;
            bool released = g.Role == 1 ? c.ReserveIn : c.DeepReserveIn;
            if (!released) continue;
            int rt = g.Role == 1 ? c.TacReserveTick : c.OpReserveTick;
            int march = g.Role == 1 ? TAC_RESERVE_MARCH : OP_RESERVE_MARCH;
            if (tick - rt >= march) g.Deployed = true;
        }

        // ── لایه‌ی یک: ذخیره‌ی تاکتیکی ──
        if (!c.ReserveIn)
        {
            //  جسارت بالا → زودتر. صبر بالا → دیرتر.
            float trigger = attacker ? 6f - c.Aggression * 3f : 4f;
            int lateLimit = (int)(40 + c.Patience * 40);
            if (depth > trigger || tick > lateLimit || crisis)
            {
                c.ReserveIn = true; c.TacReserveTick = tick;
                log.Add(tick, (byte)(attacker ? 0 : 1), LG_DECISION, attacker
                    ? $"ذخیره‌ی تاکتیکی آزاد شد تا رخنه را پهن کند؛ حدود {TAC_RESERVE_MARCH * (int)TICK_MIN} دقیقه تا رسیدنش."
                    : $"مدافع ذخیره‌ی تاکتیکی را برای بستن شکاف فرستاد؛ {TAC_RESERVE_MARCH * (int)TICK_MIN} دقیقه تا رسیدن.");
            }
        }

        // ── لایه‌ی دو: ذخیره‌ی عملیاتی ──
        //  فقط وقتی که یا رخنه‌ی واقعی هست (ارزش ضربه‌ی نهایی دارد)
        //  یا بحران است (وگرنه نبرد را می‌بازیم).
        if (c.ReserveIn && !c.DeepReserveIn)
        {
            bool worthIt = attacker ? depth > 12f : depth > 10f;
            bool tooLate = tick > MAX_TICKS - OP_RESERVE_MARCH - 20;
            //  فرمانده‌ی صبور منتظر لحظه‌ی درست می‌ماند
            bool patientReady = tick - c.TacReserveTick > (int)(10 + c.Patience * 25);
            if ((worthIt && patientReady) || crisis || tooLate)
            {
                c.DeepReserveIn = true; c.OpReserveTick = tick;
                log.Add(tick, (byte)(attacker ? 0 : 1), LG_DECISION,
                    crisis
                    ? "بحران در جبهه؛ فرمانده ذخیره‌ی عملیاتی را هم زودتر از موعد وارد کرد."
                    : tooLate
                    ? "فرصت داشت تمام می‌شد؛ ذخیره‌ی عملیاتی در آخرین لحظه آزاد شد."
                    : $"فرمانده ذخیره‌ی عملیاتی را برای ضربه‌ی نهایی آزاد کرد؛ حدود {OP_RESERVE_MARCH * (int)TICK_MIN} دقیقه در راه است.");
            }
        }
    }

    //  آیا این یگان هنوز در ذخیره است و نباید بجنگد؟
    //  خروجی true یعنی «هنوز نرسیده، دست نگه دار».
    static bool HoldingInReserve(ref Group g, in CommanderState c, int tick, float rallyX)
    {
        if (g.Role == 0) return false;

        bool released = g.Role == 1 ? c.ReserveIn : c.DeepReserveIn;
        int releaseTick = g.Role == 1 ? c.TacReserveTick : c.OpReserveTick;
        int march = g.Role == 1 ? TAC_RESERVE_MARCH : OP_RESERVE_MARCH;

        if (!released)
        {
            // منتظر دستور: در عمق جمع می‌شود، نمی‌جنگد
            g.Posture = P_HOLD;
            g.TgtX = rallyX;
            return true;
        }
        if (tick - releaseTick < march)
        {
            // در راه است: به سمت نقطه‌ی تجمع می‌رود ولی هنوز وارد خط نشده
            g.Posture = P_ADVANCE;
            g.TgtX = rallyX;
            g.TgtY = g.Y + 3.2f;
            return true;
        }
        g.Deployed = true;
        return false;   // رسید، حالا مثل بقیه می‌جنگد
    }

    // ── وضعیت اضطراری گروه (مهمات/روحیه) — مشترک بین همه‌ی دکترین‌ها ─────────
    static bool TriageGroup(ref Group g, Force me, bool attacker, int tick, BattleLog log, ref XorRng rng)
    {
        if (g.Posture == P_RETREAT) return false;
        float ammoR = 0.5f * (g.CAmmo / Math.Max(1f, g.CAmmo0) + g.MAmmo / Math.Max(1f, g.MAmmo0));
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

            // سرعت آفرود واقعی همان مدل، نه کسری از سرعت جاده
            float baseKmH;
            if (u.Type == 1)
            {
                var sp = f.Specs[u.Model];
                byte terrHere = field.TerrAt(u.X, u.Y);
                baseKmH = terrHere == T_PLAIN ? sp.Speed * 0.55f : sp.SpeedOff * 0.85f;
            }
            else baseKmH = 4.2f;
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

    // ═══════════════ فیزیک بالستیک مشترک (زمین و دریا) ══════════════════════

    // افت سرعت گلوله با فاصله (کشش هوا). گلوله‌ی سنگین‌تر و باریک‌تر بهتر سرعت نگه می‌دارد.
    //  ضریب مقطعی ساده: جرم / قطر²
    static float VelocityAt(float muzzleMs, float shellKg, float caliberMm, float rangeM)
    {
        float sectional = shellKg / MathF.Max(0.01f, (caliberMm * caliberMm) / 10000f);
        float k = 0.00042f / MathF.Max(0.05f, sectional);      // نرخ افت
        return muzzleMs * MathF.Exp(-k * rangeM);
    }

    // نفوذ در فاصله‌ی دلخواه بر پایه‌ی نفوذ مرجع ۵۰۰ متر و نسبت انرژی جنبشی.
    //  انرژی ∝ v²، و نفوذ تقریباً با √انرژی رابطه دارد → نفوذ ∝ v
    static float PenetrationAt(in TankSpec s, float rangeM)
    {
        float v500 = VelocityAt(s.MuzzleVel, s.ShellMass, s.Caliber, 500f);
        float vNow = VelocityAt(s.MuzzleVel, s.ShellMass, s.Caliber, rangeM);
        return s.PenAt500 * (vNow / MathF.Max(1f, v500));
    }

    // زره مؤثر با زاویه‌ی برخورد: هرچه زاویه از عمود بیشتر، ضخامت مؤثر بیشتر.
    //  impactCos = کسینوس زاویه‌ی برخورد (۱ = عمود کامل)
    static float EffectiveArmor(float plateMm, float slope, float impactCos)
        => plateMm * slope / Math.Clamp(impactCos, 0.28f, 1f);

    // احتمال نفوذ: تابع سیگموئید حول نسبت نفوذ به زره.
    //  پراکندگی طبیعی کیفیت فولاد و نقطه‌ی برخورد را مدل می‌کند.
    static float PenChance(float penMm, float armorMm)
    {
        float ratio = penMm / MathF.Max(1f, armorMm);
        return 1f / (1f + MathF.Exp(-(ratio - 1f) * 6.2f));
    }

    // آیا هدف از پهلو دیده می‌شود؟ زاویه‌ی بین بردار شلیک و جهت حرکت هدف.
    //  خروجی: کسینوس زاویه نسبت به جلوی هدف — نزدیک ۱ یعنی شلیک به سینه.
    static float FacingCos(float shooterX, float shooterY, in Group target)
    {
        float dx = shooterX - target.X, dy = shooterY - target.Y;
        float d = MathF.Sqrt(dx * dx + dy * dy);
        if (d < 0.001f) return 1f;
        dx /= d; dy /= d;
        // جهت رو به جلوی هدف = جهت حرکتش به سمت TgtX/TgtY
        float fx = target.TgtX - target.X, fy = target.TgtY - target.Y;
        float fd = MathF.Sqrt(fx * fx + fy * fy);
        if (fd < 0.001f) return 1f;                    // ایستاده = فرض سینه
        fx /= fd; fy /= fd;
        return Math.Clamp(dx * fx + dy * fy, -1f, 1f); // ۱=سینه، ۰=پهلو، -۱=پشت
    }

    // ═══════════ آتش زمینی: بالستیک واقعی + زاویه + مهمات شمارشی ═════════════
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

            // برد مؤثر از اپتیک و سرعت پوزه می‌آید، نه عدد ثابت
            float maxRange = u.Type == 1
                ? Math.Clamp(1.1f + ospec.Optics * 1.2f + ospec.MuzzleVel / 900f, 1.0f, 2.6f)
                : 0.9f;

            int best = -1; float bestScore = 0f, bestDist = 99f;
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
            float rangeM = bestDist * 1000f;

            // ── دقت: اپتیک، فاصله، محیط، خستگی ──
            float optics = 0.55f + ospec.Optics * 0.45f;
            float rangeFall = 1f / (1f + rangeM / 900f);       // افت دقت با فاصله
            float acc = 0.86f * optics * rangeFall * (0.45f + 0.55f * intelQ)
                        * TerAcc[field.TerrAt(u.X, u.Y)] * accEnv
                        * (1f - u.Supp * 0.5f) * nightPenalty;
            acc *= (0.9f + u.Exp * 0.3f);
            acc *= own.Prof.CrewQuality * famil;
            if (u.Posture is P_ADVANCE or P_ASSAULT or P_FLANK) acc *= 0.80f;
            if (!own.IsAttacker && u.Posture is P_DEFEND or P_AMBUSH or P_HOLD) acc *= DUGIN_ACC;
            if (field.ElevAt(u.X, u.Y) > field.ElevAt(t.X, t.Y) + 0.1f) acc *= 1.18f;

            float cover = TerCover[tt] * (t.Posture is P_DEFEND or P_AMBUSH or P_HOLD ? 1.25f : 0.8f);
            // برجک کند نمی‌تواند به تهدید پهلو سریع جواب بدهد
            float traverse = Math.Clamp(1.18f - ospec.TurretSec / 90f, 0.72f, 1.15f);
            float morale = 0.55f + u.Morale * 0.45f;
            float reliab = 0.88f + ospec.Reliab * 0.12f;
            float common = acc * morale * ambushMul * combatMul * reliab * traverse * (1f - u.Fatigue * 0.25f);

            if (u.Type == 1)
            {
                // ── شمار واقعی گلوله در این تیک: آهنگ آتش × زمان ──
                float minutes = TICK_MIN;
                float shotsPossible = u.Units * ospec.RoF * minutes * 0.05f;   // ۵٪ زمان درگیر آتش

                if (t.Type == 1)
                {
                    if (u.CAmmo >= 1f)
                    {
                        float shots = MathF.Min(shotsPossible, u.CAmmo);
                        float hits = shots * common;

                        // زاویه‌ی برخورد → کدام صفحه‌ی زره و چه ضخامت مؤثری
                        float cos = FacingCos(u.X, u.Y, t);
                        bool sideShot = cos < 0.45f;
                        float plate = sideShot ? fspec.ArmorSide : fspec.ArmorFront;
                        float impactCos = sideShot ? Math.Max(0.45f, MathF.Abs(cos)) : Math.Max(0.55f, cos);
                        float effArmor = EffectiveArmor(plate, fspec.Slope, impactCos);
                        if (t.Posture is P_DEFEND or P_AMBUSH) effArmor *= 1.18f;   // بدنه در سنگر

                        float pen = PenetrationAt(ospec, rangeM);
                        float pk = PenChance(pen, effArmor);
                        float kills = hits * 0.30f * pk * (0.9f + rng.NextF() * 0.25f);

                        ApplyDamage(foe, best, kills, own, u.Model, true);
                        u.CAmmo = MathF.Max(0f, u.CAmmo - shots);
                        u.Signature = Math.Min(1f, u.Signature + 0.55f);
                        duel += kills;
                        t.Supp = Math.Min(1f, t.Supp + 0.12f);
                    }
                }
                else
                {
                    // ضدپیاده: گلوله‌ی انفجاری + مسلسل، هر کدام از انبار خودش
                    float kills = 0f;
                    if (u.CAmmo >= 1f)
                    {
                        float heShots = MathF.Min(shotsPossible * 0.6f, u.CAmmo);
                        // اثر انفجار ∝ ریشه‌ی سوم وزن ماده‌ی منفجره (شعاع کشندگی)
                        float blast = MathF.Pow(MathF.Max(0.01f, ospec.HeFiller), 0.333f) * 2.6f;
                        kills += heShots * common * blast * (1f - cover * 0.55f);
                        u.CAmmo = MathF.Max(0f, u.CAmmo - heShots);
                        u.Signature = Math.Min(1f, u.Signature + 0.5f);
                    }
                    if (u.MAmmo >= 20f)
                    {
                        float burst = u.Units * ospec.MgCount * 220f * (minutes / 60f);
                        burst = MathF.Min(burst, u.MAmmo);
                        kills += burst * 0.00042f * common * (1f - cover * 0.85f);
                        u.MAmmo = MathF.Max(0f, u.MAmmo - burst);
                        u.Signature = Math.Min(1f, u.Signature + 0.22f);
                    }
                    if (kills > 0f)
                    {
                        ApplyDamage(foe, best, kills, own, u.Model, false);
                        t.Supp = Math.Min(1f, t.Supp + 0.3f);
                    }
                }
            }
            else
            {
                if (t.Type == 0)
                {
                    if (u.MAmmo >= 20f)
                    {
                        float rounds = u.Units * 9f * (TICK_MIN / 60f);   // شلیک انفرادی
                        rounds = MathF.Min(rounds, u.MAmmo);
                        float kills = rounds * 0.0022f * common * (1f - cover * 0.8f);
                        ApplyDamage(foe, best, kills, own, u.Model, false);
                        u.MAmmo = MathF.Max(0f, u.MAmmo - rounds);
                        u.Signature = Math.Min(1f, u.Signature + 0.16f);
                        t.Supp = Math.Min(1f, t.Supp + 0.15f);
                    }
                }
                else if (bestDist < 0.45f)
                {
                    // پیاده در برابر زره: نارنجک و مین چسبان، فقط در فاصله‌ی نزدیک.
                    //  زره پهلو و عقب هدف اصل است، نه سینه.
                    float cos = FacingCos(u.X, u.Y, t);
                    float plate = cos < 0.45f ? fspec.ArmorSide : fspec.ArmorFront;
                    float resist = 1f / (1f + plate / 40f);
                    float kills = u.Units * 0.0060f * common * resist;
                    ApplyDamage(foe, best, kills, own, u.Model, true);
                    u.MAmmo = MathF.Max(0f, u.MAmmo - u.Units * 2f);
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

            //  – ۳۷: عقب‌نشینی تصمیم فرماندهی است، نه فرار خودسر
            float fallbackP = FALLBACK_P * (0.45f + f.Cmd.Caution * 1.6f);
            if (f.Cmd.Doctrine == 22) fallbackP *= 1.7f;      // دکترین تله ذاتاً کشسان است
            if (f.Cmd.Doctrine == 11) fallbackP *= 0.5f;      // خط ثابت زمین نمی‌دهد
            if (!f.IsAttacker && lossR > 0.35f && u.Morale < 0.55f && u.Posture != P_RETREAT
                && rng.NextF() < fallbackP)
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
            //  – ۳۶: ذخیره‌ی هنوز نرسیده جزو خط تثبیت‌شده نیست
            if (!g.Deployed) continue;
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
                if (!g.Deployed) continue;
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
            float ammoR = 0.5f * (f.G[i].CAmmo / Math.Max(1f, f.G[i].CAmmo0) + f.G[i].MAmmo / Math.Max(1f, f.G[i].MAmmo0));
            float am = 0.45f + 0.55f * Math.Clamp(ammoR * 1.6f, 0f, 1f);
            if (f.G[i].Type == 1)
            {
                var s = f.Specs[f.G[i].Model];
                p += f.G[i].Units * (8f + s.ArmorFront * 0.04f + s.PenAt500 * 0.04f) * am;
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
    // ═══════════════════ نبرد هوایی: شبیه‌سازی زمان‌گام ══════════════════════
    //  برخلاف زمین که شبکه‌ی دوبعدی دارد، هوا در «ارتفاع» و «فاصله تا هدف» مدل
    //  می‌شود. ارتباط رادیویی در هوا ضعیف است، پس فرمانده‌ی هوایی فقط چند تصمیم
    //  کلان می‌گیرد و بعد هر دسته تقریباً مستقل می‌جنگد — دقیقاً مثل واقعیت ۱۹۴۰.

    const int AIR_TICKS = 20;          // ۲۰ گام × ۳ دقیقه = یک ماموریت ۶۰ دقیقه‌ای
    const float AIR_TICK_MIN = 3f;

    struct AirGroup
    {
        public float Alt;          // ارتفاع (متر) — سرمایه‌ی انرژی
        public float DistKm;       // فاصله تا هدف
        public float Count;        // تعداد هواپیمای زنده
        public float Count0;
        public float AmmoSec;      // ثانیه آتش باقی‌مانده
        public float Fuel;         // دقیقه پرواز باقی‌مانده
        public byte  Role;         // 0=جنگنده اسکورت، 1=جنگنده آزاد، 2=بمب‌افکن، 3=رهگیر
        public bool  Alive;
        public bool  Engaged;
        public bool  DroppedBombs;
    }

    // شانس دیدن هدف در هوا: تابع اختلاف ارتفاع، فاصله، ابر و خورشید.
    //  در ۱۹۴۰ رادار هوابرد نبود؛ همه‌چیز چشمی است.
    static float AirSpot(float myAlt, float foeAlt, float sepKm, byte weather, byte time, float cloudBase = 9999f)
    {
        float baseSpot = 1f - Math.Clamp(sepKm / 12f, 0f, 0.95f);
        // هواپیمای بالاتر، پایینی را روی زمینه‌ی زمین راحت‌تر می‌بیند
        float altEdge = Math.Clamp((myAlt - foeAlt) / 3000f, -0.35f, 0.35f);
        float cloud = WxAir[weather];
        float light = TimeAir[time];
        //  – ۵۸: اگر ابر بین دو هواپیما باشد، همدیگر را گم می‌کنند
        bool across = (myAlt - cloudBase) * (foeAlt - cloudBase) < 0f;
        if (across) cloud *= 0.28f;
        else if (myAlt > cloudBase && foeAlt > cloudBase) cloud = MathF.Min(1f, cloud * 1.35f);  // بالای ابر آفتابی
        return Math.Clamp((baseSpot + altEdge) * cloud * light, 0.02f, 0.98f);
    }

    // مزیت انرژی: هواپیمای بالاتر می‌تواند شیرجه بزند و ابتکار عمل را بگیرد.
    static float EnergyEdge(float myAlt, float foeAlt)
        => Math.Clamp(1f + (myAlt - foeAlt) / 4000f, 0.62f, 1.45f);

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

        float aFamil = Familiarity(atk.Faction, aFs.Origin, aProf);
        float dFamil = Familiarity(def.Faction, dFs.Origin, dProf);
        byte wx = field.Weather, tm = field.StartTime;

        // ── ۵۵: تصمیم سوخت فرمانده‌ی هوایی ──
        //  بمباران راهبردی نیاز به برد دارد → بال پر (زمان پرواز بیشتر، چرخش کندتر).
        //  برتری هوایی نیاز به چابکی دارد → بال سبک (چابک ولی زمان کمتر روی هدف).
        bool heavyFuel = aAirStrat == 2;
        float fuelMul = heavyFuel ? 1.35f : 0.85f;          // ضریب زمان پرواز
        float agilityPenalty = heavyFuel ? 1.12f : 0.94f;   // ضریب زمان دور زدن (بیشتر = کندتر)

        // ── ارتفاع ورود: تصمیم کلان فرمانده‌ی هوایی ──
        //  شکار آزاد = بالا برای مزیت انرژی. حمله به پایگاه = پایین برای غافلگیری.
        //  بمباران دقیق = بالا برای امنیت. بمباران منطقه‌ای = متوسط.
        float escortAlt, bomberAlt;
        if (aAirStrat == 1)
        {
            escortAlt = aAirTac == 1 ? MathF.Min(aFs.CeilingM * 0.85f, 7000f) : 1200f;
            bomberAlt = aAirTac == 1 ? 4000f : 1500f;
        }
        else
        {
            bomberAlt = aAirTac == 1 ? MathF.Min(aBs.CeilingM * 0.80f, 7500f) : 4200f;
            escortAlt = bomberAlt + 800f;   // اسکورت همیشه کمی بالاتر
        }
        // مدافع: CAP بالا می‌ایستد، دفاع نقطه‌ای پایین می‌ماند
        float capAlt = (dAirStrat == 1 && dAirTac == 1) ? MathF.Min(dFs.CeilingM * 0.80f, 6500f) : 2500f;

        // ── تشکیل دسته‌ها ──
        var A = new List<AirGroup>();
        var D = new List<AirGroup>();
        int perGroup = 12;

        long left = aFight;
        bool escortDuty = aBomb > 0 && aAirStrat == 2;   // در بمباران، جنگنده اسکورت است
        while (left > 0)
        {
            float n = Math.Min(left, perGroup);
            A.Add(new AirGroup { Alt = escortAlt, DistKm = 60f, Count = n, Count0 = n,
                AmmoSec = aFs.AmmoSec, Fuel = aFs.RangeKm / MathF.Max(1f, aFs.SpeedKmh) * 60f * fuelMul,
                Role = (byte)(escortDuty ? 0 : 1), Alive = true });
            left -= (long)n;
        }
        left = aBomb;
        while (left > 0)
        {
            float n = Math.Min(left, perGroup);
            A.Add(new AirGroup { Alt = bomberAlt, DistKm = 60f, Count = n, Count0 = n,
                AmmoSec = 999f, Fuel = aBs.RangeKm / MathF.Max(1f, aBs.SpeedKmh) * 60f,
                Role = 2, Alive = true });
            left -= (long)n;
        }
        left = dFight;
        while (left > 0)
        {
            float n = Math.Min(left, perGroup);
            D.Add(new AirGroup { Alt = capAlt, DistKm = 0f, Count = n, Count0 = n,
                AmmoSec = dFs.AmmoSec, Fuel = dFs.RangeKm / MathF.Max(1f, dFs.SpeedKmh) * 60f,
                Role = 3, Alive = true });
            left -= (long)n;
        }

        float aLostF = 0f, aLostB = 0f, dLostF = 0f, dLostAA = 0f;
        float bombsOnTarget = 0f, bombsOnTroops = 0f;
        float aaStrength = dAA;
        bool anyCombat = false;
        float capBonus = (dAirStrat == 1 && dAirTac == 1) ? 1.20f : 1f;
        float flakBonus = (dAirStrat == 2 && dAirTac == 1) ? 1.35f : 1f;

        // ── حلقه‌ی زمانی ماموریت ──
        for (int t = 0; t < AIR_TICKS; t++)
        {
            byte timeNow = (byte)((tm + t / 12) & 3);

            // ۱) نزدیک شدن مهاجم به هدف
            for (int i = 0; i < A.Count; i++)
            {
                var g = A[i];
                if (!g.Alive) continue;
                float spd = g.Role == 2 ? aBs.SpeedKmh : aFs.SpeedKmh;
                g.DistKm = MathF.Max(0f, g.DistKm - spd * (AIR_TICK_MIN / 60f));
                g.Fuel -= AIR_TICK_MIN;
                if (g.Fuel <= 0f) { g.Alive = false; }      // سوخت تمام → بازگشت
                A[i] = g;
            }

            // ۲) رهگیری: مدافع باید اول ببیند
            for (int di = 0; di < D.Count; di++)
            {
                var d = D[di];
                if (!d.Alive || d.AmmoSec <= 0f) continue;
                int target = -1; float bestVal = 0f;
                for (int ai = 0; ai < A.Count; ai++)
                {
                    var a = A[ai];
                    if (!a.Alive) continue;
                    float spot = AirSpot(d.Alt, a.Alt, a.DistKm, wx, timeNow, field.CloudBaseM);
                    if (rng.NextF() > spot) continue;
                    // اولویت: بمب‌افکن مهم‌تر از جنگنده
                    float val = (a.Role == 2 ? 3.2f : 1f) * a.Count / (1f + a.DistKm * 0.05f);
                    if (val > bestVal) { bestVal = val; target = ai; }
                }
                if (target < 0) continue;

                var tg = A[target];
                anyCombat = true;
                float edge = EnergyEdge(d.Alt, tg.Alt) * capBonus * dProf.CrewQuality * dFamil;

                if (tg.Role == 2)
                {
                    // رهگیری بمب‌افکن: آتش دفاعی متقابل
                    float pass = MathF.Min(d.AmmoSec, AIR_TICK_MIN * 6f);
                    float killed = pass * dFs.GunKgMin / 60f * 0.030f * edge / MathF.Max(0.4f, aBs.Durability);
                    killed = MathF.Min(killed, tg.Count);
                    tg.Count -= killed; aLostB += killed;

                    // تیراندازهای بمب‌افکن جواب می‌دهند — پوشش کروی مهم است
                    float defFire = tg.Count * aBs.DefGunKgMin / 60f * (aBs.DefPositions / 8f) * 0.016f * AIR_TICK_MIN;
                    float dk = MathF.Min(defFire, d.Count);
                    d.Count -= dk; dLostF += dk;
                    d.AmmoSec -= pass;
                }
                else
                {
                    // سگ‌جنگی: چابکی و انرژی و وزن آتش
                    float aTurn = 1f / MathF.Max(1f, aFs.TurnSec * agilityPenalty);   // ۵۵: بال پر = چرخش کندتر
                    float dTurn = 1f / MathF.Max(1f, dFs.TurnSec);
                    float aEdge = EnergyEdge(tg.Alt, d.Alt) * aProf.CrewQuality * aFamil;

                    float dScore = dTurn * dFs.GunKgMin * edge;
                    float aScore = aTurn * aFs.GunKgMin * aEdge;
                    float total = dScore + aScore;

                    float burn = MathF.Min(AIR_TICK_MIN * 4f, MathF.Min(d.AmmoSec, tg.AmmoSec));
                    float intensity = burn * 0.055f;
                    float aKilled = MathF.Min(tg.Count, intensity * dScore / MathF.Max(1f, total) / MathF.Max(0.4f, aFs.Durability));
                    float dKilled = MathF.Min(d.Count, intensity * aScore / MathF.Max(1f, total) / MathF.Max(0.4f, dFs.Durability));

                    tg.Count -= aKilled; aLostF += aKilled;
                    d.Count -= dKilled; dLostF += dKilled;
                    d.AmmoSec -= burn; tg.AmmoSec -= burn;

                    // در سگ‌جنگی ارتفاع خرج می‌شود و با نرخ صعود هر هواپیما جبران
                    float aRegain = aFs.ClimbMs * AIR_TICK_MIN * 60f * 0.20f;
                    float dRegain = dFs.ClimbMs * AIR_TICK_MIN * 60f * 0.20f;
                    tg.Alt = Math.Clamp(tg.Alt - 400f + aRegain, 500f, aFs.CeilingM);
                    d.Alt  = Math.Clamp(d.Alt  - 300f + dRegain, 500f, dFs.CeilingM);
                    tg.Engaged = true;
                }
                if (tg.Count < 0.5f) tg.Alive = false;
                if (d.Count < 0.5f) d.Alive = false;
                A[target] = tg; D[di] = d;
            }

            // ۳) پدافند: فقط وقتی مهاجم به برد رسیده
            if (aaStrength > 0f)
            {
                for (int ai = 0; ai < A.Count; ai++)
                {
                    var a = A[ai];
                    if (!a.Alive || a.DistKm > 6f) continue;
                    // آتش ضدهوایی با ارتفاع سخت‌تر می‌شود؛ توپ ۸۸ هم سقفی دارد
                    float altFactor = Math.Clamp(1f - (a.Alt - 3000f) / 9000f, 0.30f, 1f);
                    float exposure = a.Role == 2 ? 1f : 0.55f;   // بمب‌افکن مسیر مستقیم می‌رود
                    float dur = a.Role == 2 ? aBs.Durability : aFs.Durability;
                    float hit = aaStrength * flakBonus * 0.00055f * altFactor * exposure
                                * AIR_TICK_MIN / MathF.Max(0.4f, dur) * rng.Range(0.7f, 1.3f);
                    hit = MathF.Min(hit, a.Count);
                    a.Count -= hit;
                    if (a.Role == 2) aLostB += hit; else aLostF += hit;
                    if (a.Count < 0.5f) a.Alive = false;
                    A[ai] = a;
                    anyCombat = true;
                }
                // پدافند هم زیر بمب و رگبار تلفات می‌دهد
                float overhead = 0f;
                foreach (var a in A) if (a.Alive && a.DistKm < 3f) overhead += a.Count;
                if (overhead > 0f)
                {
                    float suppressed = MathF.Min(aaStrength, overhead * rng.Range(0.004f, 0.012f) * AIR_TICK_MIN);
                    aaStrength -= suppressed; dLostAA += suppressed;
                }
            }

            // ۴) رهاسازی بمب
            for (int ai = 0; ai < A.Count; ai++)
            {
                var a = A[ai];
                if (!a.Alive || a.Role != 2 || a.DroppedBombs || a.DistKm > 0.5f) continue;
                // دقت: CEP پایه، بدتر با ارتفاع و ابر، بهتر با نشانه‌روی خوب
                float cep = aBs.AccuracyCep * (1f + a.Alt / 9000f) / MathF.Max(0.35f, WxAir[wx]);
                //  – ۵۸: بمباران از بالای لایه‌ی ابر تقریباً کور است
                if (a.Alt > field.CloudBaseM) cep *= 2.4f;
                float hitFrac = Math.Clamp(220f / MathF.Max(60f, cep), 0.06f, 0.85f);
                float tons = a.Count * aBs.BombKg / 1000f;
                if (aAirStrat == 2) bombsOnTarget += tons * hitFrac;
                else bombsOnTroops += tons * hitFrac;
                a.DroppedBombs = true;
                a.Alt = MathF.Max(800f, a.Alt);
                A[ai] = a;
            }

            // ۵) پشتیبانی نزدیک جنگنده‌ها روی نیروی زمینی
            if (aAirStrat == 1)
            {
                foreach (var a in A)
                {
                    if (!a.Alive || a.Role == 2 || a.DistKm > 1.5f || a.AmmoSec <= 0f) continue;
                    bombsOnTroops += a.Count * (aFs.BombKg / 1000f) * 0.12f;
                }
            }
        }

        long aFightLost = (long)MathF.Round(MathF.Min(aFight, aLostF));
        long aBombLost  = (long)MathF.Round(MathF.Min(aBomb, aLostB));
        // خدمه‌ی از دست رفته: B-17 ده نفره سنگین‌تر از DB-3 چهار نفره است
        o.AircrewLost = (long)MathF.Round(aBombLost * aBs.Crew + aFightLost);
        long dFightLost = (long)MathF.Round(MathF.Min(dFight, dLostF));
        long dAALost    = (long)MathF.Round(MathF.Min(dAA, dLostAA));

        long aFightLeft = aFight - aFightLost;
        long aBombLeft  = aBomb - aBombLost;
        long dFightLeft = dFight - dFightLost;

        // ── برتری هوایی از توان باقی‌مانده‌ی واقعی ──
        float atkRemain = aFightLeft * (aFs.GunKgMin / MathF.Max(1f, aFs.TurnSec)) + aBombLeft * 1.2f;
        float defRemain = dFightLeft * (dFs.GunKgMin / MathF.Max(1f, dFs.TurnSec)) + (aaStrength) * 0.05f;
        o.Superiority = Math.Clamp((atkRemain - defRemain) / MathF.Max(1f, atkRemain + defRemain), -1f, 1f);
        o.HadAirCombat = anyCombat;

        // ── اثر روی زمین و اقتصاد ──
        if (bombsOnTroops > 0f)
            o.CasAtk = 1f + Math.Clamp(bombsOnTroops / MathF.Max(6f, (atk.Soldiers + 1) * 0.0018f), 0f, 0.6f);
        if (o.Superiority < -0.1f && dFightLeft > 0)
            o.CasDef = 1f + Math.Clamp(dFightLeft * dFs.BombKg / 1000f / MathF.Max(6f, (def.Soldiers + 1) * 0.002f), 0f, 0.4f);

        if (aAirStrat == 2 && bombsOnTarget > 0f)
        {
            // تناژ روی هدف → درصد خسارت اقتصادی
            float moneyFrac = Math.Clamp(bombsOnTarget * 0.0075f, 0f, aAirTac == 1 ? 0.35f : 0.30f);
            float ironFrac  = Math.Clamp(bombsOnTarget * 0.0075f, 0f, aAirTac == 1 ? 0.40f : 0.18f);
            if (aAirTac == 1)   // بمباران دقیق صنایع
            {
                o.StratMoney = (long)(def.Money * moneyFrac * 0.9f);
                o.StratIron  = (long)(def.Iron * ironFrac);
                o.StratWelfare = Math.Clamp(bombsOnTarget * 0.010f, 0f, 4f);
            }
            else                // بمباران منطقه‌ای شهرها
            {
                o.StratMoney = (long)(def.Money * moneyFrac);
                o.StratIron  = (long)(def.Iron * ironFrac * 0.5f);
                o.StratWelfare = Math.Clamp(bombsOnTarget * 0.014f, 0f, 2f);
            }
        }

        o.AtkFightersLost = Math.Max(0, aFightLost);
        o.AtkBombersLost  = Math.Max(0, aBombLost);
        o.DefFightersLost = Math.Max(0, dFightLost);
        o.DefAntiAirLost  = Math.Max(0, dAALost);
        o.BombTonsOnTarget = bombsOnTarget;
        o.EscortAltM = escortAlt;
        o.CapAltM = capAlt;
        o.Narrative = BuildAirNarrative(o, aFight, aBomb, dFight, dAA, aAirStrat, aAirTac, aFs, aBs, dFs, field);
        return o;
    }

    static string? BuildAirNarrative(AirOutcome air, long aFight, long aBomb, long dFight, long dAA,
        int aAirStrat, int aAirTac, FighterSpec aFs, BomberSpec aBs, FighterSpec dFs, Field field)
    {
        if (aFight == 0 && aBomb == 0 && dFight == 0 && dAA == 0) return null;
        var s = new StringBuilder();

        if (WxAir[field.Weather] < 0.7f)
            s.Append($"هوای {WeatherName[field.Weather]} دید خلبان‌ها را برید؛ ");

        if (aFight > 0 || aBomb > 0)
            s.Append($"سازند مهاجم در ارتفاع {air.EscortAltM:F0} متری وارد شد");
        if (dFight > 0)
            s.Append($" و گشت مدافع از {air.CapAltM:F0} متری روی سرش نشسته بود");
        s.Append(". ");

        if (air.HadAirCombat && aFight > 0 && dFight > 0)
        {
            string edge = air.EscortAltM > air.CapAltM + 500f
                ? $"{aFs.Name}ها با مزیت ارتفاع شیرجه زدند"
                : air.CapAltM > air.EscortAltM + 500f
                ? $"{dFs.Name}ها از بالا شیرجه زدند و ابتکار را گرفتند"
                : "دو طرف تقریباً هم‌ارتفاع درگیر شدند";
            s.Append($"{edge}؛ {air.AtkFightersLost} در برابر {air.DefFightersLost} جنگنده سرنگون شد. ");
        }
        else if (aFight > 0 && dFight == 0)
            s.Append($"{aFs.Name}ها بدون مقاومت هوایی آسمان را در اختیار گرفتند. ");

        if (aBomb > 0 && dFight > 0 && air.AtkBombersLost > 0)
            s.Append($"تیراندازهای {aBs.Name} ({aBs.DefPositions} جایگاه) جواب رهگیرها را دادند ولی {air.AtkBombersLost} فروند از دست رفت. ");

        if (dAA > 0 && air.AtkBombersLost + air.AtkFightersLost > 0)
            s.Append($"آتش پدافند از پایین می‌جوشید و {air.DefAntiAirLost} قبضه هم زیر رگبار خرد شد. ");

        if (air.BombTonsOnTarget > 0.05f)
            s.Append($"{air.BombTonsOnTarget:F1} تن بمب روی هدف نشست. ");

        if (aAirStrat == 2 && (air.StratMoney > 0 || air.StratIron > 0))
            s.Append(aAirTac == 1
                ? $"بمباران دقیق صنایع {air.StratMoney / 1000.0:F1}K پول و {air.StratIron / 1000.0:F1}K آهن را نابود کرد. "
                : $"بمباران منطقه‌ای {air.StratMoney / 1000.0:F1}K پول خسارت زد و روحیه‌ی شهرها را شکست. ");
        else if (aAirStrat == 1 && air.Superiority > 0.15)
            s.Append("با برتری در آسمان، پشتیبانی نزدیک مستقیم روی سر مدافع کار کرد. ");
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
        List<(string Model, long Count)>? attTankBreakdown, long attSoldiers,
        List<(string Model, long Count)>? attFighterBreakdown, List<(string Model, long Count)>? attBomberBreakdown,
        List<(string Model, long Count)>? defTankBreakdown, long defSoldiers,
        List<(string Model, long Count)>? defFighterBreakdown,
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
        List<(string Model, long Count)>? attTankBreakdown, long attSoldiers,
        List<(string Model, long Count)>? attFighterBreakdown, List<(string Model, long Count)>? attBomberBreakdown,
        List<(string Model, long Count)>? defTankBreakdown, long defSoldiers,
        List<(string Model, long Count)>? defFighterBreakdown,
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
        res.AttackerCrewLost += air.AircrewLost;
        if (air.Narrative != null) log.Add(2, 2, LG_AIR, air.Narrative);

        DistributeLoss(res.AttackerPlaneLossByModel, aFighterList, air.AtkFightersLost);
        DistributeLoss(res.AttackerBomberLossByModel, aBomberList, air.AtkBombersLost);
        DistributeLoss(res.DefenderPlaneLossByModel, dFighterList, air.DefFightersLost);

        // ── نبرد زمینی ───────────────────────────────────────────────────────
        bool defHasGround = (dTanks + dSold) > 0;
        float effDepth = 0f;
        int tick = 0, routsA = 0, routsD = 0;
        bool contact = false, ambushFired = false;
        Force? fa = null, fd = null;

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

            for (int i = 0; i < MAX_GROUPS; i++)
            {
                fa.IntelOnFoe[i] = default; fa.IntelOnFoe[i].Stale = 9999f;
                fd.IntelOnFoe[i] = default; fd.IntelOnFoe[i].Stale = 9999f;
            }

            AnnounceField(field, log);

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
                //  – ۳۲: آب‌وهوا وسط نبرد عوض می‌شود
                if (tick == field.WeatherShiftTick && field.WeatherNext != field.Weather)
                {
                    byte old = field.Weather;
                    field.Weather = field.WeatherNext;
                    log.Add(tick, 2, LG_ENV,
                        $"هوا از {WeatherName[old]} به {WeatherName[field.Weather]} تغییر کرد و شرایط میدان عوض شد.");
                }

                byte tnow = field.TimeAt(tick);
                //  گذر از یک فاز روز به فاز بعد — حالا که چرخه یک بار اتفاق
                //  می‌افتد، هر گذر یک رویداد واقعی نبرد است و باید ثبت شود.
                if (tick > 0 && tnow != field.TimeAt(tick - 1))
                {
                    log.Add(tick, 2, LG_ENV, tnow switch
                    {
                        TM_DAWN  => "سپیده زد و دید میدان کم‌کم باز شد.",
                        TM_DAY   => "روز کاملاً روشن شد؛ دید و آتش دقیق به بیشترین حد رسید.",
                        TM_DUSK  => "آفتاب پایین آمد و سایه‌ها میدان را پوشاند.",
                        _        => "شب فرا رسید؛ دید به حداقل رسید و درگیری‌ها پراکنده شد.",
                    });
                }
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
                float aMul = casA * supplyA * surprise;
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
                // ── دور زدن جبهه: مدافعی که پشتش بریده شده نمی‌تواند سر جا بماند ──
                if (tick % 6 == 0 && effDepth > 8f)
                {
                    for (int j = 0; j < fd.N; j++)
                    {
                        ref Group dg = ref fd.G[j];
                        if (!dg.Alive || dg.Posture == P_RETREAT) continue;
                        // اگر ستون مهاجم از این یگان عبور کرده و عمیقاً پشت سرش است
                        if (dg.Y > effDepth - 4f) continue;
                        float nearFriend = 0f, nearFoe = 0f;
                        for (int k = 0; k < fd.N; k++)
                        {
                            if (k == j || !fd.G[k].Alive) continue;
                            float ddx = fd.G[k].X - dg.X, ddy = fd.G[k].Y - dg.Y;
                            if (ddx * ddx + ddy * ddy < 25f) nearFriend += fd.G[k].Units;
                        }
                        for (int k = 0; k < fa.N; k++)
                        {
                            if (!fa.G[k].Alive) continue;
                            float ddx = fa.G[k].X - dg.X, ddy = fa.G[k].Y - dg.Y;
                            if (ddx * ddx + ddy * ddy < 36f) nearFoe += fa.G[k].Units;
                        }
                        if (nearFoe > nearFriend * 1.5f && rng.NextF() < 0.30f)
                        {
                            dg.Posture = P_RETREAT;
                            dg.TgtY = Math.Min(DEPTH_KM, dg.Y + 10f);
                            dg.Morale = Math.Max(0f, dg.Morale - 0.25f);
                            routsD++;
                        }
                    }
                }

                if (haltTicks > 85 && contact)
                {
                    log.Add(tick, 2, LG_CRISIS, $"پیشروی در عمق {effDepth:F1} کیلومتری زمین‌گیر شد و جبهه به بن‌بست رسید.");
                    tick++; break;
                }
            }

            // ── تثبیت نهایی ──
            //  اگر مقاومت سازمان‌یافته‌ی مدافع عملاً از بین رفته، عقبه‌ی جبهه باز است
            //  و ستون مهاجم بقیه‌ی راه را تقریباً بدون درگیری می‌رود.
            float aEnd = SidePower(fa), dEnd = SidePower(fd);
            float endDom = aEnd / Math.Max(1f, aEnd + dEnd);
            if (effDepth >= 18f && endDom > 0.80f)
                effDepth = WIN_DEPTH;                                   // فروپاشی کامل جبهه
            else if (effDepth >= 22f && effDepth < WIN_DEPTH)
                effDepth = Math.Min(WIN_DEPTH, effDepth + (WIN_DEPTH - effDepth) * 0.65f);
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
            frac, effDepth, anyGround, defHasGround, aRecover, dRecover, routsA, routsD);

        SaveBattle(attacker, defender, res);
        return res;
    }

    static List<(string Model, long Count)> Normalize(List<(string Model, long Count)>? src, long cap)
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

    // ═══════════ میدان نبرد: هیچ ضریب انتزاعی روی دکترین وجود ندارد ═════════
    //  در نسخه‌های قبل یک جدول «سنگ-کاغذ-قیچی» بود که به هر جفت دکترین یک عدد
    //  می‌داد و آن عدد مستقیم در تلفات ضرب می‌شد. آن جدول حذف شد.
    //
    //  دلیل: انتخاب استراتژی و تاکتیک باید از راه *رفتار* اثر بگذارد، نه از راه
    //  یک ضریب سرجمع. کمین وقتی جواب می‌دهد که واقعاً پنهان مانده باشد و ضریب
    //  ۲.۶ برابرِ شلیک اول را بگیرد؛ محاصره وقتی جواب می‌دهد که بازوها واقعاً
    //  به عمق برسند و حلقه ببندد؛ اکتشاف وقتی جواب می‌دهد که IntelQuality واقعاً
    //  بالا برود و ضعیف‌ترین سکتور درست پیدا شود. اگر یکی از اینها اتفاق نیفتد،
    //  دکترین هم نباید پاداشی بگیرد — و اگر اتفاق بیفتد، خودِ شبیه‌سازی
    //  پاداشش را می‌دهد. یک ضریب اضافه فقط همان اثر واقعی را کمرنگ می‌کرد.
    //
    //  زمین هم همین‌طور: اثر گذرگاه و مرداب و شهر از قبل در سرعت، پوشش، دید و
    //  دقتِ خانه‌به‌خانه‌ی نقشه هست. ضرب دوباره‌ی آن در یک عدد کلی، حساب مضاعف بود.
    //
    //  این تابع فقط میدان را برای گزارش ثبت می‌کند و هیچ عددی به نبرد نمی‌دهد.
    static void AnnounceField(Field field, BattleLog log)
    {
        log.Add(0, 2, LG_ENV, $"میدان نبرد: {MapName[field.MapType]} — {MapNote[field.MapType]}.");
        byte terr = field.DominantTerrainNear(FRONT_KM / 2f);
        log.Add(0, 2, LG_ENV, $"زمین غالب محور میانی: {TerName[terr]}.");
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
    static string? ModelLossLines(Force? f, string indent = "   ")
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
    static string? ArmorMatchupLines(Force? own, Force? foe)
    {
        if (own == null || foe == null || own.ModelNames.Length == 0 || foe.ModelNames.Length == 0) return null;
        var sb = new StringBuilder();
        // شاخص‌ترین مدل هر طرف
        int oi = 0; for (int i = 1; i < own.ModelSent.Length; i++) if (own.ModelSent[i] > own.ModelSent[oi]) oi = i;
        int fi = 0; for (int i = 1; i < foe.ModelSent.Length; i++) if (foe.ModelSent[i] > foe.ModelSent[fi]) fi = i;
        if (own.ModelSent[oi] <= 0 || foe.ModelSent[fi] <= 0) return null;

        var os = own.Specs[oi]; var fs = foe.Specs[fi];
        float penOwn = PenetrationAt(os, 800f), penFoe = PenetrationAt(fs, 800f);
        float penGap = penOwn - fs.ArmorFront;
        float defGap = penFoe - os.ArmorFront;

        string verdict;
        if (penGap > 12f && defGap < 0f)
            verdict = $"{os.Name} با توپ {os.Caliber:F0}mm در ۸۰۰ متری {penOwn:F0}mm فولاد می‌درید و زره {fs.ArmorFront:F0}mm جلوی {fs.Name} جلودارش نبود؛ در مقابل گلوله‌ی {fs.Name} ({penFoe:F0}mm) روی زره {os.ArmorFront:F0}mm آن کمانه می‌کرد.";
        else if (penGap < 0f && defGap > 12f)
            verdict = $"زره {fs.ArmorFront:F0}mm جلوی {fs.Name} در برابر {penOwn:F0}mm نفوذ {os.Name} در ۸۰۰ متری تقریباً مصون بود؛ ولی توپ {fs.Caliber:F0}mm آن با {penFoe:F0}mm زره {os.ArmorFront:F0}mm را راحت می‌شکافت.";
        else if (penGap > 0f && defGap > 0f)
            verdict = $"{os.Name} ({penOwn:F0}mm) و {fs.Name} ({penFoe:F0}mm) هر دو زره‌ی هم را می‌زدند؛ برنده‌ی هر تک‌درگیری آن‌که زودتر شلیک می‌کرد — و {(os.RoF > fs.RoF ? os.Name : fs.Name)} آهنگ آتش بالاتری داشت.";
        else
            verdict = $"نه {os.Name} ({penOwn:F0}mm) و نه {fs.Name} ({penFoe:F0}mm) نمی‌توانستند زره‌ی هم را از روبه‌رو بشکافند؛ نبرد به دور زدن و شلیک از پهلو کشید.";

        sb.Append(verdict);

        if (own.Owner != os.Origin)
            sb.Append($" ضمناً خدمه‌ی {FactionFa(own.Owner)} روی زره‌ی {FactionFa(os.Origin)} می‌جنگیدند و کارایی‌شان حدود {(int)Math.Round((1f - own.Prof.ForeignAdapt) * 100)}٪ کمتر از خدمه‌ی بومی همان تانک بود.");

        return sb.ToString();
    }

    // ─────────── تحلیل فکشن ───────────
    static string? FactionAnalysis(Force? fa, Force? fd)
    {
        if (fa == null) return null;
        var sb = new StringBuilder();
        sb.Append($"دکترین {FactionFa(fa.Owner)} مهاجم: {fa.Prof.Doctrine}.");
        if (fd != null)
            sb.Append($"\n   دکترین {FactionFa(fd.Owner)} مدافع: {fd.Prof.Doctrine}.");
        return sb.ToString();
    }

    // ── ۴۰: نقشه‌ی متنی جبهه ──
    //  یک نمای ۱۰ ستونی از جبهه که نشان می‌دهد رخنه کجا شکل گرفت.
    //  با سوییچ ادمین روشن/خاموش می‌شود (پیش‌فرض خاموش).
    public static bool ShowFrontMap = false;

    static string? FrontMap(Force? fa, Force? fd, Field field, float depth)
    {
        if (fa == null || fd == null) return null;
        Span<float> atk = stackalloc float[SECTORS];
        Span<float> def = stackalloc float[SECTORS];
        Span<float> adv = stackalloc float[SECTORS];
        for (int s = 0; s < SECTORS; s++) { atk[s] = 0f; def[s] = 0f; adv[s] = 0f; }

        for (int i = 0; i < fa.N; i++)
        {
            ref Group g = ref fa.G[i];
            if (!g.Alive || g.Posture is P_RETREAT or P_REGROUP) continue;
            int s = Math.Clamp((int)(g.X / SECTOR_KM), 0, SECTORS - 1);
            atk[s] += g.Type == 1 ? g.Units * 10f : g.Units;
            if (g.Y > adv[s]) adv[s] = g.Y;
        }
        for (int j = 0; j < fd.N; j++)
        {
            ref Group e = ref fd.G[j];
            if (!e.Alive || e.Posture == P_RETREAT) continue;
            int s = Math.Clamp((int)(e.X / SECTOR_KM), 0, SECTORS - 1);
            def[s] += e.Type == 1 ? e.Units * 10f : e.Units;
        }

        var sb = new StringBuilder(320);
        sb.Append("<code>");
        sb.Append("سکتور  ");
        for (int s = 0; s < SECTORS; s++) sb.Append((s + 1) % 10).Append(' ');
        sb.Append('\n');

        sb.Append("رخنه   ");
        for (int s = 0; s < SECTORS; s++)
        {
            float f = Math.Clamp(adv[s] / WIN_DEPTH, 0f, 1f);
            char c = atk[s] < 40f ? '.' : f > 0.85f ? '#' : f > 0.60f ? '+' : f > 0.30f ? '=' : '-';
            sb.Append(c).Append(' ');
        }
        sb.Append('\n');

        sb.Append("دفاع   ");
        for (int s = 0; s < SECTORS; s++)
        {
            char c = def[s] < 40f ? '.' : def[s] > 900f ? 'X' : def[s] > 350f ? 'x' : 'o';
            sb.Append(c).Append(' ');
        }
        sb.Append("</code>\n");
        sb.Append("<i>#عبور کامل  +رخنه عمیق  =پیشروی  -تماس  .خالی | Xدفاع سنگین  xمتوسط  oسبک</i>");
        return sb.ToString();
    }

    // ─────────── خط زمانی نبرد ───────────
    static string? Timeline(BattleLog log, byte side, int max = 14)
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

    static string Esc(string? s) => s == null ? "" : s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    // ═════════════════════════ گزارش‌های زمینی ══════════════════════════════
    static void BuildGroundReports(BattleResult r, Country atk, Country def,
        Force? fa, Force? fd, Field field, BattleLog log, AirOutcome air,
        int aStrat, int aTac, int dStrat, int dTac,
        int aAirStrat, int aAirTac, int dAirStrat, int dAirTac,
        List<(string Model, long Count)> aTankList, List<(string Model, long Count)> dTankList,
        List<(string Model, long Count)> aFighterList, List<(string Model, long Count)> dFighterList,
        List<(string Model, long Count)> aBomberList,
        long aTanks, long aSold, long dTanks, long dSold,
        long aFight, long aBomb, long dFight, long dAA,
        float frac, float depth,
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
        string env = $"🗺 {MapName[field.MapType]} | 🌦 {WeatherName[field.Weather]} | 🕓 {TimeName[field.StartTime]} | 🏞 {TerName[terr]}";


        string? armorMatch = ArmorMatchupLines(fa, fd);
        string? aModels = ModelLossLines(fa);
        string? dModels = ModelLossLines(fd);
        string? factionText = FactionAnalysis(fa, fd);

        string why = r.AttackerWon
            ? "تمرکز به‌موقع قوا روی نازک‌ترین بخش خط و توسعه‌ی سریع رخنه، کار دفاع را تمام کرد."
            : r.AttackerFailed
            ? "آتش دفاعی سازمان‌یافته و زمین مساعد، حمله را پیش از شکل‌گیری رخنه خفه کرد."
            : "هیچ طرف نتوانست ضربه‌ی قاطع بزند؛ نبرد به فرسایش کشید و جبهه تقریباً سرجایش ماند.";

        string? intelText = fa != null && fd != null
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
        if (anyGround && defHasGround) sb.Append($"• زمین عملیات: {Esc(MapNote[field.MapType])}\n");
        if (aFight > 0 || aBomb > 0) sb.Append($"• هوایی: {Esc(aAirName)} / {Esc(aAirTacName)}\n");

        if (ShowFrontMap && anyGround && defHasGround)
        {
            string? fm = FrontMap(fa, fd, field, depth);
            if (fm != null) { sb.Append("\n<b>🗺 نمای جبهه</b>\n").Append(fm).Append('\n'); }
        }

        string? tlA = Timeline(log, 0);
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

        //  – ۳۶: سرنوشت ذخیره‌ها
        if (fa != null && anyGround && defHasGround)
        {
            int held1 = 0, held2 = 0, in1 = 0, in2 = 0;
            for (int i = 0; i < fa.N; i++)
            {
                if (fa.G[i].Role == 1) { if (fa.G[i].Deployed) in1++; else held1++; }
                else if (fa.G[i].Role == 2) { if (fa.G[i].Deployed) in2++; else held2++; }
            }
            if (in1 + in2 + held1 + held2 > 0)
            {
                sb.Append("\n<b>🎖 ذخیره‌ها</b>\n");
                sb.Append(fa.Cmd.ReserveIn
                    ? $"• ذخیره‌ی تاکتیکی در ساعت <code>{Clock(fa.Cmd.TacReserveTick)}</code> آزاد شد ({in1} یگان وارد خط شد)\n"
                    : "• ذخیره‌ی تاکتیکی هرگز لازم نشد و دست‌نخورده ماند\n");
                sb.Append(fa.Cmd.DeepReserveIn
                    ? $"• ذخیره‌ی عملیاتی در ساعت <code>{Clock(fa.Cmd.OpReserveTick)}</code> آزاد شد ({in2} یگان رسید" + (held2 > 0 ? $"، {held2} یگان هنوز در راه بود)" : ")") + "\n"
                    : "• ذخیره‌ی عملیاتی تا پایان نبرد دست‌نخورده ماند\n");
            }
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
        {
            sb.Append($"   ✈️ جنگنده: {Num(r.AttackerFightersLost)} از {Num(aFight)} | 🛩 بمب‌افکن: {Num(r.AttackerBombersLost)} از {Num(aBomb)}\n");
            if (r.AttackerCrewLost > 0) sb.Append($"   ⚰️ خدمه‌ی پرواز: {Num(r.AttackerCrewLost)} نفر\n");
        }

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

        //  – ۶۰: اگر عملیات کاملاً هوایی بود، گزارش اختصاصی خودش را می‌گیرد
        if (!anyGround)
        {
            var ab = new StringBuilder(1600);
            ab.Append($"🛫 <b>گزارش عملیات هوایی — {Esc(atk.Name)} علیه {Esc(def.Name)}</b>\n");
            ab.Append($"{outcome}\n");
            ab.Append($"🌦 {WeatherName[field.Weather]} | 🕓 {TimeName[field.StartTime]}");
            if (field.CloudBaseM < 9000f) ab.Append($" | ☁️ کف ابر {field.CloudBaseM:F0} متر");
            ab.Append('\n');
            ab.Append($"⏱ مدت ماموریت: {h} ساعت و {m} دقیقه\n");

            ab.Append("\n<b>🎯 طرح عملیات</b>\n");
            ab.Append($"• {Esc(aAirName)} / {Esc(aAirTacName)}\n");
            ab.Append($"• ارتفاع ورود سازند: {air.EscortAltM:F0} متر\n");
            if (dFight > 0) ab.Append($"• گشت مدافع در {air.CapAltM:F0} متری\n");
            ab.Append($"• دفاع دشمن: {Esc(dAirName)} / {Esc(dAirTacName)}\n");

            string? tlAir = Timeline(log, 0);
            if (tlAir != null) ab.Append("\n<b>📜 روند ماموریت</b>\n").Append(tlAir).Append('\n');

            ab.Append("\n<b>💀 تلفات شما</b>\n");
            ab.Append($"   ✈️ جنگنده: {Num(r.AttackerFightersLost)} از {Num(aFight)}\n");
            ab.Append($"   🛩 بمب‌افکن: {Num(r.AttackerBombersLost)} از {Num(aBomb)}\n");
            if (r.AttackerCrewLost > 0) ab.Append($"   ⚰️ خدمه‌ی پرواز: {Num(r.AttackerCrewLost)} نفر\n");

            ab.Append("\n<b>💀 تلفات دشمن</b>\n");
            ab.Append($"   ✈️ جنگنده: {Num(r.DefenderFightersLost)} از {Num(dFight)}\n");
            ab.Append($"   🎯 پدافند: {Num(r.DefenderAntiAirLost)} از {Num(dAA)}\n");

            if (air.BombTonsOnTarget > 0.05f)
                ab.Append($"\n💣 تناژ روی هدف: {air.BombTonsOnTarget:F1} تن\n");
            ab.Append($"🛫 برتری هوایی: {AirSupText(air.Superiority)}\n");
            if (air.StratMoney > 0 || air.StratIron > 0)
                ab.Append($"🏭 خسارت به اقتصاد دشمن: {K(air.StratMoney)} پول، {K(air.StratIron)} آهن\n");
            r.AttackerReport = ab.ToString();
        }

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

        string? tlD = Timeline(log, 1);
        if (tlD != null)
        {
            sb.Append("\n<b>📜 خط زمانی نبرد</b>\n");
            sb.Append(tlD).Append('\n');
        }

        if (anyGround && defHasGround)
        {
            sb.Append("\n<b>🛡 تقابل زرهی</b>\n");
            string? armorMatchD = ArmorMatchupLines(fd, fa);
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

    // ══════════════ فیزیک دریایی: زاویه، نفوذ، منطقه‌ی مصونیت ═══════════════

    // نفوذ گلوله‌ی توپ دریایی (فرمول تجربی De Marre ساده‌شده).
    //  زره کمربند در برخورد افقی و زره عرشه در برخورد شیب‌دار مهم است.
    static float NavalPenetration(float shellKg, float velocityMs, float caliberMm)
    {
        float d = caliberMm / 25.4f;                       // اینچ
        float w = shellKg * 2.20462f;                      // پوند
        float v = velocityMs * 3.28084f;                   // فوت بر ثانیه
        // ضریب واسنجی‌شده با جدول‌های زره واقعی:
        //  Iowa 406mm در ۲۰km ≈ ۴۴۰mm، در ۳۰km ≈ ۳۲۰mm؛ Bismarck 380mm در ۲۰km ≈ ۳۹۰mm
        float pen = 0.00047f * MathF.Pow(w, 0.55f) * MathF.Pow(v, 1.1f) / MathF.Pow(MathF.Max(1f, d), 0.65f);
        return pen * 25.4f;                                 // به میلی‌متر
    }

    // سرعت گلوله در برد معین — افت با فاصله
    static float NavalVelocityAt(float muzzleMs, float rangeKm)
        => muzzleMs * MathF.Exp(-0.029f * rangeKm);

    // زاویه‌ی سقوط گلوله: در برد کم تقریباً افقی، در برد زیاد شیب‌دار.
    //  این تعیین می‌کند که به کمربند بخورد یا به عرشه.
    static float FallAngleDeg(float rangeKm, float muzzleMs)
    {
        // واسنجی: در ۲۰km حدود ۱۴°، در ۳۰km حدود ۲۹° — بالای ۲۶° گلوله به عرشه می‌خورد
        float refMv = 780f / MathF.Max(300f, muzzleMs);
        return Math.Clamp(2.0f + 0.030f * rangeKm * rangeKm * refMv, 2f, 70f);
    }

    // ═══════════ زاویه‌ی رخ کشتی — قلب نبرد دریایی ═══════════
    //  angleOnBow = زاویه‌ی بین محور طولی هدف و خط دید تیرانداز.
    //    ۹۰ درجه  = پهلوی کامل (broadside): بیشترین سطح هدف، همه‌ی توپ‌ها شلیک می‌کنند
    //     ۰ درجه  = سینه یا پاشنه: کمترین سطح، نصف توپ‌ها، ولی زره مؤثر بیشتر
    static float AngleOnBowDeg(float shooterX, float shooterY, float tgtX, float tgtY, float tgtHeadingRad)
    {
        float bx = shooterX - tgtX, by = shooterY - tgtY;
        float b = MathF.Atan2(by, bx);
        float rel = b - tgtHeadingRad;
        while (rel > MathF.PI) rel -= 2f * MathF.PI;
        while (rel < -MathF.PI) rel += 2f * MathF.PI;
        float deg = MathF.Abs(rel) * 180f / MathF.PI;
        if (deg > 90f) deg = 180f - deg;      // پاشنه هم مثل سینه باریک است
        return deg;                            // ۰..۹۰
    }

    static float WrapPi(float a)
    {
        while (a > MathF.PI) a -= 2f * MathF.PI;
        while (a < -MathF.PI) a += 2f * MathF.PI;
        return a;
    }

    // سطح مقطع هدف بر پایه‌ی زاویه: sin برای طول، cos برای عرض
    static float TargetProfile(float lengthM, float beamM, float angleDeg)
    {
        float r = angleDeg * MathF.PI / 180f;
        return lengthM * MathF.Sin(r) + beamM * MathF.Cos(r);
    }

    // چند درصد توپ‌های اصلی می‌توانند شلیک کنند؟ در سینه فقط برجک‌های جلو.
    static float GunsBearing(float angleDeg)
        => Math.Clamp(0.42f + 0.58f * MathF.Sin(angleDeg * MathF.PI / 180f), 0.42f, 1f);

    // زره مؤثر کمربند با زاویه: کج ایستادن ضخامت مؤثر را زیاد می‌کند.
    static float BeltEffective(float beltMm, float angleDeg, float fallDeg)
    {
        float obliq = MathF.Max(0.25f, MathF.Sin(angleDeg * MathF.PI / 180f));
        float vert = MathF.Max(0.35f, MathF.Cos(fallDeg * MathF.PI / 180f));
        return beltMm / (obliq * vert);
    }

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
        public float[] BSDamage = Array.Empty<float>();   // درصد آسیب هر ناو

        public long Boats => BoatCount.Sum();
        public long Subs => SubCount.Sum();
        public long BS => BSCount.Sum();
        public long BoatsLost => BoatLost.Sum();
        public long SubsLost => SubLost.Sum();
        public long BSLostTotal => BSLost.Sum();

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
        s.BSDamage = new float[w.Count];

        return s;
    }

    // ═════════════ سیستم آسیب کشتی (پیشنهاد ۱۳) ═════════════
    //  کشتی «یا سالم یا غرق» نیست. آسیب روی چهار زیرسیستم می‌نشیند و هرکدام
    //  اثر مکانیکی مشخص خودش را دارد. جای برخورد گلوله تعیین می‌کند کدام
    //  زیرسیستم آسیب ببیند.
    const byte DMG_HULL = 0;    // بدنه/شناوری → سرعت و بقا
    const byte DMG_ENGINE = 1;  // موتور → سرعت
    const byte DMG_GUNS = 2;    // برجک‌ها → توان آتش
    const byte DMG_FIRE = 3;    // آتش‌سوزی/کنترل آتش → دقت

    static readonly string[] DmgName = { "بدنه", "موتورخانه", "برجک‌ها", "سامانه‌ی کنترل آتش" };

    // ضریب سرعت باقی‌مانده بر پایه‌ی آسیب بدنه و موتور
    static float SpeedFactor(in Ship sh)
    {
        float loss = sh.DmgEngine * 0.55f + sh.DmgHull * 0.30f;
        return Math.Clamp(1f - loss, 0.22f, 1f);
    }

    // چند درصد توپ‌ها هنوز کار می‌کنند
    static float GunFactor(in Ship sh) => Math.Clamp(1f - sh.DmgGuns * 0.85f, 0.10f, 1f);

    // دقت باقی‌مانده: آتش‌سوزی و از کار افتادن فاصله‌یاب
    static float AccuracyFactor(in Ship sh) => Math.Clamp(1f - sh.DmgFire * 0.60f, 0.25f, 1f);

    // آیا کشتی باید از نبرد خارج شود؟ ناخدا با بدنه‌ی داغان عقب می‌کشد
    static bool ShouldWithdraw(in Ship sh) => sh.DmgHull > 0.72f || sh.Hp < 0.18f;

    // ───────────────── واحد شناور در میدان دریا ─────────────────
    struct Ship
    {
        public float DmgHull;      // ۰..۱ آسیب بدنه — نشت، لیست، کاهش شناوری
        public float DmgEngine;    // ۰..۱ آسیب موتورخانه
        public float DmgGuns;      // ۰..۱ برجک‌های از کار افتاده
        public float DmgFire;      // ۰..۱ آتش‌سوزی و آسیب کنترل آتش
        public float Flooding;     // نرخ نشت — هر تیک بدنه را بدتر می‌کند
        public bool  Withdrawing;  // در حال خروج از نبرد
        public float X, Y;          // کیلومتر
        public float Heading;       // رادیان
        public float Count;         // چند فروند در این دسته
        public float Count0;
        public float Hp;            // ۰..۱ سلامت دسته (برای نبردناو)
        public float Ammo;          // گلوله‌ی توپ اصلی
        public int   Torps;         // اژدر باقی‌مانده
        public float Depth;         // ۰ = سطح، ۱ = غواصی کامل
        public float Detect;        // چقدر دشمن او را دیده ۰..۱
        public byte  Kind;          // 0=قایق، 1=زیردریایی، 2=نبردناو
        public byte  Model;
        public byte  Side;          // 0=مهاجم، 1=مدافع
        public bool  Alive;
        public bool  Firing;
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

        // ══════════════ شبیه‌سازی زمان‌گام نبرد دریایی ══════════════
        //  میدان: ۶۰×۴۰ کیلومتر دریا. مدافع نزدیک ساحل (پایین)، مهاجم از بالا می‌آید.
        //  هر گام ۲ دقیقه. زاویه‌ی رخ کشتی‌ها هر گام تازه محاسبه می‌شود.
        const float SEA_W = 60f, SEA_H = 40f;
        const int   SEA_TICKS = 45;
        const float SEA_TICK_MIN = 2f;

        float stratAdv = NavalDoctrine(A, D, attStrategy, attTactic, defStrategy, defTactic, defender.PortLevel, log, ref rng);

        // ── هوا و دریا ──
        byte navWeather;
        float wr = rng.NextF();
        navWeather = wr < 0.42f ? W_CLEAR : wr < 0.64f ? W_CLOUD : wr < 0.82f ? W_RAIN : wr < 0.93f ? W_FOG : W_SNOW;
        var field = new Field { Weather = navWeather, StartTime = (byte)rng.Next(4) };
        // دریای متلاطم قایق سبک را از کار می‌اندازد
        float seaState = rng.Range(0f, 0.5f);
        if (navWeather is W_RAIN or W_SNOW) seaState = MathF.Min(1f, seaState + 0.40f);

        //  – ۱۷: شب دریایی. در ۱۹۳۹ رادار جست‌وجو نیست، پس شب یعنی کوری تقریبی.
        //     برد دید به چند کیلومتر می‌افتد و نبرد به فاصله‌ی نزدیک کشیده می‌شود.
        bool nightBattle = field.StartTime == TM_NIGHT;
        float lightFactor = nightBattle ? 0.34f : field.StartTime == TM_DUSK || field.StartTime == TM_DAWN ? 0.72f : 1f;

        //  – ۱۸: عمق آب. آب کم‌عمق یعنی زیردریایی نمی‌تواند فرار عمودی کند.
        float waterDepthM = rng.Range(45f, 320f);
        bool shallow = waterDepthM < 110f;

        log.Add(0, 2, LG_ENV,
            $"نبرد دریایی در هوای {WeatherName[navWeather]} و {TimeName[field.StartTime]} درگرفت؛ " +
            $"دریا {(seaState > 0.6f ? "متلاطم" : seaState > 0.3f ? "نیمه‌موّاج" : "آرام")} و عمق آب حدود {waterDepthM:F0} متر بود.");
        if (nightBattle)
            log.Add(0, 2, LG_ENV, "در تاریکی شب و بدون رادار جست‌وجو، ناوها تا فاصله‌ی نزدیک همدیگر را نمی‌دیدند.");
        if (shallow)
            log.Add(0, 2, LG_ENV, "آب کم‌عمق بود؛ زیردریایی‌ها نمی‌توانستند به عمق امن بروند.");

        var ships = new List<Ship>();

        void AddGroup(NavalSide side, byte sideId, byte kind, int modelIdx, long total)
        {
            if (total <= 0) return;
            int per = kind == 2 ? 1 : (kind == 1 ? 2 : 6);
            long leftN = total;
            while (leftN > 0)
            {
                float n = Math.Min(leftN, per);
                var sh = new Ship
                {
                    Count = n, Count0 = n, Hp = 1f, Kind = kind, Model = (byte)modelIdx,
                    Side = sideId, Alive = true, Depth = 0f
                };
                if (sideId == 0)
                {
                    sh.X = rng.Range(6f, SEA_W - 6f);
                    sh.Y = SEA_H - rng.Range(1f, 5f);
                    sh.Heading = -MathF.PI / 2f;             // رو به ساحل
                }
                else
                {
                    sh.X = rng.Range(4f, SEA_W - 4f);
                    sh.Y = rng.Range(1.5f, 9f);              // نزدیک ساحل
                    sh.Heading = MathF.PI / 2f;
                }
                if (kind == 2)
                {
                    var sp = side.BSSpecs[modelIdx];
                    sh.Ammo = sp.MainGuns * 110f;            // ذخیره‌ی معمول هر لوله
                }
                else if (kind == 1)
                {
                    var sp = side.SubSpecs[modelIdx];
                    sh.Torps = (int)(sp.TorpLoad * n);
                    sh.Depth = 1f;                            // زیردریایی غواصی‌شده وارد می‌شود
                }
                else
                {
                    var sp = side.BoatSpecs[modelIdx];
                    sh.Torps = (int)(sp.TorpTubes * n);
                }
                ships.Add(sh);
                leftN -= (long)n;
            }
        }

        for (int i = 0; i < A.BSCount.Length; i++) AddGroup(A, 0, 2, i, A.BSCount[i]);
        for (int i = 0; i < A.SubCount.Length; i++) AddGroup(A, 0, 1, i, A.SubCount[i]);
        for (int i = 0; i < A.BoatCount.Length; i++) AddGroup(A, 0, 0, i, A.BoatCount[i]);
        for (int i = 0; i < D.BSCount.Length; i++) AddGroup(D, 1, 2, i, D.BSCount[i]);
        for (int i = 0; i < D.SubCount.Length; i++) AddGroup(D, 1, 1, i, D.SubCount[i]);
        for (int i = 0; i < D.BoatCount.Length; i++) AddGroup(D, 1, 0, i, D.BoatCount[i]);

        NavalSide SideOf(byte id) => id == 0 ? A : D;

        // توان پدافند ناوگان (برای گزارش و دفاع در برابر حمله‌ی هوایی آینده)
        float fleetAaA = 0f, fleetAaD = 0f;
        for (int i = 0; i < A.BSCount.Length; i++) fleetAaA += A.BSCount[i] * A.BSSpecs[i].AaKgMin;
        for (int i = 0; i < D.BSCount.Length; i++) fleetAaD += D.BSCount[i] * D.BSSpecs[i].AaKgMin;

        // تلفات انباشته به تفکیک مدل
        var boatKill = new float[2][]; boatKill[0] = new float[A.BoatCount.Length]; boatKill[1] = new float[D.BoatCount.Length];
        var subKill  = new float[2][]; subKill[0]  = new float[A.SubCount.Length];  subKill[1]  = new float[D.SubCount.Length];
        var bsKill   = new float[2][]; bsKill[0]   = new float[A.BSCount.Length];   bsKill[1]   = new float[D.BSCount.Length];
        var bsDmg    = new float[2][]; bsDmg[0]    = new float[A.BSCount.Length];   bsDmg[1]    = new float[D.BSCount.Length];

        float shoreProgress = 0f;      // پیشرفت به سمت ساحل ۰..۱
        var fleetWithdraw = new bool[2];   // ۲۰: آیا ناوگان هر طرف دستور عقب‌نشینی گرفته
        bool loggedFirstBlood = false, loggedTorp = false, loggedCross = false, loggedBrace = false;
        float closestRange = 99f;

        for (int t = 0; t < SEA_TICKS; t++)
        {
            // ── ۱) حرکت و مانور ──
            for (int i = 0; i < ships.Count; i++)
            {
                var sh = ships[i];
                if (!sh.Alive) continue;
                var side = SideOf(sh.Side);

                float knots = sh.Kind == 2 ? side.BSSpecs[sh.Model].SpeedKn
                            : sh.Kind == 1 ? (sh.Depth > 0.5f ? side.SubSpecs[sh.Model].SubKn : side.SubSpecs[sh.Model].SurfKn)
                            : side.BoatSpecs[sh.Model].SpeedKn;

                // دریای متلاطم قایق سبک را کند می‌کند
                if (sh.Kind == 0)
                {
                    float keep = side.BoatSpecs[sh.Model].SeaKeeping;
                    knots *= Math.Clamp(1f - seaState * (1f - keep) * 1.1f, 0.25f, 1f);
                }
                // آسیب موتورخانه و بدنه سرعت را می‌خورد (پیشنهاد ۱۳)
                knots *= SpeedFactor(sh);
                float kmPerTick = knots * 1.852f * (SEA_TICK_MIN / 60f);

                // نزدیک‌ترین دشمن را پیدا کن
                int tgt = -1; float bestD = 999f;
                for (int j = 0; j < ships.Count; j++)
                {
                    if (!ships[j].Alive || ships[j].Side == sh.Side) continue;
                    float dd = MathF.Sqrt((ships[j].X - sh.X) * (ships[j].X - sh.X) + (ships[j].Y - sh.Y) * (ships[j].Y - sh.Y));
                    if (dd < bestD) { bestD = dd; tgt = j; }
                }
                if (tgt < 0) { ships[i] = sh; continue; }
                if (bestD < closestRange) closestRange = bestD;

                var en = ships[tgt];
                float desired;

                // ناوِ به‌شدت آسیب‌دیده از نبرد خارج می‌شود (پیشنهاد ۱۳ و ۲۰)
                if (ShouldWithdraw(sh))
                {
                    if (!sh.Withdrawing)
                    {
                        sh.Withdrawing = true;
                        if (sh.Kind == 2)
                            log.Add(t * 2, (byte)sh.Side, LG_CRISIS,
                                $"{side.BSSpecs[sh.Model].Name} با {sh.DmgHull * 100f:F0}٪ آسیب بدنه از خط خارج شد و به سمت بندر برگشت.");
                    }
                    float away = MathF.Atan2(sh.Y - en.Y, sh.X - en.X);
                    float mt = sh.Kind == 2 ? 0.16f : 0.40f;
                    sh.Heading += Math.Clamp(WrapPi(away - sh.Heading), -mt, mt);
                    float esc = knots * 1.852f * (SEA_TICK_MIN / 60f);
                    sh.X = Math.Clamp(sh.X + MathF.Cos(sh.Heading) * esc, 0.5f, SEA_W - 0.5f);
                    sh.Y = Math.Clamp(sh.Y + MathF.Sin(sh.Heading) * esc, 0.5f, SEA_H - 0.5f);
                    ships[i] = sh;
                    continue;
                }
                if (sh.Kind == 2)
                {
                    // نبردناو: در برد بهینه پهلو می‌دهد تا همه‌ی برجک‌ها شلیک کنند («کراسینگ»).
                    //  ولی اگر خیلی نزدیک شد باید فاصله بگیرد، وگرنه در حلقه گیر می‌کند و
                    //  رخِ خودش را به دشمن می‌دهد. جهت چرخش هم به سمتی است که کمترین
                    //  رخ را به دشمن نشان دهد — این همان مانور واقعی ناوبری است.
                    var sp = side.BSSpecs[sh.Model];
                    float optimal = sp.MainRangeKm * 0.55f;
                    float tooClose = sp.MainRangeKm * 0.28f;
                    float toEnemy = MathF.Atan2(en.Y - sh.Y, en.X - sh.X);
                    if (bestD > optimal) desired = toEnemy;                       // نزدیک شو
                    else if (bestD < tooClose) desired = toEnemy + MathF.PI;      // فاصله بگیر
                    else
                    {
                        // پهلو بده، ولی به سمتی بچرخ که چرخش کمتری لازم دارد
                        float optA = toEnemy + MathF.PI / 2f;
                        float optB = toEnemy - MathF.PI / 2f;
                        float dA = MathF.Abs(WrapPi(optA - sh.Heading));
                        float dB = MathF.Abs(WrapPi(optB - sh.Heading));
                        desired = dA <= dB ? optA : optB;
                    }
                }
                else if (sh.Kind == 1)
                {
                    // زیردریایی: آرام به موقعیت شلیک اژدر می‌رود
                    var sp = side.SubSpecs[sh.Model];
                    float toEnemy = MathF.Atan2(en.Y - sh.Y, en.X - sh.X);
                    desired = bestD > sp.TorpRangeKm * 0.7f ? toEnemy : toEnemy + MathF.PI / 3f;
                }
                else
                {
                    // قایق: حمله‌ی مستقیم و سریع، بعد از شلیک اژدر فرار
                    float toEnemy = MathF.Atan2(en.Y - sh.Y, en.X - sh.X);
                    desired = sh.Torps > 0 ? toEnemy : toEnemy + MathF.PI;
                }

                // نرخ چرخش واقعی: کشتی بزرگ کند می‌چرخد
                float maxTurn = sh.Kind == 2 ? 0.16f : sh.Kind == 1 ? 0.22f : 0.40f;
                float turn = Math.Clamp(WrapPi(desired - sh.Heading), -maxTurn, maxTurn);
                sh.Heading += turn;
                sh.X = Math.Clamp(sh.X + MathF.Cos(sh.Heading) * kmPerTick, 0.5f, SEA_W - 0.5f);
                sh.Y = Math.Clamp(sh.Y + MathF.Sin(sh.Heading) * kmPerTick, 0.5f, SEA_H - 0.5f);
                ships[i] = sh;
            }

            // ── ۲) شناسایی ──
            for (int i = 0; i < ships.Count; i++)
            {
                var sh = ships[i];
                if (!sh.Alive) continue;
                float best = 0f;
                for (int j = 0; j < ships.Count; j++)
                {
                    if (!ships[j].Alive || ships[j].Side == sh.Side) continue;
                    var o = ships[j];
                    float dd = MathF.Sqrt((o.X - sh.X) * (o.X - sh.X) + (o.Y - sh.Y) * (o.Y - sh.Y));
                    float horizon = sh.Kind == 2 ? 28f : sh.Kind == 1 ? (sh.Depth > 0.5f ? 8f : 14f) : 12f;
                    horizon *= lightFactor;                       // ۱۷: شب دید را می‌برد
                    // رادار کنترل آتش دید را بسیار زیاد می‌کند
                    // ۱۹۳۹: دید دریایی چشمی و اپتیکی است — رادار جست‌وجو هنوز فراگیر نیست
                    if (sh.Kind == 2)
                    {
                        horizon *= 0.80f + SideOf(sh.Side).BSSpecs[sh.Model].FireControl * 0.35f;
                        // ۱۶: هواپیمای شناسایی روی ناو — فقط روز و هوای باز، اثر سبک
                        if (!nightBattle && WxVision[field.Weather] > 0.75f) horizon *= 1.18f;
                    }
                    float vis = Math.Clamp(1f - dd / horizon, 0f, 1f) * WxVision[field.Weather];
                    // زیردریایی غواصی‌شده تقریباً نامرئی است
                    if (o.Kind == 1 && o.Depth > 0.5f)
                        vis *= 0.12f * (0.6f + SideOf(o.Side).SubSpecs[o.Model].NoiseLevel);
                    if (vis > best) best = vis;
                }
                sh.Detect = best;
                ships[i] = sh;
            }

            // ── ۳) آتش توپخانه‌ی سنگین ──
            for (int i = 0; i < ships.Count; i++)
            {
                var sh = ships[i];
                if (!sh.Alive || sh.Kind != 2 || sh.Ammo <= 0f || sh.Withdrawing) continue;
                var side = SideOf(sh.Side);
                var sp = side.BSSpecs[sh.Model];

                int tgt = -1; float bestScore = 0f, tgtRange = 0f;
                for (int j = 0; j < ships.Count; j++)
                {
                    var o = ships[j];
                    if (!o.Alive || o.Side == sh.Side) continue;
                    if (o.Kind == 1 && o.Depth > 0.5f) continue;      // زیر آب را با توپ نمی‌زنند
                    float dd = MathF.Sqrt((o.X - sh.X) * (o.X - sh.X) + (o.Y - sh.Y) * (o.Y - sh.Y));
                    if (dd > sp.MainRangeKm) continue;
                    if (o.Detect < 0.12f) continue;
                    float pri = o.Kind == 2 ? 3f : o.Kind == 0 ? 0.8f : 1.2f;
                    float sc = pri / (0.5f + dd * 0.08f);
                    if (sc > bestScore) { bestScore = sc; tgt = j; tgtRange = dd; }
                }
                // توپ‌های فرعی جداگانه روی شناور سبکِ نزدیک کار می‌کنند
                if (sp.SecGuns > 0)
                {
                    for (int j = 0; j < ships.Count; j++)
                    {
                        var o = ships[j];
                        if (!o.Alive || o.Side == sh.Side || o.Kind == 2) continue;
                        if (o.Kind == 1 && o.Depth > 0.5f) continue;
                        float dd = MathF.Sqrt((o.X - sh.X) * (o.X - sh.X) + (o.Y - sh.Y) * (o.Y - sh.Y));
                        if (dd > sp.SecMm / 18f) continue;          // برد مؤثر توپ فرعی
                        float sec = sh.Count * sp.SecGuns * (sp.SecMm / 130f) * 0.020f * SEA_TICK_MIN
                                    * sp.FireControl * rng.Range(0.6f, 1.4f);
                        sec = MathF.Min(sec, o.Count);
                        o.Count -= sec;
                        if (o.Kind == 0) boatKill[o.Side][o.Model] += sec; else subKill[o.Side][o.Model] += sec;
                        if (o.Count < 0.5f) o.Alive = false;
                        ships[j] = o;
                        break;
                    }
                }

                if (tgt < 0) continue;

                var en2 = ships[tgt];
                var eSide = SideOf(en2.Side);
                sh.Firing = true;

                // زاویه‌ی رخِ هدف — تعیین‌کننده‌ی همه‌چیز
                float aob = AngleOnBowDeg(sh.X, sh.Y, en2.X, en2.Y, en2.Heading);
                float myAob = AngleOnBowDeg(en2.X, en2.Y, sh.X, sh.Y, sh.Heading);

                float enLen, enBeam;
                if (en2.Kind == 2) { enLen = eSide.BSSpecs[en2.Model].LengthM; enBeam = eSide.BSSpecs[en2.Model].BeamM; }
                else if (en2.Kind == 0) { enLen = eSide.BoatSpecs[en2.Model].LengthM; enBeam = eSide.BoatSpecs[en2.Model].BeamM; }
                else { enLen = 67f; enBeam = 6.2f; }

                float profile = TargetProfile(enLen, enBeam, aob);
                float bearing = GunsBearing(myAob);              // چند درصد توپ‌های من شلیک می‌کنند

                float shots = sp.MainGuns * bearing * sp.MainRpm * SEA_TICK_MIN * GunFactor(sh);
                shots = MathF.Min(shots, sh.Ammo);
                sh.Ammo -= shots;

                // احتمال اصابت: کنترل آتش، فاصله، سطح هدف، دریا
                float fc = sp.FireControl * side.Prof.CrewQuality * side.Familiar(sp.Origin) * AccuracyFactor(sh);
                float hitP = Math.Clamp(0.34f * fc * (profile / 200f) / (1f + tgtRange / 11f)
                                        * WxVision[field.Weather] * (1f - seaState * 0.28f), 0.002f, 0.42f);
                float hits = shots * hitP * sh.Count;
                if (hits <= 0f) { ships[i] = sh; continue; }

                float vel = NavalVelocityAt(sp.MainMuzzleMs, tgtRange);
                float pen = NavalPenetration(sp.MainShellKg, vel, sp.MainMm);
                float fall = FallAngleDeg(tgtRange, sp.MainMuzzleMs);

                float dmg;
                bool deckHitFlag = fall > 26f;
                if (en2.Kind == 2)
                {
                    var es = eSide.BSSpecs[en2.Model];
                    // در برد کم گلوله به کمربند، در برد زیاد به عرشه می‌خورد
                    bool deckHit = deckHitFlag;
                    float armor = deckHit
                        ? es.DeckMm / MathF.Max(0.30f, MathF.Sin(fall * MathF.PI / 180f))
                        : BeltEffective(es.BeltMm, aob, fall);
                    // گاهی گلوله به برجک یا برج فرماندهی می‌خورد — نقطه‌ی حیاتی
                    if (rng.NextF() < 0.14f)
                        armor = rng.NextF() < 0.6f ? es.TurretMm : es.ConningMm;
                    float ratio2 = pen / MathF.Max(1f, armor);
                    // منطقه‌ی مصونیت: اگر نفوذ کمتر از زره باشد، گلوله کمانه می‌کند
                    float through = Math.Clamp((ratio2 - 0.85f) / 0.5f, 0f, 1f);
                    dmg = hits * (0.0016f + through * 0.0125f);
                    if (!loggedBrace && through < 0.15f && hits > 3f)
                    {
                        loggedBrace = true;
                        log.Add(t * 2, 2, LG_COMBAT,
                            $"گلوله‌های {sp.MainMm:F0}mm در برد {tgtRange:F0} کیلومتری روی زره {(deckHit ? "عرشه" : "کمربند")} {es.Name} کمانه کردند — منطقه‌ی مصونیت.");
                    }
                }
                else
                {
                    // کشتی سبک: تنها محافظت، ورقه‌ی نازک ضدترکش است
                    float splinter = en2.Kind == 0 ? eSide.BoatSpecs[en2.Model].HullArmorMm : 12f;
                    dmg = hits * 0.16f / (1f + splinter / 22f);
                }

                // اعمال آسیب — روی زیرسیستم‌ها پخش می‌شود (پیشنهاد ۱۳)
                if (en2.Kind == 2)
                {
                    en2.Hp -= dmg;
                    bsDmg[en2.Side][en2.Model] += dmg * 100f;

                    // جای برخورد تعیین می‌کند چه چیزی خراب شود
                    float roll = rng.NextF();
                    if (deckHitFlag)
                    {
                        // گلوله از عرشه می‌آید: موتورخانه و انبار مهمات زیرش است
                        if (roll < 0.42f) en2.DmgEngine = Math.Min(1f, en2.DmgEngine + dmg * 2.6f);
                        else if (roll < 0.70f) en2.DmgFire = Math.Min(1f, en2.DmgFire + dmg * 3.0f);
                        else if (roll < 0.88f) en2.DmgGuns = Math.Min(1f, en2.DmgGuns + dmg * 2.2f);
                        else en2.DmgHull = Math.Min(1f, en2.DmgHull + dmg * 1.8f);
                    }
                    else
                    {
                        // برخورد به کمربند: بیشتر بدنه و برجک
                        if (roll < 0.46f) en2.DmgHull = Math.Min(1f, en2.DmgHull + dmg * 2.4f);
                        else if (roll < 0.72f) en2.DmgGuns = Math.Min(1f, en2.DmgGuns + dmg * 2.4f);
                        else if (roll < 0.90f) en2.DmgEngine = Math.Min(1f, en2.DmgEngine + dmg * 1.8f);
                        else en2.DmgFire = Math.Min(1f, en2.DmgFire + dmg * 2.0f);
                    }
                    // نفوذ زیر خط آب → نشت
                    if (!deckHitFlag && dmg > 0.004f) en2.Flooding += dmg * 0.30f;
                    if (en2.Hp <= 0f)
                    {
                        bsKill[en2.Side][en2.Model] += en2.Count;
                        en2.Count = 0f; en2.Alive = false;
                        log.Add(t * 2, (byte)(en2.Side == 0 ? 1 : 0), LG_BREAK,
                            $"{eSide.BSSpecs[en2.Model].Name} زیر آتش {sp.MainMm:F0}mm منفجر شد و به قعر رفت.");
                    }
                }
                else
                {
                    float killed = MathF.Min(en2.Count, dmg);
                    en2.Count -= killed;
                    if (en2.Kind == 0) boatKill[en2.Side][en2.Model] += killed;
                    else subKill[en2.Side][en2.Model] += killed;
                    if (en2.Count < 0.5f) { en2.Alive = false; }
                }

                if (!loggedFirstBlood && hits > 1f)
                {
                    loggedFirstBlood = true;
                    log.Add(t * 2, 2, LG_COMBAT,
                        $"نخستین سالوی مؤثر در برد {tgtRange:F0} کیلومتری؛ هدف با زاویه‌ی رخ {aob:F0} درجه در تیررس بود.");
                }
                if (!loggedCross && myAob > 72f && aob > 72f)
                {
                    loggedCross = true;
                    log.Add(t * 2, (byte)sh.Side, LG_DECISION,
                        "هر دو ناوگان پهلو به پهلو شدند و همه‌ی برجک‌ها وارد آتش شدند.");
                }
                ships[tgt] = en2; ships[i] = sh;
            }

            // ── ۳.۵) آتش سبک قایق‌ها و توپ عرشه‌ی زیردریایی ──
            for (int i = 0; i < ships.Count; i++)
            {
                var sh = ships[i];
                if (!sh.Alive || sh.Kind == 2) continue;
                var side = SideOf(sh.Side);
                float gunMm, gunKg;
                if (sh.Kind == 0) { gunMm = side.BoatSpecs[sh.Model].GunMm; gunKg = side.BoatSpecs[sh.Model].GunKgMin; }
                else { if (sh.Depth > 0.5f) continue; gunMm = side.SubSpecs[sh.Model].DeckGunMm; gunKg = gunMm * 0.35f; }
                if (gunMm < 10f) continue;

                for (int j = 0; j < ships.Count; j++)
                {
                    var o = ships[j];
                    if (!o.Alive || o.Side == sh.Side || o.Kind == 2) continue;
                    if (o.Kind == 1 && o.Depth > 0.5f) continue;
                    float dd = MathF.Sqrt((o.X - sh.X) * (o.X - sh.X) + (o.Y - sh.Y) * (o.Y - sh.Y));
                    if (dd > 3.2f) continue;
                    float splinter = o.Kind == 0 ? SideOf(o.Side).BoatSpecs[o.Model].HullArmorMm : 14f;
                    float k = sh.Count * gunKg * 0.0016f * SEA_TICK_MIN / (1f + splinter / 20f);
                    k = MathF.Min(k, o.Count);
                    o.Count -= k;
                    if (o.Kind == 0) boatKill[o.Side][o.Model] += k; else subKill[o.Side][o.Model] += k;
                    if (o.Count < 0.5f) o.Alive = false;
                    ships[j] = o;
                    break;
                }
            }

            // ── ۴) حمله‌ی اژدر (زیردریایی و قایق) ──
            for (int i = 0; i < ships.Count; i++)
            {
                var sh = ships[i];
                if (!sh.Alive || sh.Kind == 2 || sh.Torps <= 0 || sh.Withdrawing) continue;
                var side = SideOf(sh.Side);
                float trng = sh.Kind == 1 ? side.SubSpecs[sh.Model].TorpRangeKm : side.BoatSpecs[sh.Model].TorpRangeKm;
                float twh  = sh.Kind == 1 ? side.SubSpecs[sh.Model].TorpWarheadKg : side.BoatSpecs[sh.Model].TorpWarheadKg;

                int tgt = -1; float bestV = 0f; float tr = 0f;
                for (int j = 0; j < ships.Count; j++)
                {
                    var o = ships[j];
                    if (!o.Alive || o.Side == sh.Side) continue;
                    float dd = MathF.Sqrt((o.X - sh.X) * (o.X - sh.X) + (o.Y - sh.Y) * (o.Y - sh.Y));
                    if (dd > trng) continue;
                    float v = (o.Kind == 2 ? 4f : 1f) / (0.4f + dd);
                    if (v > bestV) { bestV = v; tgt = j; tr = dd; }
                }
                if (tgt < 0) continue;

                var en3 = ships[tgt];
                var eS = SideOf(en3.Side);
                // زاویه‌ی رخ هدف: اژدر به پهلو بیشترین شانس اصابت را دارد
                float aob2 = AngleOnBowDeg(sh.X, sh.Y, en3.X, en3.Y, en3.Heading);
                float aspect = MathF.Sin(aob2 * MathF.PI / 180f);      // ۱ = پهلوی کامل

                int fired = Math.Min(sh.Torps, sh.Kind == 1 ? 4 : 2);
                sh.Torps -= fired;

                float pHit = Math.Clamp(0.32f * aspect / (1f + tr / 4f), 0.01f, 0.60f);
                // کشتی هدف اگر دیده باشد، مانور فرار می‌کند
                if (en3.Detect > 0.4f) pHit *= 0.55f;
                float hitsT = fired * pHit * sh.Count;

                if (hitsT > 0.15f)
                {
                    if (en3.Kind == 2)
                    {
                        var es = eS.BSSpecs[en3.Model];
                        // اژدر زیر خط آب می‌خورد — زره کمربند اثر کمی دارد
                        float tpd = twh / MathF.Max(400f, es.DisplacementT / 55f);
                        en3.Hp -= hitsT * tpd * 0.55f;
                        bsDmg[en3.Side][en3.Model] += hitsT * tpd * 55f;
                        // اژدر زیر خط آب می‌خورد: نشت شدید و آسیب موتورخانه، نه برجک
                        en3.DmgHull = Math.Min(1f, en3.DmgHull + hitsT * tpd * 1.6f);
                        en3.Flooding += hitsT * tpd * 0.9f;
                        if (rng.NextF() < 0.45f)
                            en3.DmgEngine = Math.Min(1f, en3.DmgEngine + hitsT * tpd * 1.1f);
                        if (en3.Hp <= 0f)
                        {
                            bsKill[en3.Side][en3.Model] += en3.Count;
                            en3.Count = 0f; en3.Alive = false;
                            log.Add(t * 2, (byte)(en3.Side == 0 ? 1 : 0), LG_BREAK,
                                $"اژدر زیر خط آب {es.Name} را شکافت و ناو غرق شد.");
                        }
                        else if (!loggedTorp)
                        {
                            loggedTorp = true;
                            log.Add(t * 2, (byte)sh.Side, LG_COMBAT,
                                $"اژدرها از زاویه‌ی {aob2:F0} درجه شلیک شدند و {es.Name} را زیر خط آب زدند.");
                        }
                    }
                    else
                    {
                        float killed = MathF.Min(en3.Count, hitsT * 1.4f);
                        en3.Count -= killed;
                        if (en3.Kind == 0) boatKill[en3.Side][en3.Model] += killed;
                        else subKill[en3.Side][en3.Model] += killed;
                        if (en3.Count < 0.5f) en3.Alive = false;
                    }
                    ships[tgt] = en3;
                }

                // زیردریایی بعد از شلیک غواصی می‌کند — تا وقتی باتری اجازه بدهد
                if (sh.Kind == 1)
                {
                    float batteryMin = side.SubSpecs[sh.Model].SubEnduranceH * 60f;
                    float submergedMin = t * SEA_TICK_MIN;
                    sh.Depth = submergedMin < batteryMin ? 1f : 0.25f;   // باتری تمام → مجبور به بالا آمدن
                }
                ships[i] = sh;
            }

            // ── ۴.۵) پیشرفت نشت و آتش‌سوزی + مهار خسارت ──
            //  خدمه‌ی بهتر سریع‌تر مهار می‌کند؛ اگر نتواند، نشت بدنه را می‌خورد
            //  تا کشتی واژگون شود. این همان مرگ تدریجی واقعی ناوهاست.
            for (int i = 0; i < ships.Count; i++)
            {
                var sh = ships[i];
                if (!sh.Alive || sh.Kind != 2) continue;
                var sd2 = SideOf(sh.Side);

                if (sh.Flooding > 0.0001f)
                {
                    sh.DmgHull = Math.Min(1f, sh.DmgHull + sh.Flooding * 0.16f);
                    sh.Hp -= sh.Flooding * 0.055f;
                    // مهار خسارت: کیفیت خدمه و بازیابی فکشن
                    float control = 0.10f + sd2.Prof.CrewQuality * 0.10f + sd2.Prof.Recovery * 0.14f;
                    sh.Flooding = MathF.Max(0f, sh.Flooding * (1f - control));
                }
                if (sh.DmgFire > 0.02f)
                {
                    // آتش‌سوزی خودش را تغذیه می‌کند تا مهار شود
                    float fight = 0.12f + sd2.Prof.CrewQuality * 0.12f;
                    sh.DmgFire = MathF.Max(0f, sh.DmgFire - fight * 0.5f);
                    sh.Hp -= sh.DmgFire * 0.010f;
                }
                if (sh.Hp <= 0f && sh.Alive)
                {
                    bsKill[sh.Side][sh.Model] += sh.Count;
                    sh.Count = 0f; sh.Alive = false;
                    log.Add(t * 2, (byte)(sh.Side == 0 ? 1 : 0), LG_BREAK,
                        $"{sd2.BSSpecs[sh.Model].Name} پس از نشت مهارنشده واژگون شد.");
                }
                ships[i] = sh;
            }

            // ── ۵) ضدزیردریایی: کشتی سطحی زیردریایی کشف‌شده را می‌کوبد ──
            for (int i = 0; i < ships.Count; i++)
            {
                var sub = ships[i];
                if (!sub.Alive || sub.Kind != 1) continue;
                var sSide = SideOf(sub.Side);
                var ssp = sSide.SubSpecs[sub.Model];
                float exposure = (1f - sub.Depth) + ssp.NoiseLevel * 0.5f;
                if (exposure < 0.25f) continue;

                float hunters = 0f;
                for (int j = 0; j < ships.Count; j++)
                {
                    var o = ships[j];
                    if (!o.Alive || o.Side == sub.Side || o.Kind == 1) continue;
                    float dd = MathF.Sqrt((o.X - sub.X) * (o.X - sub.X) + (o.Y - sub.Y) * (o.Y - sub.Y));
                    if (dd < 4.5f) hunters += o.Count * (o.Kind == 0 ? 1.3f : 0.7f);
                }
                if (hunters <= 0f) continue;
                // عمق غواصی زیاد و غواصی سریع، شانس بقا را بالا می‌برد
                // ۱۸: در آب کم‌عمق، عمق مجاز زیردریایی بی‌فایده است
                float usableDepth = shallow ? MathF.Min(ssp.TestDepthM, waterDepthM * 0.6f) : ssp.TestDepthM;
                float depthEdge = 1f / (1f + usableDepth / 150f);
                float dive = 1f / (1f + ssp.DiveSec / 40f);
                float loss = hunters * 0.010f * exposure * depthEdge * dive * SEA_TICK_MIN * rng.Range(0.5f, 1.5f);
                loss = MathF.Min(loss, sub.Count);
                sub.Count -= loss;
                subKill[sub.Side][sub.Model] += loss;
                if (sub.Count < 0.5f) sub.Alive = false;
                ships[i] = sub;
            }

            // ── ۶) توپخانه‌ی ساحلی و پیشروی به ساحل ──
            float atkNear = 0f, defAlive = 0f;
            foreach (var sh in ships)
            {
                if (!sh.Alive) continue;
                if (sh.Side == 0 && sh.Y < 12f) atkNear += sh.Count * (sh.Kind == 2 ? 6f : 1f);
                if (sh.Side == 1) defAlive += sh.Count * (sh.Kind == 2 ? 6f : 1f);
            }
            if (atkNear > 0f)
            {
                float coastal = defender.PortLevel * 1.6f;
                for (int i = 0; i < ships.Count; i++)
                {
                    var sh = ships[i];
                    if (!sh.Alive || sh.Side != 0 || sh.Y > 10f) continue;
                    float armorFactor = sh.Kind == 2 ? 0.12f : 1f;
                    float hurt = coastal * 0.0035f * armorFactor * SEA_TICK_MIN * rng.Range(0.6f, 1.4f);
                    if (sh.Kind == 2)
                    {
                        sh.Hp -= hurt * 0.05f;
                        bsDmg[0][sh.Model] += hurt * 5f;
                        if (sh.Hp <= 0f) { bsKill[0][sh.Model] += sh.Count; sh.Count = 0; sh.Alive = false; }
                    }
                    else
                    {
                        float k = MathF.Min(sh.Count, hurt);
                        sh.Count -= k;
                        if (sh.Kind == 0) boatKill[0][sh.Model] += k; else subKill[0][sh.Model] += k;
                        if (sh.Count < 0.5f) sh.Alive = false;
                    }
                    ships[i] = sh;
                }
                float push = atkNear / MathF.Max(1f, atkNear + defAlive + defender.PortLevel * 3f);
                shoreProgress = MathF.Min(1f, shoreProgress + push * 0.030f * stratAdv);
            }

            // ── ۲۰: تصمیم عقب‌نشینی ناوگان ──
            if (t > 8 && t % 4 == 0)
            {
                float pwA = 0f, pwD = 0f;
                foreach (var sh in ships)
                {
                    if (!sh.Alive) continue;
                    float w = sh.Count * (sh.Kind == 2 ? 6f : 1f) * Math.Clamp(sh.Hp, 0.1f, 1f);
                    if (sh.Side == 0) pwA += w; else pwD += w;
                }
                byte quitting = 255;
                if (pwA > 0f && pwD > 0f)
                {
                    if (pwA < pwD * 0.34f) quitting = 0;
                    else if (pwD < pwA * 0.34f) quitting = 1;
                }
                if (quitting != 255 && !fleetWithdraw[quitting])
                {
                    fleetWithdraw[quitting] = true;
                    log.Add(t * 2, quitting, LG_DECISION,
                        quitting == 0
                        ? "فرمانده‌ی ناوگان مهاجم دید که ادامه یعنی نابودی؛ دستور بازگشت داد."
                        : "فرمانده‌ی ناوگان مدافع ناوهای باقی‌مانده را از خط بیرون کشید تا حفظشان کند.");
                    for (int q = 0; q < ships.Count; q++)
                    {
                        var sq = ships[q];
                        if (sq.Alive && sq.Side == quitting) { sq.Withdrawing = true; ships[q] = sq; }
                    }
                }
            }

            bool anyA = ships.Any(x => x.Alive && x.Side == 0);
            bool anyD = ships.Any(x => x.Alive && x.Side == 1);
            if (!anyA || !anyD) break;
            if (fleetWithdraw[0] && ships.Where(x => x.Alive && x.Side == 0).All(x => x.Y > SEA_H - 6f)) break;
            if (fleetWithdraw[1] && ships.Where(x => x.Alive && x.Side == 1).All(x => x.Y < 5f)) break;
        }

        // ── نتیجه‌گیری از وضعیت واقعی میدان ──
        // ناوِ آسیب‌دیده نصفه می‌ارزد — سلامت در ارزش‌گذاری لحاظ می‌شود
        float aliveA = ships.Where(x => x.Alive && x.Side == 0)
                            .Sum(x => x.Count * (x.Kind == 2 ? 6f : 1f) * Math.Clamp(x.Hp, 0.15f, 1f));
        float aliveD = ships.Where(x => x.Alive && x.Side == 1)
                            .Sum(x => x.Count * (x.Kind == 2 ? 6f : 1f) * Math.Clamp(x.Hp, 0.15f, 1f));
        float startA = A.Boats + A.Subs + A.BS * 6f;
        float startD = D.Boats + D.Subs + D.BS * 6f + defender.PortLevel * 2f;

        float attrition = aliveA / MathF.Max(1f, aliveA + aliveD);
        float survivalA = aliveA / MathF.Max(1f, startA);

        int success = (int)Math.Clamp(
            (attStrategy == 2 ? shoreProgress * 70f + attrition * 30f      // آبی‌خاکی: رسیدن به ساحل مهم است
                              : (1f - aliveD / MathF.Max(1f, startD)) * 80f + attrition * 20f),
            0f, 100f);

        bool attackerWon = success >= 82 && survivalA > 0.30f;
        bool attackerFailed = success < 20 || survivalA < 0.22f;

        float ratio = startA / MathF.Max(1f, startD);
        float eff = attrition * 2f;

        // انتقال تلفات از شبیه‌سازی به ساختار خروجی
        for (int i = 0; i < A.BoatCount.Length; i++) A.BoatLost[i] = Math.Min(A.BoatCount[i], (long)MathF.Round(boatKill[0][i]));
        for (int i = 0; i < D.BoatCount.Length; i++) D.BoatLost[i] = Math.Min(D.BoatCount[i], (long)MathF.Round(boatKill[1][i]));
        for (int i = 0; i < A.SubCount.Length; i++)  A.SubLost[i]  = Math.Min(A.SubCount[i],  (long)MathF.Round(subKill[0][i]));
        for (int i = 0; i < D.SubCount.Length; i++)  D.SubLost[i]  = Math.Min(D.SubCount[i],  (long)MathF.Round(subKill[1][i]));
        for (int i = 0; i < A.BSCount.Length; i++)   A.BSLost[i]   = Math.Min(A.BSCount[i],   (long)MathF.Round(bsKill[0][i]));
        for (int i = 0; i < D.BSCount.Length; i++)   D.BSLost[i]   = Math.Min(D.BSCount[i],   (long)MathF.Round(bsKill[1][i]));

        // ملوانان از دست رفته بر پایه‌ی خدمه‌ی واقعی هر کلاس
        float sailorsA = 0f, sailorsD = 0f;
        for (int i = 0; i < A.BSCount.Length; i++) sailorsA += A.BSLost[i] * A.BSSpecs[i].Crew;
        for (int i = 0; i < A.SubCount.Length; i++) sailorsA += A.SubLost[i] * A.SubSpecs[i].Crew;
        for (int i = 0; i < A.BoatCount.Length; i++) sailorsA += A.BoatLost[i] * A.BoatSpecs[i].Crew;
        for (int i = 0; i < D.BSCount.Length; i++) sailorsD += D.BSLost[i] * D.BSSpecs[i].Crew;
        for (int i = 0; i < D.SubCount.Length; i++) sailorsD += D.SubLost[i] * D.SubSpecs[i].Crew;
        for (int i = 0; i < D.BoatCount.Length; i++) sailorsD += D.BoatLost[i] * D.BoatSpecs[i].Crew;
        res.AttackerCrewLost = (long)sailorsA;
        res.DefenderCrewLost = (long)sailorsD;
        if (fleetAaA + fleetAaD > 0f)
            log.Add(0, 2, LG_ENV, $"توان پدافند ناوگان‌ها: مهاجم {fleetAaA:F0} در برابر مدافع {fleetAaD:F0} کیلوگرم آتش در دقیقه.");

        long attBSDamage = (long)MathF.Round(bsDmg[0].Sum());
        long defBSDamage = (long)MathF.Round(bsDmg[1].Sum());
        for (int i = 0; i < A.BSDamage.Length; i++) A.BSDamage[i] = bsDmg[0][i];
        for (int i = 0; i < D.BSDamage.Length; i++) D.BSDamage[i] = bsDmg[1][i];

        bool oneSided = attrition > 0.85f || attrition < 0.15f;
        float frac = success / 100f;
        long lootMoney = Math.Min(defender.Money, (long)(defender.Money * 0.15 * frac * 1.5));
        long lootIron = Math.Min(defender.Iron, (long)(defender.Iron * 0.10 * frac * 1.5));

        if (closestRange < 90f)
            log.Add(0, 2, LG_ENV, $"نزدیک‌ترین برد درگیری {closestRange:F1} کیلومتر بود؛ دریا {(seaState > 0.6f ? "متلاطم" : seaState > 0.3f ? "نیمه‌موّاج" : "آرام")} بود.");

        //  – ۱۳: خلاصه‌ی وضعیت زیرسیستم ناوهای بازمانده‌ی مهاجم
        var shipStates = new List<string>();
        foreach (var sh in ships)
        {
            if (!sh.Alive || sh.Kind != 2 || sh.Side != 0) continue;
            var nm = A.BSSpecs[sh.Model].Name;
            var parts = new List<string>();
            if (sh.DmgHull > 0.10f) parts.Add($"بدنه {sh.DmgHull * 100f:F0}٪");
            if (sh.DmgEngine > 0.10f) parts.Add($"موتورخانه {sh.DmgEngine * 100f:F0}٪ (سرعت {SpeedFactor(sh) * 100f:F0}٪)");
            if (sh.DmgGuns > 0.10f) parts.Add($"برجک {sh.DmgGuns * 100f:F0}٪ (آتش {GunFactor(sh) * 100f:F0}٪)");
            if (sh.DmgFire > 0.05f) parts.Add("آتش‌سوزی");
            if (sh.Withdrawing) parts.Add("از خط خارج شد");
            shipStates.Add(parts.Count == 0 ? $"🚢 {nm}: سالم" : $"🚢 {nm}: {string.Join("، ", parts)}");
        }

        BuildNavalReports(res, attacker, defender, A, D, log, shipStates,
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
        //  – ۹: نبرد دریایی روی رفاه اثر می‌گذارد
        //     غرق شدن ناوگان و از دست دادن ملوان، روحیه‌ی ملی را می‌شکند؛
        //     شکست بندر و غارت هم به مدافع فشار می‌آورد.
        double aFleet0 = Math.Max(1, A.Boats + A.Subs + A.BS * 6);
        double dFleet0 = Math.Max(1, D.Boats + D.Subs + D.BS * 6);
        double aFleetLoss = (A.BoatsLost + A.SubsLost + A.BSLostTotal * 6) / aFleet0;
        double dFleetLoss = (D.BoatsLost + D.SubsLost + D.BSLostTotal * 6) / dFleet0;

        res.AttackerWelfareChange = -Math.Clamp(aFleetLoss * 2.2 + (attackerFailed ? 0.8 : 0)
                                    + res.AttackerCrewLost / 4000.0, 0, 3);
        res.DefenderWelfareChange = -Math.Clamp(dFleetLoss * 2.2 + (attackerWon ? 1.2 : 0)
                                    + frac * 0.6 + res.DefenderCrewLost / 4000.0, 0, 4);

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
            // زیردریایی ساکت‌تر و عمیق‌تر، کمین خطرناک‌تری می‌سازد
            adv -= D.SubSpecs.Length > 0 ? (1f - D.SubSpecs.Min(x => x.NoiseLevel)) * 0.06f : 0f;
            log.Add(1, 1, LG_PLAN, "زیردریایی‌های مدافع در تنگه‌های کم‌عمق کمین کردند.");
        }

        double ratio = (A.Subs + A.BS * 10.0) / Math.Max(1.0, D.Subs + D.BS * 10.0 + D.Boats * 0.3);
        adv += (float)Math.Clamp((ratio - 1.0) * 0.10, -0.15, 0.15);
        adv += rng.Range(-0.04f, 0.04f);

        log.Add(0, 0, LG_PLAN, note);
        return Math.Clamp(adv, 0.70f, 1.40f);
    }

    static string? NavalModelLines(NavalSide s, string indent = "   ")
    {
        var sb = new StringBuilder();
        for (int i = 0; i < s.BoatModels.Length; i++)
            if (s.BoatCount[i] > 0)
            {
                var sp = s.BoatSpecs[i];
                sb.Append($"{indent}🚤 {s.BoatModels[i]} ({sp.SpeedKn:F0} گره، {sp.TorpTubes} لوله اژدر): {Num(s.BoatLost[i])} از {Num(s.BoatCount[i])} غرق\n");
            }
        for (int i = 0; i < s.SubModels.Length; i++)
            if (s.SubCount[i] > 0)
            {
                var sp = s.SubSpecs[i];
                sb.Append($"{indent}⚓ {s.SubModels[i]} (عمق مجاز {sp.TestDepthM:F0}m، غواصی {sp.DiveSec:F0}s): {Num(s.SubLost[i])} از {Num(s.SubCount[i])} غرق\n");
            }
        for (int i = 0; i < s.BSModels.Length; i++)
            if (s.BSCount[i] > 0)
            {
                var sp = s.BSSpecs[i];
                float dmg = i < s.BSDamage.Length ? s.BSDamage[i] : 0f;
                sb.Append($"{indent}🚢 {s.BSModels[i]} ({sp.MainGuns}×{sp.MainMm:F0}mm، کمربند {sp.BeltMm:F0}mm): {Num(s.BSLost[i])} از {Num(s.BSCount[i])} منهدم");
                if (dmg > 1f) sb.Append($"، {dmg:F0}٪ آسیب");
                sb.Append('\n');
            }
        return sb.Length > 0 ? sb.ToString().TrimEnd('\n') : null;
    }

    static void BuildNavalReports(BattleResult r, Country atk, Country def, NavalSide A, NavalSide D, BattleLog log,
        List<string> shipStates,
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

        string? aModels = NavalModelLines(A);
        string? dModels = NavalModelLines(D);
        string? tl = Timeline(log, 0);
        string? tlD = Timeline(log, 1);

        int dur = (int)(15 + eff * 20);
        float frac = success / 100f;

        var sb = new StringBuilder(2500);
        sb.Append($"⚓ <b>گزارش نبرد دریایی — {Esc(atk.Name)} علیه {Esc(def.Name)}</b>\n");
        sb.Append($"{outcome}\n");
        sb.Append($"{Bar(frac, won ? 1 : failed ? 2 : 0)} <b>{success}٪</b> | ⏱ {dur} دقیقه\n");

        sb.Append("\n<b>🎯 طرح عملیات</b>\n");
        sb.Append($"• طرح شما: {Esc(aStratName)} / {Esc(aTacName)}\n");
        sb.Append($"• طرح دشمن: {Esc(dStratName)} / {Esc(dTacName)}\n");
        sb.Append($"• {Esc(advText)} | نسبت ناوگان: {ratio:F2}\n");
        if (A.BSCount.Length > 0 && D.BSCount.Length > 0)
        {
            var abs_ = A.BSSpecs[0]; var dbs_ = D.BSSpecs[0];
            float pAtk = NavalPenetration(abs_.MainShellKg, NavalVelocityAt(abs_.MainMuzzleMs, 18f), abs_.MainMm);
            float pDef = NavalPenetration(dbs_.MainShellKg, NavalVelocityAt(dbs_.MainMuzzleMs, 18f), dbs_.MainMm);
            sb.Append($"• در برد ۱۸ کیلومتری توپ {abs_.MainMm:F0}mm شما {pAtk:F0}mm فولاد می‌درد و کمربند {dbs_.Name} {dbs_.BeltMm:F0}mm است؛ توپ او {pDef:F0}mm در برابر کمربند {abs_.BeltMm:F0}mm شما.\n");
            sb.Append($"• کنترل آتش (اپتیکی): شما {abs_.FireControl:P0} در برابر {dbs_.FireControl:P0} دشمن{(abs_.FireControl > dbs_.FireControl + 0.06f ? " — فاصله‌یاب شما بهتر است" : dbs_.FireControl > abs_.FireControl + 0.06f ? " — فاصله‌یاب دشمن بهتر است" : "")}\n");
        }
        sb.Append($"• ترکیب شما: {Num(A.BS)}🚢 نبردناو، {Num(A.Subs)}⚓ زیردریایی، {Num(A.Boats)}🚤 اسکورت\n");
        sb.Append($"• ترکیب دشمن: {Num(D.BS)}🚢، {Num(D.Subs)}⚓، {Num(D.Boats)}🚤 (بندر سطح {portLevel})\n");
        if (A.Boats > 0)
            sb.Append("• یادآوری: قایق‌های تندرو فقط اسکورت‌اند؛ سهم آن‌ها در ضربه‌ی اصلی ناچیز است.\n");

        if (tl != null) { sb.Append("\n<b>📜 روند نبرد</b>\n").Append(tl).Append('\n'); }

        //  – ۱۳: وضعیت زیرسیستم ناوهای بازمانده
        if (shipStates != null && shipStates.Count > 0)
        {
            sb.Append("\n<b>🔧 وضعیت ناوهای بازمانده</b>\n");
            foreach (var st in shipStates) sb.Append("   ").Append(Esc(st)).Append('\n');
        }

        sb.Append("\n<b>💀 تلفات شما</b>\n");
        if (aModels != null) sb.Append(aModels).Append('\n');
        if (attBSDamage > 0) sb.Append($"   🔧 آسیب مجموع نبردناوها: {attBSDamage}٪ (نیاز به حوضچه‌ی خشک)\n");

        sb.Append("\n<b>💀 تلفات دشمن</b>\n");
        if (dModels != null) sb.Append(dModels).Append('\n');
        if (defBSDamage > 0) sb.Append($"   🔧 آسیب مجموع نبردناوهای دشمن: {defBSDamage}٪\n");

        if (r.AttackerCrewLost > 0 || r.DefenderCrewLost > 0)
            sb.Append($"\n⚰️ ملوانان: شما {Num(r.AttackerCrewLost)} نفر، دشمن {Num(r.DefenderCrewLost)} نفر\n");
        sb.Append($"💰 غنیمت دریایی: {K(lootMoney)} پول، {K(lootIron)} آهن\n");
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
        if (r.DefenderCrewLost > 0 || r.AttackerCrewLost > 0)
            sb.Append($"\n⚰️ ملوانان: شما {Num(r.DefenderCrewLost)} نفر، دشمن {Num(r.AttackerCrewLost)} نفر\n");
        sb.Append($"💸 خسارت: {K(lootMoney)} پول، {K(lootIron)} آهن\n");
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
