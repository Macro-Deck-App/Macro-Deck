using System.Text.Json;
using MacroDeck.Localization;

namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>Arguments for <c>host.invoke</c> against <see cref="HostApis.Notifications"/>'s
/// <c>dismiss</c> operation. <c>notify</c> sends a bare <c>MacroDeck.Sdk.Notifications.UserNotificationRequest</c>
/// as its arguments - it already carries everything the operation needs.</summary>
public sealed record NotificationsDismissArguments
{
	public required string Key { get; init; }
}

/// <summary>A button on a notification. <see cref="Style" /> is a string so an unknown style round-trips.</summary>
public sealed record NotificationsActionDto
{
	public required string Id { get; init; }

	public required LocalizedText Label { get; init; }

	public string? Style { get; init; }
}

public sealed record NotificationsProgressDto
{
	public required int Processed { get; init; }

	public int? Total { get; init; }
}

/// <summary>
/// Arguments for <c>host.invoke</c> against <see cref="HostApis.Notifications"/>'s <c>show</c> operation.
/// The vocabularies are strings so a value a newer plugin sends round-trips instead of failing the call.
/// The owner is never part of the payload: the host derives it from the authenticated session.
/// </summary>
public sealed record NotificationsShowArguments
{
	public required LocalizedText Title { get; init; }

	public LocalizedText? Message { get; init; }

	/// <summary><c>info</c>, <c>success</c>, <c>warning</c> or <c>error</c>. Absent or unknown reads as info.</summary>
	public string? Severity { get; init; }

	/// <summary><c>none</c>, <c>toast</c> or <c>dialog</c>. Absent means toast.</summary>
	public string? Popup { get; init; }

	public bool Inbox { get; init; }

	public long? DurationMs { get; init; }

	public bool Dismissible { get; init; } = true;

	/// <summary><c>all</c>, <c>admin-ui</c> or <c>devices</c>. Absent means all.</summary>
	public string? TargetKind { get; init; }

	public IReadOnlyList<Guid>? TargetDeviceIds { get; init; }

	public IReadOnlyList<NotificationsActionDto>? Actions { get; init; }

	/// <summary>A static UI tree in the widget profile, see <c>MacroDeck.Sdk.Notifications.NotificationOptions.Content</c>.</summary>
	public JsonElement? Content { get; init; }

	public NotificationsProgressDto? Progress { get; init; }
}

/// <summary>Result of <c>show</c>. It answers as soon as the notification is open. How it ends arrives
/// later as a <c>ui</c>/<c>notification.result</c> capability invoke.</summary>
public sealed record NotificationsShowResult
{
	public required Guid Id { get; init; }
}

public sealed record NotificationsUpdateArguments
{
	public required Guid Id { get; init; }

	public LocalizedText? Title { get; init; }

	public LocalizedText? Message { get; init; }

	public string? Severity { get; init; }

	public NotificationsProgressDto? Progress { get; init; }

	public IReadOnlyList<NotificationsActionDto>? Actions { get; init; }

	public JsonElement? Content { get; init; }
}

public sealed record NotificationsCloseArguments
{
	public required Guid Id { get; init; }
}
