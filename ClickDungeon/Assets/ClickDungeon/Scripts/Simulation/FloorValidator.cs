using System;
using System.Collections.Generic;
using ClickDungeon.Content;
using ClickDungeon.Domain;

namespace ClickDungeon.Simulation
{
    /// <summary>No malformed board may reach the player (rules §8).</summary>
    public static class FloorValidator
    {
        public static bool Validate(FloorState floor, ContentCatalog catalog, List<string> errors = null)
        {
            errors = errors ?? new List<string>();
            int initialCount = errors.Count;

            if (floor.Cells == null || floor.Cells.Length != BoardRules.CellCount)
            {
                errors.Add("Board must have 25 cells.");
                return false;
            }

            if (!floor.Start.InBounds)
            {
                errors.Add("Start is out of bounds.");
            }
            else
            {
                var start = floor[floor.Start];
                if (start.Terrain != Terrain.Floor) errors.Add("Start must be on floor.");
                if (start.IsExit || start.Hazard != HazardKind.None || start.Content != ContentKind.None)
                    errors.Add("Start cell must be empty.");
                if (floor.EnemyAt(floor.Start) != null) errors.Add("An enemy occupies the start.");
            }

            int exits = 0;
            int keys = 0;
            GridPos keyPos = GridPos.Invalid;
            foreach (var p in Board.AllCells)
            {
                var cell = floor[p];
                if (cell.IsExit) exits++;
                int things = (cell.IsExit ? 1 : 0) + (cell.Hazard != HazardKind.None ? 1 : 0) + (cell.Content != ContentKind.None ? 1 : 0);
                if (things > 1) errors.Add($"Cell {p} holds more than one exit/hazard/content.");
                if (things > 0 && cell.Terrain != Terrain.Floor) errors.Add($"Cell {p} has objects on non-floor terrain.");
                if (cell.Content == ContentKind.Key)
                {
                    keys++;
                    keyPos = p;
                }
            }
            if (exits != 1) errors.Add($"Expected exactly one exit, found {exits}.");
            if (!floor.Exit.InBounds || !floor[floor.Exit].IsExit) errors.Add("Exit position does not match the exit cell.");

            var occupied = new HashSet<GridPos>();
            int bosses = 0;
            foreach (var enemy in floor.Enemies)
            {
                if (!catalog.HasEnemy(enemy.DefId))
                {
                    errors.Add($"Unknown enemy id '{enemy.DefId}'.");
                    continue;
                }
                if (catalog.Enemy(enemy.DefId).IsBoss) bosses++;
                if (!enemy.Pos.InBounds)
                {
                    errors.Add($"Enemy {enemy.Id} is out of bounds.");
                    continue;
                }
                // A Key Warden's key is this floor's key, held where the warden stands (D-061).
                if (enemy.CarriesKey)
                {
                    keys++;
                    keyPos = enemy.Pos;
                }
                if (!occupied.Add(enemy.Pos)) errors.Add($"Two actors share {enemy.Pos}.");
                var cell = floor[enemy.Pos];
                if (Board.BlocksMovement(cell) || cell.Hazard != HazardKind.None || cell.Content != ContentKind.None || cell.IsExit)
                    errors.Add($"Enemy {enemy.Id} spawned inside a blocker or object at {enemy.Pos}.");
                // A warden backs away rather than ambushes, and starts where the key was placed - which may be two tiles off.
                if (!enemy.Awake && !enemy.CarriesKey && floor.Start.InBounds && enemy.Pos.Manhattan(floor.Start) <= 2)
                    errors.Add($"Dormant enemy {enemy.Id} is within 2 tiles of the start.");
            }

            if (floor.IsBossFloor)
            {
                if (bosses != 1) errors.Add($"Boss floor needs exactly one boss, found {bosses}.");
                if (keys != 0) errors.Add("Boss floors have no key.");
            }
            else if (floor.IsVault)
            {
                if (bosses != 0) errors.Add("Vaults cannot contain a boss.");
                if (keys != 0) errors.Add("Vaults have no key.");
            }
            else
            {
                if (bosses != 0) errors.Add("Normal floors cannot contain a boss.");
                if (keys != 1) errors.Add($"Expected exactly one key, found {keys}.");
            }

            if (errors.Count == initialCount)
            {
                Func<GridPos, bool> safe = p =>
                {
                    var c = floor[p];
                    return c.Terrain == Terrain.Floor && c.Hazard == HazardKind.None && !c.IsClosedChest;
                };
                var fromStart = Reachable(floor, floor.Start, safe);
                // `safe` excludes doors, so the key and the exit are always reachable without opening a vault.
                if (floor.IsVault)
                {
                    if (fromStart[floor.Exit.Index] == Pathfinding.Unreachable)
                        errors.Add("Vault exit is unreachable from its entrance.");
                }
                else if (floor.IsBossFloor)
                {
                    if (fromStart[floor.Exit.Index] == Pathfinding.Unreachable)
                        errors.Add("Exit is unreachable from the start without crossing hazards.");
                    int reachable = 0;
                    foreach (var d in fromStart)
                        if (d != Pathfinding.Unreachable) reachable++;
                    if (reachable < 8) errors.Add("Boss arena is too small.");
                }
                else if (fromStart[keyPos.Index] == Pathfinding.Unreachable)
                {
                    errors.Add("Key is unreachable from the start without crossing hazards.");
                }
                else
                {
                    var fromKey = Reachable(floor, keyPos, safe);
                    if (fromKey[floor.Exit.Index] == Pathfinding.Unreachable)
                        errors.Add("Exit is unreachable from the key without crossing hazards.");
                }
            }

            return errors.Count == initialCount;
        }

        /// <summary>
        /// Distance over the tiles the hero can actually stand on. A teleport pad is never crossed: stepping onto one ends
        /// the step on its partner, and arriving there does not fire it again (<see cref="Hazards.HeroEnter"/>), so a pad is
        /// an edge to its partner rather than a tile with neighbours of its own (REL-27). Straight steps only, like the
        /// distance field this replaced.
        /// </summary>
        static int[] Reachable(FloorState floor, GridPos source, Func<GridPos, bool> safe)
        {
            var dist = new int[BoardRules.CellCount];
            for (int i = 0; i < dist.Length; i++) dist[i] = Pathfinding.Unreachable;
            if (!source.InBounds) return dist;

            var queue = new Queue<GridPos>();
            dist[source.Index] = 0;
            queue.Enqueue(source);
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                foreach (var d in Directions.All)
                {
                    var n = p.Step(d);
                    if (!n.InBounds || !safe(n)) continue;
                    var landed = floor[n].Content == ContentKind.Teleport ? Partner(floor, n) : n;
                    // A pad with no partner is inert: the hero stays where they stepped.
                    if (!landed.InBounds || !safe(landed)) landed = n;
                    if (dist[landed.Index] != Pathfinding.Unreachable) continue;
                    dist[landed.Index] = dist[p.Index] + 1;
                    queue.Enqueue(landed);
                }
            }
            return dist;
        }

        /// <summary>The pad a hop from <paramref name="pad"/> ends on, picked exactly as <see cref="Hazards.HeroEnter"/> picks it.</summary>
        static GridPos Partner(FloorState floor, GridPos pad)
        {
            foreach (var q in Board.AllCells)
                if (q != pad && floor[q].Content == ContentKind.Teleport) return q;
            return GridPos.Invalid;
        }
    }
}
