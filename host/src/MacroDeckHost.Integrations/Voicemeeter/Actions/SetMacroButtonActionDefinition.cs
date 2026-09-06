using MacroDeckHost.Integrations.Voicemeeter.Native;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Voicemeeter.Actions;

internal sealed class SetMacroButtonActionDefinition : IActionDefinition, IStateProviderActionDefinition
{
	internal const string ButtonParameter = "button";
	internal const string StateOnlyParameter = "stateOnly";

	private const string ModePress = "press";

	private static readonly TimeSpan _pressDuration = TimeSpan.FromMilliseconds(50);

	private readonly Func<VoicemeeterConnection?> _resolver;

	public SetMacroButtonActionDefinition(Func<VoicemeeterConnection?> resolver)
	{
		_resolver = resolver;
	}

	public string Id => "set-macro-button";

	public LocalizedText Name => AppStrings.Integrations.Voicemeeter.Actions.SetMacroButton.Name();

	public LocalizedText Description => AppStrings.Integrations.Voicemeeter.Actions.SetMacroButton.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Number(ButtonParameter,
			label: AppStrings.Integrations.Voicemeeter.Actions.SetMacroButton.ButtonLabel(),
			description: AppStrings.Integrations.Voicemeeter.Actions.SetMacroButton.ButtonDescription(),
			min: 0,
			max: VoicemeeterConnection.MacroButtonCount - 1,
			step: 1,
			defaultValue: 0,
			required: true),
		ActionParameter.Choice(VoicemeeterActionValues.ModeParameter,
			options:
			[
				new ActionParameterOption
				{
					Value = ModePress, Label = AppStrings.Integrations.Voicemeeter.Actions.SetMacroButton.ModePress()
				},
				new ActionParameterOption
					{ Value = "toggle", Label = AppStrings.Integrations.Voicemeeter.Common.ToggleOption() },
				new ActionParameterOption
					{ Value = "on", Label = AppStrings.Integrations.Voicemeeter.Common.OnOption() },
				new ActionParameterOption
					{ Value = "off", Label = AppStrings.Integrations.Voicemeeter.Common.OffOption() }
			],
			label: AppStrings.Integrations.Voicemeeter.Common.ModeLabel(),
			defaultValue: ModePress),
		ActionParameter.Toggle(StateOnlyParameter,
			label: AppStrings.Integrations.Voicemeeter.Actions.SetMacroButton.StateOnlyLabel(),
			description: AppStrings.Integrations.Voicemeeter.Actions.SetMacroButton.StateOnlyDescription())
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver);

	public Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
	{
		var button = VoicemeeterActionValues.ReadChannelIndex(parameters.GetValueOrDefault(ButtonParameter));
		if (button is null)
		{
			return Task.FromResult<ActionStateSnapshot?>(null);
		}

		var state = _resolver()?.State;
		var on = state is { IsConnected: true } && state.MacroButtons.TryGetValue(button.Value, out var value)
			? value
			: (bool?)null;

		return Task.FromResult<ActionStateSnapshot?>(ActionStates.Snapshot(ActionStates.OnOff, on));
	}

	private sealed class Executor : IActionExecutor
	{
		private static readonly ILogger _logger =
			IntegrationLog.For<SetMacroButtonActionDefinition>(VoicemeeterIntegration.IntegrationId);

		private readonly Func<VoicemeeterConnection?> _resolver;

		public Executor(Func<VoicemeeterConnection?> resolver)
		{
			_resolver = resolver;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var connection = _resolver();
			var button = (int?)VoicemeeterActionValues.ReadNumber(context.Parameters, ButtonParameter);

			if (connection is null)
			{
				_logger.Warning("Voicemeeter macro button action skipped: Voicemeeter is not running");
				return ActionResult.Failed(ActionErrorCodes.NotConnected,
					AppStrings.Integrations.Voicemeeter.Errors.NotRunning());
			}

			if (button is null or < 0 or >= VoicemeeterConnection.MacroButtonCount)
			{
				_logger.Warning("Voicemeeter macro button action skipped: '{Button}' is not a valid button",
					button);
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Voicemeeter.Errors.NoMacroButtonSelected());
			}

			var mode = context.Parameters.GetValueOrDefault(VoicemeeterActionValues.ModeParameter) as string ??
				ModePress;
			var buttonMode = VoicemeeterActionValues.ReadNumber(context.Parameters, StateOnlyParameter) > 0
				? VoicemeeterMacroButtonMode.StateOnly
				: VoicemeeterMacroButtonMode.PushRelease;

			if (mode == ModePress)
			{
				connection.SetMacroButton(button.Value, buttonMode, state: true);
				await Task.Delay(_pressDuration, context.CancellationToken);
				connection.SetMacroButton(button.Value, buttonMode, state: false);
				return ActionResult.Success();
			}

			var state = VoicemeeterActionValues.ReadSwitchMode(context.Parameters)
				.Resolve(() => connection.GetMacroButton(button.Value, buttonMode));
			if (state is null)
			{
				_logger.Warning("Voicemeeter cannot toggle macro button {Button}: its state is unavailable",
					button);
				return ActionResult.Failed(ActionErrorCodes.ProviderError,
					AppStrings.Integrations.Voicemeeter.Errors.ButtonStateNotReturned());
			}

			connection.SetMacroButton(button.Value, buttonMode, state.Value);

			return ActionResult.Success();
		}
	}
}
