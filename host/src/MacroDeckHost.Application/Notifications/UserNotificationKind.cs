using System.Text.Json.Serialization;

namespace MacroDeckHost.Application.Notifications;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UserNotificationKind
{
	Error,
	Update,
	Integration,
	IconImport,
	Security,
	General
}
