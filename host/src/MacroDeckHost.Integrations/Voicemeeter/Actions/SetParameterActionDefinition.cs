using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Voicemeeter.Actions;

internal sealed class SetParameterActionDefinition : IActionDefinition
{
	internal const string ParameterNameParameter = "parameter";
	internal const string ValueParameter = "value";

	private readonly Func<VoicemeeterConnection?> _resolver;

	public SetParameterActionDefinition(Func<VoicemeeterConnection?> resolver)
	{
		_resolver = resolver;
	}

	public string Id => "set-parameter";

	public LocalizedText Name => AppStrings.Integrations.Voicemeeter.Actions.SetParameter.Name();

	public LocalizedText Description => AppStrings.Integrations.Voicemeeter.Actions.SetParameter.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Text(ParameterNameParameter,
			label: AppStrings.Integrations.Voicemeeter.Actions.SetParameter.ParameterLabel(),
			description: AppStrings.Integrations.Voicemeeter.Actions.SetParameter.ParameterDescription(),
			placeholder: "Strip[0].Comp",
			required: true),
		ActionParameter.Text(ValueParameter,
			label: AppStrings.Integrations.Voicemeeter.Actions.SetParameter.ValueLabel(),
			description: AppStrings.Integrations.Voicemeeter.Actions.SetParameter.ValueDescription(),
			required: true)
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver);

	private sealed class Executor : IActionExecutor
	{
		private static readonly ILogger _logger =
			IntegrationLog.For<SetParameterActionDefinition>(VoicemeeterIntegration.IntegrationId);

		private readonly Func<VoicemeeterConnection?> _resolver;

		public Executor(Func<VoicemeeterConnection?> resolver)
		{
			_resolver = resolver;
		}

		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var connection = _resolver();
			var parameter = VoicemeeterActionValues.ReadText(context.Parameters, ParameterNameParameter);
			var raw = context.Parameters.GetValueOrDefault(ValueParameter);

			if (connection is null)
			{
				_logger.Warning("Voicemeeter set-parameter action skipped: Voicemeeter is not running");
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.NotConnected,
					AppStrings.Integrations.Voicemeeter.Errors.NotRunning()));
			}

			if (parameter is null)
			{
				_logger.Warning("Voicemeeter set-parameter action skipped: no parameter given");
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Voicemeeter.Errors.NoParameterSelected()));
			}

			if (VoicemeeterActionValues.ReadNumber(raw) is { } number)
			{
				connection.SetParameter(parameter, (float)number);
			}
			else
			{
				connection.SetParameter(parameter, raw as string ?? string.Empty);
			}

			return ActionResult.SucceededTask;
		}
	}
}
