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
    /// <summary>
    /// D-082: things the hero carries and spends on a turn, and the Phoenix Feather that is the first of them.
    ///
    /// The rule the whole feature exists to hold: a resurrection is either IN PLACE or it is not, and nothing may put
    /// a second beside it. It lives on HeroState.Ward - a number the hero carries - so every road to one obeys it
    /// without knowing about the others.
    /// </summary>
    public class UsableTests
    {
        static RunState Hero(out UsableDefinition feather, int maxHp = 20, int hp = 20)
        {
            var run = As(Revealed(Run(".....", ".....", "H....", ".....", ".....")), "dawnward");
            run.Hero.MaxHp = maxHp;
            run.Hero.Hp = hp;
            feather = Catalog.Usable("phoenix_feather");
            return run;
        }

        [Test]
        public void AFeatherPlacesAResurrectionThatTakesTheKillingBlow()
        {
            var run = Hero(out var feather, maxHp: 20, hp: 6);
            Usables.Give(run.Hero, feather);
            Assert.That(run.Hero.Ward, Is.Zero, "Test setup: nothing in place yet.");

            DoOk(run, PlayerCommand.Use(0));
            Assert.That(run.Hero.Ward, Is.EqualTo(feather.Amount), "Using it places the resurrection.");
            Assert.That(run.Hero.Usables, Is.Empty, "And spends the feather.");

            // A blow far past the hero's remaining hearts.
            var events = new List<GameEvent>();
            Combat.DamageHero(run, 99, "test", events, blockable: false);
            Assert.That(run.Status, Is.Not.EqualTo(RunStatus.Lost), "The resurrection takes it.");
            Assert.That(run.Hero.Hp, Is.EqualTo(feather.Amount), "And gives back exactly what it promised.");
            Assert.That(run.Hero.Ward, Is.Zero, "It is spent, so the next blow lands.");
        }

        [Test]
        public void ASecondFeatherIsWastedWhileOneIsInPlace()
        {
            var run = Hero(out var feather);
            Usables.Give(run.Hero, feather, count: 2);
            Assert.That(run.Hero.Usables.Single().Charges, Is.EqualTo(2), "Test setup: two of them, stacked in one pocket.");

            DoOk(run, PlayerCommand.Use(0));
            Assert.That(run.Hero.Ward, Is.EqualTo(feather.Amount));

            var result = DoOk(run, PlayerCommand.Use(0));
            Assert.That(run.Hero.Usables, Is.Empty, "The second feather is GONE - that is what wasting it means.");
            Assert.That(run.Hero.Ward, Is.EqualTo(feather.Amount), "And it bought nothing: still one resurrection, not two.");
            Assert.That(result.Events.Any(e => e.Kind == GameEventKind.WardWasted), Is.True,
                "And the game says so, or the player is left wondering where their feather went.");
        }

        [Test]
        public void OneResurrectionSurvivesOneKillingBlowAndNoMore()
        {
            var run = Hero(out var feather, maxHp: 20, hp: 8);
            Usables.Give(run.Hero, feather, count: 2);
            DoOk(run, PlayerCommand.Use(0));
            DoOk(run, PlayerCommand.Use(0));  // wasted

            var events = new List<GameEvent>();
            Combat.DamageHero(run, 99, "test", events, blockable: false);
            Assert.That(run.Hero.Hp, Is.EqualTo(feather.Amount), "The first killing blow is taken.");
            Combat.DamageHero(run, 99, "test", events, blockable: false);
            Assert.That(run.Hero.Hp, Is.Zero, "The second is not: two feathers bought one life, not two.");
        }

        /// <summary>
        /// The Cleric's capstone is the same feather in its spell form (D-082), so it must obey the same rule rather
        /// than be a second mechanism that happens to look alike.
        /// </summary>
        [Test]
        public void TheClericsCapstonePlacesTheSameResurrectionEveryFloor()
        {
            var talent = Catalog.Talent("c_miracle");
            Assert.That(talent.Name, Is.EqualTo("Phoenix Feather"));
            Assert.That(talent.Effect, Is.EqualTo(TalentEffect.DivineShield));

            var catalog = ContentCatalog.CreateDefault(Difficulty.Medium);
            string cleric = catalog.HeroIdentities.Values.First(h => h.ClassId == "cleric").Id;
            // By the branch, because a class may learn ONE capstone (MAINT-90): spending in tier order takes whichever
            // the catalogue lists first and locks this one out, which is how this test first failed.
            var profile = BalanceTests.BuiltUp(catalog, "cleric", capstoneBranch: "mercy");
            Assert.That(Progression.Rank(profile, "c_miracle"), Is.GreaterThan(0), "Test setup: the capstone is learned.");

            var events = new List<GameEvent>();
            var run = RunFactory.NewRun(11UL, catalog, events, cleric, MovementMode.Free);
            ProfileSystem.ProvisionRun(profile, run, catalog, events);
            Assert.That(run.Hero.Ward, Is.GreaterThan(0), "A resurrection is in place from the first floor.");

            // Spend it, then walk down: the next floor places another.
            int placed = run.Hero.Ward;
            Combat.DamageHero(run, 99, "test", events, blockable: false);
            Assert.That(run.Hero.Ward, Is.Zero, "Spent.");
            RunFactory.BeginFloor(run, run.Floor.FloorIndex + 1, catalog, events);
            Assert.That(run.Hero.Ward, Is.EqualTo(placed), "And the next floor places it again.");
        }

        /// <summary>
        /// The talent's ward is ONE HEART PLUS what the talent grants, pinned against the catalogue (TEST-107).
        /// Nothing pinned this before: the one test named for DivineShield computed the same sum itself, so
        /// production could have placed any number - or nothing at all - and the suite stayed green.
        /// </summary>
        [Test]
        public void TheCapstonePlacesOneHeartPlusWhatTheTalentGrants()
        {
            var catalog = ContentCatalog.CreateDefault(Difficulty.Medium);
            var talent = catalog.Talent("c_miracle");
            Assert.That(talent.Effect, Is.EqualTo(TalentEffect.DivineShield), "Test setup: the capstone is the ward.");

            string cleric = catalog.HeroIdentities.Values.First(h => h.ClassId == "cleric").Id;
            var events = new List<GameEvent>();
            var run = RunFactory.NewRun(11UL, catalog, events, cleric, MovementMode.Free);
            ProfileSystem.ProvisionRun(BalanceTests.BuiltUp(catalog, "cleric", capstoneBranch: "mercy"), run, catalog, events);

            Assert.That(run.Hero.Ward, Is.EqualTo(1 + talent.Amount),
                "The ward is the talent's amount plus the heart the hero is left standing on.");
        }

        /// <summary>
        /// And the feather OUTRANKS it, which is the whole reason a Cleric can use one (REL-93). Before this the
        /// capstone held the only slot on every floor and a bought feather was destroyed for nothing.
        /// </summary>
        [Test]
        public void AFeatherUpgradesTheWardACapstoneAlreadyPlaced()
        {
            var catalog = ContentCatalog.CreateDefault(Difficulty.Medium);
            var feather = catalog.Usable("phoenix_feather");
            var talentWard = 1 + catalog.Talent("c_miracle").Amount;
            Assert.That(feather.Amount, Is.GreaterThan(talentWard),
                "A feather that did not outrank the talent would be unusable by four of the eight classes.");

            string cleric = catalog.HeroIdentities.Values.First(h => h.ClassId == "cleric").Id;
            var events = new List<GameEvent>();
            var run = RunFactory.NewRun(11UL, catalog, events, cleric, MovementMode.Free);
            ProfileSystem.ProvisionRun(BalanceTests.BuiltUp(catalog, "cleric", capstoneBranch: "mercy"), run, catalog, events);
            Assert.That(run.Hero.Ward, Is.EqualTo(talentWard), "Test setup: the capstone's ward is in place.");

            Usables.Give(run.Hero, feather);
            Usables.Use(run, catalog, 0, events);
            Assert.That(run.Hero.Ward, Is.EqualTo(feather.Amount), "The feather replaces the weaker ward.");
            Assert.That(events.Any(e => e.Kind == GameEventKind.WardWasted), Is.False, "And is not wasted doing it.");
        }

        /// <summary>A hero without the talent must get nothing: 1 + 0 would arm the whole roster.</summary>
        [Test]
        public void AHeroWithoutTheTalentCarriesNoResurrection()
        {
            var catalog = ContentCatalog.CreateDefault(Difficulty.Medium);
            var events = new List<GameEvent>();
            var run = RunFactory.NewRun(11UL, catalog, events, ContentCatalog.DefaultHeroId, MovementMode.Free);
            Assert.That(run.Perk(TalentEffect.DivineShield), Is.Zero, "Test setup: no talent.");
            Assert.That(run.Hero.Ward, Is.Zero, "So no resurrection, not a one-heart one.");

            run.Hero.Hp = 1;
            Combat.DamageHero(run, 99, "test", events, blockable: false);
            Assert.That(run.Hero.Hp, Is.Zero, "And the blow ends the run.");
        }

        /// <summary>
        /// The feather has to be gettable, or the feature is a mechanism nobody can reach (D-082). It is also the
        /// only provision that is CARRIED rather than spent at the door, so this pins that difference.
        /// </summary>
        [Test]
        public void AFeatherBoughtInTheShopIsCarriedIntoTheRun()
        {
            var catalog = ContentCatalog.CreateDefault(Difficulty.Medium);
            var profile = new ProfileState
            {
                Coins = Shop.CoinCost(ShopItem.PhoenixFeather) * 2,
                Gems = Shop.GemCost(ShopItem.PhoenixFeather) * 2,
            };

            Assert.That(Shop.Stock, Does.Contain(ShopItem.PhoenixFeather), "It is on the shelf.");
            Assert.That(Shop.TryBuy(profile, ShopItem.PhoenixFeather), Is.True);
            Assert.That(Shop.TryBuy(profile, ShopItem.PhoenixFeather), Is.True);
            Assert.That(Shop.Waiting(profile, ShopItem.PhoenixFeather), Is.EqualTo(2), "Two are waiting.");
            Assert.That(Shop.TryBuy(profile, ShopItem.PhoenixFeather), Is.False, "And the purse is empty.");

            var events = new List<GameEvent>();
            var run = RunFactory.NewRun(5UL, catalog, events, ContentCatalog.DefaultHeroId, MovementMode.Free);
            ProfileSystem.ProvisionRun(profile, run, catalog, events);

            Assert.That(profile.PhoenixFeathers, Is.Zero, "Spent off the profile, like every other provision.");
            var carried = run.Hero.Usables.Single(u => u.Id == "phoenix_feather");
            Assert.That(carried.Charges, Is.EqualTo(2), "But into the PACK, not into a starting number.");
            Assert.That(run.Hero.Ward, Is.Zero, "And not used: the player picks the moment.");
        }

        /// <summary>
        /// D-083: the last slot of the action bar. It was POTION and nothing else; it holds whatever the hero picks
        /// now. The potion stays first and stays present even at zero, because a bar that loses its fifth button
        /// changes shape under the player mid-run.
        /// </summary>
        [Test]
        public void TheLastSlotSwapsBetweenThePotionAndThePack()
        {
            var run = Hero(out var feather);
            run.Hero.Potions = 0;

            var empty = Usables.Pockets(run, Catalog);
            Assert.That(empty.Count, Is.EqualTo(1), "With an empty pack there is only the potion to hold.");
            Assert.That(empty[0].IsPotion, Is.True);
            Assert.That(empty[0].Label, Is.EqualTo("POTION"));
            Assert.That(empty[0].Command.Kind, Is.EqualTo(CommandKind.Potion));

            Usables.Give(run.Hero, feather, count: 2);
            var full = Usables.Pockets(run, Catalog);
            Assert.That(full.Count, Is.EqualTo(2), "The feather joins the potion.");
            Assert.That(full[0].IsPotion, Is.True, "And the potion stays first, so the default never moves.");

            var pocket = full[1];
            Assert.That(pocket.Label, Is.EqualTo("PHOENIX FEATHER"));
            Assert.That(pocket.Count, Is.EqualTo(2), "The badge counts charges, not entries.");
            Assert.That(pocket.Command.Kind, Is.EqualTo(CommandKind.Use));
            Assert.That(pocket.Command.Slot, Is.EqualTo(0), "And it points at the pack, not at the bar.");
        }

        /// <summary>
        /// The pack shrinks when the last charge goes, so a slot that was valid a moment ago is not. The bar must not
        /// be left pointing past the end of it.
        /// </summary>
        [Test]
        public void SpendingTheLastChargeTakesItOutOfTheRotation()
        {
            var run = Hero(out var feather);
            Usables.Give(run.Hero, feather);
            Assert.That(Usables.Pockets(run, Catalog).Count, Is.EqualTo(2));

            DoOk(run, PlayerCommand.Use(0));
            var after = Usables.Pockets(run, Catalog);
            Assert.That(after.Count, Is.EqualTo(1), "Spent, so there is nothing to swap to any more.");
            Assert.That(after[0].IsPotion, Is.True, "And the potion is what is left.");
        }

        /// <summary>
        /// REL-92: the rules allow a pointless spend, but exactly one function knows it is pointless, so the bot's
        /// refusal and the button's warning cannot drift apart. Before this the button was lit at full brightness
        /// whether pressing it would save the hero or destroy a 600-coin purchase for nothing.
        /// </summary>
        [Test]
        public void OnePlaceKnowsWhenASpendWouldBuyNothing()
        {
            var run = Hero(out var feather);
            Assert.That(Usables.WouldWaste(run, feather), Is.False, "Nothing in place: the spend is worth making.");

            run.Hero.Ward = feather.Amount - 1;
            Assert.That(Usables.WouldWaste(run, feather), Is.False, "A weaker ward is worth upgrading.");

            run.Hero.Ward = feather.Amount;
            Assert.That(Usables.WouldWaste(run, feather), Is.True, "An equal one is not.");
            run.Hero.Ward = feather.Amount + 1;
            Assert.That(Usables.WouldWaste(run, feather), Is.True, "Nor a stronger one.");

            // And it is still legal - the warning is the UI's job, not a new rule.
            Usables.Give(run.Hero, feather);
            Assert.That(Commands.Validate(run, PlayerCommand.Use(0), Catalog, out _), Is.True);
        }

        /// <summary>
        /// D-084: the dungeon sells a feather cheaper than the title screen, and once a run. The cap is what stops the
        /// cheap source making the dear one pointless - with DATA-53 refunding an unspent feather, a player could
        /// otherwise buy low every run, never use one, and carry them all out.
        /// </summary>
        [Test]
        public void TheDungeonSellsOneFeatherARunAndTheTitleScreenChargesDouble()
        {
            Assert.That(Shop.CoinCost(ShopItem.PhoenixFeather, ShopPlace.Title),
                Is.EqualTo(Shop.CoinCost(ShopItem.PhoenixFeather, ShopPlace.Dungeon) * 2), "Double the coin above.");
            Assert.That(Shop.GemCost(ShopItem.PhoenixFeather, ShopPlace.Title),
                Is.EqualTo(Shop.GemCost(ShopItem.PhoenixFeather, ShopPlace.Dungeon) * 2), "And double the gems.");
            Assert.That(Shop.GemCost(ShopItem.PhoenixFeather, ShopPlace.Dungeon), Is.GreaterThan(0),
                "Below, it costs gems as well as coin.");

            var catalog = ContentCatalog.CreateDefault(Difficulty.Medium);
            var run = RunFactory.NewRun(5UL, catalog, new List<GameEvent>(), ContentCatalog.DefaultHeroId, MovementMode.Free);
            var profile = new ProfileState
            {
                Coins = Shop.CoinCost(ShopItem.PhoenixFeather, ShopPlace.Dungeon) * 4,
                Gems = Shop.GemCost(ShopItem.PhoenixFeather, ShopPlace.Dungeon) * 4,
            };

            Assert.That(Shop.StockFor(ShopPlace.Dungeon, run), Does.Contain(ShopItem.PhoenixFeather), "On the shelf.");
            Assert.That(Shop.TryBuy(profile, ShopItem.PhoenixFeather, catalog, ShopPlace.Dungeon, run, out _), Is.True);
            Assert.That(profile.PhoenixFeathers, Is.EqualTo(1));

            Assert.That(Shop.StockFor(ShopPlace.Dungeon, run), NUnit.Framework.Has.No.Member(ShopItem.PhoenixFeather),
                "Withdrawn: this run has had its one.");
            Assert.That(Shop.TryBuy(profile, ShopItem.PhoenixFeather, catalog, ShopPlace.Dungeon, run, out _), Is.False,
                "And the cap is a rule, not just a shelf that stopped showing it.");
            Assert.That(profile.PhoenixFeathers, Is.EqualTo(1), "Nothing was granted.");

            // The title screen will still sell one, at double, however many the dungeon sold.
            Assert.That(Shop.StockFor(ShopPlace.Title, run), Does.Contain(ShopItem.PhoenixFeather));
            Assert.That(Shop.TryBuy(profile, ShopItem.PhoenixFeather, catalog, ShopPlace.Title, run, out _), Is.True);
            Assert.That(profile.PhoenixFeathers, Is.EqualTo(2));
        }

        [Test]
        public void AnEmptyPocketCannotBeSpent()
        {
            var run = Hero(out _);
            Assert.That(Commands.Validate(run, PlayerCommand.Use(0), Catalog, out string reason), Is.False);
            Assert.That(reason, Is.Not.Null.And.Not.Empty, "And it says why.");
            Assert.That(Commands.Validate(run, PlayerCommand.Use(-1), Catalog, out _), Is.False);
            Assert.That(Commands.Validate(run, PlayerCommand.Use(99), Catalog, out _), Is.False);
        }

        /// <summary>
        /// The bot declines a wasteful ward - playing well, not a rule of the game - which is D-081's split kept.
        /// </summary>
        [Test]
        public void TheBotWillNotWasteAFeatherItCannotUse()
        {
            var run = Hero(out var feather);
            Usables.Give(run.Hero, feather, count: 2);

            bool Offered() => AutoPlayer.LegalCommands(run, Catalog).Any(c => c.Kind == CommandKind.Use);
            Assert.That(Offered(), Is.True, "With nothing in place, spending one is worth considering.");

            run.Hero.Ward = feather.Amount;
            Assert.That(Offered(), Is.False, "With one in place it would buy nothing, so the bot does not offer it.");
            Assert.That(Commands.Validate(run, PlayerCommand.Use(0), Catalog, out _), Is.True,
                "The game still allows it - a player may waste their own feather.");
        }
    }
}
