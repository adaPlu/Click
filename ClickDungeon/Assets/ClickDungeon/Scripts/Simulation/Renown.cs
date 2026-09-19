using ClickDungeon.Content;
using ClickDungeon.Domain;

namespace ClickDungeon.Simulation
{
    /// <summary>
    /// Renown's threat on the deep floors (D-040): monsters there hit harder for a built-up hero, and so do its traps (D-041).
    /// The telegraph and the blow read the same number, so what a tile shows is what it deals.
    /// </summary>
    public static class Renown
    {
        public static bool Reaches(RunState run, ContentCatalog catalog) =>
            run != null && run.Threat > 0 && run.Floor != null && run.Floor.FloorIndex >= catalog.Renown.FirstFloor;

        /// <summary>An enemy blow on this floor: its base damage plus one per <see cref="RenownTuning.ThreatPerExtraDamage"/> threat.</summary>
        public static int Hit(RunState run, ContentCatalog catalog, int baseDamage) =>
            Reaches(run, catalog) ? baseDamage + run.Threat / System.Math.Max(1, catalog.Renown.ThreatPerExtraDamage) : baseDamage;

        /// <summary>
        /// A trap on this floor (D-041): spikes, lava and bombs deal their base damage plus one per
        /// <see cref="RenownTuning.ThreatPerExtraTrapDamage"/> threat. A pit is a choice, not a trap, and is left alone.
        /// </summary>
        public static int Trap(RunState run, ContentCatalog catalog, int baseDamage) =>
            Reaches(run, catalog) ? baseDamage + run.Threat / System.Math.Max(1, catalog.Renown.ThreatPerExtraTrapDamage) : baseDamage;
    }
}
