using System.Text.Json;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Events.Handlers;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Application.Triggers.Providers;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Integrations.Http;
using MacroDeckHost.Integrations.Http.Actions;
using MacroDeckHost.Integrations.Http.Client;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Triggers;
using MacroDeck.Sdk.Identity;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Http;

[TestFixture]
internal sealed class HttpCaptureVariableChangedTriggerTests
{
	private const string Variable = "activeplatform";

	[TestCase("SWIT", true)]
	[TestCase("SWIT\n", true)]
	[TestCase("SWIT\r\n", true)]
	[TestCase("OTHER\n", false)]
	public async Task A_captured_body_runs_a_changed_to_is_SWIT_trigger_only_when_it_reads_SWIT(
		string body,
		bool expectedToRun)
	{
		using var targets = new ActionVariableTargets(HttpIntegration.IntegrationId);
		await targets.CreateUserVariable(Variable, VariableType.Text, "PREVIOUS");
		var accessor = new HttpVariableAccessor
		{
			Current = targets.IntegrationVariables,
			UserVariables = targets.UserVariables
		};

		await HttpResponseCapture.ApplyAsync(accessor,
			new Dictionary<string, string>(StringComparer.Ordinal) { [Variable] = "$body" },
			new HttpResponseSnapshot(200,
				new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
				body,
				false,
				"text/plain",
				5),
			statusExpected: true,
			new LoggerConfiguration().CreateLogger());

		var bus = new RecordingEventBus();
		await new VariableValueChangedEventTriggerHandler(bus).Handle(
			new VariableValueChangedNotification((await targets.Find(Variable))!, "PREVIOUS"),
			CancellationToken.None);
		var occurrence = bus.Published.Single();

		var definition = new CoreEventProvider().EventDefinitions.Single(e => e.Id == EventIds.VariableChanged);
		var descriptor = new EventDefinitionDescriptor(QualifiedId.Parse(occurrence.EventId),
			CoreEventProvider.ProviderIdValue,
			"Macro Deck",
			false,
			definition);
		var subscription = new EventSubscription(EventTriggerOwner.ForWidget(Guid.NewGuid()),
			"t1",
			occurrence.EventId,
			new Dictionary<string, EventConfigurationValue>(StringComparer.Ordinal)
			{
				["variable"] = new(JsonSerializer.SerializeToElement(Variable), "=="),
				["value"] = new(JsonSerializer.SerializeToElement("SWIT"), "==")
			},
			null);
		var context = await new VariableTemplateRenderer(new VariableRegistry())
			.CreateContextAsync(VariableScope.Global, null);

		var matches = new EventSubscriptionMatcher(new ActionConditionEvaluator(
				new VariableTemplateRenderer(new VariableRegistry())))
			.Matches(subscription, descriptor, context.WithEvent(occurrence.Parameters));

		Assert.That(matches, Is.EqualTo(expectedToRun));
	}
}
