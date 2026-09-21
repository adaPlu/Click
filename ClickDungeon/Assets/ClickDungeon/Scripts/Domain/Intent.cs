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
        /// <summary>The boar rushes down this line next turn, through every tile it can cross (D-058).</summary>
        public static Intent Charge(Direction dir) => new Intent { Kind = IntentKind.Charge, Dir = dir, Target = GridPos.Invalid };
        /// <summary>The bomber lobs a lit bomb onto this tile next turn (D-058).</summary>
        public static Intent Throw(GridPos cell) => new Intent { Kind = IntentKind.Throw, Target = cell };
        /// <summary>A fallen skeleton lies as bones, pulling itself back together (D-058).</summary>
        public static Intent Reassemble() => new Intent { Kind = IntentKind.Reassemble, Target = GridPos.Invalid };

        public override string ToString()
        {
            switch (Kind)
            {
                case IntentKind.Attack:
                case IntentKind.Summon:
                case IntentKind.Slam:
                case IntentKind.Throw:
                    return $"{Kind}{Target}";
                case IntentKind.Fire:
                    return $"Fire({Dir})";
                case IntentKind.Charge:
                    return $"Charge({Dir})";
                default:
                    return Kind.ToString();
            }
        }
    }
}
