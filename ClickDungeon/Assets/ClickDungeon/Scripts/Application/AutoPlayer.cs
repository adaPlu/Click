using System;
using System.Collections.Generic;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;

namespace ClickDungeon.Application
{
    /// <summary>Outcome of one AutoPlayer run.</summary>
    public struct AutoRunResult
    {
        public ulong Seed;
        public Difficulty Difficulty;
        public RunStatus Status;
        public int Floor;
        public int Turns;
        public int Hp;
        public int MaxHp;

        public override string ToString() => $"seed {Seed} {Difficulty} {Status} floor {Floor} turn {Turns} hp {Hp}/{MaxHp}";
    }

    /// <summary>
    /// Deterministic one-turn look-ahead player for balance reports and bot demos. It tries every legal command on a
    /// copy of the run, scores the result and plays the best one. It starts careful and grows bolder while it makes no
    /// progress, the way a player eventually pushes past a sleeping enemy or a trap. The copy includes hidden cells, so
    /// it plays a little better than a careful human who can only read clues. Commands still go through the normal
    /// rules. Use one instance per run.
    /// </summary>
    public sealed class AutoPlayer
    {
        /// <summary>Turns without progress before the player takes full risks.</summary>
        public const int PatienceTurns = 8;

        /// <summary>Mistake rate used for balance targets: roughly a first-time player who mostly reads the telegraphs.</summary>
        public const double CasualMistakeRate = 0.2;

        public AutoPlayer(double mistakeRate = 0)
        {
            MistakeRate = Math.Max(0, Math.Min(1, mistakeRate));
        }

        /// <summary>Share of turns on which the player picks a random command that does not lose on the spot.</summary>
        public double MistakeRate { get; }

        static readonly CommandKind[] TargetedKinds = { CommandKind.Move, CommandKind.Slash, CommandKind.Dash, CommandKind.Interact };
        static readonly CommandKind[] UntargetedKinds = { CommandKind.Shield, CommandKind.Potion, CommandKind.Wait };

        long _bestProgress = long.MinValue;
        int _turnsWithoutProgress;

        public static List<PlayerCommand> LegalCommands(RunState run, ContentCatalog catalog)
        {
            var commands = new List<PlayerCommand>();
            foreach (var kind in TargetedKinds)
            foreach (var target in Commands.LegalTargets(run, kind, catalog))
                commands.Add(new PlayerCommand(kind, target));
            foreach (var kind in UntargetedKinds)
            {
                var command = new PlayerCommand(kind, GridPos.Invalid);
                if (Commands.Validate(run, command, catalog, out _)) commands.Add(command);
            }
            return commands;
        }

        /// <summary>Best command this turn. <paramref name="seed"/> only breaks near-ties, so equal inputs give equal choices.</summary>
        public PlayerCommand Choose(RunState run, ContentCatalog catalog, ulong seed)
        {
            TrackProgress(run, catalog);
            // Caution drops from 1 to 0.15 as the player runs out of patience.
            double caution = 1.0 - 0.85 * Math.Min(1.0, _turnsWithoutProgress / (double)PatienceTurns);

            var rng = new DeterministicRng(seed);
            bool slip = MistakeRate > 0 && rng.Next(1000) < MistakeRate * 1000;
            var best = PlayerCommand.Wait();
            double bestScore = double.NegativeInfinity;
            var survivable = new List<PlayerCommand>();
            foreach (var command in LegalCommands(run, catalog))
            {
                var copy = Copy(run);
                if (!TurnResolver.Apply(copy, command, catalog).Accepted) continue;
                if (copy.Status != RunStatus.Lost) survivable.Add(command);
                double score = Score(copy, catalog, caution) + rng.Next(8);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = command;
                }
            }
            return slip && survivable.Count > 0 ? survivable[rng.Next(survivable.Count)] : best;
        }

        /// <summary>Plays a whole run headless, stopping after <paramref name="maxCommands"/> commands.</summary>
        public static AutoRunResult PlayRun(ContentCatalog catalog, ulong seed, int maxCommands, double mistakeRate = 0)
        {
            var player = new AutoPlayer(mistakeRate);
            var run = RunFactory.NewRun(seed, catalog, new List<GameEvent>());
            for (int i = 0; i < maxCommands && run.Status == RunStatus.InProgress; i++)
                TurnResolver.Apply(run, player.Choose(run, catalog, seed * 7919UL + (ulong)i), catalog);
            return new AutoRunResult
            {
                Seed = seed, Difficulty = run.Difficulty, Status = run.Status, Floor = run.Floor.FloorIndex,
                Turns = run.Turn, Hp = run.Hero.Hp, MaxHp = run.Hero.MaxHp,
            };
        }

        void TrackProgress(RunState run, ContentCatalog catalog)
        {
            long progress = run.Floor.FloorIndex * 1_000_000L
                            + (run.Hero.HasKey || run.Floor.ExitUnlocked ? 100_000L : 0L)
                            - EnemyHp(run) * 1_000L
                            - GoalDistance(run, catalog);
            if (progress > _bestProgress)
            {
                _bestProgress = progress;
                _turnsWithoutProgress = 0;
            }
            else
            {
                _turnsWithoutProgress++;
            }
        }

        static double Score(RunState run, ContentCatalog catalog, double caution)
        {
            if (run.Status == RunStatus.Won) return 1e9;
            if (run.Status == RunStatus.Lost) return -1e9;
            var hero = run.Hero;
            var floor = run.Floor;
            double score = floor.FloorIndex * 10000.0;

            // The first half of the health bar is worth more, so potions wait until they are needed.
            int half = (hero.MaxHp + 1) / 2;
            score += (Math.Min(hero.Hp, half) * 120 + Math.Max(0, hero.Hp - half) * 60 + hero.Potions * 200) * caution;
            score += hero.MaxHp * 25 + hero.SlashDamage * 150;
            if (hero.HasKey || floor.ExitUnlocked) score += 1500;

            // Telegraphed damage can still be dodged or blocked next turn, so it weighs less than a step of progress.
            score -= Threats.DamageAt(Threats.Compute(run, catalog), hero.Pos) * 25 * caution;
            // Sleeping enemies count too: waking one costs nothing, wounding one is progress.
            foreach (var enemy in floor.Enemies)
                score -= enemy.Hp * (catalog.Enemy(enemy.DefId).IsBoss ? 90 : 35);

            score -= GoalDistance(run, catalog) * 60;
            return score;
        }

        static int EnemyHp(RunState run)
        {
            int total = 0;
            foreach (var enemy in run.Floor.Enemies) total += enemy.Hp;
            return total;
        }

        /// <summary>Steps to the current objective: key, then exit; on the boss floor, Blobert until he falls.</summary>
        static int GoalDistance(RunState run, ContentCatalog catalog)
        {
            var floor = run.Floor;
            var goals = new List<GridPos>();
            if (floor.IsBossFloor && !floor.ExitUnlocked)
            {
                foreach (var enemy in floor.Enemies)
                    if (catalog.Enemy(enemy.DefId).IsBoss) goals.Add(enemy.Pos);
            }
            else if (!run.Hero.HasKey && !floor.ExitUnlocked)
            {
                foreach (var p in Board.AllCells)
                    if (floor[p].Content == ContentKind.Key) goals.Add(p);
            }
            if (goals.Count == 0) goals.Add(floor.Exit);

            var field = Pathfinding.DistanceField(goals, p => !Board.BlocksMovement(floor[p]));
            int distance = field[run.Hero.Pos.Index];
            // Standing on the exit does nothing: it only triggers when entered, so step off and back on.
            if (distance == 0 && run.Hero.Pos == floor.Exit) return 2;
            return distance == Pathfinding.Unreachable ? 30 : distance;
        }

        /// <summary>Deep copy of a run for look-ahead. Tests check it serializes identically to the original.</summary>
        public static RunState Copy(RunState run)
        {
            return new RunState
            {
                SaveSchemaVersion = run.SaveSchemaVersion,
                RulesetVersion = run.RulesetVersion,
                ContentCatalogVersion = run.ContentCatalogVersion,
                GenerationVersion = run.GenerationVersion,
                RunSeed = run.RunSeed,
                FloorCount = run.FloorCount,
                Difficulty = run.Difficulty,
                Turn = run.Turn,
                Status = run.Status,
                Hero = Copy(run.Hero),
                Floor = Copy(run.Floor),
                OuterFloor = run.OuterFloor == null ? null : Copy(run.OuterFloor),
                ReturnPos = run.ReturnPos,
                // Reward records are never changed after they are granted.
                Rewards = new List<RewardRecord>(run.Rewards),
            };
        }

        static HeroState Copy(HeroState h) => new HeroState
        {
            IdentityId = h.IdentityId, ClassId = h.ClassId, Pos = h.Pos, Hp = h.Hp, MaxHp = h.MaxHp, SlashDamage = h.SlashDamage,
            Potions = h.Potions, HasKey = h.HasKey, Guard = h.Guard, ShieldCooldown = h.ShieldCooldown, DashCooldown = h.DashCooldown,
        };

        static FloorState Copy(FloorState f)
        {
            var copy = new FloorState
            {
                FloorIndex = f.FloorIndex, IsBossFloor = f.IsBossFloor, IsVault = f.IsVault, TemplateId = f.TemplateId, Transform = f.Transform,
                AttemptIndex = f.AttemptIndex, Start = f.Start, Exit = f.Exit, ExitUnlocked = f.ExitUnlocked, NextActorId = f.NextActorId,
                Cells = new CellState[f.Cells.Length],
            };
            for (int i = 0; i < f.Cells.Length; i++)
            {
                var c = f.Cells[i];
                copy.Cells[i] = new CellState
                {
                    Terrain = c.Terrain, IsExit = c.IsExit, Hazard = c.Hazard, BombFuse = c.BombFuse, Content = c.Content,
                    ChestOpened = c.ChestOpened, GreatChest = c.GreatChest, Used = c.Used, Knowledge = c.Knowledge,
                };
            }
            foreach (var e in f.Enemies)
            {
                copy.Enemies.Add(new EnemyState
                {
                    Id = e.Id, DefId = e.DefId, Pos = e.Pos, Hp = e.Hp, MaxHp = e.MaxHp, Awake = e.Awake, JustWoken = e.JustWoken,
                    Staggered = e.Staggered, Intent = e.Intent, ActionCounter = e.ActionCounter, Mode = e.Mode, ModeTurns = e.ModeTurns,
                });
            }
            return copy;
        }
    }
}
