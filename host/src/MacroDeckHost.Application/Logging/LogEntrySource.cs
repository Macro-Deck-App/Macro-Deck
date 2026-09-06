using System.Text.Json.Serialization;

namespace MacroDeckHost.Application.Logging;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LogEntrySource
{
	Host = 0,
	Bootstrapper = 1,
	Integration = 2
}
