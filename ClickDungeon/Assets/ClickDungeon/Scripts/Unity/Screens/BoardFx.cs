using System.Collections.Generic;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using ClickDungeon.Unity.Ui;

namespace ClickDungeon.Unity.Screens
{
    /// <summary>
    /// Board effects (art brief §8 FX row) chosen from events the simulation already produced. Presentation only:
    /// popups and the WHAT HAPPENED log still carry the information, so effects are purely additive.
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
                    case GameEventKind.SpikesTriggered: Add(ArtKeys.FxSpikesTrigger, e.To, 1); break;
                    case GameEventKind.BombExploded: Add(ArtKeys.FxExplosion, e.To, 3); break;
                    case GameEventKind.KeyCollected: Add(ArtKeys.FxKeyCollect, e.To, 1); break;
                    case GameEventKind.PotionCollected: Add(ArtKeys.FxPotionCollect, e.To, 1); break;
                    case GameEventKind.ExitUnlocked: Add(ArtKeys.FxExitUnlock, e.To, 1); break;
                    case GameEventKind.EnemyWoke: Add(ArtKeys.FxEnemyWake, e.To, 1); break;
                }
            }
            return effects;
        }
    }
}
