using MacroDeckHost.Application.Logging;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Logging;

public class GetLogSourcesRequest
{
}

public class GetLogSourcesResponse
{
	public List<LogSourceSummary> Sources { get; set; } = [];
}
