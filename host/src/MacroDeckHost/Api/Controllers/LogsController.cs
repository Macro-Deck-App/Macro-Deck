using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Logging;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/logs")]
public class LogsController : ControllerBase
{
	private readonly IUiTransportMessageHandler<GetLogsRequest, GetLogsResponse> _getLogs;
	private readonly IUiTransportMessageHandler<GetLogSourcesRequest, GetLogSourcesResponse> _getSources;

	public LogsController(
		IUiTransportMessageHandler<GetLogsRequest, GetLogsResponse> getLogs,
		IUiTransportMessageHandler<GetLogSourcesRequest, GetLogSourcesResponse> getSources)
	{
		_getLogs = getLogs;
		_getSources = getSources;
	}

	[HttpGet]
	public Task<GetLogsResponse> GetLogs([FromQuery] GetLogsRequest request, CancellationToken ct)
		=> _getLogs.Handle(request, ct).AsTask();

	[HttpGet("sources")]
	public Task<GetLogSourcesResponse> GetSources(CancellationToken ct)
		=> _getSources.Handle(new GetLogSourcesRequest(), ct).AsTask();
}
