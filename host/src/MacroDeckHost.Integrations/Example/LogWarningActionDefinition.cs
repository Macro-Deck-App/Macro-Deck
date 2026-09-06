using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Example;

public class LogWarningActionDefinition : IActionDefinition
{
	public string Id => "log-warning";
	public LocalizedText Name => AppStrings.Integrations.Example.Actions.LogWarningName();
	public LocalizedText Description => AppStrings.Integrations.Example.Actions.LogWarningDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		new()
		{
			Name = "message",
			Type = ActionParameterType.String,
			Description = AppStrings.Integrations.Example.Actions.WarningMessageDescription()
		}
	];

	public IActionExecutor CreateExecutor() => new LogWarningActionExecutor();

	private sealed class LogWarningActionExecutor : IActionExecutor
	{
		private static readonly ILogger _logger =
			IntegrationLog.For<LogWarningActionExecutor>(ExampleIntegration.IntegrationId);

		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var message = context.Parameters.TryGetValue("message", out var value)
				? value.ToString() ?? "Warning from Example Integration!"
				: "Warning from Example Integration!";

			_logger.Warning("[ExampleIntegration] {Message}", message);
			return ActionResult.SucceededTask;
		}
	}
}
