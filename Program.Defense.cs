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
{    static async Task SendDefenseStatus(
        long sendTo,
        long ownerId,
        long chatId,
        CancellationToken ct)
    {
        Database.ReconcileDefense(ownerId, chatId);

        var country = Database.GetCountry(ownerId, chatId);

        if (country == null)
        {
            await SendTemp(
                sendTo,
                "❌ کشور یافت نشد.",
                ct: ct
            );
            return;
        }

        long minimumTanks =
            (long)Math.Ceiling(country.Tanks * 0.2);

        long minimumSoldiers =
            (long)Math.Ceiling(country.Soldiers * 0.2);

        string groundStrategy =
            GroundDefenseStrategyName(
                country.DefenseStrategy
            );

        string groundTactic =
            GroundDefenseTacticName(
                country.DefenseStrategy,
                country.DefenseTactic
            );

        string airStrategy =
            AirDefenseStrategyName(
                country.AirDefStrategy
            );

        string airTactic =
            AirDefenseTacticName(
                country.AirDefStrategy,
                country.AirDefTactic
            );

        // per-model defense breakdown – including naval
        var tankBreakdown = GetExactDefenseBreakdown(country, "tanks");
        var planeBreakdown = GetExactDefenseBreakdown(country, "planes");
        var boatInventory = Database.GetEquipmentBreakdownForReconcile(country, "boats");
        var subInventory = Database.GetEquipmentBreakdownForReconcile(country, "submarines");
        var bsInventory = Database.GetEquipmentBreakdownForReconcile(country, "battleships");
        var boatDefense = Database.GetNavalDefenseModels(country, "boats").ToDictionary(x=>x.Model,x=>x.Count,StringComparer.OrdinalIgnoreCase);
        var subDefense = Database.GetNavalDefenseModels(country, "submarines").ToDictionary(x=>x.Model,x=>x.Count,StringComparer.OrdinalIgnoreCase);
        var bsDefense = Database.GetNavalDefenseModels(country, "battleships").ToDictionary(x=>x.Model,x=>x.Count,StringComparer.OrdinalIgnoreCase);

        var sbDef = new StringBuilder();
        sbDef.AppendLine($"🛡 نیروهای مستقر در دفاع (جزئی per-model + دریایی):");
        if (tankBreakdown.Count > 0)
        {
            sbDef.AppendLine("🛡 تانک‌ها:");
            foreach (var (model, count, defCount, minimum) in tankBreakdown)
                sbDef.AppendLine($"  • {model}: دفاع {defCount:N0} از {count:N0}" +
                    (minimum > 0 ? $" | اجباری {minimum:N0}" : ""));
        }
        else
        {
            sbDef.AppendLine($"🛡 تانک: {country.DefenseTanks:N0} | حداقل: {minimumTanks:N0}");
        }
        sbDef.AppendLine($"🪖 سرباز: {country.DefenseSoldiers:N0} | حداقل: {minimumSoldiers:N0}");
        if (planeBreakdown.Count > 0)
        {
            sbDef.AppendLine("✈️ جنگنده‌ها:");
            foreach (var (model, count, defCount, minimum) in planeBreakdown)
                sbDef.AppendLine($"  • {model}: دفاع {defCount:N0} از {count:N0}" +
                    (minimum > 0 ? $" | اجباری {minimum:N0}" : ""));
        }
        else
        {
            sbDef.AppendLine($"✈️ جنگنده: {country.DefenseFighters:N0}");
        }
        sbDef.AppendLine($"🎯 پدافند: {country.AntiAir:N0}");

        // Naval defense — exact per model, with a compulsory 20% reserve.
        var navalOrders=Database.GetNavalDefenseOrders(country.OwnerId,country.ChatId);
        string navalDoctrine=(navalOrders.Strategy,navalOrders.Tactic) switch
        {(1,1)=>"استحکامات، توپخانه ساحلی و میدان مین",(1,2)=>"خروج سریع و ضدحمله",(2,1)=>"حمله و عقب‌نشینی",_=>"کمین دریایی"};
        sbDef.AppendLine($"⚓ دکترین دریایی: {navalDoctrine}");
        sbDef.AppendLine("🚤 قایق‌ها:");
        foreach(var x in boatInventory)sbDef.AppendLine($"  • {x.ModelName}: دفاع {boatDefense.GetValueOrDefault(x.ModelName):N0} از {x.Count:N0}");
        sbDef.AppendLine("⚓ زیردریایی‌ها:");
        foreach(var x in subInventory)sbDef.AppendLine($"  • {x.ModelName}: دفاع {subDefense.GetValueOrDefault(x.ModelName):N0} از {x.Count:N0}");
        sbDef.AppendLine("🚢 نبردناوها:");
        foreach(var x in bsInventory)sbDef.AppendLine($"  • {x.ModelName}: دفاع {bsDefense.GetValueOrDefault(x.ModelName):N0} از {x.Count:N0}");

        string text =
            $"🛡 وضعیت دفاع {country.Name}\n\n" +

            "⚔️ دفاع زمینی\n" +
            $"استراتژی: {groundStrategy}\n" +
            $"تاکتیک: {groundTactic}\n\n" +

            "🛫 دفاع هوایی\n" +
            $"استراتژی: {airStrategy}\n" +
            $"تاکتیک: {airTactic}\n\n" +

            "⚓ دفاع دریایی\n" +
            $"بندر سطح: {country.PortLevel}\n\n" +

            sbDef.ToString() + "\n" +

            "📊 کل موجودی کشور\n" +
            $"تانک: {country.Tanks:N0} | سرباز: {country.Soldiers:N0} | جنگنده: {country.Planes:N0}\n" +
            BuildNavalInventorySummary(country);

        bool isPrivate = sendTo == ownerId;

        if (isPrivate)
        {
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        "⚔️ تاکتیک زمینی",
                        $"defense_tactic:{chatId}"
                    )
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        "🛫 دفاع هوایی",
                        $"airdef_strategy:{chatId}"
                    )
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("⚓ دفاع دریایی", $"naval_defense:{chatId}"),
                    InlineKeyboardButton.WithCallbackData("⚙️ انتخاب نیروی زمینی", $"defense_set:{chatId}")
                }
            });

            await SendTemp(
                sendTo,
                text,
                markup: keyboard,
                ct: ct
            );
        }
        else
        {
            await SendTemp(
                sendTo,
                text + "\n\n⚙️ برای تنظیم به پیوی آلیس بروید.",
                ct: ct
            );
        }
    }

    static async Task HandleDefenseStatusCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length < 2) return;
        long uid = cb.From.Id;
        if (!TryParseLong(parts[1], out long cid)) return;
        await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
        EndSession(uid);
        await SendDefenseStatus(uid, uid, cid, ct);
    }

    static readonly int[] DefensePercents = { 20, 30, 40, 50, 60, 70, 80, 90, 100 };
    static InlineKeyboardMarkup BuildPercentKeyboard(string kind, long chatId)
    {
        var rows = new List<InlineKeyboardButton[]>();
        for (int i = 0; i < DefensePercents.Length; i += 2)
        {
            var row = new List<InlineKeyboardButton> { InlineKeyboardButton.WithCallbackData($"{DefensePercents[i]}%", $"defense_pct:{chatId}:{kind}:{DefensePercents[i]}") };
            if (i + 1 < DefensePercents.Length) row.Add(InlineKeyboardButton.WithCallbackData($"{DefensePercents[i + 1]}%", $"defense_pct:{chatId}:{kind}:{DefensePercents[i + 1]}"));
            rows.Add(row.ToArray());
        }
        rows.Add(new[] { InlineKeyboardButton.WithCallbackData("❌ انصراف", "cancel") });
        return new InlineKeyboardMarkup(rows);
    }

    static InlineKeyboardMarkup BuildModelPercentKeyboard(long chatId, string category, int modelIndex)
    {
        var rows = new List<InlineKeyboardButton[]>();
        for (int i = 0; i < DefensePercents.Length; i += 2)
        {
            var row = new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData($"{DefensePercents[i]}%", $"defense_model_pct:{chatId}:{category}:{modelIndex}:{DefensePercents[i]}")
            };
            if (i + 1 < DefensePercents.Length)
                row.Add(InlineKeyboardButton.WithCallbackData($"{DefensePercents[i + 1]}%", $"defense_model_pct:{chatId}:{category}:{modelIndex}:{DefensePercents[i + 1]}"));
            rows.Add(row.ToArray());
        }
        rows.Add(new[] { InlineKeyboardButton.WithCallbackData("❌ انصراف", "cancel") });
        return new InlineKeyboardMarkup(rows);
    }

    static async Task HandleNavalDefenseCallback(CallbackQuery cb,string[] parts,CancellationToken ct)
    {
        if(parts.Length<2||cb.Message==null||!TryParseLong(parts[1],out long cid))return;
        var kb=new InlineKeyboardMarkup(new[]{
            new[]{InlineKeyboardButton.WithCallbackData("🏰 دفاع از پایگاه دریایی",$"naval_defense_strategy:{cid}:1")},
            new[]{InlineKeyboardButton.WithCallbackData("🌊 جنگ نامتقارن و فرسایشی",$"naval_defense_strategy:{cid}:2")}});
        await bot.AnswerCallbackQueryAsync(cb.Id,cancellationToken:ct);
        await bot.EditMessageTextAsync(cb.Message.Chat.Id,cb.Message.MessageId,"⚓ استراتژی دفاع دریایی را انتخاب کنید:",replyMarkup:kb,cancellationToken:ct);
    }
    static async Task HandleNavalDefenseStrategyCallback(CallbackQuery cb,string[] parts,CancellationToken ct)
    {
        if(parts.Length<3||cb.Message==null||!TryParseLong(parts[1],out long cid)||!TryParseInt(parts[2],out int strategy))return;
        var kb=strategy==1?new InlineKeyboardMarkup(new[]{
            new[]{InlineKeyboardButton.WithCallbackData("🧱 استحکامات، توپخانه و میدان مین",$"naval_defense_tactic:{cid}:1:1")},
            new[]{InlineKeyboardButton.WithCallbackData("⚡ خروج سریع و ضدحمله",$"naval_defense_tactic:{cid}:1:2")}}):
            new InlineKeyboardMarkup(new[]{
            new[]{InlineKeyboardButton.WithCallbackData("🏃 حمله و عقب‌نشینی",$"naval_defense_tactic:{cid}:2:1")},
            new[]{InlineKeyboardButton.WithCallbackData("🐋 کمین دریایی",$"naval_defense_tactic:{cid}:2:2")}});
        await bot.AnswerCallbackQueryAsync(cb.Id,cancellationToken:ct);
        await bot.EditMessageTextAsync(cb.Message.Chat.Id,cb.Message.MessageId,"⚓ تاکتیک دفاع دریایی را انتخاب کنید:",replyMarkup:kb,cancellationToken:ct);
    }
    static async Task HandleNavalDefenseTacticCallback(CallbackQuery cb,string[] parts,CancellationToken ct)
    {
        if(parts.Length<4||!TryParseLong(parts[1],out long cid)||!TryParseInt(parts[2],out int strategy)||!TryParseInt(parts[3],out int tactic))return;
        long uid=cb.From.Id;var country=Database.GetCountry(uid,cid);
        if(country==null){await bot.AnswerCallbackQueryAsync(cb.Id,"❌ کشور یافت نشد.",showAlert:true,cancellationToken:ct);return;}
        Database.SetNavalDefenseOrders(uid,cid,strategy,tactic);
        await bot.AnswerCallbackQueryAsync(cb.Id,"✅ تاکتیک ذخیره شد؛ حالا تعداد دقیق مدل‌ها را تعیین کنید.",cancellationToken:ct);
        if(cb.Message!=null)DeleteNow(cb.Message.Chat.Id,cb.Message.MessageId);
        var session=new UserSession{AttackChatId=cid};sessions[uid]=session;
        await BeginNavalDefenseCategory(uid,session,country,"boats",ct);
    }
    static async Task BeginNavalDefenseCategory(long uid,UserSession sess,Country country,string resource,CancellationToken ct)
    {
        var inventory=Database.GetEquipmentBreakdownForReconcile(country,resource);
        if(resource=="battleships")
        {
            Database.SyncBattleshipUnits(country.OwnerId,country.ChatId);
            var ready=Database.GetBattleshipUnits(country.OwnerId,country.ChatId,true)
                .GroupBy(x=>x.Model,StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x=>x.Key,x=>(long)x.Count(),StringComparer.OrdinalIgnoreCase);
            inventory=inventory.Select(x=>(x.ModelName,Count:Math.Min(x.Count,ready.GetValueOrDefault(x.ModelName))))
                .Where(x=>x.Count>0).ToList();
        }
        string category=resource=="boats"?"Boats":resource=="submarines"?"Submarines":"Battleships";
        if(inventory.Count==0)
        {
            Database.ReplaceNavalDefenseModels(uid,country.ChatId,category,new Dictionary<string,long>());
            if(resource=="boats"){await BeginNavalDefenseCategory(uid,sess,country,"submarines",ct);return;}
            if(resource=="submarines"){await BeginNavalDefenseCategory(uid,sess,country,"battleships",ct);return;}
            EndSession(uid);await SendTemp(uid,"✅ دفاع دریایی ذخیره شد.",ct:ct);return;
        }
        string defaultModel=resource=="boats"?Database.GetDefaultBoatModel(country.Faction):resource=="submarines"?Database.GetDefaultSubModel(country.Faction):Database.GetDefaultBattleshipModel(country.Faction);
        long mandatory=(long)Math.Ceiling(inventory.Sum(x=>x.Count)*0.20);
        long[] minimums=AllocateModelPriority(inventory,defaultModel,mandatory);
        var current=Database.GetNavalDefenseModels(country,resource).ToDictionary(x=>x.Model,x=>x.Count,StringComparer.OrdinalIgnoreCase);
        sess.DefenseModelNames=inventory.Select(x=>x.ModelName).ToList();sess.DefenseModelCounts=inventory.Select(x=>x.Count).ToList();
        sess.DefenseModelMinimums=minimums.ToList();sess.DefenseModelAmounts=inventory.Select((x,i)=>Math.Max(current.GetValueOrDefault(x.ModelName),minimums[i])).ToList();
        sess.DefenseModelIndex=0;sess.DefenseCurrentCategory=resource;
        sess.Step=resource=="boats"?SessionStep.NavalDefenseWaitingBoatModel:resource=="submarines"?SessionStep.NavalDefenseWaitingSubmarineModel:SessionStep.NavalDefenseWaitingBattleshipModel;
        await SendPrompt(uid,uid,$"⚓ دفاع {category} — مدل 1/{inventory.Count}\n🔧 {inventory[0].ModelName}\n📊 موجودی: {inventory[0].Count:N0}\n🔒 حداقل اجباری: {minimums[0]:N0}\nتعداد دقیق دفاع را وارد کنید:",ct:ct);
    }

    static async Task HandleDefenseSetCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length < 2 || cb.Message == null) return;
        long uid = cb.From.Id;
        if (!TryParseLong(parts[1], out long cid)) return;
        var c = Database.GetCountry(uid, cid);
        if (c == null) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ کشور نیست!", cancellationToken: ct); return; }
        await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);

        // Ground/air defense is configured as exact per-model amounts. The compulsory
        // tank reserve is 20% overall, allocated to the domestic factory model first.
        var tankBreakdown = GetExactDefenseBreakdown(c, "tanks");
        if (tankBreakdown.Count == 0)
        {
            await bot.EditMessageTextAsync(cb.Message.Chat.Id, cb.Message.MessageId,
                $"🪖 درصد سرباز:\nکل: {c.Soldiers:N0}",
                replyMarkup: BuildPercentKeyboard("soldier", cid), cancellationToken: ct);
            sessions[uid] = new UserSession
                { Step = SessionStep.DefenseWaitingSoldiers, AttackChatId = cid, DefenseTanks = 0, DefTankPct = 100 };
            return;
        }

        var sess = new UserSession
        {
            Step = SessionStep.DefenseWaitingTankModel,
            AttackChatId = cid,
            DefenseCurrentCategory = "tanks",
            DefenseModelNames = tankBreakdown.Select(x => x.ModelName).ToList(),
            DefenseModelCounts = tankBreakdown.Select(x => x.Count).ToList(),
            DefenseModelAmounts = tankBreakdown.Select(x => x.DefenseCount).ToList(),
            DefenseModelMinimums = tankBreakdown.Select(x => x.MinimumCount).ToList(),
            DefenseModelIndex = 0
        };
        sessions[uid] = sess;

        var first = tankBreakdown[0];
        string msg = $"🛡 دفاع تانک – مدل 1/{tankBreakdown.Count}\n\n🔧 مدل: {first.ModelName}\n📊 موجودی: {first.Count:N0}\n🛡 مقدار فعلی دفاع: {first.DefenseCount:N0}\n🔒 حداقل اجباری این مدل: {first.MinimumCount:N0}\n\nتعداد دقیق این مدل در دفاع را وارد کنید:";
        DeleteNow(cb.Message.Chat.Id, cb.Message.MessageId);
        await SendPrompt(uid, uid, msg, ct: ct);
    }

    static async Task HandleDefensePctCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length < 4 || cb.Message == null) return;
        long uid = cb.From.Id;
        if (!TryParseLong(parts[1], out long cid) || !TryParseInt(parts[3], out int pct)) return;
        string kind = parts[2];
        var c = Database.GetCountry(uid, cid);
        if (c == null) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌", cancellationToken: ct); return; }
        if (kind == "tank")
        {
            long dt = (long)Math.Ceiling(c.Tanks * (pct / 100.0));
            sessions[uid] = new UserSession { Step = SessionStep.DefenseWaitingSoldiers, AttackChatId = cid, DefenseTanks = dt, DefTankPct = pct };
            await bot.AnswerCallbackQueryAsync(cb.Id, $"🛡 {pct}%", cancellationToken: ct);
            await bot.EditMessageTextAsync(cb.Message.Chat.Id, cb.Message.MessageId, $"🪖 درصد سرباز:\nکل: {c.Soldiers}", replyMarkup: BuildPercentKeyboard("soldier", cid), cancellationToken: ct);
            return;
        }
        if (kind == "soldier")
        {
            long defT = c.DefenseTanks; int dtp = 100;
            if (sessions.TryGetValue(uid, out var s) && s != null && s.AttackChatId == cid) { defT = s.DefenseTanks; dtp = s.DefTankPct > 0 ? s.DefTankPct : 100; }

            long ds = (long)Math.Ceiling(c.Soldiers * (pct / 100.0));
            var planeBreakdown = GetExactDefenseBreakdown(c, "planes");
            var currentSession = sessions.TryGetValue(uid, out var existing) && existing != null
                ? existing : new UserSession();
            if (planeBreakdown.Count == 0)
            {
                var tankMap = Enumerable.Range(0, currentSession.DefenseTankModelNamesFinal.Count)
                    .Where(i => i < currentSession.DefenseTankModelAmountsFinal.Count && currentSession.DefenseTankModelAmountsFinal[i] > 0)
                    .ToDictionary(i => currentSession.DefenseTankModelNamesFinal[i],
                        i => currentSession.DefenseTankModelAmountsFinal[i], StringComparer.OrdinalIgnoreCase);
                Database.ReplaceDefenseModelAmounts(uid, cid, "Tanks", tankMap);
                Database.ReplaceDefenseModelAmounts(uid, cid, "Planes", new Dictionary<string, long>());
                Database.SetDefenseSoldierConfigured(uid,cid,true);
                Database.UpdateDefenseFull(uid, cid, defT, ds, 0, c.DefenseStrategy, c.DefenseTactic, 100, pct, 100);
                EndSession(uid);
                await bot.AnswerCallbackQueryAsync(cb.Id, $"🪖 {pct}% – ذخیره شد.", cancellationToken: ct);
                DeleteNow(cb.Message.Chat.Id, cb.Message.MessageId);
                await SendDefenseStatus(uid, uid, cid, ct);
                return;
            }

            currentSession.Step = SessionStep.DefenseWaitingPlaneModel;
            currentSession.AttackChatId = cid;
            currentSession.DefenseTanks = defT;
            currentSession.DefenseSoldiers = ds;
            currentSession.DefTankPct = 100;
            currentSession.DefSoldierPct = pct;
            currentSession.DefenseCurrentCategory = "planes";
            currentSession.DefenseModelNames = planeBreakdown.Select(x => x.ModelName).ToList();
            currentSession.DefenseModelCounts = planeBreakdown.Select(x => x.Count).ToList();
            currentSession.DefenseModelAmounts = planeBreakdown.Select(x => x.DefenseCount).ToList();
            currentSession.DefenseModelMinimums = planeBreakdown.Select(x => x.MinimumCount).ToList();
            currentSession.DefenseModelIndex = 0;
            sessions[uid] = currentSession;

            await bot.AnswerCallbackQueryAsync(cb.Id, $"🪖 {pct}%", cancellationToken: ct);
            DeleteNow(cb.Message.Chat.Id, cb.Message.MessageId);
            var firstPlane = planeBreakdown[0];
            string msgPlane = $"✈️ دفاع جنگنده – مدل 1/{planeBreakdown.Count}\n\n🔧 مدل: {firstPlane.ModelName}\n📊 موجودی: {firstPlane.Count:N0}\n🛡 مقدار فعلی دفاع: {firstPlane.DefenseCount:N0}\n🔒 حداقل اجباری: {firstPlane.MinimumCount:N0}\n\nتعداد دقیق این مدل در دفاع را وارد کنید:";
            await SendPrompt(uid, uid, msgPlane, ct: ct);
            return;
        }
        if (kind == "fighter")
        {
            // Legacy single fighter handling – kept for backward compat, now redirects to per-model if needed
            long defT = c.DefenseTanks, defS = c.DefenseSoldiers;
            int dtp = 100, dsp = 100;
            if (sessions.TryGetValue(uid, out var s) && s != null && s.AttackChatId == cid) { defT = s.DefenseTanks; defS = s.DefenseSoldiers; dtp = s.DefTankPct > 0 ? s.DefTankPct : 100; dsp = s.DefSoldierPct > 0 ? s.DefSoldierPct : 100; }
            long df = (long)Math.Ceiling(c.Planes * (pct / 100.0));
            Database.UpdateDefenseFull(uid, cid, defT, defS, df, c.DefenseStrategy, c.DefenseTactic, dtp, dsp, pct);
            EndSession(uid);
            await bot.AnswerCallbackQueryAsync(cb.Id, $"✅ ذخیره شد.", cancellationToken: ct);
            DeleteNow(cb.Message.Chat.Id, cb.Message.MessageId);
            await SendDefenseStatus(uid, uid, cid, ct);
            return;
        }
        await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
    }

    static async Task HandleDefenseModelPctCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        // Callback: defense_model_pct:{chatId}:{category}:{modelIndex}:{pct}
        if (parts.Length < 5 || cb.Message == null) return;
        long uid = cb.From.Id;
        if (!TryParseLong(parts[1], out long cid) || !TryParseInt(parts[3], out int modelIdx) || !TryParseInt(parts[4], out int pct)) return;
        string category = parts[2]; // tanks or planes
        pct = Math.Clamp(pct, 20, 100);

        if (!sessions.TryGetValue(uid, out var sess) || sess == null) return;
        var c = Database.GetCountry(uid, cid);
        if (c == null) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ کشور نیست!", cancellationToken: ct); return; }

        // Ensure session matches
        if (sess.AttackChatId != cid && sess.AttackChatId != 0) { /* mismatch, but allow */ }

        if (modelIdx < 0 || modelIdx >= sess.DefenseModelNames.Count)
        {
            await bot.AnswerCallbackQueryAsync(cb.Id, "❌ ایندکس نامعتبر", cancellationToken: ct);
            return;
        }

        // Save pct for this model – now supports naval too
        sess.DefenseModelPcts[modelIdx] = pct;
        string modelName = sess.DefenseModelNames[modelIdx];
        string dbCategory = category switch
        {
            "tanks" => "Tanks",
            "planes" => "Planes",
            "boats" => "Boats",
            "submarines" => "Submarines",
            "battleships" => "Battleships",
            _ => "Tanks"
        };
        Database.SetDefenseModel(uid, cid, dbCategory, modelName, pct);

        await bot.AnswerCallbackQueryAsync(cb.Id, $"✅ {modelName}: {pct}%", cancellationToken: ct);

        // Move to next model in same category
        sess.DefenseModelIndex = modelIdx + 1;
        if (sess.DefenseModelIndex < sess.DefenseModelNames.Count)
        {
            var next = sess.DefenseModelNames[sess.DefenseModelIndex];
            var nextCount = sess.DefenseModelCounts[sess.DefenseModelIndex];
            var nextPct = sess.DefenseModelPcts[sess.DefenseModelIndex];
            string msg = category switch
            {
                "tanks" => $"🛡 درصد دفاع تانک – مدل {sess.DefenseModelIndex + 1}/{sess.DefenseModelNames.Count}\n\n🔧 مدل: {next}\n📊 موجودی: {nextCount:N0}\n📈 فعلی: {nextPct}%\n\nچند درصد در دفاع باشد؟",
                "planes" => $"✈️ درصد دفاع جنگنده – مدل {sess.DefenseModelIndex + 1}/{sess.DefenseModelNames.Count}\n\n🔧 مدل: {next}\n📊 موجودی: {nextCount:N0}\n📈 فعلی: {nextPct}%\n\nچند درصد در دفاع باشد؟",
                "boats" => $"🚤 درصد دفاع قایق – مدل {sess.DefenseModelIndex + 1}/{sess.DefenseModelNames.Count}\n\n🔧 مدل: {next}\n📊 موجودی: {nextCount:N0}\n📈 فعلی: {nextPct}%\n\nچند درصد در دفاع باشد؟",
                "submarines" => $"⚓ درصد دفاع زیردریایی – مدل {sess.DefenseModelIndex + 1}/{sess.DefenseModelNames.Count}\n\n🔧 مدل: {next}\n📊 موجودی: {nextCount:N0}\n📈 فعلی: {nextPct}%\n\nچند درصد در دفاع باشد؟",
                _ => $"🛡 درصد دفاع {category} – مدل {sess.DefenseModelIndex + 1}/{sess.DefenseModelNames.Count}\n\n🔧 مدل: {next}\n📊 موجودی: {nextCount:N0}\n📈 فعلی: {nextPct}%\n\nچند درصد در دفاع باشد؟"
            };
            await bot.EditMessageTextAsync(cb.Message.Chat.Id, cb.Message.MessageId, msg, replyMarkup: BuildModelPercentKeyboard(cid, category, sess.DefenseModelIndex), cancellationToken: ct);
            return;
        }

        // Finished current category
        if (category == "tanks")
        {
            long totalDefTanks = 0;
            for (int i = 0; i < sess.DefenseModelNames.Count; i++)
                totalDefTanks += (long)Math.Ceiling(sess.DefenseModelCounts[i] * sess.DefenseModelPcts[i] / 100.0);
            sess.DefenseTanks = totalDefTanks;
            sess.DefTankPct = 100;
            sess.Step = SessionStep.DefenseWaitingSoldiers;
            string msg = $"🪖 درصد دفاع سرباز:\nکل: {c.Soldiers:N0}\n\nدرصد را انتخاب کنید:";
            await bot.EditMessageTextAsync(cb.Message.Chat.Id, cb.Message.MessageId, msg, replyMarkup: BuildPercentKeyboard("soldier", cid), cancellationToken: ct);
            return;
        }
        else if (category == "planes")
        {
            long totalDefPlanes = 0;
            for (int i = 0; i < sess.DefenseModelNames.Count; i++)
                totalDefPlanes += (long)Math.Ceiling(sess.DefenseModelCounts[i] * sess.DefenseModelPcts[i] / 100.0);
            sess.DefenseTanks = sess.DefenseTanks; // keep
            sess.DefenseSoldiers = sess.DefenseSoldiers;
            // Store intermediate fighter count in session for later finalization, but continue to naval
            sess.DefenseModelNames = new List<string>(); // will be reused for boats
            sess.DefenseModelCounts = new List<long>();
            sess.DefenseModelPcts = new List<int>();
            // Go to boats
            var boatBreakdown = GetDefenseBreakdown(c, "boats");
            if (boatBreakdown.Count == 0)
            {
                // No boats, go to subs
                var subBreakdown = GetDefenseBreakdown(c, "submarines");
                if (subBreakdown.Count == 0)
                {
                    // No naval, finalize
                    long defT = sess.DefenseTanks;
                    long defS = sess.DefenseSoldiers;
                    int dtp = sess.DefTankPct > 0 ? sess.DefTankPct : 100;
                    int dsp = sess.DefSoldierPct > 0 ? sess.DefSoldierPct : 100;
                    Database.UpdateDefenseFull(uid, cid, defT, defS, totalDefPlanes, c.DefenseStrategy, c.DefenseTactic, dtp, dsp, 100);
                    Database.ReconcileDefense(uid, cid);
                    EndSession(uid);
                    DeleteNow(cb.Message.Chat.Id, cb.Message.MessageId);
                    await SendDefenseStatus(uid, uid, cid, ct);
                    return;
                }
                sess.DefenseCurrentCategory = "submarines";
                sess.DefenseModelNames = subBreakdown.Select(x => x.ModelName).ToList();
                sess.DefenseModelCounts = subBreakdown.Select(x => x.Count).ToList();
                sess.DefenseModelPcts = subBreakdown.Select(x => x.DefPct).ToList();
                sess.DefenseModelIndex = 0;
                var firstSub = subBreakdown[0];
                string msgSub = $"⚓ درصد دفاع زیردریایی – مدل 1/{subBreakdown.Count}\n\n🔧 مدل: {firstSub.ModelName}\n📊 موجودی: {firstSub.Count:N0}\n📈 فعلی: {firstSub.DefPct}%\n\nچند درصد در دفاع باشد؟";
                await bot.EditMessageTextAsync(cb.Message.Chat.Id, cb.Message.MessageId, msgSub, replyMarkup: BuildModelPercentKeyboard(cid, "submarines", 0), cancellationToken: ct);
                return;
            }
            // Boats exist
            sess.DefenseCurrentCategory = "boats";
            sess.DefenseModelNames = boatBreakdown.Select(x => x.ModelName).ToList();
            sess.DefenseModelCounts = boatBreakdown.Select(x => x.Count).ToList();
            sess.DefenseModelPcts = boatBreakdown.Select(x => x.DefPct).ToList();
            sess.DefenseModelIndex = 0;
            // Store plane total in a temp field (use DefenseFighters as temp)
            sess.DefenseFighters = totalDefPlanes;
            var firstBoat = boatBreakdown[0];
            string msgBoat = $"🚤 درصد دفاع قایق – مدل 1/{boatBreakdown.Count}\n\n🔧 مدل: {firstBoat.ModelName}\n📊 موجودی: {firstBoat.Count:N0}\n📈 فعلی: {firstBoat.DefPct}%\n\nچند درصد در دفاع باشد؟";
            await bot.EditMessageTextAsync(cb.Message.Chat.Id, cb.Message.MessageId, msgBoat, replyMarkup: BuildModelPercentKeyboard(cid, "boats", 0), cancellationToken: ct);
            return;
        }
        else if (category == "boats")
        {
            // Boats finished, go to submarines
            var subBreakdown = GetDefenseBreakdown(c, "submarines");
            if (subBreakdown.Count == 0)
            {
                // Finalize with existing totals
                long defT = sess.DefenseTanks;
                long defS = sess.DefenseSoldiers;
                long defF = sess.DefenseFighters; // plane total stored earlier
                int dtp = sess.DefTankPct > 0 ? sess.DefTankPct : 100;
                int dsp = sess.DefSoldierPct > 0 ? sess.DefSoldierPct : 100;
                Database.UpdateDefenseFull(uid, cid, defT, defS, defF, c.DefenseStrategy, c.DefenseTactic, dtp, dsp, 100);
                Database.ReconcileDefense(uid, cid);
                EndSession(uid);
                DeleteNow(cb.Message.Chat.Id, cb.Message.MessageId);
                await SendDefenseStatus(uid, uid, cid, ct);
                return;
            }
            sess.DefenseCurrentCategory = "submarines";
            sess.DefenseModelNames = subBreakdown.Select(x => x.ModelName).ToList();
            sess.DefenseModelCounts = subBreakdown.Select(x => x.Count).ToList();
            sess.DefenseModelPcts = subBreakdown.Select(x => x.DefPct).ToList();
            sess.DefenseModelIndex = 0;
            var firstSub = subBreakdown[0];
            string msgSub = $"⚓ درصد دفاع زیردریایی – مدل 1/{subBreakdown.Count}\n\n🔧 مدل: {firstSub.ModelName}\n📊 موجودی: {firstSub.Count:N0}\n📈 فعلی: {firstSub.DefPct}%\n\nچند درصد در دفاع باشد؟";
            await bot.EditMessageTextAsync(cb.Message.Chat.Id, cb.Message.MessageId, msgSub, replyMarkup: BuildModelPercentKeyboard(cid, "submarines", 0), cancellationToken: ct);
            return;
        }
        else if (category == "submarines")
        {
            // All naval finished, finalize
            long defT = sess.DefenseTanks;
            long defS = sess.DefenseSoldiers;
            long defF = sess.DefenseFighters; // includes plane total
            int dtp = sess.DefTankPct > 0 ? sess.DefTankPct : 100;
            int dsp = sess.DefSoldierPct > 0 ? sess.DefSoldierPct : 100;
            Database.UpdateDefenseFull(uid, cid, defT, defS, defF, c.DefenseStrategy, c.DefenseTactic, dtp, dsp, 100);
            Database.ReconcileDefense(uid, cid);
            EndSession(uid);
            DeleteNow(cb.Message.Chat.Id, cb.Message.MessageId);
            await SendDefenseStatus(uid, uid, cid, ct);
            return;
        }
    }

    static async Task HandleDefenseTacticCallback(
        CallbackQuery cb,
        string[] parts,
        CancellationToken ct)
    {
        if (parts.Length < 2)
            return;

        long uid = cb.From.Id;

        if (!TryParseLong(parts[1], out long chatId))
            return;

        await bot.AnswerCallbackQueryAsync(
            cb.Id,
            cancellationToken: ct
        );

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    "🛡 دفاع منسجم",
                    $"defense_tactic_select:{chatId}:1"
                )
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    "💥 دفاع و ضدحمله پراکنده",
                    $"defense_tactic_select:{chatId}:2"
                )
            }
        });

        await SendTemp(
            uid,
            GroundDefenseStrategyGuide,
            markup: keyboard,
            ct: ct
        );
    }

    static async Task HandleDefenseTacticSelectCallback(
        CallbackQuery cb,
        string[] parts,
        CancellationToken ct)
    {
        if (parts.Length < 3)
            return;

        long uid = cb.From.Id;

        if (!TryParseLong(parts[1], out long chatId) ||
            !TryParseInt(parts[2], out int strategy) ||
            strategy is < 1 or > 2)
            return;

        if (parts.Length >= 4 &&
            TryParseInt(parts[3], out int tactic))
        {
            if (tactic is < 1 or > 2)
                return;

            var country =
                Database.GetCountry(uid, chatId);

            if (country == null)
            {
                await bot.AnswerCallbackQueryAsync(
                    cb.Id,
                    "❌ کشور یافت نشد.",
                    cancellationToken: ct
                );
                return;
            }

            Database.UpdateDefense(
                uid,
                chatId,
                country.DefenseTanks,
                country.DefenseSoldiers,
                strategy,
                tactic
            );

            await bot.AnswerCallbackQueryAsync(
                cb.Id,
                "✅ استراتژی و تاکتیک دفاعی ذخیره شد.",
                cancellationToken: ct
            );

            if (cb.Message != null)
            {
                DeleteNow(
                    cb.Message.Chat.Id,
                    cb.Message.MessageId
                );
            }

            await SendDefenseStatus(
                uid,
                uid,
                chatId,
                ct
            );
            return;
        }

        await bot.AnswerCallbackQueryAsync(
            cb.Id,
            cancellationToken: ct
        );

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    GroundDefenseTacticName(strategy, 1),
                    $"defense_tactic_select:{chatId}:{strategy}:1"
                )
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    GroundDefenseTacticName(strategy, 2),
                    $"defense_tactic_select:{chatId}:{strategy}:2"
                )
            }
        });

        string guide =
            GroundDefenseTacticGuide(strategy);

        if (cb.Message != null)
        {
            await bot.EditMessageTextAsync(
                cb.Message.Chat.Id,
                cb.Message.MessageId,
                guide,
                replyMarkup: keyboard,
                cancellationToken: ct
            );
        }
        else
        {
            await SendTemp(
                uid,
                guide,
                markup: keyboard,
                ct: ct
            );
        }
    }

    static async Task HandleAirDefStrategyCallback(
        CallbackQuery cb,
        string[] parts,
        CancellationToken ct)
    {
        if (parts.Length < 2 || cb.Message == null)
            return;

        long uid = cb.From.Id;

        if (!TryParseLong(parts[1], out long chatId))
            return;

        if (parts.Length >= 3 &&
            TryParseInt(parts[2], out int strategy))
        {
            if (strategy is < 1 or > 2)
                return;

            await bot.AnswerCallbackQueryAsync(
                cb.Id,
                cancellationToken: ct
            );

            string tacticOne =
                AirDefenseTacticName(strategy, 1);

            string tacticTwo =
                AirDefenseTacticName(strategy, 2);

            if (strategy == 1)
                tacticTwo += " 🔒";

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        tacticOne,
                        $"airdef_tactic:{chatId}:{strategy}:1"
                    )
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        tacticTwo,
                        $"airdef_tactic:{chatId}:{strategy}:2"
                    )
                }
            });

            await bot.EditMessageTextAsync(
                cb.Message.Chat.Id,
                cb.Message.MessageId,
                AirDefenseTacticGuide(strategy),
                replyMarkup: keyboard,
                cancellationToken: ct
            );
            return;
        }

        await bot.AnswerCallbackQueryAsync(
            cb.Id,
            cancellationToken: ct
        );

        var strategyKeyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    "🗺 دفاع منطقه‌ای",
                    $"airdef_strategy:{chatId}:1"
                )
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    "🎯 دفاع نقطه‌ای",
                    $"airdef_strategy:{chatId}:2"
                )
            }
        });

        await SendTemp(
            uid,
            AirDefenseStrategyGuide,
            markup: strategyKeyboard,
            ct: ct
        );
    }

    static async Task HandleAirDefTacticCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length < 4 || cb.Message == null) return;
        long uid = cb.From.Id;
        if (!TryParseLong(parts[1], out long cid) || !TryParseInt(parts[2], out int str) || !TryParseInt(parts[3], out int tac)) return;
        if (str == 1 && tac == 2) { await bot.AnswerCallbackQueryAsync(cb.Id, "📡 رادار ندارید! قفل.", showAlert: true, cancellationToken: ct); return; }
        var c = Database.GetCountry(uid, cid);
        if (c == null) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌", cancellationToken: ct); return; }
        c.AirDefStrategy = str; c.AirDefTactic = tac;
        Database.UpdateCountryFull(c);
        await bot.AnswerCallbackQueryAsync(cb.Id, "✅ ذخیره شد.", cancellationToken: ct);
        DeleteNow(cb.Message.Chat.Id, cb.Message.MessageId);
        await SendDefenseStatus(uid, uid, cid, ct);
    }
}
