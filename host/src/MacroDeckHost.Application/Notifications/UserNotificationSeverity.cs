using System.Text.Json.Serialization;

namespace MacroDeckHost.Application.Notifications;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UserNotificationSeverity
{
	Info,
	Warning,
	Error
}
