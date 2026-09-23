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
        /// <summary>Chest rewards granted during the run.</summary>
        public int Rewards;

        public override string ToString() => $"seed {Seed} {Difficulty} {Status} floor {Floor} turn {Turns} hp {Hp}/{MaxHp}";
    }

    /// <summary>
    /// Deterministic one-turn look-ahead player for balance reports and bot demos. It tries every legal command on a
    /// copy of the run, scores the result and plays the best one. It starts careful and grows bolder while it makes no
    /// progress, the way a player eventually pushes past a sleeping enemy or a trap. By default the copy includes hidden
    /// cells, so it plays better than a human who can only read what is revealed; construct it <c>blind</c> to decide on
    /// a redacted board instead. Commands still go through the normal rules. Use one instance per run.
    /// </summary>
    public sealed class AutoPlayer
    {
        /// <summary>Turns without progress before the player takes full risks.</summary>
        public const int PatienceTurns = 8;

        /// <summary>Mistake rate used for balance targets: roughly a first-time player who mostly reads the telegraphs.</summary>
        public const double CasualMistakeRate = 0.2;

        /// <summary>
        /// Spends a class's free talent points the way a player working through the whole tree might: a capstone as soon as
        /// one opens; otherwise the point goes to the path with the fewest points so far (ties in tree order), on the
        /// highest-tier talent that path can take now, so every path climbs tier by tier. Returns the names learned, in order.
        /// </summary>
        public static List<string> LearnTalents(ProfileState profile, ContentCatalog catalog, string classId)
        {
            var branches = new List<string>();
            foreach (var talent in catalog.TalentsOf(classId))
                if (!branches.Contains(talent.BranchId)) branches.Add(talent.BranchId);

            int InBranch(string branch)
            {
                int points = 0;
                foreach (var talent in catalog.TalentsOf(classId))
                    if (talent.BranchId == branch) points += Progression.Rank(profile, talent.Id);
                return points;
            }

            TalentDefinition Best(string branch)
            {
                TalentDefinition pick = null;
                foreach (var talent in catalog.TalentsOf(classId))
                    if ((branch == null ? talent.Capstone : talent.BranchId == branch) && Progression.CanLearn(profile, catalog, talent.Id)
                        && (pick == null || talent.Tier > pick.Tier))
                        pick = talent;
                return pick;
            }

            var learned = new List<string>();
            while (true)
            {
                var pick = Best(null);
                if (pick == null)
                {
                    var order = new List<string>(branches);
                    order.Sort((a, b) => InBranch(a) != InBranch(b) ? InBranch(a).CompareTo(InBranch(b)) : branches.IndexOf(a).CompareTo(branches.IndexOf(b)));
                    foreach (var branch in order)
                        if ((pick = Best(branch)) != null) break;
                }
                if (pick == null || !Progression.TryLearn(profile, catalog, pick.Id)) return learned;
                learned.Add(pick.Name);
            }
        }

        public AutoPlayer(double mistakeRate = 0, bool blind = false, bool loots = true)
        {
            MistakeRate = Math.Max(0, Math.Min(1, mistakeRate));
            Blind = blind;
            Loots = loots;
        }

        /// <summary>
        /// True when the player opens the chests it knows about. False plays key-to-exit only, which is how chest value is
        /// measured: compare the two.
        /// </summary>
        public bool Loots { get; }

        /// <summary>Score a single chest reward is worth before it is granted, roughly a potion.</summary>
        const double RewardWorth = 180;

        /// <summary>Leaving a floor with known loot behind while healthy costs this much: more than finishing the floor gains.</summary>
        const double LeaveLootPenalty = 20000;

        /// <summary>Share of turns on which the player picks a random command that does not lose on the spot.</summary>
        public double MistakeRate { get; }

        /// <summary>
        /// True when the bot only knows what the player knows. It decides on a redacted board with every unrevealed tile
        /// blanked, so it can neither read hidden content nor discover a trap by simulating a step onto it.
        /// </summary>
        public bool Blind { get; }

        static readonly CommandKind[] TargetedKinds = { CommandKind.Move, CommandKind.Slash, CommandKind.Dash, CommandKind.Interact };
        static readonly CommandKind[] UntargetedKinds = { CommandKind.Shield, CommandKind.Potion, CommandKind.Wait };

        long _bestProgress = long.MinValue;
        int _turnsWithoutProgress;

        // Anti-stall (D-048): travel that puts the hero back where it started and teaches it nothing is not tried again
        // from that tile. Without this the bot walks onto a teleport pad, arrives back on its pair, and repeats until it
        // dies — 200 turns of one run, with the key three steps away. A bump that reveals what blocked it still counts as
        // learning something, so it is not remembered.
        readonly HashSet<long> _barren = new HashSet<long>();
        // Doors whose vault has already been visited (D-049). A vault keeps its door open, so without this the bot walks
        // back in every time it feels healthy: one traced run crossed the same doorway three times and spent 430 turns
        // on one floor.
        readonly HashSet<int> _vaultsVisited = new HashSet<int>();
        bool _wasInVault;
        GridPos _lastPos = GridPos.Invalid;
        PlayerCommand _lastTravel;
        bool _lastWasTravel;
        int _lastKnown, _lastFloorStamp = int.MinValue;

        static long BarrenKey(GridPos from, PlayerCommand command) =>
            ((long)from.Index << 16) | ((long)command.Kind << 8) | (uint)(command.Target.InBounds ? command.Target.Index : 63);

        /// <summary>How much of this floor the player has seen; a bump that uncovers a tile moves it.</summary>
        static int KnownCells(RunState run)
        {
            int known = 0;
            foreach (var p in Board.AllCells)
                if (run.Floor[p].Knowledge != Knowledge.Unseen) known++;
            return known;
        }

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
            // A blind player reasons about the board they can see; a sighted one about all of it.
            var view = Blind ? Redact(run) : run;
            TrackProgress(view, catalog);

            // A new floor (or a vault off it) is a new board: nothing learned about the old one applies.
            int floorStamp = view.Floor.FloorIndex * 2 + (view.Floor.IsVault ? 1 : 0);
            if (floorStamp != _lastFloorStamp)
            {
                _barren.Clear();
                // Going down a floor leaves its vaults behind; stepping in and out of one does not.
                if (view.Floor.FloorIndex != _lastFloorStamp / 2) _vaultsVisited.Clear();
                _lastFloorStamp = floorStamp;
                _lastWasTravel = false;
            }
            int known = KnownCells(view);
            if (_lastWasTravel && view.Hero.Pos == _lastPos && known == _lastKnown)
                _barren.Add(BarrenKey(_lastPos, _lastTravel));
            _lastWasTravel = false;

            // The doorway that led into a vault is remembered on the way in, so the way back out is not an invitation.
            if (view.Floor.IsVault && !_wasInVault && _lastTravel.Target.InBounds) _vaultsVisited.Add(_lastTravel.Target.Index);
            _wasInVault = view.Floor.IsVault;
            // Caution drops from 1 to 0.15 as the player runs out of patience.
            double caution = 1.0 - 0.85 * Math.Min(1.0, _turnsWithoutProgress / (double)PatienceTurns);

            var rng = new DeterministicRng(seed);
            bool slip = MistakeRate > 0 && rng.Next(1000) < MistakeRate * 1000;
            var best = PlayerCommand.Wait();
            double bestScore = double.NegativeInfinity;
            var survivable = new List<PlayerCommand>();
            // The exit is always one click away in Free Roam, so a healthy player finishes the chests they know about first;
            // only danger is a reason to leave loot behind.
            bool holdForLoot = Loots && view.Hero.Hp * 2 > view.Hero.MaxHp && KnownLoot(view, catalog) > 0 && !AnyAwakeEnemy(view);
            foreach (var command in LegalCommands(view, catalog))
            {
                // Believing a move is legal is not enough: the real board still refuses it, exactly as it would a player.
                if (Blind && !Commands.Validate(run, command, catalog, out _)) continue;
                // Travel that has already led nowhere from this tile is not worth a second turn.
                if (IsTravel(command) && _barren.Contains(BarrenKey(view.Hero.Pos, command))) continue;
                // A vault is worth one visit. Its door stays open, and its loot does not come back.
                if (!view.Floor.IsVault && command.Target.InBounds && _vaultsVisited.Contains(command.Target.Index)
                    && view.Floor[command.Target].Terrain == Terrain.Door) continue;
                var copy = Copy(view);
                if (!TurnResolver.Apply(copy, command, catalog).Accepted) continue;
                if (copy.Status != RunStatus.Lost) survivable.Add(command);
                double score = Score(copy, catalog, caution, Blind, Loots) + rng.Next(8);
                if (holdForLoot && LeftTheFloor(view, copy)) score -= LeaveLootPenalty;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = command;
                }
            }
            var chosen = slip && survivable.Count > 0 ? survivable[rng.Next(survivable.Count)] : best;
            if (IsTravel(chosen))
            {
                _lastWasTravel = true;
                _lastTravel = chosen;
                _lastPos = view.Hero.Pos;
                _lastKnown = known;
            }
            return chosen;
        }

        /// <summary>Move and Dash exist to get somewhere; waiting, shielding or slashing in place is not a failed journey.</summary>
        static bool IsTravel(PlayerCommand command) => command.Kind == CommandKind.Move || command.Kind == CommandKind.Dash;

        /// <summary>Plays a whole run headless, stopping after <paramref name="maxCommands"/> commands.</summary>
        public static AutoRunResult PlayRun(ContentCatalog catalog, ulong seed, int maxCommands, double mistakeRate = 0,
            MovementMode movement = MovementMode.Free, bool blind = false, bool loots = true, string heroId = null)
        {
            var player = new AutoPlayer(mistakeRate, blind, loots);
            var run = RunFactory.NewRun(seed, catalog, new List<GameEvent>(), heroId ?? ContentCatalog.DefaultHeroId, movement);
            for (int i = 0; i < maxCommands && run.Status == RunStatus.InProgress; i++)
                TurnResolver.Apply(run, player.Choose(run, catalog, seed * 7919UL + (ulong)i), catalog);
            return new AutoRunResult
            {
                Seed = seed, Difficulty = run.Difficulty, Status = run.Status, Floor = run.Floor.FloorIndex,
                Turns = run.Turn, Hp = run.Hero.Hp, MaxHp = run.Hero.MaxHp, Rewards = run.Rewards.Count,
            };
        }

        void TrackProgress(RunState run, ContentCatalog catalog)
        {
            long progress = run.Floor.FloorIndex * 1_000_000L
                            + (run.Hero.HasKey || run.Floor.ExitUnlocked ? 100_000L : 0L)
                            // Uncovering the board is progress when it is the only way to find anything.
                            + (Blind ? RevealedCells(run.Floor) * 500L : 0L)
                            // So is working a chest open; without this, looting would read as stalling and burn patience.
                            + (Loots ? (long)ChestProgress(run, catalog) : 0L)
                            + run.Rewards.Count * 1_000L
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

        /// <summary>Taps already spent on known closed chests, valued as a share of what those chests will grant.</summary>
        static double ChestProgress(RunState run, ContentCatalog catalog)
        {
            double value = 0;
            foreach (var p in Board.AllCells)
            {
                var cell = run.Floor[p];
                if (!cell.IsClosedChest || cell.Knowledge != Knowledge.Revealed) continue;
                value += Chests.RewardDraws(cell, catalog) * RewardWorth * cell.ChestTaps / Chests.TapsToOpen(run, cell);
            }
            return value;
        }

        /// <summary>A sensible looter stays for chests only once nothing on the floor is awake and hunting it.</summary>
        static bool AnyAwakeEnemy(RunState run)
        {
            foreach (var enemy in run.Floor.Enemies)
                if (enemy.Awake) return true;
            return false;
        }

        /// <summary>What the chests a player can see are worth, opened or not.</summary>
        static double KnownLoot(RunState run, ContentCatalog catalog)
        {
            double value = 0;
            foreach (var p in Board.AllCells)
            {
                var cell = run.Floor[p];
                if (cell.IsClosedChest && cell.Knowledge == Knowledge.Revealed) value += Chests.RewardDraws(cell, catalog) * RewardWorth;
            }
            return value;
        }

        /// <summary>
        /// The command took the hero off this floor: down the stairs, into a pit, or back out of a vault. Stepping into a vault
        /// is not leaving, it is going after loot.
        /// </summary>
        static bool LeftTheFloor(RunState before, RunState after) =>
            after.Status == RunStatus.InProgress
            && (after.Floor.FloorIndex != before.Floor.FloorIndex || (before.Floor.IsVault && !after.Floor.IsVault));

        static double Score(RunState run, ContentCatalog catalog, double caution, bool blind, bool loots)
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
            // The floor a vault hangs off is still there, monsters and all. Counting only the board underfoot made them
            // vanish while the hero was inside, so walking back out - which puts them back - always scored worse than
            // staying, and in a room of nine tiles there is nowhere to wander to instead: the bot circled it to the
            // command cap (D-064).
            if (run.OuterFloor != null)
                foreach (var enemy in run.OuterFloor.Enemies)
                    score -= enemy.Hp * (catalog.Enemy(enemy.DefId).IsBoss ? 90 : 35);

            // Without hints, uncovering tiles is the only way to find the key, so a blind player values it directly.
            if (blind) score += RevealedCells(floor) * 40;

            if (loots)
            {
                // Taps spent count toward the reward, and standing in reach of a known chest is worth a little, so a
                // one-turn look-ahead still walks over and starts tapping.
                score += ChestProgress(run, catalog);
                foreach (var p in Board.AllCells)
                {
                    var cell = floor[p];
                    if (!cell.IsClosedChest || cell.Knowledge != Knowledge.Revealed) continue;
                    if (p == hero.Pos || p.IsAdjacent(hero.Pos)) score += Chests.RewardDraws(cell, catalog) * RewardWorth * 0.2;
                }
            }

            // A vault is a detour, worth staying in only to loot it safely. Leaving one returns to the same floor number, so
            // without this it scored as no progress and a chased or wounded player circled the room until the command cap.
            bool safeLooting = loots && hero.Hp * 2 > hero.MaxHp && KnownLoot(run, catalog) > 0 && !AnyAwakeEnemy(run);
            if (floor.IsVault && !safeLooting) score -= 2000;

            score -= GoalDistance(run, catalog) * 60;
            return score;
        }

        static int RevealedCells(FloorState floor)
        {
            int count = 0;
            foreach (var p in Board.AllCells)
                if (floor[p].Knowledge == Knowledge.Revealed) count++;
            return count;
        }

        /// <summary>
        /// What the player can actually see: a copy with every unrevealed tile blanked and the enemies hiding on those
        /// tiles removed. Look-ahead runs on this, so a blind bot cannot find a trap by simulating a step onto it.
        /// </summary>
        public static RunState Redact(RunState run)
        {
            var copy = Copy(run);
            var floor = copy.Floor;
            foreach (var p in Board.AllCells)
            {
                var cell = floor[p];
                if (cell.Knowledge == Knowledge.Revealed) continue;
                // Everything under a cover is unknown, the exit included (D-023).
                if (cell.IsExit)
                {
                    cell.IsExit = false;
                    floor.Exit = GridPos.Invalid;
                }
                cell.Terrain = Terrain.Floor;
                cell.Hazard = HazardKind.None;
                cell.BombFuse = -1;
                cell.Content = ContentKind.None;
                cell.ChestOpened = false;
                cell.GreatChest = false;
                cell.Quality = ChestQuality.Common;
                cell.ChestTaps = 0;
                cell.Used = false;
                cell.Premium = false;
            }
            // A sleeping monster is part of its cover; an awake one is drawn wherever it stands, so the player sees it.
            floor.Enemies.RemoveAll(e => !e.Awake && floor[e.Pos].Knowledge != Knowledge.Revealed);
            // The floor left outside a vault is weighed too, so what is still hidden on it has to stay hidden (D-064).
            var outer = copy.OuterFloor;
            if (outer != null) outer.Enemies.RemoveAll(e => !e.Awake && outer[e.Pos].Knowledge != Knowledge.Revealed);
            return copy;
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
            // The objective is a monster, not a tile: it cannot be stood on, only reached (D-062).
            bool monster = false;
            if (floor.IsBossFloor && !floor.ExitUnlocked)
            {
                foreach (var enemy in floor.Enemies)
                    if (catalog.Enemy(enemy.DefId).IsBoss) goals.Add(enemy.Pos);
                monster = goals.Count > 0;
            }
            else if (!run.Hero.HasKey && !floor.ExitUnlocked)
            {
                foreach (var p in Board.AllCells)
                    if (floor[p].Content == ContentKind.Key) goals.Add(p);
                // A Key Warden carries it (D-061): the warden is where the key is.
                // Asleep or awake: a blind player's view has already removed any it has not found (Redact).
                foreach (var e in floor.Enemies) if (e.CarriesKey) goals.Add(e.Pos);
                monster = goals.Count > 0 && goals.TrueForAll(g => floor.EnemyAt(g) != null);
                // No key in sight: the only lead a blind player has is the nearest tile they have not uncovered.
                if (goals.Count == 0)
                    foreach (var p in Board.AllCells)
                        if (floor[p].Knowledge != Knowledge.Revealed) goals.Add(p);
                // Every tile uncovered and still no key in hand (it is under an actor, or across lava): a pit is the only
                // way down without one, so take it rather than circle the floor (D-049).
                if (goals.Count == 0)
                    foreach (var p in Board.AllCells)
                        if (floor[p].Terrain == Terrain.Pit) goals.Add(p);
            }
            if (goals.Count == 0 && floor.Exit.InBounds) goals.Add(floor.Exit);
            // Holding the key with the exit still covered: search for it the same way as for the key (D-023).
            if (goals.Count == 0)
                foreach (var p in Board.AllCells)
                    if (floor[p].Knowledge != Knowledge.Revealed) goals.Add(p);
            if (goals.Count == 0) return 0;

            // A monster to reach - a warden holding the key, or the boss - is reached by standing next to it, from where
            // the next turn's swing lands. In Free Roam any tile is one click away, so without this every candidate move
            // scored the same and a one-move look-ahead never saw why to approach: a warden that kept its distance was
            // simply never chased, and the run stalled with the bot at full health (D-062).
            if (monster) return goals.Exists(g => g.IsAdjacent(run.Hero.Pos)) ? 0 : 1;
            // In Free Roam every tile is one click away; standing on the exit still needs a step off and back on.
            if (run.Movement == MovementMode.Free) return goals.Contains(run.Hero.Pos) ? 2 : 1;
            // Step by Step moves diagonally, so distances count diagonal steps.
            var field = Pathfinding.DistanceField(goals, p => !Board.BlocksMovement(floor[p]), diagonal: true);
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
                // A looted vault kept for the hero's return (REL-26). Without these the bot's look-ahead rebuilt a fresh
                // vault on re-entry while the real game restored the looted one, so its predictions diverged from play.
                VisitedVault = run.VisitedVault == null ? null : Copy(run.VisitedVault),
                VisitedVaultDoor = run.VisitedVaultDoor,
                // Reward records are never changed after they are granted.
                Rewards = new List<RewardRecord>(run.Rewards),
                CoinsFound = run.CoinsFound,
                GemsFound = run.GemsFound,
                PremiumChestsToPlace = run.PremiumChestsToPlace,
                XpEarned = run.XpEarned,
                ItemsFound = new List<string>(run.ItemsFound),
                PotionHealBonus = run.PotionHealBonus,
                BonusXpPerFloor = run.BonusXpPerFloor,
                BonusCoinsPerChestReward = run.BonusCoinsPerChestReward,
                DashCostCut = run.DashCostCut,
                Perks = run.Perks == null ? new Dictionary<string, int>() : new Dictionary<string, int>(run.Perks),
                MonstersSlain = run.MonstersSlain,
                ChestsOpened = run.ChestsOpened,
                // REL-44: the look-ahead scored every candidate under Free Roam with no renown, because these two were
                // never copied. Commands.Validate reads Movement, and Renown.Hit reads Threat, so the bot was judging a
                // different game from the one it was playing.
                Movement = run.Movement,
                Threat = run.Threat,
            };
        }

        static HeroState Copy(HeroState h) => new HeroState
        {
            IdentityId = h.IdentityId, ClassId = h.ClassId, Pos = h.Pos, Hp = h.Hp, MaxHp = h.MaxHp, SlashDamage = h.SlashDamage,
            Potions = h.Potions, HasKey = h.HasKey, SpecialKeys = h.SpecialKeys, Guard = h.Guard, Mana = h.Mana, WardSpent = h.WardSpent, DodgeSpent = h.DodgeSpent, WebbedTurns = h.WebbedTurns,
            MaxMana = h.MaxMana,
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
                    ChestOpened = c.ChestOpened, GreatChest = c.GreatChest, Premium = c.Premium, Quality = c.Quality, ChestTaps = c.ChestTaps,
                    Used = c.Used, Knowledge = c.Knowledge,
                };
            }
            foreach (var e in f.Enemies)
            {
                copy.Enemies.Add(new EnemyState
                {
                    Id = e.Id, DefId = e.DefId, Pos = e.Pos, Hp = e.Hp, MaxHp = e.MaxHp, Awake = e.Awake, JustWoken = e.JustWoken,
                    Staggered = e.Staggered, Intent = e.Intent, ActionCounter = e.ActionCounter, Mode = e.Mode, ModeTurns = e.ModeTurns, Enraging = e.Enraging, Rallied = e.Rallied, Disguised = e.Disguised, CarriesKey = e.CarriesKey,
                });
            }
            return copy;
        }
    }
}
