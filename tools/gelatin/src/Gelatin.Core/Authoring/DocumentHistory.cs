using Gelatin.Core.Models;

namespace Gelatin.Core.Authoring;

public sealed class DocumentHistory
{
    private readonly LinkedList<GelDocument> _undo = [];
    private readonly LinkedList<GelDocument> _redo = [];
    private readonly int _maximumEntries;
    private readonly long _maximumBytes;
    private long _undoBytes;

    public DocumentHistory(int maximumEntries = 30, long maximumBytes = 512L * 1024 * 1024)
    {
        _maximumEntries = Math.Max(1, maximumEntries);
        _maximumBytes = Math.Max(1024 * 1024, maximumBytes);
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Record(GelDocument current)
    {
        var snapshot = current.DeepClone();
        _undo.AddLast(snapshot);
        _undoBytes += Estimate(snapshot);
        _redo.Clear();
        while (_undo.Count > _maximumEntries || (_undoBytes > _maximumBytes && _undo.Count > 1))
        {
            var first = _undo.First!.Value;
            _undoBytes -= Estimate(first);
            _undo.RemoveFirst();
        }
    }

    public GelDocument Undo(GelDocument current)
    {
        if (_undo.Last is null) return current;
        _redo.AddLast(current.DeepClone());
        var result = _undo.Last.Value;
        _undoBytes -= Estimate(result);
        _undo.RemoveLast();
        return result.DeepClone();
    }

    public GelDocument Redo(GelDocument current)
    {
        if (_redo.Last is null) return current;
        RecordWithoutClearingRedo(current.DeepClone());
        var result = _redo.Last.Value;
        _redo.RemoveLast();
        return result.DeepClone();
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _undoBytes = 0;
    }

    private void RecordWithoutClearingRedo(GelDocument document)
    {
        _undo.AddLast(document);
        _undoBytes += Estimate(document);
    }

    private static long Estimate(GelDocument document)
        => document.PngBytes.LongLength + document.Config.Cores.Count * 256L + document.Config.RigidityStrokes.Sum(stroke => 64L + stroke.Points.Count * 24L);
}
