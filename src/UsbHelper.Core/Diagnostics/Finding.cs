namespace UsbHelper.Core.Diagnostics;

public enum FactKind
{
    /// <summary>Reported by the operating system: errors, disconnections.</summary>
    DetectedFact,

    /// <summary>From a verified or partially verified computer profile.</summary>
    VerifiedDatabaseFact,

    /// <summary>A configured limit is exceeded, so the setup may not work reliably.</summary>
    PredictedRisk,

    /// <summary>Missing or unverified information.</summary>
    Unknown,
}

/// <summary>One warning, error or note, naming what it is about and why.</summary>
/// <param name="SubjectId">Id of the node (device, hub, port, controller), or <see cref="DiagnosticEvaluator.ComputerId"/>.</param>
public sealed record Finding(FactKind Kind, string SubjectId, string SubjectName, string Message)
{
    /// <summary>
    /// A detected problem: an operating system error, a disconnect or a reconnect. Other detected facts, such as the
    /// active display count, are notes.
    /// </summary>
    public bool IsProblem { get; init; }

    /// <summary>
    /// The message without saying that a limit is unverified, for the window (owner decision in #100); the log keeps
    /// <see cref="Message"/>. The message itself when it says nothing about verification.
    /// </summary>
    public string Brief { get; init; } = Message;

    /// <summary>
    /// For a limit exceeded: every part of the count with its origin (#326), such as
    /// "computer_hub 1 platforms/macos.yaml apple-silicon". A part with an unknown count makes the count "at least".
    /// Empty for other findings.
    /// </summary>
    public IReadOnlyList<Topology.CountPart> CountParts { get; init; } = [];

    /// <summary>Equal when kind, subject, message, brief and problem are: the count parts explain a finding, they do not make it another one.</summary>
    public bool Equals(Finding? other) =>
        other is not null && Kind == other.Kind && SubjectId == other.SubjectId && SubjectName == other.SubjectName
        && Message == other.Message && Brief == other.Brief && IsProblem == other.IsProblem;

    public override int GetHashCode() => HashCode.Combine(Kind, SubjectId, SubjectName, Message, Brief, IsProblem);

    /// <summary>
    /// A real finding (owner decision in #85): a limit or recommendation exceeded, a display problem, or a detected
    /// problem. Unknowns and database facts are not real findings; they belong in the log only.
    /// </summary>
    public bool IsReal => Kind == FactKind.PredictedRisk || IsProblem;
}
