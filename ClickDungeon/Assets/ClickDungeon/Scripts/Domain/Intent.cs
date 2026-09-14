using System;

namespace ClickDungeon.Domain
{
    /// <summary>
    /// What an awake enemy will do in the next enemy phase. Always visible to the player.
    /// Attacks lock onto cells, not onto the hero.
    /// </summary>
    [Serializable]
    public struct Intent
    {
        public IntentKind Kind;
        public GridPos Target;
        public Direction Dir;

        public static Intent None() => new Intent { Kind = IntentKind.None, Target = GridPos.Invalid };
        public static Intent Attack(GridPos cell) => new Intent { Kind = IntentKind.Attack, Target = cell };
        public static Intent Move() => new Intent { Kind = IntentKind.Move, Target = GridPos.Invalid };
        public static Intent Fire(Direction dir) => new Intent { Kind = IntentKind.Fire, Dir = dir, Target = GridPos.Invalid };
        public static Intent Rest() => new Intent { Kind = IntentKind.Rest, Target = GridPos.Invalid };
        public static Intent Recover() => new Intent { Kind = IntentKind.Recover, Target = GridPos.Invalid };
        public static Intent Summon(GridPos cell) => new Intent { Kind = IntentKind.Summon, Target = cell };
        public static Intent Slam(GridPos center) => new Intent { Kind = IntentKind.Slam, Target = center };
        public static Intent PuffUp() => new Intent { Kind = IntentKind.PuffUp, Target = GridPos.Invalid };

        public override string ToString()
        {
            switch (Kind)
            {
                case IntentKind.Attack:
                case IntentKind.Summon:
                case IntentKind.Slam:
                    return $"{Kind}{Target}";
                case IntentKind.Fire:
                    return $"Fire({Dir})";
                default:
                    return Kind.ToString();
            }
        }
    }
}
