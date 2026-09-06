using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Voicemeeter.Actions;

internal sealed class GetParameterActionDefinition : IActionDefinition
{
	internal const string ParameterNameParameter = "parameter";
	internal const string VariableParameter = "variable";
	internal const string TypeParameter = "type";

	private const string TypeNumber = "number";
	private const string TypeText = "text";
	private const string TypeBoolean = "boolean";

	private readonly Func<VoicemeeterConnection?> _resolver;
	private readonly VoicemeeterVariableAccessor _variables;

	public GetParameterActionDefinition(Func<VoicemeeterConnection?> resolver, VoicemeeterVariableAccessor variables)
	{
		_resolver = resolver;
		_variables = variables;
	}

	public string Id => "get-parameter";

	public LocalizedText Name => AppStrings.Integrations.Voicemeeter.Actions.GetParameter.Name();

	public LocalizedText Description => AppStrings.Integrations.Voicemeeter.Actions.GetParameter.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Text(ParameterNameParameter,
			label: AppStrings.Integrations.Voicemeeter.Actions.GetParameter.ParameterLabel(),
			description: AppStrings.Integrations.Voicemeeter.Actions.GetParameter.ParameterDescription(),
			placeholder: "Strip[0].Gain",
			required: true),
		ActionParameter.Text(VariableParameter,
			label: AppStrings.Integrations.Voicemeeter.Actions.GetParameter.VariableLabel(),
			description: AppStrings.Integrations.Voicemeeter.Actions.GetParameter.VariableDescription(),
			required: true),
		ActionParameter.Choice(TypeParameter,
			options:
			[
				new ActionParameterOption
				{
					Value = TypeNumber, Label = AppStrings.Integrations.Voicemeeter.Actions.GetParameter.TypeNumber()
				},
				new ActionParameterOption
				{
					Value = TypeBoolean, Label = AppStrings.Integrations.Voicemeeter.Actions.GetParameter.TypeOnOff()
				},
				new ActionParameterOption
				{
					Value = TypeText, Label = AppStrings.Integrations.Voicemeeter.Actions.GetParameter.TypeText()
				}
			],
			label: AppStrings.Integrations.Voicemeeter.Actions.GetParameter.TypeLabel(),
			defaultValue: TypeNumber)
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver, _variables);

	private sealed class Executor : IActionExecutor
	{
		private static readonly ILogger _logger =
			IntegrationLog.For<GetParameterActionDefinition>(VoicemeeterIntegration.IntegrationId);

		private readonly Func<VoicemeeterConnection?> _resolver;
		private readonly VoicemeeterVariableAccessor _variables;

		public Executor(Func<VoicemeeterConnection?> resolver, VoicemeeterVariableAccessor variables)
		{
			_resolver = resolver;
			_variables = variables;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var connection = _resolver();
			var parameter = VoicemeeterActionValues.ReadText(context.Parameters, ParameterNameParameter);
			var variable = VoicemeeterActionValues.ReadText(context.Parameters, VariableParameter);

			if (connection is null)
			{
				_logger.Warning("Voicemeeter get-parameter action skipped: Voicemeeter is not running");
				return ActionResult.Failed(ActionErrorCodes.NotConnected,
					AppStrings.Integrations.Voicemeeter.Errors.NotRunning());
			}

			if (parameter is null)
			{
				_logger.Warning("Voicemeeter get-parameter action skipped: no parameter given");
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Voicemeeter.Errors.NoParameterSelected());
			}

			if (variable is null)
			{
				_logger.Warning("Voicemeeter get-parameter action skipped: no target variable given");
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Voicemeeter.Errors.NoTargetVariableSelected());
			}

			var type = VoicemeeterActionValues.ReadText(context.Parameters, TypeParameter) ?? TypeNumber;
			if (type == TypeText)
			{
				if (connection.GetTextParameter(parameter) is not { } text)
				{
					_logger.Warning("Voicemeeter get-parameter action skipped: '{Parameter}' is unavailable",
						parameter);
					return ActionResult.Failed(ActionErrorCodes.ProviderError,
						AppStrings.Integrations.Voicemeeter.Errors.ParameterNotReturned());
				}

				await VoicemeeterVariableWriter.WriteAsync(_variables, variable, VariableType.Text, text);
				return ActionResult.Success();
			}

			if (connection.GetParameter(parameter) is not { } value)
			{
				_logger.Warning("Voicemeeter get-parameter action skipped: '{Parameter}' is unavailable", parameter);
				return ActionResult.Failed(ActionErrorCodes.ProviderError,
					AppStrings.Integrations.Voicemeeter.Errors.ParameterNotReturned());
			}

			if (type == TypeBoolean)
			{
				await VoicemeeterVariableWriter.WriteAsync(_variables,
					variable,
					VariableType.Boolean,
					value > 0.5f);
				return ActionResult.Success();
			}

			await VoicemeeterVariableWriter.WriteAsync(_variables,
				variable,
				VariableType.Numeric,
				Math.Round(value, 2));

			return ActionResult.Success();
		}
	}
}
