using System.Collections.Generic;
using ClickDungeon.Content;
using ClickDungeon.Domain;

namespace ClickDungeon.Simulation
{
    /// <summary>
    /// Deterministic enemy behaviour. Declare picks the intent the player will see;
    /// Execute carries out the intent declared on the previous turn (decision D-003).
    /// </summary>
    public static class EnemyAi
    {
        public static EnemyState Spawn(FloorState floor, EnemyDefinition def, GridPos pos, bool awake)
        {
            var enemy = new EnemyState
            {
                Id = floor.NextActorId++,
                DefId = def.Id,
                Pos = pos,
                Hp = def.MaxHp,
                MaxHp = def.MaxHp,
                Awake = awake,
                Intent = Intent.None(),
            };
            floor.Enemies.Add(enemy);
            return enemy;
        }

        public static void Declare(RunState run, EnemyState enemy, ContentCatalog catalog, List<GameEvent> events)
        {
            var def = catalog.Enemy(enemy.DefId);
            // A fallen skeleton lies as bones for a while, then pulls itself back together at half its hearts (D-058).
            if (enemy.Mode == EnemyMode.Bones)
            {
                if (enemy.ModeTurns > 0)
                {
                    enemy.ModeTurns--;
                    enemy.Intent = Intent.Reassemble();
                    return;
                }
                enemy.Mode = EnemyMode.Normal;
                enemy.Hp = System.Math.Max(1, (enemy.MaxHp + 1) / 2);
                events.Add(GameEvent.Of(GameEventKind.EnemyReassembled, enemy.Id, to: enemy.Pos, amount: enemy.Hp, source: def.Id));
            }
            if (enemy.Staggered)
            {
                enemy.Staggered = false;
                enemy.Intent = Intent.Recover();
                return;
            }

            switch (def.Behavior)
            {
                case EnemyBehavior.Chaser:
                    enemy.Intent = ChaseIntent(run, enemy);
                    break;
                case EnemyBehavior.SlowChaser:
                    enemy.Intent = enemy.ActionCounter % 2 == 0 ? ChaseIntent(run, enemy) : Intent.Rest();
                    enemy.ActionCounter++;
                    break;
                case EnemyBehavior.Lane:
                    enemy.Intent = LaneIntent(run, enemy, def);
                    break;
                case EnemyBehavior.Boss:
                    enemy.Intent = BossIntent(run, enemy, def, events);
                    break;
                case EnemyBehavior.Charger:
                    enemy.Intent = ChargerIntent(run, enemy, def);
                    break;
                case EnemyBehavior.Bomber:
                    enemy.Intent = BomberIntent(run, enemy, def);
                    break;
            }
        }

        /// <summary>
        /// The Armored Boar (D-058): gores what stands next to it, charges down any clear line to the hero, and is winded
        /// for a turn after a charge - the opening to hit back. Otherwise it is heavy, and lumbers a step every other turn.
        /// </summary>
        static Intent ChargerIntent(RunState run, EnemyState enemy, EnemyDefinition def)
        {
            if (enemy.Intent.Kind == IntentKind.Charge) return Intent.Rest();
            if (enemy.Pos.IsAdjacent(run.Hero.Pos)) return Intent.Attack(run.Hero.Pos);
            if (Board.HeroInChargeLane(run, enemy.Pos, def.Range, out var dir)) return Intent.Charge(dir);
            enemy.ActionCounter++;
            return enemy.ActionCounter % 2 == 1 ? Intent.Move() : Intent.Rest();
        }

        /// <summary>
        /// The Goblin Bomber (D-058): keeps its distance, lobs a lit bomb at the hero's tile from up to
        /// <see cref="EnemyDefinition.ThrowRange"/> away, and takes a turn to light the next. Cornered, it lashes out.
        /// </summary>
        static Intent BomberIntent(RunState run, EnemyState enemy, EnemyDefinition def)
        {
            if (enemy.Intent.Kind == IntentKind.Throw) return Intent.Rest();
            if (enemy.Pos.IsAdjacent(run.Hero.Pos))
                return TryStepAway(run, enemy, out _) ? Intent.Move() : Intent.Attack(run.Hero.Pos);
            if (enemy.Pos.Chebyshev(run.Hero.Pos) <= def.ThrowRange && Board.CanHoldBomb(run.Floor, run.Hero.Pos))
                return Intent.Throw(run.Hero.Pos);
            return Intent.Move();
        }

        /// <summary>
        /// Melee enemies must stand on a tile next to the hero to attack; otherwise they step closer. Reach is the same in
        /// both movement modes (D-021): only ranged enemies (the imp's fire lane) and the boss (slam, summon) act from a
        /// distance.
        /// </summary>
        static Intent ChaseIntent(RunState run, EnemyState enemy) =>
            enemy.Pos.IsAdjacent(run.Hero.Pos) ? Intent.Attack(run.Hero.Pos) : Intent.Move();

        static Intent LaneIntent(RunState run, EnemyState enemy, EnemyDefinition def)
        {
            // Reload after every shot.
            if (enemy.Intent.Kind == IntentKind.Fire) return Intent.Rest();
            if (enemy.Pos.IsAdjacent(run.Hero.Pos) && TryStepAway(run, enemy, out _)) return Intent.Move();
            if (Board.HeroInLane(run, enemy.Pos, def.Range, out var dir)) return Intent.Fire(dir);
            return Intent.Move();
        }

        static Intent BossIntent(RunState run, EnemyState boss, EnemyDefinition def, List<GameEvent> events)
        {
            if (boss.Mode == EnemyMode.Puffed)
            {
                if (boss.ModeTurns > 0)
                {
                    boss.ModeTurns--;
                    return ChaseIntent(run, boss);
                }
                boss.Mode = EnemyMode.Deflated;
                boss.ModeTurns = 1;
                events.Add(GameEvent.Of(GameEventKind.BossDeflated, boss.Id, to: boss.Pos));
                return Intent.Rest();
            }
            if (boss.Mode == EnemyMode.Deflated)
            {
                boss.Mode = EnemyMode.Normal;
                boss.ModeTurns = 0;
            }

            // Slam, summon, (slam again, D-040), puff up.
            int step = boss.ActionCounter % (def.DoubleSlam ? 4 : 3);
            boss.ActionCounter++;
            if (step == 1)
            {
                foreach (var cell in Board.Neighbours(boss.Pos))
                    if (Board.EnemyCanEnter(run, cell)) return Intent.Summon(cell);
                return Intent.Slam(run.Hero.Pos);
            }
            if (step == 0 || (def.DoubleSlam && step == 2)) return Intent.Slam(run.Hero.Pos);
            return Intent.PuffUp();
        }

        /// <summary>
        /// Where a summon's minions appear: the declared tile, then (D-039) the boss's other free neighbours until
        /// <see cref="EnemyDefinition.SummonCount"/> are placed. A blocked declared tile cancels the summon. The threat
        /// overlay marks the same tiles, so every minion is telegraphed.
        /// </summary>
        public static List<GridPos> SummonCells(RunState run, EnemyState boss, EnemyDefinition def, GridPos target)
        {
            var cells = new List<GridPos>();
            if (!Board.EnemyCanEnter(run, target)) return cells;
            cells.Add(target);
            foreach (var cell in Board.Neighbours(boss.Pos))
            {
                if (cells.Count >= System.Math.Max(1, def.SummonCount)) break;
                if (cell != target && Board.EnemyCanEnter(run, cell)) cells.Add(cell);
            }
            return cells;
        }

        public static void Execute(RunState run, EnemyState enemy, ContentCatalog catalog, List<GameEvent> events)
        {
            var def = catalog.Enemy(enemy.DefId);
            var intent = enemy.Intent;
            switch (intent.Kind)
            {
                case IntentKind.Attack:
                    // A melee blow only lands from a neighbouring tile, on a hero still standing where it was aimed.
                    if (enemy.Pos.IsAdjacent(intent.Target) && run.Hero.Pos == intent.Target)
                    {
                        int hit = Renown.Hit(run, catalog, def.Damage);
                        events.Add(GameEvent.Of(GameEventKind.EnemyAttacked, enemy.Id, enemy.Pos, intent.Target, hit, def.Id));
                        // Riposte is answered inside DamageHero now, so every blocked blow pays alike (D-052).
                        bool blocked = Combat.DamageHero(run, hit, def.Id, events, attacker: enemy, catalog: catalog);
                        // The riposte inside DamageHero may already have killed it; nothing is dazed after it dies.
                        if (blocked && !def.IsBoss && enemy.Hp > 0)
                        {
                            enemy.Staggered = true;
                            events.Add(GameEvent.Of(GameEventKind.EnemyStaggered, enemy.Id, to: enemy.Pos, subject: def.Id));
                        }
                    }
                    else
                    {
                        events.Add(GameEvent.Of(GameEventKind.EnemyMissed, enemy.Id, enemy.Pos, intent.Target, source: def.Id));
                    }
                    break;

                case IntentKind.Move:
                {
                    GridPos step;
                    bool moved = def.Behavior == EnemyBehavior.Lane ? TryLaneStep(run, enemy, def, out step)
                        : def.Behavior == EnemyBehavior.Bomber && enemy.Pos.IsAdjacent(run.Hero.Pos) ? TryStepAway(run, enemy, out step)
                        : TryChaseStep(run, enemy, out step);
                    if (moved)
                    {
                        var from = enemy.Pos;
                        enemy.Pos = step;
                        events.Add(GameEvent.Of(GameEventKind.EnemyMoved, enemy.Id, from, step));
                    }
                    break;
                }

                case IntentKind.Fire:
                {
                    var fired = GameEvent.Of(GameEventKind.EnemyFired, enemy.Id, enemy.Pos, amount: Renown.Hit(run, catalog, def.Damage), source: def.Id);
                    events.Add(fired);
                    if (Board.TraceLane(run, enemy.Pos, intent.Dir, def.Range, out var hit))
                    {
                        fired.To = hit;
                        if (run.Hero.Pos == hit)
                        {
                            Combat.DamageHero(run, Renown.Hit(run, catalog, def.Damage), def.Id, events,
                                attacker: enemy, catalog: catalog);
                        }
                        else
                        {
                            var victim = run.Floor.EnemyAt(hit);
                            if (victim != null) Combat.DamageEnemy(run, victim, def.Damage, def.Id, catalog, events);
                        }
                    }
                    break;
                }

                case IntentKind.Slam:
                    int slam = Renown.Hit(run, catalog, def.SlamDamage);
                    events.Add(GameEvent.Of(GameEventKind.BossSlammed, enemy.Id, enemy.Pos, intent.Target, slam, def.Id));
                    if (Board.SlamCells(intent.Target, def.SlamShakesLines).Contains(run.Hero.Pos))
                        Combat.DamageHero(run, slam, def.Id, events, attacker: enemy, catalog: catalog);
                    break;

                case IntentKind.Summon:
                    if (!catalog.HasEnemy(def.SummonId)) break;
                    foreach (var cell in SummonCells(run, enemy, def, intent.Target))
                    {
                        var minion = Spawn(run.Floor, catalog.Enemy(def.SummonId), cell, awake: true);
                        minion.JustWoken = true;
                        events.Add(GameEvent.Of(GameEventKind.EnemySummoned, minion.Id, enemy.Pos, cell, source: minion.DefId));
                    }
                    break;

                case IntentKind.PuffUp:
                    enemy.Mode = EnemyMode.Puffed;
                    enemy.ModeTurns = def.PuffTurns;
                    events.Add(GameEvent.Of(GameEventKind.BossPuffed, enemy.Id, to: enemy.Pos));
                    break;

                case IntentKind.Charge:
                {
                    // It runs the line it declared, straight through anything it can cross, and stops at the first thing
                    // it cannot - the hero among them, who takes the blow for staying on the line (D-058).
                    var from = enemy.Pos;
                    var stop = enemy.Pos;
                    bool hitHero = false;
                    foreach (var cell in Board.ChargeCells(run, enemy.Pos, intent.Dir, def.Range))
                    {
                        if (cell == run.Hero.Pos) { hitHero = true; break; }
                        stop = cell;
                    }
                    enemy.Pos = stop;
                    events.Add(GameEvent.Of(GameEventKind.EnemyCharged, enemy.Id, from, stop, source: def.Id));
                    if (hitHero)
                    {
                        int gore = Renown.Hit(run, catalog, def.Damage);
                        events.Add(GameEvent.Of(GameEventKind.EnemyAttacked, enemy.Id, stop, run.Hero.Pos, gore, def.Id));
                        Combat.DamageHero(run, gore, def.Id, events, attacker: enemy, catalog: catalog);
                    }
                    break;
                }

                case IntentKind.Throw:
                    // The landing tile was marked a turn ago; from here the bomb is an ordinary lit bomb, with its own
                    // armed-then-blast telegraph. A tile that has since been covered by something takes no bomb.
                    if (Board.CanHoldBomb(run.Floor, intent.Target))
                    {
                        var landed = run.Floor[intent.Target];
                        landed.Hazard = HazardKind.Bomb;
                        landed.BombFuse = catalog.Hazards.BombFuse;
                        events.Add(GameEvent.Of(GameEventKind.BombThrown, enemy.Id, enemy.Pos, intent.Target, source: def.Id));
                    }
                    break;
            }
        }

        static bool TryChaseStep(RunState run, EnemyState enemy, out GridPos step)
        {
            step = enemy.Pos;
            if (enemy.Pos.IsAdjacent(run.Hero.Pos)) return false;
            var field = Pathfinding.DistanceField(new[] { run.Hero.Pos }, p => Board.EnemyPathable(run.Floor, p), diagonal: true);
            return TryDescend(run, enemy, field, out step);
        }

        static bool TryLaneStep(RunState run, EnemyState enemy, EnemyDefinition def, out GridPos step)
        {
            if (enemy.Pos.IsAdjacent(run.Hero.Pos)) return TryStepAway(run, enemy, out step);

            var targets = new List<GridPos>();
            foreach (var d in Directions.All)
            {
                for (int i = 1; i <= def.Range; i++)
                {
                    var p = run.Hero.Pos.Step(d, i);
                    if (!p.InBounds || run.Floor[p].Terrain == Terrain.Wall) break;
                    var occupant = run.Floor.EnemyAt(p);
                    if (occupant != null && occupant != enemy) break;
                    if (i >= 2 && Board.EnemyPathable(run.Floor, p)) targets.Add(p);
                }
            }

            step = enemy.Pos;
            if (targets.Contains(enemy.Pos)) return false;
            if (targets.Count == 0) return TryChaseStep(run, enemy, out step);
            var field = Pathfinding.DistanceField(targets, p => Board.EnemyPathable(run.Floor, p), diagonal: true);
            return TryDescend(run, enemy, field, out step);
        }

        static bool TryDescend(RunState run, EnemyState enemy, int[] field, out GridPos step)
        {
            step = enemy.Pos;
            int best = field[enemy.Pos.Index];
            bool found = false;
            foreach (var n in Board.Neighbours(enemy.Pos))
            {
                if (!Board.EnemyCanEnter(run, n)) continue;
                if (field[n.Index] < best)
                {
                    best = field[n.Index];
                    step = n;
                    found = true;
                }
            }
            return found;
        }

        static bool TryStepAway(RunState run, EnemyState enemy, out GridPos step)
        {
            step = enemy.Pos;
            int best = enemy.Pos.Chebyshev(run.Hero.Pos);
            bool found = false;
            foreach (var n in Board.Neighbours(enemy.Pos))
            {
                if (!Board.EnemyCanEnter(run, n)) continue;
                int distance = n.Chebyshev(run.Hero.Pos);
                if (distance > best)
                {
                    best = distance;
                    step = n;
                    found = true;
                }
            }
            return found;
        }
    }
}
