using MacroDeck.Plugin.Testing.Tests.ConformanceTests.Support;

namespace MacroDeck.Plugin.Testing.Tests.ConformanceTests;

/// <summary>
/// Releases <see cref="SharedInProcessConformanceRun" />'s subject, if any fixture in this namespace ever
/// actually triggered a run. A <see cref="SetUpFixtureAttribute" /> runs its <see cref="OneTimeTearDownAttribute" />
/// after every other fixture in the same namespace has finished - since every fixture in this assembly
/// shares this one namespace, that is what makes this the right, and only, place to dispose a subject
/// several of them may still be reading from.
/// </summary>
[SetUpFixture]
internal sealed class ConformanceTestsAssemblyTeardown
{
	[OneTimeTearDown]
	public Task DisposeSharedInProcessRunAsync() => SharedInProcessConformanceRun.DisposeAsync();
}
