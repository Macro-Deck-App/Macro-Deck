using MacroDeckHost.Application.Logging;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Logging;

public class LogEntriesAppendedNotification
{
	public List<LogEntry> Entries { get; set; } = [];

	public List<LogSourceSummary> Sources { get; set; } = [];
}
