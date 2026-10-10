using MacroDeck.Localization;

namespace MacroDeck.Sdk.Notifications;

/// <summary>A button on a notification. Its <see cref="Id" /> is what a response names.</summary>
public sealed record NotificationAction
{
	/// <summary>Unique within the notification. Only declared ids are accepted as a response.</summary>
	public required string Id { get; init; }

	public required LocalizedText Label { get; init; }

	public NotificationActionStyle Style { get; init; } = NotificationActionStyle.Default;
}
