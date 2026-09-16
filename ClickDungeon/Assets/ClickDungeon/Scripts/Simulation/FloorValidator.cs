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
                if (!occupied.Add(enemy.Pos)) errors.Add($"Two actors share {enemy.Pos}.");
                var cell = floor[enemy.Pos];
                if (Board.BlocksMovement(cell) || cell.Hazard != HazardKind.None || cell.Content != ContentKind.None || cell.IsExit)
                    errors.Add($"Enemy {enemy.Id} spawned inside a blocker or object at {enemy.Pos}.");
                if (!enemy.Awake && floor.Start.InBounds && enemy.Pos.Manhattan(floor.Start) <= 2)
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
                var fromStart = Pathfinding.DistanceField(new[] { floor.Start }, safe);
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
                    var fromKey = Pathfinding.DistanceField(new[] { keyPos }, safe);
                    if (fromKey[floor.Exit.Index] == Pathfinding.Unreachable)
                        errors.Add("Exit is unreachable from the key without crossing hazards.");
                }
            }

            return errors.Count == initialCount;
        }
    }
}
