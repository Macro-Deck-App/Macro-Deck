using System.Text.Json.Serialization;

namespace MacroDeckHost.Application.Logging;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LogEntryLevel
{
	Verbose = 0,
	Debug = 1,
	Information = 2,
	Warning = 3,
	Error = 4,
	Fatal = 5
}
