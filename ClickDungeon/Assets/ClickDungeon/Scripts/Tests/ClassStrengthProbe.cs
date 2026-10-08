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
        ///   THE FIXTURE. This was written against the points-spent tier table, which made every class silently lose
        ///   its second branch's tier-3 talent - for the Wizard, Soul Siphon, its only sustain. That table is gone
        ///   (D-087) and the fixture now commits to ONE corner and walks it to the end (D-088), so what a build lacks
        ///   is the whole of the road it turned down rather than one node it could not afford. The rows below compare
        ///   the two roads; read the delta as "which corner the Wizard wants", not as a missing tier-3.
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
        /// The Knight, which D-088 left as the only class whose BOTH roads are far below the field (17 and 14 of 60
        /// careless, against 22-56). Moving max hearts to the shared rail bought it 7 and fixed the Cleric outright,
        /// so the shape rule is sound and the Knight needs something of its own. Two candidates, measured rather
        /// than argued:
        ///
        ///   SECOND WIND TO THE RAIL. Its +3 hearts on every new floor is the Knight's largest sustain and sits on
        ///   one road. Swapping it with Relentless puts it where both builds get it - the same rule that fixed the
        ///   Cleric, applied to the Knight's own essential instead of to hearts.
        ///
        ///   RALLY MENDS MORE. The Knight's verbs are weak in absolute terms: Rally mends 2 where the Berserker's
        ///   Second Wind mends 4 and the Engineer's Bomb deals 3 to a tile and everything around it. This asks
        ///   whether the kit is the problem rather than where it sits.
        /// </summary>
        [Test, Explicit("Measurement: the Knight's two candidates, both roads")]
        public void Knight()
        {
            // Second Wind (bulwark t3) trades places with Relentless (rail t2), gates and all.
            void SecondWindToTheRail(ContentCatalog c)
            {
                var sw = c.Talent("k_second_wind");
                var rel = c.Talent("k_relentless");
                sw.BranchId = rel.BranchId; sw.Tier = rel.Tier; sw.Requires = rel.Requires;
                rel.BranchId = "bulwark"; rel.Tier = 3; rel.Requires = "k_riposte";
                c.Talent("k_treasure_sense").Requires = "k_relentless";
            }

            // MEASURED AND REFUTED: Second Wind on the rail. It reads as the Cleric's fix applied to the Knight, but
            // the rail has two slots and moving it there displaces Relentless - and the blade build values Relentless
            // far more. Blade went 76 -> 79 casual and 17 -> 12 careless, and with Rally mended it cost 24 wins
            // (104 -> 80). `SecondWindToTheRail` is kept as the thing that was tried, not as a candidate.
            //
            // Shield Bash as a MEND is a diagnostic, not a proposal: it asks whether the bulwark road is short of a
            // verb at all, or short of something a skill cannot supply.
            // SHIPPED NOW: Rally mends 4 (76 -> 104 casual on the blade road). What is left to settle is the bulwark
            // road, whose corner is a bare adjacent stagger. Reach is the lever that keeps it a stagger - Concussion
            // reaches two tiles and Snare three, both for 3 mana, and Shield Bash reaches one for 2.
            //
            // `ShieldBash mends 4` stays as the CEILING, not a proposal: it is what the road is worth with a real
            // heal on it (115 casual, 26 careless), and it would make both Knight corners mends, which is the one
            // thing the fork cannot afford. Cost is not the lever - at 1 mana the runs came back byte-identical.
            var variants = new (string label, Action<ContentCatalog> tweak)[]
            {
                ("shipped (Rally 4)",   null),
                ("ShieldBash reach 2",  c => c.Skill("kni_shield_bash").Range = 2),
                ("ShieldBash reach 3",  c => c.Skill("kni_shield_bash").Range = 3),
                ("ShieldBash mends 4",  c => { var s = c.Skill("kni_shield_bash"); s.Effect = SkillEffect.Heal; s.Amount = 4; s.Target = SkillTarget.Self; }),
            };

            foreach (var (label, tweak) in variants)
            {
                var line = new System.Text.StringBuilder($"{label,-20}");
                foreach (var road in new[] { "blade", "bulwark" })
                {
                    var c = ContentCatalog.CreateDefault(Difficulty.Medium);
                    tweak?.Invoke(c);
                    string heroId = c.HeroIdentities.Values.First(h => h.ClassId == "knight").Id;
                    // The instrument check the Wizard probe taught: if the build does not carry the thing being
                    // measured, every number in the row is noise.
                    var built = BalanceTests.BuiltUp(c, "knight", 12, road);
                    int sustain = Progression.Rank(built, "k_second_wind") + Progression.Rank(built, "k_sturdy");
                    line.Append($"  {road,-7} [sustain {sustain}]");
                    foreach (var (cfg, runs, rate) in new[]
                             { ("cas@120", 120, AutoPlayer.CasualMistakeRate), ("car@60", 60, BalanceTests.NoviceMistakeRate) })
                    {
                        int won = 0;
                        for (ulong seed = 1; seed <= (ulong)runs; seed++)
                            if (AutoPlayer.PlayRun(c, seed, BalanceTests.MaxCommands, rate, MovementMode.Free,
                                    blind: true, loots: true, heroId: heroId,
                                    profile: BalanceTests.BuiltUp(c, "knight", 12, road)).Status == RunStatus.Won) won++;
                        line.Append($" {cfg} {won,3}/{runs}");
                    }
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
            // PER ROAD since D-088, because a class is two builds now and not one. Averaging them, or measuring
            // whichever corner the catalogue happens to list first, hides a road that has stopped working - and the
            // first measurement after the fork found exactly that, so this prints every road a player can choose.
            var catalog = ContentCatalog.CreateDefault(Difficulty.Medium);
            foreach (var (label, runs, rate) in new[]
                     { ("casual @120", 120, AutoPlayer.CasualMistakeRate), ("careless@60", 60, BalanceTests.NoviceMistakeRate) })
            {
                var wins = new List<(string id, int won)>();
                int stalledTotal = 0;
                foreach (var heroClass in catalog.HeroClasses.Values)
                {
                    string heroId = catalog.HeroIdentities.Values.First(h => h.ClassId == heroClass.Id).Id;
                    foreach (var corner in catalog.TalentsOf(heroClass.Id).Where(t => t.Capstone))
                    {
                        int won = 0;
                        for (ulong seed = 1; seed <= (ulong)runs; seed++)
                        {
                            var r = AutoPlayer.PlayRun(catalog, seed, BalanceTests.MaxCommands, rate, MovementMode.Free,
                                blind: true, loots: true, heroId: heroId,
                                profile: BalanceTests.BuiltUp(catalog, heroClass.Id, 12, corner.BranchId));
                            if (r.Status == RunStatus.Won) won++;
                            else if (r.Status == RunStatus.InProgress) stalledTotal++;
                        }
                        wins.Add(($"{heroClass.Id.Substring(0, 3)}/{corner.BranchId.Substring(0, 4)}", won));
                    }
                }
                int weak = wins.Min(w => w.won), strong = wins.Max(w => w.won);
                // And the class's BEST road, which is what "can this class be played" actually asks.
                var best = wins.GroupBy(w => w.id.Split('/')[0]).Select(g => g.Max(w => w.won)).ToList();
                TestContext.Progress.WriteLine(
                    $"{label}  {string.Join("  ", wins.Select(w => $"{w.id} {w.won,3}"))}");
                TestContext.Progress.WriteLine(
                    $"{label}  | weakest road {weak}/{runs} ({100 * weak / runs}%)  strongest {strong}" +
                    $"  ratio {strong / (double)Math.Max(1, weak):0.00}  | weakest CLASS by its best road {best.Min()}/{runs}" +
                    $"  ratio {best.Max() / (double)Math.Max(1, best.Min()):0.00}  stalled {stalledTotal}");
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
                // Rally mends 4 as of D-088, so this row is the shipped number; it is kept at 2 to measure what the
                // change bought rather than repeating the baseline.
                ("knight",    "Rally back to 2",       c => c.Skill("kni_rally").Amount = 2, null),

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
