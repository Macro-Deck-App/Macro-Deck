using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Example;

public class LogCounterActionDefinition : IActionDefinition
{
	// Shared counter across all executor instances; accessed via Interlocked for thread safety
	private static int _counter;

	public string Id => "log-counter";
	public LocalizedText Name => AppStrings.Integrations.Example.Actions.LogCounterName();
	public LocalizedText Description => AppStrings.Integrations.Example.Actions.LogCounterDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Slider("increment",
			min: 1,
			max: 100,
			step: 1,
			label: AppStrings.Integrations.Example.Actions.IncrementLabel(),
			description: AppStrings.Integrations.Example.Actions.IncrementDescription(),
			defaultValue: 1)
	];

	public IActionExecutor CreateExecutor() => new LogCounterActionExecutor();

	private sealed class LogCounterActionExecutor : IActionExecutor
	{
		private static readonly ILogger _logger =
			IntegrationLog.For<LogCounterActionExecutor>(ExampleIntegration.IntegrationId);

		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var increment = context.Parameters.TryGetValue("increment", out var value) && value is double d
				? (int)d
				: 1;

			var newValue = Interlocked.Add(ref _counter, increment);
			_logger.Information("[ExampleIntegration] Counter incremented by {Increment}, new value: {Counter}",
				increment,
				newValue);
			return ActionResult.SucceededTask;
		}
	}
}
