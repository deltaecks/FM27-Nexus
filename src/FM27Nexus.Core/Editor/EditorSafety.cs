namespace FM27Nexus.Core.Editor;

public static class EditorSafety
{
    public const int FreezeWarnThreshold = 5;

    public static string? Evaluate(PendingEdit edit, EditQueue queue, EditorContext context)
    {
        if (edit.IsFreeze && queue.PendingFreezeCount > FreezeWarnThreshold)
            return $"Warning: more than {FreezeWarnThreshold} freezes staged — this can degrade CPU. Confirm to proceed.";

        if (string.Equals(edit.Entity, "Contract", StringComparison.OrdinalIgnoreCase)
            && string.Equals(edit.Field, "Terminate", StringComparison.OrdinalIgnoreCase)
            && context.IsHumanManagerContract(edit.Index))
        {
            return "Blocked: terminating the active human manager's own contract is a known crash-prone edit.";
        }

        return null;
    }
}

public sealed class EditorContext
{
    public int? HumanManagerContractIndex { get; init; }

    public bool IsHumanManagerContract(int index) =>
        HumanManagerContractIndex is not null && HumanManagerContractIndex.Value == index;
}
