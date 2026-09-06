using System.Text.Json;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using MacroDeck.Localization;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Streamerbot.Actions;

internal sealed class GetGlobalVariableActionDefinition : IActionDefinition
{
	internal const string VariableParameterName = "variable";
	internal const string PersistedParameterName = "persisted";
	internal const string TargetParameterName = "target";

	private readonly Func<StreamerbotConnection?> _resolver;
	private readonly StreamerbotVariableAccessor _variables;

	public GetGlobalVariableActionDefinition(
		Func<StreamerbotConnection?> resolver,
		StreamerbotVariableAccessor variables)
	{
		_resolver = resolver;
		_variables = variables;
	}

	public string Id => "get-global-variable";

	public LocalizedText Name => AppStrings.Integrations.Streamerbot.Actions.GetGlobalVariableName();

	public LocalizedText Description => AppStrings.Integrations.Streamerbot.Actions.GetGlobalVariableDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Text(VariableParameterName,
			label: AppStrings.Integrations.Streamerbot.Actions.VariableLabel(),
			description: AppStrings.Integrations.Streamerbot.Actions.VariableDescription(),
			required: true),
		ActionParameter.Toggle(PersistedParameterName,
			label: AppStrings.Integrations.Streamerbot.Actions.PersistedLabel(),
			description: AppStrings.Integrations.Streamerbot.Actions.PersistedDescription(),
			defaultValue: true),
		ActionParameter.Text(TargetParameterName,
			label: AppStrings.Integrations.Streamerbot.Actions.SaveToVariableLabel(),
			description: AppStrings.Integrations.Streamerbot.Actions.SaveToVariableDescription(),
			required: true)
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver, _variables);

	internal static (VariableType Type, object Value) Convert(JsonElement value) => value.ValueKind switch
	{
		JsonValueKind.True => (VariableType.Boolean, true),
		JsonValueKind.False => (VariableType.Boolean, false),
		JsonValueKind.Number when value.TryGetDouble(out var number) => (VariableType.Numeric, number),
		JsonValueKind.String => (VariableType.Text, (object)(value.GetString() ?? string.Empty)),
		JsonValueKind.Null or JsonValueKind.Undefined => (VariableType.Text, string.Empty),
		_ => (VariableType.Text, value.GetRawText())
	};

	private sealed class Executor : IActionExecutor
	{
		private static readonly ILogger _logger =
			IntegrationLog.For<GetGlobalVariableActionDefinition>(StreamerbotIntegration.IntegrationId);

		private readonly Func<StreamerbotConnection?> _resolver;
		private readonly StreamerbotVariableAccessor _variables;

		public Executor(Func<StreamerbotConnection?> resolver, StreamerbotVariableAccessor variables)
		{
			_resolver = resolver;
			_variables = variables;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var connection = _resolver();
			if (connection is null)
			{
				_logger.Warning("Streamer.bot get-global skipped: not configured");
				return ActionResult.Failed(ActionErrorCodes.NotConnected,
					AppStrings.Integrations.Streamerbot.Errors.NotConnected());
			}

			if (StreamerbotActionValues.ReadText(context.Parameters, VariableParameterName) is not { } variable)
			{
				_logger.Warning("Streamer.bot get-global skipped: no variable named");
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Streamerbot.Errors.NoVariableNameGiven());
			}

			if (StreamerbotActionValues.ReadText(context.Parameters, TargetParameterName) is not { } target)
			{
				_logger.Warning("Streamer.bot get-global skipped: no target variable");
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Streamerbot.Errors.NoTargetVariableConfigured());
			}

			var persisted
				= StreamerbotActionValues.ReadBool(context.Parameters, PersistedParameterName, fallback: true);
			var value = await connection.GetGlobalAsync(variable, persisted, context.CancellationToken);
			if (value is not { } element)
			{
				_logger.Warning("Streamer.bot global '{Variable}' is unavailable", variable);
				return ActionResult.Failed(ActionErrorCodes.NotFound,
					AppStrings.Integrations.Streamerbot.Errors.VariableNotFound(variable: variable));
			}

			var (type, converted) = Convert(element);
			await StreamerbotVariableWriter.WriteAsync(_variables, target, type, converted);

			return ActionResult.Success();
		}
	}
}
