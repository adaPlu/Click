using UnityEngine;

namespace ClickDungeon.Unity.Ui
{
    /// <summary>Colours sampled from the reference art: torch-lit stone, navy UI, gold trim.</summary>
    public static class Palette
    {
        public static readonly Color Background = Hex(0x15141A);
        public static readonly Color StoneDark = Hex(0x24222A);
        public static readonly Color Stone = Hex(0x3A3840);
        public static readonly Color StoneLight = Hex(0x58545E);

        public static readonly Color FloorRevealed = Hex(0x6E6A75);
        public static readonly Color FloorSensed = Hex(0x47444E);
        public static readonly Color FloorUnseen = Hex(0x2B2A31);
        public static readonly Color Wall = Hex(0x1C1B20);
        public static readonly Color WallEdge = Hex(0x55505A);
        public static readonly Color Pit = Hex(0x07060A);

        public static readonly Color Navy = Hex(0x141B33);
        public static readonly Color NavyLight = Hex(0x22305A);
        public static readonly Color Gold = Hex(0xE8B84A);
        public static readonly Color GoldDark = Hex(0x8F6320);
        public static readonly Color Parchment = Hex(0xD9C08F);
        public static readonly Color Ink = Hex(0x3B2A14);
        public static readonly Color TextLight = Hex(0xF3E9D2);
        public static readonly Color TextDim = Hex(0xA69F93);

        public static readonly Color Hp = Hex(0xD23A2E);
        public static readonly Color HpBack = Hex(0x3A1512);
        public static readonly Color PlayGreen = Hex(0x3E9B35);
        public static readonly Color QuitRed = Hex(0x9B2A22);

        public static readonly Color MoveButton = Hex(0x4A3552);
        public static readonly Color SlashButton = Hex(0x23407A);
        public static readonly Color ShieldButton = Hex(0x8A2320);
        public static readonly Color DashButton = Hex(0x1F4F8F);
        public static readonly Color PotionButton = Hex(0x2A2530);

        public static readonly Color Hero = Hex(0x3D7BD9);
        public static readonly Color Goblin = Hex(0x5DA83A);
        public static readonly Color Slime = Hex(0x8E4FD1);
        public static readonly Color Imp = Hex(0xE0572B);
        public static readonly Color Boss = Hex(0x7A3BC4);
        public static readonly Color Steel = Hex(0xB8BCC4);
        public static readonly Color Bomb = Hex(0x111111);
        public static readonly Color Fuse = Hex(0xFF9A2E);
        public static readonly Color ChestWood = Hex(0x8A5A2B);
        public static readonly Color Potion = Hex(0xE0444E);

        public static readonly Color Danger = Hex(0xFF4D3A);
        public static readonly Color FireLane = Hex(0xFF8A1F);
        public static readonly Color Slam = Hex(0xB0182B);
        public static readonly Color Summon = Hex(0xB46BFF);
        public static readonly Color Legal = Hex(0xF2C94C);
        public static readonly Color Safe = Hex(0x9FD8A0);

        public static Color WithAlpha(this Color c, float a) => new Color(c.r, c.g, c.b, a);

        /// <summary>Scales brightness, keeping alpha.</summary>
        public static Color Dim(this Color c, float k) => new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), c.a);

        static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }
}
