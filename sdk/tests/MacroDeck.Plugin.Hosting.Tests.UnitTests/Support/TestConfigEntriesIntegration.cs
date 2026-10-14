using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;

internal sealed class TestConfigEntriesIntegration : IPluginIntegration, IConfigEntryLifecycle
{
	public IReadOnlyList<IActionDefinition> Actions { get; } = [];

	public Task InitializeAsync(IIntegrationContext context)
	{
		return Task.CompletedTask;
	}

	public Task ShutdownAsync()
	{
		return Task.CompletedTask;
	}
}
