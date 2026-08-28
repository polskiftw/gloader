using Gelatin.Core.Models;

namespace Gelatin.Core.Authoring;

public readonly record struct DocumentHistoryEntry(GelDocument Document, long StateId);

public sealed class DocumentHistory
{
    private readonly LinkedList<DocumentHistoryEntry> _undo = [];
    private readonly LinkedList<DocumentHistoryEntry> _redo = [];
    private readonly int _maximumEntries;
    private readonly long _maximumBytes;
    private long _undoBytes;
    private long _redoBytes;

    public DocumentHistory(int maximumEntries = 30, long maximumBytes = 512L * 1024 * 1024)
    {
        _maximumEntries = Math.Max(1, maximumEntries);
        _maximumBytes = Math.Max(1024 * 1024, maximumBytes);
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Record(GelDocument current) => Record(current, 0);

    public void Record(GelDocument current, long stateId)
    {
        AddUndo(CloneEntry(current, stateId));
        ClearRedo();
    }

    public GelDocument Undo(GelDocument current) => Undo(current, 0).Document;

    public DocumentHistoryEntry Undo(GelDocument current, long stateId)
    {
        if (_undo.Last is null) return CloneEntry(current, stateId);
        AddRedo(CloneEntry(current, stateId));
        var result = _undo.Last.Value;
        _undoBytes -= Estimate(result.Document);
        _undo.RemoveLast();
        return CloneEntry(result.Document, result.StateId);
    }

    public GelDocument Redo(GelDocument current) => Redo(current, 0).Document;

    public DocumentHistoryEntry Redo(GelDocument current, long stateId)
    {
        if (_redo.Last is null) return CloneEntry(current, stateId);
        AddUndo(CloneEntry(current, stateId));
        var result = _redo.Last.Value;
        _redoBytes -= Estimate(result.Document);
        _redo.RemoveLast();
        return CloneEntry(result.Document, result.StateId);
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _undoBytes = 0;
        _redoBytes = 0;
    }

    private void AddUndo(DocumentHistoryEntry entry)
    {
        _undo.AddLast(entry);
        _undoBytes += Estimate(entry.Document);
        Trim(_undo, ref _undoBytes);
    }

    private void AddRedo(DocumentHistoryEntry entry)
    {
        _redo.AddLast(entry);
        _redoBytes += Estimate(entry.Document);
        Trim(_redo, ref _redoBytes);
    }

    private void ClearRedo()
    {
        _redo.Clear();
        _redoBytes = 0;
    }

    private void Trim(LinkedList<DocumentHistoryEntry> list, ref long bytes)
    {
        while (list.Count > _maximumEntries || (bytes > _maximumBytes && list.Count > 1))
        {
            var first = list.First!.Value;
            bytes -= Estimate(first.Document);
            list.RemoveFirst();
        }
    }

    private static DocumentHistoryEntry CloneEntry(GelDocument document, long stateId)
        => new(document.DeepClone(), stateId);

    private static long Estimate(GelDocument document)
        => document.PngBytes.LongLength + (document.RecoveryPngBytes?.LongLength ?? 0) +
           document.Config.Cores.Count * 256L +
           document.Config.RigidityStrokes.Sum(stroke => 64L + stroke.Points.Count * 24L);
}
