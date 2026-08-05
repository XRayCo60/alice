// ============================================================================
//  WarEngineGround.cs — هسته‌ی نبرد زمینی/هوایی نسخه ۳
// ============================================================================
//  شامل: تولید زمین، ساخت نیرو به تفکیک مدل، مه جنگ، مغزهای فرماندهی مجزا،
//        حرکت، آتش با محاسبه‌ی زره واقعی هر مدل، فاز هوایی، و حلقه‌ی اصلی نبرد.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

static partial class WarEngine
{
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
}
