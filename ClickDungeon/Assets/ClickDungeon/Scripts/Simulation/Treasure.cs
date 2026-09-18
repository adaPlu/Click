using System.Collections.Generic;
using ClickDungeon.Content;
using ClickDungeon.Domain;

namespace ClickDungeon.Simulation
{
    /// <summary>
    /// What a run pays out (D-025, rules §13). Coins come from chests and from walking down the stairs off a floor; the one
    /// gem in the game is Lord Blobert's. Falling through a pit skips the floor and pays nothing for it. The run only counts
    /// what it found: banking it into the profile is the Application layer's job, when the run ends.
    /// </summary>
    public static class Treasure
    {
        public static void Coins(RunState run, int amount, GridPos at, List<GameEvent> events)
        {
            if (amount <= 0) return;
            run.CoinsFound += amount;
            events.Add(GameEvent.Of(GameEventKind.CoinsFound, to: at, amount: amount));
        }

        /// <summary>
        /// One item from the drop table (D-028), picked by a hash of the run seed, the floor and where it dropped, so the same
        /// run always finds the same item. Whether it is a duplicate is the profile's business, settled at banking.
        /// </summary>
        public static void Item(RunState run, ContentCatalog catalog, GridPos at, ulong source, List<GameEvent> events)
        {
            if (catalog.Items.Count == 0) return;
            var rng = new DeterministicRng(Hash.Of(run.RunSeed, Hash.ItemSalt, (ulong)run.Floor.FloorIndex,
                (ulong)(at.InBounds ? at.Index : 99), source, run.Floor.IsVault ? 1UL : 0UL));
            var item = catalog.Items[rng.Next(catalog.Items.Count)];
            run.ItemsFound.Add(item.Id);
            events.Add(GameEvent.Of(GameEventKind.ItemFound, to: at, source: item.Id));
        }

        public static void Gems(RunState run, int amount, GridPos at, List<GameEvent> events)
        {
            if (amount <= 0) return;
            run.GemsFound += amount;
            events.Add(GameEvent.Of(GameEventKind.GemFound, to: at, amount: amount));
        }
    }
}
