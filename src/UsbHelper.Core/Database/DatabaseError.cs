namespace UsbHelper.Core.Database;

/// <summary>One problem found while loading the hardware database.</summary>
/// <param name="File">Path relative to the database folder.</param>
/// <param name="Line">1-based line, or 0 when the problem is not tied to a line.</param>
public sealed record DatabaseError(string File, int Line, string Message)
{
    public override string ToString() => Line > 0 ? $"{File}:{Line}: {Message}" : $"{File}: {Message}";
}
