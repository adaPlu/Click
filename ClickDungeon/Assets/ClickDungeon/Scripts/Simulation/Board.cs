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
        /// No tile blocks the hero (D-021): only another actor, or a vault door still shut, which is a closed door rather
        /// than a space. Stepping into a pit is a fall to the next floor, and pits never generate where nothing is below.
        /// </summary>
        public static bool HeroCanEnter(RunState run, GridPos p) =>
            p.InBounds
            && run.Floor.EnemyAt(p) == null
            && !run.Floor[p].IsLockedDoor
            && (run.Floor[p].Terrain != Terrain.Pit || CanFallThrough(run));

        /// <summary>
        /// Clicking this covered tile uncovers it instead of moving (D-023): a sleeping enemy hides there, or the tile cannot
        /// be entered (a shut vault door, a pit with nothing below). Refusing the click would give away what is under the
        /// cover, so it becomes a bump. An awake enemy is visible wherever it stands, so clicking it is still refused.
        /// </summary>
        public static bool ClickUncovers(RunState run, GridPos p)
        {
            // Reaching for a sleeping Mimic Chest meets teeth, not the lid (D-061): a turn spent, the hero where they were.
            if (SleepingMimicAt(run.Floor, p)) return true;
            if (!p.InBounds || run.Floor[p].Knowledge == Knowledge.Revealed) return false;
            var enemy = run.Floor.EnemyAt(p);
            if (enemy != null) return !enemy.Awake;
            return !HeroCanEnter(run, p);
        }

        /// <summary>
        /// A Mimic Chest asleep on an uncovered tile, where it passes for a chest (D-061). Every command aimed at it must be
        /// answered just as a real chest's would be - accepted where a chest's is, refused where a chest's is, in the same
        /// words - because a refused command costs no turn, and any difference would let a player test chests for free.
        /// </summary>
        public static bool SleepingMimicAt(FloorState floor, GridPos p)
        {
            if (!p.InBounds || floor[p].Knowledge != Knowledge.Revealed) return false;
            var enemy = floor.EnemyAt(p);
            return enemy != null && enemy.Disguised && !enemy.Awake;
        }

        /// <summary>
        /// No falling past a boss (D-062): with a boss every few floors rather than only on the last, a pit on a boss floor
        /// would let the hero drop straight past the fight the floor exists for.
        /// </summary>
        public static bool CanFallThrough(RunState run) =>
            !run.Floor.IsVault && !run.Floor.IsBossFloor && run.Floor.FloorIndex < run.FloorCount;

        /// <summary>Cells enemy AI will path through, ignoring actors. Enemies avoid live hazards.</summary>
        public static bool EnemyPathable(FloorState floor, GridPos p) =>
            p.InBounds && !BlocksMovement(floor[p]) && floor[p].Hazard == HazardKind.None && floor[p].Terrain != Terrain.Door;

        public static bool EnemyCanEnter(RunState run, GridPos p) =>
            EnemyPathable(run.Floor, p) && run.Floor.EnemyAt(p) == null && run.Hero.Pos != p;

        /// <summary>
        /// The tiles a charge down this line passes through (D-058): straight on until a wall, hazard, door, another
        /// monster or the board's edge stops it. The hero does not end the line — they may step off it before the charge
        /// comes — so the whole path is what the telegraph marks, exactly as a fire lane is.
        /// </summary>
        public static List<GridPos> ChargeCells(RunState run, GridPos from, Direction dir, int range)
        {
            var cells = new List<GridPos>();
            for (int i = 1; i <= range; i++)
            {
                var p = from.Step(dir, i);
                if (!EnemyPathable(run.Floor, p) || run.Floor.EnemyAt(p) != null) break;
                cells.Add(p);
            }
            return cells;
        }

        /// <summary>Whether the hero stands on a line a charge from here could reach, and which way it runs.</summary>
        public static bool HeroInChargeLane(RunState run, GridPos from, int range, out Direction dir)
        {
            foreach (var d in Directions.All)
            {
                if (ChargeCells(run, from, d, range).Contains(run.Hero.Pos))
                {
                    dir = d;
                    return true;
                }
            }
            dir = Direction.Up;
            return false;
        }

        /// <summary>
        /// Whether a thrown bomb can land here (D-058): open floor with nothing on it — no hazard already, no chest, key,
        /// potion, pad or plate, and not the exit. A throw at a tile that cannot take one is never declared.
        /// </summary>
        public static bool CanHoldBomb(FloorState floor, GridPos p) =>
            p.InBounds && floor[p].Terrain == Terrain.Floor && floor[p].Hazard == HazardKind.None
            && floor[p].Content == ContentKind.None && !floor[p].IsExit;

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
            // A sleeping Mimic Chest reads as the treasure it pretends to be (D-061): the one clue that is not the whole truth.
            if (enemy != null && !enemy.Awake) clue |= enemy.Disguised ? Clue.Treasure : Clue.Enemy;
            if (cell.Hazard != HazardKind.None) clue |= Clue.Danger;
            // Step by Step only (Free Roam senses nothing). Each thing worth finding has its own mark, so "K" means the key.
            if (cell.Content == ContentKind.Key) clue |= Clue.Objective;
            if (cell.IsExit) clue |= Clue.Exit;
            if (cell.IsClosedChest || cell.Content == ContentKind.Potion) clue |= Clue.Treasure;
            if (cell.Content == ContentKind.Fountain && !cell.Used) clue |= Clue.Treasure;
            if (cell.Terrain == Terrain.Door || cell.Content == ContentKind.PressurePlate || cell.Content == ContentKind.Teleport)
                clue |= Clue.Feature;
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

        /// <summary>
        /// The slam covers the target tile and everything touching it, diagonals included (rules §3.6); with
        /// <paramref name="lines"/> it also shakes the target's whole row and column (D-039).
        /// </summary>
        public static List<GridPos> SlamCells(GridPos center, bool lines = false)
        {
            var cells = new List<GridPos> { center };
            foreach (var step in Directions.Around)
            {
                var n = center.Offset(step);
                if (n.InBounds) cells.Add(n);
            }
            if (lines)
                for (int i = 0; i < BoardRules.Size; i++)
                {
                    var row = new GridPos(i, center.Y);
                    var column = new GridPos(center.X, i);
                    if (!cells.Contains(row)) cells.Add(row);
                    if (!cells.Contains(column)) cells.Add(column);
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

        /// <summary>Neighbouring tiles an actor could step to, diagonals included.</summary>
        public static IEnumerable<GridPos> Neighbours(GridPos p)
        {
            foreach (var step in Directions.Around)
            {
                var n = p.Offset(step);
                if (n.InBounds) yield return n;
            }
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

        static IEnumerable<GridPos> StraightNeighbours(GridPos p)
        {
            foreach (var d in Directions.All)
            {
                var n = p.Step(d);
                if (n.InBounds) yield return n;
            }
        }

        /// <summary>
        /// BFS distance from any source over cells accepted by <paramref name="passable"/>. Straight steps only by default:
        /// floor generation measures distances that way. Pass <paramref name="diagonal"/> for movement distances (D-021).
        /// </summary>
        public static int[] DistanceField(IEnumerable<GridPos> sources, Func<GridPos, bool> passable, bool diagonal = false)
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
                foreach (var n in diagonal ? Board.Neighbours(p) : StraightNeighbours(p))
                {
                    if (dist[n.Index] != Unreachable || !passable(n)) continue;
                    dist[n.Index] = dist[p.Index] + 1;
                    queue.Enqueue(n);
                }
            }
            return dist;
        }
    }
}
