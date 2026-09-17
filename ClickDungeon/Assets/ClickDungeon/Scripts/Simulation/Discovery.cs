using System.Collections.Generic;
using ClickDungeon.Domain;

namespace ClickDungeon.Simulation
{
    /// <summary>
    /// What the player may learn from a resolved turn (D-023 amendment). Every covered tile looks the same, so nothing that
    /// happens under a cover may be pointed at: no popup or effect on a covered tile, and no word about a monster still hidden
    /// there. Monsters that are awake are drawn wherever they stand, so everything about them stays visible.
    /// </summary>
    public sealed class Discovery
    {
        readonly HashSet<int> _hiddenBefore = new HashSet<int>();
        readonly FloorState _floorBefore;

        Discovery(RunState run)
        {
            _floorBefore = run.Floor;
            foreach (var enemy in run.Floor.Enemies)
                if (!enemy.Awake) _hiddenBefore.Add(enemy.Id);
        }

        /// <summary>Takes note of the monsters still hidden before the turn resolves.</summary>
        public static Discovery Before(RunState run) => new Discovery(run);

        /// <summary>Whether the event may be told at all (the log, the hero's remark). Only a still-hidden monster is silent.</summary>
        public bool CanMention(RunState after, GameEvent e) => !AboutHiddenMonster(after, e);

        /// <summary>Whether the event may mark its tile with a popup or an effect.</summary>
        public bool CanMark(RunState after, GameEvent e)
        {
            if (!CanMention(after, e)) return false;
            if (!e.To.InBounds) return true;
            var floor = after.Floor;
            if (floor[e.To].Knowledge == Knowledge.Revealed || after.Hero.Pos == e.To) return true;
            // An awake monster is drawn on top of the cover, so its own events can point at it.
            var enemy = floor.EnemyAt(e.To);
            return enemy != null && enemy.Awake && (e.ActorId == 0 || e.ActorId == enemy.Id);
        }

        bool AboutHiddenMonster(RunState after, GameEvent e)
        {
            // Ids restart on every floor and vault, so ids noted on the floor that was left say nothing about this one.
            if (e.ActorId == 0 || after.Floor != _floorBefore || !_hiddenBefore.Contains(e.ActorId)) return false;
            foreach (var enemy in after.Floor.Enemies)
                if (enemy.Id == e.ActorId) return !enemy.Awake;
            // Gone this turn without ever waking: killed under its cover.
            return true;
        }

        /// <summary>The events that may mark their tiles, in order.</summary>
        public List<GameEvent> Markable(RunState after, IReadOnlyList<GameEvent> events)
        {
            var visible = new List<GameEvent>();
            if (events == null) return visible;
            foreach (var e in events)
                if (CanMark(after, e)) visible.Add(e);
            return visible;
        }
    }
}
