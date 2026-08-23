using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

enum BattleOutcomeKind
{
    AttackerRouted,
    DefenderVictory,
    Stalemate,
    AttackerLimitedVictory,
    AttackerHeavyVictory
}

readonly record struct ModelAmount(string Model, long Count);

sealed class BattleParticipant
{
    public long OwnerId { get; init; }
    public string CountryName { get; init; } = "";
    public string OwnerName { get; init; } = "";
    public Faction Faction { get; init; }
    public long Soldiers { get; init; }
    public List<ModelAmount> Tanks { get; init; } = new();
    public List<ModelAmount> Fighters { get; init; } = new();
    public List<ModelAmount> Bombers { get; init; } = new();
    public long AntiAir { get; init; }
    public bool IsHomelandDefender { get; init; }
    public long Money { get; init; }
    public long Iron { get; init; }
}

sealed class BattleOrders
{
    public int GroundStrategy { get; init; } = 1;
    public int GroundTactic { get; init; } = 1;
    public int AirStrategy { get; init; }
    public int AirTactic { get; init; } = 1;
}

sealed class BattleRequest
{
    public long BattleId { get; init; }
    public long ChatId { get; init; }
    public ulong ScenarioSeed { get; set; }
    public List<BattleParticipant> Attackers { get; init; } = new();
    public List<BattleParticipant> Defenders { get; init; } = new();
    public BattleOrders AttackerOrders { get; init; } = new();
    public BattleOrders DefenderOrders { get; init; } = new();
    public int MaximumDurationMinutes { get; init; } = 24 * 60;
}

sealed class ParticipantBattleLoss
{
    public long OwnerId { get; init; }
    public long SoldiersKilled { get; set; }
    public long SoldiersWounded { get; set; }
    public long SoldiersUnavailable => SoldiersKilled + SoldiersWounded;
    public long AntiAirLost { get; set; }
    public Dictionary<string, long> TanksDestroyed { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, long> TanksDamaged { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, long> TanksUnavailable { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, long> FightersUnavailable { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, long> BombersUnavailable { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

sealed class BattleEvent
{
    public int Minute { get; init; }
    public string Code { get; init; } = "";
    public string Text { get; init; } = "";
}

partial class BattleResult
{
    public BattleOutcomeKind OutcomeKind;
    public double EffectiveAdvanceKm;
    public long CombatReadyReturnedSoldiers;
    public long CombatReadyReturnedTanks;
    public bool AttackerHeavyVictory;
    public bool DefenderVictory;
    public string ScenarioSummary = "";
    public List<BattleEvent> Events { get; init; } = new();
    public Dictionary<long, ParticipantBattleLoss> AttackerParticipantLosses { get; init; } = new();
    public Dictionary<long, ParticipantBattleLoss> DefenderParticipantLosses { get; init; } = new();
    public long AttackerTanksDestroyed;
    public long AttackerTanksDamaged;
    public long AttackerSoldiersKilled;
    public long AttackerSoldiersWounded;
    public long DefenderTanksDestroyed;
    public long DefenderTanksDamaged;
    public long DefenderSoldiersKilled;
    public long DefenderSoldiersWounded;
    public long DefenderBombersLost;
    public double InfrastructureDamage;
}

/// <summary>
/// Adapter between the bot's persisted multi-participant jobs and the requested
/// Combined-Arms v2 engine. All combat decisions are delegated to WarEngineV2Core.
/// </summary>
static class WarEngine
{
    static long _seedCounter = Environment.TickCount64;

    public static ulong CreateScenarioSeed() =>
        unchecked((ulong)Interlocked.Increment(ref _seedCounter) ^
                  (ulong)DateTime.UtcNow.Ticks ^ 0x9E3779B97F4A7C15UL);

    public static string CanonicalTankModel(string model, Faction faction) =>
        WarEngineV2Core.GetTankSpecByModel(string.IsNullOrWhiteSpace(model) ? DefaultTank(faction) : model).Name;

    public static string CanonicalFighterModel(string model, Faction faction) =>
        WarEngineV2Core.GetFighterSpecByModel(string.IsNullOrWhiteSpace(model) ? DefaultFighter(faction) : model).Name;

    public static string CanonicalBomberModel(string model, Faction faction) =>
        WarEngineV2Core.GetBomberSpecByModel(string.IsNullOrWhiteSpace(model) ? DefaultBomber(faction) : model).Name;

    static string DefaultTank(Faction f) => f == Faction.USA ? "M2 Medium" : f == Faction.USSR ? "T-28" : "Panzer III";
    static string DefaultFighter(Faction f) => f == Faction.USA ? "P-36" : f == Faction.USSR ? "I-16" : "Bf 109";
    static string DefaultBomber(Faction f) => f == Faction.USA ? "B-17" : f == Faction.USSR ? "DB-3" : "He 111";

    internal static double MapOperationalToStrategicAdvance(double operationalKm) =>
        Math.Clamp(operationalKm / 30.0 * 40.0,0,40);

    internal static bool IsStrategicHeavyVictory(bool attackerWon,int successPercent,
        double strategicAdvanceKm,long readySoldiers,long readyTanks) =>
        attackerWon && successPercent>=90 && strategicAdvanceKm>=35 &&
        readySoldiers>=5000 && readyTanks>=50;

    public static BattleResult Resolve(BattleRequest request)
    {
        Validate(request);
        BattleParticipant firstA = request.Attackers[0];
        BattleParticipant firstD = request.Defenders[0];
        long aSold = request.Attackers.Sum(x => x.Soldiers);
        long dSold = request.Defenders.Sum(x => x.Soldiers);
        long aTanks = request.Attackers.Sum(x => x.Tanks.Sum(m => m.Count));
        long dTanks = request.Defenders.Sum(x => x.Tanks.Sum(m => m.Count));
        long aFight = request.Attackers.Sum(x => x.Fighters.Sum(m => m.Count));
        long dFight = request.Defenders.Sum(x => x.Fighters.Sum(m => m.Count));
        long aBomb = request.Attackers.Sum(x => x.Bombers.Sum(m => m.Count));
        long dBomb = request.Defenders.Sum(x => x.Bombers.Sum(m => m.Count));
        long dAA = request.Defenders.Sum(x => x.AntiAir);

        var attacker = AggregateCountry(firstA, request.ChatId, aSold, aTanks, aFight, aBomb, 0,
            request.AttackerOrders, request.Attackers.Sum(x => x.Money), request.Attackers.Sum(x => x.Iron), false);
        var defender = AggregateCountry(firstD, request.ChatId, dSold, dTanks, dFight, dBomb, dAA,
            request.DefenderOrders, request.Defenders.Sum(x => x.Money), request.Defenders.Sum(x => x.Iron),
            request.Defenders.Any(x => x.IsHomelandDefender));

        var result = WarEngineV2Core.RunBattleAdvancedSeeded(
            attacker, defender,
            Models(request.Attackers, x => x.Tanks), aSold,
            Models(request.Attackers, x => x.Fighters), Models(request.Attackers, x => x.Bombers),
            Models(request.Defenders, x => x.Tanks), dSold,
            Models(request.Defenders, x => x.Fighters),
            request.AttackerOrders.GroundStrategy, request.AttackerOrders.GroundTactic,
            request.AttackerOrders.AirStrategy, request.AttackerOrders.AirTactic,
            request.ScenarioSeed == 0 ? CreateScenarioSeed() : request.ScenarioSeed);

        // The v2 core exposes aggregate casualties. Split them back over contributors
        // so Program.cs can settle inventories and deployments exactly once.
        // Core v2 reports operational depth on a 0..30 km scale, while the strategic
        // city system uses 0..40 km. Comparing raw 30 against >35 made heavy victories
        // impossible and silently disabled city conquest.
        result.EffectiveAdvanceKm = MapOperationalToStrategicAdvance(result.PenetrationKm);
        result.CombatReadyReturnedSoldiers = Math.Max(0, aSold - result.AttackerSoldiersLost);
        result.CombatReadyReturnedTanks = Math.Max(0, aTanks - result.AttackerTanksLost);
        result.AttackerTanksDestroyed = DestroyedShare(result.AttackerTanksLost);
        result.AttackerTanksDamaged = result.AttackerTanksLost - result.AttackerTanksDestroyed;
        result.DefenderTanksDestroyed = DestroyedShare(result.DefenderTanksLost);
        result.DefenderTanksDamaged = result.DefenderTanksLost - result.DefenderTanksDestroyed;
        result.AttackerHeavyVictory = IsStrategicHeavyVictory(result.AttackerWon,
            result.SuccessPercent,result.EffectiveAdvanceKm,result.CombatReadyReturnedSoldiers,
            result.CombatReadyReturnedTanks);
        result.OutcomeKind = result.AttackerHeavyVictory ? BattleOutcomeKind.AttackerHeavyVictory :
            result.AttackerWon ? BattleOutcomeKind.AttackerLimitedVictory :
            result.AttackerFailed && result.SuccessPercent == 0 ? BattleOutcomeKind.AttackerRouted :
            result.AttackerFailed ? BattleOutcomeKind.DefenderVictory : BattleOutcomeKind.Stalemate;
        result.DefenderVictory = result.AttackerFailed;
        result.ScenarioSummary = "Combined-Arms Battle Engine v2";

        AllocateLosses(request.Attackers, result.AttackerSoldiersLost,
            result.AttackerTanksLost, result.AttackerTanksDestroyed,
            result.AttackerFightersLost, result.AttackerBombersLost, 0,
            result.AttackerParticipantLosses);
        AllocateLosses(request.Defenders, result.DefenderSoldiersLost,
            result.DefenderTanksLost, result.DefenderTanksDestroyed,
            result.DefenderFightersLost, result.DefenderBombersLost,
            result.DefenderAntiAirLost, result.DefenderParticipantLosses);
        FillSoldierSplits(result);
        result.Events.Add(new BattleEvent
        {
            Minute = result.DurationMinutes,
            Code = "END",
            Text = result.AttackerWon ? "پیروزی مهاجم" : result.AttackerFailed ? "پیروزی مدافع" : "نتیجه نسبی"
        });
        return result;
    }

    static Country AggregateCountry(BattleParticipant first, long chatId, long soldiers,
        long tanks, long fighters, long bombers, long antiAir, BattleOrders orders,
        long money, long iron, bool homeland) => new()
    {
        OwnerId = first.OwnerId,
        ChatId = chatId,
        Name = first.CountryName,
        OwnerName = first.OwnerName,
        Faction = first.Faction,
        Soldiers = soldiers,
        Tanks = tanks,
        Planes = fighters,
        Bombers = bombers,
        AntiAir = antiAir,
        DefenseSoldiers = soldiers,
        DefenseTanks = tanks,
        DefenseFighters = fighters,
        DefenseStrategy = orders.GroundStrategy,
        DefenseTactic = orders.GroundTactic,
        AirDefStrategy = orders.AirStrategy,
        AirDefTactic = orders.AirTactic,
        Money = Math.Max(0, money),
        Iron = Math.Max(0, iron),
        Cities = homeland ? 2 : 4
    };

    static List<(string Model, long Count)> Models(IReadOnlyList<BattleParticipant> side,
        Func<BattleParticipant, List<ModelAmount>> selector) =>
        side.SelectMany(p => selector(p).Select(m => (m.Model, m.Count)))
            .Where(x => x.Count > 0)
            .GroupBy(x => x.Model, StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.Key, g.Sum(x => x.Count))).ToList();

    static long DestroyedShare(long loss) => Math.Min(loss, (long)Math.Round(loss * 0.70));

    static void AllocateLosses(IReadOnlyList<BattleParticipant> side, long soldierLoss,
        long tankLoss, long tankDestroyed, long fighterLoss, long bomberLoss, long aaLoss,
        Dictionary<long, ParticipantBattleLoss> output)
    {
        foreach (var p in side)
            output.TryAdd(p.OwnerId, new ParticipantBattleLoss { OwnerId = p.OwnerId });
        AllocateScalar(side, p => p.Soldiers, soldierLoss, (p, n) =>
        {
            output[p.OwnerId].SoldiersKilled += n / 4;
            output[p.OwnerId].SoldiersWounded += n - n / 4;
        });
        AllocateScalar(side, p => p.AntiAir, aaLoss,
            (p, n) => output[p.OwnerId].AntiAirLost += n);

        long remainingLoss = Math.Min(tankLoss, side.Sum(p => p.Tanks.Sum(x => x.Count)));
        long remainingDestroyed = Math.Min(remainingLoss, tankDestroyed);
        AllocateModels(side, p => p.Tanks, remainingLoss, 0, (p, model, n) =>
        {
            long destroyed = remainingLoss == n ? remainingDestroyed :
                Math.Min(n, (long)Math.Round((double)remainingDestroyed * n / Math.Max(1, remainingLoss)));
            Add(output[p.OwnerId].TanksDestroyed, model, destroyed);
            Add(output[p.OwnerId].TanksDamaged, model, n - destroyed);
            Add(output[p.OwnerId].TanksUnavailable, model, n);
            remainingDestroyed -= destroyed;
            remainingLoss -= n;
        });
        AllocateModels(side, p => p.Fighters, fighterLoss, 1,
            (p, model, n) => Add(output[p.OwnerId].FightersUnavailable, model, n));
        AllocateModels(side, p => p.Bombers, bomberLoss, 2,
            (p, model, n) => Add(output[p.OwnerId].BombersUnavailable, model, n));
    }

    static void AllocateScalar(IReadOnlyList<BattleParticipant> side,
        Func<BattleParticipant, long> capacityOf, long requested,
        Action<BattleParticipant, long> assign)
    {
        long capacity = side.Sum(capacityOf), remaining = Math.Min(Math.Max(0, requested), capacity);
        foreach (var p in side)
        {
            long available = Math.Max(0, capacityOf(p));
            long amount = capacity == available ? remaining :
                Math.Min(available, (long)Math.Round((double)remaining * available / Math.Max(1, capacity)));
            amount = Math.Min(amount, remaining);
            assign(p, amount); remaining -= amount; capacity -= available;
        }
    }

    static void AllocateModels(IReadOnlyList<BattleParticipant> side,
        Func<BattleParticipant, List<ModelAmount>> modelsOf, long requested, int kind,
        Action<BattleParticipant, string, long> assign)
    {
        long capacity = side.Sum(p => modelsOf(p).Sum(x => Math.Max(0, x.Count)));
        long remaining = Math.Min(Math.Max(0, requested), capacity);
        foreach (var p in side)
        foreach (var item in modelsOf(p).Where(x => x.Count > 0))
        {
            long amount = capacity == item.Count ? remaining :
                Math.Min(item.Count, (long)Math.Round((double)remaining * item.Count / Math.Max(1, capacity)));
            amount = Math.Min(amount, remaining);
            string model = kind == 1 ? CanonicalFighterModel(item.Model, p.Faction) :
                kind == 2 ? CanonicalBomberModel(item.Model, p.Faction) :
                CanonicalTankModel(item.Model, p.Faction);
            assign(p, model, amount); remaining -= amount; capacity -= item.Count;
        }
    }

    static void Add(Dictionary<string, long> values, string model, long amount)
    {
        if (amount > 0) values[model] = values.GetValueOrDefault(model) + amount;
    }

    static void FillSoldierSplits(BattleResult result)
    {
        result.AttackerSoldiersKilled = result.AttackerParticipantLosses.Values.Sum(x => x.SoldiersKilled);
        result.AttackerSoldiersWounded = result.AttackerParticipantLosses.Values.Sum(x => x.SoldiersWounded);
        result.DefenderSoldiersKilled = result.DefenderParticipantLosses.Values.Sum(x => x.SoldiersKilled);
        result.DefenderSoldiersWounded = result.DefenderParticipantLosses.Values.Sum(x => x.SoldiersWounded);
    }

    static void Validate(BattleRequest request)
    {
        if (request.Attackers.Count == 0 || request.Defenders.Count == 0)
            throw new ArgumentException("Both battle sides are required.");
        ValidateOrders(request.AttackerOrders);
        ValidateOrders(request.DefenderOrders);
        foreach (var p in request.Attackers.Concat(request.Defenders))
            if (p.Soldiers < 0 || p.AntiAir < 0 || p.Money < 0 || p.Iron < 0 ||
                p.Tanks.Any(x => x.Count < 0) || p.Fighters.Any(x => x.Count < 0) ||
                p.Bombers.Any(x => x.Count < 0))
                throw new ArgumentException("Negative force count.");
        if (request.Attackers.Sum(x => x.Soldiers + x.Tanks.Sum(t => t.Count)) <= 0)
            throw new ArgumentException("Attacker ground force is required.");
    }

    static void ValidateOrders(BattleOrders orders)
    {
        if (orders.GroundStrategy is < 1 or > 2 || orders.GroundTactic is < 1 or > 2)
            throw new ArgumentException("Ground strategy and tactic must be 1 or 2.");
        if (orders.AirStrategy is < 0 or > 2 || orders.AirTactic is < 1 or > 2)
            throw new ArgumentException("Air strategy must be 0..2 and tactic 1..2.");
    }

    public static void RunSelfTest(int seedsPerCase = 20)
    {
        seedsPerCase = Math.Clamp(seedsPerCase, 1, 200);
        Console.WriteLine($"Combined-Arms Battle Engine v2 — smoke test ({seedsPerCase} seeds)");
        int wins = 0;
        for (int i = 0; i < seedsPerCase; i++)
        {
            var request = TestRequest((ulong)(i + 1));
            BattleResult r = Resolve(request);
            if (r.AttackerWon) wins++;
            if (r.AttackerTanksLost > 4000 || r.DefenderTanksLost > 2000 ||
                r.AttackerSoldiersLost > 40000 || r.DefenderSoldiersLost > 20000)
                throw new InvalidOperationException("Self-test casualty invariant failed.");
        }
        Console.WriteLine($"حمله ۲:۱ — برد مهاجم: {wins}/{seedsPerCase} ({wins * 100.0 / seedsPerCase:F1}٪)");
    }

    static BattleRequest TestRequest(ulong seed) => new()
    {
        BattleId = (long)seed,
        ChatId = -1,
        ScenarioSeed = seed,
        Attackers = new List<BattleParticipant>
        {
            new() { OwnerId = 1, CountryName = "مهاجم", Faction = Faction.USSR,
                Soldiers = 40000, Tanks = new() { new("T-28", 4000) } }
        },
        Defenders = new List<BattleParticipant>
        {
            new() { OwnerId = 2, CountryName = "مدافع", Faction = Faction.USSR,
                Soldiers = 20000, Tanks = new() { new("T-28", 2000) }, AntiAir = 100,
                Money = 500000, Iron = 200000 }
        },
        AttackerOrders = new BattleOrders { GroundStrategy = 1, GroundTactic = 1 },
        DefenderOrders = new BattleOrders { GroundStrategy = 1, GroundTactic = 1,
            AirStrategy = 1, AirTactic = 1 }
    };
}

static class BattleExecutionScheduler
{
    sealed class WorkItem
    {
        public required BattleRequest Request { get; init; }
        public required TaskCompletionSource<BattleResult> Completion { get; init; }
        public CancellationToken CancellationToken { get; init; }
    }

    static readonly Channel<WorkItem> Queue = Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(128)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = false,
        SingleWriter = false
    });

    static BattleExecutionScheduler()
    {
        int automatic = Math.Max(1, Math.Min(4, Environment.ProcessorCount - 1));
        int workers = int.TryParse(Environment.GetEnvironmentVariable("ALICE_BATTLE_WORKERS"), out int configured)
            ? Math.Clamp(configured, 1, 4) : automatic;
        for (int i = 0; i < workers; i++) _ = Task.Run(WorkerAsync);
    }

    public static async Task<BattleResult> EnqueueAsync(BattleRequest request,
        CancellationToken cancellationToken = default)
    {
        var completion = new TaskCompletionSource<BattleResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        await Queue.Writer.WriteAsync(new WorkItem
        {
            Request = request,
            Completion = completion,
            CancellationToken = cancellationToken
        }, cancellationToken);
        return await completion.Task.WaitAsync(cancellationToken);
    }

    static async Task WorkerAsync()
    {
        await foreach (var item in Queue.Reader.ReadAllAsync())
        {
            if (item.CancellationToken.IsCancellationRequested)
            {
                item.Completion.TrySetCanceled(item.CancellationToken);
                continue;
            }
            try { item.Completion.TrySetResult(WarEngine.Resolve(item.Request)); }
            catch (Exception ex) { item.Completion.TrySetException(ex); }
        }
    }
}
