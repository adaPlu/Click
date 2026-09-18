using System;
using System.Collections.Generic;
using ClickDungeon.Content;
using ClickDungeon.Domain;

namespace ClickDungeon.Simulation
{
    /// <summary>
    /// Chest rewards are decided and granted inside the Interact command (decision D-005).
    /// The tactile tap sequence in presentation only reveals the committed record.
    /// </summary>
    public static class Chests
    {
        public static string TransactionId(int floorIndex, GridPos cell) => TransactionId(floorIndex, cell, false, 0);

        /// <summary>
        /// Stable id per chest and per draw. A vault sits on the same floor number as the floor it hangs off, so its chests
        /// carry a separate marker; the first draw of an ordinary chest keeps the original id.
        /// </summary>
        public static string TransactionId(int floorIndex, GridPos cell, bool vault, int draw)
        {
            string id = $"chest:{(vault ? "v" : "")}{floorIndex}:{cell.Index}";
            return draw > 0 ? id + ":" + draw : id;
        }

        /// <summary>Taps a chest needs before it opens (D-022): Common 2, Rare 3, Epic 4.</summary>
        public static int TapsToOpen(ChestQuality quality)
        {
            switch (quality)
            {
                case ChestQuality.Rare: return 3;
                case ChestQuality.Epic: return 4;
                default: return 2;
            }
        }

        /// <summary>
        /// A chest's quality, drawn from a hash rather than the generator's stream, so adding quality moved no other
        /// placement: floors from a given seed are unchanged. Weights: Common 60, Rare 30, Epic 10.
        /// </summary>
        public static ChestQuality RollQuality(ulong runSeed, int floorIndex, GridPos cell, bool vault)
        {
            var rng = new DeterministicRng(
                Hash.Of(runSeed, Hash.LootSalt, QualitySalt, (ulong)floorIndex, (ulong)cell.Index, vault ? 1UL : 0UL));
            int roll = rng.Next(100);
            if (roll < 10) return ChestQuality.Epic;
            return roll < 40 ? ChestQuality.Rare : ChestQuality.Common;
        }

        const ulong QualitySalt = 0x9E3779B97F4A7C15UL;

        /// <summary>
        /// How many rewards a chest grants when it opens: by quality for a regular chest (D-022), the vault's count for a
        /// great chest (D-018).
        /// </summary>
        public static int RewardDraws(CellState chest, ContentCatalog catalog)
        {
            if (chest.Premium) return Math.Max(1, catalog.Treasure.PremiumChestRewards);
            if (chest.GreatChest) return Math.Max(1, catalog.Vault.GreatChestRewards);
            var byQuality = catalog.ChestRewardsByQuality;
            int index = (int)chest.Quality;
            return index >= 0 && index < byQuality.Length ? Math.Max(1, byQuality[index]) : 1;
        }

        /// <summary>
        /// One tap on a closed chest. The lid gives a little and the turn resolves around it like any other action, so
        /// every revealed monster gets its response (D-022). Returns true when this tap is the one that opened it.
        /// </summary>
        public static bool Tap(RunState run, GridPos cell, ContentCatalog catalog, List<GameEvent> events)
        {
            var state = run.Floor[cell];
            if (!state.IsClosedChest) return false;
            state.ChestTaps++;
            int needed = TapsToOpen(state.Quality);
            if (state.ChestTaps >= needed)
            {
                Open(run, cell, catalog, events);
                return true;
            }
            events.Add(GameEvent.Of(GameEventKind.ChestTapped, to: cell, amount: needed - state.ChestTaps));
            return false;
        }

        /// <summary>
        /// True for the first reward a chest grants. Its transaction id is the chest's own (<c>chest:3:12</c>); every later
        /// draw appends its index (<c>chest:3:12:2</c>), so counting first draws counts chests, not rewards.
        /// </summary>
        public static bool IsFirstDraw(string transactionId)
        {
            if (string.IsNullOrEmpty(transactionId)) return false;
            int colons = 0;
            foreach (char c in transactionId)
                if (c == ':') colons++;
            return colons == 2;
        }

        /// <summary>How many chests the given rewards came from.</summary>
        public static int ChestsOpened(IEnumerable<RewardRecord> rewards)
        {
            int count = 0;
            foreach (var reward in rewards)
                if (reward != null && IsFirstDraw(reward.TransactionId)) count++;
            return count;
        }

        public static RewardRecord RollReward(ulong runSeed, int floorIndex, GridPos cell, ContentCatalog catalog) =>
            RollReward(runSeed, floorIndex, cell, catalog, false, 0);

        public static RewardRecord RollReward(ulong runSeed, int floorIndex, GridPos cell, ContentCatalog catalog, bool vault, int draw)
        {
            var seed = vault || draw > 0
                ? Hash.Of(runSeed, Hash.LootSalt, Hash.VaultSalt, (ulong)floorIndex, (ulong)cell.Index, (ulong)draw, vault ? 1UL : 0UL)
                : Hash.Of(runSeed, Hash.LootSalt, (ulong)floorIndex, (ulong)cell.Index);
            var rng = new DeterministicRng(seed);
            int total = 0;
            foreach (var entry in catalog.ChestRewards) total += entry.Weight;
            if (total <= 0) throw new InvalidOperationException("Chest reward table is empty.");

            int roll = rng.Next(total);
            foreach (var entry in catalog.ChestRewards)
            {
                if (roll < entry.Weight)
                {
                    return new RewardRecord
                    {
                        TransactionId = TransactionId(floorIndex, cell, vault, draw),
                        Kind = entry.Kind,
                        Amount = entry.Amount,
                        FloorIndex = floorIndex,
                    };
                }
                roll -= entry.Weight;
            }
            throw new InvalidOperationException("Unreachable reward roll.");
        }

        /// <summary>
        /// Opens a closed chest and grants its rewards exactly once; a vault's great chest grants several at once (D-018).
        /// Returns the first reward granted, or null if nothing was.
        /// </summary>
        public static RewardRecord Open(RunState run, GridPos cell, ContentCatalog catalog, List<GameEvent> events)
        {
            var state = run.Floor[cell];
            if (!state.IsClosedChest) return null;
            state.ChestOpened = true;
            // The special key turns in the lock and stays there (D-026).
            if (state.Premium && run.Hero.SpecialKeys > 0) run.Hero.SpecialKeys--;
            if (state.GreatChest) Treasure.Gems(run, catalog.Treasure.GemsPerGreatChest, cell, events);

            int draws = RewardDraws(state, catalog);
            RewardRecord first = null;
            for (int draw = 0; draw < draws; draw++)
            {
                var reward = RollReward(run.RunSeed, run.Floor.FloorIndex, cell, catalog, run.Floor.IsVault, draw);
                if (run.HasReward(reward.TransactionId)) continue;
                reward.Turn = run.Turn;
                run.Rewards.Add(reward);
                Treasure.Coins(run, catalog.Treasure.CoinsPerChestReward, cell, events);
                Grant(run.Hero, reward);

                var opened = GameEvent.Of(GameEventKind.ChestOpened, to: cell, amount: reward.Amount, source: reward.Kind.ToString());
                opened.Reward = reward;
                events.Add(opened);
                first = first ?? reward;
            }
            return first;
        }

        static void Grant(HeroState hero, RewardRecord reward)
        {
            switch (reward.Kind)
            {
                case RewardKind.Potion:
                    hero.Potions += reward.Amount;
                    break;
                case RewardKind.MaxHp:
                    hero.MaxHp += reward.Amount;
                    hero.Hp += reward.Amount;
                    break;
                case RewardKind.SlashDamage:
                    hero.SlashDamage += reward.Amount;
                    break;
            }
        }
    }
}
