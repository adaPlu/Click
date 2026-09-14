using System;
using System.Collections.Generic;

namespace ClickDungeon.Simulation
{
    /// <summary>
    /// Stateless seed derivation (decision D-006). Each purpose derives its own seed from stable
    /// context, so no random roll can shift another.
    /// </summary>
    public static class Hash
    {
        public const ulong GenerationSalt = 0x47454E5F464C4F52UL;
        public const ulong LootSalt = 0x4C4F4F545F434845UL;

        public static ulong Mix(ulong z)
        {
            unchecked
            {
                z += 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        public static ulong Of(params ulong[] parts)
        {
            ulong h = 0xC11CD00D5EEDUL;
            foreach (var part in parts) h = Mix(h ^ Mix(part));
            return h;
        }
    }

    /// <summary>SplitMix64. Deterministic on every platform. Simulation never uses UnityEngine.Random.</summary>
    public sealed class DeterministicRng
    {
        ulong _state;

        public DeterministicRng(ulong seed)
        {
            _state = seed;
        }

        public ulong NextULong()
        {
            unchecked
            {
                _state += 0x9E3779B97F4A7C15UL;
                ulong z = _state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        public int Next(int maxExclusive)
        {
            if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            return (int)(NextULong() % (ulong)maxExclusive);
        }

        public int Range(int minInclusive, int maxInclusive) => minInclusive + Next(maxInclusive - minInclusive + 1);

        public T Pick<T>(IReadOnlyList<T> items) => items[Next(items.Count)];
    }
}
