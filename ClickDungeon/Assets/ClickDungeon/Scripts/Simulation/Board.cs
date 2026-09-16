using System;
using System.Collections.Generic;
using ClickDungeon.Domain;

namespace ClickDungeon.Simulation
{
    /// <summary>Read-only board queries shared by rules, AI, generation and presentation.</summary>
    public static class Board
    {
        static readonly GridPos[] AllCellsCache = BuildAllCells();

        public static IReadOnlyList<GridPos> AllCells => AllCellsCache;

        static GridPos[] BuildAllCells()
        {
            var cells = new GridPos[BoardRules.CellCount];
            for (int i = 0; i < cells.Length; i++) cells[i] = GridPos.FromIndex(i);
            return cells;
        }

        /// <summary>Walls, pits and locked doors block. An open door is walkable: stepping into it enters the vault (D-018).</summary>
        public static bool BlocksMovement(CellState cell) =>
            cell.Terrain == Terrain.Wall || cell.Terrain == Terrain.Pit || cell.IsLockedDoor || cell.IsClosedChest;

        /// <summary>Walls and door frames stop fire; pits and floors do not.</summary>
        public static bool BlocksFire(CellState cell) => cell.Terrain == Terrain.Wall || cell.Terrain == Terrain.Door;

        /// <summary>
        /// The hero may also step into a pit: that is a fall to the next floor (rules §4). Enemies never can, and on the last
        /// floor or inside a vault there is nowhere to fall, so pits stay solid there.
        /// </summary>
        public static bool HeroCanEnter(RunState run, GridPos p) =>
            p.InBounds
            && (!BlocksMovement(run.Floor[p]) || (run.Floor[p].Terrain == Terrain.Pit && CanFallThrough(run)))
            && run.Floor.EnemyAt(p) == null;

        public static bool CanFallThrough(RunState run) =>
            !run.Floor.IsVault && run.Floor.FloorIndex < run.FloorCount;

        /// <summary>Cells enemy AI will path through, ignoring actors. Enemies avoid live hazards.</summary>
        public static bool EnemyPathable(FloorState floor, GridPos p) =>
            p.InBounds && !BlocksMovement(floor[p]) && floor[p].Hazard == HazardKind.None && floor[p].Terrain != Terrain.Door;

        public static bool EnemyCanEnter(RunState run, GridPos p) =>
            EnemyPathable(run.Floor, p) && run.Floor.EnemyAt(p) == null && run.Hero.Pos != p;

        /// <summary>
        /// Whether the exit should look open: it is unlocked, or the hero holds a normal floor's key, so stepping
        /// on it will open it. Blobert's sealed exit ignores keys.
        /// </summary>
        public static bool ExitReadsOpen(RunState run) =>
            run.Floor.ExitUnlocked || (!run.Floor.IsBossFloor && run.Hero.HasKey);

        /// <summary>Truthful clue set for a cell (rules §2.4).</summary>
        public static Clue ClueAt(FloorState floor, GridPos p)
        {
            var cell = floor[p];
            var clue = Clue.None;
            var enemy = floor.EnemyAt(p);
            if (enemy != null && !enemy.Awake) clue |= Clue.Enemy;
            if (cell.Hazard != HazardKind.None) clue |= Clue.Danger;
            if (cell.Content == ContentKind.Key) clue |= Clue.Objective;
            if (cell.IsClosedChest || cell.Content == ContentKind.Potion) clue |= Clue.Treasure;
            if (cell.Content == ContentKind.Fountain && !cell.Used) clue |= Clue.Treasure;
            if (cell.Terrain == Terrain.Door || cell.Content == ContentKind.PressurePlate || cell.Content == ContentKind.Teleport)
                clue |= Clue.Objective;
            return clue == Clue.None ? Clue.Safe : clue;
        }

        /// <summary>Walks a straight lane; walls stop it, the first actor within range is hit.</summary>
        public static bool TraceLane(RunState run, GridPos from, Direction dir, int range, out GridPos hitCell)
        {
            for (int i = 1; i <= range; i++)
            {
                var p = from.Step(dir, i);
                if (!p.InBounds || BlocksFire(run.Floor[p])) break;
                if (run.Hero.Pos == p || run.Floor.EnemyAt(p) != null)
                {
                    hitCell = p;
                    return true;
                }
            }
            hitCell = GridPos.Invalid;
            return false;
        }

        public static bool HeroInLane(RunState run, GridPos from, int range, out Direction dir)
        {
            foreach (var d in Directions.All)
            {
                if (TraceLane(run, from, d, range, out var hit) && hit == run.Hero.Pos)
                {
                    dir = d;
                    return true;
                }
            }
            dir = Direction.Up;
            return false;
        }

        /// <summary>
        /// Cells a declared fire lane threatens, for telegraph display. Walls and enemies stop it; the hero
        /// does not, because the hero may move further along the lane before it fires.
        /// </summary>
        public static List<GridPos> LaneCells(RunState run, GridPos from, Direction dir, int range)
        {
            var cells = new List<GridPos>();
            for (int i = 1; i <= range; i++)
            {
                var p = from.Step(dir, i);
                if (!p.InBounds || BlocksFire(run.Floor[p])) break;
                cells.Add(p);
                if (run.Floor.EnemyAt(p) != null) break;
            }
            return cells;
        }

        public static List<GridPos> SlamCells(GridPos center)
        {
            var cells = new List<GridPos> { center };
            foreach (var d in Directions.All)
            {
                var n = center.Step(d);
                if (n.InBounds) cells.Add(n);
            }
            return cells;
        }

        public static List<GridPos> BlastCells(GridPos center, int radius)
        {
            var cells = new List<GridPos>();
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                var p = new GridPos(center.X + dx, center.Y + dy);
                if (p.InBounds) cells.Add(p);
            }
            return cells;
        }

        public static bool IsDeadEnd(FloorState floor, GridPos p)
        {
            int open = 0;
            foreach (var d in Directions.All)
            {
                var n = p.Step(d);
                if (n.InBounds && floor[n].Terrain == Terrain.Floor) open++;
            }
            return open <= 1;
        }
    }

    public static class Pathfinding
    {
        public const int Unreachable = int.MaxValue;

        /// <summary>BFS distance from any source over cells accepted by <paramref name="passable"/>.</summary>
        public static int[] DistanceField(IEnumerable<GridPos> sources, Func<GridPos, bool> passable)
        {
            var dist = new int[BoardRules.CellCount];
            for (int i = 0; i < dist.Length; i++) dist[i] = Unreachable;
            var queue = new Queue<GridPos>();
            foreach (var s in sources)
            {
                if (!s.InBounds || dist[s.Index] == 0) continue;
                dist[s.Index] = 0;
                queue.Enqueue(s);
            }
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                foreach (var d in Directions.All)
                {
                    var n = p.Step(d);
                    if (!n.InBounds || dist[n.Index] != Unreachable || !passable(n)) continue;
                    dist[n.Index] = dist[p.Index] + 1;
                    queue.Enqueue(n);
                }
            }
            return dist;
        }
    }
}
