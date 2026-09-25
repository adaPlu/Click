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
        /// <summary>A far shot (D-063): Longshot's bonus and Pinning Shot's stagger need the target this many tiles away.</summary>
        public const int FarShot = 3;

        /// <summary>
        /// The slash's damage against this enemy: Opening Strike, Executioner, Judgement and Dawnstrike (D-037), and the
        /// new classes' rules (D-063) - Ambush, Longshot, Rage and Bloodlust.
        /// </summary>
        public static int SlashDamage(RunState run, EnemyState enemy, ContentCatalog catalog)
        {
            var hero = run.Hero;
            int damage = hero.SlashDamage;
            if (enemy.Hp >= enemy.MaxHp) damage += run.Perk(TalentEffect.OpeningStrike);
            if (enemy.Hp <= 2) damage += run.Perk(TalentEffect.Executioner);
            // A stagger lasts exactly as long as the turn that caused it: EnemyAi.Declare clears the flag in step 9 and turns
            // it into Intent.Recover, which is the "does nothing next turn, free hit" the player is shown. Judgement has to read
            // that durable state, or it can never fire on the player's turn (REL-23).
            if (enemy.Staggered || enemy.Intent.Kind == IntentKind.Recover) damage += run.Perk(TalentEffect.Judgement);
            var def = catalog.Enemy(enemy.DefId);
            if (def.IsBoss) damage += run.Perk(TalentEffect.Dawnstrike);
            // Holy Wrath (D-072): the Paladin hits what is risen harder. A boss can be both, and they stack.
            if (def.Undead) damage += run.Perk(TalentEffect.HolyWrath);
            if (IsAmbush(run, enemy, catalog)) damage += run.Perk(TalentEffect.Ambush);
            if (hero.Pos.Chebyshev(enemy.Pos) >= FarShot) damage += run.Perk(TalentEffect.Longshot);
            int rage = run.Perk(TalentEffect.Rage);
            if (rage > 0) damage += (hero.MaxHp - hero.Hp) / rage;
            if (hero.Hp * 2 <= hero.MaxHp) damage += run.Perk(TalentEffect.Bloodlust);
            return damage;
        }

        /// <summary>
        /// Ambush (D-063): the Rogue strikes an opening - a monster whose declared action is not aimed at the hero's tile.
        /// It reads the same telegraph the player sees, so an ambush is always visible before it is taken.
        /// </summary>
        public static bool IsAmbush(RunState run, EnemyState enemy, ContentCatalog catalog)
        {
            if (run.Perk(TalentEffect.Ambush) <= 0) return false;
            foreach (var threat in Threats.Compute(run, catalog))
                if (threat.SourceId == enemy.Id && threat.Cell == run.Hero.Pos) return false;
            return true;
        }

        /// <summary>
        /// What follows a slash. Cleave hits the hero's other neighbours; Relentless and Wrath of Dawn answer a kill (D-037).
        /// The new classes (D-063): a Fireball scorches the target's neighbours and a Piercing Arrow the monster behind it; a
        /// survivor may be staggered (Eviscerate on an ambush, Pinning Shot from afar) or knocked back a tile (the Wizard's
        /// Firebolt); a kill may be picked clean (Pickpocket). Bosses are never staggered or moved.
        /// </summary>
        public static void AfterSlash(RunState run, EnemyState target, ContentCatalog catalog, List<GameEvent> events, bool ambush = false)
        {
            var hero = run.Hero;
            var floor = run.Floor;
            bool boss = catalog.Enemy(target.DefId).IsBoss;
            int distance = hero.Pos.Chebyshev(target.Pos);
            Directions.TryStepFromDelta(target.Pos.X - hero.Pos.X, target.Pos.Y - hero.Pos.Y, out var step);

            if (run.Perk(TalentEffect.Cleave) > 0)
                foreach (var other in Neighbours(run, target))
                    Combat.DamageEnemy(run, other, run.Perk(TalentEffect.Cleave), "cleave", catalog, events);
            if (run.Perk(TalentEffect.Fireball) > 0)
                foreach (var other in floor.Enemies.ToArray())
                    if (other != target && other.Awake && other.Hp > 0 && other.Pos.IsAdjacent(target.Pos))
                        Combat.DamageEnemy(run, other, run.Perk(TalentEffect.Fireball), "fireball", catalog, events);
            if (run.Perk(TalentEffect.PiercingArrow) > 0)
            {
                var behind = floor.EnemyAt(target.Pos.Offset(step));
                if (behind != null && behind.Awake && behind.Hp > 0)
                    Combat.DamageEnemy(run, behind, run.Perk(TalentEffect.PiercingArrow), "piercing", catalog, events);
            }

            if (target.Hp > 0)
            {
                if (boss) return;
                if ((ambush && run.Perk(TalentEffect.Eviscerate) > 0) || (distance >= FarShot && run.Perk(TalentEffect.PinningShot) > 0))
                    Stagger(target, events);
                // Knocked back a tile, onto an uncovered tile only, so the push never tells what a cover hides.
                if (run.Perk(TalentEffect.Knockback) > 0)
                {
                    var to = target.Pos.Offset(step);
                    if (to.InBounds && floor[to].Knowledge == Knowledge.Revealed && Board.EnemyCanEnter(run, to))
                    {
                        var from = target.Pos;
                        target.Pos = to;
                        events.Add(GameEvent.Of(GameEventKind.EnemyKnockedBack, target.Id, from, to, source: target.DefId));
                        // REL-37: nothing needs undoing here. A declared lane or charge is anchored at the tile it was
                        // declared from (Intent.Fire/Charge), so being shoved moves the monster without moving the line
                        // the board already drew - and it still pays its winded turn afterwards.
                    }
                }
                return;
            }

            if (run.Perk(TalentEffect.Relentless) > 0)
            {
                int before = hero.Hp;
                hero.Hp = System.Math.Min(hero.MaxHp, hero.Hp + run.Perk(TalentEffect.Relentless));
                hero.Mana = System.Math.Min(hero.MaxMana, hero.Mana + 2 * run.Perk(TalentEffect.Relentless));
                if (hero.Hp > before) events.Add(GameEvent.Of(GameEventKind.HeroHealed, to: hero.Pos, amount: hero.Hp - before, source: "relentless"));
            }
            if (run.Perk(TalentEffect.WrathOfDawn) > 0)
                foreach (var other in Neighbours(run, target))
                    if (!catalog.Enemy(other.DefId).IsBoss && other.Hp > 0) Stagger(other, events);
            if (run.Perk(TalentEffect.Pickpocket) > 0) Treasure.Coins(run, run.Perk(TalentEffect.Pickpocket), target.Pos, events);
        }

        static void Stagger(EnemyState enemy, List<GameEvent> events)
        {
            enemy.Staggered = true;
            events.Add(GameEvent.Of(GameEventKind.EnemyStaggered, enemy.Id, to: enemy.Pos, subject: enemy.DefId));
        }

        /// <summary>
        /// The Engineer's Spark Drone (D-063): on every turn the hero does not slash - a step, a shield, a potion, a wait -
        /// it zaps an awake monster within reach, the weakest first and then the lowest id, so every replay picks the same.
        /// Arc Chain zaps them all. It covers the turns the hero spends on something else; it does not add to a slash.
        /// </summary>
        public static void Drone(RunState run, EnemyState slashed, ContentCatalog catalog, List<GameEvent> events)
        {
            int zap = run.Perk(TalentEffect.Drone);
            if (zap <= 0 || slashed != null) return;
            int reach = 1 + run.Perk(TalentEffect.DroneRange);
            var targets = new List<EnemyState>();
            foreach (var enemy in run.Floor.Enemies)
                if (enemy.Awake && enemy.Hp > 0 && enemy.Pos.Chebyshev(run.Hero.Pos) <= reach) targets.Add(enemy);
            if (targets.Count == 0) return;
            targets.Sort((a, b) => a.Hp != b.Hp ? a.Hp.CompareTo(b.Hp) : a.Id.CompareTo(b.Id));
            int count = run.Perk(TalentEffect.ArcChain) > 0 ? targets.Count : 1;
            for (int i = 0; i < count; i++)
            {
                var enemy = targets[i];
                events.Add(GameEvent.Of(GameEventKind.DroneZapped, enemy.Id, run.Hero.Pos, enemy.Pos, zap, enemy.DefId));
                Combat.DamageEnemy(run, enemy, zap, "drone", catalog, events);
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
