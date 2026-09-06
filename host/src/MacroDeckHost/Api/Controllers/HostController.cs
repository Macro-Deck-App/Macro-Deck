using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Host;
using MacroDeckHost.Application.Ui.Transport.Messages.Notifications;
using MacroDeckHost.Auth;
using MacroDeckHost.Integrations.System.Notifications;
using Microsoft.AspNetCore.Mvc;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Api.Controllers;

public record ShellNotificationsResponse(IReadOnlyList<ShellNotification> Notifications);

public record ShellNotificationResultRequest(bool Shown);

[ApiController]
[Route("api/host")]
public class HostController : ControllerBase
{
	// Shorter than the bootstrapper's own request timeout, so a poll that finds nothing ends as an
	// empty response rather than as a client-side timeout.
	private static readonly TimeSpan ShellNotificationHold = TimeSpan.FromSeconds(20);

	private readonly ILogger _logger;
	private readonly IHostApplicationLifetime _lifetime;
	private readonly IShellNotificationBridge _shellNotifications;

	private readonly IUiTransportMessageHandler<ReportUpdateStateRequest, ReportUpdateStateResponse>
		_reportUpdateState;

	private readonly IUiTransportMessageHandler<RestartApplicationRequest, RestartApplicationResponse>
		_restartApplication;

	private readonly IUiTransportMessageHandler<GetDataDirectoryRequest, GetDataDirectoryResponse>
		_getDataDirectory;

	private readonly IUiTransportMessageHandler<GetHostSessionRequest, GetHostSessionResponse> _getHostSession;

	private readonly IUiTransportMessageHandler<OpenDataDirectoryRequest, OpenDataDirectoryResponse>
		_openDataDirectory;

	public HostController(
		ILogger logger,
		IHostApplicationLifetime lifetime,
		IUiTransportMessageHandler<ReportUpdateStateRequest, ReportUpdateStateResponse> reportUpdateState,
		IUiTransportMessageHandler<RestartApplicationRequest, RestartApplicationResponse> restartApplication,
		IUiTransportMessageHandler<GetDataDirectoryRequest, GetDataDirectoryResponse> getDataDirectory,
		IUiTransportMessageHandler<OpenDataDirectoryRequest, OpenDataDirectoryResponse> openDataDirectory,
		IUiTransportMessageHandler<GetHostSessionRequest, GetHostSessionResponse> getHostSession,
		IShellNotificationBridge shellNotifications)
	{
		_logger = logger;
		_shellNotifications = shellNotifications;
		_lifetime = lifetime;
		_reportUpdateState = reportUpdateState;
		_restartApplication = restartApplication;
		_getDataDirectory = getDataDirectory;
		_openDataDirectory = openDataDirectory;
		_getHostSession = getHostSession;
	}

	[HttpGet("session")]
	public Task<GetHostSessionResponse> Session(CancellationToken ct)
		=> _getHostSession.Handle(new GetHostSessionRequest(), ct).AsTask();

	[HttpPost("shutdown")]
	public IActionResult Shutdown([FromQuery] string? reason = null)
	{
		if (!LoopbackConnection.IsTrusted(HttpContext))
		{
			return NotFound();
		}

		_logger.Information("Shutdown requested via API, reason: {Reason}", reason ?? "none");
		_lifetime.StopApplication();
		return Ok();
	}

	[HttpPost("restart")]
	public async Task<IActionResult> Restart(RestartApplicationRequest body, CancellationToken ct)
		=> Ok(await _restartApplication.Handle(body, ct));

	[HttpPost("update-state")]
	public async Task<IActionResult> UpdateState(ReportUpdateStateRequest body, CancellationToken ct)
	{
		if (!LoopbackConnection.IsTrusted(HttpContext))
		{
			return NotFound();
		}

		await _reportUpdateState.Handle(body, ct);
		return Ok();
	}

	[HttpGet("shell-notifications")]
	public async Task<IActionResult> ShellNotifications(CancellationToken ct)
	{
		if (!LoopbackConnection.IsTrusted(HttpContext))
		{
			return NotFound();
		}

		try
		{
			var notifications = await _shellNotifications.WaitAsync(ShellNotificationHold, ct);
			return Ok(new ShellNotificationsResponse(notifications));
		}
		catch (InvalidOperationException)
		{
			return Conflict();
		}
	}

	[HttpPost("shell-notifications/{id:long}/result")]
	public IActionResult ReportShellNotification(long id, ShellNotificationResultRequest body)
	{
		if (!LoopbackConnection.IsTrusted(HttpContext))
		{
			return NotFound();
		}

		_shellNotifications.Report(id, body.Shown);
		return Ok();
	}

	[HttpGet("data-directory")]
	public async Task<IActionResult> GetDataDirectory(CancellationToken ct)
	{
		var request = new GetDataDirectoryRequest { TrustedConnection = LoopbackConnection.IsTrusted(HttpContext) };
		return Ok(await _getDataDirectory.Handle(request, ct));
	}

	[HttpPost("data-directory/open")]
	public async Task<IActionResult> OpenDataDirectory(CancellationToken ct)
	{
		if (!LoopbackConnection.IsTrusted(HttpContext))
		{
			return NotFound();
		}

		return Ok(await _openDataDirectory.Handle(new OpenDataDirectoryRequest(), ct));
	}
}
