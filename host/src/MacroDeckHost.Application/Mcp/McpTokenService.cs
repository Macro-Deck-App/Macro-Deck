using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Mcp;

public interface IMcpTokenService
{
	Task<McpAccessTokenCreated> Create(string name);

	Task<IReadOnlyList<McpAccessToken>> GetAll();

	Task<McpAccessToken?> Authenticate(string rawToken);

	Task<McpAccessToken?> Find(string rawToken);

	Task<McpAccessTokenRevokeResult> Revoke(Guid id);

	Task<McpAccessTokenDeleteResult> Delete(Guid id);
}

public class McpTokenService : IMcpTokenService
{
	public const string TokenPrefix = "mdmcp_";
	public const int MaxNameLength = 64;

	private static readonly TimeSpan _lastUsedResolution = TimeSpan.FromMinutes(1);

	private readonly IMcpAccessTokenRepository _repository;
	private readonly IMcpConnections _connections;
	private readonly TimeProvider _timeProvider;
	private readonly IMcpSecretLedger? _secrets;

	public McpTokenService(
		IMcpAccessTokenRepository repository,
		IMcpConnections connections,
		TimeProvider timeProvider,
		IMcpSecretLedger? secrets = null)
	{
		_repository = repository;
		_connections = connections;
		_timeProvider = timeProvider;
		_secrets = secrets;
	}

	public async Task<McpAccessTokenCreated> Create(string name)
	{
		var now = _timeProvider.GetUtcNow().UtcDateTime;
		var raw = TokenPrefix + TokenHasher.Generate();
		var entity = new McpAccessTokenEntity
		{
			Id = Guid.NewGuid(),
			Name = name.Trim(),
			TokenHash = TokenHasher.Hash(raw),
			CreatedAt = now
		};

		await _repository.Create(entity);

		return new McpAccessTokenCreated { Token = ToDto(entity), PlaintextToken = raw };
	}

	public async Task<IReadOnlyList<McpAccessToken>> GetAll()
	{
		var entities = await _repository.GetAll();
		return entities.Select(ToDto).ToList();
	}

	public async Task<McpAccessToken?> Authenticate(string rawToken)
	{
		var entity = await FindEntity(rawToken);
		if (entity is null || entity.RevokedAt is not null)
		{
			return null;
		}

		var now = _timeProvider.GetUtcNow().UtcDateTime;
		if (entity.LastUsedAt is not { } lastUsed || now - lastUsed >= _lastUsedResolution)
		{
			await _repository.TouchLastUsed(entity.Id, now);
			entity.LastUsedAt = now;
		}

		return ToDto(entity);
	}

	public async Task<McpAccessToken?> Find(string rawToken)
	{
		var entity = await FindEntity(rawToken);
		return entity is null ? null : ToDto(entity);
	}

	public async Task<McpAccessTokenRevokeResult> Revoke(Guid id)
	{
		var token = await _repository.GetById(id);
		if (token is null)
		{
			return McpAccessTokenRevokeResult.NotFound;
		}

		if (token.RevokedAt is not null)
		{
			return McpAccessTokenRevokeResult.AlreadyRevoked;
		}

		await _repository.Revoke(id, _timeProvider.GetUtcNow().UtcDateTime);
		_connections.TerminateForToken(id);

		return McpAccessTokenRevokeResult.Revoked;
	}

	public async Task<McpAccessTokenDeleteResult> Delete(Guid id)
	{
		var token = await _repository.GetById(id);
		if (token is null)
		{
			return McpAccessTokenDeleteResult.NotFound;
		}

		if (token.RevokedAt is null)
		{
			return McpAccessTokenDeleteResult.NotRevoked;
		}

		await _repository.Delete(id);
		if (_secrets is not null)
		{
			await _secrets.ForgetToken(id);
		}

		return McpAccessTokenDeleteResult.Deleted;
	}

	private async Task<McpAccessTokenEntity?> FindEntity(string rawToken)
	{
		if (string.IsNullOrEmpty(rawToken) || !rawToken.StartsWith(TokenPrefix, StringComparison.Ordinal))
		{
			return null;
		}

		var entity = await _repository.GetByHash(TokenHasher.Hash(rawToken));
		return entity is null || !TokenHasher.Verify(rawToken, entity.TokenHash) ? null : entity;
	}

	private static McpAccessToken ToDto(McpAccessTokenEntity entity)
	{
		return new McpAccessToken
		{
			Id = entity.Id,
			Name = entity.Name,
			CreatedAt = AsUtc(entity.CreatedAt),
			LastUsedAt = entity.LastUsedAt is { } lastUsed ? AsUtc(lastUsed) : null,
			RevokedAt = entity.RevokedAt is { } revoked ? AsUtc(revoked) : null
		};
	}

	private static DateTime AsUtc(DateTime value)
	{
		return value.Kind == DateTimeKind.Local
			? value.ToUniversalTime()
			: DateTime.SpecifyKind(value, DateTimeKind.Utc);
	}
}
