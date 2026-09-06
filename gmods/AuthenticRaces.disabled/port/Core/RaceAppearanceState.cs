#if GLOADER
using System;
using System.Runtime.CompilerServices;
using Terraria;

namespace AuthenticRaces.Core
{
    /// <summary>
    /// The custom appearance vocabulary MrPlague stores outside vanilla Player data.
    /// Vanilla already owns hair/skin/eye/clothing colours and the primary hairstyle.
    /// </summary>
    internal static class RaceAppearanceState
    {
        private static ConditionalWeakTable<Player, Holder> States =
            new ConditionalWeakTable<Player, Holder>();

        public static void ResetAll()
        {
            States = new ConditionalWeakTable<Player, Holder>();
        }

        public static RaceAppearanceData Get(Player player)
        {
            if (player == null)
                throw new ArgumentNullException(nameof(player));

            return States.GetValue(player, _ => new Holder()).Value;
        }

        public static void Set(Player player, RaceAppearanceData appearance)
        {
            if (player == null)
                throw new ArgumentNullException(nameof(player));

            States.GetValue(player, _ => new Holder()).Value = appearance;
        }

        public static void RestoreDefault(Player player)
        {
            Set(player, RaceAppearanceData.Default);
        }

        private sealed class Holder
        {
            public RaceAppearanceData Value = RaceAppearanceData.Default;
        }
    }

    internal struct RaceAppearanceData : IEquatable<RaceAppearanceData>
    {
        public Rgb24 DetailColor;
        public Rgb24 AuxiliaryDetailColor1;
        public Rgb24 AuxiliaryDetailColor2;
        public Rgb24 AuxiliaryDetailColor3;

        public int AuxiliaryHairstyle1;
        public int AuxiliaryHairstyle2;
        public int AuxiliaryHairstyle3;

        public static RaceAppearanceData Default => new RaceAppearanceData {
            // These match MrPlagueRacesPlayer's initial custom fields. Race-selection
            // defaults can intentionally replace them later when that seam is ported.
            DetailColor = Rgb24.White,
            AuxiliaryDetailColor1 = Rgb24.White,
            AuxiliaryDetailColor2 = Rgb24.White,
            AuxiliaryDetailColor3 = Rgb24.White,
            AuxiliaryHairstyle1 = 0,
            AuxiliaryHairstyle2 = 0,
            AuxiliaryHairstyle3 = 0
        };

        public bool Equals(RaceAppearanceData other)
        {
            return DetailColor.Equals(other.DetailColor) &&
                AuxiliaryDetailColor1.Equals(other.AuxiliaryDetailColor1) &&
                AuxiliaryDetailColor2.Equals(other.AuxiliaryDetailColor2) &&
                AuxiliaryDetailColor3.Equals(other.AuxiliaryDetailColor3) &&
                AuxiliaryHairstyle1 == other.AuxiliaryHairstyle1 &&
                AuxiliaryHairstyle2 == other.AuxiliaryHairstyle2 &&
                AuxiliaryHairstyle3 == other.AuxiliaryHairstyle3;
        }

        public override bool Equals(object obj)
        {
            return obj is RaceAppearanceData other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + DetailColor.GetHashCode();
                hash = hash * 31 + AuxiliaryDetailColor1.GetHashCode();
                hash = hash * 31 + AuxiliaryDetailColor2.GetHashCode();
                hash = hash * 31 + AuxiliaryDetailColor3.GetHashCode();
                hash = hash * 31 + AuxiliaryHairstyle1;
                hash = hash * 31 + AuxiliaryHairstyle2;
                hash = hash * 31 + AuxiliaryHairstyle3;
                return hash;
            }
        }
    }

    internal struct Rgb24 : IEquatable<Rgb24>
    {
        public byte R;
        public byte G;
        public byte B;

        public Rgb24(byte r, byte g, byte b)
        {
            R = r;
            G = g;
            B = b;
        }

        public static Rgb24 White => new Rgb24(255, 255, 255);

        public bool Equals(Rgb24 other)
        {
            return R == other.R && G == other.G && B == other.B;
        }

        public override bool Equals(object obj)
        {
            return obj is Rgb24 other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (R << 16) | (G << 8) | B;
        }

        public override string ToString()
        {
            return R + "," + G + "," + B;
        }
    }
}
#endif
