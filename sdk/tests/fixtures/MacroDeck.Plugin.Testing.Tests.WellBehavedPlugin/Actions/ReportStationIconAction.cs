using MacroDeck.Sdk.Actions;
using MacroDeck.Localization;

namespace MacroDeck.Plugin.Testing.Tests.WellBehavedPlugin.Actions;

/// <summary>
/// The icon-provider action of the five: a widget following this instance shows a fixed image the
/// fixture uploads as bytes rather than as an icon-pack reference - see
/// <see cref="IIconProviderActionDefinition"/>. Free-standing from <see cref="ToggleStationPowerAction"/>
/// on purpose, so the icon and state capabilities stay independently attributable in a conformance
/// report exactly as the two are independent on the wire. What the conformance suite's <c>MDC0312</c> and
/// <c>MDC0313</c> checks have a real, well-behaved icon-provider action to pass against.
/// </summary>
internal sealed class ReportStationIconAction : IActionDefinition, IIconProviderActionDefinition
{
	internal const string IconVersion = "station-icon-v1";

	// Real bytes crossing the plugin-to-host asset pipeline - their content is never inspected by
	// anything that consumes this fixture, only their media type and the content hash the host reports
	// back for them.
	private static readonly byte[] _iconBytes = "well-behaved-station-icon"u8.ToArray();

	public string Id => "report-station-icon";

	public LocalizedText Name => "Report station icon";

	public LocalizedText Description => "Reports a fixed icon for the fixture's synthetic weather station.";

	public IReadOnlyList<ActionParameter> Parameters { get; } = [];

	public IActionExecutor CreateExecutor() => new Executor();

	public Task<ActionIconSnapshot?> GetActionIconAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
		=> Task.FromResult<ActionIconSnapshot?>(new ActionIconSnapshot
			{ Version = IconVersion, MediaType = "image/png" });

	public Task<ActionIconContent?> GetActionIconContentAsync(
		IReadOnlyDictionary<string, object?> parameters,
		string version,
		CancellationToken cancellationToken)
		=> Task.FromResult<ActionIconContent?>(new ActionIconContent(_iconBytes, "image/png"));

	private sealed class Executor : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context) => ActionResult.SucceededTask;
	}
}
