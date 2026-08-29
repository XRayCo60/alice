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
    static string GroundAttackStrategyName(int strategy) =>
        strategy == 1
            ? "هجوم منسجم"
            : "محاصره و ضربه";

    static string GroundAttackTacticName(int strategy, int tactic) =>
        (strategy, tactic) switch
        {
            (1, 1) => "حمله مستقیم به قلب خط دفاع",
            (1, 2) => "حملات سبک هدف‌دار و هجوم سنگین متمرکز",
            (2, 1) => "حلقه محاصره با حملات پراکنده و هجوم سریع",
            (2, 2) => "حلقه محاصره متحرک و ضربات سنگین",
            _ => "تاکتیک نامشخص"
        };

    static readonly string GroundAttackStrategyGuide = """
⚔️ انتخاب استراتژی حمله زمینی

1️⃣ هجوم منسجم
نیروهای مهاجم به‌شکل منظم و متمرکز وارد نبرد می‌شوند تا با ایجاد فشار مستقیم، خط دفاع دشمن را بشکنند.

2️⃣ محاصره و ضربه
نیروها برای محدود کردن تحرک و ارتباط دشمن، خطوط دفاعی را محاصره می‌کنند و سپس با ضربات هماهنگ آن‌ها را فرسوده و نابود می‌کنند.
""";

    static string GroundAttackTacticGuide(int strategy)
    {
        if (strategy == 1)
        {
            return """
⚔️ استراتژی: هجوم منسجم

1️⃣ حمله مستقیم به قلب خط دفاع
تمام سربازان و تانک‌ها در یک نقطه متمرکز می‌شوند و در قالب چند واحد منظم پیشروی می‌کنند. هدف، درگیری مستقیم و شکستن خطوط غیرمتمرکز دشمن با ضربات سنگین است.

2️⃣ حملات سبک هدف‌دار و هجوم سنگین متمرکز
نیروها تقسیم می‌شوند. گروه‌های سبک با حملات هدف‌دار نظم دشمن را برهم می‌زنند و نقاط ضعف را آشکار می‌کنند؛ سپس ارتش اصلی به‌صورت متمرکز هجوم می‌برد و خط دفاع را می‌شکند.
""";
        }

        return """
⚔️ استراتژی: محاصره و ضربه

1️⃣ حلقه محاصره با حملات پراکنده و هجوم سریع
خطوط دفاعی دشمن کاملاً محاصره می‌شوند تا قدرت تحرک آن‌ها کاهش یابد. حملات پراکنده دشمن را فرسوده می‌کند و در پایان، هجوم سریع خطوط نامنظم را درهم می‌شکند.

2️⃣ حلقه محاصره متحرک و ضربات سنگین
دشمن در حلقه‌ای بزرگ گرفتار و ارتباطش با بیرون قطع می‌شود. ارتش از تمام جهات، آهسته اما هماهنگ پیشروی می‌کند و با ضربات سنگین گروه‌های کوچک را حذف و نیروهای باقی‌مانده را متراکم و بی‌حرکت می‌کند.
""";
    }

    static string AirAttackStrategyName(int strategy) =>
        strategy == 1
            ? "برتری هوایی"
            : "بمباران راهبردی";

    static string AirAttackTacticName(int strategy, int tactic) =>
        (strategy, tactic) switch
        {
            (1, 1) => "شکار آزاد (Freie Jagd)",
            (1, 2) => "حمله به پایگاه‌ها (Counter-air Strike)",
            (2, 1) => "بمباران دقیق (Precision Bombing)",
            (2, 2) => "بمباران منطقه‌ای (Area Bombing)",
            _ => "تاکتیک نامشخص"
        };

    static readonly string AirAttackStrategyGuide = """
🛫 انتخاب استراتژی حمله هوایی

1️⃣ برتری هوایی (Air Superiority)
هدف، از بین بردن توان هوایی دشمن و به‌دست گرفتن کنترل آسمان است.

2️⃣ بمباران راهبردی (Strategic Bombing)
هدف، تضعیف توان اقتصادی، صنعتی و روحیه دشمن با حمله به اهداف مهم در عمق قلمرو اوست.
""";

    static string AirAttackTacticGuide(int strategy)
    {
        if (strategy == 1)
        {
            return """
🛫 استراتژی: برتری هوایی

1️⃣ شکار آزاد (Freie Jagd)
جنگنده‌ها به‌صورت مستقل یا در گروه‌های کوچک به پشت خطوط دشمن نفوذ می‌کنند و هواپیماهای در حال پرواز، شامل جنگنده‌ها، بمب‌افکن‌ها و هواپیماهای شناسایی را هدف می‌گیرند.

2️⃣ حمله به پایگاه‌ها (Counter-air Strike)
فرودگاه‌ها، آشیانه‌ها، برج‌های مراقبت و انبارهای سوخت دشمن به‌شکل غافلگیرانه بمباران می‌شوند تا هواپیماهای دشمن پیش از برخاستن، روی زمین منهدم شوند.
""";
        }

        return """
🛫 استراتژی: بمباران راهبردی

1️⃣ بمباران دقیق (Precision Bombing)
اهداف کوچک و حیاتی مانند کارخانه‌های تسلیحات، پالایشگاه‌ها و ایستگاه‌های راه‌آهن انتخاب می‌شوند و از ارتفاع متوسط، با تمرکز بالا بمباران می‌شوند.

2️⃣ بمباران منطقه‌ای (Area Bombing)
گروه بزرگی از بمب‌افکن‌ها یک منطقه وسیع، مانند شهر یا منطقه صنعتی، را هدف می‌گیرند تا زیرساخت‌ها به‌طور گسترده تخریب و روحیه دشمن تضعیف شود.
""";
    }
}
