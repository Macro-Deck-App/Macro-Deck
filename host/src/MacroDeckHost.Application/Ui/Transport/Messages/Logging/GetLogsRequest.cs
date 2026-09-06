using MacroDeckHost.Application.Logging;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Logging;

public class GetLogsRequest
{
	public int? Limit { get; set; }

	public string? Before { get; set; }

	public List<LogEntryLevel>? Levels { get; set; }

	public LogEntrySource? Source { get; set; }

	public string? IntegrationId { get; set; }

	public string? Category { get; set; }

	public string? Search { get; set; }

	public DateTimeOffset? From { get; set; }

	public DateTimeOffset? To { get; set; }

	public LogQuery ToQuery()
		=> new()
		{
			Levels = Levels,
			Source = Source,
			IntegrationId = IntegrationId,
			Category = Category,
			Search = Search,
			From = From,
			To = To
		};
}
