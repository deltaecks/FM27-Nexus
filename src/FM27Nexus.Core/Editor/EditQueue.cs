namespace FM27Nexus.Core.Editor;

public enum EditState
{
    Pending,
    Applied,
    Undone,
    Blocked
}

public sealed class PendingEdit
{
    public required string Id { get; init; }
    public required string Entity { get; init; }
    public required int Index { get; init; }
    public required string Field { get; init; }
    public required long NewValue { get; init; }
    public long? OldValue { get; set; }
    public EditState State { get; set; } = EditState.Pending;
    public string? BlockReason { get; set; }
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
    public bool IsFreeze { get; init; }
}

public sealed class EditQueue
{
    private readonly List<PendingEdit> _edits = [];
    private readonly Stack<string> _undo = new();

    public IReadOnlyList<PendingEdit> Edits => _edits;

    public PendingEdit Stage(string entity, int index, string field, long newValue, bool isFreeze = false)
    {
        var edit = new PendingEdit
        {
            Id = Guid.NewGuid().ToString("N"),
            Entity = entity,
            Index = index,
            Field = field,
            NewValue = newValue,
            IsFreeze = isFreeze
        };
        _edits.Add(edit);
        return edit;
    }

    public IReadOnlyList<PendingEdit> Pending => _edits.Where(e => e.State == EditState.Pending).ToList();
    public IReadOnlyList<PendingEdit> Applied => _edits.Where(e => e.State == EditState.Applied).ToList();

    public int PendingFreezeCount => Pending.Count(e => e.IsFreeze);

    public void MarkApplied(string id, long? oldValue)
    {
        var edit = _edits.First(e => e.Id == id);
        edit.OldValue = oldValue;
        edit.State = EditState.Applied;
        _undo.Push(id);
    }

    public void MarkBlocked(string id, string reason)
    {
        var edit = _edits.First(e => e.Id == id);
        edit.State = EditState.Blocked;
        edit.BlockReason = reason;
    }

    public PendingEdit? PopUndo()
    {
        while (_undo.Count > 0)
        {
            var id = _undo.Pop();
            var edit = _edits.FirstOrDefault(e => e.Id == id && e.State == EditState.Applied);
            if (edit is not null)
                return edit;
        }

        return null;
    }

    public void MarkUndone(string id)
    {
        var edit = _edits.First(e => e.Id == id);
        edit.State = EditState.Undone;
    }
}
