using System.Collections.Generic;
using ClickDungeon.Content;
using ClickDungeon.Domain;

namespace ClickDungeon.Simulation
{
    /// <summary>
    /// The single command model for every input device. Validation lives only here so UI
    /// highlighting and the resolver can never disagree.
    /// </summary>
    public static class Commands
    {
        public static bool Validate(RunState run, PlayerCommand command, ContentCatalog catalog, out string reason)
        {
            reason = null;
            if (run.Status != RunStatus.InProgress) return Fail(out reason, "The run is over.");

            var hero = run.Hero;
            var floor = run.Floor;
            var target = command.Target;

            switch (command.Kind)
            {
                case CommandKind.Wait:
                    return true;

                case CommandKind.Move:
                    if (!target.InBounds || target == hero.Pos) return Fail(out reason, "Pick another tile.");
                    // Free Roam reaches the whole board; Step by Step is one tile at a time, diagonals included (D-021).
                    if (run.Movement == MovementMode.Step && !hero.Pos.IsAdjacent(target))
                        return Fail(out reason, "Sir Clickington can only step to a neighbouring tile.");
                    if (Board.ClickUncovers(run, target)) { reason = null; return true; }
                    if (!Board.HeroCanEnter(run, target)) return Fail(out reason, "That way is blocked.");
                    return true;

                case CommandKind.Slash:
                {
                    if (!target.InBounds || !hero.Pos.IsAdjacent(target))
                        return Fail(out reason, "Slash only reaches neighbouring tiles.");
                    var enemy = floor.EnemyAt(target);
                    if (enemy != null && enemy.Awake) return true;
                    var cell = floor[target];
                    if (cell.Hazard == HazardKind.Bomb && !cell.BombArmed && cell.Knowledge == Knowledge.Revealed) return true;
                    return Fail(out reason, "Nothing to slash there.");
                }

                case CommandKind.Shield:
                    if (hero.ShieldCooldown > 0) return Fail(out reason, $"Shield is recharging ({hero.ShieldCooldown}).");
                    return true;

                case CommandKind.Dash:
                    return ValidateDash(run, target, catalog.HeroClass(hero.ClassId), out reason);

                case CommandKind.Potion:
                    if (hero.Potions <= 0) return Fail(out reason, "No potions left.");
                    if (hero.Hp >= hero.MaxHp) return Fail(out reason, "Already at full health.");
                    return true;

                case CommandKind.Interact:
                    // Chests do not block, so the hero may be standing on the one they open.
                    if (!target.InBounds || (target != hero.Pos && !hero.Pos.IsAdjacent(target)))
                        return Fail(out reason, "Stand next to it first.");
                    if (!floor[target].IsClosedChest) return Fail(out reason, "Nothing to open there.");
                    return true;
            }

            return Fail(out reason, "Unknown command.");
        }

        static bool ValidateDash(RunState run, GridPos target, HeroClassDefinition heroClass, out string reason)
        {
            var hero = run.Hero;
            if (hero.DashCooldown > 0) return Fail(out reason, $"Dash is recharging ({hero.DashCooldown}).");
            int distance = hero.Pos.Chebyshev(target);
            // One or two tiles in a straight line, diagonals included (rules §5).
            if (!target.InBounds
                || !Directions.TryStepFromDelta(target.X - hero.Pos.X, target.Y - hero.Pos.Y, out var step)
                || distance < 1 || distance > heroClass.DashDistance)
                return Fail(out reason, $"Dash moves one or {heroClass.DashDistance} tiles in a straight line.");

            for (int i = 1; i < distance; i++)
            {
                var middle = hero.Pos.Offset(new GridPos(step.X * i, step.Y * i));
                // Whatever blocks the dash under a cover stops it as a bump instead of refusing it (D-023).
                if (DashBumpsAt(run, middle, landing: false)) { reason = null; return true; }
                var cell = run.Floor[middle];
                if (cell.Terrain != Terrain.Floor || cell.IsClosedChest || run.Floor.EnemyAt(middle) != null)
                    return Fail(out reason, "Something blocks the dash.");
            }

            var landing = run.Floor[target];
            // Free Roam has no sensing, so every distant tile is unknown; a blind dash is no worse than a blind step.
            if (run.Movement == MovementMode.Step && landing.Knowledge == Knowledge.Unseen)
                return Fail(out reason, "Can't dash into the unknown.");
            if (DashBumpsAt(run, target, landing: true)) { reason = null; return true; }
            var occupant = run.Floor.EnemyAt(target);
            if (occupant != null) return Fail(out reason, occupant.Awake ? "An enemy stands there." : "Something lurks there.");
            if (!Board.HeroCanEnter(run, target)) return Fail(out reason, "Can't land there.");

            reason = null;
            return true;
        }

        /// <summary>
        /// A covered tile on a dash path that stops the dash as a bump (D-023). The landing uses the move rule; a middle tile
        /// also stops the dash for anything a dash cannot pass over (a wall, pit, door or closed chest).
        /// </summary>
        public static bool DashBumpsAt(RunState run, GridPos p, bool landing)
        {
            if (!p.InBounds || run.Floor[p].Knowledge == Knowledge.Revealed) return false;
            if (landing) return Board.ClickUncovers(run, p);
            var enemy = run.Floor.EnemyAt(p);
            if (enemy != null) return !enemy.Awake;
            var cell = run.Floor[p];
            return cell.Terrain != Terrain.Floor || cell.IsClosedChest;
        }

        /// <summary>Cells where a targeted command of this kind is currently legal.</summary>
        public static List<GridPos> LegalTargets(RunState run, CommandKind kind, ContentCatalog catalog)
        {
            var cells = new List<GridPos>();
            foreach (var p in Board.AllCells)
                if (Validate(run, new PlayerCommand(kind, p), catalog, out _)) cells.Add(p);
            return cells;
        }

        /// <summary>MOVE-mode tap (decision D-011): step, slash an adjacent enemy, open a chest, or wait on self.</summary>
        public static bool TryContextual(RunState run, GridPos cell, out PlayerCommand command)
        {
            var hero = run.Hero;
            command = default;
            if (cell == hero.Pos)
            {
                command = run.Floor[cell].IsClosedChest ? PlayerCommand.Interact(cell) : PlayerCommand.Wait();
                return true;
            }
            if (!cell.InBounds) return false;

            var enemy = run.Floor.EnemyAt(cell);
            if (hero.Pos.IsAdjacent(cell))
            {
                if (enemy != null && enemy.Awake) command = PlayerCommand.Slash(cell);
                else if (run.Floor[cell].IsClosedChest) command = PlayerCommand.Interact(cell);
                else command = PlayerCommand.Move(cell);
                return true;
            }
            // Free Roam: a tap anywhere on the board is a move, as long as the tile can be entered.
            if (run.Movement != MovementMode.Free) return false;
            command = PlayerCommand.Move(cell);
            return true;
        }

        static bool Fail(out string reason, string message)
        {
            reason = message;
            return false;
        }
    }
}
