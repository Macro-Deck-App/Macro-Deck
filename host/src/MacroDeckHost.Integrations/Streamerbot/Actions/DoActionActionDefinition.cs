using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeck.Localization;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Streamerbot.Actions;

internal sealed class DoActionActionDefinition : IDynamicOptionsActionDefinition
{
	internal const string ActionParameterName = "action";
	internal const string ArgumentsParameterName = "args";

	private readonly Func<StreamerbotConnection?> _resolver;

	public DoActionActionDefinition(Func<StreamerbotConnection?> resolver)
	{
		_resolver = resolver;
	}

	public string Id => "do-action";

	public LocalizedText Name => AppStrings.Integrations.Streamerbot.Actions.DoActionName();

	public LocalizedText Description => AppStrings.Integrations.Streamerbot.Actions.DoActionDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.DynamicChoice(ActionParameterName,
			label: AppStrings.Integrations.Streamerbot.Actions.DoActionActionLabel(),
			description: AppStrings.Integrations.Streamerbot.Actions.DoActionActionDescription(),
			required: true),
		ActionParameter.KeyValue(ArgumentsParameterName,
			label: AppStrings.Integrations.Streamerbot.Actions.ArgumentsLabel(),
			description: AppStrings.Integrations.Streamerbot.Actions.DoActionArgumentsDescription())
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver);

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
		=> Task.FromResult(StreamerbotOptions.Actions(_resolver()));

	private sealed class Executor : IActionExecutor
	{
		private static readonly ILogger _logger =
			IntegrationLog.For<DoActionActionDefinition>(StreamerbotIntegration.IntegrationId);

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
				_logger.Warning("Streamer.bot action skipped: not configured");
				return ActionResult.Failed(ActionErrorCodes.NotConnected,
					AppStrings.Integrations.Streamerbot.Errors.NotConnected());
			}

			if (StreamerbotActionValues.ReadText(context.Parameters, ActionParameterName) is not { } action)
			{
				_logger.Warning("Streamer.bot action skipped: no action selected");
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Streamerbot.Errors.NoActionSelected());
			}

			var arguments = StreamerbotActionValues.ReadArguments(context.Parameters, ArgumentsParameterName);
			await connection.DoActionAsync(action, arguments, context.CancellationToken);

			return ActionResult.Success();
		}
	}
}
