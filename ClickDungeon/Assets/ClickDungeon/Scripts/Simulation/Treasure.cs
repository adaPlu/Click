using System.Collections.Generic;
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

        public static void Gems(RunState run, int amount, GridPos at, List<GameEvent> events)
        {
            if (amount <= 0) return;
            run.GemsFound += amount;
            events.Add(GameEvent.Of(GameEventKind.GemFound, to: at, amount: amount));
        }
    }
}
