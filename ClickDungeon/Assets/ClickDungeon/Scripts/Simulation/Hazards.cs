using System;
using System.Collections.Generic;
using ClickDungeon.Content;
using ClickDungeon.Domain;

namespace ClickDungeon.Simulation
{
    /// <summary>Spikes, bombs, and walking onto pickups (rules §4, §5).</summary>
    public static class Hazards
    {
        public static void Arm(FloorState floor, GridPos p, HazardTuning tuning, List<GameEvent> events)
        {
            var cell = floor[p];
            if (cell.Hazard != HazardKind.Bomb || cell.BombFuse >= 0) return;
            cell.BombFuse = tuning.BombFuse;
            cell.Knowledge = Knowledge.Revealed;
            events.Add(GameEvent.Of(GameEventKind.BombArmed, to: p));
        }

        /// <summary>Resolves entering a cell: hazard → pickup. Exit is handled by the turn resolver.</summary>
        public static void HeroEnter(RunState run, GridPos p, ContentCatalog catalog, List<GameEvent> events)
        {
            var cell = run.Floor[p];
            var tuning = catalog.Hazards;

            if (cell.Hazard == HazardKind.Spikes)
            {
                events.Add(GameEvent.Of(GameEventKind.SpikesTriggered, to: p, amount: tuning.SpikeDamage));
                Combat.DamageHero(run, tuning.SpikeDamage, "spikes", events, blockable: false);
            }
            else if (cell.Hazard == HazardKind.Bomb)
            {
                Arm(run.Floor, p, tuning, events);
            }
            else if (cell.Hazard == HazardKind.Lava)
            {
                // Lava is permanent, like spikes, but hotter: enemies path around it and Guard does not help.
                events.Add(GameEvent.Of(GameEventKind.LavaBurned, to: p, amount: tuning.LavaDamage));
                Combat.DamageHero(run, tuning.LavaDamage, "lava", events, blockable: false);
            }

            if (cell.Content == ContentKind.Key)
            {
                cell.Content = ContentKind.None;
                run.Hero.HasKey = true;
                events.Add(GameEvent.Of(GameEventKind.KeyCollected, to: p));
            }
            else if (cell.Content == ContentKind.Potion)
            {
                cell.Content = ContentKind.None;
                run.Hero.Potions++;
                events.Add(GameEvent.Of(GameEventKind.PotionCollected, to: p, amount: 1));
            }
            else if (cell.Content == ContentKind.Fountain && !cell.Used)
            {
                cell.Used = true;
                int healed = Math.Min(tuning.FountainHeal, run.Hero.MaxHp - run.Hero.Hp);
                if (healed > 0)
                {
                    run.Hero.Hp += healed;
                    events.Add(GameEvent.Of(GameEventKind.HeroHealed, to: p, amount: healed, source: "fountain"));
                }
                events.Add(GameEvent.Of(GameEventKind.FountainUsed, to: p, amount: healed));
            }
            else if (cell.Content == ContentKind.PressurePlate && !cell.Used)
            {
                cell.Used = true;
                int opened = 0;
                foreach (var q in Board.AllCells)
                {
                    var other = run.Floor[q];
                    if (!other.IsLockedDoor) continue;
                    other.Used = true;
                    other.Knowledge = Knowledge.Revealed;
                    opened++;
                }
                events.Add(GameEvent.Of(GameEventKind.DoorsOpened, to: p, amount: opened));
            }
            else if (cell.Content == ContentKind.Teleport)
            {
                // Pads come in pairs. Arriving on the far pad does not fire it again, so the hop always ends there.
                // An occupied far pad means nobody arrives: two actors may never share a tile.
                foreach (var q in Board.AllCells)
                {
                    if (q == p || run.Floor[q].Content != ContentKind.Teleport) continue;
                    if (run.Floor.EnemyAt(q) != null) break;
                    run.Hero.Pos = q;
                    events.Add(GameEvent.Of(GameEventKind.Teleported, from: p, to: q));
                    break;
                }
            }
        }

        /// <summary>Environment step: bombs at fuse 0 explode, other armed bombs tick down.</summary>
        public static void TickEnvironment(RunState run, ContentCatalog catalog, List<GameEvent> events)
        {
            var floor = run.Floor;
            var tuning = catalog.Hazards;
            var exploding = new List<GridPos>();
            var ticking = new List<GridPos>();
            foreach (var p in Board.AllCells)
            {
                var cell = floor[p];
                if (!cell.BombArmed) continue;
                if (cell.BombFuse == 0) exploding.Add(p);
                else ticking.Add(p);
            }

            foreach (var p in ticking) floor[p].BombFuse--;

            foreach (var p in exploding)
            {
                var cell = floor[p];
                cell.Hazard = HazardKind.None;
                cell.BombFuse = -1;
                events.Add(GameEvent.Of(GameEventKind.BombExploded, to: p, amount: tuning.BombDamage));
                foreach (var q in Board.BlastCells(p, tuning.BombRadius))
                {
                    if (run.Hero.Pos == q) Combat.DamageHero(run, tuning.BombDamage, "bomb", events);
                    var enemy = floor.EnemyAt(q);
                    if (enemy != null) Combat.DamageEnemy(run, enemy, tuning.BombDamage, "bomb", catalog, events);
                    if (q != p) Arm(floor, q, tuning, events);
                }
            }
        }
    }
}
