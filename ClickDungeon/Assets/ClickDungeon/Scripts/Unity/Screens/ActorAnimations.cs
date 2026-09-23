using System.Collections.Generic;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;

namespace ClickDungeon.Unity.Screens
{
    /// <summary>
    /// Chooses action animations from events the simulation already produced (art brief §8). Each token gets at most one
    /// animation per turn: the most important thing that happened to it. Presentation only; nothing here affects rules.
    /// </summary>
    public static class ActorAnimations
    {
        public const int HeroToken = -1;

        public struct Cue
        {
            public string ContentId;
            public string Animation;
            public int Priority;
        }

        public static Dictionary<int, Cue> Pick(RunState run, IReadOnlyList<GameEvent> events)
        {
            var cues = new Dictionary<int, Cue>();
            if (run == null || events == null) return cues;
            string hero = run.Hero.IdentityId;

            void Offer(int token, string contentId, string animation, int priority)
            {
                if (contentId == null) return;
                if (cues.TryGetValue(token, out var existing) && existing.Priority >= priority) return;
                cues[token] = new Cue { ContentId = contentId, Animation = animation, Priority = priority };
            }

            foreach (var e in events)
            {
                switch (e.Kind)
                {
                    case GameEventKind.HeroMoved: Offer(HeroToken, hero, "step", 1); break;
                    case GameEventKind.HeroDashed: Offer(HeroToken, hero, "dash", 3); break;
                    case GameEventKind.HeroSlashed: Offer(HeroToken, hero, "slash", 3); break;
                    case GameEventKind.HeroShielded: Offer(HeroToken, hero, "shield", 3); break;
                    case GameEventKind.HeroHealed: Offer(HeroToken, hero, "potion", 3); break;
                    case GameEventKind.HeroDamaged: Offer(HeroToken, hero, "hit", 5); break;
                    case GameEventKind.RunWon: Offer(HeroToken, hero, "victory", 9); break;
                    case GameEventKind.RunLost: Offer(HeroToken, hero, "defeat", 10); break;

                    case GameEventKind.EnemyWoke: Offer(e.ActorId, EnemyId(run, e), "wake", 2); break;
                    case GameEventKind.EnemyMoved: Offer(e.ActorId, EnemyId(run, e), "move", 1); break;
                    case GameEventKind.EnemyAttacked: Offer(e.ActorId, EnemyId(run, e), "attack", 4); break;
                    case GameEventKind.EnemyFired: Offer(e.ActorId, EnemyId(run, e), "fire", 4); break;
                    case GameEventKind.BossSlammed: Offer(e.ActorId, EnemyId(run, e), "slam", 4); break;
                    case GameEventKind.BossPuffed: Offer(e.ActorId, EnemyId(run, e), "puffup", 4); break;
                    case GameEventKind.BossDeflated: Offer(e.ActorId, EnemyId(run, e), "deflate", 4); break;
                    case GameEventKind.EnemySummoned:
                        // ActorId is the new minion; From is where the summoner stands.
                        Offer(e.ActorId, e.Source, "spawn", 2);
                        var summoner = run.Floor.EnemyAt(e.From);
                        if (summoner != null) Offer(summoner.Id, summoner.DefId, "summon", 4);
                        break;
                    case GameEventKind.EnemyDamaged: Offer(e.ActorId, EnemyId(run, e), "hit", 5); break;
                    case GameEventKind.EnemyImmune: Offer(e.ActorId, EnemyId(run, e), "immune", 5); break;
                    case GameEventKind.EnemyDied: Offer(e.ActorId, EnemyId(run, e), "defeat", 10); break;
                }
            }
            return cues;
        }

        /// <summary>Animation names to try in order: a specific animation falls back to a similar general one.</summary>
        public static string[] Chain(string animation)
        {
            switch (animation)
            {
                case "dash": return new[] { "dash", "step" };
                case "fire": return new[] { "fire", "attack" };
                case "slam": return new[] { "slam", "attack" };
                case "summon": return new[] { "summon", "attack" };
                case "immune": return new[] { "immune", "hit" };
                case "spawn": return new[] { "spawn", "wake" };
                default: return new[] { animation };
            }
        }

        /// <summary>The run is over after the hero's victory or defeat, so those hold their last frame.</summary>
        public static bool Holds(int token, string animation) =>
            token == HeroToken && (animation == "victory" || animation == "defeat");

        /// <summary>Pose name that changes the token's standing art: Lord Blobert boasts while a slam is telegraphed, slimes rest.</summary>
        public static string Pose(EnemyState enemy, EnemyDefinition def)
        {
            // MAINT-38: a puffed or deflated boss is drawn by its mode, so it has no pose - but an enraged one is still
            // drawn by its intent, and gating this on Normal pinned the pose string the moment a boss enraged, after
            // which BoardView's visual key stopped changing and the body was never redrawn again.
            if (def.IsBoss)
                return enemy.Mode != EnemyMode.Puffed && enemy.Mode != EnemyMode.Deflated
                       && enemy.Intent.Kind == IntentKind.Slam ? "boast" : "";
            return enemy.Intent.Kind == IntentKind.Rest ? "rest" : "";
        }

        // Damage events carry the attack source in Source and the victim in Subject; movement carries neither.
        static string EnemyId(RunState run, GameEvent e) => e.Subject ?? e.Source ?? run.Floor.EnemyById(e.ActorId)?.DefId;
    }
}
