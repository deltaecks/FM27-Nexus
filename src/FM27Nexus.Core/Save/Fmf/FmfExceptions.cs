namespace FM27Nexus.Core.Save.Fmf;

public sealed class NotAFmSaveException : Exception
{
    public NotAFmSaveException(string message) : base(message) { }
}

public sealed class CorruptSaveException : Exception
{
    public CorruptSaveException(string message) : base(message) { }
}

public sealed class SaveChangedException : Exception
{
    public SaveChangedException(string message) : base(message) { }
}
