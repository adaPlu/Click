using System;
using System.Collections.Generic;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using UnityEngine;

namespace ClickDungeon.Unity.Ui
{
    /// <summary>
    /// Production art keyed by file name (decision D-016). Built by the editor from
    /// Assets/ClickDungeon/Art/Runtime; lives in Art/Resources so code-built UI can load it without scene references.
    /// </summary>
    [CreateAssetMenu(menuName = "ClickDungeon/Art Catalog", fileName = "ArtCatalog")]
    public sealed class ArtCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string Key;
            public Sprite[] Frames = Array.Empty<Sprite>();
            public float Fps = 10f;
            /// <summary>True for reference slices in Art/Runtime/Placeholders (art brief D4), not production art.</summary>
            public bool Placeholder;
        }

        [SerializeField] List<Entry> _entries = new List<Entry>();
        Dictionary<string, Entry> _lookup;

        public IReadOnlyList<Entry> Entries => _entries;

        public void SetEntries(IEnumerable<Entry> entries)
        {
            _entries = new List<Entry>(entries);
            _lookup = null;
        }

        public bool TryGet(string key, out Entry entry)
        {
            if (_lookup == null)
            {
                _lookup = new Dictionary<string, Entry>();
                foreach (var e in _entries)
                {
                    if (e == null || string.IsNullOrEmpty(e.Key) || e.Frames == null || e.Frames.Length == 0 || e.Frames[0] == null) continue;
                    _lookup[e.Key] = e;
                }
            }
            return _lookup.TryGetValue(key, out entry);
        }

        void OnValidate() => _lookup = null;
    }

    /// <summary>
    /// Runtime access to production art. A missing key returns false so callers keep drawing the procedural
    /// placeholder: art can arrive piece by piece without breaking the game.
    /// </summary>
    public static class Art
    {
        public const string CatalogResourceName = "ArtCatalog";

        static ArtCatalog _catalog;
        static bool _loaded;

        public static ArtCatalog Catalog
        {
            get
            {
                if (!_loaded)
                {
                    _catalog = Resources.Load<ArtCatalog>(CatalogResourceName);
                    _loaded = true;
                }
                return _catalog;
            }
        }

        public static bool TryGet(string key, out ArtCatalog.Entry entry)
        {
            entry = null;
            var catalog = Catalog;
            return catalog != null && !string.IsNullOrEmpty(key) && catalog.TryGet(key, out entry);
        }

        public static bool TryGetSprite(string key, out Sprite sprite)
        {
            sprite = TryGet(key, out var entry) ? entry.Frames[0] : null;
            return sprite != null;
        }

        public static bool Has(string key) => TryGet(key, out _);

        /// <summary>Use a specific catalog (tests, tools). Pass null to force placeholders.</summary>
        public static void Override(ArtCatalog catalog)
        {
            _catalog = catalog;
            _loaded = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset()
        {
            _catalog = null;
            _loaded = false;
        }
    }

    /// <summary>Art key naming (docs/art-brief.md, appendix). Keys are file names without extension.</summary>
    public static class ArtKeys
    {
        public const string HeroId = ContentCatalog.DefaultHeroId;

        public const string FloorStone = "tile_floor_stone";
        public const string Wall = "tile_wall";
        public const string Pit = "tile_pit";
        public const string Spikes = "tile_spikes";
        public const string Bomb = "tile_bomb";
        public const string BombArmed = "tile_bomb_armed";
        public const string Key = "tile_key";
        public const string ChestClosed = "tile_chest_closed";
        public const string ChestOpen = "tile_chest_open";
        public const string Potion = "tile_potion";
        public const string ExitLocked = "tile_exit_locked";
        public const string ExitOpen = "tile_exit_open";
        public const string Logo = "logo_clickdungeon";
        public const string TitleBackground = "bg_title";
        public const string GameplayBackground = "bg_gameplay";

        public static readonly string[] Expressions =
            { "neutral", "happy", "confident", "worried", "shocked", "angry", "victorious", "defeated" };

        public static string Actor(string contentId, string state = "idle") => $"actor_{contentId}_{state}";

        public static string ClueIcon(Clue flag) => $"icon_clue_{flag.ToString().ToLowerInvariant()}";

        public static string AbilityIcon(CommandKind kind) => $"icon_ability_{kind.ToString().ToLowerInvariant()}";

        public static string Portrait(string heroId, string expression) => $"portrait_{heroId}_{expression.ToLowerInvariant()}";

        /// <summary>Every key the game currently looks up (used by the coverage report).</summary>
        public static List<string> Wired(ContentCatalog catalog)
        {
            var keys = new List<string>
            {
                FloorStone, Wall, Pit, Spikes, Bomb, BombArmed, Key, ChestClosed, ChestOpen, Potion, ExitLocked, ExitOpen,
                Logo, TitleBackground, GameplayBackground,
                Actor(HeroId),
            };
            foreach (var enemy in catalog.Enemies.Values) keys.Add(Actor(enemy.Id));
            foreach (var enemy in catalog.Enemies.Values)
            {
                if (!enemy.IsBoss) continue;
                keys.Add(Actor(enemy.Id, "puffed"));
                keys.Add(Actor(enemy.Id, "deflated"));
            }
            foreach (var clue in new[] { Clue.Enemy, Clue.Danger, Clue.Objective, Clue.Treasure, Clue.Safe }) keys.Add(ClueIcon(clue));
            foreach (var kind in new[] { CommandKind.Move, CommandKind.Slash, CommandKind.Shield, CommandKind.Dash, CommandKind.Potion })
                keys.Add(AbilityIcon(kind));
            foreach (var expression in Expressions) keys.Add(Portrait(HeroId, expression));
            return keys;
        }
    }
}
