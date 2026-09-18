using System;
using System.Globalization;
using ClickDungeon.Content;
using ClickDungeon.Domain;

namespace ClickDungeon.Application
{
    /// <summary>
    /// The daily reward (D-029, rules §16): one claim per calendar day, a seven-day week of rewards that climbs while the
    /// player comes back every day and starts over after a missed day. The caller passes today's date, so nothing here reads
    /// a clock and the tests can walk through any calendar.
    /// </summary>
    public static class DailyReward
    {
        public static string DateKey(DateTime day) => day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        static DateTime? LastClaim(ProfileState profile) =>
            profile != null && DateTime.TryParseExact(profile.LastDailyClaim, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var day) ? day : (DateTime?)null;

        /// <summary>
        /// True once per day. A clock set back to before the last claim waits for that day to pass, so winding the clock
        /// back and forth cannot claim the same day twice.
        /// </summary>
        public static bool CanClaim(ProfileState profile, DateTime today)
        {
            if (profile == null) return false;
            var last = LastClaim(profile);
            return last == null || today.Date > last.Value;
        }

        /// <summary>The week's day (1 to 7) the next claim pays, as the panel shows it.</summary>
        public static int NextDay(ProfileState profile, ContentCatalog catalog, DateTime today)
        {
            int week = catalog.DailyRewards.Count;
            var last = LastClaim(profile);
            if (last == null || week == 0) return 1;
            // Claimed today: the next claim is tomorrow's, which continues the streak.
            bool continues = today.Date <= last.Value.AddDays(1);
            return continues ? profile.DailyStreak % week + 1 : 1;
        }

        public static DailyRewardDefinition Reward(ContentCatalog catalog, int day) =>
            catalog.DailyRewards.Count == 0 ? null : catalog.DailyRewards[(day - 1) % catalog.DailyRewards.Count];

        /// <summary>Pays today's reward into the profile. Null when today is already claimed.</summary>
        public static DailyRewardDefinition Claim(ProfileState profile, ContentCatalog catalog, DateTime today)
        {
            if (!CanClaim(profile, today) || catalog.DailyRewards.Count == 0) return null;
            int day = NextDay(profile, catalog, today);
            var reward = Reward(catalog, day);
            profile.Coins += reward.Coins;
            profile.Gems += reward.Gems;
            profile.PotionRations += reward.PotionRations;
            profile.HeartTokens += reward.HeartTokens;
            profile.SpecialKeys += reward.SpecialKeys;
            profile.DailyStreak = day;
            profile.LastDailyClaim = DateKey(today);
            return reward;
        }
    }
}
