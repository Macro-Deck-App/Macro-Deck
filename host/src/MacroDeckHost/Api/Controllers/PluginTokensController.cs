using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Ui.Transport.Messages;
using Mediator;
using Microsoft.AspNetCore.Mvc;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Api.Controllers;

public record PluginRegistrationSummaryBody(
	string PluginId,
	string DisplayName,
	DateTime CreatedAt,
	DateTime? LastSeenAt,
	DateTime? RevokedAt,
	bool Online);

public record PluginAccessTokenBody(
	Guid Id,
	string Name,
	IReadOnlyList<string> Scopes,
	DateTime CreatedAt,
	DateTime? ExpiresAt,
	DateTime? LastUsedAt,
	DateTime? RevokedAt,
	IReadOnlyList<PluginRegistrationSummaryBody> Registrations,
	int ActiveSessionCount);

public record GetPluginTokensResponse(IReadOnlyList<PluginAccessTokenBody> Tokens);

public record CreatePluginTokenBody(string Name, int? ExpiresInDays);

public record CreatePluginTokenResponse(PluginAccessTokenBody Token, string Plaintext);

public record RevokePluginTokenResponse(bool Success, TransportError? Error, int TerminatedSessions);

public record DeletePluginTokenResponse(bool Success, TransportError? Error);

[ApiController]
[Route("api/plugin-tokens")]
public class PluginTokensController : ControllerBase
{
	private readonly IPluginTokenService _tokenService;
	private readonly IPluginRegistrationRepository _registrationRepository;
	private readonly IPluginSessionRegistry _sessionRegistry;
	private readonly IMediator _mediator;

	public PluginTokensController(
		IPluginTokenService tokenService,
		IPluginRegistrationRepository registrationRepository,
		IPluginSessionRegistry sessionRegistry,
		IMediator mediator)
	{
		_tokenService = tokenService;
		_registrationRepository = registrationRepository;
		_sessionRegistry = sessionRegistry;
		_mediator = mediator;
	}

	[HttpGet]
	public async Task<GetPluginTokensResponse> GetAll()
	{
		var tokens = await _tokenService.GetAll();
		var registrations = await _registrationRepository.GetAll();
		var sessions = _sessionRegistry.Snapshot();
		var liveSessionPluginIds = sessions
			.Where(session => session.State != PluginSessionState.Dropped)
			.Select(session => session.PluginId)
			.ToHashSet(StringComparer.Ordinal);

		var body = tokens.Select(token =>
		{
			var tokenRegistrations = registrations
				.Where(registration => registration.AccessTokenId == token.Id)
				.Select(registration => new PluginRegistrationSummaryBody(registration.PluginId,
					registration.DisplayName,
					registration.CreatedAt,
					registration.LastSeenAt,
					registration.RevokedAt,
					liveSessionPluginIds.Contains(registration.PluginId)))
				.ToList();

			var activeSessionCount = tokenRegistrations
				.Count(registration => liveSessionPluginIds.Contains(registration.PluginId));

			return new PluginAccessTokenBody(token.Id,
				token.Name,
				token.Scopes,
				token.CreatedAt,
				token.ExpiresAt,
				token.LastUsedAt,
				token.RevokedAt,
				tokenRegistrations,
				activeSessionCount);
		}).ToList();

		return new GetPluginTokensResponse(body);
	}

	[HttpPost]
	public async Task<IActionResult> Create(CreatePluginTokenBody body, CancellationToken ct)
	{
		var created = await _tokenService.Create(body.Name, body.ExpiresInDays);
		await _mediator.Publish(new PluginTokensChangedNotification(), ct);

		var dto = new PluginAccessTokenBody(created.Token.Id,
			created.Token.Name,
			created.Token.Scopes,
			created.Token.CreatedAt,
			created.Token.ExpiresAt,
			created.Token.LastUsedAt,
			created.Token.RevokedAt,
			[],
			0);

		return StatusCode(StatusCodes.Status201Created, new CreatePluginTokenResponse(dto, created.PlaintextToken));
	}

	[HttpPost("{id:guid}/revoke")]
	public async Task<RevokePluginTokenResponse> Revoke(Guid id, CancellationToken ct)
	{
		var existing = (await _tokenService.GetAll()).FirstOrDefault(token => token.Id == id);
		if (existing is null)
		{
			return new RevokePluginTokenResponse(false,
				new TransportError { Code = "not_found", Message = AppStrings.Errors.Plugins.NoDeveloperToken() },
				0);
		}

		if (existing.RevokedAt is not null)
		{
			return new RevokePluginTokenResponse(false,
				new TransportError
					{ Code = "already_revoked", Message = AppStrings.Errors.Plugins.DeveloperTokenAlreadyRevoked() },
				0);
		}

		var terminated = await _tokenService.Revoke(id);
		await _mediator.Publish(new PluginTokensChangedNotification(), ct);
		if (terminated > 0)
		{
			await _mediator.Publish(new PluginSessionsChangedNotification(), ct);
		}

		return new RevokePluginTokenResponse(true, null, terminated);
	}

	/// <summary>
	/// Removes a revoked token for good, together with the plugin registrations that belonged to it.
	/// Reports failure in the same envelope as <see cref="Revoke" /> rather than as a status code, so
	/// a caller handles both outcomes the same way.
	/// </summary>
	[HttpDelete("{id:guid}")]
	public async Task<DeletePluginTokenResponse> Delete(Guid id, CancellationToken ct)
	{
		var result = await _tokenService.Delete(id);

		switch (result)
		{
			case PluginAccessTokenDeleteResult.NotFound:
				return new DeletePluginTokenResponse(false,
					new TransportError
						{ Code = "not_found", Message = AppStrings.Errors.Plugins.NoDeveloperToken() });
			case PluginAccessTokenDeleteResult.NotRevoked:
				return new DeletePluginTokenResponse(false,
					new TransportError
					{
						Code = "not_revoked", Message = AppStrings.Errors.Plugins.DeveloperTokenNotRevoked()
					});
		}

		await _mediator.Publish(new PluginTokensChangedNotification(), ct);

		return new DeletePluginTokenResponse(true, null);
	}
}
