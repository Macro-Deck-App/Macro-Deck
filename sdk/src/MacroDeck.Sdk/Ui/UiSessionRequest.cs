using MacroDeck.Ui.Model.Surfaces;

namespace MacroDeck.Sdk.Ui;

/// <summary>What the host is asking a provider to serve.</summary>
public sealed record UiSessionRequest
{
	/// <summary>Where the tree will be rendered, including the session mode the host will enforce. Both
	/// vocabularies are open, so a provider that does not recognise the kind declines the session rather
	/// than guessing.</summary>
	public required UiSurface Surface { get; init; }

	/// <summary>The UI model version the host speaks.</summary>
	public required int UiModelVersion { get; init; }
}
