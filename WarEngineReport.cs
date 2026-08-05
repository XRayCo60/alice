// ============================================================================
//  WarEngineReport.cs — ساخت گزارش‌های نبرد (گروه / مهاجم / مدافع)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

static partial class WarEngine
{
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
}
