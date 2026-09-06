using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Voicemeeter.Actions;

internal sealed class SetChannelSwitchActionDefinition : IDynamicOptionsActionDefinition, IStateProviderActionDefinition
{
	private static readonly IReadOnlyList<ActionParameterOption> _stripSwitches =
	[
		new()
		{
			Value = VoicemeeterParameters.Mono, Label = AppStrings.Integrations.Voicemeeter.Actions.SetSwitch.Mono()
		},
		new()
		{
			Value = VoicemeeterParameters.Solo, Label = AppStrings.Integrations.Voicemeeter.Actions.SetSwitch.Solo()
		}
	];

	private static readonly IReadOnlyList<ActionParameterOption> _busSwitches =
	[
		new()
		{
			Value = VoicemeeterParameters.Mono, Label = AppStrings.Integrations.Voicemeeter.Actions.SetSwitch.Mono()
		},
		new() { Value = VoicemeeterParameters.Eq, Label = AppStrings.Integrations.Voicemeeter.Actions.SetSwitch.Eq() },
		new() { Value = VoicemeeterParameters.Sel, Label = AppStrings.Integrations.Voicemeeter.Actions.SetSwitch.Sel() }
	];

	private readonly VoicemeeterChannelTarget _target;
	private readonly Func<VoicemeeterConnection?> _resolver;

	public SetChannelSwitchActionDefinition(VoicemeeterChannelTarget target, Func<VoicemeeterConnection?> resolver)
	{
		_target = target;
		_resolver = resolver;

		var switches = target.Kind == VoicemeeterChannelKind.Strip ? _stripSwitches : _busSwitches;

		Parameters =
		[
			target.Picker(),
			ActionParameter.Choice(VoicemeeterActionValues.SwitchParameter,
				options: switches,
				label: AppStrings.Integrations.Voicemeeter.Actions.SetSwitch.SwitchLabel(),
				defaultValue: VoicemeeterParameters.Mono,
				required: true),
			ActionParameter.Choice(VoicemeeterActionValues.ModeParameter,
				options: VoicemeeterActionValues.SwitchModeOptions,
				label: AppStrings.Integrations.Voicemeeter.Common.ModeLabel(),
				defaultValue: "toggle")
		];
	}

	public string Id => $"set-{_target.IdPrefix}-switch";

	public LocalizedText Name => _target.Kind == VoicemeeterChannelKind.Strip
		? AppStrings.Integrations.Voicemeeter.Actions.SetSwitch.StripName()
		: AppStrings.Integrations.Voicemeeter.Actions.SetSwitch.BusName();

	public LocalizedText Description => _target.Kind == VoicemeeterChannelKind.Strip
		? AppStrings.Integrations.Voicemeeter.Actions.SetSwitch.StripDescription()
		: AppStrings.Integrations.Voicemeeter.Actions.SetSwitch.BusDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; }

	public IActionExecutor CreateExecutor() => new Executor(_target, _resolver);

	// Only Mono and Solo are part of the polled channel snapshot; EQ and Sel are written but never read
	// back, so a button following one of those is told "unavailable" rather than shown a guess.
	public Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
	{
		var index = VoicemeeterActionValues.ReadChannelIndex(parameters.GetValueOrDefault(_target.ParameterName));
		if (index is null ||
			parameters.GetValueOrDefault(VoicemeeterActionValues.SwitchParameter)?.ToString()
				is not { Length: > 0 } name)
		{
			return Task.FromResult<ActionStateSnapshot?>(null);
		}

		var state = _resolver()?.State;
		var channel = state is { IsConnected: true } ? _target.Channel(state, index.Value) : null;
		var active = channel is null
			? null
			: name switch
			{
				VoicemeeterParameters.Mono => channel.Mono,
				VoicemeeterParameters.Solo => channel.Solo,
				_ => (bool?)null
			};

		return Task.FromResult<ActionStateSnapshot?>(ActionStates.Snapshot(ActionStates.OnOff, active));
	}

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		var catalog = _resolver()?.Catalog ?? VoicemeeterChannelCatalog.Unknown;
		return Task.FromResult(VoicemeeterOptions.Channels(catalog, _target.Kind));
	}

	private sealed class Executor : IActionExecutor
	{
		private static readonly ILogger _logger =
			IntegrationLog.For<SetChannelSwitchActionDefinition>(VoicemeeterIntegration.IntegrationId);

		private readonly VoicemeeterChannelTarget _target;
		private readonly Func<VoicemeeterConnection?> _resolver;

		public Executor(VoicemeeterChannelTarget target, Func<VoicemeeterConnection?> resolver)
		{
			_target = target;
			_resolver = resolver;
		}

		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var connection = _resolver();
			var index = VoicemeeterActionValues.ReadChannelIndex(context.Parameters, _target.ParameterName);
			var name = VoicemeeterActionValues.ReadText(context.Parameters, VoicemeeterActionValues.SwitchParameter);

			if (connection is null)
			{
				_logger.Warning("Voicemeeter switch action skipped: Voicemeeter is not running");
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.NotConnected,
					AppStrings.Integrations.Voicemeeter.Errors.NotRunning()));
			}

			if (index is null)
			{
				_logger.Warning("Voicemeeter switch action skipped: no {Target} selected", _target.Title);
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Voicemeeter.Errors.NoTargetSelected(target: _target.NounText)));
			}

			if (name is null)
			{
				_logger.Warning("Voicemeeter switch action skipped: no switch selected");
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Voicemeeter.Errors.NoSwitchSelected()));
			}

			var parameter = _target.Parameter(index.Value, name);
			var applied = ChannelSwitch.Apply(connection,
				parameter,
				VoicemeeterActionValues.ReadSwitchMode(context.Parameters));

			return Task.FromResult(applied
				? ActionResult.Success()
				: ActionResult.Failed(ActionErrorCodes.ProviderError,
					AppStrings.Integrations.Voicemeeter.Errors.CurrentSwitchStateNotReturned()));
		}
	}
}

internal static class ChannelSwitch
{
	private static readonly ILogger _logger =
		IntegrationLog.For(VoicemeeterIntegration.IntegrationId, typeof(ChannelSwitch));

	public static bool Apply(VoicemeeterConnection connection, string parameter, SwitchMode mode)
	{
		var value = mode.Resolve(() => connection.GetParameter(parameter) is { } current ? current > 0.5f : null);
		if (value is null)
		{
			_logger.Warning("Voicemeeter cannot toggle '{Parameter}': its current value is unavailable", parameter);
			return false;
		}

		connection.SetParameter(parameter, value.Value ? 1f : 0f);
		return true;
	}
}
