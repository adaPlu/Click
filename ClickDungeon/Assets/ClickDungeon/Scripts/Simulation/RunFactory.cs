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
                Mana = heroClass.MaxMana,
                MaxMana = heroClass.MaxMana,
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
            // A new floor has its own door and its own vault behind it.
            run.VisitedVault = null;
            run.VisitedVaultDoor = GridPos.Invalid;
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
            // The door stays open after a visit, so a second step through it must find the room as it was left: looted chests,
            // dead guards and all. Only a room nobody has been in is generated and stocked (REL-26).
            bool revisit = run.VisitedVault != null && run.VisitedVaultDoor == door;
            if (revisit)
            {
                // While the hero is inside, the room is the live floor; LeaveVault puts it back.
                run.Floor = run.VisitedVault;
                run.VisitedVault = null;
            }
            else
            {
                run.VisitedVault = null;
                run.Floor = FloorGenerator.GenerateVault(run.RunSeed, outer.FloorIndex, door, catalog);
                AssignChestQuality(run);
                // A vault hangs off its floor and keeps that floor's index, so renown reaches its guards' blows; their hearts
                // have to come from the same place, or they hit for the raised number and die on the base one (D-046).
                ApplyThreat(run, catalog);
            }
            run.VisitedVaultDoor = door;
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
            // Kept as it stands, so the door leads back into this same room and not a fresh one (REL-26).
            run.VisitedVault = run.Floor;
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

        /// <summary>
        /// Chest quality (D-022). Assigned after generation rather than during it, so it draws on the run seed without
        /// moving the generator's stream: floors from a given seed are unchanged. A great chest is always Epic. Every
        /// floor that holds chests runs this, vaults included — they arrive through EnterVault, not SetupFloor (D-043).
        /// </summary>
        static void AssignChestQuality(RunState run)
        {
            var floor = run.Floor;
            foreach (var p in Board.AllCells)
            {
                var cell = floor[p];
                if (cell.Content != ContentKind.Chest) continue;
                cell.Quality = cell.GreatChest
                    ? ChestQuality.Epic
                    : Chests.RollQuality(run.RunSeed, floor.FloorIndex, p, floor.IsVault);
            }
        }

        /// <summary>Shared arrival work: clear guard, update sight and re-declare intents. The exit stays covered (D-023).</summary>
        static void ArriveOnFloor(RunState run, ContentCatalog catalog, List<GameEvent> events)
        {
            var hero = run.Hero;
            if (run.Floor.Start.InBounds && run.OuterFloor != null) hero.Pos = run.Floor.Start;
            hero.Guard = false;
            hero.WebbedTurns = 0;
            Visibility.Update(run, catalog, events);
            TurnResolver.DeclareAll(run, catalog, events);
        }

        /// <summary>Places the hero at the start, applies initial knowledge, and declares awake intents.</summary>
        /// <summary>
        /// One premium chest per special key carried in, one per floor from the first premium floor on (D-026). The tile is
        /// drawn from a hash of the run seed, not the generator's stream, so floors from a seed are otherwise unchanged; it
        /// is any plain, empty floor tile away from the start and exit, so it hides under a cover like everything else.
        /// </summary>
        static void PlacePremiumChest(RunState run, ContentCatalog catalog)
        {
            var floor = run.Floor;
            var treasure = catalog.Treasure;
            if (run.PremiumChestsToPlace <= 0 || floor.IsVault || floor.IsBossFloor) return;
            if (floor.FloorIndex < treasure.PremiumFirstFloor || floor.FloorIndex > treasure.PremiumLastFloor) return;

            var candidates = new List<GridPos>();
            foreach (var p in Board.AllCells)
            {
                var cell = floor[p];
                if (p == floor.Start || cell.IsExit || cell.Terrain != Terrain.Floor || cell.Hazard != HazardKind.None
                    || cell.Content != ContentKind.None || floor.EnemyAt(p) != null) continue;
                if (p.Manhattan(floor.Start) < 2) continue;
                candidates.Add(p);
            }
            if (candidates.Count == 0) return;

            var rng = new DeterministicRng(Hash.Of(run.RunSeed, Hash.PremiumSalt, (ulong)floor.FloorIndex));
            var chest = floor[candidates[rng.Next(candidates.Count)]];
            chest.Content = ContentKind.Chest;
            chest.Premium = true;
            chest.Quality = ChestQuality.Epic;
            run.PremiumChestsToPlace--;
        }

        /// <summary>
        /// Guiding Light (D-037): the key's tile starts uncovered. Called for every floor, and once more when a run's talents
        /// are applied, since the first floor is set up before they are.
        /// </summary>
        public static void RevealByTalents(RunState run, List<GameEvent> events)
        {
            var floor = run.Floor;
            if (run.Perk(TalentEffect.GuidingLight) <= 0 || floor.IsVault) return;
            foreach (var p in Board.AllCells)
                if (floor[p].Content == ContentKind.Key && floor[p].Knowledge != Knowledge.Revealed) Visibility.Reveal(floor, p, events);
        }

        /// <summary>Renown's threat (D-040): extra hearts for every monster on the deep floors, more for Lord Blobert.</summary>
        public static void ApplyThreat(RunState run, ContentCatalog catalog)
        {
            if (!Renown.Reaches(run, catalog)) return;
            foreach (var enemy in run.Floor.Enemies)
            {
                int extra = run.Threat * (catalog.Enemy(enemy.DefId).IsBoss ? catalog.Renown.BossHpPerThreat : catalog.Renown.HpPerThreat);
                enemy.MaxHp += extra;
                enemy.Hp += extra;
            }
        }

        public static void SetupFloor(RunState run, ContentCatalog catalog, List<GameEvent> events)
        {
            var floor = run.Floor;
            var hero = run.Hero;
            hero.Pos = floor.Start;
            hero.HasKey = false;
            hero.Guard = false;
            hero.WebbedTurns = 0;
            hero.WardSpent = false;
            Mana.Refill(hero);

            AssignChestQuality(run);
            PlacePremiumChest(run, catalog);
            ApplyThreat(run, catalog);

            // The exit is covered like every other tile until it is clicked (D-023).
            events.Add(GameEvent.Of(GameEventKind.FloorStarted, amount: floor.FloorIndex, to: floor.Start));
            RevealByTalents(run, events);
            Visibility.Update(run, catalog, events);
            TurnResolver.DeclareAll(run, catalog, events);
        }
    }
}
