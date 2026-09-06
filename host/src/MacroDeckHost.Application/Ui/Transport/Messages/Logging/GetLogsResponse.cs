using MacroDeckHost.Application.Logging;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Logging;

public class GetLogsResponse
{
	public List<LogEntry> Entries { get; set; } = [];

	public string? OlderCursor { get; set; }

	public bool HasMore { get; set; }

	public string TailAnchor { get; set; } = string.Empty;
}
