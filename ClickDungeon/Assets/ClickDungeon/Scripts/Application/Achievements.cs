using System;
using System.Collections.Generic;
using ClickDungeon.Content;
using ClickDungeon.Domain;

namespace ClickDungeon.Application
{
    /// <summary>
    /// The crown's achievements (D-030, rules §17): goals counted over every run. Each is earned once, when its count reaches
    /// the target, and sends its gift by mail. Counts only grow when a run is banked, so a run itself never reads them.
    /// </summary>
    public static class Achievements
    {
        public static int Progress(ProfileState profile, AchievementStat stat)
        {
            if (profile == null) return 0;
            switch (stat)
            {
                case AchievementStat.RunsFinished: return profile.RunsFinished;
                case AchievementStat.RunsWon: return profile.RunsWon;
                case AchievementStat.MonstersSlain: return profile.MonstersSlain;
                case AchievementStat.ChestsOpened: return profile.ChestsOpened;
                case AchievementStat.DeepestFloor: return profile.DeepestFloor;
                case AchievementStat.CoinsEarned: return profile.CoinsEarned;
                case AchievementStat.Level: return Progression.Level(profile);
                case AchievementStat.ItemsOwned: return profile.Items?.Count ?? 0;
                default: return 0;
            }
        }

        public static bool Earned(ProfileState profile, string id) => profile?.Achievements != null && profile.Achievements.Contains(id);

        public static int EarnedCount(ProfileState profile, ContentCatalog catalog)
        {
            int count = 0;
            foreach (var a in catalog.Achievements)
                if (Earned(profile, a.Id)) count++;
            return count;
        }

        /// <summary>Marks every goal now reached and mails its gift. Returns the ones earned by this call.</summary>
        public static List<AchievementDefinition> Check(ProfileState profile, ContentCatalog catalog)
        {
            var earned = new List<AchievementDefinition>();
            if (profile == null) return earned;
            if (profile.Achievements == null) profile.Achievements = new List<string>();
            foreach (var a in catalog.Achievements)
            {
                if (Earned(profile, a.Id) || Progress(profile, a.Stat) < a.Target) continue;
                profile.Achievements.Add(a.Id);
                Mailbox.Post(profile, "The Crown", $"Achievement: {a.Title}", $"{a.Description}. Well done! Your reward is attached.", a.Reward);
                earned.Add(a);
            }
            return earned;
        }
    }
}
