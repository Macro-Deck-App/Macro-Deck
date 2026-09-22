using System.Text.Json;
using MacroDeck.Plugin.Testing.Conformance;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Messaging;

namespace MacroDeck.Plugin.Testing.Tests.ConformanceTests;

[TestFixture]
internal sealed class MessagingPluginInProcessSuiteTests : ConformanceFixture
{
	protected override Task<ConformanceSubject> CreateSubjectAsync()
		=> Task.FromResult(ConformanceSubject.InProcess(builder => builder.RegisterIntegration(_ => new ChannelOnlyIntegration()),
			new PluginTestManifest(id: "app.macro-deck.messaging-test-plugin", name: "Messaging Test Plugin", version: "1.0.0")));

	[TestCase("MDC0302", ConformanceOutcome.Passed)]
	[TestCase("MDC0303", ConformanceOutcome.Passed)]
	[TestCase("MDC0501", ConformanceOutcome.Skipped)]
	public void The_messaging_capability_is_handled_by_the_capability_checks(string checkId, ConformanceOutcome expected)
		=> Assert.That(Report.Results.Single(result => result.Id == checkId).Result.Outcome, Is.EqualTo(expected));

	private sealed class ChannelOnlyIntegration : IPluginIntegration
	{
		public IReadOnlyList<IActionDefinition> Actions => [];

		public async Task InitializeAsync(IIntegrationContext context)
			=> await context.Messages.HandleRequestsAsync("conformance.ping",
				(_, _) => Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement("pong")));

		public Task ShutdownAsync() => Task.CompletedTask;
	}
}
