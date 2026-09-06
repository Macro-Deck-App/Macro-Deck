namespace MacroDeck.Ui.Previews;

/// <summary>One discovered preview scenario, as Developer Tools lists it.</summary>
public sealed record UiPreviewDeclaration
{
	/// <summary>
	/// Addresses the scenario when a session is opened. Derived from where the method is declared rather
	/// than from its name, so renaming the scenario a developer reads does not invalidate a selection or
	/// a link that names it.
	/// </summary>
	public required string Id { get; init; }

	/// <summary>The view this scenario previews. Scenarios that share it are shown together.</summary>
	public required string View { get; init; }

	/// <summary>The state this scenario shows, unique within <see cref="View" />.</summary>
	public required string Scenario { get; init; }

	/// <summary>Which vocabulary the scenario's tree is authored in - see
	/// <see cref="UiPreviewProfiles" />.</summary>
	public required string Profile { get; init; }
}
