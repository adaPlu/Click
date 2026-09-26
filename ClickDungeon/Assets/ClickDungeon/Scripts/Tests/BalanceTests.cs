using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using ClickDungeon.Application;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using NUnit.Framework;
using static ClickDungeon.Tests.Scenario;

namespace ClickDungeon.Tests
{
    public class AutoPlayerTests
    {
        [Test]
        public void TheCopyCarriesEveryFieldARunHas()
        {
            // REL-44: Movement and Threat were missing from the copy for as long as it has existed, and the test below
            // could not see it - a run built with the defaults serializes the same either way. This one puts a
            // non-default value in every field the look-ahead reads before comparing.
            var catalog = ContentCatalog.CreateDefault(Difficulty.Hardcore);
            var run = RunFactory.NewRun(31UL, catalog, new List<GameEvent>(), "shadowcut", MovementMode.Step);
            run.Threat = 3;
            run.Hero.SpecialKeys = 2;
            run.PremiumChestsToPlace = 1;
            run.Perks["Ambush"] = 4;
            run.Hero.WebbedTurns = 1;
            run.Hero.DodgeSpent = true;
            run.Floor.Enemies[0].Mode = EnemyMode.Enraged;
            run.Floor.Enemies[0].Enraging = true;
            run.Floor.Enemies[0].CarriesKey = true;
            run.Floor.Enemies[0].Disguised = true;
            run.Skills = new List<string> { "rog_smoke" };
            run.Turn = 7;
            run.CoinsFound = 11;
            run.GemsFound = 2;
            run.XpEarned = 33;
            run.MonstersSlain = 4;
            run.ChestsOpened = 3;
            run.ItemsFound = new List<string> { "steel_sword" };
            run.Rewards.Add(new RewardRecord { TransactionId = "t1", Kind = RewardKind.Potion, Amount = 1, FloorIndex = 1, Turn = 2 });
            run.PotionHealBonus = 1;
            run.BonusXpPerFloor = 2;
            run.BonusCoinsPerChestReward = 3;
            run.DashCostCut = 1;
            run.OuterFloor = FloorState.CreateEmpty();
            run.ReturnPos = P(1, 1);
            run.VisitedVault = FloorState.CreateEmpty();
            run.VisitedVaultDoor = P(2, 2);
            Assert.That(SaveSerializer.ToJson(AutoPlayer.Copy(run)), Is.EqualTo(SaveSerializer.ToJson(run)));

            // And the reason this test exists has to be enforced, not intended. Twice now a field has been added to
            // RunState and left out of Copy - Movement and Threat (REL-44), then Skills (D-075) - and both times this
            // test was green, because a field nobody thought to set above is a field it cannot see. So: every field of
            // RunState must differ from a fresh one. Add a field, and this names it until it is given a value here.
            var fresh = new RunState();
            foreach (var field in typeof(RunState).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                // The version stamps and the status of a live run are the same on any run; there is no "non-default"
                // to give them, and Copy carrying them is pinned by the JSON comparison above.
                if (field.Name.EndsWith("Version") || field.Name == "Status") continue;
                Assert.That(Newtonsoft.Json.JsonConvert.SerializeObject(field.GetValue(run)),
                    Is.Not.EqualTo(Newtonsoft.Json.JsonConvert.SerializeObject(field.GetValue(fresh))),
                    $"RunState.{field.Name} still holds its default here, so this test cannot tell whether Copy carries it. "
                    + "Give it a value above.");
            }
        }

        [Test]
        public void CopySerializesExactlyLikeTheRun()
        {
            // Every tier and deep runs, so rare state (boss modes, stagger, minions, chests) is copied too.
            foreach (var tier in new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hardcore })
            {
                var catalog = ContentCatalog.CreateDefault(tier);
                foreach (ulong seed in new ulong[] { 21, 22, 23 })
                {
                    var run = RunFactory.NewRun(seed, catalog, new List<GameEvent>());
                    var player = new AutoPlayer(AutoPlayer.CasualMistakeRate);
                    for (int i = 0; i < 150 && run.Status == RunStatus.InProgress; i++)
                    {
                        Assert.That(SaveSerializer.ToJson(AutoPlayer.Copy(run)), Is.EqualTo(SaveSerializer.ToJson(run)), $"{tier} seed {seed} command {i}");
                        var result = TurnResolver.Apply(run, player.Choose(run, catalog, seed * 31 + (ulong)i), catalog);
                        Assert.That(result.Accepted, Is.True, result.RejectReason);
                    }
                }
            }
        }

        [Test]
        public void SameSeedPlaysTheSameRun()
        {
            Assert.That(AutoPlayer.PlayRun(Catalog, 3UL, 120).ToString(), Is.EqualTo(AutoPlayer.PlayRun(Catalog, 3UL, 120).ToString()));
        }
    }

    /// <summary>
    /// Balance targets measured with AutoPlayer, a look-ahead bot that plays about as well as a careful player.
    /// The fast checks guard the targets; the explicit report prints the full table for tuning.
    /// </summary>
    public class BalanceTests
    {
        /// <summary>
        /// The command cap for one bot run: 80 a floor, the ratio the original 400 set for five floors. It must grow with
        /// the dungeon (D-059) - held at 400 over seven floors, a slow but winning run was cut off and counted as a stall,
        /// the bot running out of time dressed up as the dungeon winning.
        /// </summary>
        public static readonly int MaxCommands = 80 * ContentCatalog.CreateDefault().RunFloorCount;
        static readonly Difficulty[] Tiers = { Difficulty.Easy, Difficulty.Medium, Difficulty.Hardcore };
        static readonly int FloorSlots = ContentCatalog.CreateDefault().RunFloorCount + 1;

        sealed class Tally
        {
            public int Runs, Won, ReachedBoss, Stalled;
            public long Turns;
            // One slot per floor, 1-based. Sized from the catalog: it was a literal 6 until the dungeon grew (D-059).
            public int[] DeathsByFloor = new int[FloorSlots];
            public int[] StalledByFloor = new int[FloorSlots];
        }

        static readonly (string name, double mistakeRate)[] Skills =
        {
            ("sharp", 0.0), ("casual", AutoPlayer.CasualMistakeRate), ("sloppy", 0.35), ("novice", NoviceMistakeRate), ("flailing", FlailingMistakeRate),
        };

        static Tally Measure(Difficulty tier, int runs, double mistakeRate, MovementMode movement = MovementMode.Free,
            bool blind = false) =>
            Measure(ContentCatalog.CreateDefault(tier), runs, mistakeRate, movement, blind);

        static Tally Measure(ContentCatalog catalog, int runs, double mistakeRate, MovementMode movement = MovementMode.Free,
            bool blind = false)
        {
            var tally = new Tally { Runs = runs };
            for (ulong seed = 1; seed <= (ulong)runs; seed++)
            {
                var r = AutoPlayer.PlayRun(catalog, seed, MaxCommands, mistakeRate, movement, blind);
                if (r.Floor >= catalog.RunFloorCount) tally.ReachedBoss++;
                if (r.Status == RunStatus.Won) tally.Won++;
                else if (r.Status == RunStatus.Lost) tally.DeathsByFloor[r.Floor]++;
                else
                {
                    tally.Stalled++;
                    tally.StalledByFloor[r.Floor]++;
                }
                tally.Turns += r.Turns;
            }
            return tally;
        }

        /// <summary>
        /// Half the turns spent on a command the look-ahead did not choose. Under the default error model that is a
        /// uniform draw from everything legal that survives the turn - mostly a step to a random tile, and never a walk
        /// into a telegraphed killing blow, which is the most characteristic novice death there is. Careless play here
        /// means "tolerance to position-independent chip damage"; it does not mean a person misreading a board
        /// (TEST-85, and MistakeModel.Misjudged for the other model).
        /// </summary>
        public const double NoviceMistakeRate = 0.5;

        /// <summary>
        /// A player who has been here before: levelled, their class tree spent as far as the rules allow, wearing what
        /// the dungeon drops, and carrying the threat their renown earns. Every guard in this file used to measure an
        /// empty profile without saying so, which meant none of them could see a talent, a worn item or renown at all
        /// (TEST-23, D-069). `BuiltUp` is the other half of the picture, not a replacement for the first.
        /// </summary>
        /// <param name="capstoneBranch">
        /// Which branch's capstone this build takes. A class may learn one (Progression.Locked), so spending in list
        /// order silently always took the first-listed branch's - two capstones in three were measured by nothing at
        /// all, by the guard whose whole purpose is catching a talent that has stopped paying (MAINT-90). Null keeps
        /// the old build, for the guards that want one build a class.
        /// </param>
        public static ProfileState BuiltUp(ContentCatalog catalog, string classId, int level = 12, string capstoneBranch = null)
        {
            var profile = new ProfileState { Xp = Progression.XpForLevel(level) };
            if (capstoneBranch != null)
            {
                // Climb the named branch first. Skipping the other capstones is not enough on its own: a capstone opens
                // at seven points spent and needs its own tier-3 beneath it, and a build that spreads evenly reaches
                // whichever branch the catalogue happens to list first. So: take the next rung of this branch if one is
                // open, and otherwise buy a point anywhere below tier 4 to move the tier gate along.
                var chain = catalog.TalentsOf(classId).Where(t => t.BranchId == capstoneBranch).OrderBy(t => t.Tier).ToList();
                string capstoneId = chain.Last(t => t.Tier == 4).Id;
                bool progress = true;
                while (Progression.Rank(profile, capstoneId) == 0 && progress)
                {
                    progress = false;
                    foreach (var talent in chain)
                        if (Progression.TryLearn(profile, catalog, talent.Id)) { progress = true; break; }
                    if (progress) continue;
                    foreach (var talent in catalog.TalentsOf(classId))
                        if (talent.Tier < 4 && Progression.TryLearn(profile, catalog, talent.Id)) { progress = true; break; }
                }
            }
            // Spend every point the level bought, in the order the tree lists them: a real player's build is not this
            // tidy, but it is a build rather than a blank, and it is the same one on every seed.
            bool spent = true;
            while (spent)
            {
                spent = false;
                foreach (var talent in catalog.TalentsOf(classId))
                {
                    if (capstoneBranch != null && talent.Tier == 4 && talent.BranchId != capstoneBranch) continue;
                    if (Progression.TryLearn(profile, catalog, talent.Id)) spent = true;
                }
            }
            foreach (var item in catalog.Items) Inventory.Grant(profile, catalog, item.Id);
            // Grant fills a slot only while it is empty, and the catalogue lists Commons first, so owning everything
            // dressed this profile head to toe in Commons and left every Epic in the bag - while the guard below has
            // been failing with "the best gear worn" in its message all along (TEST-80). Wear the best of each slot,
            // ties going to the one the catalogue lists first so every seed sees the same build.
            foreach (var slot in Inventory.SlotOrder)
            {
                ItemDefinition best = null;
                foreach (var item in catalog.Items)
                    if (item.Slot == slot && (best == null || item.Rarity > best.Rarity)) best = item;
                if (best != null) Inventory.Equip(profile, catalog, best.Id);
            }
            // And they have been to the shop (TEST-81). One of each: the smallest stock that is not nothing, so every
            // branch of ProfileSystem.Provision is on the board rather than only the ones a blank profile reaches.
            profile.HeartTokens = profile.PotionRations = profile.ManaTonics = 1;
            profile.StrengthElixirs = profile.FortuneScrolls = profile.WisdomScrolls = 1;
            // A run SPENDS these: ProfileSystem.Provision zeroes them on the profile it is handed. One profile shared
            // across a seed loop would therefore arm the first run and no other, so every guard below builds its own
            // inside the loop (MAINT-94).
            return profile;
        }

        /// <summary>
        /// What `BuiltUp` claims to be. Every measurement of a returning player in this file reads it, so a fixture that
        /// quietly stops building one turns eight guards into eight measurements of something else (TEST-80, TEST-81).
        /// </summary>
        [Test]
        public void TheBuiltUpProfileIsActuallyBuiltUp()
        {
            var catalog = ContentCatalog.CreateDefault(Difficulty.Medium);
            var profile = BuiltUp(catalog, "knight");

            Assert.That(profile.Talents, Is.Not.Empty, "A build, not a blank.");
            foreach (var slot in Inventory.SlotOrder)
            {
                var worn = catalog.Item(Inventory.Worn(profile, slot));
                Assert.That(worn, Is.Not.Null, $"Nothing worn in the {slot} slot.");
                var bestAvailable = catalog.Items.Where(i => i.Slot == slot).Max(i => i.Rarity);
                Assert.That(worn.Rarity, Is.EqualTo(bestAvailable),
                    $"{slot}: wearing a {worn.Rarity} {worn.DisplayName} with a {bestAvailable} in the bag.");
            }

            var run = new RunState { Hero = new HeroState { MaxHp = 20, Hp = 20 }, Floor = FloorState.CreateEmpty() };
            int bareSlash = run.Hero.SlashDamage, barePotions = run.Hero.Potions;
            ProfileSystem.ProvisionRun(profile, run, catalog, new List<GameEvent>());
            Assert.That(run.Hero.SlashDamage, Is.GreaterThan(bareSlash), "The gear reaches the run.");
            Assert.That(run.Hero.Potions, Is.GreaterThan(barePotions), "And so do the provisions.");
            Assert.That(run.Hero.MaxHp, Is.GreaterThan(20), "Hearts from the token and the armour both.");
        }

        /// <summary>Most turns spent on a command the look-ahead did not choose (see NoviceMistakeRate). Separates the tiers clearly.</summary>
        public const double FlailingMistakeRate = 0.7;

        /// <summary>
        /// Every tier number in this repo is measured by the bot, so a guard that only reads win rates cannot tell a
        /// dungeon that got harder from a bot that stopped playing. MAINT-15 was exactly that: a bot looping between
        /// teleport pads measured Knight's Trial at 30% won and no guard moved, because the count it would have moved
        /// was already being tallied and never read (D-053).
        /// </summary>
        static void AssertTheBotWasPlaying(Tally tally, string what)
        {
            // A ceiling, not zero: the 60-seed DifficultySweep shows one or two stalls a tier even with a healthy bot,
            // so zero would go red the first time a legitimate change moved a seed. What this catches is the systemic
            // case -- MAINT-15 looped the bot on whole classes of floor, not on one unlucky seed.
            int allowed = 1 + tally.Runs / 30;
            Assert.That(tally.Stalled, Is.LessThanOrEqualTo(allowed),
                $"{tally.Stalled} of {tally.Runs} {what} runs ran out of commands without an ending (at most {allowed} "
                + $"is normal): by floor {ByFloor(tally.StalledByFloor)}. That is the instrument failing, not the dungeon winning.");
        }

        [Test]
        public void SquiresStrollLetsANovicePlayerBeatBlobert()
        {
            // Blind: a sighted bot walks to a key it could not see, so its numbers are not about playing this game.
            // Measured 30-seed baseline with click-to-reveal and a covered exit: 30 reach, 30 won (rules §10.2). Thresholds sit a few runs below that.
            var easy = Measure(Difficulty.Easy, 30, NoviceMistakeRate, blind: true);
            Assert.That(easy.ReachedBoss, Is.GreaterThanOrEqualTo(27), $"Only {easy.ReachedBoss}/30 novice easy runs reached floor 5.");
            Assert.That(easy.Won, Is.GreaterThanOrEqualTo(26), $"Only {easy.Won}/30 novice easy runs beat Lord Blobert.");
            AssertTheBotWasPlaying(easy, "easy");
        }

        [Test]
        public void KnightsTrialLetsANovicePlayerReachBlobert()
        {
            // Re-measured after D-062 (twenty floors, a boss every five) on 240 blind seeds - 60 proved too few over twenty
            // floors: a blind novice reaches the last floor in 16%. About 70% of that over these 30 seeds is 3, the margin
            // this guard always kept (it was 11/30 against 52%), so it catches a real collapse, not a small drift.
            var medium = Measure(Difficulty.Medium, 30, NoviceMistakeRate, blind: true);
            Assert.That(medium.ReachedBoss, Is.GreaterThanOrEqualTo(3), $"Only {medium.ReachedBoss}/30 novice medium runs reached the last floor."); 
            AssertTheBotWasPlaying(medium, "medium");
        }

        [Test]
        public void TiersKeepTheirOrder()
        {
            // Blind novice, 60-seed sweep after D-056: 100% / 32% / 18% won (rules §10.1).
            var easy = Measure(Difficulty.Easy, 40, NoviceMistakeRate, blind: true);
            var medium = Measure(Difficulty.Medium, 40, NoviceMistakeRate, blind: true);
            var hardcore = Measure(Difficulty.Hardcore, 40, NoviceMistakeRate, blind: true);
            // TEST-21: a stalling bot measures nothing, and this guard used to report that as a difficulty verdict.
            AssertTheBotWasPlaying(easy, "easy");
            AssertTheBotWasPlaying(medium, "medium");
            AssertTheBotWasPlaying(hardcore, "hardcore");
            Assert.That(easy.Won, Is.GreaterThan(medium.Won), "Squire's Stroll must be won more often than Knight's Trial.");
            // Knight's Trial and Blobert's Wrath are genuinely close for a blind novice: 240 seeds measure 12% against
            // 8% (rules 10.1), a real gap but one that 40 seeds cannot resolve - they tied at 4 wins each after audit
            // 4's fixes. The guard asserts the ordering is not inverted, which is what this sample can prove, and the
            // separation itself is measured by DifficultySweep. Asserting a strict > here would be a coin flip.
            Assert.That(medium.Won, Is.GreaterThanOrEqualTo(hardcore.Won),
                $"Knight's Trial ({medium.Won}/40) must not be won less often than Blobert's Wrath ({hardcore.Won}/40).");
            Assert.That(easy.Won, Is.GreaterThan(hardcore.Won + 10), "The easiest and hardest tiers must stay far apart.");
            // Hiding the board de-saturates this, so reaching Blobert tells the tiers apart again.
            Assert.That(easy.ReachedBoss, Is.GreaterThan(hardcore.ReachedBoss), "Squire's Stroll must reach Blobert more often than Blobert's Wrath.");
        }

        [Test, Explicit("Slow balance report: dotnet test --filter Name=BalanceReport --logger \"console;verbosity=detailed\"")]
        public void BalanceReport()
        {
            const int runs = 200;
            TestContext.Out.WriteLine($"AutoPlayer balance report: {runs} seeds per tier, {MaxCommands} command cap");
            TestContext.Out.WriteLine("sees    mode  player  tier       reach F5   won   avg turns  deaths F1..F5         stalled F1..F5");
            // Half the runs blind: the bot only knows what the player knows, which is the number that describes real play.
            foreach (var blind in new[] { false, true })
            foreach (var movement in new[] { MovementMode.Free, MovementMode.Step })
            foreach (var (name, mistakeRate) in Skills)
            foreach (var tier in Tiers)
            {
                var t = Measure(tier, runs, mistakeRate, movement, blind);
                TestContext.Out.WriteLine(
                    $"{(blind ? "blind" : "all"),-7} {movement,-5} {name,-7} {tier,-10} {Pct(t.ReachedBoss, runs),7}  {Pct(t.Won, runs),5}  " +
                    $"{t.Turns / (double)runs,9:0.0}  {ByFloor(t.DeathsByFloor),-20}  {ByFloor(t.StalledByFloor)}");
            }
        }

        [Test, Explicit("Tuning aid: compares candidate numbers for Knight's Trial and Blobert's Wrath")]
        public void DifficultySweep()
        {
            const int runs = 60;
            DifficultyDefinition Tier(Difficulty id, Action<DifficultyDefinition> tweak = null)
            {
                // CreateDefault builds fresh definitions, so tweaking this one changes nothing else.
                var d = ContentCatalog.CreateDefault().DifficultyInfo(id);
                tweak?.Invoke(d);
                return d;
            }

            var candidates = new List<(string name, DifficultyDefinition tuning)>
            {
                // Click-to-reveal (D-023): every click is a blind step, so hazards now kill. Candidates walk back the crowding
                // and scarcity added for adjacent-only melee, and soften hazards.
                // Round 2. Knight's Trial is now "base, hazards -1" (65% novice wins in round 1). Blobert's Wrath was still at
                // 13% at its softest round-1 candidate, so these walk it back further.
                // Round 3 (D-038): Knight's Trial was won 10 of 10 by the casual bot across a playthrough. MB and HB were taken
                // (casual 70% / novice 67%, and casual 53% / novice 33%); the rest were measured against the old numbers.
                ("E0 current", Tier(Difficulty.Easy)),
                ("M0 current", Tier(Difficulty.Medium)),
                ("H0 current", Tier(Difficulty.Hardcore)),
                ("M4 traps +1 (chosen)", Tier(Difficulty.Medium, d => d.HazardDamage += 1)),
                ("H1 hardcore traps +2", Tier(Difficulty.Hardcore, d => d.HazardDamage += 1)),
                ("H2 hardcore traps +2 hp0", Tier(Difficulty.Hardcore, d => { d.HazardDamage += 1; d.EnemyHp = 0; })),
            };

            // Blind and in Free Roam, like the guards: this is the number that describes real play.
            TestContext.Out.WriteLine($"Difficulty sweep (blind, Free Roam): {runs} seeds, reach F5 / win per player; novice deaths F1..F5");
            foreach (var (name, tuning) in candidates)
            {
                var catalog = ContentCatalog.CreateTuned(tuning);
                var line = new System.Text.StringBuilder($"{name,-20}");
                Tally novice = null;
                foreach (var (skill, rate) in new[] { ("casual", AutoPlayer.CasualMistakeRate), ("novice", NoviceMistakeRate) })
                {
                    var t = Measure(catalog, runs, rate, MovementMode.Free, blind: true);
                    if (skill == "novice") novice = t;
                    // Stalls are printed too: a player who runs out of commands failed the bot, not the dungeon.
                    line.Append($"  {skill} {Pct(t.ReachedBoss, runs),4}/{Pct(t.Won, runs),-4} stall {t.Stalled,2}");
                }
                line.Append($"  | {ByFloor(novice.DeathsByFloor)}");
                TestContext.Out.WriteLine(line.ToString());
            }
        }

        [Test, Explicit("Tuning aid: candidate numbers for Lord Blobert on Knight's Trial")]
        public void BlobertSweep()
        {
            const int runs = 60;
            var candidates = new List<(string name, Action<ContentCatalog> tweak)>
            {
                ("B0 current", c => { }),
                ("BB lines summon2 traps", c => { var b = c.Enemy("lord_blobert"); b.SlamShakesLines = true; b.SummonCount = 2; var f = c.ProfileFor(5); f.MinSpikes = f.MaxSpikes = 2; f.MinBombs = f.MaxBombs = 1; }),
                ("BC BB hp16", c => { var b = c.Enemy("lord_blobert"); b.MaxHp = 16; b.SlamShakesLines = true; b.SummonCount = 2; var f = c.ProfileFor(5); f.MinSpikes = f.MaxSpikes = 2; f.MinBombs = f.MaxBombs = 1; }),
                ("BD BB hp18", c => { var b = c.Enemy("lord_blobert"); b.MaxHp = 18; b.SlamShakesLines = true; b.SummonCount = 2; var f = c.ProfileFor(5); f.MinSpikes = f.MaxSpikes = 2; f.MinBombs = f.MaxBombs = 1; }),
                ("BE BC summon3", c => { var b = c.Enemy("lord_blobert"); b.MaxHp = 16; b.SlamShakesLines = true; b.SummonCount = 3; var f = c.ProfileFor(5); f.MinSpikes = f.MaxSpikes = 2; f.MinBombs = f.MaxBombs = 1; }),
            };
            TestContext.Out.WriteLine($"Blobert sweep (Knight's Trial, blind, Free Roam): {runs} seeds, reach F5 / win / died on F5, avg turns");
            foreach (var (name, tweak) in candidates)
            {
                var catalog = ContentCatalog.CreateDefault(Difficulty.Medium);
                tweak(catalog);
                var line = new System.Text.StringBuilder($"{name,-24}");
                foreach (var (skill, rate) in new[] { ("sharp", 0.0), ("casual", AutoPlayer.CasualMistakeRate), ("novice", NoviceMistakeRate) })
                {
                    var t = Measure(catalog, runs, rate, MovementMode.Free, blind: true);
                    line.Append($"  {skill} {Pct(t.ReachedBoss, runs),4}/{Pct(t.Won, runs),-4} F5 died {t.DeathsByFloor[5],2} stall {t.StalledByFloor[5],2}");
                }
                TestContext.Out.WriteLine(line.ToString());
            }
        }

        /// <summary>
        /// D-047: the classes should win about as often as each other. Before it, a blind novice won 11 of 40 as the
        /// Knight and 22 as the Paladin - the same dungeons, twice the wins. This guard catches that gap coming back, and
        /// since D-063 it covers all eight classes, each played by the first hero of that class on the roster.
        /// </summary>
        // The most expensive guard in the repo: 100 seeds x 8 classes x 2 mistake rates is 1600 whole runs, about 104
        // seconds headless and half again as long under Unity's EditMode runner. Unity's own default is three minutes,
        // and D-076's extra hearts - heroes surviving longer means more turns a run - pushed it over. Raised rather
        // than shrunk: the seed count is the guard, and the note above says why 100 and not 60.
        [Test, Timeout(600000)]
        public void TheClassesWinAboutAsOftenAsEachOther()
        {
            // 100, not 40. At 40 the eight classes land within a few wins of each other and the band has no headroom left
            // to report: D-064 measured paladin 18 and cleric 28 of 40 - ratio 1.56 against a ceiling of 1.6 - where 150
            // seeds of the same build put the same two at 61% and 69%. A guard that cannot resolve what it asserts either
            // red-builds on noise or gets widened until it guards nothing (TEST-17).
            const int runs = 100;
            var catalog = ContentCatalog.CreateDefault(Difficulty.Medium);
            int Wins(string heroId, double mistakeRate)
            {
                // TEST-21: count the stalls as well. A class rule that makes the look-ahead loop collapses that class's
                // win count, and this guard would have blamed the dungeon for it.
                int won = 0, stalled = 0;
                for (ulong seed = 1; seed <= runs; seed++)
                {
                    var r = AutoPlayer.PlayRun(catalog, seed, MaxCommands, mistakeRate, MovementMode.Free, true, heroId: heroId);
                    if (r.Status == RunStatus.Won) won++;
                    else if (r.Status == RunStatus.InProgress) stalled++;
                }
                Assert.That(stalled, Is.LessThanOrEqualTo(1 + runs / 30),
                    $"{heroId} stalled in {stalled}/{runs} runs: the bot stopped playing, so this measures nothing.");
                return won;
            }

            // The band has to be a ratio, not a count. D-047 measured novice 20 / 17 and an absolute +/-8 was a
            // reasonable fence around that; D-050 then halved the win counts, and at the smaller scale the same 8
            // tolerates 8 against 16 -- the very 2x split this guard exists to refuse (D-053). A floor under the
            // smaller count keeps the ratio meaningful when both classes are losing most runs.
            // Casual only since D-063: over eight classes the novice counts are small enough (2 to 9 of 40) that the
            // ratio says nothing, and the run cost would be twice this. ClassSweep prints both.
            var wins = new List<(string id, int won)>();
            foreach (var heroClass in catalog.HeroClasses.Values)
            {
                string heroId = System.Linq.Enumerable.First(catalog.HeroIdentities.Values, h => h.ClassId == heroClass.Id).Id;
                wins.Add((heroClass.Id, Wins(heroId, AutoPlayer.CasualMistakeRate)));
            }
            string table = string.Join(", ", wins.ConvertAll(w => $"{w.id} {w.won}"));
            int fewer = wins[0].won, more = wins[0].won;
            string weakest = wins[0].id, strongest = wins[0].id;
            foreach (var (id, won) in wins)
            {
                if (won < fewer) { fewer = won; weakest = id; }
                if (won > more) { more = won; strongest = id; }
            }
            Assert.That(fewer, Is.GreaterThanOrEqualTo(5),
                $"{weakest} won only {fewer}/{runs}. Every class has collapsed -- this is a difficulty regression, not a "
                + $"parity one, and the ratio below would be meaningless anyway. ({table})");
            // The fence is the ratio: the strongest class may win at most 8/5 of what the weakest does. Audit 4
            // (TEST-17) found the old `Math.Min(fewer + 8, ...)` passing with exactly zero margin - measured 22 and 30,
            // allowed 30 - because that absolute +8 was written when a tier measured ~20 wins and now binds by
            // accident at 40 seeds and 50-70% win rates. A guard that sits on its own boundary red-builds on noise and
            // gets widened, which is how it stops guarding. The ratio says the thing worth saying: no class is half
            // again better than another.
            int allowed = fewer * 8 / 5;
            Assert.That(more, Is.LessThanOrEqualTo(allowed),
                $"{strongest} won {more}/{runs} of the same dungeons and {weakest} only {fewer}, so the strongest may win "
                + $"at most {allowed}. ({table})");
            // Sitting exactly on the ceiling is the state audit 4 caught this guard in: it passes, then red-builds on
            // one run of noise and gets widened. Reported separately so a real parity break reads as one.
            Assert.That(more, Is.Not.EqualTo(allowed),
                $"The band has no headroom left: {strongest} {more}, {weakest} {fewer}, ceiling {allowed}. ({table})");
        }

        /// <summary>
        /// D-069: the same eight classes, played by someone who has been here before. Every other guard in this file
        /// measures an empty profile, so a talent tree could be broken end to end - as Judgement was until audit 3 -
        /// without moving one of them. This is the guard that would have seen it.
        /// </summary>
        [Test]
        public void EveryClassTreeIsWorthPlaying()
        {
            const int runs = 40;
            var catalog = ContentCatalog.CreateDefault(Difficulty.Medium);
            var wins = new List<(string id, int won)>();
            foreach (var heroClass in catalog.HeroClasses.Values)
            {
                string heroId = System.Linq.Enumerable.First(catalog.HeroIdentities.Values, h => h.ClassId == heroClass.Id).Id;
                Assert.That(BuiltUp(catalog, heroClass.Id).Talents, Is.Not.Empty,
                    $"{heroClass.Id}: the build spent no points, so this measures nothing.");
                int won = 0, stalled = 0;
                for (ulong seed = 1; seed <= runs; seed++)
                {
                    // A fresh profile a seed: a run spends the provisions off the one it is given, so sharing one would
                    // arm seed 1 and leave the other thirty-nine measuring a different player (MAINT-94).
                    var r = AutoPlayer.PlayRun(catalog, seed, MaxCommands, AutoPlayer.CasualMistakeRate,
                        MovementMode.Free, blind: true, loots: true, heroId: heroId, profile: BuiltUp(catalog, heroClass.Id));
                    if (r.Status == RunStatus.Won) won++;
                    else if (r.Status == RunStatus.InProgress) stalled++;
                }
                Assert.That(stalled, Is.LessThanOrEqualTo(1 + runs / 30), $"{heroClass.Id} stalled {stalled}/{runs} built-up runs.");
                wins.Add((heroClass.Id, won));
            }

            string table = string.Join(", ", wins.ConvertAll(w => $"{w.id} {w.won}"));
            var weakest = wins[0];
            foreach (var row in wins)
                if (row.won < weakest.won) weakest = row;
            // ONE question, and it is not parity. Measured after D-073 gave this fixture the gear and the provisions it
            // had always claimed: 37-40 of 40 on Knight's Trial, five of the eight winning every seed, and 37-40 on
            // Blobert's Wrath too. A player who has been here before, wearing the best of every slot with a full shelf
            // behind them, saturates the game - which is a design fact worth knowing and makes a spread unmeasurable
            // here. The ratio arm that used to stand below could not have failed without this floor failing first, and
            // while the Berserker sat at 40/40 it silently demanded the weakest class win 25 of 40 - a 62.5% floor
            // under an arm advertising 50%, and the one that would have gone red first (TEST-83). The spread is
            // measured where there is room to see one, by NoClassIsHopelessForACarelessPlayer.
            //
            // Thirty of forty, against a measured minimum of 37. It is the broken-tree floor: D-069 puts the same eight
            // classes at 50-66% with an EMPTY profile, so a tree that has stopped paying altogether lands at 20-26 and
            // this catches it, while four sigma of room keeps it from going red on noise.
            Assert.That(weakest.won, Is.GreaterThanOrEqualTo(runs * 3 / 4),
                $"{weakest.id} won {weakest.won}/{runs} with its whole tree spent and the best gear worn. A class tree "
                + $"that buys nothing is a broken talent, not a hard dungeon. ({table})");
        }

        /// <summary>
        /// MAINT-90: a class may learn one capstone, so a build that spends in list order always took the first-listed
        /// branch's. Sixteen of the twenty-four capstones in the game were measured by nothing at all - by the guard
        /// whose stated purpose is catching a tree broken end to end, as Judgement was until audit 3.
        /// </summary>
        [Test]
        public void EveryCapstoneCarriesABuild()
        {
            const int runs = 12;
            var catalog = ContentCatalog.CreateDefault(Difficulty.Medium);
            var table = new List<string>();
            foreach (var heroClass in catalog.HeroClasses.Values)
            {
                string heroId = catalog.HeroIdentities.Values.First(h => h.ClassId == heroClass.Id).Id;
                foreach (var branch in heroClass.Branches)
                {
                    var capstone = catalog.TalentsOf(heroClass.Id).Single(t => t.Tier == 4 && t.BranchId == branch.Id);
                    // The assertion that keeps this from measuring the first capstone three times over.
                    Assert.That(Progression.Rank(BuiltUp(catalog, heroClass.Id, capstoneBranch: branch.Id), capstone.Id),
                        Is.GreaterThan(0), $"{capstone.Id} was never learned, so this run measures a different build.");

                    int won = 0, stalled = 0;
                    for (ulong seed = 1; seed <= runs; seed++)
                    {
                        var r = AutoPlayer.PlayRun(catalog, seed, MaxCommands, AutoPlayer.CasualMistakeRate,
                            MovementMode.Free, blind: true, loots: true, heroId: heroId,
                            profile: BuiltUp(catalog, heroClass.Id, capstoneBranch: branch.Id));
                        if (r.Status == RunStatus.Won) won++;
                        else if (r.Status == RunStatus.InProgress) stalled++;
                    }
                    table.Add($"{capstone.Id} {won}");
                    Assert.That(stalled, Is.LessThanOrEqualTo(1 + runs / 6),
                        $"{capstone.Id} stalled {stalled}/{runs}: the bot stopped playing, so this measures nothing.");
                    // A third of a dozen. Far below what any of them win today and far above a capstone that has
                    // stopped paying: this is a smoke test for twenty-four builds, not a parity guard.
                    Assert.That(won, Is.GreaterThanOrEqualTo(runs / 3),
                        $"{capstone.Id} won {won}/{runs} as the capstone of a full build. ({string.Join(", ", table)})");
                }
            }
        }

        /// <summary>
        /// MAINT-94: a run SPENDS the provisions on the profile it is handed. Sharing one profile across a seed loop
        /// therefore arms the first run and no other, and the guard reports a blend of two players without a word. The
        /// coupling is invisible at the call site, so it is written down here.
        /// </summary>
        [Test]
        public void AProfileIsSpentByTheRunItIsHandedTo()
        {
            var catalog = ContentCatalog.CreateDefault(Difficulty.Medium);
            var profile = BuiltUp(catalog, "knight");
            Assert.That(profile.PotionRations, Is.GreaterThan(0), "Test setup: there is something to spend.");

            var first = new RunState { Hero = new HeroState { MaxHp = 20, Hp = 20 }, Floor = FloorState.CreateEmpty() };
            ProfileSystem.ProvisionRun(profile, first, catalog, new List<GameEvent>());
            Assert.That(profile.PotionRations, Is.Zero, "The first run took them.");

            var second = new RunState { Hero = new HeroState { MaxHp = 20, Hp = 20 }, Floor = FloorState.CreateEmpty() };
            ProfileSystem.ProvisionRun(profile, second, catalog, new List<GameEvent>());
            Assert.That(second.Hero.Potions, Is.LessThan(first.Hero.Potions),
                "And the second run is a different player - which is why the guards build a profile a seed.");
        }

        /// <summary>
        /// D-071: the classes measured where they actually come apart. Every other parity guard here watches careful
        /// play, where the eight compress into 1.3 of each other and say nothing; under sloppy play they spread 4 to 1,
        /// and on Blobert's Wrath 10 to 1. A class whose rule only pays when you position well is a demanding class,
        /// which is a design choice (the Rogue is marked as one); a class that wins one run in sixteen while another
        /// wins two in three is a balance failure, and nothing in this file could see it.
        /// </summary>
        [Test]
        public void NoClassIsHopelessForACarelessPlayer()
        {
            // SIXTY, the number D-071 was measured at, not thirty. At 30 seeds the weakest class sat 1.3 sigma above
            // the floor and the ratio arm needed 7 wins of an expected 9.9, so roughly one change in seven that merely
            // perturbed the RNG stream would have turned this red for no balance reason - and the reflex this file
            // records for that is to widen the band until it guards nothing. At 60 the floor is 2.2 sigma out (TEST-82).
            const int runs = 60;
            var catalog = ContentCatalog.CreateDefault(Difficulty.Medium);
            var wins = new List<(string id, int won)>();
            foreach (var heroClass in catalog.HeroClasses.Values)
            {
                string heroId = System.Linq.Enumerable.First(catalog.HeroIdentities.Values, h => h.ClassId == heroClass.Id).Id;
                Assert.That(BuiltUp(catalog, heroClass.Id).Talents, Is.Not.Empty,
                    $"{heroClass.Id}: the build spent no points, so this measures nothing.");
                int won = 0, stalled = 0;
                for (ulong seed = 1; seed <= runs; seed++)
                {
                    // Fresh a seed (MAINT-94), and the stalls counted (TEST-84). This guard runs at the rate the bot is
                    // likeliest to burn the command cap at, and without the tally a bot that had stopped playing would
                    // read as eight classes that all got worse at once - the MAINT-15 failure this file was written for.
                    var r = AutoPlayer.PlayRun(catalog, seed, MaxCommands, NoviceMistakeRate,
                        MovementMode.Free, blind: true, loots: true, heroId: heroId, profile: BuiltUp(catalog, heroClass.Id));
                    if (r.Status == RunStatus.Won) won++;
                    else if (r.Status == RunStatus.InProgress) stalled++;
                }
                Assert.That(stalled, Is.LessThanOrEqualTo(1 + runs / 30),
                    $"{heroClass.Id} stalled {stalled}/{runs} careless runs: the bot stopped playing, so this measures nothing.");
                wins.Add((heroClass.Id, won));
            }

            string table = string.Join(", ", wins.ConvertAll(w => $"{w.id} {w.won}"));
            var weakest = wins[0];
            var strongest = wins[0];
            foreach (var row in wins)
            {
                if (row.won < weakest.won) weakest = row;
                if (row.won > strongest.won) strongest = row;
            }
            // Measured 33% to 71% over 60 seeds a class after D-071, against 15% to 61% before it. A fifth of the runs
            // sits well below the weakest and far above a class that has stopped working for a careless player.
            Assert.That(weakest.won, Is.GreaterThanOrEqualTo(runs / 5),
                $"{weakest.id} won {weakest.won}/{runs} for a careless player. ({table})");
            // Three to one, not the eight-fifths careful play is held to: these are sloppy runs and the spread is wider
            // by nature. What this refuses is the four-to-one the classes sat at before D-071.
            // Plainly three to one. The `Math.Max(3, ...)` that stood here could only pick its 3 when the weakest had
            // won one run or none, which the floor assert above has already refused: it read as a guard against a
            // vacuous ratio and had never once executed (MAINT-91).
            Assert.That(strongest.won, Is.LessThanOrEqualTo(weakest.won * 3),
                $"{strongest.id} won {strongest.won}/{runs} where {weakest.id} won {weakest.won}. ({table})");
        }

        /// <summary>
        /// TEST-85: the two error models side by side. "Careless play" has always meant one thing - a uniform draw from
        /// the legal commands that survive the turn - and on a 5x5 board in Free Roam that list is dominated by Move,
        /// so three mistakes in four were a teleport. D-071 read the class spread off that model and shipped three
        /// balance changes on it. This prints the same classes under a slip that misreads the board instead, so the
        /// question "is the ordering a fact about the game or about the noise" has an answer on the record.
        /// </summary>
        [Test, Explicit("Measurement: the class ordering under each error model (TEST-85)")]
        public void CompareTheTwoErrorModels()
        {
            const int runs = 40;
            var catalog = ContentCatalog.CreateDefault(Difficulty.Medium);
            foreach (var model in new[] { MistakeModel.RandomCommand, MistakeModel.Misjudged })
            {
                var rows = new List<(string id, int won, int died, int stalled)>();
                foreach (var heroClass in catalog.HeroClasses.Values)
                {
                    string heroId = catalog.HeroIdentities.Values.First(h => h.ClassId == heroClass.Id).Id;
                    int won = 0, stalled = 0, died = 0;
                    for (ulong seed = 1; seed <= runs; seed++)
                    {
                        var r = AutoPlayer.PlayRun(catalog, seed, MaxCommands, NoviceMistakeRate, MovementMode.Free,
                            blind: true, loots: true, heroId: heroId,
                            profile: BuiltUp(catalog, heroClass.Id), model: model);
                        if (r.Status == RunStatus.Won) won++;
                        else if (r.Status == RunStatus.InProgress) stalled++;
                        else died++;
                    }
                    rows.Add((heroClass.Id, won, died, stalled));
                }
                rows.Sort((x, y) => y.won.CompareTo(x.won));
                TestContext.Progress.WriteLine($"{model} ({runs} blind seeds a class, careless, built-up): "
                    + string.Join(", ", rows.ConvertAll(r => $"{r.id} {r.won}")));
                TestContext.Progress.WriteLine("   stalls: " + string.Join(", ", rows.ConvertAll(r => $"{r.id} {r.stalled}")));
            }
        }

        [Test, Explicit("Tuning aid: every class on the shipped numbers, on the same dungeons")]
        public void ClassSweep()
        {
            // D-063: one row per class, played by the first hero of that class on the roster. Blind, Free Roam, Knight's
            // Trial, no talents - so it compares the classes' own numbers and rules, not their trees.
            const int runs = 40;
            var catalog = ContentCatalog.CreateDefault(Difficulty.Medium);
            TestContext.Out.WriteLine($"Class sweep (blind, Free Roam, Knight's Trial): {runs} seeds per class, won / avg hearts left / deepest floor");
            foreach (var heroClass in catalog.HeroClasses.Values)
            {
                string heroId = System.Linq.Enumerable.First(catalog.HeroIdentities.Values, h => h.ClassId == heroClass.Id).Id;
                var line = new System.Text.StringBuilder($"{heroClass.Id,-10}");
                foreach (var (skill, rate) in new[] { ("casual", AutoPlayer.CasualMistakeRate), ("novice", NoviceMistakeRate) })
                {
                    int won = 0;
                    long hp = 0, depth = 0;
                    for (ulong seed = 1; seed <= (ulong)runs; seed++)
                    {
                        var r = AutoPlayer.PlayRun(catalog, seed, MaxCommands, rate, MovementMode.Free, true, heroId: heroId);
                        if (r.Status == RunStatus.Won) won++;
                        hp += r.Hp;
                        depth += r.Floor;
                    }
                    line.Append($"  {skill} {won,2} ({hp / (float)runs,4:0.0}hp, F{depth / (float)runs,4:0.0})");
                }
                TestContext.Out.WriteLine(line.ToString());
            }
        }

        static string ByFloor(int[] counts) => string.Join(" / ", System.Linq.Enumerable.Skip(counts, 1));

        [Test, Explicit("Tuning aid: prints the end of stalled or lost AutoPlayer runs")]
        public void AutoPlayerTrace()
        {
            foreach (var tier in Tiers)
            {
                var catalog = ContentCatalog.CreateDefault(tier);
                int shown = 0;
                for (ulong seed = 1; seed <= 200 && shown < 2; seed++)
                {
                    if (AutoPlayer.PlayRun(catalog, seed, MaxCommands, AutoPlayer.CasualMistakeRate).Status == RunStatus.Won) continue;
                    shown++;
                    Trace(catalog, seed);
                }
            }
        }

        static void Trace(ContentCatalog catalog, ulong seed)
        {
            {
                var run = RunFactory.NewRun(seed, catalog, new List<GameEvent>());
                var player = new AutoPlayer(AutoPlayer.CasualMistakeRate);
                var lines = new List<string>();
                for (int i = 0; i < MaxCommands && run.Status == RunStatus.InProgress; i++)
                {
                    var command = player.Choose(run, catalog, seed * 7919UL + (ulong)i);
                    var enemies = string.Join(" ", run.Floor.Enemies.Select(e => $"{e.DefId}@{e.Pos}{(e.Awake ? "!" : "z")}{e.Intent}"));
                    lines.Add($"#{i} F{run.Floor.FloorIndex} hero {run.Hero.Pos} hp {run.Hero.Hp} key {run.Hero.HasKey} -> {command} | {enemies}");
                    TurnResolver.Apply(run, command, catalog);
                }
                TestContext.Out.WriteLine($"{catalog.Difficulty} seed {seed}: {run.Status} floor {run.Floor.FloorIndex} turn {run.Turn}");
                foreach (var line in lines.Skip(Math.Max(0, lines.Count - 12))) TestContext.Out.WriteLine("  " + line);
                for (int y = BoardRules.Size - 1; y >= 0; y--)
                {
                    var row = new System.Text.StringBuilder("  ");
                    for (int x = 0; x < BoardRules.Size; x++)
                    {
                        var p = new GridPos(x, y);
                        var cell = run.Floor[p];
                        char ch = cell.Terrain == Terrain.Wall ? '#' : cell.Terrain == Terrain.Pit ? 'o' : '.';
                        if (cell.Hazard == HazardKind.Spikes) ch = '^';
                        else if (cell.Hazard == HazardKind.Bomb) ch = 'b';
                        if (cell.Content == ContentKind.Key) ch = 'K';
                        else if (cell.IsClosedChest) ch = 'C';
                        else if (cell.Content == ContentKind.Potion) ch = 'P';
                        if (cell.IsExit) ch = run.Floor.ExitUnlocked ? 'x' : 'X';
                        if (run.Floor.EnemyAt(p) != null) ch = run.Floor.EnemyAt(p).Awake ? 'E' : 'e';
                        if (run.Hero.Pos == p) ch = 'H';
                        row.Append(ch);
                    }
                    TestContext.Out.WriteLine(row.ToString());
                }
            }
        }

        static string Pct(int count, int runs) => $"{100.0 * count / runs:0}%";
    }
}
