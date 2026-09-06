using MacroDeck.Plugin.Protocol.Errors;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Plugins;

public sealed record PluginAccessToken
{
	public required Guid Id { get; init; }

	public required string Name { get; init; }

	public required IReadOnlyList<string> Scopes { get; init; }

	public DateTime? ExpiresAt { get; init; }

	public DateTime? LastUsedAt { get; init; }

	public DateTime? RevokedAt { get; init; }

	public required DateTime CreatedAt { get; init; }
}

public sealed record PluginAccessTokenCreated
{
	public required PluginAccessToken Token { get; init; }

	public required string PlaintextToken { get; init; }
}

/// <summary>
/// Outcome of removing a developer token for good. Removal is deliberately not a stronger revoke:
/// only an already-revoked credential can be removed, so the two actions stay distinguishable in
/// the UI and a live credential can never be destroyed by one click.
/// </summary>
public enum PluginAccessTokenDeleteResult
{
	Deleted,
	NotFound,
	NotRevoked
}

public interface IPluginTokenService
{
	Task<PluginAccessTokenCreated> Create(string name, int? expiresInDays);

	Task<IReadOnlyList<PluginAccessToken>> GetAll();

	Task<PluginAccessTokenEntity?> Authenticate(string rawToken);

	Task<int> Revoke(Guid id);

	/// <summary>
	/// Removes an already-revoked token and the plugin registrations belonging to it. Refuses a token
	/// that is still live with <see cref="PluginAccessTokenDeleteResult.NotRevoked"/>.
	/// </summary>
	Task<PluginAccessTokenDeleteResult> Delete(Guid id);
}

public class PluginTokenService : IPluginTokenService
{
	private readonly IPluginAccessTokenRepository _tokenRepository;
	private readonly IPluginRegistrationRepository _registrationRepository;
	private readonly IPluginSessionRegistry _sessionRegistry;
	private readonly IPluginIdentityForgetter _forgetter;
	private readonly TimeProvider _timeProvider;

	public PluginTokenService(
		IPluginAccessTokenRepository tokenRepository,
		IPluginRegistrationRepository registrationRepository,
		IPluginSessionRegistry sessionRegistry,
		IPluginIdentityForgetter forgetter,
		TimeProvider timeProvider)
	{
		_tokenRepository = tokenRepository;
		_registrationRepository = registrationRepository;
		_sessionRegistry = sessionRegistry;
		_forgetter = forgetter;
		_timeProvider = timeProvider;
	}

	public async Task<PluginAccessTokenCreated> Create(string name, int? expiresInDays)
	{
		var now = _timeProvider.GetUtcNow().UtcDateTime;
		var raw = TokenHasher.Generate();
		var entity = new PluginAccessTokenEntity
		{
			Id = Guid.NewGuid(),
			Name = name,
			TokenHash = TokenHasher.Hash(raw),
			Scopes = PluginTokenScopes.Enroll,
			ExpiresAt = expiresInDays is { } days ? now.AddDays(days) : null,
			CreatedAt = now
		};

		await _tokenRepository.Create(entity);

		return new PluginAccessTokenCreated { Token = ToDto(entity), PlaintextToken = raw };
	}

	public async Task<IReadOnlyList<PluginAccessToken>> GetAll()
	{
		var entities = await _tokenRepository.GetAll();
		return entities.Select(ToDto).ToList();
	}

	public async Task<PluginAccessTokenEntity?> Authenticate(string rawToken)
	{
		if (string.IsNullOrEmpty(rawToken))
		{
			return null;
		}

		var hash = TokenHasher.Hash(rawToken);
		var entity = await _tokenRepository.GetByHash(hash);
		if (entity is null || !TokenHasher.Verify(rawToken, entity.TokenHash))
		{
			return null;
		}

		if (entity.RevokedAt is not null)
		{
			return null;
		}

		var now = _timeProvider.GetUtcNow().UtcDateTime;
		if (entity.ExpiresAt is { } expiresAt && expiresAt <= now)
		{
			return null;
		}

		return entity;
	}

	public async Task<int> Revoke(Guid id)
	{
		var now = _timeProvider.GetUtcNow().UtcDateTime;
		var token = await _tokenRepository.GetById(id);
		if (token is null || token.RevokedAt is not null)
		{
			return 0;
		}

		await _tokenRepository.Revoke(id, now);

		var affectedPluginIds = await _registrationRepository.RevokeByAccessTokenId(id, now);

		var terminated = 0;
		foreach (var pluginId in affectedPluginIds)
		{
			if (await _sessionRegistry.TerminateForPlugin(pluginId,
				ProtocolCloseCodes.AuthenticationFailed,
				"Developer token revoked."))
			{
				terminated++;
			}

			// Independent of whether a session was terminated: a plugin that happens to be offline right
			// now is just as gone as one that was connected, and its compatibility verdict would otherwise
			// keep a row in the Developer Tools "Compatibility" tab for a credential that no longer exists.
			_forgetter.Forget(pluginId);
		}

		return terminated;
	}

	public async Task<PluginAccessTokenDeleteResult> Delete(Guid id)
	{
		var token = await _tokenRepository.GetById(id);
		if (token is null)
		{
			return PluginAccessTokenDeleteResult.NotFound;
		}

		if (token.RevokedAt is null)
		{
			return PluginAccessTokenDeleteResult.NotRevoked;
		}

		var removedPluginIds = await _registrationRepository.DeleteByAccessTokenId(id);
		await _tokenRepository.Delete(id);

		foreach (var pluginId in removedPluginIds)
		{
			// Revoke already forgot these when it dropped the credential, but a registration can also
			// have been revoked on its own; forgetting again is cheap and leaves nothing behind.
			_forgetter.Forget(pluginId);
		}

		return PluginAccessTokenDeleteResult.Deleted;
	}

	private static PluginAccessToken ToDto(PluginAccessTokenEntity entity) => new()
	{
		Id = entity.Id,
		Name = entity.Name,
		Scopes = entity.Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
		ExpiresAt = entity.ExpiresAt,
		LastUsedAt = entity.LastUsedAt,
		RevokedAt = entity.RevokedAt,
		CreatedAt = entity.CreatedAt
	};
}
