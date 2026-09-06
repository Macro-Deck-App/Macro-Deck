using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Ui;
using MacroDeck.Localization;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;

namespace MacroDeck.Plugin.Testing.Tests.WellBehavedPlugin.ConfigFlow;

/// <summary>
/// A single-step setup flow that collects the location the fixture's synthetic weather station
/// reports for. Deliberately the simplest possible shape - one step, one required field, no OAuth -
/// so it reads as the config-flow contract's minimum rather than a second walkthrough of
/// <c>IOAuthSession</c> (see the Spotify integration for that).
///
/// A new instance is created per session (<see cref="WellBehavedIntegration.CreateConfigFlow"/>), but this
/// flow keeps no session state of its own: the submitted value goes straight into the completed
/// config entry, and <see cref="WellBehavedIntegration.InitializeAsync"/> is what actually reads it back -
/// the same division of labor <c>SpotifyIntegration.ConnectFromConfig</c> uses.
///
/// <para>
/// Also implements <see cref="IUiConfigFlow"/>: <see cref="CreateUiSessionAsync"/> renders the same step
/// as a tree instead of declaring it - the declared <see cref="ConfigFlowStep.Fields"/> above stay the
/// only thing that is ever persisted or classified as secret. The tree's one top-level input keeps
/// <see cref="LocationFieldName"/> as its key, per the DSL's identity rule that a top-level input id is
/// its field key, which is what lets <see cref="SubmitAsync"/> - the only path that ever completes this
/// flow - go on reading the same name regardless of which representation collected it.
/// </para>
/// </summary>
internal sealed class WellBehavedConfigFlow : IConfigFlow, IUiConfigFlow
{
	/// <summary>
	/// The field name, which is also the config entry key <see cref="WellBehavedIntegration.InitializeAsync"/>
	/// reads back - a config flow's <see cref="ConfigFlowStep.Fields"/> are persisted under their own
	/// names automatically, with no need to echo them into <see cref="ConfigFlowResult.Complete"/>'s
	/// <c>values</c> argument.
	/// </summary>
	internal const string LocationFieldName = "location";

	private const string StepId = "location";

	public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
		=> Task.FromResult(ConfigFlowResult.Step(BuildStep()));

	public Task<ConfigFlowResult> SubmitAsync(
		string stepId,
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context,
		CancellationToken cancellationToken)
	{
		if (!string.Equals(stepId, StepId, StringComparison.Ordinal))
		{
			return Task.FromResult(ConfigFlowResult.Error(BuildStep(), "Unknown step."));
		}

		if (input.GetValueOrDefault(LocationFieldName) is not string { Length: > 0 } location)
		{
			return Task.FromResult(ConfigFlowResult.Error(BuildStep(),
				"Enter a location name.",
				new Dictionary<string, LocalizedText> { [LocationFieldName] = "Required." }));
		}

		return Task.FromResult(ConfigFlowResult.Complete($"Sample ({location})"));
	}

	/// <summary>
	/// Renders the same "sample location" step as a <see cref="UiFlow"/>/<see cref="UiStep"/> tree. The
	/// field is bound two-way, so a client's <c>change</c> event on it produces a real patch - a real
	/// provider for the "dispatching an event never persists anything" scenarios, since only
	/// <see cref="SubmitAsync"/> ever writes to the completed config entry.
	/// </summary>
	public Task<IUiSession?> CreateUiSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		var view = new UiView(request.Surface, BuildTree("Berlin, Germany"));

		return Task.FromResult<IUiSession?>(new WellBehavedUiSession(view));
	}

	// Shared with WellBehavedConfigFlowPreviews so a preview renders the tree this flow really serves
	// rather than a copy of it that can drift.
	internal static UiElement BuildTree(string location) => new UiFlow
	{
		Key = "setup",
		Title = "Sample location",
		StepId = UiValue.Of(StepId),
		Children =
		[
			new UiStep
			{
				Key = StepId,
				StepId = UiValue.Of(StepId),
				Title = "Sample location",
				Description = "Pick the location the fixture's synthetic weather station reports for.",
				Children =
				[
					new UiStringInput
					{
						Key = LocationFieldName,
						Label = "Location name",
						Binding = Bind.To(new UiState<string>(location)),
						Required = true
					}
				]
			}
		]
	};

	private static ConfigFlowStep BuildStep() => new()
	{
		StepId = StepId,
		Title = "Sample location",
		Description = "Pick the location the fixture's synthetic weather station reports for.",
		Fields =
		[
			ActionParameter.Text(LocationFieldName,
				label: "Location name",
				defaultValue: "Berlin, Germany",
				required: true)
		]
	};
}
