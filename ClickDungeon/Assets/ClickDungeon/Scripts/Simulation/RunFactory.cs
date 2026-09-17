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
            string identityId = ContentCatalog.DefaultHeroId, MovementMode movement = MovementMode.Free)
        {
            var run = new RunState
            {
                RunSeed = seed,
                FloorCount = catalog.RunFloorCount,
                Difficulty = catalog.Difficulty,
                Movement = movement,
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

        /// <summary>
        /// Steps through an open door into its vault room, keeping the floor left behind exactly as it was (D-018).
        /// The key, potions and cooldowns carry in and out; vaults never nest.
        /// </summary>
        public static bool EnterVault(RunState run, ContentCatalog catalog, List<GameEvent> events, GridPos from)
        {
            if (run.OuterFloor != null) return false;
            var door = run.Hero.Pos;
            var outer = run.Floor;
            run.OuterFloor = outer;
            // The hero comes back out onto the tile they stepped in from: nobody ever stands in a doorway.
            run.ReturnPos = StepOutTile(outer, door, from);
            run.Floor = FloorGenerator.GenerateVault(run.RunSeed, outer.FloorIndex, door, catalog);
            events.Add(GameEvent.Of(GameEventKind.VaultEntered, from: door, to: run.Floor.Start, amount: outer.FloorIndex));
            ArriveOnFloor(run, catalog, events);
            run.Turn++;
            return true;
        }

        /// <summary>Takes the vault's stair back out, putting the hero on the door tile of the floor they left.</summary>
        public static bool LeaveVault(RunState run, ContentCatalog catalog, List<GameEvent> events)
        {
            if (run.OuterFloor == null) return false;
            var outer = run.OuterFloor;
            run.OuterFloor = null;
            run.Floor = outer;
            run.Hero.Pos = run.ReturnPos.InBounds ? run.ReturnPos : outer.Start;
            run.ReturnPos = GridPos.Invalid;
            events.Add(GameEvent.Of(GameEventKind.VaultLeft, to: run.Hero.Pos, amount: outer.FloorIndex));
            ArriveOnFloor(run, catalog, events);
            run.Turn++;
            return true;
        }

        /// <summary>The tile a vault visit ends on: where the hero came from, or any free neighbour of the door.</summary>
        static GridPos StepOutTile(FloorState floor, GridPos door, GridPos from)
        {
            if (from.InBounds && floor[from].Terrain == Terrain.Floor) return from;
            foreach (var d in Directions.All)
            {
                var n = door.Step(d);
                if (n.InBounds && floor[n].Terrain == Terrain.Floor && !Board.BlocksMovement(floor[n])) return n;
            }
            return floor.Start;
        }

        /// <summary>Shared arrival work: clear guard, reveal the exit, update sight and re-declare intents.</summary>
        static void ArriveOnFloor(RunState run, ContentCatalog catalog, List<GameEvent> events)
        {
            var hero = run.Hero;
            if (run.Floor.Start.InBounds && run.OuterFloor != null) hero.Pos = run.Floor.Start;
            hero.Guard = false;
            if (run.Floor.Exit.InBounds) run.Floor[run.Floor.Exit].Knowledge = Knowledge.Revealed;
            Visibility.Update(run, catalog, events);
            TurnResolver.DeclareAll(run, catalog, events);
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

            // Chest quality (D-022). Assigned here rather than during generation so it draws on the run seed without
            // moving the generator's stream: floors from a given seed are unchanged. A vault's great chest is always Epic.
            foreach (var p in Board.AllCells)
            {
                var cell = floor[p];
                if (cell.Content != ContentKind.Chest) continue;
                cell.Quality = cell.GreatChest
                    ? ChestQuality.Epic
                    : Chests.RollQuality(run.RunSeed, floor.FloorIndex, p, floor.IsVault);
            }

            if (floor.Exit.InBounds) floor[floor.Exit].Knowledge = Knowledge.Revealed;
            events.Add(GameEvent.Of(GameEventKind.FloorStarted, amount: floor.FloorIndex, to: floor.Start));
            Visibility.Update(run, catalog, events);
            TurnResolver.DeclareAll(run, catalog, events);
        }
    }
}
