using System;

namespace ClickDungeon.Domain
{
    public enum Direction { Up = 0, Right = 1, Down = 2, Left = 3 }

    public static class Directions
    {
        /// <summary>Canonical tie-break order used everywhere in simulation.</summary>
        public static readonly Direction[] All = { Direction.Up, Direction.Right, Direction.Down, Direction.Left };

        public static GridPos ToOffset(this Direction d)
        {
            switch (d)
            {
                case Direction.Up: return new GridPos(0, 1);
                case Direction.Right: return new GridPos(1, 0);
                case Direction.Down: return new GridPos(0, -1);
                default: return new GridPos(-1, 0);
            }
        }

        public static bool TryFromDelta(int dx, int dy, out Direction dir)
        {
            dir = Direction.Up;
            if (dx == 0 && dy > 0) { dir = Direction.Up; return true; }
            if (dx > 0 && dy == 0) { dir = Direction.Right; return true; }
            if (dx == 0 && dy < 0) { dir = Direction.Down; return true; }
            if (dx < 0 && dy == 0) { dir = Direction.Left; return true; }
            return false;
        }
    }

    /// <summary>Board coordinate. (0,0) is bottom-left; y grows upward.</summary>
    [Serializable]
    public struct GridPos : IEquatable<GridPos>
    {
        public int X;
        public int Y;

        public GridPos(int x, int y)
        {
            X = x;
            Y = y;
        }

        public static readonly GridPos Invalid = new GridPos(-1, -1);

        public bool InBounds => X >= 0 && X < BoardRules.Size && Y >= 0 && Y < BoardRules.Size;

        public int Index => Y * BoardRules.Size + X;

        public static GridPos FromIndex(int index) => new GridPos(index % BoardRules.Size, index / BoardRules.Size);

        public int Manhattan(GridPos other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);

        public int Chebyshev(GridPos other) => Math.Max(Math.Abs(X - other.X), Math.Abs(Y - other.Y));

        public bool IsOrthogonallyAdjacent(GridPos other) => Manhattan(other) == 1;

        public GridPos Step(Direction dir, int count = 1)
        {
            var o = dir.ToOffset();
            return new GridPos(X + o.X * count, Y + o.Y * count);
        }

        public static bool operator ==(GridPos a, GridPos b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(GridPos a, GridPos b) => !(a == b);
        public bool Equals(GridPos other) => this == other;
        public override bool Equals(object obj) => obj is GridPos other && this == other;
        public override int GetHashCode() => X * 31 + Y;
        public override string ToString() => $"({X},{Y})";
    }
}
