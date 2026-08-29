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
{    static async Task HandleCallbackAsync(CallbackQuery cb, CancellationToken ct)
    {
        if (cb.Data == null) return;

        if(cb.Data.StartsWith("spam_admin:",StringComparison.Ordinal))
        {
            await HandleSpamAdminCallback(cb,ct);
            return;
        }
        if (cb.Data.StartsWith("adm:", StringComparison.Ordinal))
        {
            await HandleAdminCallbackAsync(cb, ct);
            return;
        }

        if (cb.Data.StartsWith("ally_")) { await HandleAllianceInviteCallback(cb, ct); return; }
        if (cb.Data.StartsWith("tf_")) { await HandleTransferCallback(cb, ct); return; }
        if (cb.Data.StartsWith("dep_")) { await HandleDeploymentCallback(cb, ct); return; }
        if (cb.Message == null) return;
        var parts = cb.Data.Split(':');
        if (parts.Length < 1) return;

        if (parts[0] is "eq_details" or "dep_info" or "faction" or "build_menu" or "upgrade" or "tank_info" or "tank_buy" or "plane_info" or "plane_buy" or "bomber_info" or "bomber_buy" or "aa_info" or "aa_buy" or "boat_info" or "boat_buy" or "sub_info" or "sub_buy" or "battleship_info" or "battleship_buy" or "battleship_repair" or "battleship_scrap_menu" or "cancel")
        {
            if (parts.Length >= 2 && TryParseLong(parts[1], out long ownerBtn))
            {
                if (ownerBtn != cb.From.Id) { await bot.AnswerCallbackQueryAsync(cb.Id, "⛔ این دکمه برای شما نیست!", showAlert: true, cancellationToken: ct); return; }
            }
        }

        switch (parts[0])
        {
            case "cancel": await HandleCancelCallback(cb, ct); break;
            case "faction": await HandleFactionCallback(cb, parts, ct); break;
            case "eq_details": await SendCountryEquipmentDetails(cb, parts, ct); break;
            case "dep_info": await SendDeploymentInfoDetails(cb, parts, ct); break;
            case "build_menu": await HandleBuildMenuCallback(cb, parts, ct); break;
            case "upgrade": await HandleUpgradeCallback(cb, parts, ct); break;
            case "timing": await HandleTimingCallback(cb, parts, ct); break;
            case "tank_info": await HandleTankInfoCallback(cb, parts, ct); break;
            case "tank_buy": await HandleTankBuyCallback(cb, parts, ct); break;
            case "plane_info": await HandlePlaneInfoCallback(cb, parts, ct); break;
            case "plane_buy": await HandlePlaneBuyCallback(cb, parts, ct); break;
            case "bomber_info": await HandleBomberInfoCallback(cb, parts, ct); break;
            case "bomber_buy": await HandleBomberBuyCallback(cb, parts, ct); break;
            case "aa_info": await HandleAntiAirInfoCallback(cb, parts, ct); break;
            case "aa_buy": await HandleAntiAirBuyCallback(cb, parts, ct); break;
            case "defense_status": await HandleDefenseStatusCallback(cb, parts, ct); break;
            case "defense_tactic": await HandleDefenseTacticCallback(cb, parts, ct); break;
            case "defense_tactic_select": await HandleDefenseTacticSelectCallback(cb, parts, ct); break;
            case "defense_set": await HandleDefenseSetCallback(cb, parts, ct); break;
            case "naval_defense": await HandleNavalDefenseCallback(cb, parts, ct); break;
            case "naval_defense_strategy": await HandleNavalDefenseStrategyCallback(cb, parts, ct); break;
            case "naval_defense_tactic": await HandleNavalDefenseTacticCallback(cb, parts, ct); break;
            case "naval_cancel": await HandleNavalCancellationCallback(cb, parts, ct); break;
            case "naval_locked": await bot.AnswerCallbackQueryAsync(cb.Id, "🔒 این استراتژی فعلاً قفل است.", showAlert: true, cancellationToken: ct); break;
            case "defense_pct": await HandleDefensePctCallback(cb, parts, ct); break;
            case "defense_model_pct": await HandleDefenseModelPctCallback(cb, parts, ct); break;
            case "boat_info": await HandleBoatInfoCallback(cb, parts, ct); break;
            case "boat_buy": await HandleBoatBuyCallback(cb, parts, ct); break;
            case "sub_info": await HandleSubInfoCallback(cb, parts, ct); break;
            case "sub_buy": await HandleSubBuyCallback(cb, parts, ct); break;
            case "battleship_info": await HandleBattleshipInfoCallback(cb, parts, ct); break;
            case "battleship_buy": await HandleBattleshipBuyCallback(cb, parts, ct); break;
            case "battleship_repair": await HandleBattleshipRepairCallback(cb, parts, ct); break;
            case "battleship_repair_quote": await HandleBattleshipRepairQuoteCallback(cb, parts, ct); break;
            case "battleship_repair_unit": await HandleBattleshipRepairUnitCallback(cb, parts, ct); break;
            case "battleship_scrap_menu": await HandleBattleshipScrapMenuCallback(cb, ct); break;
            case "battleship_scrap": await HandleBattleshipScrapQuoteCallback(cb, parts, ct); break;
            case "battleship_scrap_confirm": await HandleBattleshipScrapConfirmCallback(cb, parts, ct); break;
            case "airdef_strategy": await HandleAirDefStrategyCallback(cb, parts, ct); break;
            case "airdef_tactic": await HandleAirDefTacticCallback(cb, parts, ct); break;
            case "attack_group": await HandleAttackGroupCallback(cb, parts, ct); break;
            case "attack_target": await HandleAttackTargetCallback(cb, parts, ct); break;
            case "revenge": await HandleRevengeCallback(cb, parts, ct); break;
            case "attack_type": await HandleAttackTypeCallback(cb, parts, ct); break;
            case "attack_strategy": await HandleAttackStrategyCallback(cb, parts, ct); break;
            case "attack_tactic": await HandleAttackTacticCallback(cb, parts, ct); break;
            case "attack_air_strategy": await HandleAttackAirStrategyCallback(cb, parts, ct); break;
            case "attack_air_tactic": await HandleAttackAirTacticCallback(cb, parts, ct); break;
            case "attack_naval_strategy": await HandleAttackNavalStrategyCallback(cb, parts, ct); break;
            case "attack_naval_tactic": await HandleAttackNavalTacticCallback(cb, parts, ct); break;
        }
    }

    static async Task HandleCancelCallback(CallbackQuery cb, CancellationToken ct)
    {
        long uid = cb.From.Id;
        await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
        EndSession(uid);
        if (cb.Message != null) DeleteNow(cb.Message.Chat.Id, cb.Message.MessageId);
    }

    static async Task HandleNavalCancellationCallback(CallbackQuery cb,string[] parts,CancellationToken ct)
    {
        long uid=cb.From.Id;
        if(parts.Length<3||!TryParseLong(parts[1],out long chatId)||!TryParseLong(parts[2],out long operationId))
        {
            await bot.AnswerCallbackQueryAsync(cb.Id,"❌ درخواست نامعتبر است.",showAlert:true,cancellationToken:ct);
            return;
        }
        NavalInvasion? cancelled=null;
        var locks=await AcquireCountryMutationLocks(chatId,new[]{uid},ct);
        try
        {
            var operation=Database.GetCancelableNavalOperation(operationId,uid,chatId);
            if(operation!=null&&Database.ReturnNavalOperationWithoutBattle(operation,"Cancelled"))cancelled=operation;
        }
        finally{ReleaseCountryMutationLocks(locks);}
        if(cancelled==null)
        {
            await bot.AnswerCallbackQueryAsync(cb.Id,"❌ عملیات قبلاً رسیده، لغو شده یا متعلق به شما نیست.",showAlert:true,cancellationToken:ct);
            return;
        }
        EndSession(uid);
        await bot.AnswerCallbackQueryAsync(cb.Id,"✅ کل ناوگان برگشت.",showAlert:true,cancellationToken:ct);
        if(cb.Message!=null)DeleteNow(cb.Message.Chat.Id,cb.Message.MessageId);
        string groupTitle=await GetGroupTitleCached(chatId,ct);
        await SendPermanent(uid,$"✅ لشکرکشی دریایی #{operationId} لغو شد.\n"+
            $"💬 گپ: {groupTitle}\n🎯 مقصد قبلی: {cancelled.DefenderName}\n"+
            $"↩️ کل ناوگان بدون تلفات همان لحظه به دارایی برگشت.\n"+
            $"🚤 {cancelled.Boats:N0} | ⚓ {cancelled.Submarines:N0} | 🚢 {cancelled.Battleships:N0}",ct:ct);
        try{await SendPermanent(cancelled.DefenderId,
            $"ℹ️ هشدار دریایی لغو شد.\nعملیات #{operationId} کشور {cancelled.AttackerName} در گپ «{groupTitle}» متوقف شد.",ct:ct);}catch{}
        try{await SendPermanent(chatId,
            $"↩️ {cancelled.AttackerName} لشکرکشی دریایی #{operationId} علیه {cancelled.DefenderName} را لغو کرد و ناوگانش برگشت.",ct:ct);}catch{}
        Console.WriteLine($"[NAVAL CANCELLED] operation={operationId} attacker={uid} chat={chatId} boats={cancelled.Boats} subs={cancelled.Submarines} battleships={cancelled.Battleships}");
    }

    static async Task HandleFactionCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length != 3) return;
        long uid = cb.From.Id;
        string facStr = parts[2];
        Faction fac = facStr switch { "USSR" => Faction.USSR, "USA" => Faction.USA, _ => Faction.Reich };
        sessions[uid] = new UserSession { Step = SessionStep.WaitingCountryName, Faction = fac, FactionStr = facStr };
        if (cb.Message != null) { await bot.EditMessageTextAsync(cb.Message.Chat.Id, cb.Message.MessageId, "اسم کشور را وارد کنید", cancellationToken: ct); TrackPrompt(uid, cb.Message.Chat.Id, cb.Message.MessageId); }
        await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
    }

    static async Task HandleBuildMenuCallback(
        CallbackQuery cb,
        string[] parts,
        CancellationToken ct)
    {
        if (parts.Length != 3 || cb.Message == null)
            return;

        long uid = cb.From.Id;
        string bt = parts[2];

        if (bt is not ("factory" or "port" or "mine"))
        {
            await bot.AnswerCallbackQueryAsync(
                cb.Id,
                "❌ ساختمان نامعتبر است.",
                showAlert: true,
                cancellationToken: ct
            );
            return;
        }

        long chatId = cb.Message.Chat.Id;
        var c = Database.GetCountry(uid, chatId);

        if (c == null)
        {
            await bot.AnswerCallbackQueryAsync(
                cb.Id,
                "❌ کشوری ندارید!",
                cancellationToken: ct
            );
            return;
        }

        int cur = bt switch
        {
            "factory" => c.FactoryLevel,
            "port" => c.PortLevel,
            "mine" => c.MineLevel,
            _ => 1
        };

        int max = MaxBuildLevel(c, bt);

        if (cur >= max)
        {
            string maxMessage = c.Besieged >= 2
                ? "🔒 به‌دلیل شرایط بحرانی، امکان ارتقا وجود ندارد."
                : "✅ این ساختمان در حداکثر سطح است.";

            await bot.AnswerCallbackQueryAsync(
                cb.Id,
                maxMessage,
                showAlert: true,
                cancellationToken: ct
            );
            return;
        }

        int next = cur + 1;

        double currentIncome = bt switch
        {
            "factory" => FactoryIncome[cur],
            "port" => PortIncome[cur],
            "mine" => MineIncome[cur],
            _ => 0
        };

        double nextIncome = bt switch
        {
            "factory" => FactoryIncome[next],
            "port" => PortIncome[next],
            "mine" => MineIncome[next],
            _ => 0
        };

        string buildingName = bt switch
        {
            "factory" => "🏭 کارخانه",
            "port" => "⚓ بندر",
            "mine" => "⛏️ معدن",
            _ => "ساختمان"
        };

        string incomeUnit = bt == "mine" ? "آهن" : "پول";

        bool usesRoyalCoins = bt == "mine" && next >= 6;

        string priceText;
        string balanceText;

        if (usesRoyalCoins)
        {
            int royalCost = MineRoyalCostForTargetLevel(next);
            long royalBalance = Database.GetRoyalCoins(uid);

            priceText = $"{royalCost:N0} رویال‌کوین 💎";
            balanceText = $"موجودی رویال: {royalBalance:N0}";
        }
        else
        {
            int costK = bt switch
            {
                "factory" => FactoryUpgradeCost[cur],
                "port" => PortUpgradeCost[cur],
                "mine" => MineUpgradeCost[cur],
                _ => 0
            };

            priceText = $"{costK:N0}K پول 💰";
            balanceText = $"پول: {(c.Money / 1000.0):F1}K";
        }

        string text =
            $"{buildingName}\n" +
            $"سطح فعلی: {cur}\n" +
            $"درآمد فعلی: {currentIncome:F1}K {incomeUnit}\n\n" +
            $"سطح بعدی: {next}\n" +
            $"درآمد بعدی: {nextIncome:F1}K {incomeUnit}\n" +
            $"هزینه ارتقا: {priceText}\n" +
            balanceText;

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    "✅ ارتقا",
                    $"upgrade:{uid}:{bt}"
                )
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    "❌ لغو",
                    $"cancel:{uid}"
                )
            }
        });

        await bot.EditMessageTextAsync(
            chatId,
            cb.Message.MessageId,
            text,
            replyMarkup: keyboard,
            cancellationToken: ct
        );

        await bot.AnswerCallbackQueryAsync(
            cb.Id,
            cancellationToken: ct
        );
    }

    static async Task HandleTimingCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length < 2 || cb.Message == null) return;
        long uid = cb.From.Id;
        if (parts[1] == "daily") { sessions[uid] = new UserSession { Step = SessionStep.OwnerWaitingDailyTime }; await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct); await SendPrompt(uid, cb.Message.Chat.Id, "⏰ ساعت HHMM:", ct: ct); return; }
        if (parts[1] == "minute") { sessions[uid] = new UserSession { Step = SessionStep.OwnerWaitingMinuteTime }; await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct); await SendPrompt(uid, cb.Message.Chat.Id, "⌛ هر چند دقیقه؟", ct: ct); return; }
        await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
    }

    static async Task HandleUpgradeCallback(
        CallbackQuery cb,
        string[] parts,
        CancellationToken ct)
    {
        if (parts.Length != 3 || cb.Message == null)
            return;

        long uid = cb.From.Id;
        string bt = parts[2];

        if (bt is not ("factory" or "port" or "mine"))
        {
            await bot.AnswerCallbackQueryAsync(
                cb.Id,
                "❌ ساختمان نامعتبر است.",
                showAlert: true,
                cancellationToken: ct
            );
            return;
        }

        long chatId = cb.Message.Chat.Id;
        var c = Database.GetCountry(uid, chatId);

        if (c == null)
        {
            await bot.AnswerCallbackQueryAsync(
                cb.Id,
                "❌ کشور یافت نشد!",
                cancellationToken: ct
            );
            return;
        }

        int cur = bt switch
        {
            "factory" => c.FactoryLevel,
            "port" => c.PortLevel,
            "mine" => c.MineLevel,
            _ => 1
        };

        int max = MaxBuildLevel(c, bt);

        if (cur >= max)
        {
            await bot.AnswerCallbackQueryAsync(
                cb.Id,
                c.Besieged >= 2
                    ? "🔒 به‌دلیل شرایط بحرانی امکان ارتقا وجود ندارد."
                    : "✅ ساختمان در حداکثر سطح است.",
                showAlert: true,
                cancellationToken: ct
            );
            return;
        }

        int newLevel = cur + 1;
        bool usesRoyalCoins = bt == "mine" && newLevel >= 6;

        long moneyCost = 0;
        int royalCost = 0;

        if (usesRoyalCoins)
        {
            royalCost = MineRoyalCostForTargetLevel(newLevel);
            long royalBalance = Database.GetRoyalCoins(uid);

            if (royalBalance < royalCost)
            {
                await bot.AnswerCallbackQueryAsync(
                    cb.Id,
                    $"💎 رویال‌کوین کافی نیست!\n" +
                    $"نیاز: {royalCost:N0}\n" +
                    $"موجودی: {royalBalance:N0}",
                    showAlert: true,
                    cancellationToken: ct
                );
                return;
            }

            if (!Database.TryUpgradeMineWithRoyal(uid, chatId, cur, newLevel, royalCost))
            {
                await bot.AnswerCallbackQueryAsync(cb.Id,
                    "❌ ارتقای معدن انجام نشد؛ سطح یا موجودی رویال تغییر کرده است.",
                    showAlert: true, cancellationToken: ct);
                return;
            }
        }
        else
        {
            int costK = bt switch
            {
                "factory" => FactoryUpgradeCost[cur],
                "port" => PortUpgradeCost[cur],
                "mine" => MineUpgradeCost[cur],
                _ => 0
            };

            moneyCost = costK * 1000L;

            if (c.Money < moneyCost)
            {
                await bot.AnswerCallbackQueryAsync(
                    cb.Id,
                    $"💰 پول کافی نیست!\n" +
                    $"نیاز: {costK:N0}K\n" +
                    $"موجودی: {(c.Money / 1000.0):F1}K",
                    showAlert: true,
                    cancellationToken: ct
                );
                return;
            }

            Database.UpdateBuildingLevel(
                uid,
                chatId,
                bt,
                newLevel,
                -moneyCost
            );
        }

        var updatedCountry = Database.GetCountry(uid, chatId);

        if (updatedCountry == null)
        {
            await bot.AnswerCallbackQueryAsync(
                cb.Id,
                "⚠️ ارتقا انجام شد، اما اطلاعات جدید دریافت نشد.",
                showAlert: true,
                cancellationToken: ct
            );
            return;
        }

        bool canUpgradeMore = newLevel < max;

        string buildingName = bt switch
        {
            "factory" => "کارخانه",
            "port" => "بندر",
            "mine" => "معدن",
            _ => "ساختمان"
        };

        string currentBalance = usesRoyalCoins
            ? $"💎 رویال باقی‌مانده: {Database.GetRoyalCoins(uid):N0}"
            : $"💰 پول باقی‌مانده: {(updatedCountry.Money / 1000.0):F1}K";

        string resultText =
            $"✅ {buildingName} به سطح {newLevel} ارتقا یافت.\n" +
            currentBalance;

        InlineKeyboardMarkup? keyboard = null;

        if (canUpgradeMore)
        {
            int followingLevel = newLevel + 1;
            bool nextUsesRoyal =
                bt == "mine" && followingLevel >= 6;

            string nextPrice;

            if (nextUsesRoyal)
            {
                int nextRoyalCost =
                    MineRoyalCostForTargetLevel(followingLevel);

                nextPrice =
                    $"{nextRoyalCost:N0} رویال‌کوین";
            }
            else
            {
                int nextCostK = bt switch
                {
                    "factory" => FactoryUpgradeCost[newLevel],
                    "port" => PortUpgradeCost[newLevel],
                    "mine" => MineUpgradeCost[newLevel],
                    _ => 0
                };

                nextPrice = $"{nextCostK:N0}K پول";
            }

            resultText +=
                $"\nارتقای بعدی: سطح {followingLevel}" +
                $" — {nextPrice}";

            keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        "⬆️ ارتقای بعدی",
                        $"upgrade:{uid}:{bt}"
                    )
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        "❌ بستن",
                        $"cancel:{uid}"
                    )
                }
            });
        }
        else
        {
            resultText += "\n🏁 حداکثر سطح";
        }

        await bot.EditMessageTextAsync(
            chatId,
            cb.Message.MessageId,
            resultText,
            replyMarkup: keyboard,
            cancellationToken: ct
        );

        await bot.AnswerCallbackQueryAsync(
            cb.Id,
            usesRoyalCoins
                ? $"✅ {royalCost:N0} رویال‌کوین کسر شد."
                : "✅ ارتقا انجام شد.",
            cancellationToken: ct
        );
    }

    static async Task HandleTankInfoCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length != 3 || cb.Message == null) return;
        long uid = cb.From.Id;
        string tid = parts[2];
        string info = tid switch
        {
            "M2Medium" => "🇺🇸 M2 Medium\n\n⚖️ ۱۸ تن | 🔫 ۳۷mm | 🛡 ۳۰mm | ⚡ ۴۲km/h\n💰 هر ۵ تانک: ۲K آهن + ۲K پول",
            "T28" => "🇷🇺 T-28\n\n⚖️ ۲۸ تن | 🔫 ۷۶mm | 🛡 ۸۰mm | ⚡ ۳۷km/h\n💰 هر ۵ تانک: ۳K آهن + ۳K پول",
            "PanzerIII" => "🇩🇪 Panzer III\n\n⚖️ ۲۳ تن | 🔫 ۵۰mm | 🛡 ۶۰mm | ⚡ ۴۰km/h\n💰 هر ۵ تانک: ۲.۵K آهن + ۲.۵K پول",
            _ => "تانک ناشناخته"
        };
        await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
        if (info == "تانک ناشناخته") { await SendTemp(cb.Message.Chat.Id, info, ct: ct); return; }
        var kb = new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("1", $"tank_buy:{uid}:{tid}:1"), InlineKeyboardButton.WithCallbackData("5", $"tank_buy:{uid}:{tid}:5") }, new[] { InlineKeyboardButton.WithCallbackData("10", $"tank_buy:{uid}:{tid}:10"), InlineKeyboardButton.WithCallbackData("25", $"tank_buy:{uid}:{tid}:25") }, new[] { InlineKeyboardButton.WithCallbackData("❌ انصراف", $"cancel:{uid}") } });
        await SendTemp(cb.Message.Chat.Id, info, markup: kb, ct: ct);
    }

    static async Task HandleTankBuyCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length != 4 || cb.Message == null) return;
        long uid = cb.From.Id;
        string tid = parts[2];
        if (!TryParseInt(parts[3], out int cnt) || cnt <= 0) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ تعداد نامعتبر", cancellationToken: ct); return; }
        long cid = cb.Message.Chat.Id;
        var c = Database.GetCountry(uid, cid);
        if (c == null) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ کشور یافت نشد!", cancellationToken: ct); return; }
        double i5 = tid switch { "M2Medium" => 2000, "T28" => 3000, "PanzerIII" => 2500, _ => 0 };
        double m5 = tid switch { "M2Medium" => 2000, "T28" => 3000, "PanzerIII" => 2500, _ => 0 };
        long ti = (long)Math.Ceiling(cnt / 5.0 * i5);
        long tm = (long)Math.Ceiling(cnt / 5.0 * m5);
        if (c.Iron < ti) { await bot.AnswerCallbackQueryAsync(cb.Id, $"❌ آهن: نیاز {ti / 1000.0:F1}K", cancellationToken: ct); return; }
        if (c.Money < tm) { await bot.AnswerCallbackQueryAsync(cb.Id, $"❌ پول: نیاز {tm / 1000.0:F1}K", cancellationToken: ct); return; }
        c.Iron -= ti; c.Tanks += cnt; c.Money -= tm;
        Database.UpdateCountryResources(uid, cid, c.Money, c.Iron, c.Tanks);
        string tn = tid switch { "M2Medium" => "M2 Medium", "T28" => "T-28", "PanzerIII" => "Panzer III", _ => tid };
        await SendTemp(cb.Message.Chat.Id, $"✅ {cnt} تانک {tn} خریداری شد!\n💰 پول: {(c.Money / 1000.0):F1}K\n🔩 آهن: {(c.Iron / 1000.0):F1}K", ct: ct);
        await bot.AnswerCallbackQueryAsync(cb.Id, "✅ خرید موفق", cancellationToken: ct);
    }

    static async Task HandlePlaneInfoCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length != 3 || cb.Message == null) return;
        long uid = cb.From.Id;
        string pid = parts[2];
        string info = pid switch
        {
            "Bf109" => "🇩🇪 Bf 109\n⚡ ۵۷۰km/h | 🎯 مانور ۸/۱۰\n💰 هر ۵: ۲K آهن + ۵K پول",
            "P36" => "🇺🇸 P-36\n⚡ ۵۰۰km/h | 🎯 مانور ۹/۱۰\n💰 هر ۵: ۱.۵K آهن + ۴K پول",
            "I16" => "🇷🇺 I-16\n⚡ ۵۲۰km/h | 🎯 مانور ۹/۱۰\n💰 هر ۵: ۱K آهن + ۳.۵K پول",
            _ => "ناشناخته"
        };
        await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
        var kb = new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("1", $"plane_buy:{uid}:{pid}:1"), InlineKeyboardButton.WithCallbackData("5", $"plane_buy:{uid}:{pid}:5") }, new[] { InlineKeyboardButton.WithCallbackData("10", $"plane_buy:{uid}:{pid}:10"), InlineKeyboardButton.WithCallbackData("25", $"plane_buy:{uid}:{pid}:25") }, new[] { InlineKeyboardButton.WithCallbackData("❌ انصراف", $"cancel:{uid}") } });
        await SendTemp(cb.Message.Chat.Id, info, markup: kb, ct: ct);
    }

    static async Task HandlePlaneBuyCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length != 4 || cb.Message == null) return;
        long uid = cb.From.Id;
        string pid = parts[2];
        if (!TryParseInt(parts[3], out int cnt) || cnt <= 0) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ تعداد", cancellationToken: ct); return; }
        long cid = cb.Message.Chat.Id;
        var c = Database.GetCountry(uid, cid);
        if (c == null) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ کشور", cancellationToken: ct); return; }
        double i5 = pid switch { "I16" => 1000, "P36" => 1500, "Bf109" => 2000, _ => 0 };
        double m5 = pid switch { "I16" => 3500, "P36" => 4000, "Bf109" => 5000, _ => 0 };
        long ti = (long)Math.Ceiling(cnt / 5.0 * i5);
        long tm = (long)Math.Ceiling(cnt / 5.0 * m5);
        if (c.Iron < ti) { await bot.AnswerCallbackQueryAsync(cb.Id, $"❌ آهن", cancellationToken: ct); return; }
        if (c.Money < tm) { await bot.AnswerCallbackQueryAsync(cb.Id, $"❌ پول", cancellationToken: ct); return; }
        c.Iron -= ti; c.Planes += cnt; c.Money -= tm;
        Database.UpdatePlanesResources(uid, cid, c.Money, c.Iron, c.Planes);
        string pn = pid switch { "I16" => "I-16", "P36" => "P-36", "Bf109" => "Bf 109", _ => pid };
        await SendTemp(cb.Message.Chat.Id, $"✅ {cnt} {pn} خریداری شد!", ct: ct);
        await bot.AnswerCallbackQueryAsync(cb.Id, "✅", cancellationToken: ct);
    }

    static async Task HandleBomberInfoCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length != 3 || cb.Message == null) return;
        long uid = cb.From.Id;
        string bid = parts[2];
        string info = bid switch
        {
            "B17" => "🇺🇸 B-17\n⚡ ۴۶۰km/h | 🛡 ۸/۱۰ | 💣 ۳۶۰۰kg\n💰 هر ۱: ۳K آهن + ۵K پول",
            "He111" => "🇩🇪 He 111\n⚡ ۴۳۵km/h | 🛡 ۵/۱۰ | 💣 ۲۰۰۰kg\n💰 هر ۱: ۲K آهن + ۴K پول",
            "DB3" => "🇷🇺 DB-3\n⚡ ۴۳۰km/h | 🛡 ۳/۱۰ | 💣 ۱۰۰۰kg\n💰 هر ۱: ۱K آهن + ۳K پول",
            _ => "ناشناخته"
        };
        await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
        var kb = new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("1", $"bomber_buy:{uid}:{bid}:1"), InlineKeyboardButton.WithCallbackData("2", $"bomber_buy:{uid}:{bid}:2") }, new[] { InlineKeyboardButton.WithCallbackData("5", $"bomber_buy:{uid}:{bid}:5"), InlineKeyboardButton.WithCallbackData("10", $"bomber_buy:{uid}:{bid}:10") }, new[] { InlineKeyboardButton.WithCallbackData("❌ انصراف", $"cancel:{uid}") } });
        await SendTemp(cb.Message.Chat.Id, info, markup: kb, ct: ct);
    }

    static async Task HandleBomberBuyCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length != 4 || cb.Message == null) return;
        long uid = cb.From.Id;
        string bid = parts[2];
        if (!TryParseInt(parts[3], out int cnt) || cnt <= 0) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ تعداد", cancellationToken: ct); return; }
        long cid = cb.Message.Chat.Id;
        var c = Database.GetCountry(uid, cid);
        if (c == null) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ کشور", cancellationToken: ct); return; }
        double i1 = bid switch { "DB3" => 1000, "He111" => 2000, "B17" => 3000, _ => 0 };
        double m1 = bid switch { "DB3" => 3000, "He111" => 4000, "B17" => 5000, _ => 0 };
        long ti = (long)(cnt * i1);
        long tm = (long)(cnt * m1);
        if (c.Iron < ti) { await bot.AnswerCallbackQueryAsync(cb.Id, $"❌ آهن", cancellationToken: ct); return; }
        if (c.Money < tm) { await bot.AnswerCallbackQueryAsync(cb.Id, $"❌ پول", cancellationToken: ct); return; }
        c.Iron -= ti; c.Bombers += cnt; c.Money -= tm;
        Database.UpdateBombersResources(uid, cid, c.Money, c.Iron, c.Bombers);
        string bn = bid switch { "DB3" => "DB-3", "He111" => "He 111", "B17" => "B-17", _ => bid };
        await SendTemp(cb.Message.Chat.Id, $"✅ {cnt} {bn} خریداری شد!", ct: ct);
        await bot.AnswerCallbackQueryAsync(cb.Id, "✅", cancellationToken: ct);
    }

    static async Task HandleAntiAirInfoCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length != 3 || cb.Message == null) return;
        long uid = cb.From.Id;
        string info = "🎯 توپ ۷۶mm\n💰 هر ۵: ۲K آهن + ۴K پول";
        await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
        // FIX(1): همهٔ callbackها aa_buy (قبلاً یکی اشتباه aabuy بود)
        var kb = new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("1", $"aa_buy:{uid}:AA76:1"), InlineKeyboardButton.WithCallbackData("5", $"aa_buy:{uid}:AA76:5") }, new[] { InlineKeyboardButton.WithCallbackData("10", $"aa_buy:{uid}:AA76:10"), InlineKeyboardButton.WithCallbackData("25", $"aa_buy:{uid}:AA76:25") }, new[] { InlineKeyboardButton.WithCallbackData("❌ انصراف", $"cancel:{uid}") } });
        await SendTemp(cb.Message.Chat.Id, info, markup: kb, ct: ct);
    }

    static async Task HandleAntiAirBuyCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length != 4 || cb.Message == null) return;
        long uid = cb.From.Id;
        if (!TryParseInt(parts[3], out int cnt) || cnt <= 0) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ تعداد", cancellationToken: ct); return; }
        long cid = cb.Message.Chat.Id;
        var c = Database.GetCountry(uid, cid);
        if (c == null) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ کشور", cancellationToken: ct); return; }
        long ti = (long)Math.Ceiling(cnt / 5.0 * 2000);
        long tm = (long)Math.Ceiling(cnt / 5.0 * 4000);
        if (c.Iron < ti) { await bot.AnswerCallbackQueryAsync(cb.Id, $"❌ آهن", cancellationToken: ct); return; }
        if (c.Money < tm) { await bot.AnswerCallbackQueryAsync(cb.Id, $"❌ پول", cancellationToken: ct); return; }
        c.Iron -= ti; c.AntiAir += cnt; c.Money -= tm;
        Database.UpdateAntiAirResources(uid, cid, c.Money, c.Iron, c.AntiAir);
        await SendTemp(cb.Message.Chat.Id, $"✅ {cnt} پدافند خریداری شد!", ct: ct);
        await bot.AnswerCallbackQueryAsync(cb.Id, "✅", cancellationToken: ct);
    }

    // ================= NAVAL SHOP –  =================
    static async Task HandleBoatInfoCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length != 3 || cb.Message == null) return;
        long uid = cb.From.Id;
        string bid = parts[2];
        string info = bid switch
        {
            "SBoot" => "🇩🇪 S-Boot (E-Boat)\n⚡ سرعت: 38–41 گره (70–76 km/h)\n🛡 زره: تقریباً هیچ (بدنه فولادی سبک)\n👥 خدمه: 21–24 نفر\n🔫 تسلیحات: 2x لوله اژدر 533mm، 1x توپ 20mm، چند مسلسل 7.92mm\n💰 هر 5 عدد: 2K پول + 1K آهن",
            "PTBoat" => "🇺🇸 PT Boat\n⚡ سرعت: 40–45 گره (74–83 km/h)\n🛡 زره: هیچ\n👥 خدمه: 10–14 نفر\n🔫 تسلیحات: 2–4 اژدر، مسلسل 12.7mm، گاهی توپ 20mm\n💰 هر 5 عدد: 3K پول + 1.5K آهن",
            "G5" => "🇷🇺 G-5\n⚡ سرعت: 50–53 گره (93–98 km/h)\n🛡 زره: هیچ\n👥 خدمه: 6 نفر\n🔫 تسلیحات: 2x اژدر 533mm، 2x مسلسل 7.62mm\n💰 هر 5 عدد: 2.5K پول + 1.5K آهن",
            _ => "قایق ناشناخته"
        };
        await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
        var kb = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("5", $"boat_buy:{uid}:{bid}:5"), InlineKeyboardButton.WithCallbackData("10", $"boat_buy:{uid}:{bid}:10") },
            new[] { InlineKeyboardButton.WithCallbackData("25", $"boat_buy:{uid}:{bid}:25"), InlineKeyboardButton.WithCallbackData("❌ انصراف", $"cancel:{uid}") }
        });
        await SendTemp(cb.Message.Chat.Id, info, markup: kb, ct: ct);
    }

    static async Task HandleBoatBuyCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length != 4 || cb.Message == null) return;
        long uid = cb.From.Id;
        string bid = parts[2];
        if (!TryParseInt(parts[3], out int cnt) || cnt <= 0) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ تعداد", cancellationToken: ct); return; }
        long cid = cb.Message.Chat.Id;
        var c = Database.GetCountry(uid, cid);
        if (c == null) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ کشور", cancellationToken: ct); return; }

        // Price per 5
        double moneyPer5 = bid switch { "SBoot" => 2000, "PTBoat" => 3000, "G5" => 2500, _ => 0 };
        double ironPer5 = bid switch { "SBoot" => 1000, "PTBoat" => 1500, "G5" => 1500, _ => 0 };
        long tm = (long)Math.Ceiling(cnt / 5.0 * moneyPer5);
        long ti = (long)Math.Ceiling(cnt / 5.0 * ironPer5);

        if (c.Money < tm) { await bot.AnswerCallbackQueryAsync(cb.Id, $"❌ پول کم: نیاز {tm}", cancellationToken: ct); return; }
        if (c.Iron < ti) { await bot.AnswerCallbackQueryAsync(cb.Id, $"❌ آهن کم: نیاز {ti}", cancellationToken: ct); return; }

        c.Money -= tm; c.Iron -= ti; c.Boats += cnt;
        Database.UpdateCountryFull(c);
        string modelName = bid switch { "SBoot" => "S-Boot", "PTBoat" => "PT Boat", "G5" => "G-5", _ => bid };
        Database.AddEquipmentModel(uid, cid, "Boats", modelName, cnt);

        await SendTemp(cb.Message.Chat.Id, $"✅ {cnt} قایق {modelName} خریداری شد!\n💰 باقی‌مانده: {(c.Money / 1000.0):F1}K | 🔩 آهن: {(c.Iron / 1000.0):F1}K", ct: ct);
        await bot.AnswerCallbackQueryAsync(cb.Id, "✅ خرید موفق", cancellationToken: ct);
    }

    static async Task HandleSubInfoCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length != 3 || cb.Message == null) return;
        long uid = cb.From.Id;
        string sid = parts[2];
        string info = sid switch
        {
            "VIIC" => "🇩🇪 Type VIIC U-boat\n⚡ سرعت: 17.7 گره روی آب / 7.6 گره زیر آب\n🛡 زره: ندارد (بدنه فشاری 18–22mm فولاد)\n👥 خدمه: 44–52 نفر\n🔫 تسلیحات: 5x لوله اژدر 533mm، 11–14 اژدر، 1x توپ 88mm، 1x توپ 20mm ضدهوایی\n💰 هر 1 عدد: 10K پول + 5K آهن",
            "Gato" => "🇺🇸 Gato\n⚡ سرعت: 21 گره روی آب / 9 گره زیر آب\n🛡 زره: ندارد (بدنه فشاری فولادی)\n👥 خدمه: 55–60 نفر\n🔫 تسلیحات: 8x لوله اژدر 533mm، 24 اژدر، 1x توپ 76mm، مسلسل ضدهوایی\n💰 هر 1 عدد: 10K پول + 5K آهن",
            "SClass" => "🇷🇺 S-class, Series IX\n⚡ سرعت: 13–14 گره روی آب / 7–8 گره زیر آب\n🛡 زره: ندارد (بدنه فشاری فولادی)\n👥 خدمه: 37–44 نفر\n🔫 تسلیحات: 6x لوله اژدر 533mm، 10 اژدر، 1x توپ 45mm، مسلسل ضدهوایی\n💰 هر 1 عدد: 8K پول + 4K آهن",
            _ => "زیردریایی ناشناخته"
        };
        await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
        var kb = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("1", $"sub_buy:{uid}:{sid}:1"), InlineKeyboardButton.WithCallbackData("2", $"sub_buy:{uid}:{sid}:2") },
            new[] { InlineKeyboardButton.WithCallbackData("5", $"sub_buy:{uid}:{sid}:5"), InlineKeyboardButton.WithCallbackData("❌ انصراف", $"cancel:{uid}") }
        });
        await SendTemp(cb.Message.Chat.Id, info, markup: kb, ct: ct);
    }

    static async Task HandleSubBuyCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length != 4 || cb.Message == null) return;
        long uid = cb.From.Id;
        string sid = parts[2];
        if (!TryParseInt(parts[3], out int cnt) || cnt <= 0) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ تعداد", cancellationToken: ct); return; }
        long cid = cb.Message.Chat.Id;
        var c = Database.GetCountry(uid, cid);
        if (c == null) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ کشور", cancellationToken: ct); return; }

        double moneyPer1 = sid switch { "VIIC" => 10000, "Gato" => 10000, "SClass" => 8000, _ => 0 };
        double ironPer1 = sid switch { "VIIC" => 5000, "Gato" => 5000, "SClass" => 4000, _ => 0 };
        long tm = (long)(cnt * moneyPer1);
        long ti = (long)(cnt * ironPer1);

        if (c.Money < tm) { await bot.AnswerCallbackQueryAsync(cb.Id, $"❌ پول کم", cancellationToken: ct); return; }
        if (c.Iron < ti) { await bot.AnswerCallbackQueryAsync(cb.Id, $"❌ آهن کم", cancellationToken: ct); return; }

        c.Money -= tm; c.Iron -= ti; c.Submarines += cnt;
        Database.UpdateCountryFull(c);
        string modelName = sid switch { "VIIC" => "Type VIIC", "Gato" => "Gato", "SClass" => "S-class", _ => sid };
        Database.AddEquipmentModel(uid, cid, "Submarines", modelName, cnt);

        await SendTemp(cb.Message.Chat.Id, $"✅ {cnt} زیردریایی {modelName} خریداری شد!", ct: ct);
        await bot.AnswerCallbackQueryAsync(cb.Id, "✅", cancellationToken: ct);
    }

    static string FormatBattleshipTechnicalSpec(string modelKey)
    {
        string model=modelKey=="Soyuz"?"Sovetsky Soyuz":modelKey;
        var s=WarEngineV2Core.GetBattleshipSpecByModel(model);
        var weapons=new List<string>{$"{s.MainGuns:0} × توپ {s.MainCaliber:0.#} میلی‌متری",$"{s.SecGuns:0} × توپ {s.SecondaryCaliber:0.#} میلی‌متری"};
        if(s.HeavyAACount>0)weapons.Add($"{s.HeavyAACount:0} × توپ {s.HeavyAACaliber:0.#} میلی‌متری ضدهوایی");
        if(s.MediumAACount>0)weapons.Add($"{s.MediumAACount:0} × توپ {s.MediumAACaliber:0.#} میلی‌متری ضدهوایی");
        if(s.LightAACount>0)weapons.Add($"{s.LightAACount:0} × توپ {s.LightAACaliber:0.#} میلی‌متری ضدهوایی");
        if(s.MachineGunCount>0)weapons.Add($"{s.MachineGunCount:0} × مسلسل {s.MachineGunCaliber:0.#} میلی‌متری");
        weapons.Add(s.ReconAircraft>0?$"{s.ReconAircraft:0} × هواپیمای شناسایی":"بدون هواپیمای شناسایی");
        return $"🚢 {s.Name}\nسرعت: {s.Speed:0.#} گره ({s.SpeedKph:0.#} کیلومتر بر ساعت)\n"+
               $"خدمه: {s.Crew:N0} نفر\n\n🛡 زره\nکمربند اصلی: {s.Belt:0}mm\nعرشه: {s.DeckMin:0}-{s.DeckMax:0}mm\n"+
               $"برجک‌ها: {s.Turret:0}mm\nبرج فرماندهی: {s.CommandArmor:0}mm\n\n🔫 تسلیحات\n• {string.Join("\n• ",weapons)}";
    }

    static async Task HandleBattleshipInfoCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length != 3 || cb.Message == null) return;
        long uid = cb.From.Id;
        string bid = parts[2];
        string price=bid switch{"Bismarck"=>"50K پول + 30K آهن","Iowa"=>"50K پول + 40K آهن","Soyuz"=>"45K پول + 25K آهن",_=>"نامشخص"};
        string info=FormatBattleshipTechnicalSpec(bid)+$"\n\n💰 هزینه ساخت: {price}\n⚠️ نیازمند بندر سطح ۴؛ سقف مالکیت، مأموریت و محموله‌های درراه روی‌هم ۳ فروند";
        await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
        var kb = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("1", $"battleship_buy:{uid}:{bid}:1") },
            new[] { InlineKeyboardButton.WithCallbackData("❌ انصراف", $"cancel:{uid}") }
        });
        await SendTemp(cb.Message.Chat.Id, info, markup: kb, ct: ct);
    }

    static async Task HandleBattleshipBuyCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length != 4 || cb.Message == null) return;
        long uid = cb.From.Id;
        string bid = parts[2];
        if (!TryParseInt(parts[3], out int cnt) || cnt != 1) cnt = 1; // battleship only 1 at a time
        long cid = cb.Message.Chat.Id;
        var c = Database.GetCountry(uid, cid);
        if (c == null) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ کشور", cancellationToken: ct); return; }

        if (c.PortLevel < 4)
        {
            await bot.AnswerCallbackQueryAsync(cb.Id, "⚓ برای ساخت نبردناو بندر سطح ۴ لازم است", showAlert: true, cancellationToken: ct);
            return;
        }
        long battleshipCapacityUsed = Database.GetBattleshipCapacityUsed(uid, cid);
        if (battleshipCapacityUsed >= 3)
        {
            await bot.AnswerCallbackQueryAsync(cb.Id, $"❌ حداکثر 3 نبردناو می‌توانید داشته باشید؛ ظرفیت فعلی/درراه: {battleshipCapacityUsed}/3", showAlert: true, cancellationToken: ct);
            return;
        }

        double moneyPer1 = bid switch { "Bismarck" => 50000, "Iowa" => 50000, "Soyuz" => 45000, _ => 0 };
        double ironPer1 = bid switch { "Bismarck" => 30000, "Iowa" => 40000, "Soyuz" => 25000, _ => 0 };
        long tm = (long)moneyPer1;
        long ti = (long)ironPer1;

        string modelName = bid switch { "Bismarck" => "Bismarck", "Iowa" => "Iowa", "Soyuz" => "Sovetsky Soyuz", _ => bid };
        // Group callbacks already hold this country's mutation lock in HandleUpdateAsync.
        // Acquiring it again here deadlocked because SemaphoreSlim is not re-entrant.
        bool purchased = Database.TryPurchaseBattleship(uid, cid, modelName, tm, ti);
        if (!purchased)
        {
            await bot.AnswerCallbackQueryAsync(cb.Id,
                "❌ خرید انجام نشد؛ پول/آهن، سطح بندر یا ظرفیت ۳ نبردناو (شامل ناوهای در دریا و درراه) را بررسی کنید.",
                showAlert: true, cancellationToken: ct);
            return;
        }
        long totalNow = Database.GetBattleshipCapacityUsed(uid, cid);
        await SendTemp(cb.Message.Chat.Id, $"✅ 1 نبردناو {modelName} خریداری شد! (ظرفیت: {totalNow}/3)", ct: ct);
        await bot.AnswerCallbackQueryAsync(cb.Id, "✅", cancellationToken: ct);
    }

    static async Task HandleBattleshipRepairCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length < 2 || cb.Message == null) return;
        long uid = cb.From.Id;
        long cid = cb.Message.Chat.Id;
        var c = Database.GetCountry(uid, cid);
        if (c == null) { await bot.AnswerCallbackQueryAsync(cb.Id, "❌ کشور یافت نشد", showAlert: true, cancellationToken: ct); return; }
        Database.SyncBattleshipUnits(uid, cid);
        var damaged = Database.GetBattleshipUnits(uid, cid, onlyCombatReady: false)
            .Where(x => x.DamagePercent > 0).ToList();
        if (damaged.Count == 0) { await bot.AnswerCallbackQueryAsync(cb.Id, "✅ آسیبی نیست", showAlert: true, cancellationToken: ct); return; }
        var rows = damaged.Select(x => new[]
        {
            InlineKeyboardButton.WithCallbackData($"🔧 {x.Model} شماره {x.ShipNumber} — آسیب {x.DamagePercent}٪",
                $"battleship_repair_quote:{x.UnitId}")
        }).ToList();
        rows.Add(new[] { InlineKeyboardButton.WithCallbackData("❌ لغو", $"cancel:{uid}") });
        await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
        await SendTemp(cb.Message.Chat.Id, "🔧 نبردناو موردنظر برای تعمیر فوری را انتخاب کنید.\nهزینه دقیقاً متناسب با درصد آسیب و قیمت همان مدل است.",
            markup: new InlineKeyboardMarkup(rows), ct: ct);
    }

    static async Task HandleBattleshipRepairQuoteCallback(CallbackQuery cb,string[] parts,CancellationToken ct)
    {
        if(parts.Length<2||cb.Message==null||!TryParseLong(parts[1],out long unitId))return;
        long uid=cb.From.Id,cid=cb.Message.Chat.Id;
        if(!Database.GetBattleshipRepairQuote(unitId,uid,cid,out string model,out int damage,out long money,out long iron))
        {await bot.AnswerCallbackQueryAsync(cb.Id,"❌ ناو قابل تعمیر نیست.",showAlert:true,cancellationToken:ct);return;}
        int shipNumber=Database.GetBattleshipUnits(uid,cid,false).FirstOrDefault(x=>x.UnitId==unitId)?.ShipNumber??0;
        var kb=new InlineKeyboardMarkup(new[]{
            new[]{InlineKeyboardButton.WithCallbackData($"✅ تعمیر فوری — {money:N0} پول + {iron:N0} آهن",$"battleship_repair_unit:{unitId}")},
            new[]{InlineKeyboardButton.WithCallbackData("❌ لغو","cancel")}});
        await bot.AnswerCallbackQueryAsync(cb.Id,cancellationToken:ct);
        await SendTemp(cid,$"🔧 تعمیر {model} شماره {shipNumber}\n💥 آسیب: {damage}٪\n💰 هزینه: {money:N0} پول\n🔩 هزینه: {iron:N0} آهن",markup:kb,ct:ct);
    }

    static async Task HandleBattleshipRepairUnitCallback(CallbackQuery cb, string[] parts, CancellationToken ct)
    {
        if (parts.Length < 2 || cb.Message == null || !TryParseLong(parts[1], out long unitId)) return;
        long uid = cb.From.Id, cid = cb.Message.Chat.Id;
        int shipNumber=Database.GetBattleshipUnits(uid,cid,false).FirstOrDefault(x=>x.UnitId==unitId)?.ShipNumber??0;
        bool repaired = Database.RepairBattleshipUnit(unitId, uid, cid, out long money, out long iron);
        if (!repaired)
        {
            await bot.AnswerCallbackQueryAsync(cb.Id, "❌ منابع کافی نیست، ناو در مأموریت است یا قبلاً تعمیر شده.",
                showAlert: true, cancellationToken: ct);
            return;
        }
        await bot.AnswerCallbackQueryAsync(cb.Id, "✅ تعمیر کامل شد.", cancellationToken: ct);
        await SendTemp(cid, $"✅ نبردناو شماره {shipNumber} فوراً تعمیر شد.\n💰 {money:N0} پول\n🔩 {iron:N0} آهن", ct: ct);
        DeleteNow(cb.Message.Chat.Id, cb.Message.MessageId);
    }

    static async Task HandleBattleshipScrapMenuCallback(CallbackQuery cb,CancellationToken ct)
    {
        if(cb.Message==null)return;long uid=cb.From.Id,cid=cb.Message.Chat.Id;
        var country=Database.GetCountry(uid,cid);if(country==null){await bot.AnswerCallbackQueryAsync(cb.Id,"❌ کشور یافت نشد.",showAlert:true,cancellationToken:ct);return;}
        Database.SyncBattleshipUnits(uid,cid);
        var ships=Database.GetBattleshipUnits(uid,cid,onlyCombatReady:false);
        if(ships.Count==0){await bot.AnswerCallbackQueryAsync(cb.Id,"❌ نبردناو آماده‌ای برای اوراق ندارید.",showAlert:true,cancellationToken:ct);return;}
        var rows=ships.Select(x=>new[]{InlineKeyboardButton.WithCallbackData($"♻️ {x.Model} شماره {x.ShipNumber} — آسیب {x.DamagePercent}٪",$"battleship_scrap:{x.UnitId}")}).ToList();
        rows.Add(new[]{InlineKeyboardButton.WithCallbackData("❌ لغو","cancel")});
        await bot.AnswerCallbackQueryAsync(cb.Id,cancellationToken:ct);
        await SendTemp(cid,"♻️ نبردناو موردنظر را انتخاب کنید. ۵۰٪ قیمت ساخت پول و آهن برمی‌گردد.",markup:new InlineKeyboardMarkup(rows),ct:ct);
    }

    static async Task HandleBattleshipScrapQuoteCallback(CallbackQuery cb,string[] parts,CancellationToken ct)
    {
        if(parts.Length<2||cb.Message==null||!TryParseLong(parts[1],out long unitId))return;
        long uid=cb.From.Id,cid=cb.Message.Chat.Id;
        if(!Database.GetBattleshipScrapQuote(unitId,uid,cid,out string model,out int damage,out long money,out long iron))
        {await bot.AnswerCallbackQueryAsync(cb.Id,"❌ این نبردناو قابل اوراق نیست یا در مأموریت/انتقال است.",showAlert:true,cancellationToken:ct);return;}
        int shipNumber=Database.GetBattleshipUnits(uid,cid,false).FirstOrDefault(x=>x.UnitId==unitId)?.ShipNumber??0;
        var kb=new InlineKeyboardMarkup(new[]{
            new[]{InlineKeyboardButton.WithCallbackData($"✅ اوراق — دریافت {money:N0} پول + {iron:N0} آهن",$"battleship_scrap_confirm:{unitId}")},
            new[]{InlineKeyboardButton.WithCallbackData("❌ لغو","cancel")}});
        await bot.AnswerCallbackQueryAsync(cb.Id,cancellationToken:ct);
        await SendTemp(cid,$"♻️ اوراق {model} شماره {shipNumber}\n💥 آسیب فعلی: {damage}٪\n💰 بازگشت پول: {money:N0}\n🔩 بازگشت آهن: {iron:N0}\n⚠️ این عملیات غیرقابل بازگشت است.",markup:kb,ct:ct);
    }

    static async Task HandleBattleshipScrapConfirmCallback(CallbackQuery cb,string[] parts,CancellationToken ct)
    {
        if(parts.Length<2||cb.Message==null||!TryParseLong(parts[1],out long unitId))return;
        long uid=cb.From.Id,cid=cb.Message.Chat.Id;
        int shipNumber=Database.GetBattleshipUnits(uid,cid,false).FirstOrDefault(x=>x.UnitId==unitId)?.ShipNumber??0;
        // The outer group callback already owns the country mutation lock. Do not re-enter it.
        bool scrapped=Database.ScrapBattleshipUnit(unitId,uid,cid,out string model,out long money,out long iron);
        if(!scrapped)
        {await bot.AnswerCallbackQueryAsync(cb.Id,"❌ اوراق انجام نشد؛ وضعیت ناو یا موجودی تغییر کرده است.",showAlert:true,cancellationToken:ct);return;}
        await bot.AnswerCallbackQueryAsync(cb.Id,"✅ نبردناو اوراق شد.",cancellationToken:ct);
        await SendTemp(cid,$"✅ {model} شماره {shipNumber} اوراق شد.\n💰 {money:N0} پول\n🔩 {iron:N0} آهن بازگردانده شد.",ct:ct);
        DeleteNow(cb.Message.Chat.Id,cb.Message.MessageId);
    }

    // ============================================================
    //  تایمر آپدیت دارایی — نسخه اصلاح‌شده (FIXED)
    // ============================================================
}
