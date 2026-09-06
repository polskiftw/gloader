#if GLOADER_CLIENT
using Microsoft.Xna.Framework.Graphics;
using Terraria.DataStructures;
using Terraria.GameContent;

namespace AuthenticRaces.Rendering
{
    internal enum VanillaPlayerDrawKind
    {
        PlayerTexture,
        Hair,
        HairAlt
    }

    internal struct VanillaPlayerDrawSource
    {
        public VanillaPlayerDrawKind Kind;
        public int Slot;

        public VanillaPlayerDrawSource(VanillaPlayerDrawKind kind, int slot)
        {
            Kind = kind;
            Slot = slot;
        }
    }

    /// <summary>
    /// Identifies vanilla player-owned draw records by their source texture. Classification
    /// happens after Terraria has already calculated frame, position, rotation, shader and
    /// ordering, so later race renderers can preserve that work while substituting art.
    /// </summary>
    internal static class VanillaPlayerDrawClassifier
    {
        public static bool TryClassify(
            ref PlayerDrawSet drawInfo,
            DrawData drawData,
            out VanillaPlayerDrawSource source)
        {
            source = default(VanillaPlayerDrawSource);

            Texture2D texture = drawData.texture;
            var player = drawInfo.drawPlayer;
            if (texture == null || player == null)
                return false;

            int skinVariant = drawInfo.skinVar;
            if (skinVariant >= 0 &&
                skinVariant < TextureAssets.Players.GetLength(0))
            {
                int slotCount = TextureAssets.Players.GetLength(1);
                for (int slot = 0; slot < slotCount; slot++)
                {
                    var asset = TextureAssets.Players[skinVariant, slot];
                    if (asset != null && ReferenceEquals(asset.Value, texture))
                    {
                        source = new VanillaPlayerDrawSource(
                            VanillaPlayerDrawKind.PlayerTexture,
                            slot);
                        return true;
                    }
                }
            }

            int hair = player.hair;
            if (hair >= 0 && hair < TextureAssets.PlayerHair.Length)
            {
                var asset = TextureAssets.PlayerHair[hair];
                if (asset != null && ReferenceEquals(asset.Value, texture))
                {
                    source = new VanillaPlayerDrawSource(VanillaPlayerDrawKind.Hair, hair);
                    return true;
                }
            }

            if (hair >= 0 && hair < TextureAssets.PlayerHairAlt.Length)
            {
                var asset = TextureAssets.PlayerHairAlt[hair];
                if (asset != null && ReferenceEquals(asset.Value, texture))
                {
                    source = new VanillaPlayerDrawSource(VanillaPlayerDrawKind.HairAlt, hair);
                    return true;
                }
            }

            return false;
        }
    }
}
#endif
