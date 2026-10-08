using System.Collections.Generic;
using System.Linq;
using ClickDungeon.Application;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using NUnit.Framework;
using static ClickDungeon.Tests.Scenario;

namespace ClickDungeon.Tests
{
    /// <summary>D-037: each class has its own talent tree; its tiers, prerequisites and capstone choice; and what each does.</summary>
    public class ClassTalentTests
    {
        /// <summary>A scenario run with these talents' perks, as a profile with them learned would start it.</summary>
        static RunState With(RunState run, params (TalentEffect effect, int value)[] perks)
        {
            foreach (var (effect, value) in perks) run.Perks[effect.ToString()] = value;
            // D-082: a ward is a thing the hero CARRIES, placed when the floor begins. Granting the talent after the
            // floor was built is a thing only a test does, so the placement the game would have done happens here -
            // through PRODUCTION's own function, not a copy of its arithmetic. Writing `1 + ward` out here again made
            // the test below pass with production's placement deleted entirely (TEST-107).
            Usables.PlaceTalentWard(run, null);
            return run;
        }

        [Test]
        public void EachPlayableClassHasItsOwnWellFormedTree()
        {
            foreach (var heroClass in Catalog.HeroClasses.Values)
            {
                var tree = Catalog.TalentsOf(heroClass.Id);
                // Eleven: three skills and the eight bonuses on the roads between them (D-087).
                Assert.That(tree.Count, Is.EqualTo(11), heroClass.Id);
                Assert.That(heroClass.Branches.Length, Is.EqualTo(3), heroClass.Id);
                // The three branches are the triangle's three EDGES now (D-087). Two run from the apex skill to a
                // corner skill - three bonuses then the skill, so tiers 1-4 - and the third is the base between the
                // corners, two bonuses and no skill of its own. Every edge is still a chain: each rung needs the one
                // below it, which is what makes skipping an edge cost you the skill at its end.
                var apexNode = tree.Single(t => t.BranchId == "apex");
                foreach (var branch in heroClass.Branches)
                {
                    var path = tree.Where(t => t.BranchId == branch.Id).OrderBy(t => t.Tier).ToList();
                    var tiers = path.Select(t => t.Tier).ToArray();
                    Assert.That(tiers, Is.EqualTo(new[] { 1, 2, 3, 4 }).Or.EqualTo(new[] { 1, 2 }), branch.Id);
                    bool isEdge = tiers.Length == 4;
                    // WHERE the skill sits, not just how many (D-088). Counting alone let a corner skill be parked at
                    // tier 1, where every build would get it free and skipping the edge would cost nothing.
                    Assert.That(path.Count(t => t.SkillId != null), Is.EqualTo(isEdge ? 1 : 0), branch.Id);
                    if (isEdge)
                    {
                        Assert.That(path[3].SkillId, Is.Not.Null, $"{branch.Id}: the skill belongs at the END of the edge.");
                        Assert.That(path[3].Capstone, Is.True, $"{branch.Id}: the end of an edge is a capstone.");
                        Assert.That(path.Take(3).Any(t => t.Capstone), Is.False, $"{branch.Id}: only the end is a capstone.");
                    }
                    // Rung 1 included: the root is the least obvious edge in the shape and used to be asserted by
                    // nothing, so an edge could float free of the apex and the whole suite stayed green.
                    if (isEdge)
                        Assert.That(path[0].Requires, Is.EqualTo(apexNode.Id), $"{branch.Id}: an edge starts at the apex.");
                    else
                        Assert.That(path[0].RequiresAny, Is.EquivalentTo(tree.Where(t => t.Capstone).Select(t => t.Id)),
                            $"{branch.Id}: the rail opens from either corner.");
                    for (int i = 1; i < path.Count; i++) Assert.That(path[i].Requires, Is.EqualTo(path[i - 1].Id), path[i].Id);
                }
                // And the apex skill, which belongs to no edge because both start from it.
                Assert.That(tree.Count(t => t.BranchId == "apex"), Is.EqualTo(1), heroClass.Id);
                Assert.That(apexNode.Requires, Is.Null, $"{heroClass.Id}: the apex stands on nothing.");
                Assert.That(apexNode.SkillId, Is.Not.Null, $"{heroClass.Id}: the apex pays the skill every build starts with.");
                Assert.That(tree.Count(t => t.Capstone), Is.EqualTo(2), $"{heroClass.Id}: two corners, and a build takes one.");
                foreach (var talent in tree)
                {
                    Assert.That(talent.Name, Is.Not.Empty);
                    Assert.That(talent.Summary, Is.Not.Empty);
                    Assert.That(talent.PerRank, Is.Not.Empty);
                }
            }
            var knight = Catalog.TalentsOf("knight").Select(t => t.Effect);
            var paladin = Catalog.TalentsOf("paladin").Select(t => t.Effect);
            Assert.That(knight.Intersect(paladin), Is.EquivalentTo(new[] { TalentEffect.MaxHearts }), "Only the one basic stat is shared.");
            Assert.That(Catalog.Talents.Select(t => t.Id).Distinct().Count(), Is.EqualTo(Catalog.Talents.Count));
        }

        [Test]
        public void TiersOpenWithPointsSpentAndTheTalentBelow()
        {
            // An edge is a chain: every rung needs the one below it, and the apex needs nothing because both edges
            // start there (D-087). The old tier table is gone - what gates a node now is the node beneath it.
            var profile = new ProfileState { Xp = Progression.XpForLevel(12) };
            var tree = Catalog.TalentsOf("knight");
            var apex = tree.Single(t => t.BranchId == "apex");
            var edge = tree.Where(t => t.BranchId == apex.BranchId).ToList();
            var road = tree.Where(t => t.Requires == apex.Id).ToList();
            Assert.That(road.Count, Is.EqualTo(2), "Two edges leave the apex.");

            Assert.That(Progression.Locked(profile, Catalog, apex.Id), Is.Null, "The apex is open from the start.");
            var first = road[0];
            var second = tree.Single(t => t.Requires == first.Id);
            Assert.That(Progression.Locked(profile, Catalog, second.Id), Does.Contain(first.Name),
                "A rung names the one below it while that one is unlearned.");

            Assert.That(LearnTo(profile, apex.Id), Is.True);
            Assert.That(LearnTo(profile, apex.Id), Is.False, "One rank.");
            Assert.That(Progression.Locked(profile, Catalog, first.Id), Is.Null, "And now the edge is open.");
        }

        [Test]
        public void AClassTakesOnlyOneCapstone()
        {
            // THE FORK (D-088), and it is asserted by ATTEMPTING the forbidden corner. The version this replaced read
            // the rejected corner's rank without ever trying to learn it - a talent nobody attempts is rank 0 whatever
            // the rules say, so it passed unconditionally, and the rule it named was false besides: nine points bought
            // both corners. Every class, both directions, and the refusal has to say why.
            foreach (var heroClass in Catalog.HeroClasses.Values)
            {
                var tree = Catalog.TalentsOf(heroClass.Id);
                if (tree.Count == 0) continue;
                var apex = tree.Single(t => t.BranchId == "apex");
                var corners = tree.Where(t => t.Capstone).ToList();
                Assert.That(corners.Count, Is.EqualTo(2), $"{heroClass.Id}: two corners to choose between.");
                Assert.That(corners.All(t => t.SkillId != null), Is.True,
                    $"{heroClass.Id}: a corner is a capstone because it pays a skill.");

                for (int d = 0; d < 2; d++)
                {
                    var taken = corners[d];
                    var refused = corners[1 - d];
                    string who = $"{heroClass.Id} via {taken.BranchId}";
                    // Level 20 - far more points than either road costs - so what stops the second corner is the rule
                    // and not the purse. At level 12 a refusal proves nothing: the points would have run out anyway.
                    var profile = new ProfileState { Xp = Progression.XpForLevel(20) };
                    Assert.That(LearnTo(profile, apex.Id), Is.True, $"{who}: the apex is where every build starts.");
                    Assert.That(LearnTo(profile, taken.Id), Is.True, $"{who}: could not walk its own road.");

                    Assert.That(Progression.PointsFree(profile, Catalog, heroClass.Id), Is.GreaterThan(
                        Catalog.TalentsOf(heroClass.Id).Count(t => t.BranchId == refused.BranchId)),
                        $"{who}: not enough points left over for the refusal to mean anything.");
                    Assert.That(LearnTo(profile, refused.Id), Is.False, $"{who}: took both corners.");
                    Assert.That(Progression.Rank(profile, refused.Id), Is.Zero, $"{who}: the refused corner was learned.");
                    Assert.That(Progression.Locked(profile, Catalog, refused.Id),
                        Does.Contain(taken.Name), $"{who}: the refusal does not say which corner is chosen.");

                    // And it is a commitment, not a sentence: resetting the tree reopens the other road.
                    Progression.Reset(profile, Catalog, heroClass.Id);
                    Assert.That(Progression.PointsSpent(profile, Catalog, heroClass.Id), Is.Zero, $"{who}: reset refunds the points.");
                    Assert.That(LearnTo(profile, refused.Id), Is.True, $"{who}: reset did not reopen the other corner.");
                }
            }
        }

        [Test]
        public void TheSharedRailOpensFromEitherCorner()
        {
            // The base is shared progression, not a road between the corners (D-088): whichever corner a build
            // commits to opens it. Under D-087 it hung off one named corner, so half of every class's builds could
            // not reach two of its eleven talents at all, while the screen drew a triangle saying they could.
            foreach (var heroClass in Catalog.HeroClasses.Values)
            {
                var tree = Catalog.TalentsOf(heroClass.Id);
                if (tree.Count == 0) continue;
                var rail = tree.Where(t => t.RequiresAny != null && t.RequiresAny.Length > 0).ToList();
                Assert.That(rail.Count, Is.EqualTo(1), $"{heroClass.Id}: one node joins the rail to the fork.");
                var corners = tree.Where(t => t.Capstone).ToList();
                Assert.That(rail[0].RequiresAny, Is.EquivalentTo(corners.Select(t => t.Id)),
                    $"{heroClass.Id}: the rail opens from exactly the two corners.");

                foreach (var corner in corners)
                {
                    var profile = new ProfileState { Xp = Progression.XpForLevel(20) };
                    foreach (var name in corners.Select(t => t.Name))
                        Assert.That(Progression.Locked(profile, Catalog, rail[0].Id), Does.Contain(name),
                            $"{heroClass.Id}: the rail is open, or does not name both corners, before either is taken.");
                    Assert.That(LearnTo(profile, corner.Id), Is.True, heroClass.Id);
                    Assert.That(Progression.Locked(profile, Catalog, rail[0].Id), Is.Null,
                        $"{heroClass.Id}: {corner.BranchId} did not open the shared rail.");
                }
            }
        }

        [Test]
        public void EachClassSpendsTheLevelsPointsOnItsOwnTree()
        {
            // Four, not three: reaching any bonus costs the apex skill beneath it as well (D-087), so three points buy
            // the apex and two ranks of the node this test is about. It has to be a node ON A ROAD - the shared rail
            // sits behind a capstone since D-088, so nothing there is reachable with three points.
            var profile = new ProfileState { Xp = Progression.XpForLevel(4) };
            Assert.That(LearnTo(profile, "k_opening_strike"), Is.True);
            Assert.That(LearnTo(profile, "k_opening_strike"), Is.True);
            Assert.That(Progression.PointsFree(profile, Catalog, "knight"), Is.Zero);
            Assert.That(Progression.PointsFree(profile, Catalog, "paladin"), Is.EqualTo(3), "The Paladin has its own points.");
            Progression.Reset(profile, Catalog, "paladin");
            Assert.That(Progression.Rank(profile, "k_opening_strike"), Is.EqualTo(2), "Resetting one class leaves the other.");
        }

        [Test]
        public void OldTalentsFromBeforeTheTreesAreRefunded()
        {
            var profile = new ProfileState { Xp = Progression.XpForLevel(4) };
            profile.Talents["tough"] = 3;
            Assert.That(Progression.PointsFree(profile, Catalog, "knight"), Is.EqualTo(3));
        }

        [Test]
        public void OnlyThePlayingClassesTalentsShapeTheRun()
        {
            // Seven: the hearts moved to the shared rail (D-088), which opens only once a road has been walked to its
            // capstone, so the +1 heart this asserts now costs six points rather than two. Points are per class, so
            // the Paladin still pays only for its own.
            var profile = new ProfileState { Xp = Progression.XpForLevel(7) };
            Assert.That(LearnTo(profile, "k_sturdy"), Is.True, "k_sturdy");
            Assert.That(LearnTo(profile, "p_holy_wrath"), Is.True, "p_holy_wrath");
            var knight = RunFactory.NewRun(3UL, Catalog, new List<GameEvent>());
            Progression.Apply(profile, knight, Catalog);
            Assert.That(knight.Hero.MaxHp, Is.EqualTo(Catalog.HeroClass("knight").MaxHp + 1));
            Assert.That(knight.Perk(TalentEffect.HolyWrath), Is.Zero);
            var paladin = RunFactory.NewRun(3UL, Catalog, new List<GameEvent>(), "dawnward");
            Progression.Apply(profile, paladin, Catalog);
            Assert.That(paladin.Hero.MaxHp, Is.EqualTo(Catalog.HeroClass("paladin").MaxHp));
            Assert.That(paladin.Perk(TalentEffect.HolyWrath), Is.EqualTo(1));
        }

        [Test]
        public void TheUndeadAreTheRisenAndNothingElse()
        {
            // The tag carries rules, so what wears it is a design statement, not a convenience. Bones and spectres and
            // the book that animates them are risen; a mimic is furniture and a demon was never alive (D-072).
            // Both directions, exhaustively. A list of "these are not undead" cannot see a fourth monster being tagged,
            // and the one D-072 argues hardest about - the Theater Curtain Demon, the only boss in that sentence - was
            // missing from it. Tagging a boss undead would stack Holy Wrath with Dawnstrike silently (TEST-102).
            Assert.That(Catalog.Enemies.Values.Where(e => e.Undead).Select(e => e.Id),
                Is.EquivalentTo(new[] { "skeleton", "spooky_spellbook", "spectral_page" }),
                "The risen are exactly these three.");
            Assert.That(Catalog.Enemy("mimic_chest").Undead, Is.False, "A mimic is furniture.");
            Assert.That(Catalog.Enemy("theater_curtain_demon").Undead, Is.False, "A demon was never alive.");
        }

        [Test]
        public void HolyWrathAnswersTheRisenAndLeavesTheLivingAlone()
        {
            // Z is a Skeleton Warrior, G a goblin: same slash, one of them holy.
            var undead = With(As(Run(".....", ".....", ".HZ..", ".....", "....."), "dawnward"), (TalentEffect.HolyWrath, 2));
            var living = With(As(Run(".....", ".....", ".HG..", ".....", "....."), "dawnward"), (TalentEffect.HolyWrath, 2));
            int bare = living.Hero.SlashDamage;

            Assert.That(Talents.SlashDamage(undead, undead.Floor.Enemies[0], Catalog), Is.EqualTo(bare + 2),
                "Two ranks of Holy Wrath, two more damage to what is risen.");
            Assert.That(Talents.SlashDamage(living, living.Floor.Enemies[0], Catalog), Is.EqualTo(bare),
                "A goblin is alive and feels nothing of it.");

            // A Skeleton Warrior is the only undead that also reassembles, so testing only against it would pass if the
            // rule read the wrong flag. A Spectral Page is risen and does not get back up (TEST-103).
            var page = Run(".....", ".....", ".H...", ".....", ".....");
            EnemyAi.Spawn(page.Floor, Catalog.Enemy("spectral_page"), P(2, 2), awake: true);
            var spectral = As(page, "dawnward");
            spectral.Perks[TalentEffect.HolyWrath.ToString()] = 2;
            Assert.That(Catalog.Enemy("spectral_page").Reassembles, Is.False, "Test setup: it is risen but does not rise again.");
            Assert.That(Talents.SlashDamage(spectral, spectral.Floor.Enemies[0], Catalog),
                Is.EqualTo(spectral.Hero.SlashDamage + 2), "Holy Wrath reads the tag, not a stand-in for it.");
        }

        [Test]
        public void ARisenBossTakesBothBonuses()
        {
            // Talents.cs says "a boss can be both, and they stack" and nothing tested it: the roster ships no undead
            // boss, so the old test spawned a skeleton, asserted one bonus, and proved only what its neighbour already
            // did. The rule is about a combination of two flags, so the fixture has to make that combination (TEST-92).
            var catalog = ContentCatalog.CreateDefault();
            catalog.Enemy("skeleton").IsBoss = true;
            var run = With(As(Run(".....", ".....", ".HZ..", ".....", "....."), "dawnward"),
                (TalentEffect.HolyWrath, 1), (TalentEffect.Dawnstrike, 1));
            var risenBoss = run.Floor.Enemies[0];
            int bare = run.Hero.SlashDamage;

            Assert.That(Talents.SlashDamage(run, risenBoss, catalog), Is.EqualTo(bare + 2),
                "Risen and a boss: both bonuses, added, not the larger of the two.");
            Assert.That(Catalog.Enemies.Values.Any(e => e.IsBoss && e.Undead), Is.False,
                "No shipped monster is both today - this test pins the rule, and says so rather than implying a roster.");
        }

        [Test]
        public void TheSlashTheHudPromisesIsTheSlashTheHeroLands()
        {
            // REL-90: the HUD kept its own copy of the bonus list and took Judgement, Dawnstrike and Holy Wrath as
            // alternatives where the rule adds them. Literal numbers, because "the same as what SlashDamage returns"
            // is a tautology against a second copy of the arithmetic - the whole defect was two copies agreeing.
            var catalog = ContentCatalog.CreateDefault();
            catalog.Enemy("skeleton").IsBoss = true;
            var run = With(As(Run(".....", ".....", ".HZ..", ".....", "....."), "dawnward"),
                (TalentEffect.HolyWrath, 1), (TalentEffect.Dawnstrike, 1), (TalentEffect.Judgement, 1));
            run.Hero.SlashDamage = 3;
            var risenBoss = run.Floor.Enemies[0];
            risenBoss.Staggered = true;

            Talents.SlashSpan(run, catalog, out int low, out int high);
            Assert.That(low, Is.EqualTo(3), "What a slash carries whatever it meets.");
            Assert.That(high, Is.EqualTo(6), "Risen, a boss and reeling: all three, added. The HUD used to say 4.");

            // And it is the board's best target, not a catalogue of everything the hero owns: out of reach, out of the span.
            risenBoss.Pos = P(4, 4);
            Talents.SlashSpan(run, catalog, out low, out high);
            Assert.That(high, Is.EqualTo(low), "Nothing within reach, nothing to promise.");
        }

        // ------------------------------------------------------------------ Knight

        [Test]
        public void OpeningStrikeAndExecutionerHitHarderAtEachEndOfAFight()
        {
            var run = With(Run(".....", ".....", ".HC..", ".....", "....."), (TalentEffect.OpeningStrike, 2), (TalentEffect.Executioner, 1));
            var slime = EnemyAi.Spawn(run.Floor, Catalog.Enemy("crowned_slime"), P(2, 3), awake: true);
            int baseDamage = run.Hero.SlashDamage;
            Assert.That(Talents.SlashDamage(run, slime, Catalog), Is.EqualTo(baseDamage + 2), "Full health.");
            slime.Hp = 2;
            Assert.That(Talents.SlashDamage(run, slime, Catalog), Is.EqualTo(baseDamage + 1), "Nearly dead.");
            slime.Hp = 4;
            Assert.That(Talents.SlashDamage(run, slime, Catalog), Is.EqualTo(baseDamage));
        }

        [Test]
        public void CleaveHitsTheOtherEnemiesBesideYou()
        {
            var run = With(Run(".....", ".....", ".HG..", ".G...", "....."), (TalentEffect.Cleave, 1));
            var first = run.Floor.Enemies[0];
            var second = run.Floor.Enemies[1];
            first.Awake = second.Awake = true;
            DoOk(run, PlayerCommand.Slash(first.Pos));
            Assert.That(second.Hp, Is.EqualTo(second.MaxHp - 1));
        }

        [Test]
        public void RelentlessRestoresOnAKillAndRiposteAnswersABlock()
        {
            var run = With(Run(".....", ".....", ".HG..", ".....", "....."), (TalentEffect.Relentless, 1));
            run.Hero.Hp = 3;
            run.Hero.Mana = 0;
            var goblin = Enemy(run, "goblin");
            goblin.Hp = 1;
            DoOk(run, PlayerCommand.Slash(goblin.Pos));
            Assert.That(run.Hero.Hp, Is.EqualTo(4));
            Assert.That(run.Hero.Mana, Is.EqualTo(2 + Mana.PerTurn));

            var block = With(Run(".....", "..G..", "..H..", ".....", "....."), (TalentEffect.Riposte, 2));
            var attacker = Enemy(block, "goblin");
            for (int i = 0; i < 4 && attacker.Intent.Kind != IntentKind.Attack; i++) DoOk(block, PlayerCommand.Wait());
            Assert.That(attacker.Intent.Kind, Is.EqualTo(IntentKind.Attack));
            block.Hero.Hp = block.Hero.MaxHp;
            DoOk(block, PlayerCommand.Shield());
            Assert.That(attacker.Hp, Is.EqualTo(attacker.MaxHp - 2), "Blocked, and answered.");
        }

        /// <summary>
        /// D-052: Riposte and Holy Bulwark are written with the same promise — "a blocked attack" — so they answer the
        /// same blows. A fire imp's lane shot and Lord Blobert's slam are blocked attacks like any other.
        /// </summary>
        [Test]
        public void RiposteAnswersEveryBlockItIsPromisedFor()
        {
            var fire = With(Run(".....", ".....", "I.H..", ".....", "....."), (TalentEffect.Riposte, 1));
            var imp = Enemy(fire, "fire_imp");
            for (int i = 0; i < 6 && imp.Intent.Kind != IntentKind.Fire; i++) DoOk(fire, PlayerCommand.Wait());
            Assert.That(imp.Intent.Kind, Is.EqualTo(IntentKind.Fire), "Test setup: the imp is about to shoot down the lane.");
            fire.Hero.Hp = fire.Hero.MaxHp;
            DoOk(fire, PlayerCommand.Shield());
            Assert.That(imp.Hp, Is.EqualTo(imp.MaxHp - 1), "A blocked fireball is answered like a blocked sword.");
        }

        [Test]
        public void ShieldWallBastionAndTreasureSense()
        {
            var run = With(Run(".....", ".....", ".HC..", ".....", "....."), (TalentEffect.ShieldCostCut, 1), (TalentEffect.Bastion, 1), (TalentEffect.ChestTapCut, 1));
            var knight = Catalog.HeroClass("knight");
            Assert.That(Mana.ShieldCost(run, knight), Is.EqualTo(knight.ShieldCost - 1));
            run.Hero.Hp = 5;
            DoOk(run, PlayerCommand.Shield());
            Assert.That(run.Hero.Hp, Is.EqualTo(6), "Bastion mends.");
            run.Floor[P(2, 2)].Quality = ChestQuality.Epic;
            Assert.That(Chests.TapsToOpen(run, run.Floor[P(2, 2)]), Is.EqualTo(Chests.TapsToOpen(ChestQuality.Epic) - 1));
            run.Floor[P(2, 2)].Quality = ChestQuality.Common;
            Assert.That(Chests.TapsToOpen(run, run.Floor[P(2, 2)]), Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void SecondWindHealsMoreOnEveryNewFloor()
        {
            var plain = Run(".....", ".....", ".HxK.", ".....", ".....");
            var winded = With(Run(".....", ".....", ".HxK.", ".....", "....."), (TalentEffect.SecondWind, 3));
            foreach (var run in new[] { plain, winded })
            {
                // Exactly half, so the stairs' mercy (D-071) adds nothing and this measures Second Wind alone.
                run.Hero.Hp = run.Hero.MaxHp / 2;
                DoOk(run, PlayerCommand.Move(P(3, 2)));
                DoOk(run, PlayerCommand.Move(P(2, 2)));
            }
            Assert.That(winded.Hero.Hp, Is.EqualTo(System.Math.Min(winded.Hero.MaxHp, plain.Hero.Hp + 3)));
        }

        // ------------------------------------------------------------------ Paladin

        [Test]
        public void JudgementHitsAnEnemyTheShieldJustStaggered()
        {
            // REL-23: Staggered is set in the enemy phase and cleared in the same turn's declare step, so the flag a slash
            // reads on the player's next turn is always false. What lasts is the Recover intent the board calls a free hit.
            int SlashAStaggeredGoblin(params (TalentEffect effect, int value)[] perks)
            {
                var run = With(Run(".....", ".....", ".HG..", ".....", "....."), perks);
                var goblin = Enemy(run, "goblin");
                // Plenty of hearts, so the blow can be read off them instead of killing it.
                goblin.MaxHp = goblin.Hp = 20;
                Assert.That(goblin.Intent.Kind, Is.EqualTo(IntentKind.Attack), "Test setup: the goblin is about to swing.");

                DoOk(run, PlayerCommand.Shield());
                Assert.That(goblin.Staggered, Is.False, "The stagger flag never outlives the turn that set it.");
                Assert.That(goblin.Intent.Kind, Is.EqualTo(IntentKind.Recover), "A blocked blow staggers: it does nothing next turn.");

                int before = goblin.Hp;
                DoOk(run, PlayerCommand.Slash(goblin.Pos));
                return before - goblin.Hp;
            }

            Assert.That(SlashAStaggeredGoblin((TalentEffect.Judgement, 2)),
                Is.EqualTo(SlashAStaggeredGoblin() + 2), "Judgement has to land on the enemy the shield just staggered.");
        }

        [Test]
        public void DawnstrikePunishesTheBoss()
        {
            var boss = With(Run(Catalog.RunFloorCount, 1234UL, ".....", ".....", ".HB..", ".....", "....X"), (TalentEffect.Dawnstrike, 1));
            Assert.That(Talents.SlashDamage(boss, Enemy(boss, "lord_blobert"), Catalog), Is.EqualTo(boss.Hero.SlashDamage + 1));
        }

        [Test]
        public void ConsecrateBurnsNeighboursAndWrathStaggersThemOnAKill()
        {
            var run = With(Run(".....", ".....", ".HG..", ".G...", "....."), (TalentEffect.Consecrate, 1));
            foreach (var e in run.Floor.Enemies) e.Awake = true;
            DoOk(run, PlayerCommand.Shield());
            Assert.That(run.Floor.Enemies.All(e => e.Hp == e.MaxHp - 1), Is.True);

            var wrath = With(Run(".....", ".....", ".HG..", ".G...", "....."), (TalentEffect.WrathOfDawn, 1));
            foreach (var e in wrath.Floor.Enemies) e.Awake = true;
            var target = wrath.Floor.Enemies[0];
            var other = wrath.Floor.Enemies[1];
            target.Hp = 1;
            var result = DoOk(wrath, PlayerCommand.Slash(target.Pos));
            Assert.That(result.Events.Any(e => e.Kind == GameEventKind.EnemyStaggered && e.ActorId == other.Id), Is.True);
        }

        [Test]
        public void HolyBulwarkUnyieldingAndDivineShield()
        {
            var run = With(Run(".....", ".....", "..H..", ".....", "....."), (TalentEffect.HolyBulwark, 2), (TalentEffect.Unyielding, 1), (TalentEffect.DivineShield, 3));
            var hero = run.Hero;
            var events = new List<GameEvent>();
            hero.Guard = true;
            hero.Mana = 0;
            Combat.DamageHero(run, 3, "goblin", events);
            Assert.That(hero.Mana, Is.EqualTo(2), "A block restores mana.");
            hero.Guard = false;
            hero.Hp = hero.MaxHp / 2;
            int before = hero.Hp;
            Combat.DamageHero(run, 3, "goblin", events);
            Assert.That(hero.Hp, Is.EqualTo(before - 2), "Softened at half hearts.");
            Combat.DamageHero(run, 99, "slam", events);
            Assert.That(hero.Hp, Is.EqualTo(4), "The ward holds: 1 heart plus 3.");
            Combat.DamageHero(run, 99, "slam", events);
            Assert.That(hero.Hp, Is.Zero, "Once per floor.");
        }

        [Test]
        public void PrayerSanctifiedAndGuidingLight()
        {
            var run = With(Run(".....", ".....", "..H..", ".....", "....."), (TalentEffect.Prayer, 1), (TalentEffect.Sanctified, 1));
            run.Hero.Mana = 0;
            DoOk(run, PlayerCommand.Wait());
            Assert.That(run.Hero.Mana, Is.EqualTo(1 + Mana.PerTurn));
            run.Hero.Hp = 1;
            run.Hero.Potions = 1;
            DoOk(run, PlayerCommand.Potion());
            Assert.That(run.Hero.Mana, Is.EqualTo(run.Hero.MaxMana), "A potion refills mana.");

            // The capstone FIRST (D-088). Blessed Draught is on the shared rail, which either corner opens, so walking
            // to it from a blank profile commits to whichever corner the rail lists first - the Hammer - which would
            // then refuse Prayer at the end of the Aegis road. Committing to Aegis first opens the rail from that
            // side instead. The order is the build; it is not interchangeable any more.
            var profile = new ProfileState { Xp = Progression.XpForLevel(20) };
            foreach (var id in new[] { "p_prayer", "p_plated", "p_blessed_draught" })
                Assert.That(LearnTo(profile, id), Is.True, id);
            Assert.That(Progression.Rank(profile, "p_plated"), Is.EqualTo(2),
                "Plated twice - once on the way to Prayer, once for itself - and the rail reached through that capstone.");
            var session = new GameSession(Catalog, null);
            foreach (var kv in profile.Talents) session.Profile.Talents[kv.Key] = kv.Value;
            session.Profile.Xp = profile.Xp;

            // GuidingLight left the Paladin with the bonus the triangle dropped (D-087). The Ranger's Tracker still
            // grants it, and the EFFECT is what this test is about - so it is learned AND PLAYED as the Ranger,
            // because Progression.Apply only reads the talents of the class actually in the dungeon.
            var tracker = Catalog.Talents.Single(t => t.Effect == TalentEffect.GuidingLight);
            Assert.That(LearnTo(session.Profile, tracker.Id), Is.True, tracker.Id);
            string ranger = Catalog.HeroIdentities.Values.First(h => h.ClassId == tracker.ClassId).Id;
            session.StartNewRun(21UL, Difficulty.Medium, MovementMode.Free, ranger);
            var floor = session.Run.Floor;
            var key = Board.AllCells.FirstOrDefault(p => floor[p].Content == ContentKind.Key);
            Assert.That(floor[key].Knowledge, Is.EqualTo(Knowledge.Revealed), "The key starts uncovered.");
        }
    }
}
