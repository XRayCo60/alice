// ============================================================================
//  WarEngineNaval.cs — نبرد دریایی نسخه ۳
// ============================================================================
//  قواعد جدید:
//    • سوخت به‌کلی حذف شد.
//    • قایق تندرو نیروی تهاجمی نیست: فقط در دفاع ساحلی می‌جنگد. اگر مهاجم
//      قایق بفرستد، به‌عنوان اسکورت کوتاه‌برد عمل می‌کند و در بمباران/تصرف
//      سهمی ندارد؛ ضربه‌ی اصلی فقط با نبردناو و زیردریایی زده می‌شود.
//    • تلفات و گزارش به تفکیک مدل محاسبه می‌شود.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

static partial class WarEngine
{
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
}
