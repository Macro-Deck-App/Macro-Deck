using System.Text.Json;
using MacroDeck.Localization;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Modals;

/// <summary>An action opened a modal on this client. The client opens the session itself - see
/// <see cref="OpenModalUiSessionRequest" /> - so the session binds to the client's own principal.</summary>
public sealed record UiModalOpenedEvent
{
	public required string ModalId { get; init; }

	public LocalizedText Title { get; init; }
}

public sealed record OpenModalUiSessionRequest
{
	public string ModalId { get; init; } = string.Empty;
}

public sealed record OpenModalUiSessionResponse
{
	public required bool Accepted { get; init; }

	public required string SessionId { get; init; }

	public string? Code { get; init; }

	public string? Message { get; init; }
}

/// <summary>Completes or cancels a modal. Sending no <see cref="Value" /> is a cancellation, whatever
/// <see cref="Cancelled" /> says - an action must never read a value the user did not supply.</summary>
public sealed record CompleteUiModalRequest
{
	public string ModalId { get; init; } = string.Empty;

	public bool Cancelled { get; init; }

	public JsonElement? Value { get; init; }
}

public sealed record CompleteUiModalResponse
{
	public required bool Accepted { get; init; }
}
