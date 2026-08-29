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
    static string GroundDefenseStrategyName(int strategy) =>
        strategy == 1
            ? "دفاع منسجم"
            : "دفاع و ضدحمله پراکنده";

    static string GroundDefenseTacticName(int strategy, int tactic) =>
        (strategy, tactic) switch
        {
            (1, 1) => "دفاع ایستا و ثابت با قوای زرهی",
            (1, 2) => "گشت متحرک با گروه‌های ترکیبی",
            (2, 1) => "استتار و ضربه به گروه‌های پیشرو",
            (2, 2) => "عقب‌نشینی تاکتیکی و تله‌گذاری مخفی",
            _ => "تاکتیک نامشخص"
        };

    static readonly string GroundDefenseStrategyGuide = """
🛡 انتخاب استراتژی دفاع زمینی

1️⃣ دفاع منسجم
نیروهای مدافع در یک ساختار هماهنگ و نسبتاً ثابت مستقر می‌شوند تا خط دفاعی قدرتمندی ایجاد کنند و مانع نفوذ مستقیم دشمن شوند.

2️⃣ دفاع و ضدحمله پراکنده
نیروها با استتار، پراکندگی و عقب‌نشینی حساب‌شده، مهاجم را به عمق منطقه می‌کشانند و سپس با ضدحمله و محاصره به او ضربه می‌زنند.
""";

    static string GroundDefenseTacticGuide(int strategy)
    {
        if (strategy == 1)
        {
            return """
🛡 استراتژی: دفاع منسجم

1️⃣ دفاع ایستا و ثابت با قوای زرهی
سربازان در سنگرها و پشت موانع طبیعی مستقر می‌شوند و تانک‌ها در خط اول قرار می‌گیرند تا با آتش مستقیم، حرکت مهاجم را متوقف کنند.

2️⃣ گشت متحرک با گروه‌های ترکیبی
گروه‌های کوچک ترکیبی، متشکل از تانک و سرباز، به‌طور مداوم در خط مقدم حرکت می‌کنند تا نیروهای پراکنده مهاجم را شناسایی و هدف قرار دهند.
""";
        }

        return """
🛡 استراتژی: دفاع و ضدحمله پراکنده

1️⃣ استتار و ضربه به گروه‌های پیشرو
سربازان در بوته‌زارها، خرابه‌ها یا پشت تپه‌ها مخفی می‌شوند و تانک‌ها در سنگرهای پنهان و ثابت قرار می‌گیرند تا نیروی پیشرو دشمن غافلگیر شود و ضربه سنگینی دریافت کند.

2️⃣ عقب‌نشینی تاکتیکی و تله‌گذاری مخفی
بخشی از خطوط دفاعی عمداً خالی گذاشته می‌شود تا دشمن وارد عمق منطقه شود. سپس مسیرهای ارتباطی او مسدود و واحدهای مهاجم در محاصره و تله‌های مختلف گرفتار می‌شوند.
""";
    }

    static string AirDefenseStrategyName(int strategy) =>
        strategy == 1
            ? "دفاع منطقه‌ای (Area Defense)"
            : "دفاع نقطه‌ای (Point Defense)";

    static string AirDefenseTacticName(int strategy, int tactic) =>
        (strategy, tactic) switch
        {
            (1, 1) => "گشت هوایی رزمی (CAP)",
            (1, 2) => "ایستگاه‌های شنود و هشدار سریع",
            (2, 1) => "آتشبند (Flak Barrage)",
            (2, 2) => "پوشش مستقیم جنگنده (Close Escort)",
            _ => "تاکتیک نامشخص"
        };

    static readonly string AirDefenseStrategyGuide = """
🛫 انتخاب استراتژی دفاع هوایی

1️⃣ دفاع منطقه‌ای (Area Defense)
هدف، حفاظت از یک منطقه وسیع مانند کشور یا جبهه بزرگ با پراکندگی نیروها و رهگیری تهدیدها پیش از رسیدن به اهداف حساس است.

2️⃣ دفاع نقطه‌ای (Point Defense)
تمرکز نیروهای دفاعی بر حفاظت از اهداف حیاتی و محدود مانند شهرها، کارخانه‌ها، پایگاه‌ها و تأسیسات مهم است.
""";

    static string AirDefenseTacticGuide(int strategy)
    {
        if (strategy == 1)
        {
            return """
🛫 استراتژی: دفاع منطقه‌ای

1️⃣ گشت هوایی رزمی (CAP)
جنگنده‌ها به‌طور مداوم در آسمان منطقه گشت می‌زنند تا هواپیماهای دشمن را پیش از رسیدن به اهداف حساس شناسایی و رهگیری کنند.

2️⃣ ایستگاه‌های شنود و هشدار سریع 🔒
رادارهای زمینی و تجهیزات شنود، حرکت دشمن را کشف می‌کنند و جنگنده‌ها را به سمت تهدید هدایت می‌کنند.

این تاکتیک در وضعیت فعلی آلیس به رادار نیاز دارد و قفل است.
""";
        }

        return """
🛫 استراتژی: دفاع نقطه‌ای

1️⃣ آتشبند (Flak Barrage)
توپ‌های ضدهوایی به‌صورت متراکم در اطراف هدف مستقر می‌شوند و با آتش متوالی و سنگین، مسیر پرواز هواپیماهای دشمن را مسدود می‌کنند.

2️⃣ پوشش مستقیم جنگنده (Close Escort)
جنگنده‌های دفاعی در مجاورت هدف حیاتی، مانند کارخانه یا پایگاه، گشت می‌زنند و در لحظه حمله مستقیماً وارد درگیری می‌شوند.
""";
    }
}
