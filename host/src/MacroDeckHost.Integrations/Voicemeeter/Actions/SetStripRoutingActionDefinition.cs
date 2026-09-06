using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Voicemeeter.Actions;

internal sealed class SetStripRoutingActionDefinition : IDynamicOptionsActionDefinition, IStateProviderActionDefinition
{
	private const string ModeOnly = "only";

	private static readonly IReadOnlyList<ActionParameterOption> _modes =
	[
		new() { Value = "toggle", Label = AppStrings.Integrations.Voicemeeter.Common.ToggleOption() },
		new() { Value = "on", Label = AppStrings.Integrations.Voicemeeter.Common.OnOption() },
		new() { Value = "off", Label = AppStrings.Integrations.Voicemeeter.Common.OffOption() },
		new() { Value = ModeOnly, Label = AppStrings.Integrations.Voicemeeter.Actions.SetStripRouting.ModeOnly() }
	];

	private readonly Func<VoicemeeterConnection?> _resolver;

	public SetStripRoutingActionDefinition(Func<VoicemeeterConnection?> resolver)
	{
		_resolver = resolver;
	}

	public string Id => "set-strip-routing";

	public LocalizedText Name => AppStrings.Integrations.Voicemeeter.Actions.SetStripRouting.Name();

	public LocalizedText Description => AppStrings.Integrations.Voicemeeter.Actions.SetStripRouting.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		VoicemeeterChannelTarget.Strip.Picker(),
		ActionParameter.DynamicChoice(VoicemeeterActionValues.BusParameter,
			label: AppStrings.Integrations.Voicemeeter.Actions.SetStripRouting.BusLabel(),
			description: AppStrings.Integrations.Voicemeeter.Actions.SetStripRouting.BusDescription(),
			required: true),
		ActionParameter.Choice(VoicemeeterActionValues.ModeParameter,
			options: _modes,
			label: AppStrings.Integrations.Voicemeeter.Common.ModeLabel(),
			defaultValue: "toggle")
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver);

	public Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
	{
		var index = VoicemeeterActionValues.ReadChannelIndex(
			parameters.GetValueOrDefault(VoicemeeterActionValues.StripParameter));
		if (index is null ||
			parameters.GetValueOrDefault(VoicemeeterActionValues.BusParameter)?.ToString()
				is not { Length: > 0 } bus)
		{
			return Task.FromResult<ActionStateSnapshot?>(null);
		}

		var state = _resolver()?.State;
		var strip = state is { IsConnected: true }
			? VoicemeeterChannelTarget.Strip.Channel(state, index.Value)
			: null;
		var routed = strip is not null && strip.Assignments.TryGetValue(bus, out var value) ? value : (bool?)null;

		return Task.FromResult<ActionStateSnapshot?>(ActionStates.Snapshot(ActionStates.OnOff, routed));
	}

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		var catalog = _resolver()?.Catalog ?? VoicemeeterChannelCatalog.Unknown;

		return Task.FromResult(context.ParameterName == VoicemeeterActionValues.BusParameter
			? VoicemeeterOptions.BusAssignments(catalog)
			: VoicemeeterOptions.Channels(catalog, VoicemeeterChannelKind.Strip));
	}

	private sealed class Executor : IActionExecutor
	{
		private static readonly ILogger _logger =
			IntegrationLog.For<SetStripRoutingActionDefinition>(VoicemeeterIntegration.IntegrationId);

		private readonly Func<VoicemeeterConnection?> _resolver;

		public Executor(Func<VoicemeeterConnection?> resolver)
		{
			_resolver = resolver;
		}

		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var connection = _resolver();
			var strip = VoicemeeterActionValues.ReadChannelIndex(context.Parameters,
				VoicemeeterActionValues.StripParameter);
			var bus = VoicemeeterActionValues.ReadText(context.Parameters, VoicemeeterActionValues.BusParameter);

			if (connection is null)
			{
				_logger.Warning("Voicemeeter routing action skipped: Voicemeeter is not running");
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.NotConnected,
					AppStrings.Integrations.Voicemeeter.Errors.NotRunning()));
			}

			if (strip is null)
			{
				_logger.Warning("Voicemeeter routing action skipped: no strip selected");
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Voicemeeter.Errors.NoInputStripSelected()));
			}

			if (bus is null)
			{
				_logger.Warning("Voicemeeter routing action skipped: no bus selected");
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Voicemeeter.Errors.NoOutputBusSelected()));
			}

			if (VoicemeeterActionValues.ReadText(context.Parameters, VoicemeeterActionValues.ModeParameter) == ModeOnly)
			{
				return Task.FromResult(RouteExclusively(connection, strip.Value, bus)
					? ActionResult.Success()
					: ActionResult.Failed(ActionErrorCodes.ProviderError,
						AppStrings.Integrations.Voicemeeter.Errors.BusLayoutUnknown()));
			}

			var applied = ChannelSwitch.Apply(connection,
				VoicemeeterParameters.StripBusAssignment(strip.Value, bus),
				VoicemeeterActionValues.ReadSwitchMode(context.Parameters));

			return Task.FromResult(applied
				? ActionResult.Success()
				: ActionResult.Failed(ActionErrorCodes.ProviderError,
					AppStrings.Integrations.Voicemeeter.Errors.SendStateNotReturned()));
		}

		private static bool RouteExclusively(VoicemeeterConnection connection, int strip, string bus)
		{
			var buses = connection.Catalog.BusAssignments;
			if (buses.Count == 0)
			{
				_logger.Warning("Voicemeeter exclusive routing skipped: no bus layout is known yet");
				return false;
			}

			var script = string.Join(';',
				buses.Select(candidate => string.Create(CultureInfo.InvariantCulture,
					$"{VoicemeeterParameters.StripBusAssignment(strip, candidate)}={(candidate == bus ? 1 : 0)}")));

			connection.RunScript(script);
			return true;
		}
	}
}
