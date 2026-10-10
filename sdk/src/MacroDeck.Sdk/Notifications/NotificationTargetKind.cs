namespace MacroDeck.Sdk.Notifications;

public enum NotificationTargetKind
{
	/// <summary>Every connected client that can show notifications.</summary>
	All,

	/// <summary>The Macro Deck desktop app and any administrator session.</summary>
	AdminUi,

	/// <summary>The listed devices, identified by their authenticated device id.</summary>
	Devices
}
