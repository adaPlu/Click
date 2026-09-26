using System.Collections.Generic;
using ClickDungeon.Content;
using ClickDungeon.Domain;

namespace ClickDungeon.Simulation
{
    /// <summary>
    /// The usable skills (D-075, rules §14.2). Every one of the ninety-six talents is passive: the game had no verb for
    /// "do a thing now", so a class's whole identity had to be expressed as modifiers to a slash or a shield. A skill is
    /// that verb. It costs a turn and some mana, it is unlocked by learning the talent that names it, and the hero
    /// carries at most <see cref="ContentCatalog.SkillSlots"/> of them.
    ///
    /// Nothing here may break the two contracts the rest of the game is built on: a skill never reads a tile the player
    /// has not uncovered (rules §2.1, which is why a targeted skill only reaches an AWAKE monster), and a skill is the
    /// hero's own action on their own turn, so it telegraphs nothing and nothing has to be warned about it (§3.2).
    /// </summary>
    public static class Skills
    {
        /// <summary>The skill in this slot, or null when the slot is empty or holds something this class cannot use.</summary>
        public static SkillDefinition InSlot(RunState run, ContentCatalog catalog, int slot)
        {
            if (run?.Skills == null || slot < 0 || slot >= run.Skills.Count) return null;
            var skill = catalog.SkillOrNull(run.Skills[slot]);
            return skill != null && skill.ClassId == run.Hero.ClassId ? skill : null;
        }

        /// <summary>
        /// Whether this skill can be used right now, and why not. The single place that decides it, so the button, the
        /// resolver and the bot can never disagree - the same rule <see cref="Commands"/> exists for.
        /// </summary>
        public static bool CanUse(RunState run, ContentCatalog catalog, int slot, GridPos target, out string reason)
        {
            reason = null;
            var skill = InSlot(run, catalog, slot);
            if (skill == null) return Fail(out reason, "No skill in that slot.");
            var hero = run.Hero;
            if (!Mana.CanPay(hero, skill.ManaCost))
                return Fail(out reason, $"Not enough mana for {skill.Name} ({hero.Mana}/{skill.ManaCost}).");

            switch (skill.Target)
            {
                case SkillTarget.Self:
                    // The only rule a self-cast has is "do not spend a turn on nothing".
                    if (skill.Effect == SkillEffect.Heal && hero.Hp >= hero.MaxHp)
                        return Fail(out reason, "Already at full health.");
                    if (skill.Effect == SkillEffect.Burst && !AnyAwakeNeighbour(run))
                        return Fail(out reason, "Nothing next to you to hit.");
                    return true;

                case SkillTarget.Enemy:
                {
                    var enemy = run.Floor.EnemyAt(target);
                    // Awake, exactly as a slash requires: aiming at a monster the player has not found would read a
                    // tile they have not uncovered, and nothing may differ on what a cover hides (rules §2.1).
                    if (enemy == null || !enemy.Awake) return Fail(out reason, "Nothing to aim at there.");
                    if (hero.Pos.Chebyshev(target) > skill.Range)
                        return Fail(out reason, $"{skill.Name} reaches {skill.Range} tile{(skill.Range == 1 ? "" : "s")}.");
                    return true;
                }
            }
            return Fail(out reason, "Unknown skill.");
        }

        /// <summary>
        /// Runs the skill. The caller has already validated it; this spends the mana and does the thing.
        /// </summary>
        public static void Use(RunState run, ContentCatalog catalog, int slot, GridPos target, List<GameEvent> events)
        {
            var skill = InSlot(run, catalog, slot);
            if (skill == null) return;
            var hero = run.Hero;
            Mana.Spend(hero, skill.ManaCost);

            switch (skill.Effect)
            {
                case SkillEffect.Heal:
                {
                    int before = hero.Hp;
                    hero.Hp = System.Math.Min(hero.MaxHp, hero.Hp + skill.Amount);
                    int mended = hero.Hp - before;
                    events.Add(GameEvent.Of(GameEventKind.SkillUsed, to: hero.Pos, amount: mended, source: skill.Id));
                    if (mended > 0)
                        events.Add(GameEvent.Of(GameEventKind.HeroHealed, to: hero.Pos, amount: mended, source: skill.Id));
                    break;
                }

                case SkillEffect.Banish:
                case SkillEffect.Strike:
                {
                    var enemy = run.Floor.EnemyAt(target);
                    if (enemy == null) return;
                    // Double against the risen (D-072): the tag exists so that "an answer to the undead" can name
                    // something, and Dispel is the skill that was the reason for naming it.
                    int damage = skill.Effect == SkillEffect.Banish && catalog.Enemy(enemy.DefId).Undead
                        ? skill.Amount * 2
                        : skill.Amount;
                    events.Add(GameEvent.Of(GameEventKind.SkillUsed, enemy.Id, hero.Pos, target, damage, skill.Id));
                    Combat.DamageEnemy(run, enemy, damage, skill.Id, catalog, events);
                    break;
                }

                case SkillEffect.Burst:
                {
                    events.Add(GameEvent.Of(GameEventKind.SkillUsed, to: hero.Pos, amount: skill.Amount, source: skill.Id));
                    // A copy, because a death removes it from the list while this is walking it.
                    foreach (var enemy in run.Floor.Enemies.ToArray())
                        if (enemy.Awake && enemy.Hp > 0 && enemy.Pos.IsAdjacent(hero.Pos))
                            Combat.DamageEnemy(run, enemy, skill.Amount, skill.Id, catalog, events);
                    break;
                }

                case SkillEffect.Stagger:
                {
                    var enemy = run.Floor.EnemyAt(target);
                    if (enemy == null) return;
                    events.Add(GameEvent.Of(GameEventKind.SkillUsed, enemy.Id, hero.Pos, target, 0, skill.Id));
                    // A boss is never staggered, exactly as the talents that stagger cannot stagger one (D-037).
                    if (catalog.Enemy(enemy.DefId).IsBoss) break;
                    enemy.Staggered = true;
                    events.Add(GameEvent.Of(GameEventKind.EnemyStaggered, enemy.Id, to: enemy.Pos, subject: enemy.DefId));
                    break;
                }

            }
        }

        static bool AnyAwakeNeighbour(RunState run)
        {
            foreach (var enemy in run.Floor.Enemies)
                if (enemy.Awake && enemy.Hp > 0 && enemy.Pos.IsAdjacent(run.Hero.Pos)) return true;
            return false;
        }

        static bool Fail(out string reason, string why)
        {
            reason = why;
            return false;
        }
    }
}
