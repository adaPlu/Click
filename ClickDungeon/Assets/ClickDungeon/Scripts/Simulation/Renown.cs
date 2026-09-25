using System;
using ClickDungeon.Content;
using ClickDungeon.Domain;

namespace ClickDungeon.Simulation
{
    /// <summary>
    /// Renown's threat on the deep floors (D-040): monsters there hit harder for a built-up hero, and so do its traps (D-041).
    /// Since D-067 the depth adds threat of its own, so the bottom of the dungeon is not the top with the same monsters.
    /// The telegraph and the blow read the same number, so what a tile shows is what it deals.
    /// </summary>
    public static class Renown
    {
        /// <summary>
        /// The threat on this floor: what the player brought plus what the depth adds. The player's part is fixed for the
        /// run (their renown, capped by <see cref="RenownTuning.MaxThreat"/>); the depth's part is a step every
        /// <see cref="RenownTuning.FloorsPerThreat"/> floors below the first floor renown reaches, capped in turn. A vault
        /// hangs off a floor and carries its index, so it is as deep as the floor it opens off (D-046).
        /// </summary>
        public static int Level(RunState run, ContentCatalog catalog)
        {
            if (run == null || catalog == null || run.Floor == null) return 0;
            int first = catalog.Renown.FirstFloor;
            int floorIndex = run.Floor.FloorIndex;
            if (floorIndex < first) return 0;
            // The player's own threat ramps in as they descend rather than landing whole on the first floor it reaches:
            // at the bottom it is everything their renown earned, halfway down it is half of it. Before D-067 it was flat
            // from floor 3 to floor 20, so a twenty-floor dungeon had no curve of its own at all.
            int span = Math.Max(1, Math.Max(run.FloorCount, floorIndex) - first + 1);
            int player = Math.Max(0, run.Threat) * (floorIndex - first + 1) / span;
            // What the depth adds on its own account, for a player who has earned no renown yet. Zero switches it off.
            int perThreat = catalog.Renown.FloorsPerThreat;
            int depth = perThreat <= 0
                ? 0
                : Math.Min(catalog.Renown.MaxDepthThreat, (floorIndex - first) / perThreat);
            return player + Math.Max(0, depth);
        }

        public static bool Reaches(RunState run, ContentCatalog catalog) => Level(run, catalog) > 0;

        /// <summary>An enemy blow on this floor: its base damage plus one per <see cref="RenownTuning.ThreatPerExtraDamage"/> threat.</summary>
        public static int Hit(RunState run, ContentCatalog catalog, int baseDamage) =>
            baseDamage + Level(run, catalog) / Math.Max(1, catalog.Renown.ThreatPerExtraDamage);

        /// <summary>
        /// A trap on this floor (D-041): spikes, lava and bombs deal their base damage plus one per
        /// <see cref="RenownTuning.ThreatPerExtraTrapDamage"/> threat. A pit is a choice, not a trap, and is left alone.
        /// </summary>
        public static int Trap(RunState run, ContentCatalog catalog, int baseDamage) =>
            baseDamage + Level(run, catalog) / Math.Max(1, catalog.Renown.ThreatPerExtraTrapDamage);
    }
}
