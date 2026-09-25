using System.Collections.Generic;
using System.Linq;
using ClickDungeon.Application;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using NUnit.Framework;
using static ClickDungeon.Tests.Scenario;

namespace ClickDungeon.Tests
{
    /// <summary>
    /// TEST-18: what each talent is wired to. Every behaviour test writes the perk it wants into the run by hand, so
    /// nothing ever asked the catalog what a talent actually does - Overclock could have been wired to the drone's
    /// reach instead of its damage, or Pickpocket shipped with an amount of 1 against its "5 coins" text, and the whole
    /// suite would still have been green. This is the one place the wiring itself is pinned.
    /// </summary>
    public class TalentWiringTests
    {
        static readonly (string id, TalentEffect effect, int amount, int ranks)[] Wiring =
        {
            ("k_opening_strike", TalentEffect.OpeningStrike, 1, 3),
            ("k_cleave", TalentEffect.Cleave, 1, 1),
            ("k_executioner", TalentEffect.Executioner, 1, 2),
            ("k_relentless", TalentEffect.Relentless, 1, 1),
            ("k_sturdy", TalentEffect.MaxHearts, 1, 3),
            ("k_shield_wall", TalentEffect.ShieldCostCut, 1, 1),
            ("k_riposte", TalentEffect.Riposte, 1, 2),
            ("k_bastion", TalentEffect.Bastion, 1, 1),
            ("k_light_step", TalentEffect.DashCostCut, 1, 2),
            ("k_treasure_sense", TalentEffect.ChestTapCut, 1, 1),
            ("k_fortune", TalentEffect.CoinsPerChestReward, 3, 2),
            ("k_second_wind", TalentEffect.SecondWind, 3, 1),
            ("p_holy_wrath", TalentEffect.HolyWrath, 1, 3),
            ("p_consecrate", TalentEffect.Consecrate, 1, 1),
            ("p_dawnstrike", TalentEffect.Dawnstrike, 1, 2),
            ("p_wrath_of_dawn", TalentEffect.WrathOfDawn, 1, 1),
            ("p_plated", TalentEffect.MaxHearts, 1, 3),
            ("p_holy_bulwark", TalentEffect.HolyBulwark, 2, 1),
            ("p_unyielding", TalentEffect.Unyielding, 1, 1),
            ("p_divine_shield", TalentEffect.DivineShield, 3, 1),
            ("p_blessed_draught", TalentEffect.PotionHeal, 1, 3),
            ("p_prayer", TalentEffect.Prayer, 1, 1),
            ("p_guiding_light", TalentEffect.GuidingLight, 1, 1),
            ("p_sanctified", TalentEffect.Sanctified, 1, 1),
            ("ro_cruel_edge", TalentEffect.Ambush, 1, 3),
            ("ro_twin_fangs", TalentEffect.Cleave, 1, 1),
            ("ro_coup_de_grace", TalentEffect.Executioner, 1, 2),
            ("ro_eviscerate", TalentEffect.Eviscerate, 1, 1),
            ("ro_supple_leathers", TalentEffect.MaxHearts, 1, 3),
            ("ro_light_feet", TalentEffect.DashCostCut, 1, 1),
            ("ro_slippery", TalentEffect.Dodge, 1, 1),
            ("ro_vanishing_act", TalentEffect.SecondWind, 3, 1),
            ("ro_fence", TalentEffect.CoinsPerChestReward, 3, 2),
            ("ro_lockpick", TalentEffect.ChestTapCut, 1, 1),
            ("ro_casing_the_joint", TalentEffect.GuidingLight, 1, 1),
            ("ro_pickpocket", TalentEffect.Pickpocket, 5, 1),
            ("w_searing_bolt", TalentEffect.OpeningStrike, 1, 3),
            ("w_fireball", TalentEffect.Fireball, 1, 1),
            ("w_cinders", TalentEffect.Executioner, 1, 2),
            ("w_blinding_flash", TalentEffect.WrathOfDawn, 1, 1),
            ("w_deep_well", TalentEffect.MaxMana, 1, 3),
            ("w_meditation", TalentEffect.Prayer, 1, 1),
            ("w_soul_siphon", TalentEffect.Relentless, 1, 1),
            ("w_alchemy", TalentEffect.Sanctified, 1, 1),
            ("w_warded_robes", TalentEffect.MaxHearts, 1, 3),
            ("w_quick_ward", TalentEffect.ShieldCostCut, 1, 1),
            ("w_flame_ward", TalentEffect.Consecrate, 1, 1),
            ("w_phoenix_feather", TalentEffect.DivineShield, 3, 1),
            ("r_aimed_shot", TalentEffect.OpeningStrike, 1, 3),
            ("r_piercing_arrow", TalentEffect.PiercingArrow, 1, 1),
            ("r_kill_shot", TalentEffect.Executioner, 1, 2),
            ("r_pinning_shot", TalentEffect.PinningShot, 1, 1),
            ("r_rangers_leathers", TalentEffect.MaxHearts, 1, 3),
            ("r_herbalism", TalentEffect.PotionHeal, 1, 1),
            ("r_endurance", TalentEffect.Unyielding, 1, 1),
            ("r_second_wind", TalentEffect.SecondWind, 3, 1),
            ("r_fleet_foot", TalentEffect.DashCostCut, 1, 2),
            ("r_tracker", TalentEffect.GuidingLight, 1, 1),
            ("r_scavenger", TalentEffect.CoinsPerChestReward, 3, 2),
            ("r_hawkeye", TalentEffect.Hawkeye, 1, 1),
            ("c_blessed_draught", TalentEffect.PotionHeal, 1, 3),
            ("c_prayer", TalentEffect.Prayer, 1, 1),
            ("c_renewal", TalentEffect.SecondWind, 3, 1),
            ("c_holy_water", TalentEffect.Sanctified, 1, 1),
            ("c_faith", TalentEffect.MaxHearts, 1, 3),
            ("c_swift_grace", TalentEffect.ShieldCostCut, 1, 1),
            ("c_blessed_ward", TalentEffect.Sanctuary, 1, 1),
            ("c_miracle", TalentEffect.DivineShield, 3, 1),
            ("c_rebuke", TalentEffect.Judgement, 1, 3),
            ("c_holy_light", TalentEffect.Consecrate, 1, 1),
            ("c_smite", TalentEffect.Dawnstrike, 1, 2),
            ("c_radiance", TalentEffect.Bastion, 1, 1),
            ("b_bloodlust", TalentEffect.Bloodlust, 1, 3),
            ("b_wide_swing", TalentEffect.Cleave, 1, 1),
            ("b_brutal_finish", TalentEffect.Executioner, 1, 2),
            ("b_blood_frenzy", TalentEffect.Relentless, 1, 1),
            ("b_thick_hide", TalentEffect.MaxHearts, 1, 3),
            ("b_iron_gut", TalentEffect.PotionHeal, 2, 1),
            ("b_pain_is_progress", TalentEffect.Unyielding, 1, 1),
            ("b_undying_rage", TalentEffect.DivineShield, 3, 1),
            ("b_headlong", TalentEffect.DashCostCut, 1, 2),
            ("b_smash_open", TalentEffect.ChestTapCut, 1, 1),
            ("b_plunder", TalentEffect.CoinsPerChestReward, 3, 2),
            ("b_war_cry", TalentEffect.WrathOfDawn, 1, 1),
            ("e_calibrated_wrench", TalentEffect.OpeningStrike, 1, 3),
            ("e_overclock", TalentEffect.Drone, 1, 1),
            ("e_long_range_coil", TalentEffect.DroneRange, 1, 1),
            ("e_tesla_coil", TalentEffect.ArcChain, 1, 1),
            ("e_riveted_plating", TalentEffect.MaxHearts, 1, 3),
            ("e_quick_deploy", TalentEffect.ShieldCostCut, 1, 1),
            ("e_shock_plating", TalentEffect.Riposte, 1, 2),
            ("e_static_field", TalentEffect.Consecrate, 1, 1),
            ("e_salvage", TalentEffect.CoinsPerChestReward, 3, 2),
            ("e_lockpicks", TalentEffect.ChestTapCut, 1, 1),
            ("e_survey_drone", TalentEffect.Hawkeye, 1, 1),
            ("e_field_repairs", TalentEffect.SecondWind, 3, 1),
        };

        [Test]
        public void EveryTalentIsWiredToTheEffectAndAmountItPromises()
        {
            Assert.That(Catalog.Talents.Count, Is.EqualTo(Wiring.Length), "A talent was added or removed; pin it here too.");
            foreach (var (id, effect, amount, ranks) in Wiring)
            {
                var talent = Catalog.Talent(id);
                Assert.That(talent, Is.Not.Null, id);
                Assert.That(talent.Effect, Is.EqualTo(effect), id);
                Assert.That(talent.Amount, Is.EqualTo(amount), id);
                Assert.That(talent.MaxRank, Is.EqualTo(ranks), id);
            }
        }

        [Test]
        public void EveryTalentsTextAgreesWithItsNumber()
        {
            // A per-rank line that quotes a number must quote the number the simulation will use.
            foreach (var talent in Catalog.Talents)
            {
                if (talent.Amount == 1) continue;
                Assert.That(talent.PerRank, Does.Contain(talent.Amount.ToString()),
                    $"{talent.Id} grants {talent.Amount} but its text does not say so: \"{talent.PerRank}\"");
            }
        }

        [Test]
        public void EveryTalentReachesTheRunThroughProgression()
        {
            // Learn every talent of every class and check the perk or starting number actually arrives - the path a
            // player takes, not the dictionary write the other tests use.
            foreach (var heroClass in Catalog.HeroClasses.Values)
            {
                var profile = new ProfileState { Xp = Progression.XpForLevel(60) };
                foreach (var talent in Catalog.TalentsOf(heroClass.Id).OrderBy(t => t.Tier))
                    for (int rank = 0; rank < talent.MaxRank; rank++)
                        Progression.TryLearn(profile, Catalog, talent.Id);

                string heroId = Catalog.HeroIdentities.Values.First(h => h.ClassId == heroClass.Id).Id;
                var run = RunFactory.NewRun(5UL, Catalog, new List<GameEvent>(), heroId);
                var bare = RunFactory.NewRun(5UL, Catalog, new List<GameEvent>(), heroId);
                Progression.Apply(profile, run, Catalog);

                int learned = 0;
                foreach (var talent in Catalog.TalentsOf(heroClass.Id))
                {
                    int rank = Progression.Rank(profile, talent.Id);
                    if (rank <= 0) continue;
                    learned++;
                    bool arrived;
                    switch (talent.Effect)
                    {
                        case TalentEffect.MaxHearts: arrived = run.Hero.MaxHp > bare.Hero.MaxHp; break;
                        case TalentEffect.MaxMana: arrived = run.Hero.MaxMana > bare.Hero.MaxMana; break;
                        case TalentEffect.DashCostCut: arrived = run.DashCostCut > 0; break;
                        case TalentEffect.PotionHeal: arrived = run.PotionHealBonus > 0; break;
                        case TalentEffect.CoinsPerChestReward: arrived = run.BonusCoinsPerChestReward > 0; break;
                        default: arrived = run.Perk(talent.Effect) > bare.Perk(talent.Effect); break;
                    }
                    Assert.That(arrived, Is.True, $"{talent.Id} was learned but never reached the run.");
                }
                Assert.That(learned, Is.GreaterThanOrEqualTo(10), heroClass.Id);
            }
        }
    }
}
