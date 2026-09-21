using System;
using System.Collections.Generic;
using ClickDungeon.Content;
using ClickDungeon.Domain;

namespace ClickDungeon.Application
{
    /// <summary>
    /// The mail (D-030, rules §17): letters the game sends the player between runs. A letter can carry a gift, which waits in
    /// the letter until it is collected, so a reward is never lost because the player was busy. Mail comes from things that
    /// really happened: the welcome, a level reached, an achievement earned.
    /// </summary>
    public static class Mailbox
    {
        /// <summary>Letters kept. Past this, the oldest read letter with nothing left to collect goes; a gift is never thrown away.</summary>
        public const int Capacity = 40;

        public static MailMessage Post(ProfileState profile, string from, string subject, string body, RewardBundle gift = null)
        {
            if (profile.Mail == null) profile.Mail = new List<MailMessage>();
            var message = new MailMessage
            {
                Id = ++profile.NextMailId, From = from, Subject = subject, Body = body,
                Coins = gift?.Coins ?? 0, Gems = gift?.Gems ?? 0, PotionRations = gift?.PotionRations ?? 0,
                HeartTokens = gift?.HeartTokens ?? 0, SpecialKeys = gift?.SpecialKeys ?? 0, GiftLabel = gift?.Label,
            };
            profile.Mail.Add(message);
            while (profile.Mail.Count > Capacity)
            {
                int old = profile.Mail.FindIndex(m => m.Read && !Waiting(m));
                if (old < 0) break;
                profile.Mail.RemoveAt(old);
            }
            return message;
        }

        /// <summary>A gift not yet collected.</summary>
        public static bool Waiting(MailMessage message) => message.HasGift && !message.Collected;

        /// <summary>Letters that want the player: unread, or holding a gift. The mail button's red "!" shows while this is above zero.</summary>
        public static int Unread(ProfileState profile)
        {
            if (profile?.Mail == null) return 0;
            int count = 0;
            foreach (var m in profile.Mail)
                if (!m.Read || Waiting(m)) count++;
            return count;
        }

        public static MailMessage Find(ProfileState profile, int id) => profile?.Mail?.Find(m => m.Id == id);

        public static void Open(ProfileState profile, int id)
        {
            var message = Find(profile, id);
            if (message != null) message.Read = true;
        }

        /// <summary>Adds the letter's gift to the profile, once. False when there is nothing to collect.</summary>
        public static bool Collect(ProfileState profile, int id)
        {
            var message = Find(profile, id);
            if (message == null || !Waiting(message)) return false;
            // A gift only ever adds (SEC-04): a hand-edited letter carrying a negative amount must not empty the purse.
            profile.Coins += Math.Max(0, message.Coins);
            profile.Gems += Math.Max(0, message.Gems);
            profile.PotionRations += Math.Max(0, message.PotionRations);
            profile.HeartTokens += Math.Max(0, message.HeartTokens);
            profile.SpecialKeys += Math.Max(0, message.SpecialKeys);
            message.Collected = true;
            message.Read = true;
            return true;
        }

        public static int CollectAll(ProfileState profile)
        {
            if (profile?.Mail == null) return 0;
            int collected = 0;
            foreach (var m in profile.Mail.ToArray())
                if (Collect(profile, m.Id)) collected++;
            return collected;
        }

        /// <summary>The first letter, sent once to a new profile.</summary>
        public static void Welcome(ProfileState profile, ContentCatalog catalog)
        {
            if (profile == null || profile.NextMailId > 0) return;
            Post(profile, "Sir Clickington", "Welcome to the dungeon!",
                "Every tile hides something, and every run leaves you stronger: coins for the shop, experience for talents, " +
                "gear for your slots. The crown keeps your achievements; each one sends a gift here. " +
                "Here is a potion ration for your first descent.",
                catalog.WelcomeGift);
        }

        /// <summary>Called when a run is banked: a letter for each level gained, so a waiting talent point is never missed.</summary>
        public static void LevelsGained(ProfileState profile, int before, int after)
        {
            for (int level = before + 1; level <= after; level++)
                Post(profile, "The Guild", $"Level {level}!",
                    $"You reached level {level} and earned a talent point. Spend it under TALENTS; it shapes every run you start.");
        }
    }
}
