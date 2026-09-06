using System.Text.Json;
using MacroDeck.Localization;

namespace MacroDeck.Sdk.Ui;

/// <summary>What an action is asking Macro Deck to open on the client that triggered it.</summary>
/// <remarks>
/// A definition names a view rather than carrying one. The tree is built by the integration's own
/// <see cref="IUiProvider" /> when Macro Deck opens the <c>dialog</c> surface, which is what gives a modal
/// the same lifecycle and the same reactive updates a widget has - a modal that carried a pre-built tree
/// would be a snapshot, and a plugin's could never be anything else, since a live view does not cross the
/// wire.
/// </remarks>
public sealed record ModalDefinition
{
	/// <summary>Which of the provider's dialogs to open, as the provider's own
	/// <see cref="IUiProvider.CreateSessionAsync" /> recognises it. Arrives on the surface as
	/// <c>UiDialogSurfaceAttributes.ModalId</c>'s sibling and is the provider's to interpret.</summary>
	public required string ViewId { get; init; }

	/// <summary>The heading Macro Deck renders in its own chrome above the provider's content, in the
	/// reader's own language. Absent means no heading.</summary>
	public LocalizedText? Title { get; init; }

	/// <summary>What the modal is about - the entity being picked, the value being confirmed. Reaches the
	/// provider verbatim as the surface's <c>data</c> attribute; Macro Deck never interprets it.</summary>
	public IReadOnlyDictionary<string, JsonElement>? Data { get; init; }
}
