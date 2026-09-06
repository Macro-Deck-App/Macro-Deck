using MacroDeck.Sdk.Actions;
using MacroDeck.Localization;

namespace MacroDeck.Plugin.Testing.Tests.WellBehavedPlugin.Actions;

/// <summary>
/// The plain action of the three: no parameters, no dynamic behavior. Advances the fixture's
/// synthetic weather reading by one tick, which republishes the <c>weather-refreshed</c> event and
/// updates the temperature variable and the weather snapshot - the one button that ties every other
/// capability in this fixture together.
/// </summary>
internal sealed class RefreshWeatherAction(WellBehavedIntegration integration) : IActionDefinition
{
	public string Id => "refresh-weather";

	public LocalizedText Name => "Refresh weather";

	public LocalizedText Description => "Advances the fixture's synthetic weather reading and republishes it.";

	public IReadOnlyList<ActionParameter> Parameters { get; } = [];

	public IActionExecutor CreateExecutor() => new Executor(integration);

	private sealed class Executor(WellBehavedIntegration integration) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			integration.Station.Tick();
			return ActionResult.SucceededTask;
		}
	}
}
