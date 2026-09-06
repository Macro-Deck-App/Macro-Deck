using System.Text.Json;
using MacroDeck.Localization;

namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>Arguments for <c>host.invoke</c> against <see cref="HostApis.ActionInteractions"/>'s
/// <c>request-item-picker</c> operation.</summary>
public sealed record ActionInteractionsRequestItemPickerArguments
{
	/// <summary>The correlation id of the <c>actions/execute</c> invocation this picker request
	/// belongs to - not the <c>host.invoke</c>'s own id. The host only honours a picker request while
	/// this correlation names a live <c>actions/execute</c> of the same plugin; otherwise any plugin
	/// could pop a picker on a user's client at will.</summary>
	public required string ExecuteCorrelationId { get; init; }

	public string? OriginClientId { get; init; }

	public required string InstanceId { get; init; }

	/// <summary>The <c>MacroDeck.Sdk.MusicPlayer.MusicPlayerCatalogItemKind</c> enumerant name - see
	/// <see cref="VariablesCreateArguments.Type"/>'s remarks on why this is a plain string.</summary>
	public required string Kind { get; init; }

	public string? Prompt { get; init; }
}

/// <summary>Arguments for <c>request-device-picker</c>.</summary>
public sealed record ActionInteractionsRequestDevicePickerArguments
{
	/// <summary>See <see cref="ActionInteractionsRequestItemPickerArguments.ExecuteCorrelationId"/>.</summary>
	public required string ExecuteCorrelationId { get; init; }

	public string? OriginClientId { get; init; }

	public required string InstanceId { get; init; }

	public bool StartPlayback { get; init; }

	public string? Prompt { get; init; }
}

/// <summary>
/// Arguments for <c>show-modal</c>: open a Macro Deck UI modal on the client that triggered the action.
/// The host answers as soon as the modal is open; the user's answer arrives later as a
/// <c>ui</c>/<c>modal.result</c> capability invoke naming the same <c>ModalId</c>.
/// </summary>
public sealed record ActionInteractionsShowModalArguments
{
	/// <summary>See <see cref="ActionInteractionsRequestItemPickerArguments.ExecuteCorrelationId"/>. A
	/// modal is gated on a live execution for the same reason a picker is: without it any connected plugin
	/// could put a dialog on a user's screen whenever it liked.</summary>
	public required string ExecuteCorrelationId { get; init; }

	public string? OriginClientId { get; init; }

	/// <summary>Which of the provider's dialogs to open. Travels to the provider's own UI capability as the
	/// opened surface's view id.</summary>
	public required string ViewId { get; init; }

	/// <summary>The heading Macro Deck renders in its own chrome above the provider's content.</summary>
	public LocalizedText? Title { get; init; }

	/// <summary>What the modal is about. Opaque to the host.</summary>
	public IReadOnlyDictionary<string, JsonElement>? Data { get; init; }

	/// <summary>Whether the plugin is waiting for an answer. False is fire-and-forget: the host opens the
	/// modal and sends no <c>modal.result</c>.</summary>
	public bool AwaitResult { get; init; }
}

/// <summary>Result of <c>show-modal</c>.</summary>
public sealed record ActionInteractionsShowModalResult
{
	/// <summary>Whether a modal was opened. False when there was no client to show it on, or the execution
	/// correlation named no live run of this plugin.</summary>
	public required bool Opened { get; init; }

	/// <summary>The host's id for the modal, which a later <c>modal.result</c> names. Empty when
	/// <see cref="Opened" /> is false.</summary>
	public string ModalId { get; init; } = string.Empty;
}
