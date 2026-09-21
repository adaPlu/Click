using System.Collections.Generic;
using ClickDungeon.Application;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using NUnit.Framework;
using static ClickDungeon.Tests.Scenario;

namespace ClickDungeon.Tests
{
    /// <summary>
    /// D-061: the second expansion monsters. As with the first, each adds one rule and every rule is telegraphed the turn
    /// before it lands; these tests pin what each does and that the warning matches it.
    /// </summary>
    public class SecondWaveMonsterTests
    {
        static RunState Revealed(RunState run)
        {
            foreach (var p in Board.AllCells) run.Floor[p].Knowledge = Knowledge.Revealed;
            return run;
        }

        // ------------------------------------------------------------------ Cave Spider

        [Test]
        public void TheSpiderWebsTheHerosTileAndHoldsThemForATurn()
        {
            var run = Revealed(Run(".....", ".....", "A..H.", ".....", "....."));
            var spider = Enemy(run, "cave_spider");
            var tile = run.Hero.Pos;
            Assert.That(spider.Intent.Kind, Is.EqualTo(IntentKind.Web));
            Assert.That(spider.Intent.Target, Is.EqualTo(tile));
            Assert.That(Threats.Compute(run, Catalog).Exists(t => t.Kind == ThreatKind.Web && t.Cell == tile), Is.True,
                "The web's landing tile is marked the turn before.");

            var caught = DoOk(run, PlayerCommand.Wait());
            Assert.That(Has(caught, GameEventKind.HeroWebbed), Is.True);
            Assert.That(run.Hero.WebbedTurns, Is.EqualTo(1));

            var move = Do(run, PlayerCommand.Move(P(4, 1)));
            Assert.That(move.Accepted, Is.False, "Stuck: no moving.");
            Assert.That(Do(run, PlayerCommand.Dash(P(4, 1))).Accepted, Is.False, "And no dashing out either.");

            DoOk(run, PlayerCommand.Wait());   // the turn the web holds
            Assert.That(run.Hero.WebbedTurns, Is.Zero);
            DoOk(run, PlayerCommand.Move(P(4, 1)));
        }

        [Test]
        public void SteppingOffTheMarkedTileLeavesTheWebEmpty()
        {
            var run = Revealed(Run(".....", ".....", "A..H.", ".....", "....."));
            var result = DoOk(run, PlayerCommand.Move(P(4, 0)));
            Assert.That(Has(result, GameEventKind.HeroWebbed), Is.False);
            Assert.That(run.Hero.WebbedTurns, Is.Zero);
        }

        [Test]
        public void AWebSurvivesASaveAndEndsOnANewFloor()
        {
            var run = Revealed(Run(".....", ".....", "A..H.", ".....", "....."));
            DoOk(run, PlayerCommand.Wait());
            var loaded = SaveSerializer.FromJson(SaveSerializer.ToJson(run));
            Assert.That(loaded.Hero.WebbedTurns, Is.EqualTo(1), "A reload must not free the hero.");

            RunFactory.SetupFloor(run, Catalog, new List<GameEvent>());
            Assert.That(run.Hero.WebbedTurns, Is.Zero, "A web does not follow the hero down the stairs.");
        }

        // ------------------------------------------------------------------ Spooky Spellbook

        [Test]
        public void TheSpellbookSummonsAPageBesideIt()
        {
            var run = Revealed(Run(".....", ".....", "Y....", ".....", "...H."));
            var book = Enemy(run, "spooky_spellbook");
            Assert.That(book.Intent.Kind, Is.EqualTo(IntentKind.Summon));
            Assert.That(Threats.Compute(run, Catalog).Exists(t => t.Kind == ThreatKind.Summon), Is.True);

            DoOk(run, PlayerCommand.Wait());

            var page = Enemy(run, "spectral_page");
            Assert.That(page, Is.Not.Null);
            Assert.That(page.Pos.IsAdjacent(book.Pos), Is.True);
            Assert.That(book.Intent.Kind, Is.EqualTo(IntentKind.Rest), "It rests after calling one up.");
        }

        [Test]
        public void TheSpellbookKeepsNoMoreThanTwoPagesAlive()
        {
            var run = Revealed(Run(".....", ".....", "Y....", ".....", "...H."));
            var book = Enemy(run, "spooky_spellbook");
            EnemyAi.Spawn(run.Floor, Catalog.Enemy("spectral_page"), P(0, 3), awake: true);
            EnemyAi.Spawn(run.Floor, Catalog.Enemy("spectral_page"), P(1, 2), awake: true);
            book.ActionCounter = 0;
            book.Intent = Intent.None();

            EnemyAi.Declare(run, book, Catalog, new List<GameEvent>());

            Assert.That(book.Intent.Kind, Is.Not.EqualTo(IntentKind.Summon));
        }

        [Test]
        public void BetweenPagesTheSpellbookFiresDownALane()
        {
            var run = Revealed(Run(".....", ".....", "Y..H.", ".....", "....."));
            var book = Enemy(run, "spooky_spellbook");
            book.ActionCounter = 1;   // not a summoning turn
            book.Intent = Intent.None();

            EnemyAi.Declare(run, book, Catalog, new List<GameEvent>());

            Assert.That(book.Intent.Kind, Is.EqualTo(IntentKind.Fire));
            Assert.That(book.Intent.Dir, Is.EqualTo(Direction.Right));
        }

        // ------------------------------------------------------------------ Mimic Chest

        [Test]
        public void AMimicSleepsWhenSeenAndReadsAsTreasure()
        {
            var run = Revealed(Run(".....", ".....", "H..Q.", ".....", "....."));
            var mimic = Enemy(run, "mimic_chest");

            DoOk(run, PlayerCommand.Wait());

            Assert.That(mimic.Awake, Is.False, "Seen from across the room, it is just a chest.");
            Assert.That(Board.ClueAt(run.Floor, mimic.Pos), Is.EqualTo(Clue.Treasure));
        }

        [Test]
        public void WalkingUpToAMimicWakesItAndItsBiteComesTheTurnAfter()
        {
            var run = Revealed(Run(".....", ".....", "H..Q.", ".....", "....."));
            var mimic = Enemy(run, "mimic_chest");
            int hp = run.Hero.Hp;

            DoOk(run, PlayerCommand.Move(P(2, 2)));

            Assert.That(mimic.Awake, Is.True);
            Assert.That(run.Hero.Hp, Is.EqualTo(hp), "Waking is not biting: the bite is declared, a turn away.");
            Assert.That(mimic.Intent.Kind, Is.EqualTo(IntentKind.Attack));
            Assert.That(mimic.Intent.Target, Is.EqualTo(run.Hero.Pos));

            DoOk(run, PlayerCommand.Wait());
            Assert.That(run.Hero.Hp, Is.EqualTo(hp - Catalog.Enemy("mimic_chest").Damage));
        }

        /// <summary>
        /// A sleeping mimic answers a slash exactly as a real chest does. A refused command costs no turn, so if it were
        /// struck instead, every chest on the board could be tested for free and the disguise would be worth nothing.
        /// </summary>
        [Test]
        public void ASleepingMimicCannotBeProbedWithASlash()
        {
            var mimicRun = Revealed(Run(".....", ".....", "..HQ.", ".....", "....."));
            var chestRun = Revealed(Run(".....", ".....", "..HC.", ".....", "....."));

            var atMimic = Do(mimicRun, PlayerCommand.Slash(P(3, 2)));
            var atChest = Do(chestRun, PlayerCommand.Slash(P(3, 2)));

            Assert.That(atMimic.Accepted, Is.False);
            Assert.That(atMimic.RejectReason, Is.EqualTo(atChest.RejectReason), "Word for word what a real chest says.");
            Assert.That(Enemy(mimicRun, "mimic_chest").Awake, Is.False, "And it is still asleep.");
        }

        /// <summary>
        /// Every way a player can aim at a tile is answered the same for a sleeping mimic as for a real chest: accepted
        /// where the chest's is, refused in the same words where the chest's is. A refused command costs nothing, so any
        /// difference at all would be a free test for mimics. Accepted ones cost a turn either way - and the mimic wakes.
        /// </summary>
        [TestCase("H..Q.", CommandKind.Move, 3, TestName = "Tapping it from across the room")]
        [TestCase("..HQ.", CommandKind.Move, 3, TestName = "Tapping it from beside it")]
        [TestCase("..HQ.", CommandKind.Interact, 3, TestName = "Opening it")]
        [TestCase("..HQ.", CommandKind.Slash, 3, TestName = "Slashing it")]
        [TestCase(".H.Q.", CommandKind.Dash, 3, TestName = "Dashing onto it")]
        [TestCase("..HQ.", CommandKind.Dash, 4, TestName = "Dashing through it")]
        public void ASleepingMimicAnswersEveryCommandAsARealChestWould(string row, CommandKind kind, int x)
        {
            var mimicRun = Revealed(Run(".....", ".....", row, ".....", "....."));
            var chestRun = Revealed(Run(".....", ".....", row.Replace('Q', 'C'), ".....", "....."));
            var command = new PlayerCommand { Kind = kind, Target = P(x, 2) };

            var atMimic = Do(mimicRun, command);
            var atChest = Do(chestRun, command);

            Assert.That(atMimic.Accepted, Is.EqualTo(atChest.Accepted), $"{kind}: accepted for one and not the other.");
            Assert.That(atMimic.RejectReason, Is.EqualTo(atChest.RejectReason), $"{kind}: refused in different words.");
            if (atMimic.Accepted)
                Assert.That(Enemy(mimicRun, "mimic_chest").Awake, Is.True, "A turn spent on it wakes it.");
        }

        [Test]
        public void AMimicPaysCoinsWhenItFalls()
        {
            var run = Revealed(Run(".....", ".....", "..HQ.", ".....", "....."));
            var mimic = Enemy(run, "mimic_chest");
            DoOk(run, PlayerCommand.Wait());   // standing beside it wakes it
            Assert.That(mimic.Awake, Is.True);
            mimic.Hp = 1;
            int coins = run.CoinsFound;

            DoOk(run, PlayerCommand.Slash(mimic.Pos));

            Assert.That(run.Floor.Enemies.Contains(mimic), Is.False);
            Assert.That(run.CoinsFound, Is.EqualTo(coins + Catalog.Enemy("mimic_chest").LootCoins));
        }

        // ------------------------------------------------------------------ Goblin Key Warden

        [Test]
        public void TheWardenDropsTheKeyWhereItFalls()
        {
            var run = Revealed(Run(".....", ".....", ".HJ..", ".....", "....."));
            var warden = Enemy(run, "goblin_key_warden");
            var at = warden.Pos;
            warden.Hp = 1;

            var result = DoOk(run, PlayerCommand.Slash(at));

            Assert.That(Has(result, GameEventKind.KeyDropped), Is.True);
            Assert.That(run.Floor[at].Content, Is.EqualTo(ContentKind.Key));
            DoOk(run, PlayerCommand.Move(at));
            Assert.That(run.Hero.HasKey, Is.True);
        }

        [Test]
        public void TheWardenBacksAwayFromTheHero()
        {
            var run = Revealed(Run(".....", ".....", ".HJ..", ".....", "....."));
            var warden = Enemy(run, "goblin_key_warden");
            Assert.That(warden.Intent.Kind, Is.EqualTo(IntentKind.Move));
            int before = warden.Pos.Chebyshev(run.Hero.Pos);

            DoOk(run, PlayerCommand.Wait());

            Assert.That(warden.Pos.Chebyshev(run.Hero.Pos), Is.GreaterThan(before));
        }

        [Test]
        public void AGuardedFloorHasNoKeyLyingAroundOnlyTheWardensAndItStaysValid()
        {
            var catalog = ContentCatalog.CreateDefault();
            catalog.ProfileFor(2).KeyWarden = true;
            for (ulong seed = 1; seed <= 40; seed++)
            {
                var floor = FloorGenerator.Generate(seed, 2, catalog);
                foreach (var p in Board.AllCells)
                    Assert.That(floor[p].Content, Is.Not.EqualTo(ContentKind.Key), $"seed {seed}: the key lies at {p}.");
                var wardens = floor.Enemies.FindAll(e => e.DefId == FloorGenerator.KeyWardenId);
                Assert.That(wardens.Count, Is.EqualTo(1), $"seed {seed}");
                Assert.That(wardens[0].CarriesKey, Is.True);
                Assert.That(wardens[0].Awake, Is.False, "It sleeps until found, like any monster.");
            }
        }
    }
}
