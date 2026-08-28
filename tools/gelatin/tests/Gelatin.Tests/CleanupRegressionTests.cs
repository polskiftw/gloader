using Gelatin.App;
using Gelatin.Core;
using Gelatin.Core.Authoring;
using Gelatin.Core.Imaging;
using Gelatin.Core.Models;
using SkiaSharp;

namespace Gelatin.Tests;

public sealed class CleanupRegressionTests
{
    [Fact]
    public void HistoryPreservesStateIdentityAcrossUndoRedo()
    {
        var history = new DocumentHistory();
        var document = TestAssets.Document();
        history.Record(document, 41);
        document.Config.AssetName = "Changed";

        var undone = history.Undo(document, 42);
        Assert.Equal(41, undone.StateId);
        Assert.Equal("Round Trip Gel", undone.Document.Config.AssetName);

        var redone = history.Redo(undone.Document, undone.StateId);
        Assert.Equal(42, redone.StateId);
        Assert.Equal("Changed", redone.Document.Config.AssetName);
    }

    [Fact]
    public async Task UndoBackToSavedStateClearsDirtyFlag()
    {
        var controller = new DocumentController();
        var file = Path.Combine(Path.GetTempPath(), $"gelatin-cleanup-{Guid.NewGuid():N}.gel");
        try
        {
            await controller.SaveAsync(file);
            Assert.False(controller.IsDirty);
            controller.Mutate(config => config.AssetName = "Changed", DocumentChangeKind.Metadata);
            Assert.True(controller.IsDirty);
            controller.Undo();
            Assert.False(controller.IsDirty);
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }

    [Fact]
    public async Task EditingAfterSaveStartsRemainsDirtyWhenOlderSnapshotFinishes()
    {
        var controller = new DocumentController();
        var file = Path.Combine(Path.GetTempPath(), $"gelatin-save-race-{Guid.NewGuid():N}.gel");
        try
        {
            var saving = controller.SaveAsync(file);
            controller.Mutate(config => config.AssetName = "Edited during save", DocumentChangeKind.Metadata);
            await saving;
            Assert.True(controller.IsDirty);
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }

    [Fact]
    public void ProductVersionComesFromSharedAssemblyVersion()
    {
        Assert.Equal("0.1.6", GelatinProduct.Version);
        Assert.Equal(GelatinProduct.Version, new AuthoringConfig().ToolVersion);
    }

    [Fact]
    public void RawImageTransformsHonorCancellation()
    {
        var png = TestAssets.Png(32, 32, (_, _) => SKColors.White);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            RawRgbaTransforms.RemoveBackgroundCancellable(png, SKColors.White, 0.1, 0.1, cancellation.Token));
    }
}
