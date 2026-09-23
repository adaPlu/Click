using System;
using System.Collections.Generic;
using System.Linq;
using ClickDungeon.Content;
using ClickDungeon.Domain;

namespace ClickDungeon.Simulation
{
    public static class TemplateTransform
    {
        public const int Count = 8;

        /// <summary>Maps a board cell to the template cell it reads from (mirror, then quarter turns).</summary>
        public static GridPos ToTemplate(GridPos p, int transform)
        {
            int n = BoardRules.Size - 1;
            int x = p.X;
            int y = p.Y;
            if (transform >= 4) x = n - x;
            for (int r = 0; r < transform % 4; r++)
            {
                int nx = y;
                y = n - x;
                x = nx;
            }
            return new GridPos(x, y);
        }
    }

    /// <summary>Hand-authored topology × transform + seeded content placement (decision D-008).</summary>
    public static class FloorGenerator
    {
        public const int MaxAttempts = 100;

        public const string KeyWardenId = "goblin_key_warden";

        public static FloorState Generate(ulong runSeed, int floorIndex, ContentCatalog catalog)
        {
            var profile = catalog.ProfileFor(floorIndex);
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var seed = Hash.Of(runSeed, Hash.GenerationSalt, (ulong)Versions.Generation, (ulong)floorIndex, (ulong)attempt);
                var floor = TryBuild(new DeterministicRng(seed), floorIndex, attempt, profile, catalog);
                if (floor != null && FloorValidator.Validate(floor, catalog)) return HandKeyToWarden(floor, profile, catalog);
            }
            throw new InvalidOperationException($"Could not generate a valid floor {floorIndex} for seed {runSeed}.");
        }

        /// <summary>
        /// On a floor whose key is guarded (D-061), a Goblin Key Warden takes it up from where it lay: it starts on that very
        /// tile, which the floor has already been proved to reach, and drops the key where it falls.
        /// </summary>
        static FloorState HandKeyToWarden(FloorState floor, FloorProfile profile, ContentCatalog catalog)
        {
            if (!profile.KeyWarden || !catalog.HasEnemy(KeyWardenId)) return floor;
            foreach (var p in Board.AllCells)
            {
                if (floor[p].Content != ContentKind.Key) continue;
                floor[p].Content = ContentKind.None;
                var warden = EnemyAi.Spawn(floor, catalog.Enemy(KeyWardenId), p, awake: false);
                warden.CarriesKey = true;
                break;
            }
            return floor;
        }

        static FloorState TryBuild(DeterministicRng rng, int floorIndex, int attempt, FloorProfile profile, ContentCatalog catalog)
        {
            var templates = catalog.Templates.Where(t => t.BossArena == profile.IsBoss).ToList();
            if (templates.Count == 0) return null;
            var template = rng.Pick(templates);
            int transform = rng.Next(TemplateTransform.Count);

            var floor = FloorState.CreateEmpty();
            floor.FloorIndex = floorIndex;
            floor.IsBossFloor = profile.IsBoss;
            floor.TemplateId = template.Id;
            floor.Transform = transform;
            floor.AttemptIndex = attempt;
            // Walls are only the cover drawn over unknown tiles, so no floor is generated with them (D-021). A template's
            // walls become plain floor; its pits stay, except where there is no floor below to fall to.
            bool pitsAllowed = floorIndex < catalog.RunFloorCount;
            foreach (var p in Board.AllCells)
            {
                var tp = TemplateTransform.ToTemplate(p, transform);
                var terrain = template.TerrainAt(tp.X, tp.Y);
                if (terrain == Terrain.Wall || (terrain == Terrain.Pit && !pitsAllowed)) terrain = Terrain.Floor;
                floor[p].Terrain = terrain;
            }

            var usable = LargestFloorRegion(floor);
            if (usable.Count < 10) return null;

            var start = rng.Pick(usable);
            var dist = Pathfinding.DistanceField(new[] { start }, p => floor[p].Terrain == Terrain.Floor);
            int farthest = usable.Max(p => dist[p.Index]);
            int wanted = Math.Min(profile.MinExitDistance, farthest);
            if (wanted < 3) return null;
            var exit = rng.Pick(usable.Where(p => dist[p.Index] >= wanted).ToList());
            floor.Start = start;
            floor.Exit = exit;
            floor[exit].IsExit = true;

            var taken = new HashSet<GridPos> { start, exit };
            List<GridPos> Free(Func<GridPos, bool> ok) => usable.Where(p => !taken.Contains(p) && ok(p)).ToList();

            if (profile.IsBoss)
            {
                var spots = Free(p => p.Manhattan(start) >= 3);
                if (spots.Count == 0) return null;
                var bossPos = rng.Pick(spots);
                EnemyAi.Spawn(floor, catalog.Enemy(profile.BossId), bossPos, awake: true);
                taken.Add(bossPos);
            }
            else
            {
                var keySpots = Free(p => dist[p.Index] >= 2);
                if (keySpots.Count == 0) return null;
                var keyPos = rng.Pick(keySpots);
                floor[keyPos].Content = ContentKind.Key;
                taken.Add(keyPos);

                int enemyCount = rng.Range(profile.MinEnemies, profile.MaxEnemies);
                for (int i = 0; i < enemyCount; i++)
                {
                    var spots = Free(p => p.Manhattan(start) >= 3);
                    if (spots.Count == 0) break;
                    var pos = rng.Pick(spots);
                    EnemyAi.Spawn(floor, catalog.Enemy(rng.Pick(profile.EnemyPool)), pos, awake: false);
                    taken.Add(pos);
                }
            }

            void PlaceHazards(HazardKind kind, int count)
            {
                for (int i = 0; i < count; i++)
                {
                    var spots = Free(p => p.Manhattan(start) >= 2);
                    if (spots.Count == 0) return;
                    var pos = rng.Pick(spots);
                    floor[pos].Hazard = kind;
                    taken.Add(pos);
                }
            }

            PlaceHazards(HazardKind.Spikes, rng.Range(profile.MinSpikes, profile.MaxSpikes));
            PlaceHazards(HazardKind.Bomb, rng.Range(profile.MinBombs, profile.MaxBombs));

            for (int i = 0; i < profile.Chests; i++)
            {
                var pockets = Free(p => p.Manhattan(start) >= 2 && Board.IsDeadEnd(floor, p));
                var spots = pockets.Count > 0 ? pockets : Free(p => p.Manhattan(start) >= 2);
                if (spots.Count == 0) break;
                var pos = rng.Pick(spots);
                floor[pos].Content = ContentKind.Chest;
                taken.Add(pos);
            }

            PlaceHazards(HazardKind.Lava, rng.Range(profile.MinLava, profile.MaxLava));

            // A vault door only exists together with the plate that opens it (D-018). Dead ends first, so doors rarely block a route.
            if (profile.Vault)
            {
                var pockets = Free(p => p.Manhattan(start) >= 2 && Board.IsDeadEnd(floor, p));
                var doorSpots = pockets.Count > 0 ? pockets : Free(p => p.Manhattan(start) >= 2);
                if (doorSpots.Count > 0)
                {
                    var doorPos = rng.Pick(doorSpots);
                    taken.Add(doorPos);
                    var plateSpots = Free(p => p.Manhattan(start) >= 1);
                    if (plateSpots.Count > 0)
                    {
                        floor[doorPos].Terrain = Terrain.Door;
                        var platePos = rng.Pick(plateSpots);
                        floor[platePos].Content = ContentKind.PressurePlate;
                        taken.Add(platePos);
                    }
                    else
                    {
                        taken.Remove(doorPos);
                    }
                }
            }

            if (profile.Teleports)
            {
                var firstSpots = Free(p => p.Manhattan(start) >= 2);
                if (firstSpots.Count > 0)
                {
                    var first = rng.Pick(firstSpots);
                    taken.Add(first);
                    var secondSpots = Free(p => p.Manhattan(first) >= 3);
                    if (secondSpots.Count > 0)
                    {
                        var second = rng.Pick(secondSpots);
                        floor[first].Content = ContentKind.Teleport;
                        floor[second].Content = ContentKind.Teleport;
                        taken.Add(second);
                    }
                    else
                    {
                        taken.Remove(first);
                    }
                }
            }

            for (int i = 0; i < profile.Fountains; i++)
            {
                var spots = Free(p => p.Manhattan(start) >= 2);
                if (spots.Count == 0) break;
                var pos = rng.Pick(spots);
                floor[pos].Content = ContentKind.Fountain;
                taken.Add(pos);
            }

            int potions = rng.Range(profile.MinPotions, profile.MaxPotions);
            for (int i = 0; i < potions; i++)
            {
                var spots = Free(p => p.Manhattan(start) >= 1);
                if (spots.Count == 0) break;
                var pos = rng.Pick(spots);
                floor[pos].Content = ContentKind.Potion;
                taken.Add(pos);
            }

            return floor;
        }

        /// <summary>
        /// The room behind a door (D-018): nine tiles, three by three, walled in on every side, with the door the hero
        /// came through at its centre. Awake guards hold the far tiles and either one great chest or a few ordinary ones
        /// stand between them (D-064).
        /// </summary>
        public static FloorState GenerateVault(ulong runSeed, int floorIndex, GridPos door, ContentCatalog catalog)
        {
            var profile = catalog.ProfileFor(floorIndex);
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var seed = Hash.Of(runSeed, Hash.VaultSalt, (ulong)Versions.Generation, (ulong)floorIndex, (ulong)door.Index, (ulong)attempt);
                var floor = TryBuildVault(new DeterministicRng(seed), floorIndex, attempt, profile, catalog);
                if (floor != null && FloorValidator.Validate(floor, catalog)) return floor;
            }
            throw new InvalidOperationException($"Could not generate a vault for floor {floorIndex} behind {door}.");
        }

        /// <summary>The id a vault room carries instead of a template's: its shape is fixed, not drawn from the catalog.</summary>
        public const string VaultRoomId = "vault_room";

        static FloorState TryBuildVault(DeterministicRng rng, int floorIndex, int attempt, FloorProfile profile, ContentCatalog catalog)
        {
            var floor = FloorState.CreateEmpty();
            floor.FloorIndex = floorIndex;
            floor.IsVault = true;
            floor.TemplateId = VaultRoomId;
            floor.Transform = 0;
            floor.AttemptIndex = attempt;

            // The room is the same nine tiles every time, so it needs no template: a three-by-three block in the middle of
            // the board with the door it was entered by at its centre, and stone all round. A vault hangs off a floor and
            // has nowhere to fall to, so none of it is a pit (D-021). That stone is known from the moment the hero steps
            // in - it is the shape of the room, not something kept from them - so only the nine tiles carry covers.
            foreach (var p in Board.AllCells)
            {
                floor[p].Terrain = Terrain.Wall;
                floor[p].Knowledge = Knowledge.Revealed;
            }
            var back = new GridPos(BoardRules.Size / 2, BoardRules.Size / 2);
            var room = new List<GridPos>();
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var p = new GridPos(back.X + dx, back.Y + dy);
                    floor[p].Terrain = Terrain.Floor;
                    floor[p].Knowledge = Knowledge.Unseen;
                    if (p != back) room.Add(p);
                }

            floor.Exit = back;
            floor[back].IsExit = true;
            floor.ExitUnlocked = true;
            // A floor's stair stays under its cover because finding it is the game (D-023). A vault's door is not: the hero
            // has just walked through it, so it is drawn from the first turn and the way back is never in doubt.
            floor[back].Knowledge = Knowledge.Revealed;
            // Nobody ever stands in a doorway: the hero steps through it and stands on one of the four tiles that share an
            // edge with it, and steps back onto it to leave.
            var start = rng.Pick(room.Where(p => p.Manhattan(back) == 1).ToList());
            floor.Start = start;

            var taken = new HashSet<GridPos> { start };
            List<GridPos> Free(Func<GridPos, bool> ok) => room.Where(p => !taken.Contains(p) && ok(p)).ToList();

            var pool = profile.EnemyPool.Length > 0 ? profile.EnemyPool : new[] { ContentCatalog.DefaultVaultEnemyId };
            var vault = catalog.Vault;
            int guards = rng.Range(vault.MinEnemies, vault.MaxEnemies);
            for (int i = 0; i < guards; i++)
            {
                // Two tiles clear of the hero, as on any other floor: a guard is in the room, not in your face.
                var spots = Free(p => p.Manhattan(start) >= 2);
                if (spots.Count == 0) break;
                var pos = rng.Pick(spots);
                EnemyAi.Spawn(floor, catalog.Enemy(rng.Pick(pool)), pos, awake: true);
                taken.Add(pos);
            }

            bool great = rng.Next(2) == 0;
            int chests = great ? 1 : rng.Range(vault.MinChests, vault.MaxChests);
            for (int i = 0; i < chests; i++)
            {
                var spots = Free(p => p.Manhattan(start) >= 1);
                if (spots.Count == 0) break;
                var pos = rng.Pick(spots);
                floor[pos].Content = ContentKind.Chest;
                floor[pos].GreatChest = great;
                taken.Add(pos);
            }
            return floor;
        }

        public static List<GridPos> LargestFloorRegion(FloorState floor)
        {
            var best = new List<GridPos>();
            var visited = new bool[BoardRules.CellCount];
            foreach (var origin in Board.AllCells)
            {
                if (visited[origin.Index] || floor[origin].Terrain != Terrain.Floor) continue;
                var region = new List<GridPos>();
                var queue = new Queue<GridPos>();
                visited[origin.Index] = true;
                queue.Enqueue(origin);
                while (queue.Count > 0)
                {
                    var p = queue.Dequeue();
                    region.Add(p);
                    foreach (var d in Directions.All)
                    {
                        var n = p.Step(d);
                        if (!n.InBounds || visited[n.Index] || floor[n].Terrain != Terrain.Floor) continue;
                        visited[n.Index] = true;
                        queue.Enqueue(n);
                    }
                }
                if (region.Count > best.Count) best = region;
            }
            best.Sort((a, b) => a.Index.CompareTo(b.Index));
            return best;
        }
    }
}
