using MacroDeck.Localization;
using MacroDeck.Ui.Model.Nodes;

namespace MacroDeck.Sdk.Notifications;

/// <summary>
/// A change to a notification that is still open. A null member keeps its current value. The presentation,
/// target and duration cannot change after it was shown.
/// </summary>
public sealed record NotificationUpdate
{
	public LocalizedText? Title { get; init; }

	public LocalizedText? Message { get; init; }

	public NotificationSeverity? Severity { get; init; }

	public NotificationProgress? Progress { get; init; }

	public IReadOnlyList<NotificationAction>? Actions { get; init; }

	public UiNode? Content { get; init; }
}
