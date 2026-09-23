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
    /// Audit 4's fixes that no existing test could see. Each was COMPILED before this file existed: the code was right
    /// by inspection and nothing would have gone red if it were put back the way it was.
    /// </summary>
    public class RepairTests
    {
        // ------------------------------------------------------------------ REL-41: dying is final

        [Test]
        public void AHeroKilledInTheSameStepThatFelledTheBossStillLosesTheRun()
        {
            // The act-clear heal used to run before the run decided whether the hero had died, so a bomb that killed
            // both restored the hero to full and the run carried on - into a win, on the last floor.
            var run = Revealed(Run(ContentCatalog.BossEvery, 5UL, ".....", ".....", ".HN..", ".....", "....X"));
            var king = Enemy(run, "goblin_brute_king");
            var events = new List<GameEvent>();
            run.Hero.Hp = 2;
            Combat.DamageHero(run, 99, "bomb", events, blockable: false);
            Combat.DamageEnemy(run, king, 99, "bomb", Catalog, events);

            Combat.ResolveDeaths(run, Catalog, events);

            Assert.That(run.Status, Is.EqualTo(RunStatus.Lost));
            Assert.That(run.Hero.Hp, Is.EqualTo(0), "Nothing heals a hero who is already gone.");
        }

        [Test]
        public void TheDroneNeverCarriesADeadHeroThroughABossFight()
        {
            // The Engineer's second path into the same hole: the drone fires before deaths resolve, so its kill on the
            // boss reached the act-clear heal on a turn the hero had already walked onto spikes. The board needs the
            // boss on it and the drone's zap has to be the killing blow, or this test proves nothing.
            var run = Revealed(As(Run(ContentCatalog.BossEvery, 5UL, ".....", ".....", ".HN..", ".....", "....X"), "gearspark"));
            var king = Enemy(run, "goblin_brute_king");
            king.Hp = 1;                                          // one zap from the drone finishes him
            king.Intent = Intent.Rest();
            run.Floor[P(2, 3)].Hazard = HazardKind.Spikes;        // and the hero's own step is lethal
            run.Hero.Hp = 1;

            DoOk(run, PlayerCommand.Move(P(2, 3)));

            Assert.That(king.Hp, Is.EqualTo(0), "Test setup: the drone did fell the boss on this turn.");
            Assert.That(run.Status, Is.EqualTo(RunStatus.Lost), "The spikes ended the run; clearing an act does not undo that.");
            Assert.That(run.Hero.Hp, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------ REL-43: Dodge is for blows

        [Test]
        public void SlipperyTurnsAsideABlowButNotSpikesOrAFall()
        {
            var spikes = Revealed(As(Run(".....", ".....", ".H^..", ".....", "....X"), "shadowcut"));
            spikes.Perks[TalentEffect.Dodge.ToString()] = 1;
            spikes.Floor[P(2, 2)].Hazard = HazardKind.Spikes;
            int before = spikes.Hero.Hp;
            DoOk(spikes, PlayerCommand.Move(P(2, 2)));
            Assert.That(spikes.Hero.Hp, Is.LessThan(before), "Spikes are unblockable; Dodge must not eat them.");
            Assert.That(spikes.Hero.DodgeSpent, Is.False, "And the charge is still there for a real blow.");

            var fall = Revealed(As(Run(".....", ".....", ".Ho..", ".....", "....X"), "shadowcut"));
            fall.Perks[TalentEffect.Dodge.ToString()] = 1;
            int hp = fall.Hero.Hp;
            DoOk(fall, PlayerCommand.Move(P(2, 2)));
            Assert.That(fall.Hero.Hp, Is.LessThan(hp), "A fall is unblockable too.");

            var blow = As(Run(".....", ".....", ".HG..", ".....", "....X"), "shadowcut");
            blow.Perks[TalentEffect.Dodge.ToString()] = 1;
            int hearts = blow.Hero.Hp;
            var result = DoOk(blow, PlayerCommand.Wait());
            Assert.That(Has(result, GameEventKind.HeroDodged), Is.True, "A monster's blow is exactly what Dodge is for.");
            Assert.That(blow.Hero.Hp, Is.EqualTo(hearts));
        }

        [Test]
        public void UnyieldingSoftensBlowsButNotSpikes()
        {
            // REL-46, found while reviewing REL-43: Unyielding read the damage the same way Dodge did and softened the
            // one category rules 4 says nothing softens.
            var run = Revealed(As(Run(".....", ".....", ".H^..", ".....", "....X"), "windsong"));
            run.Perks[TalentEffect.Unyielding.ToString()] = 1;
            run.Floor[P(2, 2)].Hazard = HazardKind.Spikes;
            run.Hero.Hp = run.Hero.MaxHp / 2;                     // wounded, so Unyielding is live
            int before = run.Hero.Hp;
            DoOk(run, PlayerCommand.Move(P(2, 2)));
            Assert.That(before - run.Hero.Hp, Is.EqualTo(Catalog.Hazards.SpikeDamage), "Spikes bite for their full amount.");

            var blow = As(Run(".....", ".....", ".HG..", ".....", "....X"), "windsong");
            blow.Perks[TalentEffect.Unyielding.ToString()] = 1;
            blow.Hero.Hp = blow.Hero.MaxHp / 2;
            int hearts = blow.Hero.Hp;
            DoOk(blow, PlayerCommand.Wait());
            Assert.That(hearts - blow.Hero.Hp, Is.EqualTo(Catalog.Enemy("goblin").Damage - 1), "A monster's blow is softened.");
        }

        // ------------------------------------------------------------------ DATA-21: one hoard a run

        [Test]
        public void AnActBossPaysAShareAndOnlyTheLastOnePaysTheHoard()
        {
            int act = Catalog.Treasure.GemsForAnActBoss, hoard = Catalog.Treasure.GemsForTheBoss;
            Assert.That(act, Is.LessThan(hoard), "An act boss is not worth the whole hoard.");

            var first = Revealed(Run(ContentCatalog.BossEvery, 5UL, ".....", ".....", ".HN..", ".....", "....X"));
            var king = Enemy(first, "goblin_brute_king");
            king.Hp = 1;
            int gems = first.GemsFound, xp = first.XpEarned;
            DoOk(first, PlayerCommand.Slash(king.Pos));
            Assert.That(first.GemsFound - gems, Is.EqualTo(act));
            Assert.That(first.XpEarned - xp, Is.EqualTo(Catalog.Xp.ForAnActBoss));

            var last = Revealed(Run(Catalog.RunFloorCount, 5UL, ".....", ".....", ".HB..", ".....", "....X"));
            var blobert = Enemy(last, "lord_blobert");
            blobert.Hp = 1;
            blobert.Mode = EnemyMode.Normal;
            int gems2 = last.GemsFound, xp2 = last.XpEarned;
            DoOk(last, PlayerCommand.Slash(blobert.Pos));
            Assert.That(last.GemsFound - gems2, Is.EqualTo(hoard), "Lord Blobert still pays what the rules promise.");
            Assert.That(last.XpEarned - xp2, Is.EqualTo(Catalog.Xp.ForTheBoss));

            // The gear roll is the last boss's too: four 50% rolls a run was the other half of DATA-21.
            for (ulong seed = 1; seed <= 12; seed++)
            {
                var fight = Revealed(Run(ContentCatalog.BossEvery, seed, ".....", ".....", ".HN..", ".....", "....X"));
                var boss = Enemy(fight, "goblin_brute_king");
                boss.Hp = 1;
                var killed = DoOk(fight, PlayerCommand.Slash(boss.Pos));
                var item = killed.Events.Find(e => e.Kind == GameEventKind.ItemFound);
                Assert.That(item, Is.Null,
                    $"seed {seed}: an act boss dropped gear ({item?.Source}) on floor {fight.Floor.FloorIndex} of {fight.FloorCount}.");
            }
        }

        // ------------------------------------------------------------------ REL-45: a dropped key stays reachable

        [Test]
        public void ADroppedKeyNeverLandsUnderAnActor()
        {
            var run = Revealed(Run(4, 5UL, ".....", ".....", ".HJG.", ".....", "....X"));
            var warden = Enemy(run, "goblin_key_warden");
            // Its own tile cannot hold the key, and every other tile is taken by something - so the only ground the
            // search can find is the hero's own tile and the goblin's. Both must be refused.
            run.Floor[warden.Pos].Content = ContentKind.Fountain;
            foreach (var p in Board.AllCells)
                if (run.Floor.EnemyAt(p) == null && p != run.Hero.Pos && run.Floor[p].Content == ContentKind.None)
                    run.Floor[p].Content = ContentKind.Fountain;
            var events = new List<GameEvent>();
            Combat.DamageEnemy(run, warden, 99, "slash", Catalog, events);
            Combat.ResolveDeaths(run, Catalog, events);

            GridPos key = GridPos.Invalid;
            foreach (var p in Board.AllCells)
                if (run.Floor[p].Content == ContentKind.Key) key = p;
            if (!key.InBounds)
            {
                Assert.That(run.Hero.HasKey, Is.True, "With nowhere to drop it, the hero simply takes it.");
                return;
            }
            Assert.That(run.Floor.EnemyAt(key), Is.Null, $"The key landed under a monster at {key}.");
            Assert.That(key, Is.Not.EqualTo(run.Hero.Pos), "The key landed under the hero, who cannot walk onto it.");
        }

        // ------------------------------------------------------------------ DATA-22: every floor a save carries

        [Test]
        public void AVaultSaveNamingAMonsterThisBuildLacksIsRefused()
        {
            var run = RunFactory.NewRun(9UL, Catalog, new List<GameEvent>());
            // A save taken inside a vault keeps the floor it came from in OuterFloor; the check used to skip it.
            run.OuterFloor = run.Floor;
            run.Floor = FloorState.CreateEmpty();
            run.Floor.FloorIndex = run.OuterFloor.FloorIndex;
            run.Floor.IsVault = true;
            run.Floor.Start = run.Hero.Pos;
            run.Floor.Exit = P(4, 4);
            run.ReturnPos = P(2, 2);                              // where the vault door put the hero
            run.OuterFloor.Enemies.Add(new EnemyState { Id = 99, DefId = "a_monster_that_was_renamed", Pos = P(0, 0), Hp = 1, MaxHp = 1 });

            var session = new GameSession(Catalog, new SaveJson(SaveSerializer.ToJson(run)));
            Assert.That(session.TryContinue(out var message), Is.False, "It must be refused, not resumed and then thrown out of.");
            Assert.That(message, Does.Contain("a_monster_that_was_renamed"));
        }

        // ------------------------------------------------------------------ TEST-19: the save carries the new state

        [Test]
        public void ARunCarriesItsClassRuleAndItsMonstersSecretsThroughASave()
        {
            // The round-trip test compares ToJson(FromJson(json)) with json, so a field that stopped serializing is
            // absent on both sides and it still passes. These are read back one by one instead. A lost CarriesKey
            // leaves a floor with no key at all; a lost Perks entry takes a hero's class rule with it.
            var run = RunFactory.NewRun(11UL, Catalog, new List<GameEvent>(), "shadowcut");
            run.Hero.WebbedTurns = 1;
            run.Hero.DodgeSpent = true;
            run.Threat = 2;
            var warden = EnemyAi.Spawn(run.Floor, Catalog.Enemy("goblin_key_warden"), FreeTile(run), awake: true);
            warden.CarriesKey = true;
            var mimic = EnemyAi.Spawn(run.Floor, Catalog.Enemy("mimic_chest"), FreeTile(run), awake: false);
            mimic.Disguised = true;
            var boss = EnemyAi.Spawn(run.Floor, Catalog.Enemy("goblin_brute_king"), FreeTile(run), awake: true);
            boss.Mode = EnemyMode.Enraged;
            boss.Enraging = true;

            var back = SaveSerializer.FromJson(SaveSerializer.ToJson(run));

            Assert.That(back.Perk(TalentEffect.Ambush), Is.EqualTo(run.Perk(TalentEffect.Ambush)), "The Rogue's class rule.");
            Assert.That(back.Perk(TalentEffect.Ambush), Is.GreaterThan(0));
            Assert.That(back.Threat, Is.EqualTo(2));
            Assert.That(back.Hero.WebbedTurns, Is.EqualTo(1));
            Assert.That(back.Hero.DodgeSpent, Is.True);
            Assert.That(back.Floor.Enemies.Find(e => e.DefId == "goblin_key_warden").CarriesKey, Is.True,
                "Without this the resumed floor has no key anywhere and the exit can never open.");
            Assert.That(back.Floor.Enemies.Find(e => e.DefId == "mimic_chest").Disguised, Is.True);
            var king = back.Floor.Enemies.Find(e => e.DefId == "goblin_brute_king");
            Assert.That(king.Mode, Is.EqualTo(EnemyMode.Enraged));
            Assert.That(king.Enraging, Is.True);
        }

        static GridPos FreeTile(RunState run)
        {
            foreach (var p in Board.AllCells)
                if (run.Floor.EnemyAt(p) == null && p != run.Hero.Pos && run.Floor[p].Terrain == Terrain.Floor
                    && run.Floor[p].Content == ContentKind.None && !run.Floor[p].IsExit) return p;
            return GridPos.Invalid;
        }

        sealed class SaveJson : ISaveStore
        {
            readonly string _json;
            public SaveJson(string json) => _json = json;
            public bool Exists => true;
            public bool TryLoad(out RunState run, out string message)
            {
                message = null;
                run = SaveSerializer.FromJson(_json);
                return true;
            }
            public void Save(RunState run) { }
            public void Delete() { }
        }
    }
}
