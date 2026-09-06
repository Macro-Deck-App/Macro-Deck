using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Example;

[MacroDeckIntegration]
public class ExampleIntegration : IIntegration, IVariableProvider, IConfigFlowProvider, ISampleIntegration
{
	public const string IntegrationId = "app.macro-deck.example";

	private static readonly ILogger _logger = IntegrationLog.For<ExampleIntegration>(IntegrationId);

	private DateTime _startedAt = DateTime.UtcNow;

	public string Id => IntegrationId;
	public LocalizedText Name => AppStrings.Integrations.Example.Name();
	public string Version => "1.0.0";
	public bool IsInitialized { get; private set; }

	public IReadOnlyList<IActionDefinition> Actions { get; } =
	[
		new LogInfoActionDefinition(),
		new LogWarningActionDefinition(),
		new LogCounterActionDefinition(),
		new ParameterShowcaseActionDefinition()
	];

	public IReadOnlyList<VariableDefinition> Variables { get; } =
	[
		VariableDefinition.Eager("example_random", VariableType.Numeric, 0, TimeSpan.FromSeconds(2))
			with
			{
				DisplayName = AppStrings.Integrations.Example.Variables.Random()
			},
		VariableDefinition.Eager("example_uptime_seconds", VariableType.Numeric, 0, TimeSpan.FromSeconds(1))
			with
			{
				DisplayName = AppStrings.Integrations.Example.Variables.UptimeSeconds()
			},
		VariableDefinition.Eager("example_status", VariableType.Text, refreshInterval: TimeSpan.FromSeconds(5))
			with
			{
				DisplayName = AppStrings.Integrations.Example.Variables.Status()
			},
		VariableDefinition.Eager("example_flaky", VariableType.Text, refreshInterval: TimeSpan.FromSeconds(3))
			with
			{
				DisplayName = AppStrings.Integrations.Example.Variables.Flaky()
			},
	];

	public Task InitializeAsync(IIntegrationContext context)
	{
		_startedAt = DateTime.UtcNow;
		_logger.Information("Example Integration initialized");
		IsInitialized = true;
		return Task.CompletedTask;
	}

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		object? value = localId switch
		{
			"example-random" => Random.Shared.Next(0, 100),
			"example-uptime-seconds" => (int)(DateTime.UtcNow - _startedAt).TotalSeconds,
			"example-status" => "online",
			"example-flaky" => Random.Shared.Next(0, 2) == 0 ? null : "available",
			_ => null
		};

		return ValueTask.FromResult(VariableReading.Of(value));
	}

	public IConfigFlow CreateConfigFlow() => new ExampleConfigFlow();

	public Task ShutdownAsync()
	{
		_logger.Information("Example Integration shut down");
		return Task.CompletedTask;
	}
}
