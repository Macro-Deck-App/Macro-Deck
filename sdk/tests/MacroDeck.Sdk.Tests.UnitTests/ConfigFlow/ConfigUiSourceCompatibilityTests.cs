using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Capabilities.Actions;
using MacroDeck.Plugin.Hosting.Capabilities.ConfigFlow;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Actions;
using MacroDeck.Plugin.Protocol.Capabilities.ConfigFlow;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeck.Sdk.Tests.UnitTests.ConfigFlow;

/// <summary>
/// S7b (issue #543): types written the way an author would have written them before <c>IUiConfigFlowProvider</c>
/// and <c>IUiConfigurableActionDefinition</c> existed - implementing only the pre-#543 members. The point
/// is not that they compile (a tautology any type check would prove); it is that the host's/plugin's real
/// describe mapping, run against these exact objects, reports the new flag <c>false</c> and the legacy
/// path still produces a working step - see <c>Variables/UserVariableApiCompatibilityTests</c> for the
/// same shape applied to a different pre-existing interface.
/// </summary>
[TestFixture]
public class ConfigUiSourceCompatibilityTests
{
	[Test]
	public async Task An_old_style_config_flow_provider_describes_ServesConfigUiTree_as_false_through_the_real_mapper()
	{
		var integration = new PreExistingConfigFlowIntegration();
		Assert.That(integration, Is.Not.InstanceOf<IUiConfigFlowProvider>());

		var handler = new ConfigFlowCapabilityHandler([integration], new PluginConfigFlowSessions(TimeProvider.System));

		var result = await handler.InvokeAsync(new CapabilityInvocation
			{
				Kind = CapabilityKinds.ConfigFlow,
				LocalId = ProviderCapabilityId.LocalId,
				Operation = CapabilityOperations.ConfigFlow.Describe,
				CorrelationId = "correlation-1",
				Services = new ServiceCollection().BuildServiceProvider()
			},
			CancellationToken.None);

		var described = result.Data!.Value.Deserialize<ConfigFlowDescribePayload>(PluginProtocolJson.Options)!;

		Assert.That(described.ServesConfigUiTree, Is.False);

		// The legacy path still works: starting the flow returns the declared step unchanged.
		var flow = ((IConfigFlowProvider)integration).CreateConfigFlow();
		var oldContext = new PreExistingConfigFlowContext();
		Assert.That(oldContext, Is.Not.InstanceOf<IConfigFlowEntryContext>());
		var start = await flow.StartAsync(oldContext, CancellationToken.None);
		Assert.That(start.NextStep!.Fields.Single().Name, Is.EqualTo("apiKey"));
	}

	[Test]
	public async Task An_old_style_action_describes_ConfiguresWithUiTree_as_false_through_the_real_mapper()
	{
		var action = new PreExistingAction();
		Assert.That(action, Is.Not.InstanceOf<IUiConfigurableActionDefinition>());

		var handler = new ActionsCapabilityHandler([new PreExistingActionIntegration(action)]);

		var result = await handler.InvokeAsync(new CapabilityInvocation
			{
				Kind = CapabilityKinds.Actions,
				LocalId = action.Id,
				Operation = CapabilityOperations.Actions.Describe,
				CorrelationId = "correlation-1",
				Services = new ServiceCollection().BuildServiceProvider()
			},
			CancellationToken.None);

		var described = result.Data!.Value.Deserialize<ActionCatalogPayload>(PluginProtocolJson.Options)!;

		var descriptor = described.Actions.Single(candidate => candidate.LocalId == action.Id);
		Assert.That(descriptor.ConfiguresWithUiTree, Is.False);
	}

	private sealed class PreExistingConfigFlowIntegration : IPluginIntegration, IConfigFlowProvider
	{
		public IReadOnlyList<IActionDefinition> Actions { get; } = [];

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;

		public IConfigFlow CreateConfigFlow() => new PreExistingConfigFlow();

		public bool AllowsMultipleConfigurations => true;
	}

	private sealed class PreExistingConfigFlow : IConfigFlow
	{
		public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
			=> Task.FromResult(ConfigFlowResult.Step(new ConfigFlowStep
			{
				StepId = "one", Fields = [ActionParameter.Secret("apiKey", label: "API key", required: true)]
			}));

		public Task<ConfigFlowResult> SubmitAsync(
			string stepId,
			IReadOnlyDictionary<string, object?> input,
			IConfigFlowContext context,
			CancellationToken cancellationToken)
			=> Task.FromResult(ConfigFlowResult.Complete("Entry"));
	}

	private sealed class PreExistingConfigFlowContext : IConfigFlowContext
	{
		public IOAuthSession OAuth => new NoOAuthSession();

		private sealed class NoOAuthSession : IOAuthSession
		{
			public string RedirectUri => "http://127.0.0.1/callback";

			public string State => "state";

			public string? AuthorizationCode => null;
		}
	}

	private sealed class PreExistingAction : IActionDefinition
	{
		public string Id => "legacy-action";

		public LocalizedText Name => "Legacy action";

		public LocalizedText Description => "An action written before configuration trees existed.";

		public IReadOnlyList<ActionParameter> Parameters { get; } = [ActionParameter.Text("value")];

		public IActionExecutor CreateExecutor() => new Executor();

		private sealed class Executor : IActionExecutor
		{
			public Task<ActionResult> ExecuteAsync(ActionExecutionContext context) => ActionResult.SucceededTask;
		}
	}

	private sealed class PreExistingActionIntegration(IActionDefinition action) : IPluginIntegration
	{
		public IReadOnlyList<IActionDefinition> Actions { get; } = [action];

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;
	}
}
