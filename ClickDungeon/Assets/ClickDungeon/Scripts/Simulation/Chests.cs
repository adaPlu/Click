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
        public static string TransactionId(int floorIndex, GridPos cell) => $"chest:{floorIndex}:{cell.Index}";

        public static RewardRecord RollReward(ulong runSeed, int floorIndex, GridPos cell, ContentCatalog catalog)
        {
            var rng = new DeterministicRng(Hash.Of(runSeed, Hash.LootSalt, (ulong)floorIndex, (ulong)cell.Index));
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
                        TransactionId = TransactionId(floorIndex, cell),
                        Kind = entry.Kind,
                        Amount = entry.Amount,
                        FloorIndex = floorIndex,
                    };
                }
                roll -= entry.Weight;
            }
            throw new InvalidOperationException("Unreachable reward roll.");
        }

        /// <summary>Opens a closed chest and grants its reward exactly once. Returns null if nothing was granted.</summary>
        public static RewardRecord Open(RunState run, GridPos cell, ContentCatalog catalog, List<GameEvent> events)
        {
            var state = run.Floor[cell];
            if (!state.IsClosedChest) return null;
            state.ChestOpened = true;

            var reward = RollReward(run.RunSeed, run.Floor.FloorIndex, cell, catalog);
            if (run.HasReward(reward.TransactionId)) return null;
            reward.Turn = run.Turn;
            run.Rewards.Add(reward);
            Grant(run.Hero, reward);

            var opened = GameEvent.Of(GameEventKind.ChestOpened, to: cell, amount: reward.Amount, source: reward.Kind.ToString());
            opened.Reward = reward;
            events.Add(opened);
            return reward;
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
