using MacroDeck.Sdk.Actions;
using MacroDeck.Localization;

namespace MacroDeck.Plugin.Testing.Tests.WellBehavedPlugin.Actions;

/// <summary>
/// The state-provider action of the four: a button following this instance shows one of two states and
/// switches to whichever one <see cref="GetActionStateAsync"/> currently reports - see
/// <see cref="IStateProviderActionDefinition"/>. Toggles whether the fixture's synthetic weather station
/// is powered.
/// </summary>
internal sealed class ToggleStationPowerAction(WellBehavedIntegration integration)
	: IActionDefinition, IStateProviderActionDefinition
{
	internal const string OnStateId = "on";
	internal const string OffStateId = "off";

	public string Id => "toggle-station-power";

	public LocalizedText Name => "Toggle station power";

	public LocalizedText Description => "Switches the fixture's synthetic weather station on or off.";

	public IReadOnlyList<ActionParameter> Parameters { get; } = [];

	public IActionExecutor CreateExecutor() => new Executor(integration);

	public Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
		=> Task.FromResult<ActionStateSnapshot?>(new ActionStateSnapshot(
			[new ActionStateDefinition(OnStateId, "On"), new ActionStateDefinition(OffStateId, "Off")],
			integration.StationPoweredOn ? OnStateId : OffStateId));

	private sealed class Executor(WellBehavedIntegration integration) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			integration.StationPoweredOn = !integration.StationPoweredOn;
			return ActionResult.SucceededTask;
		}
	}
}
