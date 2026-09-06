namespace MacroDeck.Ui.Previews;

/// <summary>
/// Marks a method as one named preview scenario, discoverable by Developer Tools through
/// <see cref="UiPreviewCatalog" />.
/// </summary>
/// <remarks>
/// <para>
/// The method must be <c>static</c>, take no parameters, and return either a
/// <see cref="Dsl.UiElement" /> or a <see cref="Runtime.UiView" />. Mock dependencies and initial state
/// are constructed in the method body, which is why no parameters are taken: a scenario names what it
/// renders, it does not ask to be supplied with anything.
/// </para>
/// <para>
/// A method that does not meet those requirements is skipped and reported as a
/// <see cref="UiPreviewDiagnostic" /> rather than throwing. Discovery runs alongside the surfaces a
/// plugin really serves, and one malformed preview must not be able to take those down.
/// </para>
/// <para>
/// The method is never called during discovery - only when a developer opens the scenario. Declaring a
/// preview therefore costs a running Macro Deck nothing.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class UiPreviewAttribute : Attribute
{
	/// <param name="scenario">The human-readable state this scenario shows, for example <c>Connected</c>
	/// or <c>Long text</c>. Shown under <see cref="View" /> in Developer Tools.</param>
	public UiPreviewAttribute(string scenario)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(scenario);

		Scenario = scenario;
	}

	/// <summary>The human-readable state this scenario shows.</summary>
	public string Scenario { get; }

	/// <summary>
	/// Which view the scenario previews, used to group scenarios in Developer Tools. Defaults to the
	/// declaring type's name with a trailing <c>Previews</c> removed, so a
	/// <c>SpotifyConfigViewPreviews</c> class groups under <c>SpotifyConfigView</c> without repeating it.
	/// </summary>
	public string? View { get; set; }

	/// <summary>Which vocabulary the scenario authors its tree in - see
	/// <see cref="UiPreviewProfiles" />. Defaults to <see cref="UiPreviewProfiles.Config" />.</summary>
	public string Profile { get; set; } = UiPreviewProfiles.Config;
}
