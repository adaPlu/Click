using ClickDungeon.Simulation;
using NUnit.Framework;
using static ClickDungeon.Tests.Scenario;

namespace ClickDungeon.Tests
{
    public class ThreatTests
    {
        [Test]
        public void TelegraphMatchesWhatActuallyHappens()
        {
            var run = Run(
                ".....",
                ".....",
                ".HG..",
                ".....",
                ".....");
            var threats = Threats.Compute(run, Catalog);
            Assert.That(Threats.DamageAt(threats, run.Hero.Pos), Is.EqualTo(2));

            DoOk(run, PlayerCommand.Wait());
            Assert.That(run.Hero.Hp, Is.EqualTo(8));
        }

        [Test]
        public void FireLanePreviewExtendsPastTheHero()
        {
            var run = Run(
                ".....",
                ".....",
                ".H.I.",
                ".....",
                ".....");
            var threats = Threats.Compute(run, Catalog);
            Assert.That(Threats.DamageAt(threats, P(0, 2)), Is.EqualTo(2), "Retreating along the lane is still inside it.");

            DoOk(run, PlayerCommand.Move(P(0, 2)));
            Assert.That(run.Hero.Hp, Is.EqualTo(8));
        }

        [Test]
        public void ImminentBombShowsBlastArea()
        {
            var run = Run(
                ".....",
                ".....",
                ".Hb..",
                ".....",
                ".....");
            DoOk(run, PlayerCommand.Move(P(2, 2)));
            var threats = Threats.Compute(run, Catalog);
            Assert.That(Threats.DamageAt(threats, P(3, 3)), Is.EqualTo(4));
            Assert.That(Threats.DamageAt(threats, P(4, 2)), Is.EqualTo(0));
        }
    }
}
