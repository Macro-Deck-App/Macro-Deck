using MacroDeck.Plugin.Testing.Conformance;
using MacroDeck.Plugin.Testing.Tests.ConformanceTests.Support;

namespace MacroDeck.Plugin.Testing.Tests.ConformanceTests;

/// <summary>
/// The full suite against the well-behaved fixture as an in-process subject, with zero Required
/// failures - the acceptance criterion the issue names explicitly. Reuses
/// <see cref="Support.SharedInProcessConformanceRun" /> rather than running its own copy of the same suite
/// against the same subject: <see cref="ReportHonestyTests" /> and the in-process third of
/// <see cref="ReusabilityTests.SameCheckIdsAcrossAllThreeSubjectKinds" /> need exactly this same report too.
/// </summary>
[TestFixture]
internal sealed class WellBehavedInProcessSuiteTests : ConformanceFixture
{
	protected override Task<ConformanceReport> GetReportAsync() => SharedInProcessConformanceRun.GetReportAsync();
}
