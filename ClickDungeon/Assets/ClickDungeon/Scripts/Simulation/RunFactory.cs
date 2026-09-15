using System.Collections.Generic;
using ClickDungeon.Content;
using ClickDungeon.Domain;

namespace ClickDungeon.Simulation
{
    public static class RunFactory
    {
        public static HeroState CreateHero(ContentCatalog catalog, string identityId)
        {
            var identity = catalog.HeroIdentity(identityId);
            var heroClass = catalog.HeroClass(identity.ClassId);
            return new HeroState
            {
                IdentityId = identity.Id,
                ClassId = heroClass.Id,
                Pos = GridPos.Invalid,
                Hp = heroClass.MaxHp,
                MaxHp = heroClass.MaxHp,
                SlashDamage = heroClass.SlashDamage,
                Potions = heroClass.StartingPotions,
            };
        }

        public static RunState NewRun(ulong seed, ContentCatalog catalog, List<GameEvent> events,
            string identityId = ContentCatalog.DefaultHeroId)
        {
            var run = new RunState
            {
                RunSeed = seed,
                FloorCount = catalog.RunFloorCount,
                Difficulty = catalog.Difficulty,
                ContentCatalogVersion = catalog.Version,
                Hero = CreateHero(catalog, identityId),
            };
            BeginFloor(run, 1, catalog, events);
            return run;
        }

        public static void BeginFloor(RunState run, int floorIndex, ContentCatalog catalog, List<GameEvent> events)
        {
            run.Floor = FloorGenerator.Generate(run.RunSeed, floorIndex, catalog);
            SetupFloor(run, catalog, events);
        }

        /// <summary>Places the hero at the start, applies initial knowledge, and declares awake intents.</summary>
        public static void SetupFloor(RunState run, ContentCatalog catalog, List<GameEvent> events)
        {
            var floor = run.Floor;
            var hero = run.Hero;
            hero.Pos = floor.Start;
            hero.HasKey = false;
            hero.Guard = false;
            hero.ShieldCooldown = 0;
            hero.DashCooldown = 0;

            if (floor.Exit.InBounds) floor[floor.Exit].Knowledge = Knowledge.Revealed;
            events.Add(GameEvent.Of(GameEventKind.FloorStarted, amount: floor.FloorIndex, to: floor.Start));
            Visibility.Update(run, catalog, events);
            TurnResolver.DeclareAll(run, catalog, events);
        }
    }
}
