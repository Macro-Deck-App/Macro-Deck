using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Example;

public class LogInfoActionDefinition : IActionDefinition
{
	public string Id => "log-info";
	public LocalizedText Name => AppStrings.Integrations.Example.Actions.LogInfoName();
	public LocalizedText Description => AppStrings.Integrations.Example.Actions.LogInfoDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Text("message",
			label: AppStrings.Integrations.Example.Actions.MessageLabel(),
			description: AppStrings.Integrations.Example.Actions.MessageDescription(),
			placeholder: "Hello from Example Integration!",
			required: true)
	];

	public IActionExecutor CreateExecutor() => new LogInfoActionExecutor();

	private sealed class LogInfoActionExecutor : IActionExecutor
	{
		private static readonly ILogger _logger =
			IntegrationLog.For<LogInfoActionExecutor>(ExampleIntegration.IntegrationId);

		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var message = context.Parameters.TryGetValue("message", out var value)
				? value.ToString() ?? "Hello from Example Integration!"
				: "Hello from Example Integration!";

			_logger.Information("[ExampleIntegration] {Message}", message);
			return ActionResult.SucceededTask;
		}
	}
}
