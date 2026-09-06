#if GLOADER_CLIENT
using AuthenticRaces.Core;
using Terraria.DataStructures;

namespace AuthenticRaces.Rendering
{
    /// <summary>
    /// Smallest useful race renderer: replace one vanilla player texture slot with one
    /// race PNG sheet while preserving Terraria's completed DrawData transform/color/shader.
    /// </summary>
    internal sealed class PlayerTextureSheetRenderer : RaceRenderer
    {
        private readonly string _raceAssetName;
        private readonly int _playerTextureSlot;
        private readonly string _sheetPath;

        public PlayerTextureSheetRenderer(
            string raceAssetName,
            int playerTextureSlot,
            string sheetPath)
        {
            _raceAssetName = raceAssetName;
            _playerTextureSlot = playerTextureSlot;
            _sheetPath = sheetPath;
        }

        public override void Rewrite(
            ref PlayerDrawSet drawInfo,
            Race race,
            RaceAppearanceData appearance)
        {
            var player = drawInfo.drawPlayer;
            if (player == null || drawInfo.DrawDataCache == null)
                return;

            Microsoft.Xna.Framework.Graphics.Texture2D replacement = null;

            for (int i = 0; i < drawInfo.DrawDataCache.Count; i++)
            {
                var drawData = drawInfo.DrawDataCache[i];
                if (!VanillaPlayerDrawClassifier.TryClassify(
                        ref drawInfo,
                        drawData,
                        out var source) ||
                    source.Kind != VanillaPlayerDrawKind.PlayerTexture ||
                    source.Slot != _playerTextureSlot)
                {
                    continue;
                }

                if (replacement == null)
                {
                    replacement = RaceTextureLoader.GetRaceSheet(
                        _raceAssetName,
                        female: !player.Male,
                        _sheetPath);
                }

                drawData.texture = replacement;
                drawInfo.DrawDataCache[i] = drawData;
            }
        }
    }
}
#endif
