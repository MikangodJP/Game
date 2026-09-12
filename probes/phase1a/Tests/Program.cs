using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using Phase1A;
using Phase1A.Encounter;
using Phase1A.Rules;

// Separate OS process entry for the determinism test; no test results mixed into stdout.
if (args is ["--child-battle", var seedText, var cultureName])
{
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
    CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
    Console.OutputEncoding = new UTF8Encoding(false);
    Console.Write(CanonicalLog.Format(Scenario.Run(ulong.Parse(seedText, CultureInfo.InvariantCulture))));
    return 0;
}

var tests = new (string Name, Action Run)[]
{
    ("Battle finishes in victory after both enemies die", () =>
    {
        var result = Scenario.Run(Scenario.GoldenSeed);
        Equal(Outcome.Victory, result.Outcome);
        Equal(2, result.EventLog.Count(e => e.Kind == "Died" && e.Target != 0));
        Equal("BattleEnded", result.EventLog[^1].Kind);
    }),
    ("Damage reports actual HP loss and skips a killed target on the next node", () =>
    {
        var state = Fixture(enemyHp: 5);
        var results = Run(state,
            new(OpKind.Damage, new(TargetScope.Selected), new(Base: 100)),
            new(OpKind.Damage, new(TargetScope.PriorTargets, 0), new(Base: 100)));
        Equal(0, state.Read(1).Hp);
        Equal(new OpResult(OpStatus.Applied, 5, 1, true), results[0].Result);
        Equal(OpStatus.NoOp, results[1].Result.Status);
        Check(state.Events.Any(e => e.Kind == "EffectSkipped"), "dead-target skip must be logged");
    }),
    ("Caster is frozen but a self-target sees the preceding debuff", () =>
    {
        var state = Fixture(heroHp: 30, heroDefense: 4);
        Run(state,
            new(OpKind.ApplyStatus, new(TargetScope.Self), new(Base: 3), Status: Scenario.Weakened),
            new(OpKind.Damage, new(TargetScope.Self), new(CasterStrength: 1)));
        // Starting attack 10 remains the magnitude; new defense is 4 - 3 = 1.
        Equal(21, state.Read(0).Hp);
        Equal(7, state.Read(0).Strength);
    }),
    ("Later node reads the target defense after the status lands", () =>
    {
        var state = Fixture(enemyHp: 30, enemyDefense: 4);
        Run(state,
            new(OpKind.ApplyStatus, new(TargetScope.Selected), new(Base: 3), Status: Scenario.Weakened),
            new(OpKind.Damage, new(TargetScope.PriorTargets, 0), new(Base: 10)));
        Equal(21, state.Read(1).Hp);
    }),
    ("Prior amount uses applied damage and rounds midpoint away from zero", () =>
    {
        var state = Fixture(heroHp: 20, enemyHp: 5);
        Run(state,
            new(OpKind.Damage, new(TargetScope.Selected), new(Base: 100)),
            new(OpKind.Heal, new(TargetScope.Self), new(PriorNode: 0, PriorAmount: 0.5)));
        Equal(23, state.Read(0).Hp); // round(5 * 0.5) = 3, not requested damage / 2
    }),
    ("Required rejection aborts remaining nodes without rolling back damage", () =>
    {
        var state = Fixture(heroHp: 20, heroMp: 0);
        var results = Run(state,
            new(OpKind.Damage, new(TargetScope.Selected), new(Base: 3)),
            new(OpKind.SpendMana, new(TargetScope.Self), new(Base: 1), Required: true),
            new(OpKind.Heal, new(TargetScope.Self), new(Base: 7)));
        Equal(27, state.Read(1).Hp);
        Equal(20, state.Read(0).Hp);
        Equal(2, results.Length);
        Equal(OpStatus.Rejected, results[1].Result.Status);
    }),
    ("Required NoOp continues and an optional rejection also continues", () =>
    {
        var state = Fixture(heroMp: 0);
        Run(state,
            new(OpKind.Heal, new(TargetScope.Self), new(Base: 7), Required: true),
            new(OpKind.SpendMana, new(TargetScope.Self), new(Base: 1)),
            new(OpKind.Damage, new(TargetScope.Selected), new(Base: 3)));
        Equal(27, state.Read(1).Hp);
    }),
    ("Prior target list preserves dead IDs while each next node checks current life", () =>
    {
        var state = new BattleState(new([
            Seed(Side.Adventurers, 30, 30, 0, 10, 0, "hero"),
            Seed(Side.Monsters, 5, 5, 0, 1, 0), Seed(Side.Monsters, 20, 20, 0, 1, 0)], 7));
        var results = Run(state,
            new(OpKind.Damage, new(TargetScope.Enemies), new(Base: 6)),
            new(OpKind.Damage, new(TargetScope.PriorTargets, 0), new(Base: 2)));
        Equal(2, results.Length);
        Check(results[0].Targets.SequenceEqual(new[] { 1, 2 }), "first selector order");
        Check(results[1].Targets.SequenceEqual(new[] { 1, 2 }), "reuse must not reselect only the survivor");
        Equal(11, results[0].Result.Amount);
        Equal(12, state.Read(2).Hp);
    }),
    ("AffectsDead allows processing an already dead target", () =>
    {
        var state = Fixture(enemyHp: 5);
        Run(state,
            new(OpKind.Damage, new(TargetScope.Selected), new(Base: 100)),
            new(OpKind.Heal, new(TargetScope.PriorTargets, 0), new(Base: 3), AffectsDead: true));
        Equal(3, state.Read(1).Hp);
    }),
    ("Copied setup remains unchanged and results expose only persistent vitals", () =>
    {
        var setup = Scenario.Setup(Scenario.GoldenSeed);
        var state = new BattleState(setup);
        Check(state.TakeTurn(new(0, Scenario.Crush, 1)), "legal turn must run");
        Equal(38, setup.Actors[1].InitialStats.MaxHp);
        Equal(12, setup.Actors[0].InitialStats.MaxMp);
        Equal<int?>(null, setup.Actors[1].Hp);
        Equal<int?>(null, setup.Actors[0].Mp);
        var result = state.Finish();
        Equal(1, result.Deltas.Length);
        Equal(new VitalsChanged("adventurer-1", 80, 10), result.Deltas[0]);
        Equal(Outcome.Aborted, result.Outcome);
    }),
    ("Illegal turn and insufficient action cost do not advance or mutate combat", () =>
    {
        var state = Fixture(heroMp: 0);
        Check(!state.TakeTurn(new(1, Scenario.Strike, 0)), "wrong actor must be rejected");
        Check(!state.TakeTurn(new(0, Scenario.Drain, 1)), "unaffordable action must be rejected");
        Equal(0, state.NextActorId);
        Equal(30, state.Read(1).Hp);
        Equal(0, state.Read(0).Mp);
        Check(state.TakeTurn(new(0, Scenario.Strike, 1)), "rejections must leave the legal turn usable");
    }),
    ("Weakness freezes its amount then expires after two affected actor turns", () =>
    {
        var state = Fixture(enemyHp: 100, enemyDefense: 4);
        Check(state.TakeTurn(new(0, Scenario.Crush, 1)), "crush");
        Equal(3, state.Read(1).Strength); // 8 - round(10 * .5)
        Check(state.TakeTurn(new(1, Scenario.Strike, 0)), "enemy turn one");
        Equal(3, state.Read(1).Strength);
        Check(state.TakeTurn(new(0, Scenario.Strike, 1)), "hero turn two");
        Check(state.TakeTurn(new(1, Scenario.Strike, 0)), "enemy turn two");
        Equal(8, state.Read(1).Strength);
        Check(state.Events.Any(e => e.Kind == "StatusExpired"), "expiry event");
    }),
    ("A defeat resolves and dead actors never receive another turn", () =>
    {
        var state = Fixture(heroHp: 1, enemyHp: 100);
        Check(state.TakeTurn(new(0, Scenario.Strike, 1)), "hero turn");
        Check(state.TakeTurn(new(1, Scenario.Strike, 0)), "enemy turn");
        Equal(Outcome.Defeat, state.Finish().Outcome);
        Check(!state.TakeTurn(new(0, Scenario.Strike, 1)), "finished battle rejects commands");
    }),
    ("Unrelated random streams cannot perturb combat and seeds affect combat", () =>
    {
        var a = new DeterministicRng(12, "battle.effect");
        var unrelated = new DeterministicRng(12, "unrelated");
        for (var i = 0; i < 30; i++) unrelated.NextInclusive(1000);
        var b = new DeterministicRng(12, "battle.effect");
        Check(Enumerable.Range(0, 20).All(_ => a.NextInclusive(1000) == b.NextInclusive(1000)), "isolated streams");
        Check(CanonicalLog.Format(Scenario.Run(12)) != CanonicalLog.Format(Scenario.Run(13)), "seed must affect actual events");
    }),
    ("A sealed result also seals the state's NoOp event path", () =>
    {
        var state = Fixture();
        var result = state.Finish();
        var rejected = false;
        try { Run(state, new EffectNode(OpKind.Heal, new(TargetScope.Self), new(Base: 1))); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "NoOp after Finish must be rejected too");
        Check(state.Events.SequenceEqual(result.EventLog), "sealed log must not diverge");
        Check(state.Finish().EventLog.SequenceEqual(result.EventLog), "Finish must be idempotent");
    }),
    ("Action cost remains paid when a required effect rejects", () =>
    {
        var state = Fixture(heroHp: 20, heroMp: 5);
        var ability = new Ability("fixture:required", 2,
            [new(OpKind.Damage, new(TargetScope.Selected), new(Base: 3)),
             new(OpKind.SpendMana, new(TargetScope.Self), new(Base: 4), Required: true),
             new(OpKind.Heal, new(TargetScope.Self), new(Base: 7))]);
        Check(state.TakeTurn(new(0, ability, 1)), "committed action must complete cleanup");
        Equal(3, state.Read(0).Mp);
        Equal(27, state.Read(1).Hp);
        Equal(20, state.Read(0).Hp);
        Equal(1, state.NextActorId);
    }),
    ("Defend spends one action and protects against each enemy until the next accepted action", () =>
    {
        var state = new BattleState(new([
            Seed(Side.Adventurers, 30, 30, 5, 10, 0, "hero"),
            Seed(Side.Monsters, 30, 30, 0, 8, 0),
            Seed(Side.Monsters, 30, 30, 0, 8, 0)], 7));
        var hit = new Ability("fixture:hit", 0, [new(OpKind.Damage, new(TargetScope.Selected), new(Base: 8))]);
        Check(state.TakeTurn(new(0, null, -1, CommandKind.Defend)), "defend accepted");
        Equal(1, state.NextActorId);
        Equal(5, state.Read(0).Mp);
        Check(state.Read(0).Guarding, "guard must be visible in the snapshot");
        Check(state.TakeTurn(new(1, hit, 0)), "first enemy action");
        Check(state.TakeTurn(new(2, hit, 0)), "second enemy action");
        Equal(22, state.Read(0).Hp);
        Equal(0, state.NextActorId);
        Check(!state.TakeTurn(new(0, Scenario.Strike, 99)), "invalid target rejected");
        Check(state.Read(0).Guarding, "rejected action must not consume guard");
        Check(state.TakeTurn(new(0, Scenario.Strike, 1)), "next accepted action");
        Check(!state.Read(0).Guarding, "guard expires when an accepted action starts");
        Equal(2, state.Events.Count(e => e.Kind == "GuardBlocked"));
        Equal(1, state.Events.Count(e => e.Kind == "GuardEnded"));
        var guardEnd = state.Events.Single(e => e.Kind == "GuardEnded").Sequence;
        Equal("ActionStarted", state.Events[guardEnd + 1].Kind);
        Check(state.TakeTurn(new(1, hit, 0)), "unguarded hit");
        Equal(14, state.Read(0).Hp);
    }),
    ("Guard halves post-defense damage before HP clamping and reports actual applied amount", () =>
    {
        var state = Fixture(heroHp: 5, heroDefense: 2);
        Check(state.TakeTurn(new(0, null, -1, CommandKind.Defend)), "defend");
        var nodes = ImmutableArray.Create(new EffectNode(OpKind.Damage, new(TargetScope.Selected), new(Base: 10)));
        var results = EffectRunner.Apply(state, 1, 0, nodes, new DeterministicRng(7, "battle.effect"));
        Equal(1, state.Read(0).Hp); // (10 - 2) / 2 = 4, then clamp to current HP 5.
        Equal(new OpResult(OpStatus.Applied, 4, 1, false), results[0].Result);
        var lethal = EffectRunner.Apply(state, 1, 0, nodes, new DeterministicRng(7, "battle.effect"));
        Equal(new OpResult(OpStatus.Applied, 1, 1, true), lethal[0].Result);
        Equal(1, state.Events.Count(e => e.Kind == "Died"));
    }),
    ("Guard rounds retained odd damage down and may fully block one damage", () =>
    {
        var state = Fixture(heroDefense: 2);
        Check(state.TakeTurn(new(0, null, -1, CommandKind.Defend)), "defend");
        var results = EffectRunner.Apply(state, 1, 0,
            [new(OpKind.Damage, new(TargetScope.Selected), new(Base: 7)),
             new(OpKind.Damage, new(TargetScope.Selected), new(Base: 3))],
            new DeterministicRng(7, "battle.effect"));
        Equal(new OpResult(OpStatus.Applied, 2, 1, false), results[0].Result);
        Equal(new OpResult(OpStatus.NoOp), results[1].Result);
        Equal(28, state.Read(0).Hp);
        Check(state.Events.Where(e => e.Kind == "GuardBlocked").Select(e => e.Amount).SequenceEqual(new[] { 3, 1 }),
            "blocked event must contain prevented post-defense damage before HP clamping");
    }),
    ("Guarded prior-node amount uses actual damage for subsequent healing", () =>
    {
        var state = new BattleState(new([
            Seed(Side.Adventurers, 30, 30, 0, 10, 0, "hero"),
            Seed(Side.Monsters, 10, 30, 0, 8, 0)], 7));
        Check(state.TakeTurn(new(0, null, -1, CommandKind.Defend)), "defend");
        var results = EffectRunner.Apply(state, 1, 0,
            [new(OpKind.Damage, new(TargetScope.Selected), new(Base: 9)),
             new(OpKind.Heal, new(TargetScope.Self), new(PriorNode: 0, PriorAmount: 1))],
            new DeterministicRng(7, "battle.effect"));
        Equal(4, results[0].Result.Amount);
        Equal(14, state.Read(1).Hp);
    }),
    ("Defend ticks the existing status and status snapshots remain immutable", () =>
    {
        var state = Fixture();
        Equal<StatusSnapshot?>(null, state.ReadStatus(0));
        state.ApplyStatus(0, Scenario.Weakened, 3, 1);
        var snapshot = state.ReadStatus(0);
        Equal(new StatusSnapshot(Scenario.Weakened.Id, 3, 2, 1), snapshot);
        Check(state.TakeTurn(new(0, null, -1, CommandKind.Defend)), "defend");
        Equal(1, state.ReadStatus(0)!.RemainingTurns);
        Equal(2, snapshot!.RemainingTurns);
        Check(state.Read(0).Guarding, "guard must not occupy the existing status slot");
    }),
    ("Run always escapes and immediately seals a distinct immutable result", () =>
    {
        var state = Fixture();
        Check(!state.TakeTurn(new(1, null, -1, CommandKind.Run)), "out of turn escape rejected");
        Check(state.TakeTurn(new(0, null, -1, CommandKind.Run)), "escape accepted");
        Check(state.IsFinished, "run must finish immediately");
        Equal(-1, state.NextActorId);
        var result = state.Finish();
        Equal(Outcome.Fled, result.Outcome);
        Equal(new VitalsChanged("hero", 30, 5), result.Deltas.Single());
        Equal("Fled", result.EventLog[^2].Kind);
        Equal("BattleEnded", result.EventLog[^1].Kind);
        Equal("Fled", result.EventLog[^1].Detail);
        Check(!state.TakeTurn(new(0, Scenario.Strike, 1)), "finished battle rejects attack");
        var rejected = false;
        try { state.ChangeHp(0, -1, 1, "Damaged"); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Run seals state mutation immediately");
        Check(state.Events.SequenceEqual(result.EventLog), "result and authoritative events remain identical");
    }),
    ("Malformed harness commands are rejected without consuming the turn", () =>
    {
        var state = Fixture();
        Check(!state.TakeTurn(new(0, null, 1)), "ability command needs an ability");
        Check(!state.TakeTurn(new(0, null, -1, (CommandKind)99)), "unknown command kind rejected");
        Equal(0, state.NextActorId);
        Equal(30, state.Read(0).Hp);
        Check(state.TakeTurn(new(0, Scenario.Strike, 1)), "ordinary strike remains available");
    }),
    ("Reviewed battle matches the fixed golden bytes and persistent outcome", () =>
    {
        var result = Scenario.Run(Scenario.GoldenSeed);
        var baseline = GoldenBytes();
        Check(baseline.SequenceEqual(Encoding.UTF8.GetBytes(CanonicalLog.Format(result))), "golden event log changed");
        Equal(new VitalsChanged("adventurer-1", 46, 0), result.Deltas.Single());
    }),
    ("Two fresh processes match each other and golden across process cultures", () =>
    {
        var a = ChildBattle("en-US");
        var b = ChildBattle("tr-TR");
        Check(a.SequenceEqual(b), "fresh process outputs differ");
        Check(a.SequenceEqual(GoldenBytes()), "fresh process differs from golden");
    })
}.Concat(StatTests.All).Concat(EquipmentTests.All).Concat(FieldTests.All)
    .Concat(MagicTests.All).Concat(CombatStyleTests.All).ToArray();

var failures = 0;
foreach (var (name, run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception error) { failures++; Console.Error.WriteLine($"FAIL {name}: {error.Message}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed");
return failures == 0 ? 0 : 1;

static ActorSeed Seed(Side side, int hp, int maxHp, int mp, int attack, int defense, string? instanceId = null) =>
    new("fixture", instanceId, side, new(maxHp, mp, attack, defense, 0, 0, 0), hp, mp);
static BattleState Fixture(int heroHp = 30, int heroMp = 5, int heroDefense = 0, int enemyHp = 30, int enemyDefense = 0) =>
    new(new([Seed(Side.Adventurers, heroHp, 30, heroMp, 10, heroDefense, "hero"),
             Seed(Side.Monsters, enemyHp, enemyHp, 0, 8, enemyDefense)], 7));
static ImmutableArray<NodeResult> Run(BattleState state, params EffectNode[] nodes) =>
    EffectRunner.Apply(state, 0, 1, [.. nodes], new DeterministicRng(7, "battle.effect"));
static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
static void Equal<T>(T expected, T actual) => Check(EqualityComparer<T>.Default.Equals(expected, actual), $"expected {expected}, got {actual}");
static byte[] GoldenBytes()
{
    var file = Path.Combine(AppContext.BaseDirectory, "golden", "battle-20260909.log");
    Check(File.Exists(file), "reviewed golden event log has not been recorded");
    return File.ReadAllBytes(file);
}
static byte[] ChildBattle(string culture)
{
    var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
    {
        UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
    };
    info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
    info.ArgumentList.Add("--child-battle");
    info.ArgumentList.Add(Scenario.GoldenSeed.ToString(CultureInfo.InvariantCulture));
    info.ArgumentList.Add(culture);
    using var process = Process.Start(info) ?? throw new Exception("Cannot start child process");
    using var bytes = new MemoryStream();
    var copy = process.StandardOutput.BaseStream.CopyToAsync(bytes);
    var error = process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit(10000))
    {
        process.Kill(entireProcessTree: true);
        throw new Exception("Child battle exceeded 10 seconds");
    }
    copy.GetAwaiter().GetResult();
    Check(process.ExitCode == 0, $"child failed: {error.GetAwaiter().GetResult()}");
    return bytes.ToArray();
}
