using System.Collections.Generic;
using System.Linq;
using ClickDungeon.Application;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using NUnit.Framework;

namespace ClickDungeon.Tests
{
    public class ScratchTune
    {
        [Test]
        public void Measure()
        {
            const int runs = 24;
            var candidates = new List<(string name, System.Action<ContentCatalog> tweak)>
            {
                ("shipped (flat, bomb 3)", c => { }),
                ("hearts/20", c => c.StirHeartsPerPressure = 20),
                ("hearts/25", c => c.StirHeartsPerPressure = 25),
                ("hearts/30", c => c.StirHeartsPerPressure = 30),
                ("hearts/25 + bomb 2", c => { c.StirHeartsPerPressure = 25; c.Skill("eng_bomb").Amount = 2; }),
                ("flat + bomb 2", c => c.Skill("eng_bomb").Amount = 2),
            };

            foreach (var (name, tweak) in candidates)
            {
                var c = ContentCatalog.CreateDefault(Difficulty.Medium);
                tweak(c);
                var line = new System.Text.StringBuilder($"{name,-24}");
                foreach (var (label, rate) in new[] { ("cas", AutoPlayer.CasualMistakeRate), ("car", BalanceTests.NoviceMistakeRate) })
                {
                    var wins = new List<int>();
                    foreach (var heroClass in c.HeroClasses.Values)
                    {
                        string heroId = c.HeroIdentities.Values.First(h => h.ClassId == heroClass.Id).Id;
                        int won = 0;
                        for (ulong seed = 1; seed <= runs; seed++)
                            if (AutoPlayer.PlayRun(c, seed, BalanceTests.MaxCommands, rate, MovementMode.Free,
                                    blind: true, loots: true, heroId: heroId,
                                    profile: BalanceTests.BuiltUp(c, heroClass.Id)).Status == RunStatus.Won) won++;
                        wins.Add(won);
                    }
                    int weak = wins.Min(), strong = wins.Max();
                    line.Append($"  {label} {100 * wins.Sum() / (runs * wins.Count),3}% spread {(weak == 0 ? 99 : strong / (double)weak):0.00}");
                }
                // And a newcomer, who must not be touched by any of this.
                int fresh = 0;
                for (ulong seed = 1; seed <= 80; seed++)
                    if (AutoPlayer.PlayRun(c, seed, BalanceTests.MaxCommands, AutoPlayer.CasualMistakeRate, MovementMode.Free, blind: true).Status == RunStatus.Won) fresh++;
                line.Append($"  | fresh {100 * fresh / 80,3}%");
                TestContext.Progress.WriteLine(line.ToString());
            }
        }
    }
}
