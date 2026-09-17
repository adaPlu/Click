using ClickDungeon.Application;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using NUnit.Framework;

namespace ClickDungeon.Tests
{
    /// <summary>
    /// Prints the slice the balance guards assert, sighted and blind, at the sample sizes those guards use. Run it after
    /// anything that moves the numbers, and set the guard thresholds from what it prints rather than by nudging them.
    /// </summary>
    public class GuardBaselineTests
    {
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
    }
}
