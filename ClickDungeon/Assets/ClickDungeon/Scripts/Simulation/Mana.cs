using System;
using ClickDungeon.Content;
using ClickDungeon.Domain;

namespace ClickDungeon.Simulation
{
    /// <summary>
    /// Mana (D-032, rules §5.2): SHIELD and DASH are paid for from a pool that refills by one at the end of every turn and
    /// fully on every new floor. Move, slash and potions are free. Replaces the old per-ability cooldowns, so the player
    /// chooses when to spend rather than waiting out timers.
    /// </summary>
    public static class Mana
    {
        /// <summary>Mana regained at the end of each turn.</summary>
        public const int PerTurn = 1;

        public static int ShieldCost(RunState run, HeroClassDefinition heroClass) => Math.Max(1, heroClass.ShieldCost);

        /// <summary>The class's price less any cut from talents and gear, never below one.</summary>
        public static int DashCost(RunState run, HeroClassDefinition heroClass) => Math.Max(1, heroClass.DashCost - run.DashCostCut);

        public static bool CanPay(HeroState hero, int cost) => hero.Mana >= cost;

        public static void Spend(HeroState hero, int cost) => hero.Mana = Math.Max(0, hero.Mana - cost);

        public static void Regain(HeroState hero) => hero.Mana = Math.Min(hero.MaxMana, hero.Mana + PerTurn);

        public static void Refill(HeroState hero) => hero.Mana = hero.MaxMana;
    }
}
