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

            int draws = state.GreatChest ? Math.Max(1, catalog.Vault.GreatChestRewards) : 1;
            RewardRecord first = null;
            for (int draw = 0; draw < draws; draw++)
            {
                var reward = RollReward(run.RunSeed, run.Floor.FloorIndex, cell, catalog, run.Floor.IsVault, draw);
                if (run.HasReward(reward.TransactionId)) continue;
                reward.Turn = run.Turn;
                run.Rewards.Add(reward);
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
