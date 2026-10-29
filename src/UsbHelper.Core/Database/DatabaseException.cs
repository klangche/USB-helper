namespace UsbHelper.Core.Database;

/// <summary>The hardware database is invalid. Lists every problem found, not only the first.</summary>
public sealed class DatabaseException(IReadOnlyList<DatabaseError> errors)
    : Exception(string.Join(Environment.NewLine, errors))
{
    public IReadOnlyList<DatabaseError> Errors { get; } = errors;
}
