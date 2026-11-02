using UsbHelper.Core.Diagnostics;

namespace UsbHelper.Core.Tests.Diagnostics;

public class FindingTests
{
    [Theory]
    [InlineData(FactKind.PredictedRisk, false, true)]
    [InlineData(FactKind.DetectedFact, true, true)]
    [InlineData(FactKind.DetectedFact, false, false)] // A note, such as the active display count.
    [InlineData(FactKind.VerifiedDatabaseFact, false, false)]
    [InlineData(FactKind.Unknown, false, false)]
    public void OnlyRisksAndDetectedProblemsAreReal(FactKind kind, bool isProblem, bool isReal) =>
        Assert.Equal(isReal, new Finding(kind, "id", "name", "message") { IsProblem = isProblem }.IsReal);
}
