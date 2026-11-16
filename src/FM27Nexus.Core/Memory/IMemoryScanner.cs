namespace FM27Nexus.Core.Memory;

public interface IMemoryScanner
{
    bool IsAttached { get; }
    int? ProcessId { get; }
    string? ExecutablePath { get; }
    AttachResult Attach(int processId, string expectedExecutablePath);
    void Detach();
    ReadResult ReadField(string entity, int index, string field);
    WriteResult WriteField(string entity, int index, string field, long value, bool force = false);
    ScanValidationResult ValidateReadSanity();
}

public sealed class AttachResult
{
    public required bool Ok { get; init; }
    public string? Error { get; init; }
}

public sealed class ReadResult
{
    public required bool Ok { get; init; }
    public string? Error { get; init; }
    public long? Value { get; init; }
    public bool WithinExpectedBounds { get; init; }
}

public sealed class WriteResult
{
    public required bool Ok { get; init; }
    public string? Error { get; init; }
}

public sealed class ScanValidationResult
{
    public required bool Ok { get; init; }
    public string? Error { get; init; }
    public int FieldsChecked { get; init; }
    public int FieldsPassed { get; init; }
}
