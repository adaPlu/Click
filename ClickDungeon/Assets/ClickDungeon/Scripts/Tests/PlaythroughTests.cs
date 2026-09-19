using System;
using System.Linq;
using ClickDungeon.Application;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using NUnit.Framework;

namespace ClickDungeon.Tests
{
    /// <summary>
    /// Playtest aid: one player's first runs, back to back through the real session, so the profile carries over as it
    /// would for a person (coins, XP, gear, achievements, mail). Between runs the player collects their mail and spends
    /// free talent points. The bot plays blind with the casual mistake rate.
    /// </summary>
    public class PlaythroughTests
    {
        [Test, Explicit("Playtest aid: dotnet test --filter Name=TenPlaythroughs --logger \"console;verbosity=detailed\"")]
        public void TenPlaythroughs()
        {
            var session = new GameSession(ContentCatalog.CreateDefault(), null);
            Console.WriteLine("run  seed      result  floor  turns  hp     coins  gems  +xp  level  items  achievements");
            for (int i = 1; i <= 10; i++)
            {
                Mailbox.CollectAll(session.Profile);
                // The first talent it can learn each time, path by path: a simple, repeatable build.
                bool learned = true;
                while (learned)
                {
                    learned = false;
                    foreach (var talent in session.Catalog.TalentsOf("knight"))
                        if (Progression.TryLearn(session.Profile, session.Catalog, talent.Id)) { learned = true; break; }
                }

                ulong seed = 20260918UL + (ulong)i * 101UL;
                session.StartNewRun(seed, Difficulty.Medium, MovementMode.Free);
                var bot = new AutoPlayer(AutoPlayer.CasualMistakeRate, blind: true);
                for (int c = 0; c < 1500 && session.Run.Status == RunStatus.InProgress; c++)
                    session.Submit(bot.Choose(session.Run, session.Catalog, seed * 7919UL + (ulong)c));
                var run = session.Run;
                if (run.Status == RunStatus.InProgress) session.Abandon();

                var p = session.Profile;
                Console.WriteLine($"{i,3}  {seed,-8}  {(run.Status == RunStatus.InProgress ? "STALL" : run.Status.ToString()),-6}  {run.Floor.FloorIndex,5}  " +
                                  $"{run.Turn,5}  {run.Hero.Hp,2}/{run.Hero.MaxHp,-3}  {p.Coins,5}  {p.Gems,4}  {run.XpEarned,3}  {Progression.Level(p),5}  " +
                                  $"{p.Items.Count,5}  {Achievements.EarnedCount(p, session.Catalog)}");
            }
            var profile = session.Profile;
            Console.WriteLine($"\nWon {profile.RunsWon} of {profile.RunsFinished}. Deepest floor {profile.DeepestFloor}, monsters {profile.MonstersSlain}, " +
                              $"chests {profile.ChestsOpened}, coins carried out {profile.CoinsEarned}.");
            Console.WriteLine("Gear: " + string.Join(", ", profile.Items));
            Console.WriteLine("Talents: " + string.Join(", ", profile.Talents.Select(kv => $"{kv.Key} {kv.Value}")));
            Console.WriteLine("Achievements: " + string.Join(", ", session.Catalog.Achievements.Where(a => Achievements.Earned(profile, a.Id)).Select(a => a.Title)));
        }
    }
}
