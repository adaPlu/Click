using ClickDungeon.Application;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using NUnit.Framework;

namespace ClickDungeon.Tests
{
    /// <summary>
    /// Prints the slice the balance guards assert, sighted and blind, at the sample sizes those guards use. Run it after
    /// anything that moves the numbers, and set the guard thresholds from what it prints rather than by nudging them.
    /// </summary>
    public class GuardBaselineTests
    {
        /// <summary>Each hero against each tier, so a new class can be judged next to the one it is joining (D-024).</summary>
        [Test, Explicit("Tuning aid: dotnet test --filter Name=HeroSweep --logger \"console;verbosity=detailed\"")]
        public void HeroSweep()
        {
            TestContext.Out.WriteLine("hero            sees     tier      | 40 seeds reach/won | avg turns | avg hp left");
            var catalogs = ContentCatalog.CreateDefault();
            foreach (var identity in catalogs.HeroIdentities.Values)
            foreach (var blind in new[] { false, true })
            foreach (var tier in new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hardcore })
            {
                var catalog = ContentCatalog.CreateDefault(tier);
                int reached = 0, won = 0;
                long turns = 0, hp = 0;
                for (ulong seed = 1; seed <= 40; seed++)
                {
                    var r = AutoPlayer.PlayRun(catalog, seed, BalanceTests.MaxCommands, 0.5,
                        MovementMode.Free, blind, heroId: identity.Id);
                    if (r.Floor >= catalog.RunFloorCount) reached++;
                    if (r.Status == RunStatus.Won) won++;
                    turns += r.Turns;
                    hp += r.Hp;
                }
                TestContext.Out.WriteLine($"{identity.Id,-15} {(blind ? "blind " : "sees  "),-8} {tier,-9} | {reached,6}/{won,-11} | {turns / 40f,9:0.0} | {hp / 40f,11:0.0}");
            }
        }

        [Test, Explicit("Tuning aid: dotnet test --filter Name=GuardBaseline --logger \"console;verbosity=detailed\"")]
        public void GuardBaseline()
        {
            TestContext.Out.WriteLine("sees     skill    tier      | 30 seeds reach/won | 40 seeds reach/won | avg turns");
            foreach (var blind in new[] { false, true })
            foreach (var (skill, rate) in new[] { ("novice", 0.5), ("flailing", 0.7) })
            foreach (var tier in new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hardcore })
            {
                var catalog = ContentCatalog.CreateDefault(tier);
                int reach30 = 0, won30 = 0, reach40 = 0, won40 = 0;
                long turns = 0;
                for (ulong seed = 1; seed <= 40; seed++)
                {
                    var r = AutoPlayer.PlayRun(catalog, seed, BalanceTests.MaxCommands, rate, MovementMode.Free, blind);
                    bool reached = r.Floor >= catalog.RunFloorCount;
                    bool won = r.Status == RunStatus.Won;
                    turns += r.Turns;
                    if (seed <= 30)
                    {
                        if (reached) reach30++;
                        if (won) won30++;
                    }
                    if (reached) reach40++;
                    if (won) won40++;
                }
                TestContext.Out.WriteLine(
                    $"{(blind ? "blind" : "sighted"),-8} {skill,-8} {tier,-9} |      {reach30,2} / {won30,2}       |      {reach40,2} / {won40,2}       | {turns / 40.0,6:0.0}");
            }
        }

        [Test, Explicit("Tuning aid: how blind novice runs end — won, stalled, or killed by what, and where damage came from")]
        public void RunEndings()
        {
            const int runs = 30;
            foreach (var tier in new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hardcore })
            {
                var catalog = ContentCatalog.CreateDefault(tier);
                var endings = new System.Collections.Generic.SortedDictionary<string, int>();
                var damage = new System.Collections.Generic.SortedDictionary<string, int>();
                int floors = 0;
                for (ulong seed = 1; seed <= runs; seed++)
                {
                    var run = RunFactory.NewRun(seed, catalog, new System.Collections.Generic.List<GameEvent>());
                    var player = new AutoPlayer(BalanceTests.NoviceMistakeRate, blind: true);
                    string lastSource = "?";
                    for (int i = 0; i < BalanceTests.MaxCommands && run.Status == RunStatus.InProgress; i++)
                    {
                        var result = TurnResolver.Apply(run, player.Choose(run, catalog, seed * 7919UL + (ulong)i), catalog);
                        foreach (var e in result.Events)
                        {
                            if (e.Kind != GameEventKind.HeroDamaged) continue;
                            lastSource = e.Source ?? "?";
                            damage[lastSource] = (damage.TryGetValue(lastSource, out var d) ? d : 0) + e.Amount;
                        }
                    }
                    string ending = run.Status == RunStatus.Won ? "won" : run.Status == RunStatus.Lost ? "killed by " + lastSource : "stalled";
                    endings[ending] = (endings.TryGetValue(ending, out var n) ? n : 0) + 1;
                    floors += run.Floor.FloorIndex;
                }
                TestContext.Out.WriteLine($"{tier,-9} avg floor {floors / (double)runs:0.0} | endings: {string.Join(", ", endings)} | damage taken: {string.Join(", ", damage)}");
            }
        }

        [Test, Explicit("Tuning aid: the last commands of stalled blind novice runs on Squire's Stroll")]
        public void StallTrace()
        {
            var catalog = ContentCatalog.CreateDefault(Difficulty.Easy);
            int shown = 0;
            for (ulong seed = 1; seed <= 30 && shown < 3; seed++)
            {
                var run = RunFactory.NewRun(seed, catalog, new System.Collections.Generic.List<GameEvent>());
                var player = new AutoPlayer(BalanceTests.NoviceMistakeRate, blind: true);
                var lines = new System.Collections.Generic.List<string>();
                for (int i = 0; i < BalanceTests.MaxCommands && run.Status == RunStatus.InProgress; i++)
                {
                    var command = player.Choose(run, catalog, seed * 7919UL + (ulong)i);
                    int revealed = 0, closedChests = 0;
                    foreach (var p in Board.AllCells)
                    {
                        if (run.Floor[p].Knowledge == Knowledge.Revealed) revealed++;
                        if (run.Floor[p].IsClosedChest && run.Floor[p].Knowledge == Knowledge.Revealed) closedChests++;
                    }
                    var awake = string.Join(" ", run.Floor.Enemies.FindAll(e => e.Awake).ConvertAll(e => $"{e.DefId}@{e.Pos}:{e.Intent.Kind}"));
                    lines.Add($"#{i} F{run.Floor.FloorIndex}{(run.Floor.IsVault ? "v" : "")} hero {run.Hero.Pos} hp {run.Hero.Hp}/{run.Hero.MaxHp} key {run.Hero.HasKey} revealed {revealed} knownChests {closedChests} -> {command} | awake: {awake}");
                    TurnResolver.Apply(run, command, catalog);
                }
                if (run.Status != RunStatus.InProgress) continue;
                shown++;
                TestContext.Out.WriteLine($"--- seed {seed}: stalled on floor {run.Floor.FloorIndex}");
                for (int j = lines.Count - 12; j < lines.Count; j++) TestContext.Out.WriteLine("  " + lines[j]);
            }
        }

        [Test, Explicit("Demo aid: seeds where the -cdBot smart -cdBlind 1 demo opens chests, and on which commands")]
        public void ChestDemoSeeds()
        {
            // Mirrors ClickDungeonApp's automation loop, so the command numbers printed here are valid -cdTurns values.
            var catalog = ContentCatalog.CreateDefault(Difficulty.Medium);
            for (ulong seed = 1; seed <= 40; seed++)
            {
                var run = RunFactory.NewRun(seed, catalog, new System.Collections.Generic.List<GameEvent>());
                var player = new AutoPlayer(0.0, blind: true);
                var taps = new System.Collections.Generic.List<int>();
                var opens = new System.Collections.Generic.List<int>();
                var bumps = new System.Collections.Generic.List<string>();
                for (int i = 0; i < BalanceTests.MaxCommands && run.Status == RunStatus.InProgress; i++)
                {
                    var result = TurnResolver.Apply(run, player.Choose(run, catalog, seed * 7919UL + (ulong)i), catalog);
                    if (result.Events.Exists(e => e.Kind == GameEventKind.ChestTapped)) taps.Add(i + 1);
                    if (result.Events.Exists(e => e.Kind == GameEventKind.ChestOpened)) opens.Add(i + 1);
                    var bump = result.Events.Find(e => e.Kind == GameEventKind.HeroBumped);
                    if (bump != null) bumps.Add($"{i + 1}{(bump.Source == "lurker" ? "L" : "O")}");
                }
                if (opens.Count == 0) continue;
                TestContext.Out.WriteLine(
                    $"seed {seed,2}: taps after {string.Join(",", taps)} | opened after {string.Join(",", opens)} | bumps {string.Join(",", bumps)} | {run.Status} turn {run.Turn} rewards {run.Rewards.Count}");
            }
        }

        [Test, Explicit("Tuning aid: are chests worth opening? dotnet test --filter Name=ChestWorth --logger \"console;verbosity=detailed\"")]
        public void ChestWorth()
        {
            // The same player skipping chests, then looting (only when safe) under candidate reward strengths.
            const int runs = 60;
            TestContext.Out.WriteLine($"Chest worth: blind novice, Free Roam, {runs} seeds — won / reached F5 / rewards per run / avg turns");
            foreach (var tier in new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hardcore })
            // Shipped content against the earlier 1/2/3 rewards with 1 potion and +2 max HP.
            foreach (var (label, loots, oldRewards) in new[]
            {
                ("skips chests", false, false),
                ("loots, shipped rewards", true, false),
                ("loots, old 1/2/3 rewards", true, true),
            })
            {
                var catalog = ContentCatalog.CreateDefault(tier);
                if (oldRewards)
                {
                    var old = new[] { 1, 2, 3 };
                    for (int i = 0; i < old.Length; i++) catalog.ChestRewardsByQuality[i] = old[i];
                    foreach (var entry in catalog.ChestRewards)
                    {
                        if (entry.Kind == RewardKind.Potion) entry.Amount = 1;
                        else if (entry.Kind == RewardKind.MaxHp) entry.Amount = 2;
                    }
                }
                int won = 0, reached = 0;
                long rewards = 0, turns = 0;
                for (ulong seed = 1; seed <= runs; seed++)
                {
                    var r = AutoPlayer.PlayRun(catalog, seed, BalanceTests.MaxCommands, BalanceTests.NoviceMistakeRate,
                        MovementMode.Free, blind: true, loots: loots);
                    if (r.Status == RunStatus.Won) won++;
                    if (r.Floor >= catalog.RunFloorCount) reached++;
                    rewards += r.Rewards;
                    turns += r.Turns;
                }
                TestContext.Out.WriteLine(
                    $"{tier,-9} {label,-24} won {won,2}  reach {reached,2}  rewards/run {rewards / (double)runs,4:0.0}  turns {turns / (double)runs,6:0.0}");
            }
        }
    }
}
