using System.Collections.Generic;
using ClickDungeon.Content;
using ClickDungeon.Domain;

namespace ClickDungeon.Simulation
{
    /// <summary>
    /// The dungeon's patience (D-078, rules §4.3). A finished build cannot be threatened by bigger numbers: on a
    /// five-by-five board the hero can always step away, and potions and mana refill on every floor, so time is free
    /// and a veteran simply out-waits anything. This is the cost of waiting.
    ///
    /// Only on the deep floors (<see cref="ContentCatalog.StirFirstFloor"/>), which is the whole of its targeting: a
    /// hero who has outgrown the dungeon is always down there, and one still learning it usually is not. Measured,
    /// blind and careless over forty runs: a fresh hero reached floors 11-20 eighty-five times, a built-up one 349.
    ///
    /// It is not a telegraphed blow and does not pretend to be (rules §3.2 is about a monster's own attacks). It is
    /// weather: announced several turns before it arrives, shown in the HUD every turn it is on, and it never touches
    /// a tile - so nothing here can differ on what a cover hides (§2.1).
    /// </summary>
    public static class Stir
    {
        /// <summary>Turns of warning before the dungeon loses patience.</summary>
        public const int Warning = 3;

        /// <summary>Whether this floor is one the dungeon is impatient on at all.</summary>
        public static bool Watches(RunState run, ContentCatalog catalog) =>
            run?.Floor != null && catalog != null
            && catalog.StirAfterTurns > 0
            // A vault is a side room off a floor, and its door is one-way scenery: its own clock would punish looking
            // in the treasure room, which is the opposite of what this is for. The floor outside keeps its count.
            && !run.Floor.IsVault
            && run.Floor.FloorIndex >= catalog.StirFirstFloor;

        /// <summary>
        /// What the dungeon presses in for right now: nothing until its patience is spent, then one, and one more for
        /// every <see cref="ContentCatalog.StirEscalationTurns"/> turns after that - so lingering cannot be out-healed.
        /// </summary>
        public static int Pressure(RunState run, ContentCatalog catalog)
        {
            if (!Watches(run, catalog)) return 0;
            // The turn the patience expires is the first pressed turn, not the one after it: the warning counts
            // three, two, one and the blow lands, with no silent turn in between for the player to wonder about.
            int over = run.Floor.TurnsHere - Patience(run, catalog) + 1;
            if (over <= 0) return 0;
            int step = 1 + (over - 1) / System.Math.Max(1, catalog.StirEscalationTurns);
            if (catalog.StirMaxPressure > 0) step = System.Math.Min(step, catalog.StirMaxPressure);
            return step;
        }

        /// <summary>
        /// The turns this hero is given on a deep floor: the tier's budget, less what their renown has spent of it.
        /// A newcomer has no renown and keeps all of it; a returning hero is at the cap by level five and keeps least.
        /// </summary>
        public static int Patience(RunState run, ContentCatalog catalog) =>
            System.Math.Max(catalog.StirEscalationTurns,
                catalog.StirAfterTurns - System.Math.Max(0, run.Threat) * catalog.StirPatienceLostPerThreat);

        /// <summary>Turns left before it starts, or -1 when it already has or this floor is not watched.</summary>
        public static int TurnsOfPatienceLeft(RunState run, ContentCatalog catalog)
        {
            if (!Watches(run, catalog)) return -1;
            int left = Patience(run, catalog) - run.Floor.TurnsHere;
            return left > 0 ? left : -1;
        }

        /// <summary>
        /// One turn of the floor's clock, run at the end of the turn with the other environment steps. The count goes
        /// up first, so the turn the patience runs out is the turn it is felt - no free turn on the boundary.
        /// </summary>
        public static void Tick(RunState run, ContentCatalog catalog, List<GameEvent> events)
        {
            if (!Watches(run, catalog)) return;
            run.Floor.TurnsHere++;

            int left = TurnsOfPatienceLeft(run, catalog);
            if (left > 0)
            {
                // Said once a turn over the last few, so the first blow is never a surprise.
                if (left <= Warning)
                    events.Add(GameEvent.Of(GameEventKind.DungeonStirring, to: run.Hero.Pos, amount: left));
                return;
            }

            int pressure = Pressure(run, catalog);
            if (pressure <= 0) return;
            events.Add(GameEvent.Of(GameEventKind.DungeonPressed, to: run.Hero.Pos, amount: pressure, source: "stir"));
            // Not blockable: a raised shield stops a monster, not the dark. Guard would make waiting free again.
            Combat.DamageHero(run, pressure, "stir", events, blockable: false);
        }
    }
}
