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
                // Sanctuary (D-063): the Cleric's blocks heal.
                int mend = run.Perk(TalentEffect.Sanctuary);
                if (mend > 0 && hero.Hp < hero.MaxHp)
                {
                    int before = hero.Hp;
                    hero.Hp = Math.Min(hero.MaxHp, hero.Hp + mend);
                    events.Add(GameEvent.Of(GameEventKind.HeroHealed, to: hero.Pos, amount: hero.Hp - before, source: "sanctuary"));
                }
                return true;
            }
            // Dodge (D-063): once per floor, the first blow that would land is turned aside.
            if (run.Perk(TalentEffect.Dodge) > 0 && !hero.DodgeSpent)
            {
                hero.DodgeSpent = true;
                events.Add(GameEvent.Of(GameEventKind.HeroDodged, to: hero.Pos, amount: amount, source: source));
                return false;
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
            // The Goblin Brute King enrages at half his hearts (D-062): from then on every blow is one harder.
            if (enemy.Hp > 0 && enemy.Mode == EnemyMode.Normal && enemy.Hp * 2 <= enemy.MaxHp && catalog.Enemy(enemy.DefId).EnragesAtHalf)
            {
                enemy.Mode = EnemyMode.Enraged;
                events.Add(GameEvent.Of(GameEventKind.BossEnraged, enemy.Id, to: enemy.Pos, source: enemy.DefId));
            }
        }

        /// <summary>
        /// A fallen Key Warden's key lands where it fell (D-061) - or, if that tile already holds something, on the nearest
        /// empty floor tile that is not a hazard or the exit, so the key is never lost under a potion or a chest.
        /// </summary>
        static void DropKey(RunState run, GridPos at, List<GameEvent> events)
        {
            var floor = run.Floor;
            bool Clear(GridPos p) => p.InBounds && floor[p].Terrain == Terrain.Floor && floor[p].Hazard == HazardKind.None
                                     && floor[p].Content == ContentKind.None && !floor[p].IsExit;
            var spot = GridPos.Invalid;
            if (Clear(at)) spot = at;
            else
            {
                int best = int.MaxValue;
                foreach (var p in Board.AllCells)
                    if (Clear(p) && p.Chebyshev(at) < best) { best = p.Chebyshev(at); spot = p; }
            }
            if (!spot.InBounds) { run.Hero.HasKey = true; return; }   // nowhere to drop it: it is simply taken
            floor[spot].Content = ContentKind.Key;
            floor[spot].Knowledge = Knowledge.Revealed;
            events.Add(GameEvent.Of(GameEventKind.KeyDropped, to: spot));
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
                if (enemy.CarriesKey) DropKey(run, enemy.Pos, events);
                if (fallen.LootCoins > 0) Treasure.Coins(run, fallen.LootCoins, enemy.Pos, events);
                bool boss = catalog.Enemy(enemy.DefId).IsBoss;
                run.XpEarned += boss ? catalog.Xp.ForTheBoss : catalog.Xp.PerMonster;
                run.MonstersSlain++;
                if (boss) bossDied = true;
            }

            if (bossDied)
            {
                // A boss closes an act (D-062): beating one restores every heart and all mana. Over twenty floors the harder
                // tiers never heal between floors, and without this a player who had just won the fight walked into the
                // next act on their last heart and died there - the floor after each boss was as deadly as the boss.
                var hero = run.Hero;
                int restored = hero.MaxHp - hero.Hp;
                hero.Hp = hero.MaxHp;
                Mana.Refill(hero);
                if (restored > 0) events.Add(GameEvent.Of(GameEventKind.HeroHealed, to: hero.Pos, amount: restored, source: "act_cleared"));
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
