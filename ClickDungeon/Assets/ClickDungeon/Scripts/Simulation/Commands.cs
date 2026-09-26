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
                    if (hero.WebbedTurns > 0) return Fail(out reason, "You're stuck in a web! Slash, shield, drink or wait it out.");
                    if (!target.InBounds || target == hero.Pos) return Fail(out reason, "Pick another tile.");
                    // Free Roam reaches the whole board; Step by Step is one tile at a time, diagonals included (D-021).
                    if (run.Movement == MovementMode.Step && !hero.Pos.IsAdjacent(target))
                        return Fail(out reason, "You can only step to a neighbouring tile.");
                    if (Board.ClickUncovers(run, target)) { reason = null; return true; }
                    if (!Board.HeroCanEnter(run, target)) return Fail(out reason, "That way is blocked.");
                    return true;

                case CommandKind.Slash:
                {
                    var enemy = floor.EnemyAt(target);
                    // A shooting class reaches an awake monster down a clear line (D-063); a bomb is still armed by hand.
                    int reach = Board.SlashReach(run);
                    if (reach > 1 && enemy != null && enemy.Awake && !hero.Pos.IsAdjacent(target))
                        return Board.HeroHasShot(run, target, reach) ? true : Fail(out reason, "No clear shot at that one.");
                    if (!target.InBounds || !hero.Pos.IsAdjacent(target))
                        return Fail(out reason, reach > 1 ? "No clear shot there." : "Slash only reaches neighbouring tiles.");
                    if (enemy != null && enemy.Awake) return true;
                    var cell = floor[target];
                    if (cell.Hazard == HazardKind.Bomb && !cell.BombArmed && cell.Knowledge == Knowledge.Revealed) return true;
                    return Fail(out reason, "Nothing to slash there.");
                }

                case CommandKind.Shield:
                {
                    int cost = Mana.ShieldCost(run, catalog.HeroClass(hero.ClassId));
                    if (!Mana.CanPay(hero, cost)) return Fail(out reason, $"Not enough mana to shield ({hero.Mana}/{cost}).");
                    return true;
                }

                case CommandKind.Dash:
                    if (hero.WebbedTurns > 0) return Fail(out reason, "You're stuck in a web! Slash, shield, drink or wait it out.");
                    return ValidateDash(run, target, catalog.HeroClass(hero.ClassId), out reason);

                case CommandKind.Potion:
                    if (hero.Potions <= 0) return Fail(out reason, "No potions left.");
                    if (hero.Hp >= hero.MaxHp) return Fail(out reason, "Already at full health.");
                    return true;

                case CommandKind.Skill:
                    // A web pins the hero's feet, not their hands: the message has always said "Slash, shield, drink or
                    // wait it out", and a skill is one of the things they can still do standing still (D-075).
                    return Skills.CanUse(run, catalog, command.Slot, target, out reason);

                case CommandKind.Interact:
                    // Chests do not block, so the hero may be standing on the one they open.
                    if (!target.InBounds || (target != hero.Pos && !hero.Pos.IsAdjacent(target)))
                        return Fail(out reason, "Stand next to it first.");
                    // Opening a sleeping mimic is accepted like opening a chest; it wakes instead (D-061).
                    if (Board.SleepingMimicAt(floor, target)) return true;
                    // A covered chest is just a cover: tapping it uncovers it like any other tile (D-023 amendment).
                    if (!floor[target].IsClosedChest || floor[target].Knowledge != Knowledge.Revealed)
                        return Fail(out reason, "Nothing to open there.");
                    // A premium chest opens only with a special key (D-026).
                    if (floor[target].Premium && hero.SpecialKeys <= 0)
                        return Fail(out reason, "This chest needs a special key.");
                    return true;
            }

            return Fail(out reason, "Unknown command.");
        }

        static bool ValidateDash(RunState run, GridPos target, HeroClassDefinition heroClass, out string reason)
        {
            var hero = run.Hero;
            int cost = Mana.DashCost(run, heroClass);
            if (!Mana.CanPay(hero, cost)) return Fail(out reason, $"Not enough mana to dash ({hero.Mana}/{cost}).");
            int distance = hero.Pos.Chebyshev(target);
            // One or two tiles in a straight line, diagonals included (rules §5).
            if (!target.InBounds
                || !Directions.TryStepFromDelta(target.X - hero.Pos.X, target.Y - hero.Pos.Y, out var step)
                || distance < 1 || distance > heroClass.DashDistance)
                return Fail(out reason, heroClass.DashDistance > 1
                    ? $"Dash moves one or {heroClass.DashDistance} tiles in a straight line."
                    : "Dash moves one tile in a straight line.");

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
            // A dash may land on a chest, so it may "land" on a mimic too - and meet it (D-061). Passing through one is
            // refused below as "Something blocks the dash", word for word what a chest in the way gets.
            if (landing && Board.SleepingMimicAt(run.Floor, p)) return true;
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
                // REL-39: an uncovered sleeping mimic is tapped exactly as the chest it looks like - the tap wakes it
                // (TurnResolver turns it into a bump). Routing it to Move instead answered differently from a chest.
                else if ((run.Floor[cell].IsClosedChest || Board.SleepingMimicAt(run.Floor, cell))
                         && run.Floor[cell].Knowledge == Knowledge.Revealed) command = PlayerCommand.Interact(cell);
                else command = PlayerCommand.Move(cell);
                return true;
            }
            // A shooting class taps a monster down a clear line to shoot it (D-063), in either movement mode.
            if (enemy != null && enemy.Awake && Board.SlashReach(run) > 1 && Board.HeroHasShot(run, cell, Board.SlashReach(run)))
            {
                command = PlayerCommand.Slash(cell);
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
