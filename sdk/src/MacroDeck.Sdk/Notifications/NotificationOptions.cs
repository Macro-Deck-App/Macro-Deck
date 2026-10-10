using MacroDeck.Localization;
using MacroDeck.Ui.Model.Nodes;

namespace MacroDeck.Sdk.Notifications;

/// <summary>What to show. Macro Deck derives the source (system, built-in integration or plugin) itself.</summary>
public sealed record NotificationOptions
{
	public required LocalizedText Title { get; init; }

	public LocalizedText? Message { get; init; }

	public NotificationSeverity Severity { get; init; } = NotificationSeverity.Info;

	/// <summary>The popup to raise. At least one of this and <see cref="Inbox" /> must be set.</summary>
	public NotificationPopup Popup { get; init; } = NotificationPopup.Toast;

	/// <summary>Also keeps an entry in the Inbox. Closing the popup never removes it.</summary>
	public bool Inbox { get; init; }

	/// <summary>
	/// How long the popup stays. The host clamps it and also applies its own maximum lifetime to every
	/// popup. Null means the default of the popup kind: a few seconds for a toast, until answered for a
	/// dialog.
	/// </summary>
	public TimeSpan? Duration { get; init; }

	/// <summary>Whether the user can close the popup without choosing an action.</summary>
	public bool Dismissible { get; init; } = true;

	public NotificationTarget Target { get; init; } = NotificationTarget.All;

	/// <summary>At most four. The first valid press is the notification's result on every client.</summary>
	public IReadOnlyList<NotificationAction> Actions { get; init; } = [];

	/// <summary>
	/// A static Macro Deck UI tree shown in a dialog. It uses the widget profile. A text field, toggle,
	/// slider or segmented control that declares a change or adjust event is an input, its node id is
	/// the field name in <see cref="NotificationResult.Values" />. Required fields and patterns are not
	/// part of the widget profile: validate the returned values yourself.
	/// </summary>
	public UiNode? Content { get; init; }

	public NotificationProgress? Progress { get; init; }
}
