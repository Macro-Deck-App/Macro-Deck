using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Version;
using Serilog;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetVersionRequestMessageHandler : IUiTransportMessageHandler<GetVersionRequest, GetVersionResponse>
{
	private readonly ILogger _logger = Log.ForContext<GetVersionRequestMessageHandler>();

	public ValueTask<GetVersionResponse> Handle(GetVersionRequest request, CancellationToken cancellationToken)
	{
		_logger.Information("Received GetVersionRequest");

		var version = HostVersion.Current;

		var response = new GetVersionResponse
		{
			Version = version,
			IsBeta = HostVersion.IsBeta
		};

		_logger.Debug("Returning GetVersionResponse with version {Version}", version);
		return ValueTask.FromResult(response);
	}
}
