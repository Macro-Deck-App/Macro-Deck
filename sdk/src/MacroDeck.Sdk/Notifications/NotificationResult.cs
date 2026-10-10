using System.Text.Json;

namespace MacroDeck.Sdk.Notifications;

/// <summary>How a notification ended. It is produced exactly once.</summary>
public sealed record NotificationResult
{
	public required NotificationCloseReason CloseReason { get; init; }

	/// <summary>The action that was pressed. Only set when <see cref="CloseReason" /> is
	/// <see cref="NotificationCloseReason.Action" />.</summary>
	public string? ActionId { get; init; }

	/// <summary>
	/// The authenticated identity that responded: the device id, or the literal <c>admin</c> for the
	/// desktop app and administrator sessions. Null when nobody responded.
	/// </summary>
	public string? ClientId { get; init; }

	/// <summary>
	/// The validated values of every input in the notification content, keyed by node id and seeded
	/// from the tree's own initial values. Empty when there was no content or nobody responded with
	/// an action.
	/// </summary>
	public IReadOnlyDictionary<string, JsonElement> Values { get; init; } = new Dictionary<string, JsonElement>();
}
