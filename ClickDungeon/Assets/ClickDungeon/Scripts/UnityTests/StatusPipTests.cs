using System.Linq;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using ClickDungeon.Unity.Screens;
using ClickDungeon.Unity.Ui;
using NUnit.Framework;

namespace ClickDungeon.UnityTests
{
    /// <summary>
    /// D-086: the standing states drawn under the hero. A pip is for a state the player must keep in mind while
    /// deciding - not for a thing that happened, which is what a popup is for. Both of the hero's were told once in
    /// the log and then invisible: a resurrection in place (the bot could read hero.Ward and the player could not,
    /// REL-92) and a web, which forbids Move and Dash for as long as it holds (Unity EditMode only).
    /// </summary>
    public class StatusPipTests
    {
        static readonly ContentCatalog Catalog = ContentCatalog.CreateDefault();

        static RunState Hero()
        {
            var run = RunFactory.NewRun(9UL, Catalog, new System.Collections.Generic.List<GameEvent>(),
                ContentCatalog.DefaultHeroId, MovementMode.Free);
            run.Hero.Ward = 0;
            run.Hero.WebbedTurns = 0;
            return run;
        }

        [Test]
        public void AHeroInNoParticularStateWearsNoPips()
        {
            Assert.That(BoardView.HeroStatuses(Hero()), Is.Empty);
            Assert.That(BoardView.HeroStatuses(null), Is.Empty, "And a board with no run does not throw.");
        }

        [Test]
        public void AResurrectionInPlaceIsShownWithTheHeartsItGivesBack()
        {
            var run = Hero();
            run.Hero.Ward = 7;
            var pips = BoardView.HeroStatuses(run);
            Assert.That(pips.Count, Is.EqualTo(1));
            Assert.That(pips[0].IconKey, Is.EqualTo(ArtKeys.StatusWard));
            Assert.That(pips[0].Text, Is.EqualTo("7"), "The number matters: a 4 and a 7 are different promises.");
        }

        /// <summary>
        /// The pip has to come and go with the state itself, or it is decoration. Spending the ward must clear it.
        /// </summary>
        [Test]
        public void SpendingTheResurrectionTakesItsPipAway()
        {
            var run = Hero();
            run.Hero.MaxHp = 20;
            run.Hero.Hp = 5;
            Usables.Place(run.Hero, 7, "test", null);
            Assert.That(BoardView.HeroStatuses(run).Count, Is.EqualTo(1), "Test setup: it is in place.");

            Combat.DamageHero(run, 99, "test", new System.Collections.Generic.List<GameEvent>(), blockable: false);
            Assert.That(run.Hero.Hp, Is.EqualTo(7), "Test setup: the ward fired.");
            Assert.That(BoardView.HeroStatuses(run), Is.Empty, "And the pip goes with it.");
        }

        [Test]
        public void AWebIsShownAndCountsDownOnlyWhileItHoldsMoreThanOneTurn()
        {
            var run = Hero();
            run.Hero.WebbedTurns = 2;
            var pips = BoardView.HeroStatuses(run);
            Assert.That(pips.Single().IconKey, Is.EqualTo(ArtKeys.StatusWebbed));
            Assert.That(pips.Single().Text, Is.EqualTo("2"));

            run.Hero.WebbedTurns = 1;
            Assert.That(BoardView.HeroStatuses(run).Single().Text, Is.Null,
                "One turn left needs no number - the picture already says you are held.");

            run.Hero.WebbedTurns = 0;
            Assert.That(BoardView.HeroStatuses(run), Is.Empty);
        }

        [Test]
        public void BothAtOnceShowInAStableOrder()
        {
            var run = Hero();
            run.Hero.Ward = 4;
            run.Hero.WebbedTurns = 2;
            var pips = BoardView.HeroStatuses(run);
            Assert.That(pips.Count, Is.EqualTo(2));
            Assert.That(pips[0].IconKey, Is.EqualTo(ArtKeys.StatusWard), "The ward first, every time.");
            Assert.That(pips[1].IconKey, Is.EqualTo(ArtKeys.StatusWebbed));
        }

        /// <summary>Every pip must name art the catalogue actually holds, or it draws an empty box.</summary>
        [Test]
        public void EveryPipNamesArtTheGameHas()
        {
            var run = Hero();
            run.Hero.Ward = 4;
            run.Hero.WebbedTurns = 1;
            foreach (var pip in BoardView.HeroStatuses(run))
                Assert.That(Art.TryGetSprite(pip.IconKey, out _), Is.True, pip.IconKey);
        }
    }
}
