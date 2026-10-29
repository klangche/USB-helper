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
public sealed record Finding(FactKind Kind, string SubjectId, string SubjectName, string Message);
