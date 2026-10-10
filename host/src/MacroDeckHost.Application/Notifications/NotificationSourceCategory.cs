using System.Text.Json.Serialization;

namespace MacroDeckHost.Application.Notifications;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NotificationSourceCategory
{
	System,
	BuiltInIntegration,
	Plugin
}
