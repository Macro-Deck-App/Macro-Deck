using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeck.Localization;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Streamerbot.Actions;

internal sealed class ExecuteCodeTriggerActionDefinition : IDynamicOptionsActionDefinition
{
	internal const string TriggerParameterName = "trigger";
	internal const string ArgumentsParameterName = "args";

	private readonly Func<StreamerbotConnection?> _resolver;

	public ExecuteCodeTriggerActionDefinition(Func<StreamerbotConnection?> resolver)
	{
		_resolver = resolver;
	}

	public string Id => "execute-code-trigger";

	public LocalizedText Name => AppStrings.Integrations.Streamerbot.Actions.ExecuteCodeTriggerName();

	public LocalizedText Description => AppStrings.Integrations.Streamerbot.Actions.ExecuteCodeTriggerDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.DynamicChoice(TriggerParameterName,
			label: AppStrings.Integrations.Streamerbot.Actions.TriggerLabel(),
			description: AppStrings.Integrations.Streamerbot.Actions.TriggerDescription(),
			required: true),
		ActionParameter.KeyValue(ArgumentsParameterName,
			label: AppStrings.Integrations.Streamerbot.Actions.ArgumentsLabel(),
			description: AppStrings.Integrations.Streamerbot.Actions.TriggerArgumentsDescription())
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver);

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
		=> Task.FromResult(StreamerbotOptions.CodeTriggers(_resolver()));

	private sealed class Executor : IActionExecutor
	{
		private static readonly ILogger _logger =
			IntegrationLog.For<ExecuteCodeTriggerActionDefinition>(StreamerbotIntegration.IntegrationId);

		private readonly Func<StreamerbotConnection?> _resolver;

		public Executor(Func<StreamerbotConnection?> resolver)
		{
			_resolver = resolver;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var connection = _resolver();
			if (connection is null)
			{
				_logger.Warning("Streamer.bot code trigger skipped: not configured");
				return ActionResult.Failed(ActionErrorCodes.NotConnected,
					AppStrings.Integrations.Streamerbot.Errors.NotConnected());
			}

			if (StreamerbotActionValues.ReadText(context.Parameters, TriggerParameterName) is not { } trigger)
			{
				_logger.Warning("Streamer.bot code trigger skipped: no trigger selected");
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Streamerbot.Errors.NoTriggerSelected());
			}

			var arguments = StreamerbotActionValues.ReadArguments(context.Parameters, ArgumentsParameterName);
			await connection.ExecuteCodeTriggerAsync(trigger, arguments, context.CancellationToken);

			return ActionResult.Success();
		}
	}
}
