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

            // 5. Exit
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

        static bool TryCompleteFloor(RunState run, ContentCatalog catalog, List<GameEvent> events)
        {
            var floor = run.Floor;
            var hero = run.Hero;
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
