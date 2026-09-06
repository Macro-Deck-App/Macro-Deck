using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Voicemeeter.Actions;

internal sealed class SetChannelMuteActionDefinition : IDynamicOptionsActionDefinition, IStateProviderActionDefinition
{
	private readonly VoicemeeterChannelTarget _target;
	private readonly Func<VoicemeeterConnection?> _resolver;

	public SetChannelMuteActionDefinition(VoicemeeterChannelTarget target, Func<VoicemeeterConnection?> resolver)
	{
		_target = target;
		_resolver = resolver;

		Parameters =
		[
			target.Picker(),
			ActionParameter.Choice(VoicemeeterActionValues.ModeParameter,
				options: VoicemeeterActionValues.SwitchModeOptions,
				label: AppStrings.Integrations.Voicemeeter.Common.ModeLabel(),
				defaultValue: "toggle")
		];
	}

	public string Id => $"set-{_target.IdPrefix}-mute";

	public LocalizedText Name => _target.Kind == VoicemeeterChannelKind.Strip
		? AppStrings.Integrations.Voicemeeter.Actions.SetMute.StripName()
		: AppStrings.Integrations.Voicemeeter.Actions.SetMute.BusName();

	public LocalizedText Description => _target.Kind == VoicemeeterChannelKind.Strip
		? AppStrings.Integrations.Voicemeeter.Actions.SetMute.StripDescription()
		: AppStrings.Integrations.Voicemeeter.Actions.SetMute.BusDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; }

	public IActionExecutor CreateExecutor() => new Executor(_target, _resolver);

	public Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
	{
		var index = VoicemeeterActionValues.ReadChannelIndex(parameters.GetValueOrDefault(_target.ParameterName));
		if (index is null)
		{
			return Task.FromResult<ActionStateSnapshot?>(null);
		}

		var state = _resolver()?.State;
		var muted = state is { IsConnected: true } ? _target.Channel(state, index.Value)?.Muted : null;
		return Task.FromResult<ActionStateSnapshot?>(ActionStates.Snapshot(ActionStates.Mute, muted));
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
			IntegrationLog.For<SetChannelMuteActionDefinition>(VoicemeeterIntegration.IntegrationId);

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
			if (connection is null)
			{
				_logger.Warning("Voicemeeter mute action skipped: Voicemeeter is not running");
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.NotConnected,
					AppStrings.Integrations.Voicemeeter.Errors.NotRunning()));
			}

			if (index is null)
			{
				_logger.Warning("Voicemeeter mute action skipped: no {Target} selected", _target.Title);
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Voicemeeter.Errors.NoTargetSelected(target: _target.NounText)));
			}

			var applied = ChannelSwitch.Apply(connection,
				_target.Parameter(index.Value, VoicemeeterParameters.Mute),
				VoicemeeterActionValues.ReadSwitchMode(context.Parameters));

			return Task.FromResult(applied
				? ActionResult.Success()
				: ActionResult.Failed(ActionErrorCodes.ProviderError,
					AppStrings.Integrations.Voicemeeter.Errors.CurrentMuteStateNotReturned()));
		}
	}
}
