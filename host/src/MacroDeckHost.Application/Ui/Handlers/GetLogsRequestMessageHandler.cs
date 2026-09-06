using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Logging;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetLogsRequestMessageHandler : IUiTransportMessageHandler<GetLogsRequest, GetLogsResponse>
{
	private readonly ILogFileReader _reader;

	public GetLogsRequestMessageHandler(ILogFileReader reader)
	{
		_reader = reader;
	}

	public ValueTask<GetLogsResponse> Handle(GetLogsRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		var before = LogCursor.TryParse(request.Before, out var cursor) ? cursor : (LogCursor?)null;
		var page = _reader.ReadPage(request.ToQuery(), before, request.Limit ?? 0);

		return ValueTask.FromResult(new GetLogsResponse
		{
			Entries = page.Entries.ToList(),
			OlderCursor = page.Older?.Encode(),
			HasMore = page.Older is not null,
			TailAnchor = page.TailAnchor.Encode()
		});
	}
}
