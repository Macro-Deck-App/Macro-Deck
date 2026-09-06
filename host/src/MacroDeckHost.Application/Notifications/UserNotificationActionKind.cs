using System.Text.Json.Serialization;

namespace MacroDeckHost.Application.Notifications;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UserNotificationActionKind
{
	None,
	OpenIntegration,
	OpenLogs,
	OpenIconPacks,
	OpenUpdateSettings,

	RestartApplication,

	OpenExtensionStore,

	OpenUpdateDetails,
	InstallUpdate,
	DismissNotification
}
