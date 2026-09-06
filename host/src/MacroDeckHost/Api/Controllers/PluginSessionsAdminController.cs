using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Ui.Transport.Messages;
using Mediator;
using Microsoft.AspNetCore.Mvc;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Api.Controllers;

public record PluginSessionInfoBody(
	string SessionId,
	string PluginId,
	string DisplayName,
	string? InstanceId,
	Guid? TokenId,
	string Origin,
	int NegotiatedVersion,
	string State,
	DateTimeOffset ConnectedAt,
	DateTimeOffset LastSeenAt);

public record GetPluginSessionsResponse(IReadOnlyList<PluginSessionInfoBody> Sessions);

public record TerminatePluginSessionResponse(bool Success, TransportError? Error);

[ApiController]
[Route("api/plugin-sessions")]
public class PluginSessionsAdminController : ControllerBase
{
	private readonly IPluginSessionRegistry _sessionRegistry;
	private readonly IPluginSessionService _sessionService;
	private readonly IMediator _mediator;

	public PluginSessionsAdminController(
		IPluginSessionRegistry sessionRegistry,
		IPluginSessionService sessionService,
		IMediator mediator)
	{
		_sessionRegistry = sessionRegistry;
		_sessionService = sessionService;
		_mediator = mediator;
	}

	[HttpGet]
	public GetPluginSessionsResponse GetAll()
	{
		var sessions = _sessionRegistry.Snapshot();

		var body = sessions.Select(session =>
		{
			var connectedAt = session.ConnectedAt ?? session.CreatedAt;
			var lastSeenAt = session.DroppedAt ?? session.ConnectedAt ?? session.CreatedAt;

			return new PluginSessionInfoBody(session.SessionId,
				session.PluginId,
				session.DisplayName,
				session.InstanceId,
				session.AccessTokenId,
				ToWireOrigin(session.Origin),
				session.NegotiatedVersion,
				ToWireState(session.State),
				connectedAt,
				lastSeenAt);
		}).ToList();

		return new GetPluginSessionsResponse(body);
	}

	[HttpPost("{sessionId}/terminate")]
	public async Task<TerminatePluginSessionResponse> Terminate(string sessionId, CancellationToken ct)
	{
		var terminated = await _sessionService.Close(sessionId, 1000, "Terminated by an administrator.");
		if (!terminated)
		{
			return new TerminatePluginSessionResponse(false,
				new TransportError { Code = "not_found", Message = AppStrings.Errors.Plugins.SessionNotFound() });
		}

		await _mediator.Publish(new PluginSessionsChangedNotification(), ct);

		return new TerminatePluginSessionResponse(true, null);
	}

	// TokenId cannot stand in for this: an interactively paired plugin has no owning token, so it and
	// a host-launched plugin are both null there.
	private static string ToWireOrigin(PluginSessionOrigin origin) => origin switch
	{
		PluginSessionOrigin.Managed => "managed",
		_ => "self-registered"
	};

	private static string ToWireState(PluginSessionState state) => state switch
	{
		PluginSessionState.Awaiting => "awaiting",
		PluginSessionState.Connected => "connected",
		_ => "dropped"
	};
}
