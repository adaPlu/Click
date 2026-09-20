using System.Collections.Generic;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using ClickDungeon.Unity.Ui;

namespace ClickDungeon.Unity.Screens
{
    /// <summary>
    /// Board effects (art brief §8 FX row) chosen from events the simulation already produced. Presentation only:
    /// popups and the WHAT HAPPENED log still carry the information, so effects are purely additive.
    ///
    /// D-045: the sparkle stars for picking up a key or a potion and for stepping on spikes are gone — they fired on
    /// ordinary turns, on top of a popup that already said the same thing. What is left marks something that happens
    /// once: the blast of a bomb, the exit opening, and a monster waking up.
    /// </summary>
    public static class BoardFx
    {
        public struct Effect
        {
            public string Key;
            public GridPos Cell;
            /// <summary>Size in tiles, centred on Cell (the bomb explosion covers its 3×3 blast).</summary>
            public int Tiles;
        }

        public static List<Effect> Pick(IReadOnlyList<GameEvent> events)
        {
            var effects = new List<Effect>();
            if (events == null) return effects;

            void Add(string key, GridPos cell, int tiles)
            {
                if (cell.InBounds) effects.Add(new Effect { Key = key, Cell = cell, Tiles = tiles });
            }

            foreach (var e in events)
            {
                switch (e.Kind)
                {
                    case GameEventKind.BombExploded: Add(ArtKeys.FxExplosion, e.To, 3); break;
                    case GameEventKind.ExitUnlocked: Add(ArtKeys.FxExitUnlock, e.To, 1); break;
                    case GameEventKind.EnemyWoke: Add(ArtKeys.FxEnemyWake, e.To, 1); break;
                }
            }
            return effects;
        }
    }
}
