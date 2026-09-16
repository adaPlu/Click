using System;
using System.Collections.Generic;
using ClickDungeon.Content;
using ClickDungeon.Domain;

namespace ClickDungeon.Simulation
{
    /// <summary>
    /// Resolves one committed gameplay command as a complete transaction (rules §6).
    /// When this returns, the state is at a stable boundary and may be saved.
    /// </summary>
    public static class TurnResolver
    {
        public static CommandResult Apply(RunState run, PlayerCommand command, ContentCatalog catalog)
        {
            // 1. Validate
            if (!Commands.Validate(run, command, catalog, out var reason)) return CommandResult.Rejected(reason);

            var result = new CommandResult { Accepted = true };
            var events = result.Events;
            var hero = run.Hero;
            var heroClass = catalog.HeroClass(hero.ClassId);
            bool enteredCell = false;
            // Where the hero stepped from, so a vault can put them back there (D-018).
            var enteredFrom = GridPos.Invalid;

            // 2. Player action
            switch (command.Kind)
            {
                case CommandKind.Move:
                {
                    var from = hero.Pos;
                    hero.Pos = command.Target;
                    events.Add(GameEvent.Of(GameEventKind.HeroMoved, from: from, to: hero.Pos));
                    Hazards.HeroEnter(run, hero.Pos, catalog, events);
                    enteredCell = true;
                    enteredFrom = from;
                    break;
                }
                case CommandKind.Wait:
                    events.Add(GameEvent.Of(GameEventKind.HeroWaited, to: hero.Pos));
                    break;
                case CommandKind.Slash:
                {
                    events.Add(GameEvent.Of(GameEventKind.HeroSlashed, from: hero.Pos, to: command.Target, amount: hero.SlashDamage));
                    var enemy = run.Floor.EnemyAt(command.Target);
                    if (enemy != null) Combat.DamageEnemy(run, enemy, hero.SlashDamage, "slash", catalog, events);
                    else Hazards.Arm(run.Floor, command.Target, catalog.Hazards, events);
                    break;
                }
                case CommandKind.Shield:
                    hero.Guard = true;
                    hero.ShieldCooldown = heroClass.ShieldCooldown;
                    events.Add(GameEvent.Of(GameEventKind.HeroShielded, to: hero.Pos));
                    break;
                case CommandKind.Dash:
                {
                    var from = hero.Pos;
                    hero.Pos = command.Target;
                    hero.DashCooldown = heroClass.DashCooldown;
                    events.Add(GameEvent.Of(GameEventKind.HeroDashed, from: from, to: hero.Pos));
                    Hazards.HeroEnter(run, hero.Pos, catalog, events);
                    enteredCell = true;
                    enteredFrom = from;
                    break;
                }
                case CommandKind.Potion:
                {
                    int before = hero.Hp;
                    hero.Potions--;
                    hero.Hp = Math.Min(hero.MaxHp, hero.Hp + heroClass.PotionHeal);
                    events.Add(GameEvent.Of(GameEventKind.HeroHealed, to: hero.Pos, amount: hero.Hp - before));
                    break;
                }
                case CommandKind.Interact:
                    Chests.Open(run, command.Target, catalog, events);
                    break;
            }

            // 3. Deaths
            Combat.ResolveDeaths(run, catalog, events);
            if (run.Status != RunStatus.InProgress) return result;

            // 4. Visibility / waking
            Visibility.Update(run, catalog, events);

            // 5. A fall through a pit, a door into a vault, or the exit
            if (enteredCell && run.Floor[hero.Pos].Terrain == Terrain.Pit && TryFall(run, catalog, events, enteredFrom)) return result;
            if (enteredCell && run.Floor[hero.Pos].IsOpenDoor && RunFactory.EnterVault(run, catalog, events, enteredFrom)) return result;
            if (enteredCell && hero.Pos == run.Floor.Exit && TryCompleteFloor(run, catalog, events)) return result;

            // 6. Enemy phase: execute previously declared intents in ascending id order.
            var actingIds = new List<int>();
            foreach (var enemy in run.Floor.Enemies)
                if (enemy.Awake && !enemy.JustWoken) actingIds.Add(enemy.Id);
            actingIds.Sort();
            foreach (var id in actingIds)
            {
                var enemy = run.Floor.EnemyById(id);
                if (enemy == null) continue;
                EnemyAi.Execute(run, enemy, catalog, events);
                Combat.ResolveDeaths(run, catalog, events);
                if (run.Status != RunStatus.InProgress) return result;
            }

            // 7-8. Environment, then deaths
            Hazards.TickEnvironment(run, catalog, events);
            Combat.ResolveDeaths(run, catalog, events);
            if (run.Status != RunStatus.InProgress) return result;

            // 9. Declare next intents, expire guard, tick cooldowns
            DeclareAll(run, catalog, events);
            hero.Guard = false;
            if (hero.ShieldCooldown > 0) hero.ShieldCooldown--;
            if (hero.DashCooldown > 0) hero.DashCooldown--;
            run.Turn++;
            return result;
        }

        public static void DeclareAll(RunState run, ContentCatalog catalog, List<GameEvent> events)
        {
            var ids = new List<int>();
            foreach (var enemy in run.Floor.Enemies)
                if (enemy.Awake) ids.Add(enemy.Id);
            ids.Sort();
            foreach (var id in ids)
            {
                var enemy = run.Floor.EnemyById(id);
                EnemyAi.Declare(run, enemy, catalog, events);
                enemy.JustWoken = false;
            }
        }

        /// <summary>
        /// Dropping through a pit: costs HP and lands on the next floor, skipping this floor's key, chests and exit.
        /// There is no floor below the last one, and vaults hang off a floor rather than above one.
        /// </summary>
        static bool TryFall(RunState run, ContentCatalog catalog, List<GameEvent> events, GridPos enteredFrom)
        {
            var floor = run.Floor;
            if (!Board.CanFallThrough(run)) return false;

            int damage = catalog.Hazards.FallDamage;
            events.Add(GameEvent.Of(GameEventKind.FellThroughPit, to: run.Hero.Pos, amount: damage));
            Combat.DamageHero(run, damage, "fall", events, blockable: false);
            Combat.ResolveDeaths(run, catalog, events);
            if (run.Status != RunStatus.InProgress)
            {
                // A fatal fall never lands: the hero stays on the tile they stepped from, so no state has a hero in a pit.
                if (enteredFrom.InBounds && floor[enteredFrom].Terrain == Terrain.Floor) run.Hero.Pos = enteredFrom;
                return true;
            }

            events.Add(GameEvent.Of(GameEventKind.FloorCompleted, amount: floor.FloorIndex));
            run.Turn++;
            // No breather after a fall: the stairs heal is for walking down them.
            RunFactory.BeginFloor(run, floor.FloorIndex + 1, catalog, events);
            return true;
        }

        static bool TryCompleteFloor(RunState run, ContentCatalog catalog, List<GameEvent> events)
        {
            var floor = run.Floor;
            var hero = run.Hero;
            // A vault's stair only leads back to the floor the hero came from.
            if (floor.IsVault) return RunFactory.LeaveVault(run, catalog, events);
            if (!floor.ExitUnlocked)
            {
                if (floor.IsBossFloor || !hero.HasKey) return false;
                floor.ExitUnlocked = true;
                hero.HasKey = false;
                events.Add(GameEvent.Of(GameEventKind.ExitUnlocked, to: floor.Exit));
            }

            events.Add(GameEvent.Of(GameEventKind.FloorCompleted, amount: floor.FloorIndex));
            run.Turn++;
            if (floor.FloorIndex >= run.FloorCount)
            {
                run.Status = RunStatus.Won;
                events.Add(GameEvent.Of(GameEventKind.RunWon));
                return true;
            }
            RunFactory.BeginFloor(run, floor.FloorIndex + 1, catalog, events);

            // Difficulty breather: heal on arrival, never above max.
            int heal = Math.Min(catalog.FloorClearHeal, hero.MaxHp - hero.Hp);
            if (heal > 0)
            {
                hero.Hp += heal;
                events.Add(GameEvent.Of(GameEventKind.HeroHealed, to: hero.Pos, amount: heal, source: "stairs"));
            }
            return true;
        }
    }
}
