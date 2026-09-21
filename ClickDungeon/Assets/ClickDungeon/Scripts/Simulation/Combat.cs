using System;
using System.Collections.Generic;
using ClickDungeon.Content;
using ClickDungeon.Domain;

namespace ClickDungeon.Simulation
{
    public static class Combat
    {
        /// <summary>
        /// Applies damage to the hero. Returns true when Guard blocked it. <paramref name="attacker"/> is whoever threw
        /// the blow, where there is one — a hazard has none — so that a block can be answered wherever it happens.
        /// </summary>
        public static bool DamageHero(RunState run, int amount, string source, List<GameEvent> events,
            bool blockable = true, EnemyState attacker = null, ContentCatalog catalog = null)
        {
            var hero = run.Hero;
            if (amount <= 0 || hero.Hp <= 0) return false;
            if (blockable && hero.Guard)
            {
                events.Add(GameEvent.Of(GameEventKind.HeroBlocked, to: hero.Pos, amount: amount, source: source));
                // Holy Bulwark (D-037): a block gives mana back.
                int mana = run.Perk(TalentEffect.HolyBulwark);
                if (mana > 0) hero.Mana = Math.Min(hero.MaxMana, hero.Mana + mana);
                // Riposte (D-037, D-052): a blocked blow is answered. It lives here beside Holy Bulwark so the two
                // talents keep the promise they are both written with — every block, not only the ones thrown by a
                // neighbour. A hazard passes no attacker, so a blocked bomb answers nobody.
                int riposte = run.Perk(TalentEffect.Riposte);
                if (riposte > 0 && attacker != null && catalog != null)
                    DamageEnemy(run, attacker, riposte, "riposte", catalog, events);
                return true;
            }
            // Unyielding (D-037): at half hearts or fewer, every hit is softened, never below 1.
            int soften = run.Perk(TalentEffect.Unyielding);
            if (soften > 0 && hero.Hp * 2 <= hero.MaxHp) amount = Math.Max(1, amount - soften);
            hero.Hp = Math.Max(0, hero.Hp - amount);
            events.Add(GameEvent.Of(GameEventKind.HeroDamaged, to: hero.Pos, amount: amount, source: source));
            // Divine Shield (D-037): once per floor, the last heart holds.
            int ward = run.Perk(TalentEffect.DivineShield);
            if (hero.Hp == 0 && ward > 0 && !hero.WardSpent)
            {
                hero.WardSpent = true;
                hero.Hp = Math.Min(hero.MaxHp, 1 + ward);
                events.Add(GameEvent.Of(GameEventKind.HeroHealed, to: hero.Pos, amount: hero.Hp, source: "divine_shield"));
            }
            return false;
        }

        public static void DamageEnemy(RunState run, EnemyState enemy, int amount, string source, ContentCatalog catalog,
            List<GameEvent> events)
        {
            if (amount <= 0 || enemy.Hp <= 0) return;
            if (enemy.Mode == EnemyMode.Puffed)
            {
                events.Add(GameEvent.Of(GameEventKind.EnemyImmune, enemy.Id, to: enemy.Pos, source: source, subject: enemy.DefId));
                return;
            }
            if (enemy.Mode == EnemyMode.Deflated) amount *= catalog.Enemy(enemy.DefId).DeflatedDamageMultiplier;
            enemy.Hp = Math.Max(0, enemy.Hp - amount);
            events.Add(GameEvent.Of(GameEventKind.EnemyDamaged, enemy.Id, to: enemy.Pos, amount: amount, source: source, subject: enemy.DefId));
        }

        /// <summary>Removes dead enemies, handles boss death, and ends the run if the hero fell.</summary>
        public static void ResolveDeaths(RunState run, ContentCatalog catalog, List<GameEvent> events)
        {
            var floor = run.Floor;
            bool bossDied = false;
            for (int i = 0; i < floor.Enemies.Count;)
            {
                var enemy = floor.Enemies[i];
                if (enemy.Hp > 0)
                {
                    i++;
                    continue;
                }
                // A Skeleton Warrior's first fall is not its last (D-058). It collapses into bones where it stood, and the
                // kill - with its experience - only counts once the bones are broken.
                var fallen = catalog.Enemy(enemy.DefId);
                if (fallen.Reassembles && !enemy.Rallied)
                {
                    enemy.Rallied = true;
                    enemy.Hp = 1;
                    enemy.Mode = EnemyMode.Bones;
                    enemy.ModeTurns = fallen.ReassembleTurns;
                    enemy.Intent = Intent.Reassemble();
                    enemy.Staggered = false;
                    events.Add(GameEvent.Of(GameEventKind.EnemyCollapsed, enemy.Id, to: enemy.Pos, source: enemy.DefId));
                    i++;
                    continue;
                }
                floor.Enemies.RemoveAt(i);
                events.Add(GameEvent.Of(GameEventKind.EnemyDied, enemy.Id, to: enemy.Pos, source: enemy.DefId));
                bool boss = catalog.Enemy(enemy.DefId).IsBoss;
                run.XpEarned += boss ? catalog.Xp.ForTheBoss : catalog.Xp.PerMonster;
                run.MonstersSlain++;
                if (boss) bossDied = true;
            }

            if (bossDied)
            {
                Treasure.Gems(run, catalog.Treasure.GemsForTheBoss, run.Hero.Pos, events);
                Treasure.Item(run, catalog, run.Hero.Pos, 3UL, catalog.Treasure.ItemChanceBoss, events);
                foreach (var minion in floor.Enemies)
                    events.Add(GameEvent.Of(GameEventKind.EnemyDied, minion.Id, to: minion.Pos, source: minion.DefId));
                floor.Enemies.Clear();
                if (!floor.ExitUnlocked)
                {
                    floor.ExitUnlocked = true;
                    events.Add(GameEvent.Of(GameEventKind.ExitUnlocked, to: floor.Exit));
                }
            }

            if (run.Hero.Hp <= 0 && run.Status == RunStatus.InProgress)
            {
                run.Status = RunStatus.Lost;
                events.Add(GameEvent.Of(GameEventKind.RunLost));
            }
        }
    }
}
