using System;
using System.Security.Cryptography;
using ClickDungeon.Domain;

namespace ClickDungeon.Application
{
    /// <summary>Run seeds and launch-option parsing shared by the app and its tests.</summary>
    public static class LaunchOptions
    {
        /// <summary>
        /// A fresh random run seed. It is logged in playtest telemetry, so it comes from a cryptographic source rather than the
        /// clock or uptime, which a seed would otherwise reveal.
        /// </summary>
        public static ulong NewRunSeed()
        {
            var bytes = new byte[8];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return BitConverter.ToUInt64(bytes, 0);
        }

        /// <summary>Difficulty by tier name (easy, medium, hardcore; any case). Numbers and unknown names give Medium.</summary>
        public static Difficulty ParseDifficulty(string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                foreach (Difficulty tier in Enum.GetValues(typeof(Difficulty)))
                    if (string.Equals(tier.ToString(), value.Trim(), StringComparison.OrdinalIgnoreCase)) return tier;
            }
            return Difficulty.Medium;
        }

        /// <summary>Movement mode by name (free, step; any case). Numbers and unknown names give Free Roam, the default.</summary>
        public static MovementMode ParseMovement(string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                foreach (MovementMode mode in Enum.GetValues(typeof(MovementMode)))
                    if (string.Equals(mode.ToString(), value.Trim(), StringComparison.OrdinalIgnoreCase)) return mode;
            }
            return MovementMode.Free;
        }
    }
}
