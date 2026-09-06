using MacroDeck.Plugin.Testing.Conformance;
using MacroDeck.Plugin.Testing.Tests.ConformanceTests.Support;

namespace MacroDeck.Plugin.Testing.Tests.ConformanceTests;

/// <summary>
/// The full suite against the misbehaving fixture as a real, external process, with no
/// <c>MACRODECK_MISBEHAVE</c> flags set - well-behaved by default (see <c>MisbehavingIntegration</c>'s own
/// remarks) - and with zero Required failures. Complements <see cref="WellBehavedInProcessSuiteTests" /> in two
/// ways: it is the executable (managed) subject kind rather than in-process/self-registering, and it
/// declares the one slow action (<c>stall</c>) and the one log-flood action the Recommended checks
/// (MDC0502, MDC0504, MDC0801, MDC0802, MDC0505/MDC0805's siblings) need real cooperation to ever leave
/// Skipped - so this is also where those checks get proven to genuinely pass, not just skip cleanly.
/// </summary>
[TestFixture]
internal sealed class MisbehavingExecutableSuiteTests : ConformanceFixture
{
	protected override Task<ConformanceSubject> CreateSubjectAsync()
		=> Task.FromResult(
			ConformanceSubject.Executable(
				PluginLaunchSpec.ForExecutable(PluginLocator.FindMisbehavingPluginExecutable())));
}
