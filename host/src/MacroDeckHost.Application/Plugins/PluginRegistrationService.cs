using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Plugins;

public enum PluginRegistrationError
{
	InvalidPluginId,
	AlreadyRegistered
}

public sealed record PluginRegistrationResult
{
	public required bool Succeeded { get; init; }

	public PluginRegistrationError? Error { get; init; }

	public string? ErrorDetail { get; init; }

	public PluginRegistrationEntity? Registration { get; init; }

	public string? PluginSecret { get; init; }

	public static PluginRegistrationResult Success(PluginRegistrationEntity registration, string secret)
		=> new() { Succeeded = true, Registration = registration, PluginSecret = secret };

	public static PluginRegistrationResult Fail(PluginRegistrationError error, string? detail = null)
		=> new() { Succeeded = false, Error = error, ErrorDetail = detail };
}

public interface IPluginRegistrationService
{
	Task<PluginRegistrationResult> Register(string pluginId, string displayName, Guid? accessTokenId, string origin);

	Task<PluginRegistrationResult> ReplaceSecret(string pluginId,
		string displayName,
		Guid? accessTokenId,
		string origin);

	Task<PluginRegistrationEntity?> Authenticate(string pluginId, string secret);

	Task Revoke(string pluginId);
}

public class PluginRegistrationService : IPluginRegistrationService
{
	private readonly IPluginRegistrationRepository _registrationRepository;
	private readonly IPluginAccessTokenRepository _tokenRepository;
	private readonly IPluginSessionRegistry _sessionRegistry;
	private readonly IPluginIdentityForgetter _forgetter;
	private readonly IPluginInstallationCatalog _catalog;
	private readonly TimeProvider _timeProvider;

	public PluginRegistrationService(
		IPluginRegistrationRepository registrationRepository,
		IPluginAccessTokenRepository tokenRepository,
		IPluginSessionRegistry sessionRegistry,
		IPluginIdentityForgetter forgetter,
		IPluginInstallationCatalog catalog,
		TimeProvider timeProvider)
	{
		_registrationRepository = registrationRepository;
		_tokenRepository = tokenRepository;
		_sessionRegistry = sessionRegistry;
		_forgetter = forgetter;
		_catalog = catalog;
		_timeProvider = timeProvider;
	}

	public async Task<PluginRegistrationResult> Register(string pluginId,
		string displayName,
		Guid? accessTokenId,
		string origin)
	{
		if (!PluginId.TryValidate(pluginId, out var validationError))
		{
			return PluginRegistrationResult.Fail(PluginRegistrationError.InvalidPluginId, validationError);
		}

		if (_catalog.Discover().Any(plugin =>
			string.Equals(plugin.PluginId, pluginId, StringComparison.Ordinal) && plugin.Versions.Count > 0))
		{
			return PluginRegistrationResult.Fail(PluginRegistrationError.AlreadyRegistered);
		}

		var existing = await _registrationRepository.GetByPluginId(pluginId);
		if (existing is { RevokedAt: null })
		{
			return PluginRegistrationResult.Fail(PluginRegistrationError.AlreadyRegistered);
		}

		var now = _timeProvider.GetUtcNow().UtcDateTime;
		var secret = TokenHasher.Generate();
		var secretHash = TokenHasher.Hash(secret);

		if (existing is { RevokedAt: not null })
		{
			// A revoked plugin id is re-enrolled in place rather than left permanently stranded - the
			// unique index means it can never go through Create again. Reactivate only touches a row
			// that is still revoked, so a concurrent reactivation loses this race cleanly instead of
			// corrupting the other caller's write.
			var reactivated = await _registrationRepository.Reactivate(pluginId,
				displayName,
				secretHash,
				accessTokenId,
				origin,
				now);
			if (!reactivated)
			{
				return PluginRegistrationResult.Fail(PluginRegistrationError.AlreadyRegistered);
			}

			if (accessTokenId is { } reactivatedTokenId)
			{
				await _tokenRepository.TouchLastUsed(reactivatedTokenId, now);
			}

			var registrationRow = await _registrationRepository.GetByPluginId(pluginId);
			return PluginRegistrationResult.Success(registrationRow!, secret);
		}

		var registration = new PluginRegistrationEntity
		{
			Id = Guid.NewGuid(),
			PluginId = pluginId,
			DisplayName = displayName,
			SecretHash = secretHash,
			AccessTokenId = accessTokenId,
			Origin = origin,
			CreatedAt = now
		};

		try
		{
			await _registrationRepository.Create(registration);
		}
		catch (PluginRegistrationConflictException)
		{
			// The pre-check above raced another registration for the same id.
			return PluginRegistrationResult.Fail(PluginRegistrationError.AlreadyRegistered);
		}

		if (accessTokenId is { } tokenId)
		{
			await _tokenRepository.TouchLastUsed(tokenId, now);
		}

		return PluginRegistrationResult.Success(registration, secret);
	}

	public async Task<PluginRegistrationResult> ReplaceSecret(string pluginId,
		string displayName,
		Guid? accessTokenId,
		string origin)
	{
		var secret = TokenHasher.Generate();
		var secretHash = TokenHasher.Hash(secret);

		var rotated = await _registrationRepository.RotateSecret(pluginId,
			displayName,
			secretHash,
			accessTokenId,
			origin);
		if (!rotated)
		{
			return PluginRegistrationResult.Fail(PluginRegistrationError.AlreadyRegistered);
		}

		// Rotate first, terminate second: the single conditional update above replaces the hash
		// atomically, so there is never a moment where both the old and the new secret authenticate.
		// Terminating the live session before the rotation lands would leave a window in which the old
		// secret could still open a fresh session.
		await _sessionRegistry.TerminateForPlugin(pluginId,
			ProtocolCloseCodes.AuthenticationFailed,
			"Plugin credential replaced.");

		_forgetter.Forget(pluginId);

		var registrationRow = await _registrationRepository.GetByPluginId(pluginId);
		return PluginRegistrationResult.Success(registrationRow!, secret);
	}

	public async Task<PluginRegistrationEntity?> Authenticate(string pluginId, string secret)
	{
		if (string.IsNullOrEmpty(pluginId) || string.IsNullOrEmpty(secret))
		{
			return null;
		}

		var registration = await _registrationRepository.GetByPluginId(pluginId);
		if (registration is null || registration.RevokedAt is not null)
		{
			return null;
		}

		if (!TokenHasher.Verify(secret, registration.SecretHash))
		{
			return null;
		}

		if (registration.AccessTokenId is not { } accessTokenId)
		{
			// A paired registration has no owning Developer token - it was created directly by user
			// approval, so its validity is its own RevokedAt alone, already checked above.
			return registration;
		}

		var token = await _tokenRepository.GetById(accessTokenId);
		if (token is null || token.RevokedAt is not null)
		{
			return null;
		}

		var now = _timeProvider.GetUtcNow().UtcDateTime;
		if (token.ExpiresAt is { } expiresAt && expiresAt <= now)
		{
			return null;
		}

		return registration;
	}

	public async Task Revoke(string pluginId)
	{
		var now = _timeProvider.GetUtcNow().UtcDateTime;
		await _registrationRepository.Revoke(pluginId, now);
		await _sessionRegistry.TerminateForPlugin(pluginId,
			ProtocolCloseCodes.AuthenticationFailed,
			"Plugin registration revoked.");

		// A genuine, host-initiated "this plugin id is gone" signal - unlike an ordinary session end or
		// reconnect, a plugin cannot trigger this itself. Doing this on session end instead would let a
		// reconnect loop re-log the same compatibility findings forever and reset its own log budget.
		_forgetter.Forget(pluginId);
	}
}
