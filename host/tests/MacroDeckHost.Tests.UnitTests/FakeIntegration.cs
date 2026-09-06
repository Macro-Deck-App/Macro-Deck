using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Localization;

namespace MacroDeckHost.Tests.UnitTests;

internal sealed class FakeIntegration : IIntegration
{
	// Reverse-domain, because registration rejects anything else: a single-segment id would describe an
	// integration that can no longer exist, and would silently drop out of every qualified lookup.
	public string Id { get; init; } = "test.integration";
	public LocalizedText Name => "Test Integration";
	public string Version => "1.0.0";
	public IReadOnlyList<IActionDefinition> Actions { get; init; } = [];

	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;
	public Task ShutdownAsync() => Task.CompletedTask;
	public bool IsInitialized => true;
}
