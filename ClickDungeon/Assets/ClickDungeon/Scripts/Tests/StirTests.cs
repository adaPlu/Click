using System.Collections.Generic;
using System.Linq;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using NUnit.Framework;
using static ClickDungeon.Tests.Scenario;

namespace ClickDungeon.Tests
{
    /// <summary>
    /// D-078: the cost of waiting. A finished build cannot be threatened by bigger numbers - on a five-by-five board
    /// the hero can always step away and everything refills on the stairs - so time was free and a veteran simply
    /// out-waited the dungeon. The deep floors are impatient now, and the patience a hero gets is the tier's budget
    /// less what their renown has spent of it.
    /// </summary>
    public class StirTests
    {
        static readonly ContentCatalog Medium = ContentCatalog.CreateDefault(Difficulty.Medium);

        static RunState OnFloor(int floorIndex, int threat = 0)
        {
            var run = Revealed(Run(floorIndex, 9UL, ".....", ".....", "H....", ".....", "....X"));
            run.Threat = threat;
            run.Hero.MaxHp = run.Hero.Hp = 200;   // enough to stand there and watch it press
            return run;
        }

        static int Linger(RunState run, int turns)
        {
            int taken = 0;
            for (int i = 0; i < turns; i++)
            {
                int before = run.Hero.Hp;
                TurnResolver.Apply(run, PlayerCommand.Wait(), Medium);
                taken += before - run.Hero.Hp;
            }
            return taken;
        }

        [Test]
        public void TheShallowFloorsArePatientForever()
        {
            // The targeting rests on this: a player still learning the game is up here, and nothing below reaches them.
            var run = OnFloor(Medium.StirFirstFloor - 1);
            Assert.That(Stir.Watches(run, Medium), Is.False);
            Assert.That(Linger(run, 120), Is.Zero, "The upper floors never lose patience.");
            Assert.That(run.Floor.TurnsHere, Is.Zero, "And do not even keep the clock.");
        }

        [Test]
        public void ANewcomerKeepsTheWholeBudgetAndAReturningHeroDoesNot()
        {
            // Renown is the game's own measure of how far a player has come, and it is exactly zero on a first run.
            // Depth alone could not aim this: a fresh CASUAL player reaches floors 11-20 on four floors in five, so a
            // flat budget cost them nine points where it cost a veteran eight - a difficulty increase in disguise.
            var newcomer = OnFloor(Medium.StirFirstFloor, threat: 0);
            var veteran = OnFloor(Medium.StirFirstFloor, threat: Medium.Renown.MaxThreat);

            Assert.That(Stir.Patience(newcomer, Medium), Is.EqualTo(Medium.StirAfterTurns));
            Assert.That(Stir.Patience(veteran, Medium),
                Is.EqualTo(Medium.StirAfterTurns - Medium.Renown.MaxThreat * Medium.StirPatienceLostPerThreat));
            Assert.That(Stir.Patience(veteran, Medium), Is.LessThan(Stir.Patience(newcomer, Medium)));

            // And it never runs out entirely, however decorated the hero: there is always a floor's worth of room.
            Assert.That(Stir.Patience(OnFloor(Medium.StirFirstFloor, threat: 99), Medium),
                Is.GreaterThanOrEqualTo(Medium.StirEscalationTurns));
        }

        [Test]
        public void ItIsAnnouncedBeforeItArrivesAndThenPressesEveryTurn()
        {
            var run = OnFloor(Medium.StirFirstFloor);
            int patience = Stir.Patience(run, Medium);

            var quiet = new List<GameEvent>();
            for (int i = 0; i < patience - Stir.Warning - 1; i++)
                quiet.AddRange(TurnResolver.Apply(run, PlayerCommand.Wait(), Medium).Events);
            Assert.That(quiet.Any(e => e.Kind == GameEventKind.DungeonStirring), Is.False, "Warned too early.");
            Assert.That(run.Hero.Hp, Is.EqualTo(run.Hero.MaxHp), "And nothing taken yet.");

            for (int left = Stir.Warning; left > 0; left--)
            {
                var result = TurnResolver.Apply(run, PlayerCommand.Wait(), Medium);
                var warned = result.Events.Where(e => e.Kind == GameEventKind.DungeonStirring).ToList();
                Assert.That(warned, Is.Not.Empty, $"No warning with {left} turns left.");
                Assert.That(warned[0].Amount, Is.EqualTo(left), "The warning says how long is left.");
                Assert.That(run.Hero.Hp, Is.EqualTo(run.Hero.MaxHp), "A warning is a warning, not a blow.");
            }

            for (int i = 0; i < 3; i++)
            {
                int before = run.Hero.Hp;
                var result = TurnResolver.Apply(run, PlayerCommand.Wait(), Medium);
                Assert.That(result.Events.Any(e => e.Kind == GameEventKind.DungeonPressed), Is.True, "Silent damage.");
                Assert.That(run.Hero.Hp, Is.LessThan(before), "It presses every turn, not once.");
            }
        }

        [Test]
        public void LingeringCostsMoreTheLongerItLasts()
        {
            // Flat pressure is a tax a veteran out-heals. It has to grow, or waiting is still free.
            var run = OnFloor(Medium.StirFirstFloor);
            Linger(run, Stir.Patience(run, Medium));
            int first = Linger(run, 1);
            Linger(run, Medium.StirEscalationTurns);
            int later = Linger(run, 1);

            Assert.That(first, Is.EqualTo(1), "It starts gently.");
            Assert.That(later, Is.GreaterThan(first), $"After {Medium.StirEscalationTurns} more turns it presses harder.");
        }

        [Test]
        public void AShieldDoesNotStopTheDark()
        {
            var run = OnFloor(Medium.StirFirstFloor);
            Linger(run, Stir.Patience(run, Medium) + 1);

            int before = run.Hero.Hp;
            TurnResolver.Apply(run, PlayerCommand.Shield(), Medium);
            Assert.That(run.Hero.Hp, Is.LessThan(before),
                "Guard stops a monster, not the dungeon - blockable would make waiting free again.");
        }

        [Test]
        public void AVaultIsNeverPressed()
        {
            // A vault is a side room off a floor. Its own clock would punish looking in the treasure room, which is the
            // opposite of the point; the floor outside keeps its own count while the hero is in there.
            var vault = Revealed(Run(Medium.StirFirstFloor, 9UL, ".....", ".....", "H....", ".....", "....X"));
            vault.Floor.IsVault = true;
            vault.Hero.MaxHp = vault.Hero.Hp = 200;

            Assert.That(Stir.Watches(vault, Medium), Is.False);
            Assert.That(Linger(vault, 100), Is.Zero, "A vault is never pressed.");
            Assert.That(vault.Floor.TurnsHere, Is.Zero);
        }

        [Test]
        public void EveryTierSaysHowLongItWillWait()
        {
            var tiers = new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hardcore }
                .Select(id => ContentCatalog.CreateDefault(id)).ToArray();
            Assert.That(tiers.Select(c => c.StirAfterTurns), Is.EqualTo(new[] { 75, 60, 60 }),
                "Squire's Stroll waits longest; the other two share a clock.");
            foreach (var catalog in tiers)
            {
                Assert.That(catalog.StirPatienceLostPerThreat, Is.GreaterThan(0), $"{catalog.Difficulty}: nothing aims it.");
                Assert.That(catalog.StirFirstFloor, Is.GreaterThan(1), $"{catalog.Difficulty}: it would reach the first floor.");
            }
        }
    }
}
