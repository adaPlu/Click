using System.Collections.Generic;
using ClickDungeon.Content;
using ClickDungeon.Domain;

namespace ClickDungeon.Simulation
{
    /// <summary>
    /// The class talents that change how a slash or a shield plays out (D-037, rules §14). Each reads a perk the run started
    /// with; with no talents learned every method here does nothing.
    /// </summary>
    public static class Talents
    {
        /// <summary>The slash's damage against this enemy, with Opening Strike, Executioner, Judgement and Dawnstrike.</summary>
        public static int SlashDamage(RunState run, EnemyState enemy, ContentCatalog catalog)
        {
            int damage = run.Hero.SlashDamage;
            if (enemy.Hp >= enemy.MaxHp) damage += run.Perk(TalentEffect.OpeningStrike);
            if (enemy.Hp <= 2) damage += run.Perk(TalentEffect.Executioner);
            if (enemy.Staggered) damage += run.Perk(TalentEffect.Judgement);
            if (catalog.Enemy(enemy.DefId).IsBoss) damage += run.Perk(TalentEffect.Dawnstrike);
            return damage;
        }

        /// <summary>Cleave hits the target's neighbours beside the hero; Relentless and Wrath of Dawn answer a kill.</summary>
        public static void AfterSlash(RunState run, EnemyState target, ContentCatalog catalog, List<GameEvent> events)
        {
            var hero = run.Hero;
            if (run.Perk(TalentEffect.Cleave) > 0)
                foreach (var other in Neighbours(run, target))
                    Combat.DamageEnemy(run, other, run.Perk(TalentEffect.Cleave), "cleave", catalog, events);

            if (target.Hp > 0) return;
            if (run.Perk(TalentEffect.Relentless) > 0)
            {
                int before = hero.Hp;
                hero.Hp = System.Math.Min(hero.MaxHp, hero.Hp + run.Perk(TalentEffect.Relentless));
                hero.Mana = System.Math.Min(hero.MaxMana, hero.Mana + 2 * run.Perk(TalentEffect.Relentless));
                if (hero.Hp > before) events.Add(GameEvent.Of(GameEventKind.HeroHealed, to: hero.Pos, amount: hero.Hp - before, source: "relentless"));
            }
            if (run.Perk(TalentEffect.WrathOfDawn) > 0)
                foreach (var other in Neighbours(run, target))
                {
                    if (catalog.Enemy(other.DefId).IsBoss || other.Hp <= 0) continue;
                    other.Staggered = true;
                    events.Add(GameEvent.Of(GameEventKind.EnemyStaggered, other.Id, to: other.Pos, subject: other.DefId));
                }
        }

        /// <summary>Consecrate burns every awake enemy beside the hero; Bastion mends.</summary>
        public static void AfterShield(RunState run, ContentCatalog catalog, List<GameEvent> events)
        {
            var hero = run.Hero;
            if (run.Perk(TalentEffect.Consecrate) > 0)
                foreach (var enemy in Neighbours(run, null))
                    Combat.DamageEnemy(run, enemy, run.Perk(TalentEffect.Consecrate), "consecrate", catalog, events);
            int mend = run.Perk(TalentEffect.Bastion);
            if (mend > 0 && hero.Hp < hero.MaxHp)
            {
                int before = hero.Hp;
                hero.Hp = System.Math.Min(hero.MaxHp, hero.Hp + mend);
                events.Add(GameEvent.Of(GameEventKind.HeroHealed, to: hero.Pos, amount: hero.Hp - before, source: "bastion"));
            }
        }

        /// <summary>Awake enemies next to the hero, other than <paramref name="except"/>.</summary>
        static List<EnemyState> Neighbours(RunState run, EnemyState except)
        {
            var list = new List<EnemyState>();
            foreach (var enemy in run.Floor.Enemies)
                if (enemy != except && enemy.Awake && enemy.Hp > 0 && enemy.Pos.IsAdjacent(run.Hero.Pos)) list.Add(enemy);
            return list;
        }
    }
}
