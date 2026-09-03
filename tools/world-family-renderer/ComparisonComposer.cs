using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace WorldFamilyRenderer;

internal static class ComparisonComposer
{
    public static string Compose(
        IReadOnlyList<RenderedWorld> rendered,
        string outputPath,
        CancellationToken cancellationToken)
    {
        if (rendered.Count != 6)
            throw new ArgumentException("Exactly six rendered worlds are required.", nameof(rendered));

        var images = rendered.Select(item => Image.FromFile(item.PngPath)).ToList();
        try
        {
            int widest = images.Max(image => image.Width);
            float uiScale = Math.Clamp(widest / 1920f, 0.72f, 2.0f);
            int margin = (int)Math.Round(38 * uiScale);
            int topPadding = (int)Math.Round(12 * uiScale);
            int titleHeight = (int)Math.Round(40 * uiScale);
            int subtitleHeight = (int)Math.Round(27 * uiScale);
            int labelHeight = (int)Math.Round(28 * uiScale);
            int gap = (int)Math.Round(11 * uiScale);
            int bottomPadding = (int)Math.Round(18 * uiScale);

            int canvasWidth = Math.Max((int)Math.Round(820 * uiScale), widest + margin * 2);
            int canvasHeight = topPadding + titleHeight + subtitleHeight + gap;
            for (int i = 0; i < images.Count; i++)
                canvasHeight += labelHeight + images[i].Height + gap;
            canvasHeight += bottomPadding;

            using var canvas = new Bitmap(canvasWidth, canvasHeight, PixelFormat.Format32bppArgb);
            using Graphics graphics = Graphics.FromImage(canvas);
            graphics.Clear(Color.FromArgb(9, 16, 27));
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;

            using var titleFont = new Font("Segoe UI", Math.Max(14f, 20f * uiScale), FontStyle.Bold, GraphicsUnit.Pixel);
            using var subtitleFont = new Font("Segoe UI", Math.Max(10f, 13f * uiScale), FontStyle.Bold, GraphicsUnit.Pixel);
            using var labelFont = new Font("Segoe UI", Math.Max(10f, 13f * uiScale), FontStyle.Bold, GraphicsUnit.Pixel);
            using var titleBrush = new SolidBrush(Color.FromArgb(240, 242, 247));
            using var accentBrush = new SolidBrush(Color.FromArgb(105, 180, 255));
            using var labelBrush = new SolidBrush(Color.FromArgb(236, 239, 244));
            using var borderPen = new Pen(Color.FromArgb(65, 88, 116), Math.Max(1f, uiScale));
            using var centerFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

            int y = topPadding;
            graphics.DrawString(
                "Expanded Worlds — Same Seed, All Sizes",
                titleFont,
                titleBrush,
                new RectangleF(0, y, canvasWidth, titleHeight),
                centerFormat);
            y += titleHeight;
            graphics.DrawString(
                "Vanilla + XL + Huge + THICC",
                subtitleFont,
                accentBrush,
                new RectangleF(0, y, canvasWidth, subtitleHeight),
                centerFormat);
            y += subtitleHeight + gap;

            for (int i = 0; i < images.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RenderedWorld item = rendered[i];
                Image image = images[i];
                string label = $"{item.Preset.Number}. {item.Preset.Name} — {item.Preset.Width:N0} x {item.Preset.Height:N0}";
                graphics.DrawString(
                    label,
                    labelFont,
                    labelBrush,
                    new RectangleF(0, y, canvasWidth, labelHeight),
                    centerFormat);
                y += labelHeight;

                int x = (canvasWidth - image.Width) / 2;
                graphics.DrawImageUnscaled(image, x, y);
                graphics.DrawRectangle(borderPen, x, y, image.Width - 1, image.Height - 1);
                y += image.Height + gap;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            canvas.Save(outputPath, ImageFormat.Png);
            return outputPath;
        }
        finally
        {
            foreach (Image image in images)
                image.Dispose();
        }
    }
}
