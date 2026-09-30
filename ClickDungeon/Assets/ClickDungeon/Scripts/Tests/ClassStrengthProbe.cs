using System;
using System.Collections.Generic;
using System.Linq;
using ClickDungeon.Application;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using NUnit.Framework;

namespace ClickDungeon.Tests
{
    /// <summary>
    /// Why two classes win twice as often as the other six. Explicit, so it can never gate a kit.
    ///
    /// Ablating a perk by editing the catalogue does not work: perks are ADDITIVE, so zeroing the Engineer's Drone
    /// class trait still leaves the +1 from e_overclock and measures a halved drone rather than none. These probes
    /// patch run.Perks after provisioning instead, which is the only place the total exists.
    /// </summary>
    public class ClassStrengthProbe
    {
        /// <summary>What a built-up hero of each class actually walks in carrying. No runs - just composition.</summary>
        [Test, Explicit("Measurement: the built-up hero each class provisions")]
        public void Composition()
        {
            var catalog = ContentCatalog.CreateDefault(Difficulty.Medium);
            foreach (var heroClass in catalog.HeroClasses.Values)
            {
                string heroId = catalog.HeroIdentities.Values.First(h => h.ClassId == heroClass.Id).Id;
                var events = new List<GameEvent>();
                var run = RunFactory.NewRun(7UL, catalog, events, heroId, MovementMode.Free);
                ProfileSystem.ProvisionRun(BalanceTests.BuiltUp(catalog, heroClass.Id), run, catalog, events);
                var hero = run.Hero;
                var perks = (run.Perks ?? new Dictionary<string, int>())
                    .Where(p => p.Value != 0).OrderBy(p => p.Key)
                    .Select(p => $"{p.Key} {p.Value}");
                TestContext.Progress.WriteLine(
                    $"{heroClass.Id,-10} hp {hero.MaxHp,3}  slash {hero.SlashDamage,2}  mana {hero.MaxMana,2}  " +
                    $"potions {hero.Potions,2}  | {string.Join(", ", perks)}");
            }
        }

        /// <summary>
        /// PlayRun's loop, with a hook between provisioning and the first turn. Every step must match
        /// <see cref="AutoPlayer.PlayRun"/> exactly or an ablation measures the harness instead of the perk.
        /// </summary>
        static int Wins(ContentCatalog catalog, string classId, int seeds, Action<RunState> ablate)
        {
            string heroId = catalog.HeroIdentities.Values.First(h => h.ClassId == classId).Id;
            int won = 0;
            for (ulong seed = 1; seed <= (ulong)seeds; seed++)
            {
                var player = new AutoPlayer(BalanceTests.NoviceMistakeRate, blind: true, loots: true);
                var events = new List<GameEvent>();
                var run = RunFactory.NewRun(seed, catalog, events, heroId, MovementMode.Free);
                ProfileSystem.ProvisionRun(BalanceTests.BuiltUp(catalog, classId), run, catalog, events);
                ablate?.Invoke(run);
                for (int i = 0; i < BalanceTests.MaxCommands && run.Status == RunStatus.InProgress; i++)
                    TurnResolver.Apply(run, player.Choose(run, catalog, seed * 7919UL + (ulong)i), catalog);
                if (run.Status == RunStatus.Won) won++;
            }
            return won;
        }

        /// <summary>The harness must reproduce PlayRun before any ablation through it means anything.</summary>
        [Test]
        public void TheAblationHarnessReproducesPlayRun()
        {
            var catalog = ContentCatalog.CreateDefault(Difficulty.Medium);
            foreach (var classId in new[] { "berserker", "engineer", "wizard" })
            {
                string heroId = catalog.HeroIdentities.Values.First(h => h.ClassId == classId).Id;
                int viaPlayRun = 0;
                for (ulong seed = 1; seed <= 8; seed++)
                    if (AutoPlayer.PlayRun(catalog, seed, BalanceTests.MaxCommands, BalanceTests.NoviceMistakeRate,
                            MovementMode.Free, blind: true, loots: true, heroId: heroId,
                            profile: BalanceTests.BuiltUp(catalog, classId)).Status == RunStatus.Won) viaPlayRun++;
                Assert.That(Wins(catalog, classId, 8, null), Is.EqualTo(viaPlayRun),
                    $"{classId}: the ablation harness drifted from PlayRun, so every ablation through it is void.");
            }
        }

        /// <summary>
        /// The Wizard, which is the weakest class on both parity guards and the one that makes the casual floor hard
        /// to place. Two candidate causes, separated here:
        ///
        ///   THE FIXTURE. BuiltUp spends breadth-first in catalogue order, and on the first pass it reaches every
        ///   branch's tier-3 gate having spent 3 points where the gate wants 4 - except the third branch's, reached at
        ///   5. So EVERY class silently loses its SECOND branch's tier-3 talent. For seven of them that is a minor
        ///   perk (the Knight's Riposte, the Berserker's Pain Is Progress); for the Wizard it is Soul Siphon, the
        ///   Relentless that D-081 measured as worth 13 wins to the Berserker, and its only sustain.
        ///
        ///   THE CLASS. The Wizard is the only one of eight whose three abilities include no mend, so it cannot turn
        ///   the largest mana pool in the game into hearts. Its arcana tier 2 unlocks Arcane Nova, a Burst.
        /// </summary>
        [Test, Explicit("Measurement: the Wizard's missing sustain, fixture against class")]
        public void Wizard()
        {
            // Instrument check first: the arcana build must actually carry Relentless, or every row below is noise.
            var probe = ContentCatalog.CreateDefault(Difficulty.Medium);
            string wiz = probe.HeroIdentities.Values.First(h => h.ClassId == "wizard").Id;
            foreach (var branch in new[] { null, "arcana" })
            {
                var events = new List<GameEvent>();
                var run = RunFactory.NewRun(7UL, probe, events, wiz, MovementMode.Free);
                ProfileSystem.ProvisionRun(BalanceTests.BuiltUp(probe, "wizard", 12, branch), run, probe, events);
                var perks = (run.Perks ?? new Dictionary<string, int>()).Where(x => x.Value != 0)
                    .OrderBy(x => x.Key).Select(x => $"{x.Key} {x.Value}");
                TestContext.Progress.WriteLine($"build {branch ?? "default",-8} hp {run.Hero.MaxHp,3} slash {run.Hero.SlashDamage,2} mana {run.Hero.MaxMana,2} | {string.Join(", ", perks)}");
            }

            var variants = new (string label, string branch, Action<ContentCatalog> tweak)[]
            {
                ("default (shipped)",  null,     null),
                ("arcana build",       "arcana", null),
                ("Nova -> mend 3",     null,     c => { var n = c.Skill("wiz_nova"); n.Effect = SkillEffect.Heal; n.Amount = 3; n.Target = SkillTarget.Self; n.ManaCost = 3; }),
                ("both",               "arcana", c => { var n = c.Skill("wiz_nova"); n.Effect = SkillEffect.Heal; n.Amount = 3; n.Target = SkillTarget.Self; n.ManaCost = 3; }),
            };

            foreach (var (label, branch, tweak) in variants)
            {
                var c = ContentCatalog.CreateDefault(Difficulty.Medium);
                tweak?.Invoke(c);
                string heroId = c.HeroIdentities.Values.First(h => h.ClassId == "wizard").Id;
                var line = new System.Text.StringBuilder($"{label,-18}");
                foreach (var (cfg, runs, rate) in new[]
                         { ("casual @120", 120, AutoPlayer.CasualMistakeRate), ("careless@60", 60, BalanceTests.NoviceMistakeRate) })
                {
                    int won = 0;
                    for (ulong seed = 1; seed <= (ulong)runs; seed++)
                        if (AutoPlayer.PlayRun(c, seed, BalanceTests.MaxCommands, rate, MovementMode.Free,
                                blind: true, loots: true, heroId: heroId,
                                profile: BalanceTests.BuiltUp(c, "wizard", 12, branch)).Status == RunStatus.Won) won++;
                    line.Append($"   {cfg} {won,3}/{runs} ({100 * won / runs,3}%)");
                }
                TestContext.Progress.WriteLine(line.ToString());
            }
        }

        /// <summary>
        /// The two built-up parity guards' own tables, at their own seed counts, so their floors can be set against a
        /// measurement rather than a memory. EveryClassTreeIsWorthPlaying is casual at 40; NoClassIsHopeless... is
        /// careless at 60.
        /// </summary>
        [Test, Explicit("Measurement: the parity guards' tables, for re-baselining")]
        public void GuardTables()
        {
            var catalog = ContentCatalog.CreateDefault(Difficulty.Medium);
            foreach (var (label, runs, rate) in new[]
                     { ("casual @120", 120, AutoPlayer.CasualMistakeRate), ("careless@60", 60, BalanceTests.NoviceMistakeRate) })
            {
                var wins = new List<(string id, int won)>();
                int stalledTotal = 0;
                foreach (var heroClass in catalog.HeroClasses.Values)
                {
                    string heroId = catalog.HeroIdentities.Values.First(h => h.ClassId == heroClass.Id).Id;
                    int won = 0;
                    for (ulong seed = 1; seed <= (ulong)runs; seed++)
                    {
                        var r = AutoPlayer.PlayRun(catalog, seed, BalanceTests.MaxCommands, rate, MovementMode.Free,
                            blind: true, loots: true, heroId: heroId, profile: BalanceTests.BuiltUp(catalog, heroClass.Id));
                        if (r.Status == RunStatus.Won) won++;
                        else if (r.Status == RunStatus.InProgress) stalledTotal++;
                    }
                    wins.Add((heroClass.Id, won));
                }
                int weak = wins.Min(w => w.won), strong = wins.Max(w => w.won);
                TestContext.Progress.WriteLine(
                    $"{label}  {string.Join("  ", wins.Select(w => $"{w.id.Substring(0, 3)} {w.won,2}"))}" +
                    $"  | weakest {weak}/{runs} ({100 * weak / runs}%)  strongest {strong}  ratio {strong / (double)Math.Max(1, weak):0.00}  stalled {stalledTotal}");
            }
        }

        /// <summary>
        /// How a class's runs END, not just how many it wins. A class at 22/24 may be winning because it never dies
        /// or because it is fast; those want opposite fixes, and the win count alone cannot tell them apart.
        /// </summary>
        /// <summary>
        /// What the bot SPENDS ITS TURNS ON. If a heal competes with SHIELD for one mana pool, removing the heal
        /// should show up as more shielding - and if it does not, the mana story is wrong.
        /// </summary>
        [Test, Explicit("Measurement: the command mix, with and without the heal")]
        public void CommandMix()
        {
            const int seeds = 12;
            var plan = new (string classId, string label, Action<ContentCatalog> tweak)[]
            {
                ("knight",    "baseline",        null),
                ("knight",    "Rally off",       c => c.Skill("kni_rally").ManaCost = 99),
                ("berserker", "baseline",        null),
                ("berserker", "Second Wind off", c => c.Skill("ber_second_wind").ManaCost = 99),
            };

            foreach (var (classId, label, tweak) in plan)
            {
                var catalog = ContentCatalog.CreateDefault(Difficulty.Medium);
                tweak?.Invoke(catalog);
                string heroId = catalog.HeroIdentities.Values.First(h => h.ClassId == classId).Id;
                var mix = new Dictionary<CommandKind, int>();
                int turns = 0, won = 0;
                for (ulong seed = 1; seed <= (ulong)seeds; seed++)
                {
                    var player = new AutoPlayer(BalanceTests.NoviceMistakeRate, blind: true, loots: true);
                    var events = new List<GameEvent>();
                    var run = RunFactory.NewRun(seed, catalog, events, heroId, MovementMode.Free);
                    ProfileSystem.ProvisionRun(BalanceTests.BuiltUp(catalog, classId), run, catalog, events);
                    for (int i = 0; i < BalanceTests.MaxCommands && run.Status == RunStatus.InProgress; i++)
                    {
                        var command = player.Choose(run, catalog, seed * 7919UL + (ulong)i);
                        mix[command.Kind] = mix.TryGetValue(command.Kind, out var n) ? n + 1 : 1;
                        TurnResolver.Apply(run, command, catalog);
                        turns++;
                    }
                    if (run.Status == RunStatus.Won) won++;
                }
                var parts = mix.OrderByDescending(kv => kv.Value)
                    .Select(kv => $"{kv.Key} {100.0 * kv.Value / turns:0}%");
                TestContext.Progress.WriteLine($"{classId,-10} {label,-18} won {won,2}/{seeds}  turns {turns,5}  | {string.Join("  ", parts)}");
            }
        }

        /// <summary>
        /// Is the bot simply HEALING TOO MUCH? A heal costs a turn, and a turn costs monster blows and stir. If the
        /// greedy policy is the cause, pricing the heal up should make the Knight better, not worse.
        /// </summary>
        [Test, Explicit("Measurement: over-healing, the Sanctuary interaction, and Rage at 48 seeds")]
        public void HealingPolicy()
        {
            const int seeds = 48;
            var plan = new (string classId, string label, Action<ContentCatalog> tweak, Action<RunState> ablate)[]
            {
                ("knight",    "baseline (3 mana)",     null, null),
                ("knight",    "Rally 6 mana",          c => c.Skill("kni_rally").ManaCost = 6, null),
                ("knight",    "Rally 9 mana",          c => c.Skill("kni_rally").ManaCost = 9, null),
                ("knight",    "Rally off",             c => c.Skill("kni_rally").ManaCost = 99, null),

                ("cleric",    "baseline",              null, null),
                ("cleric",    "Sanctuary 0",           null, r => r.Perks["Sanctuary"] = 0),
                ("cleric",    "Heal off",              c => c.Skill("cle_mend").ManaCost = 99, null),
                ("cleric",    "Heal off + Sanct 0",    c => c.Skill("cle_mend").ManaCost = 99, r => r.Perks["Sanctuary"] = 0),

                ("berserker", "baseline",              null, null),
                ("berserker", "Rage 0",                null, r => r.Perks["Rage"] = 0),
                ("berserker", "Second Wind 8 mana",    c => c.Skill("ber_second_wind").ManaCost = 8, null),
            };

            foreach (var (classId, label, tweak, ablate) in plan)
            {
                var catalog = ContentCatalog.CreateDefault(Difficulty.Medium);
                tweak?.Invoke(catalog);
                TestContext.Progress.WriteLine($"{classId,-10} {label,-22} {Wins(catalog, classId, seeds, ablate),2}/{seeds}");
            }
        }

        /// <summary>
        /// Is the two classes' advantage SUSTAIN - hearts per mana, spent turn after turn? Each candidate gets its
        /// own catalogue, so a disabled skill is disabled for that measurement only.
        /// </summary>
        [Test, Explicit("Measurement: sustain - the heal skill and the drone, at 48 seeds")]
        public void SustainAblations()
        {
            const int seeds = 48;
            var plan = new (string classId, string label, Action<ContentCatalog> tweak, Action<RunState> ablate)[]
            {
                ("berserker", "baseline",              null, null),
                ("berserker", "Second Wind off",       c => c.Skill("ber_second_wind").ManaCost = 99, null),
                ("berserker", "Relentless 0",          null, r => r.Perks["Relentless"] = 0),
                ("berserker", "both off",              c => c.Skill("ber_second_wind").ManaCost = 99, r => r.Perks["Relentless"] = 0),

                ("engineer",  "baseline",              null, null),
                ("engineer",  "Field Repairs off",     c => c.Skill("eng_repair").ManaCost = 99, null),
                ("engineer",  "Drone 0",               null, r => r.Perks["Drone"] = 0),
                ("engineer",  "both off",              c => c.Skill("eng_repair").ManaCost = 99, r => r.Perks["Drone"] = 0),

                ("knight",    "baseline",              null, null),
                ("knight",    "Rally off",             c => c.Skill("kni_rally").ManaCost = 99, null),
                ("knight",    "Rally heals 4",         c => c.Skill("kni_rally").Amount = 4, null),

                ("cleric",    "baseline",              null, null),
                ("cleric",    "Heal off",              c => c.Skill("cle_mend").ManaCost = 99, null),
            };

            foreach (var (classId, label, tweak, ablate) in plan)
            {
                var catalog = ContentCatalog.CreateDefault(Difficulty.Medium);
                tweak?.Invoke(catalog);
                TestContext.Progress.WriteLine($"{classId,-10} {label,-20} {Wins(catalog, classId, seeds, ablate),2}/{seeds}");
            }
        }

        [Test, Explicit("Measurement: won / died / ran out of clock, per class")]
        public void Outcomes()
        {
            const int seeds = 24;
            var catalog = ContentCatalog.CreateDefault(Difficulty.Medium);
            foreach (var heroClass in catalog.HeroClasses.Values)
            {
                string heroId = catalog.HeroIdentities.Values.First(h => h.ClassId == heroClass.Id).Id;
                int won = 0, died = 0, stalled = 0, turns = 0, floors = 0, maxHp = 0, pressed = 0;
                for (ulong seed = 1; seed <= (ulong)seeds; seed++)
                {
                    var r = AutoPlayer.PlayRun(catalog, seed, BalanceTests.MaxCommands, BalanceTests.NoviceMistakeRate,
                        MovementMode.Free, blind: true, loots: true, heroId: heroId,
                        profile: BalanceTests.BuiltUp(catalog, heroClass.Id));
                    if (r.Status == RunStatus.Won) won++;
                    else if (r.Status == RunStatus.Lost) died++;
                    else stalled++;
                    turns += r.Turns; floors += r.Floor; maxHp += r.MaxHp; pressed += r.PressedDamage;
                }
                TestContext.Progress.WriteLine(
                    $"{heroClass.Id,-10} won {won,2}  died {died,2}  stalled {stalled,2}  | " +
                    $"turns {turns / seeds,4}  floor {floors / (double)seeds,5:0.0}  endMaxHp {maxHp / (double)seeds,5:0.0}  stir {pressed / (double)seeds,5:0.0}");
            }
        }

        [Test, Explicit("Measurement: which perk carries the Berserker and the Engineer")]
        public void Ablations()
        {
            const int seeds = 24;
            var catalog = ContentCatalog.CreateDefault(Difficulty.Medium);
            Action<RunState> Zero(params string[] keys) => run =>
            {
                foreach (var key in keys)
                    if (run.Perks != null && run.Perks.ContainsKey(key)) run.Perks[key] = 0;
            };

            var plan = new (string classId, string label, Action<RunState> ablate)[]
            {
                ("berserker", "baseline",            null),
                ("berserker", "Rage 0",              Zero("Rage")),
                ("berserker", "Bloodlust 0",         Zero("Bloodlust")),
                ("berserker", "Rage + Bloodlust 0",  Zero("Rage", "Bloodlust")),
                ("berserker", "Cleave 0",            Zero("Cleave")),
                ("berserker", "every perk 0",        run => { foreach (var k in run.Perks.Keys.ToList()) run.Perks[k] = 0; }),

                ("engineer",  "baseline",            null),
                ("engineer",  "Drone 0",             Zero("Drone")),
                ("engineer",  "DroneRange 0",        Zero("DroneRange")),
                ("engineer",  "ArcChain 0",          Zero("ArcChain")),
                ("engineer",  "Drone family 0",      Zero("Drone", "DroneRange", "ArcChain")),
                ("engineer",  "every perk 0",        run => { foreach (var k in run.Perks.Keys.ToList()) run.Perks[k] = 0; }),

                ("wizard",    "baseline",            null),
                ("wizard",    "every perk 0",        run => { foreach (var k in run.Perks.Keys.ToList()) run.Perks[k] = 0; }),
            };

            foreach (var (classId, label, ablate) in plan)
                TestContext.Progress.WriteLine($"{classId,-10} {label,-20} {Wins(catalog, classId, seeds, ablate),2}/{seeds}");
        }
    }
}
