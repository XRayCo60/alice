using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InputFiles;
using Telegram.Bot.Types.ReplyMarkups;
using Microsoft.Data.Sqlite;

partial class Program
{
    // Kept lazy enough for offline regression commands; normal bot startup validates it in Main.
    static readonly string BOT_TOKEN = Environment.GetEnvironmentVariable("BOT_TOKEN") ?? "";
    const long OWNER_ID = 8248899977L;
    static TelegramBotClient bot = null!;
    static readonly ConcurrentDictionary<long, UserSession> sessions = new();
    static readonly Random rng = new();
    static readonly ConcurrentDictionary<long, SemaphoreSlim> userLocks = new();
    static readonly ConcurrentDictionary<(long ChatId, long OwnerId), SemaphoreSlim> countryMutationLocks = new();
    static readonly HashSet<int> processedUpdates = new();
    static readonly object processedLock = new();

    enum SpamDecisionKind { Allow, Drop, Warn }
    readonly record struct SpamDecision(SpamDecisionKind Kind,long UserId,long UntilMs,string Reason);
    readonly record struct SpamEvent(long AtMs,string Fingerprint,bool InvalidCallback);
    sealed class SpamState
    {
        public readonly object Gate=new();
        public readonly Queue<SpamEvent> Events=new();
        public bool Loaded;
        public long RestrictUntilMs;
        public int Level;
        public long LastViolationMs;
        public long LastWarningMs;
        public string LastFingerprint="";
        public long LastFingerprintAtMs;
        public int Dropped;
        public string Reason="";
    }
    static readonly ConcurrentDictionary<long,SpamState> spamStates=new();
    static readonly HashSet<string> knownCallbackActions=new(StringComparer.Ordinal)
    {
        "cancel","faction","eq_details","dep_info","build_menu","upgrade","timing","tank_info","tank_buy",
        "plane_info","plane_buy","bomber_info","bomber_buy","aa_info","aa_buy","defense_status","defense_tactic",
        "defense_tactic_select","defense_set","naval_defense","naval_defense_strategy","naval_defense_tactic","naval_cancel",
        "naval_locked","defense_pct","defense_model_pct","boat_info","boat_buy","sub_info","sub_buy","battleship_info",
        "battleship_buy","battleship_repair","battleship_repair_quote","battleship_repair_unit","battleship_scrap_menu",
        "battleship_scrap","battleship_scrap_confirm","airdef_strategy","airdef_tactic","attack_group","attack_target",
        "revenge","attack_type","attack_strategy","attack_tactic","attack_air_strategy","attack_air_tactic",
        "attack_naval_strategy","attack_naval_tactic"
    };

    static bool IsKnownCallbackData(string data)
    {
        if(data.StartsWith("adm:",StringComparison.Ordinal)||data.StartsWith("spam_admin:",StringComparison.Ordinal)||
           data.StartsWith("ally_",StringComparison.Ordinal)||data.StartsWith("tf_",StringComparison.Ordinal)||
           data.StartsWith("dep_",StringComparison.Ordinal))return true;
        string action=data.Split(':',2)[0];return knownCallbackActions.Contains(action);
    }

    static string SpamFingerprint(Update update,out long userId,out long chatId,out bool invalidCallback,out bool callback)
    {
        userId=0;chatId=0;invalidCallback=false;callback=false;
        if(update.CallbackQuery!=null)
        {
            callback=true;userId=update.CallbackQuery.From.Id;chatId=update.CallbackQuery.Message?.Chat.Id??userId;
            string data=update.CallbackQuery.Data??"";invalidCallback=!IsKnownCallbackData(data);
            int messageId=update.CallbackQuery.Message?.MessageId??0;
            return $"cb:{chatId}:{messageId}:{data}";
        }
        if(update.Message?.From!=null)
        {
            userId=update.Message.From.Id;chatId=update.Message.Chat.Id;
            string value=(update.Message.Text??update.Message.Caption??update.Message.Type.ToString()).Trim().Replace('\n',' ');
            if(value.Length>80)value=value[..80];
            return $"msg:{chatId}:{value}";
        }
        return "";
    }

    static SpamDecision EvaluateSpam(Update update)
    {
        string fingerprint=SpamFingerprint(update,out long userId,out long chatId,out bool invalidCallback,out bool callback);
        if(userId==0||userId==OWNER_ID||fingerprint.Length==0)return new(SpamDecisionKind.Allow,userId,0,"");
        var state=spamStates.GetOrAdd(userId,_=>new SpamState());
        long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        lock(state.Gate)
        {
            if(!state.Loaded)
            {
                state.Loaded=true;
                try
                {
                    var saved=Database.GetSpamRestriction(userId);
                    if(saved!=null)
                    {
                        state.RestrictUntilMs=saved.UntilMs;state.Level=saved.Level;state.LastViolationMs=saved.UpdatedAtMs;
                        state.Dropped=saved.DroppedCount;state.Reason=saved.Reason;
                    }
                }
                catch { }
            }
            if(state.RestrictUntilMs>now)
            {
                state.Dropped++;
                if(state.Dropped%25==0)
                    try{Database.SaveSpamRestriction(new SpamRestrictionInfo{UserId=userId,ChatId=chatId,UntilMs=state.RestrictUntilMs,
                        Level=state.Level,Reason=state.Reason,LastFingerprint=state.LastFingerprint,DroppedCount=state.Dropped,UpdatedAtMs=state.LastViolationMs});}catch{}
                bool warn=now-state.LastWarningMs>=60_000;
                if(warn)state.LastWarningMs=now;
                return new(warn?SpamDecisionKind.Warn:SpamDecisionKind.Drop,userId,state.RestrictUntilMs,"محدودیت ضداسپم فعال است");
            }

            while(state.Events.Count>0&&now-state.Events.Peek().AtMs>60_000)state.Events.Dequeue();
            bool exactFast=callback&&state.LastFingerprint==fingerprint&&now-state.LastFingerprintAtMs<=1_200;
            state.LastFingerprint=fingerprint;state.LastFingerprintAtMs=now;
            state.Events.Enqueue(new SpamEvent(now,fingerprint,invalidCallback));
            var tenSeconds=state.Events.Where(x=>now-x.AtMs<=10_000).ToList();
            int sameTen=tenSeconds.Count(x=>x.Fingerprint==fingerprint);
            int invalidTen=tenSeconds.Count(x=>x.InvalidCallback);
            string? violation=null;
            if(invalidTen>=8)violation="دکمه‌های نامعتبر تکراری";
            else if(exactFast&&sameTen>=7)violation="فشردن پشت‌سرهم یک دکمه";
            else if(tenSeconds.Count>=30&&tenSeconds.GroupBy(x=>x.Fingerprint).Max(x=>x.Count())>=15)violation="درخواست تکراری سنگین";
            else if(tenSeconds.Count>=60)violation="حجم غیرعادی درخواست";
            else if(state.Events.Count>=180)violation="اسپم مداوم یک‌دقیقه‌ای";

            if(violation==null)
            {
                if(exactFast||invalidCallback)
                {
                    state.Dropped++;
                    return new(SpamDecisionKind.Drop,userId,0,"");
                }
                return new(SpamDecisionKind.Allow,userId,0,"");
            }

            state.Level=now-state.LastViolationMs>3_600_000?1:Math.Min(3,state.Level+1);
            state.LastViolationMs=now;
            long duration=state.Level switch{1=>15_000,2=>120_000,_=>1_800_000};
            state.RestrictUntilMs=now+duration;state.LastWarningMs=now;state.Dropped++;state.Reason=violation;
            string storedFingerprint=fingerprint.Length>120?fingerprint[..120]:fingerprint;
            try{Database.SaveSpamRestriction(new SpamRestrictionInfo{UserId=userId,ChatId=chatId,UntilMs=state.RestrictUntilMs,
                Level=state.Level,Reason=violation,LastFingerprint=storedFingerprint,DroppedCount=state.Dropped,UpdatedAtMs=now});}catch{}
            Console.WriteLine($"[SPAM BLOCK] user={userId} chat={chatId} level={state.Level} until={state.RestrictUntilMs} reason={violation}");
            return new(SpamDecisionKind.Warn,userId,state.RestrictUntilMs,violation);
        }
    }

    static void ClearSpamState(long userId)
    {
        spamStates.TryRemove(userId,out _);Database.ClearSpamRestriction(userId);
    }

    static void RestrictSpamUser(long userId,long chatId,TimeSpan duration,string reason)
    {
        long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();long until=now+(long)duration.TotalMilliseconds;
        var state=spamStates.GetOrAdd(userId,_=>new SpamState());
        lock(state.Gate){state.Loaded=true;state.RestrictUntilMs=until;state.Level=3;state.LastViolationMs=now;state.LastWarningMs=0;state.Reason=reason;
            Database.SaveSpamRestriction(new SpamRestrictionInfo{UserId=userId,ChatId=chatId,UntilMs=until,Level=3,Reason=reason,
                LastFingerprint=state.LastFingerprint,DroppedCount=state.Dropped,UpdatedAtMs=now});}
    }

    sealed class MsgContext { public long UserId; public long ChatId; public int MessageId; public bool Marked; }
    static readonly AsyncLocal<MsgContext?> incomingCtx = new();

    static void MarkIncomingHandled()
    {
        var c = incomingCtx.Value;
        if (c != null && !c.Marked)
        {
            c.Marked = true;
            Database.MarkPlayerActive(c.UserId);
            ScheduleDelete(c.ChatId, c.MessageId, 30);
        }
    }

    static Timer? assetUpdateTimer;
    static Timer? transferTimer;
    static readonly SemaphoreSlim transferProcessorLock = new(1, 1);
    static readonly SemaphoreSlim deploymentProcessorLock = new(1, 1);
    static readonly SemaphoreSlim navalProcessorLock = new(1, 1);
    static readonly JsonSerializerOptions BattleJsonOptions = new()
    {
        IncludeFields = true,
        PropertyNameCaseInsensitive = true
    };
    static int databaseMaintenanceRunning = 0;
    static int activeUpdateHandlers = 0;
    static int assetUpdateRunning = 0;
    static DateTime lastAssetRunUtc = DateTime.MinValue;

    static readonly ConcurrentDictionary<string, int> attackCounts = new();
    static int MAX_ATTACKS_PER_UPDATE = 8;
    const int MAX_NAVAL_ATTACKS_PER_UPDATE = 8;
    static readonly ConcurrentDictionary<string, int> navalAttackCounts = new();
    static readonly ConcurrentDictionary<string, int> transferCounts = new();
    static int MAX_TRANSFERS_PER_UPDATE = 2;
    static DateTime lastAssetUpdateAt = DateTime.MinValue;
    static int ATTACK_LOCK_MINUTES = 30;
    static double SHIELD_HOURS = 48.0;

    static string AtkKey(long chatId, long ownerId) => $"{chatId}:{ownerId}";
    static int GetAttackCount(long chatId, long ownerId) => attackCounts.TryGetValue(AtkKey(chatId, ownerId), out var v) ? v : 0;
    static int IncAttackCount(long chatId, long ownerId) => attackCounts.AddOrUpdate(AtkKey(chatId, ownerId), 1, (_, v) => v + 1);
    // Starting an attack forfeits every kind of protection held by the attacker.
    // Outgoing attacks never add a hit toward the attacker's own five-hit shield.
    internal static void BreakAttackerShieldOnAttack(long attackerId,long chatId)
    {
        Database.ClearAttackShield(attackerId,chatId);
        Database.SetShieldExemption(attackerId,chatId); // also ends the new-country shield
    }
    internal static void ApplyCompletedAttackShieldRules(long attackerId,long defenderId,long chatId,bool fullExemption)
    {
        BreakAttackerShieldOnAttack(attackerId,chatId);
        if(!fullExemption)Database.AddAttackShieldHit(defenderId,chatId);
    }
    static int GetNavalAttackCount(long chatId,long ownerId) => navalAttackCounts.TryGetValue(AtkKey(chatId,ownerId),out var v)?v:0;
    static int IncNavalAttackCount(long chatId,long ownerId) => navalAttackCounts.AddOrUpdate(AtkKey(chatId,ownerId),1,(_,v)=>v+1);
    static string TfKey(long chatId, long ownerId) => $"{chatId}:{ownerId}";
    static int GetTransferCount(long chatId, long ownerId) => transferCounts.TryGetValue(TfKey(chatId, ownerId), out var v) ? v : 0;
    static int IncTransferCount(long chatId, long ownerId) => transferCounts.AddOrUpdate(TfKey(chatId, ownerId), 1, (_, v) => v + 1);

    static string UpdateMode = "daily";
    static int UpdateValue = 1200;
    static string SpecialPhotoFileId = "";

    static readonly int[] FactoryUpgradeCost = { 0, 5, 12, 30, 80 };
    static readonly int[] PortUpgradeCost = { 0, 13, 25, 50, 75 };
    static readonly int[] MineUpgradeCost = { 0, 5, 12, 30, 80, 0, 0 };
    // Levels 6 and 7 are premium upgrades. These prices are keyed by TARGET level;
    // existing level-6/7 countries are never migrated, charged again, or downgraded.
    static readonly IReadOnlyDictionary<int, int> MineRoyalUpgradeCost =
        new Dictionary<int, int> { [6] = 5, [7] = 10 };
    static int MineRoyalCostForTargetLevel(int targetLevel) =>
        MineRoyalUpgradeCost.TryGetValue(targetLevel, out int cost) ? cost : 0;
    static readonly double[] FactoryIncome = { 0, 1, 2, 5, 15, 30 };
    static readonly double[] PortIncome = { 0, 1, 2, 4, 8, 15 };
    static readonly double[] MineIncome = { 0, 1, 2, 5, 15, 30, 40, 50 };

    const string MsgNoCountryGuide = "❌ شما در این گپ کشوری ندارید.\nℹ️ با نوشتن دستور «راهنما» می‌توانید راهنمای بازی را ببینید. دستور دریافت کشور «انتخاب کشور» است.";

    // ============================================================
    //  متن راهنمای کامل — FIX(4)
    //  در گروه و پیوی یکسان استفاده می‌شود.
    // ============================================================
        const string HelpText =
        "📘 <b>راهنمای کامل آلیس</b>\n" +
        "برای اجرای هر بخش، فقط کافی است دستور مربوطه را (در گروه) بنویسید.\n" +
        "بعضی بخش‌ها (حمله، ترنسفر، صف‌آرایی، وضعیت دفاع) برای تنظیم دقیق به <b>پیوی ربات</b> منتقل می‌شوند.\n" +
        "برای لغو هر عملیات نیمه‌کاره، کلمهٔ «<b>لغو</b>» را بنویسید.\n" +
        "──────────────\n\n" +

        "🌍 <b>شروع و مدیریت کشور</b>\n" +
        "• <b>انتخاب کشور</b> — ساخت کشور جدید (انتخاب فکشن 🇺🇸/☭/⚫ + نام).\n" +
        "  ❌ نام‌های مشابه بالای 90% ممنوع: «این نام خیلی شبیه به نام موجود است!!»\n" +
        "• <b>دارایی</b> (یا «کشورم») — مشاهدهٔ کامل اقتصادی، نظامی و دریایی.\n" +
        "• <b>مان پاور</b> — قدرت کل + تفکیک عوامل.\n" +
        "• <b>تغییر اسم</b> — تغییر نام کشور (بررسی شباهت 90%).\n" +
        "• <b>تغییر پرچم</b> — ارسال عکس.\n" +
        "• <b>انصراف</b> — حذف کامل کشور (۲۴ ساعت قفل ساخت مجدد).\n\n" +

        "🏗 <b>اقتصاد و توسعه</b>\n" +
        "• <b>اقتصاد</b> / ساختمان — ارتقای 🏭 کارخانه، ⚓ بندر و ⛏️ معدن.\n" +
        "• بندر سطح 4 لازم برای نبردناو (Bismarck/Iowa/Sovetsky Soyuz) حداکثر 3 عدد.\n" +
        "• <b>مالیات</b> ۰-۱۰۰٪، <b>آموزش سرباز</b> ۰-۱۰، <b>ترید</b> 1 رویال=10K پول.\n\n" +

        "⚔️ <b>ساخت ارتش — چندمدلی</b>\n" +
        "• هر کشور چندین مدل تجهیزات دارد و حتی با تغییر فکشن حفظ می‌شود.\n" +
        "• <b>ساخت تانک</b> — M2 Medium 🇺🇸 / T-28 ☭ / Panzer III ⚫ (هر ۵ عدد).\n" +
        "• <b>ساخت هواپیما</b> — P-36 / I-16 / Bf 109 + بمب‌افکن B-17 / DB-3 / He 111.\n" +
        "• <b>پدافند</b> — توپ 76mm ضد هوایی.\n" +
        "• در حمله و دفاع می‌توانید برای هر مدل جداگانه تعداد / درصد تعیین کنید.\n" +
        "• موتور جنگ هر مدل را با مشخصات واقعی ۱۹۳۹، مهمات و سوخت داخلی همان مدل شبیه‌سازی می‌کند.\n\n" +

        "⚓ <b>نیروی دریایی — ناوگان</b>\n" +
        "• دستور: <b>خرید ناو / خرید کشتی / خرید قایق / نیروی دریایی / ناوگان</b>\n" +
        "  🇩🇪 S-Boot 38–41 گره — هر 5: 2K پول+1K آهن\n" +
        "  🇺🇸 PT Boat 40–45 گره — هر 5: 3K+1.5K\n" +
        "  ☭ G-5 50–53 گره — هر 5: 2.5K+1.5K\n" +
        "• زیردریایی: Type VIIC 17.7/7.6 — 10K+5K | Gato 21/9 — 10K+5K | S-class 13–14/7–8 — 8K+4K\n" +
        "• نبردناو: Bismarck 30 گره 2092 خدمه 8x380mm — 50K+30K | Iowa 28 گره 1800 خدمه 9x406mm — 50K+40K | Sovetsky Soyuz 23 گره 1220 خدمه 12x305mm — 45K+25K (پورت>=4 max3)\n" +
        "• انتقال نبردناو: <b>نمیتوانید به این کشور نبردناو ترنسفر کنید، تعداد نبرد ناو: 3</b>\n" +
        "• هر کشور تا آپدیت بعدی حداکثر <b>۸ حمله دریایی</b> می‌تواند آغاز کند.\n" +
        "• <b>لغو لشکرکشی دریایی</b> در پیوی — انتخاب عملیات و بازگشت فوری کل ناوگان بدون تلفات.\n\n" +

        "🗡 <b>حمله زمینی و هوایی — موتور ۱۹۳۹</b>\n" +
        "• میدان هر نبرد ۴۰×۴۰ کیلومتر و حداکثر زمان عملیات ۲۴ ساعت است.\n" +
        "• زمین و آب‌وهوا پس از ثبت فرمان‌ها به‌صورت منسجم تولید می‌شوند.\n" +
        "• فرماندهان بر اساس استراتژی، تاکتیک، اطلاعات کشف‌شده و وضعیت واقعی میدان تصمیم می‌گیرند.\n" +
        "• پیروزی سنگین: بیش از ۳۵km پیشروی مؤثر با بازگشت حداقل ۵۰۰۰ سرباز و ۵۰ تانک سالم.\n" +
        "• <b>لیست نبردهای در جریان</b> در گروه یا پیوی — نمایش پیشرفت گرفتن یا از دست دادن شهر.\n" +
        "• با از دست دادن شهر، مالک در پیوی هشدار و دکمه <b>⚔️ انتقام</b> دریافت می‌کند.\n" +
        "• با آغاز حمله، سپر مهاجم از بین می‌رود و حملات خروجی برای مهاجم سپر ایجاد نمی‌کنند.\n" +
        "• اثر محاصره فقط پس از ازدست‌رفتن یکی از ۴ شهر اولیه فعال می‌شود؛ حذف مهاجم یا هم‌اتحادشدن، محاصره را برمی‌دارد.\n\n" +

        "🛡 <b>دفاع — چندمدلی و دریایی</b>\n" +
        "• <b>وضعیت دفاع</b> در پیوی: درصد برای هر مدل تانک/جنگنده/قایق/زیر جداگانه (20-100%). حداقل 20% همیشه در دفاع.\n" +
        "• دفاع دریایی: قایق و زیردریایی per-model.\n\n" +

        "🤝 <b>اتحادها</b>\n" +
        "• <b>ساخت اتحاد</b> (شباهت 90% چک)، <b>ایجاد درخواست عضویت</b> ریپلای، <b>وضعیت اتحاد</b>، <b>لیست اتحاد ها</b>، <b>حذف N</b>، <b>خروج</b>، <b>انحلال</b>.\n\n" +

        "🚚 <b>عملیات مشترک — ترنسفر و صف‌آرایی</b>\n" +
        "• <b>ترنسفر</b> — پول/آهن/سرباز/تانک/جنگنده/بمب‌افکن/قایق/زیر/نبردناو به هم‌اتحادی (پیوی). حفظ مدل حتی با تغییر فکشن. هر مدل مقدار جداگانه. نبردناو max3.\n" +
        "• <b>صف‌آرایی تهاجمی/دفاعی</b> فعال است و نیروهای چند کشور در موتور جدید به‌صورت مشارکت‌کننده مستقل محاسبه می‌شوند.\n" +
        "• نیروهای دفاعی در دارایی دیده نمی‌شوند، فقط در <b>جزئیات نظامی → اطلاعات نیروهای صف آرایی</b> گروه‌بندی فکشن با مجموع. پیام گروه فقط مشارکت‌کنندگان + 🎯 استراتژی: X | تاکتیک: Y. پس از join پیام پین ویرایش می‌شود.\n" +
        "• <b>اعزام نیرو</b> / دکمه ⚔️ مشارکت. <b>لغو صف آرایی</b> → آنپین+حذف.\n" +
        "• قبل از حمله/ترنسفر/صف‌آرایی یک‌بار در پیوی استارت کنید. «لغو» برای خروج.\n\n" +

        "🏆 <b>لیدربورد شبانه</b>\n" +
        "• هر شب 22:00 تهران +30 ثانیه، سه بورد عمومی ارسال می‌شود: برترین مان‌پاور پلیرها، برترین گروه‌ها از نظر تعداد پلیر و برترین گروه‌ها از نظر مجموع مان‌پاور.\n\n" +

        "──────────────\n📢 @alice_safe_house1";

    // ============================================================
    //  منطقه‌زمانی تهران — مقاوم و مستقل از تنظیمات سرور
    // ============================================================
    static readonly TimeSpan TehranOffset = TimeSpan.FromHours(3.5);
    static DateTime GetTehranNow()
    {
        return DateTime.UtcNow.AddHours(3.5);
    }

    static async Task RecoverPersistedBattleJobs(CancellationToken ct)
    {
        foreach (var job in Database.GetRecoverableBattleJobs())
        {
            if (!job.JobType.Equals("Direct", StringComparison.OrdinalIgnoreCase))
            {
                if (job.JobType.Equals("Deployment", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var deploymentContext = JsonSerializer.Deserialize<BattleJobContext>(job.ContextJson, BattleJsonOptions);
                        if (deploymentContext != null && Database.GetDeploymentById(deploymentContext.DeploymentId) == null)
                            Database.UpdateBattleJob(job.BattleId, "Completed", job.ResultJson);
                        else
                            Database.UpdateBattleJob(job.BattleId, "Pending", job.ResultJson);
                    }
                    catch (Exception ex) { Database.UpdateBattleJob(job.BattleId, "Pending", error: ex.Message); }
                }
                continue;
            }
            try
            {
                var request = JsonSerializer.Deserialize<BattleRequest>(job.RequestJson, BattleJsonOptions)
                    ?? throw new InvalidOperationException("Stored request is invalid.");
                var context = JsonSerializer.Deserialize<BattleJobContext>(job.ContextJson, BattleJsonOptions)
                    ?? throw new InvalidOperationException("Stored context is invalid.");
                BattleResult result;
                if (!string.IsNullOrWhiteSpace(job.ResultJson))
                    result = JsonSerializer.Deserialize<BattleResult>(job.ResultJson, BattleJsonOptions)
                        ?? throw new InvalidOperationException("Stored result is invalid.");
                else
                {
                    Database.UpdateBattleJob(job.BattleId, "Running");
                    result = await BattleExecutionScheduler.EnqueueAsync(request, ct);
                    Database.UpdateBattleJob(job.BattleId, "Resolved",
                        JsonSerializer.Serialize(result, BattleJsonOptions));
                }

                var attacker = Database.GetCountry(context.AttackerId, context.ChatId);
                var defender = Database.GetCountry(context.DefenderId, context.ChatId);
                if (attacker == null || defender == null)
                {
                    Database.UpdateBattleJob(job.BattleId, "Failed", error: "Country no longer exists.");
                    continue;
                }
                var ownDefense = request.Defenders.FirstOrDefault(x => x.OwnerId == context.DefenderId)
                    ?? request.Defenders.First();
                var deploymentParticipants = request.Defenders.Where(x => !ReferenceEquals(x, ownDefense)).ToList();
                var defensiveDeployments = context.DefensiveDeploymentIds
                    .Select(Database.GetDeploymentById).Where(x => x != null).Cast<Deployment>().ToList();
                bool applied = ApplyDirectBattleLosses(job.BattleId, attacker, defender, ownDefense,
                    deploymentParticipants, defensiveDeployments, result);
                if (applied)
                {
                    try { Database.SaveBattleResult(request, result); } catch { }
                    IncAttackCount(context.ChatId, context.AttackerId);
                    string today = DateTime.UtcNow.AddHours(3.5).ToString("yyyy-MM-dd");
                    Database.IncDailyDefendCount(context.DefenderId, today);
                    Database.SetAttackerFlag(context.AttackerId, today);
                    ApplyCompletedAttackShieldRules(context.AttackerId,context.DefenderId,context.ChatId,
                        Database.HasGroupLockExemption(context.ChatId));
                    try { await SendPermanent(context.AttackerId, result.AttackerReport, ct: ct); } catch { }
                    try { await SendPermanent(context.DefenderId, result.DefenderReport, ct: ct); } catch { }
                    try { await SendPermanent(context.ChatId, result.GroupAnnouncement, ct: ct); } catch { }
                    await ProcessStrategicBattleOutcome(context.AttackerId, context.DefenderId,
                        context.ChatId, result, ct);
                }
                Database.UpdateBattleJob(job.BattleId, "Completed",
                    JsonSerializer.Serialize(result, BattleJsonOptions));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BATTLE RECOVERY ERR] id={job.BattleId}: {ex}");
                Database.UpdateBattleJob(job.BattleId, "Pending", error: ex.Message);
            }
        }
    }

    static async Task Main(string[] args)
    {
        // خودآزمایی موتور نبرد بدون راه‌اندازی ربات: dotnet run -- selftest [تعداد seed]
        if (args.Length > 0 && args[0].Equals("selftest", StringComparison.OrdinalIgnoreCase))
        {
            int seeds = args.Length > 1 && int.TryParse(args[1], out int s)
                ? Math.Clamp(s, 1, 200) : 20;
            WarEngine.RunSelfTest(seeds);
            return;
        }
        if (args.Length > 0 && args[0].Equals("navaltest", StringComparison.OrdinalIgnoreCase))
        {
            NavalRegressionTests.Run();
            return;
        }
        if (args.Length > 0 && args[0].Equals("attacktest", StringComparison.OrdinalIgnoreCase))
        {
            AttackSelectionRegressionTests.Run();
            return;
        }
        if (args.Length > 0 && args[0].Equals("alltests", StringComparison.OrdinalIgnoreCase))
        {
            NavalRegressionTests.Run();
            EconomyRegressionTests.Run();
            AttackSelectionRegressionTests.Run();
            StrategicBattleRegressionTests.Run();
            SiegeRegressionTests.Run();
            GroupLifecycleRegressionTests.Run();
            return;
        }
        if (string.IsNullOrWhiteSpace(BOT_TOKEN))
            throw new InvalidOperationException("BOT_TOKEN environment variable is required.");
        Database.Init();
        Console.WriteLine($"[SIEGE INTEGRITY] {Database.RepairSiegeIntegrity()}");
        Console.WriteLine($"[DEPLOYMENT INTEGRITY] {Database.RepairDeploymentIntegrity()}");
        Database.InitNavalV2();
        Console.WriteLine($"[NAVAL INTEGRITY] {Database.RepairPendingNavalOperations()}");
        Database.InitActivity();
        Database.InitAdminPanel(OWNER_ID);
        LoadSettings();
        // Proxy support for IR filtering – if TELEGRAM_PROXY env or setting exists, use it
        try
        {
            string proxyUrl = Environment.GetEnvironmentVariable("TELEGRAM_PROXY") ?? Database.GetSetting("ProxyUrl");
            if (!string.IsNullOrWhiteSpace(proxyUrl))
            {
                var proxy = new System.Net.WebProxy(proxyUrl);
                var httpClient = new HttpClient(new HttpClientHandler { Proxy = proxy, UseProxy = true });
                bot = new TelegramBotClient(BOT_TOKEN, httpClient);
                Console.WriteLine("[BOT] Using configured proxy");
            }
            else
            {
                bot = new TelegramBotClient(BOT_TOKEN);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PROXY ERR] {ex.Message} – falling back to direct");
            bot = new TelegramBotClient(BOT_TOKEN);
        }
        Console.WriteLine("Bot starting...");
        using var cts = new CancellationTokenSource();
        await RecoverPersistedBattleJobs(cts.Token);
        bot.StartReceiving(
            updateHandler: HandleUpdateAsync,
            pollingErrorHandler: HandlePollingErrorAsync,
            receiverOptions: new ReceiverOptions { AllowedUpdates = Array.Empty<UpdateType>() },
            cancellationToken: cts.Token
        );
        Console.WriteLine("Bot is running...");
        // ArriveAtMs is an absolute persisted timestamp. Any operation that arrived while
        // the bot was offline is resolved immediately on startup; settlement is idempotent.
        try { await ProcessNavalInvasions(cts.Token); }
        catch (Exception ex) { Console.WriteLine($"[NAVAL STARTUP RECOVERY ERR] {ex}"); }
        StartAssetUpdateTimer();
        StartTransferTimer();
        StartActivityStatsTimer();
        StartLeaderboardTimer();
        await Task.Delay(-1);
    }

    static async Task<(bool Success, string Error)> RestoreDatabaseSafely(
        string uploadedPath,
        CancellationToken ct)
    {
        if (!Database.ValidateDatabaseFile(uploadedPath, out string validationError))
            return (false, $"فایل دیتابیس معتبر نیست: {validationError}");

        if (Interlocked.CompareExchange(ref databaseMaintenanceRunning, 1, 0) != 0)
            return (false, "عملیات نگهداری دیگری در حال اجراست.");

        bool transferLocked = false;
        bool deploymentLocked = false;
        bool navalLocked = false;
        string rollbackPath = $"gamedata_pre_restore_{DateTime.UtcNow:yyyyMMdd_HHmmss}.db";

        assetUpdateTimer?.Dispose();
        transferTimer?.Dispose();
        activityStatsTimer?.Dispose();
        leaderboardTimer?.Dispose();

        try
        {
            DateTime waitUntil = DateTime.UtcNow.AddMinutes(2);
            while ((Volatile.Read(ref activeUpdateHandlers) > 1 ||
                    Volatile.Read(ref assetUpdateRunning) != 0) &&
                   DateTime.UtcNow < waitUntil)
            {
                await Task.Delay(100, ct);
            }
            if (Volatile.Read(ref activeUpdateHandlers) > 1 ||
                Volatile.Read(ref assetUpdateRunning) != 0)
            {
                return (false, "ربات هنوز در حال پردازش عملیات دیگری است؛ کمی بعد دوباره تلاش کنید.");
            }

            await transferProcessorLock.WaitAsync(ct);
            transferLocked = true;
            await deploymentProcessorLock.WaitAsync(ct);
            deploymentLocked = true;
            await navalProcessorLock.WaitAsync(ct);
            navalLocked = true;

            Database.CreateConsistentBackup(rollbackPath);
            Database.CheckpointAndClearPools();

            System.IO.File.Move(uploadedPath, "gamedata.db", true);
            TryDeleteSqliteSidecar("gamedata.db-wal");
            TryDeleteSqliteSidecar("gamedata.db-shm");

            try
            {
                Database.Init();
                Console.WriteLine($"[SIEGE INTEGRITY] {Database.RepairSiegeIntegrity()}");
                Console.WriteLine($"[DEPLOYMENT INTEGRITY] {Database.RepairDeploymentIntegrity()}");
                Database.InitNavalV2();
                Console.WriteLine($"[NAVAL INTEGRITY] {Database.RepairPendingNavalOperations()}");
                Database.InitActivity();
                Database.InitAdminPanel(OWNER_ID);
                LoadSettings();
            }
            catch
            {
                SqliteConnection.ClearAllPools();
                System.IO.File.Copy(rollbackPath, "gamedata.db", true);
                TryDeleteSqliteSidecar("gamedata.db-wal");
                TryDeleteSqliteSidecar("gamedata.db-shm");
                Database.Init();
                Console.WriteLine($"[SIEGE INTEGRITY] {Database.RepairSiegeIntegrity()}");
                Console.WriteLine($"[DEPLOYMENT INTEGRITY] {Database.RepairDeploymentIntegrity()}");
                Database.InitNavalV2();
                Console.WriteLine($"[NAVAL INTEGRITY] {Database.RepairPendingNavalOperations()}");
                Database.InitActivity();
                Database.InitAdminPanel(OWNER_ID);
                LoadSettings();
                throw;
            }

            return (true, "");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return (false, "عملیات بازیابی لغو شد.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
        finally
        {
            if (navalLocked) navalProcessorLock.Release();
            if (deploymentLocked) deploymentProcessorLock.Release();
            if (transferLocked) transferProcessorLock.Release();
            Volatile.Write(ref databaseMaintenanceRunning, 0);
            try { await ProcessNavalInvasions(CancellationToken.None); }
            catch (Exception ex) { Console.WriteLine($"[NAVAL RESTORE RECOVERY ERR] {ex}"); }
            StartAssetUpdateTimer();
            StartTransferTimer();
            StartActivityStatsTimer();
            StartLeaderboardTimer();
        }
    }

    static void TryDeleteSqliteSidecar(string path)
    {
        try
        {
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SQLITE SIDECAR CLEANUP ERR] {path}: {ex.Message}");
        }
    }

    static void LoadSettings()
    {
        var mode = Database.GetSetting("UpdateMode");
        var val = Database.GetSetting("UpdateValue");
        var special = Database.GetSetting("SpecialPhotoFileId");
        if (!string.IsNullOrEmpty(mode)) UpdateMode = mode;
        if (TryParseInt(val, out int v)) UpdateValue = v;
        if (!string.IsNullOrEmpty(special)) SpecialPhotoFileId = special;
    }

    static string NormalizeDigits(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.Trim())
        {
            if (ch >= '\u06F0' && ch <= '\u06F9') sb.Append((char)('0' + (ch - '\u06F0')));
            else if (ch >= '\u0660' && ch <= '\u0669') sb.Append((char)('0' + (ch - '\u0660')));
            else if (ch == '\u066C' || ch == ',' || ch == '\u060C' || ch == ' ' || ch == '\u200c') { }
            else sb.Append(ch);
        }
        return sb.ToString();
    }

    static bool TryParseLong(string? s, out long v) =>
        long.TryParse(NormalizeDigits(s), NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
    static bool TryParseInt(string? s, out int v) =>
        int.TryParse(NormalizeDigits(s), NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
    static string InventoryLine(long amount) =>
        amount > 0 ? $"موجودی: {amount:N0}" : "⚠️ موجودی نداری";

    static bool HasAvailableForces(
        Country country,
        long tanks,
        long soldiers,
        long fighters,
        long bombers) =>
        tanks >= 0 && soldiers >= 0 && fighters >= 0 && bombers >= 0 &&
        country.Tanks >= tanks &&
        country.Soldiers >= soldiers &&
        country.Planes >= fighters &&
        country.Bombers >= bombers;

    static string AvailableForcesText(Country country) =>
        $"🛡 تانک: {country.Tanks:N0}\n" +
        $"🪖 سرباز: {country.Soldiers:N0}\n" +
        $"✈️ جنگنده: {country.Planes:N0}\n" +
        $"🛩 بمب‌افکن: {country.Bombers:N0}";

    static long GetCountryResourceAmount(Country country, string resourceType) => resourceType switch
    {
        "money" => country.Money,
        "iron" => country.Iron,
        "soldiers" => country.Soldiers,
        "tanks" => country.Tanks,
        "planes" => country.Planes,
        "bombers" => country.Bombers,
        "boats" => country.Boats,
        "submarines" => country.Submarines,
        "battleships" => country.Battleships,
        _ => 0
    };

    static async Task<bool> TryCreateTransfersSafely(
        long senderId,
        long chatId,
        long allianceId,
        long receiverId,
        string resourceType,
        IReadOnlyList<(string ModelName, long Amount)> shipments,
        long arriveAtMs,
        CancellationToken ct)
    {
        if(!Database.IsBotGroupActive(chatId))return false;
        // Receiver is locked too: battleship capacity (including in-flight transfers) must
        // be checked and reserved atomically against concurrent senders.
        var locks = await AcquireCountryMutationLocks(chatId, new[] { senderId, receiverId }, ct);
        try
        {
            return Database.TryCreateTransfers(
                senderId,
                chatId,
                allianceId,
                receiverId,
                resourceType,
                shipments,
                arriveAtMs);
        }
        finally
        {
            ReleaseCountryMutationLocks(locks);
        }
    }

    static IReadOnlyDictionary<string, long> SelectedDeploymentModels(
        IReadOnlyList<string> names, IReadOnlyList<long> amounts,
        long total, string defaultModel)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < names.Count && i < amounts.Count; i++)
            if (amounts[i] > 0)
                result[names[i]] = result.GetValueOrDefault(names[i]) + amounts[i];
        if (result.Count == 0 && total > 0) result[defaultModel] = total;
        if (result.Values.Sum() != total)
            throw new InvalidOperationException("Selected deployment model totals are inconsistent.");
        return result;
    }

    static async Task<long> TryCreateDeploymentSafely(Deployment deployment, CancellationToken ct,
        IReadOnlyDictionary<string, long>? tankModels = null,
        IReadOnlyDictionary<string, long>? fighterModels = null,
        IReadOnlyDictionary<string, long>? bomberModels = null)
    {
        if(!Database.IsBotGroupActive(deployment.ChatId))return 0;
        await deploymentProcessorLock.WaitAsync(ct);
        List<SemaphoreSlim>? locks = null;
        try
        {
            locks = await AcquireCountryMutationLocks(
                deployment.ChatId,
                new[] { deployment.InitiatorId },
                ct);
            try
            {
                return Database.TryCreateDeploymentWithForces(deployment,
                    tankModels, fighterModels, bomberModels);
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"[DEPLOYMENT MODEL RESERVATION] {ex.Message}");
                return 0;
            }
        }
        finally
        {
            if (locks != null) ReleaseCountryMutationLocks(locks);
            deploymentProcessorLock.Release();
        }
    }

    static async Task<bool> TryJoinDeploymentSafely(
        Deployment deployment,
        DeploymentContributor contributor,
        CancellationToken ct,
        IReadOnlyDictionary<string, long>? tankModels = null,
        IReadOnlyDictionary<string, long>? fighterModels = null,
        IReadOnlyDictionary<string, long>? bomberModels = null)
    {
        if(!Database.IsBotGroupActive(deployment.ChatId))return false;
        await deploymentProcessorLock.WaitAsync(ct);
        List<SemaphoreSlim>? locks = null;
        try
        {
            locks = await AcquireCountryMutationLocks(
                deployment.ChatId,
                new[] { contributor.UserId },
                ct);
            try
            {
                return Database.TryJoinDeploymentWithForces(
                    deployment.Id,
                    contributor,
                    deployment.ChatId,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    tankModels, fighterModels, bomberModels);
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"[DEPLOYMENT JOIN MODEL RESERVATION] {ex.Message}");
                return false;
            }
        }
        finally
        {
            if (locks != null) ReleaseCountryMutationLocks(locks);
            deploymentProcessorLock.Release();
        }
    }

    static async Task CancelDeploymentSafely(Deployment deployment, CancellationToken ct)
    {
        await deploymentProcessorLock.WaitAsync(ct);
        List<SemaphoreSlim>? locks = null;
        try
        {
            var contributorIds = Database.GetDeploymentContributors(deployment.Id)
                .Select(x => x.UserId)
                .Append(deployment.InitiatorId)
                .Append(deployment.TargetUserId);
            locks = await AcquireCountryMutationLocks(deployment.ChatId, contributorIds, ct);
            if (!Database.CancelDeploymentForces(deployment))
                throw new InvalidOperationException("Deployment cancellation ledger validation failed.");
            await UnpinAndDeleteAnnounce(deployment.ChatId, deployment.AnnounceMsgId, ct);
        }
        finally
        {
            if (locks != null)
                ReleaseCountryMutationLocks(locks);
            deploymentProcessorLock.Release();
        }
    }

    // Name similarity check – Levenshtein based, >90% considered too similar
    static int LevenshteinDistance(string s, string t)
    {
        if (string.IsNullOrEmpty(s)) return string.IsNullOrEmpty(t) ? 0 : t.Length;
        if (string.IsNullOrEmpty(t)) return s.Length;
        s = s.ToLowerInvariant().Trim();
        t = t.ToLowerInvariant().Trim();
        int n = s.Length;
        int m = t.Length;
        int[,] d = new int[n + 1, m + 1];
        for (int i = 0; i <= n; i++) d[i, 0] = i;
        for (int j = 0; j <= m; j++) d[0, j] = j;
        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = s[i - 1] == t[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        }
        return d[n, m];
    }

    static double CalculateNameSimilarity(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return 0;
        a = a.Trim().ToLowerInvariant();
        b = b.Trim().ToLowerInvariant();
        if (a == b) return 1.0;
        int maxLen = Math.Max(a.Length, b.Length);
        if (maxLen == 0) return 1.0;
        int distance = LevenshteinDistance(a, b);
        return 1.0 - (double)distance / maxLen;
    }

    static bool IsNameTooSimilar(string newName, IEnumerable<string> existingNames, double threshold = 0.9)
    {
        foreach (var existing in existingNames)
        {
            if (string.IsNullOrWhiteSpace(existing)) continue;
            // Exact match already handled elsewhere, but still consider similar
            double sim = CalculateNameSimilarity(newName, existing);
            if (sim >= threshold) return true;
        }
        return false;
    }

    static void ScheduleDelete(long chatId, int messageId, int seconds = 30)
    {
        if (messageId == 0) return;
        if (chatId == OWNER_ID) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds));
                await bot.DeleteMessageAsync(chatId, messageId);
            }
            catch { }
        });
    }

    static void DeleteNow(long chatId, int messageId)
    {
        if (messageId == 0) return;
        _ = Task.Run(async () =>
        {
            try { await bot.DeleteMessageAsync(chatId, messageId); } catch { }
        });
    }

    // FIX(2): آنپین + حذف پیام اعلام صف‌آرایی
    static async Task UnpinAndDeleteAnnounce(long chatId, int messageId, CancellationToken ct = default)
    {
        if (messageId == 0) return;
        try { await bot.UnpinChatMessageAsync(chatId, messageId, cancellationToken: ct); } catch { }
        try { await bot.DeleteMessageAsync(chatId, messageId, cancellationToken: ct); } catch { }
    }

    static async Task<Message> SendTemp(long chatId, string text, IReplyMarkup? markup = null,
        int? replyTo = null, ParseMode? parseMode = null, CancellationToken ct = default)
    {
        MarkIncomingHandled();
        var m = await bot.SendTextMessageAsync(chatId, text, parseMode: parseMode,
            replyToMessageId: replyTo, replyMarkup: markup, cancellationToken: ct);
        ScheduleDelete(chatId, m.MessageId, 30);
        return m;
    }

    static async Task<Message> SendTempPhoto(long chatId, string fileId, string caption,
        IReplyMarkup? markup = null, ParseMode? parseMode = null, CancellationToken ct = default)
    {
        MarkIncomingHandled();
        Message m;
        try
        {
            m = await bot.SendPhotoAsync(chatId, fileId, caption: caption, parseMode: parseMode,
                replyMarkup: markup, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PHOTO FALLBACK] {ex.Message}");
            m = await bot.SendTextMessageAsync(chatId, caption, parseMode: parseMode,
                replyMarkup: markup, cancellationToken: ct);
        }
        ScheduleDelete(chatId, m.MessageId, 30);
        return m;
    }

    static async Task<Message> SendPermanent(long chatId, string text, IReplyMarkup? markup = null,
        int? replyTo = null, ParseMode? parseMode = null, CancellationToken ct = default)
    {
        MarkIncomingHandled();
        return await bot.SendTextMessageAsync(chatId, text, parseMode: parseMode,
            replyToMessageId: replyTo, replyMarkup: markup, cancellationToken: ct);
    }

    static async Task<Message> SendPermanentPhoto(long chatId, string fileId, string caption,
        IReplyMarkup? markup = null, ParseMode? parseMode = null, CancellationToken ct = default)
    {
        MarkIncomingHandled();
        try
        {
            return await bot.SendPhotoAsync(chatId, fileId, caption: caption, parseMode: parseMode,
                replyMarkup: markup, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PHOTO FALLBACK] {ex.Message}");
            return await bot.SendTextMessageAsync(chatId, caption, parseMode: parseMode,
                replyMarkup: markup, cancellationToken: ct);
        }
    }

    static async Task<Message> SendPrompt(long uid, long chatId, string text, IReplyMarkup? markup = null, CancellationToken ct = default)
    {
        MarkIncomingHandled();
        ClearPromptNow(uid);
        var m = await bot.SendTextMessageAsync(chatId, text, replyMarkup: markup, cancellationToken: ct);
        if (sessions.TryGetValue(uid, out var s))
        {
            s.PromptChatId = chatId;
            s.PromptMsgId = m.MessageId;
        }
        return m;
    }

    static void TrackPrompt(long uid, long chatId, int messageId)
    {
        if (sessions.TryGetValue(uid, out var s))
        {
            s.PromptChatId = chatId;
            s.PromptMsgId = messageId;
        }
    }

    static void ClearPromptNow(long uid)
    {
        if (sessions.TryGetValue(uid, out var s) && s.PromptMsgId != 0)
        {
            DeleteNow(s.PromptChatId, s.PromptMsgId);
            s.PromptMsgId = 0;
        }
    }

    static void EndSession(long uid)
    {
        ClearPromptNow(uid);
        sessions.TryRemove(uid, out _);
    }

    static long SessionGameChatId(UserSession? session)
    {
        if(session==null)return 0;
        foreach(long id in new[]{session.AttackChatId,session.TransferChatId,session.DeployChatId,session.AllianceChatId,session.ChatId})
            if(id<0)return id;
        return 0;
    }

    static long ResolveCallbackGameChatId(long uid,string? data)
    {
        if(sessions.TryGetValue(uid,out var session))
        {
            long fromSession=SessionGameChatId(session);if(fromSession!=0)return fromSession;
        }
        var parts=(data??"").Split(':');
        if(parts.Length>1&&long.TryParse(parts[1],out long parsed)&&parsed<0)return parsed;
        return 0;
    }

    static SemaphoreSlim GetUserLock(long uid) =>
        userLocks.GetOrAdd(uid, _ => new SemaphoreSlim(1, 1));

    static Task<List<SemaphoreSlim>> AcquireCountryMutationLocks(
        long chatId,
        IEnumerable<long> ownerIds,
        CancellationToken ct) =>
        AcquireCountryMutationLocks(ownerIds.Select(ownerId => (chatId, ownerId)), ct);

    static async Task<List<SemaphoreSlim>> AcquireCountryMutationLocks(
        IEnumerable<(long ChatId, long OwnerId)> countryKeys,
        CancellationToken ct)
    {
        var locks = countryKeys
            .Distinct()
            .OrderBy(x => x.ChatId)
            .ThenBy(x => x.OwnerId)
            .Select(key => countryMutationLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1)))
            .ToList();
        var acquired = new List<SemaphoreSlim>(locks.Count);
        try
        {
            foreach (var item in locks)
            {
                if (!await item.WaitAsync(TimeSpan.FromSeconds(30), ct))
                    throw new TimeoutException("Timed out waiting for a country mutation lock.");
                acquired.Add(item);
            }
            return acquired;
        }
        catch
        {
            for (int i = acquired.Count - 1; i >= 0; i--)
                acquired[i].Release();
            throw;
        }
    }

    static void ReleaseCountryMutationLocks(List<SemaphoreSlim> locks)
    {
        for (int i = locks.Count - 1; i >= 0; i--)
            locks[i].Release();
    }

    static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken ct)
    {
        lock (processedLock)
        {
            if (!processedUpdates.Add(update.Id)) return;
            if (processedUpdates.Count > 5000) processedUpdates.Clear();
        }
        if(update.MyChatMember!=null)
        {
            string status=update.MyChatMember.NewChatMember.Status.ToString();
            bool active=status is not ("Left" or "Kicked");
            Database.SetBotGroupActive(update.MyChatMember.Chat.Id,active);
            Console.WriteLine($"[BOT GROUP STATUS] chat={update.MyChatMember.Chat.Id} status={status} active={active}");
            return;
        }
        SpamDecision spamDecision=EvaluateSpam(update);
        if(spamDecision.Kind!=SpamDecisionKind.Allow)
        {
            if(spamDecision.Kind==SpamDecisionKind.Warn)
            {
                long leftSeconds=Math.Max(1,(spamDecision.UntilMs-DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()+999)/1000);
                string warning=$"⛔ سیستم ضداسپم فعال شد. درخواست‌های تکراری موقتاً نادیده گرفته می‌شوند.\n⏱ زمان باقی‌مانده: {leftSeconds} ثانیه";
                try
                {
                    if(update.CallbackQuery!=null)
                        await botClient.AnswerCallbackQueryAsync(update.CallbackQuery.Id,warning,showAlert:true,cancellationToken:ct);
                    else if(update.Message!=null)
                        await botClient.SendTextMessageAsync(update.Message.Chat.Id,warning,cancellationToken:ct);
                }
                catch { }
            }
            return;
        }
        if (Volatile.Read(ref databaseMaintenanceRunning) != 0)
            return;

        Interlocked.Increment(ref activeUpdateHandlers);
        if (Volatile.Read(ref databaseMaintenanceRunning) != 0)
        {
            Interlocked.Decrement(ref activeUpdateHandlers);
            return;
        }
        long updateStartedMs = Environment.TickCount64;
        try
        {
            if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery != null)
            {
                long cbUid = update.CallbackQuery.From.Id;

                var cbChat = update.CallbackQuery.Message?.Chat;
                if (cbChat != null &&
                    (cbChat.Type == ChatType.Group || cbChat.Type == ChatType.Supergroup))
                {
                    Database.MarkGroupActive(cbChat.Id);
                    Database.SetBotGroupActive(cbChat.Id,true);
                    if(!string.IsNullOrWhiteSpace(cbChat.Title))groupTitleCache[cbChat.Id]=cbChat.Title;
                }

                var l = GetUserLock(cbUid);
                if (!await l.WaitAsync(TimeSpan.FromSeconds(30), ct))
                {
                    Console.WriteLine($"[UPDATE LOCK TIMEOUT] callback user={cbUid} data={update.CallbackQuery.Data}");
                    try { await bot.AnswerCallbackQueryAsync(update.CallbackQuery.Id,
                        "⚠️ عملیات قبلی هنوز در حال پردازش است؛ دوباره تلاش کنید.", showAlert: true, cancellationToken: ct); } catch { }
                    return;
                }
                List<SemaphoreSlim>? callbackCountryLocks = null;
                try
                {
                    long gameChatId=ResolveCallbackGameChatId(cbUid,update.CallbackQuery.Data);
                    if(gameChatId!=0&&!Database.IsBotGroupActive(gameChatId))
                    {
                        EndSession(cbUid);
                        try{await bot.AnswerCallbackQueryAsync(update.CallbackQuery.Id,
                            "⛔ ربات دیگر در گروه این کشور حضور ندارد؛ عملیات خصوصی غیرفعال است.",showAlert:true,cancellationToken:ct);}catch{}
                        return;
                    }
                    if (cbChat != null &&
                        (cbChat.Type == ChatType.Group || cbChat.Type == ChatType.Supergroup) &&
                        !(update.CallbackQuery.Data?.StartsWith("dep_", StringComparison.Ordinal) ?? false))
                    {
                        callbackCountryLocks = await AcquireCountryMutationLocks(
                            cbChat.Id,
                            new[] { cbUid },
                            ct);
                    }
                    await HandleCallbackAsync(update.CallbackQuery, ct);
                    Database.MarkPlayerActive(cbUid);
                }
                finally
                {
                    if (callbackCountryLocks != null)
                        ReleaseCountryMutationLocks(callbackCountryLocks);
                    l.Release();
                }
                return;
            }
            if (update.Type != UpdateType.Message || update.Message == null)
                return;
            var msg = update.Message;
            var user = msg.From;
            if (user == null) return;
            incomingCtx.Value = new MsgContext { UserId = user.Id, ChatId = msg.Chat.Id, MessageId = msg.MessageId };
            long uid = user.Id;
            var lk = GetUserLock(uid);
            if (!await lk.WaitAsync(TimeSpan.FromSeconds(30), ct))
            {
                Console.WriteLine($"[UPDATE LOCK TIMEOUT] message user={uid} chat={msg.Chat.Id}");
                try { await SendTemp(msg.Chat.Id, "⚠️ عملیات قبلی هنوز در حال پردازش است؛ کمی بعد دوباره تلاش کنید.", ct: ct); } catch { }
                return;
            }
            try
            {
                bool isPrivate = msg.Chat.Type == ChatType.Private;
                bool isOwner = uid == OWNER_ID;

                if (!isPrivate &&
                    (msg.Chat.Type == ChatType.Group || msg.Chat.Type == ChatType.Supergroup))
                {
                    Database.MarkGroupActive(msg.Chat.Id);
                    Database.SetBotGroupActive(msg.Chat.Id,true);
                    if(!string.IsNullOrWhiteSpace(msg.Chat.Title))groupTitleCache[msg.Chat.Id]=msg.Chat.Title;
                }
                if(isPrivate&&IsPanelAdmin(uid)&&IsSpamReportCommand(msg.Text?.Trim()??""))
                {
                    await SendSpamReport(uid,ct);
                    return;
                }
                if (isPrivate && IsPanelAdmin(uid))
                {
                    bool handledByPanel =
                        await TryHandleAdminPrivateMessageAsync(
                            msg,
                            user,
                            ct
                        );

                    if (handledByPanel)
                        return;
                }

                if (isPrivate && !isOwner) { await HandleUserPrivateAsync(msg, user, ct); return; }
                if (isPrivate && isOwner) { await HandleOwnerPrivateAsync(msg, user, ct); return; }
                await HandleGroupMessageAsync(msg, user, msg.Chat, ct);
            }
            finally { lk.Release(); incomingCtx.Value = null; }
        }
        catch (Exception ex)
        {
            string kind = update.CallbackQuery?.Data ?? update.Message?.Text ?? update.Type.ToString();
            Console.WriteLine($"[UPDATE ERR] update={update.Id} kind={kind}\n{ex}");
        }
        finally
        {
            long elapsedMs = Environment.TickCount64 - updateStartedMs;
            if (elapsedMs >= 5_000)
            {
                string kind = update.CallbackQuery?.Data ?? update.Message?.Text ?? update.Type.ToString();
                Console.WriteLine($"[SLOW UPDATE] update={update.Id} elapsed={elapsedMs}ms kind={kind}");
            }
            Interlocked.Decrement(ref activeUpdateHandlers);
        }
    }

    static Task HandlePollingErrorAsync(ITelegramBotClient bot, Exception ex, CancellationToken ct)
    {
        Console.WriteLine(ex.Message);
        return Task.CompletedTask;
    }

    static bool IsSpamReportCommand(string text)=>text is "گزارش اسپم" or "گزارش ضد اسپم" or "لیست اسپمرها";

    static async Task SendSpamReport(long adminId,CancellationToken ct)
    {
        long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var items=Database.GetSpamRestrictionReport(15);
        if(items.Count==0)
        {
            await SendTemp(adminId,"✅ هنوز هیچ محدودیت ضداسپمی ثبت نشده است.",ct:ct);
            return;
        }
        var lines=new List<string>{"🛡 گزارش ضداسپم"};
        var buttons=new List<InlineKeyboardButton[]>();
        foreach(var item in items)
        {
            string status=item.UntilMs>now?$"فعال — {FormatRemaining(item.UntilMs-now)}": "پایان‌یافته";
            string fingerprint=item.LastFingerprint.Replace('\n',' ');
            if(fingerprint.Length>55)fingerprint=fingerprint[..55]+"…";
            lines.Add($"\n👤 {item.UserId} | گپ {item.ChatId}\nوضعیت: {status} | مرحله {item.Level}\nحذف‌شده: {item.DroppedCount:N0} | علت: {item.Reason}\nآخرین الگو: {fingerprint}");
            buttons.Add(new[]{
                InlineKeyboardButton.WithCallbackData($"✅ رفع {item.UserId}",$"spam_admin:clear:{item.UserId}"),
                InlineKeyboardButton.WithCallbackData("⛔ ۳۰ دقیقه",$"spam_admin:block:{item.UserId}:{item.ChatId}")
            });
        }
        await SendPermanent(adminId,string.Join('\n',lines),new InlineKeyboardMarkup(buttons),ct:ct);
    }

    static async Task HandleSpamAdminCallback(CallbackQuery cb,CancellationToken ct)
    {
        if(cb.Data==null||!IsPanelAdmin(cb.From.Id))
        {
            try{await bot.AnswerCallbackQueryAsync(cb.Id,"⛔ دسترسی ندارید.",showAlert:true,cancellationToken:ct);}catch{}
            return;
        }
        var parts=cb.Data.Split(':');
        if(parts.Length<3||!TryParseLong(parts[2],out long userId))return;
        if(parts[1]=="clear")
        {
            ClearSpamState(userId);
            await bot.AnswerCallbackQueryAsync(cb.Id,"✅ محدودیت پاک شد.",showAlert:true,cancellationToken:ct);
        }
        else if(parts[1]=="block")
        {
            long chatId=parts.Length>3&&TryParseLong(parts[3],out long parsed)?parsed:0;
            RestrictSpamUser(userId,chatId,TimeSpan.FromMinutes(30),"محدودیت دستی مدیر");
            await bot.AnswerCallbackQueryAsync(cb.Id,"⛔ محدودیت ۳۰ دقیقه‌ای اعمال شد.",showAlert:true,cancellationToken:ct);
        }
        if(cb.Message!=null)DeleteNow(cb.Message.Chat.Id,cb.Message.MessageId);
        await SendSpamReport(cb.From.Id,ct);
    }


    static async Task ShowFactionFlags(long uid, string factionStr, string emoji, CancellationToken ct)
    {
        var flags = Database.GetFactionFlags(factionStr);
        sessions[uid] = new UserSession { Step = SessionStep.OwnerWaitingFlagManage, FactionStr = factionStr };
        await SendTemp(uid, $"{emoji} تعداد پرچم ها: {flags.Count}\nبرای حذف، شماره را ارسال کنید؛ برای افزودن، عکس بفرستید.", ct: ct);
        for (int i = 0; i < flags.Count; i++)
            await SendTempPhoto(uid, flags[i], $"شماره {i + 1}", ct: ct);
    }

    // ============================================================
    //  Group message handler
    // ============================================================

    // ============================================================
    //  Private message handlers
    // ============================================================
    static async Task SendStartMessage(long uid, CancellationToken ct)
    {
        // FIX(3): پیام خوش‌آمد/استارت در پیوی — تا کاربر فکر نکند بات خاموش است
        string startText =
            "👋 سلام! ربات «آلیس» روشن و فعال است ✅\n\n" +
            "🎮 این یک بازی استراتژیک جنگ جهانی است که <b>فقط داخل گروه‌ها</b> اجرا می‌شود.\n" +
            "برای بازی، ربات را به گروه خود اضافه کنید و در همان‌جا دستورها را بنویسید.\n\n" +
            "📌 دستورهای بازی (مثل «انتخاب کشور»، «دارایی»، «حمله» و ...) را باید <b>در گروه</b> بفرستید؛ " +
            "بعضی مراحل (حمله، ترنسفر، صف‌آرایی، وضعیت دفاع) به‌طور خودکار برای تنظیم دقیق به همین پیوی هدایت می‌شوند.\n\n" +
            "ℹ️ برای دیدن فهرست کامل دستورها و توضیح هرکدام، همین‌جا در پیوی بنویسید: <b>راهنما</b>\n" +
            "(دستور «راهنما» هم در گروه و هم در پیوی کار می‌کند.)";
        await SendPermanent(uid, startText, parseMode: ParseMode.Html, ct: ct);
    }

    static bool IsNavalCancellationCommand(string text) =>
        text is "لغو لشکر کشی دریایی" or "لغو لشکرکشی دریایی" or
            "لغو لشکرکشی دریائی" or "لغو عملیات دریایی" or "بازگشت ناوگان";
    static bool IsOngoingBattlesCommand(string text) =>
        text is "لیست نبرد های در جریان" or "لیست نبردهای در جریان" or
            "لیست نبرد‌های در جریان" or "نبرد های در جریان" or "نبردهای در جریان" or "نبرد‌های در جریان";

    static async Task ShowOngoingBattles(long uid,CancellationToken ct,long? onlyChatId=null)
    {
        Console.WriteLine($"[SIEGE INTEGRITY] {Database.RepairSiegeIntegrity()}");
        var lines=new List<string>{"⚔️ لیست نبردهای در جریان","فقط پیروزی‌های سنگین برای فتح شهر شمرده می‌شوند."};
        int shown=0;
        var chatIds=Database.GetUserActiveChatIds(uid);
        if(onlyChatId.HasValue)chatIds=chatIds.Where(x=>x==onlyChatId.Value).ToList();
        foreach(long chatId in chatIds)
        {
            var progress=Database.GetRoutBattleProgress(uid,chatId);
            if(progress.Count==0)continue;
            string title=await GetGroupTitleCached(chatId,ct);
            lines.Add($"\n💬 {title}");
            foreach(var battle in progress)
            {
                int remaining=Math.Max(0,5-battle.Count);
                if(battle.AttackerId==uid)
                    lines.Add($"🟢 علیه {battle.DefenderName}: {battle.Count}/5 — {remaining} پیروزی سنگین تا گرفتن یک شهر");
                else
                    lines.Add($"🔴 مقابل {battle.AttackerName}: {battle.Count}/5 — {remaining} شکست سنگین تا از دست دادن یک شهر");
                shown++;
            }
        }
        if(shown==0)lines.Add("\n✅ در حال حاضر هیچ نبردی با پیشرفت فتح شهر ندارید.");
        await SendPermanent(uid,string.Join('\n',lines),ct:ct);
    }

    static async Task ShowNavalCancellationMenu(long uid,CancellationToken ct)
    {
        var operations=Database.GetUserActiveChatIds(uid)
            .SelectMany(chatId=>Database.GetActiveNavalInvasionsByAttacker(uid,chatId))
            .Where(x=>x.Processed==0)
            .OrderBy(x=>x.ArriveAtMs)
            .ToList();
        if(operations.Count==0)
        {
            await SendTemp(uid,"❌ هیچ لشکرکشی دریایی فعال و قابل لغو ندارید.",ct:ct);
            return;
        }
        var lines=new List<string>
        {
            "↩️ لغو لشکرکشی دریایی",
            "با زدن دکمه، همان لحظه کل ناوگان آن عملیات بدون تلفات برمی‌گردد."
        };
        var buttons=new List<InlineKeyboardButton[]>();
        foreach(var op in operations)
        {
            string title=await GetGroupTitleCached(op.ChatId,ct);
            long left=Math.Max(0,op.ArriveAtMs-DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            lines.Add($"\n#{op.Id} | گپ: {title}\n🎯 {op.DefenderName} | ⏱ {FormatRemaining(left)}\n🚤 {op.Boats:N0} | ⚓ {op.Submarines:N0} | 🚢 {op.Battleships:N0}");
            buttons.Add(new[]{InlineKeyboardButton.WithCallbackData(
                $"↩️ لغو #{op.Id} — {op.DefenderName}",$"naval_cancel:{op.ChatId}:{op.Id}")});
        }
        await SendPermanent(uid,string.Join('\n',lines),new InlineKeyboardMarkup(buttons),ct:ct);
    }


    // ============================================================
    //  Callback handlers
    // ============================================================
}
