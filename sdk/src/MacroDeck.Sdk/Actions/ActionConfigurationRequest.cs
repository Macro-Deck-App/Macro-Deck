using System.Text.Json;
using MacroDeck.Sdk.Ui;

namespace MacroDeck.Sdk.Actions;

/// <summary>What Macro Deck is asking an action to render a configuration session for.</summary>
public sealed record ActionConfigurationRequest
{
	/// <summary>The surface and UI model version the host is opening the session for.</summary>
	public required UiSessionRequest Session { get; init; }

	/// <summary>
	/// The values already stored for the action instance being configured, keyed by parameter name.
	/// Empty for an action that was just added.
	/// </summary>
	/// <remarks>
	/// A tree renders these; it does not persist them. Macro Deck stores what the tree reports through
	/// its top-level input ids, exactly as it stores values entered into the declared parameter list.
	/// </remarks>
	public IReadOnlyDictionary<string, JsonElement> Parameters { get; init; } =
		new Dictionary<string, JsonElement>(StringComparer.Ordinal);
}
