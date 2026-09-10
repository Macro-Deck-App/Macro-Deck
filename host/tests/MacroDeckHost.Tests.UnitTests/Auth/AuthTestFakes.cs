using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Deck;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Profiles;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using MacroDeckHost.Application.Ui.Transport.Messages.Profiles;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeck.Sdk.Profiles;

namespace MacroDeckHost.Tests.UnitTests.Auth;

internal sealed class InMemoryUserRepository : IUserRepository
{
	public UserEntity? User { get; set; }

	public Task<UserEntity?> GetSingle() => Task.FromResult(User);

	public Task<bool> AnyExists() => Task.FromResult(User is not null);

	public Task Create(UserEntity user)
	{
		User = user;
		return Task.CompletedTask;
	}

	public Task Update(UserEntity user)
	{
		User = user;
		return Task.CompletedTask;
	}
}

internal sealed class InMemoryRefreshTokenRepository : IRefreshTokenRepository
{
	public List<RefreshTokenEntity> Tokens { get; } = [];

	public Task<RefreshTokenEntity?> GetByTokenHash(string tokenHash)
		=> Task.FromResult(Tokens.FirstOrDefault(t => t.TokenHash == tokenHash));

	public Task Create(RefreshTokenEntity token)
	{
		Tokens.Add(token);
		return Task.CompletedTask;
	}

	public Task Update(RefreshTokenEntity token)
	{
		var index = Tokens.FindIndex(t => t.Id == token.Id);
		if (index >= 0)
		{
			Tokens[index] = token;
		}

		return Task.CompletedTask;
	}

	public Task RevokeAllForUser(Guid userId, DateTime revokedAt)
	{
		foreach (var token in Tokens.Where(t => t.UserId == userId && t.RevokedAt is null))
		{
			token.RevokedAt = revokedAt;
		}

		return Task.CompletedTask;
	}

	public Task RevokeAllForDevice(Guid deviceId, DateTime revokedAt)
	{
		foreach (var token in Tokens.Where(t => t.DeviceId == deviceId && t.RevokedAt is null))
		{
			token.RevokedAt = revokedAt;
		}

		return Task.CompletedTask;
	}

	public Task<IReadOnlyList<Guid>> GetDeviceIdsWithLiveTokens(DateTime now)
		=> Task.FromResult<IReadOnlyList<Guid>>(Tokens
			.Where(t => t.DeviceId is not null && t.RevokedAt is null && t.ExpiresAt >= now)
			.Select(t => t.DeviceId!.Value)
			.Distinct()
			.ToList());

	public Task DeleteExpired(DateTime now)
	{
		Tokens.RemoveAll(t => t.ExpiresAt < now ||
			(t.RevokedAt is { } revokedAt && revokedAt < now - AuthDefaults.RevokedRefreshTokenRetention));
		return Task.CompletedTask;
	}
}

internal sealed class InMemoryDeviceRepository : IDeviceRepository
{
	public List<DeviceEntity> Devices { get; } = [];

	/// <summary>How often a device was read back. An idle device session must not raise it.</summary>
	public int Reads { get; private set; }

	public Task<DeviceEntity?> GetById(Guid id)
	{
		Reads++;
		return Task.FromResult(Devices.FirstOrDefault(d => d.Id == id));
	}

	public Task<DeviceEntity?> GetByProviderIdentity(string providerId, string providerDeviceId)
		=> Task.FromResult(Devices.FirstOrDefault(d =>
			d.ProviderId == providerId && d.ProviderDeviceId == providerDeviceId));

	public Task<IReadOnlyList<DeviceEntity>> GetByProviderId(string providerId)
		=> Task.FromResult<IReadOnlyList<DeviceEntity>>(Devices.Where(d => d.ProviderId == providerId).ToList());

	public Task<IReadOnlyList<DeviceEntity>> GetAll()
		=> Task.FromResult<IReadOnlyList<DeviceEntity>>(Devices.OrderByDescending(d => d.LastSeenAt).ToList());

	public Task<IReadOnlyList<DeviceEntity>> GetByStartupProfileId(string profileId)
		=> Task.FromResult<IReadOnlyList<DeviceEntity>>(Devices.Where(d => d.StartupProfileId == profileId).ToList());

	public Task Create(DeviceEntity device)
	{
		Devices.Add(device);
		return Task.CompletedTask;
	}

	public Task Update(DeviceEntity device)
	{
		var index = Devices.FindIndex(d => d.Id == device.Id);
		if (index >= 0)
		{
			Devices[index] = device;
		}

		return Task.CompletedTask;
	}

	public Task Delete(Guid id)
	{
		Devices.RemoveAll(d => d.Id == id);
		return Task.CompletedTask;
	}

	public Task TouchLastSeen(IReadOnlyCollection<Guid> ids, DateTime seenAt)
	{
		foreach (var device in Devices.Where(d => ids.Contains(d.Id)))
		{
			device.LastSeenAt = seenAt;
		}

		return Task.CompletedTask;
	}

	public Task<IReadOnlyList<DeviceEntity>> GetStale(DateTime lastSeenBefore)
		=> Task.FromResult<IReadOnlyList<DeviceEntity>>(Devices.Where(d => d.LastSeenAt < lastSeenBefore).ToList());

	public Task DeleteMany(IReadOnlyCollection<Guid> ids)
	{
		Devices.RemoveAll(d => ids.Contains(d.Id));
		return Task.CompletedTask;
	}
}

internal sealed class RecordingUiTransport : IUiTransport
{
	public Task SendToConnection<T>(string connectionId, T message, CancellationToken cancellationToken = default)
		where T : class
		=> Task.CompletedTask;

	public Task AddToGroup(string connectionId, string group, CancellationToken cancellationToken = default)
		=> Task.CompletedTask;

	public Task RemoveFromGroup(string connectionId, string group, CancellationToken cancellationToken = default)
		=> Task.CompletedTask;

	public List<object> Sent { get; } = [];

	public List<(string Group, object Message)> GroupMessages { get; } = [];

	public Task Send<T>(T message, CancellationToken cancellationToken = default)
		where T : class
	{
		Sent.Add(message);
		return Task.CompletedTask;
	}

	public Task SendToGroup<T>(string group, T message, CancellationToken cancellationToken = default)
		where T : class
	{
		GroupMessages.Add((group, message));
		return Task.CompletedTask;
	}
}

internal sealed class FakePasswordHasher : IPasswordHasher
{
	public string Hash(string password) => "H:" + password;

	public bool Verify(string password, string encodedHash) => encodedHash == "H:" + password;
}

internal sealed class FakeAccessTokenIssuer : IAccessTokenIssuer
{
	private readonly TimeProvider _timeProvider;

	public FakeAccessTokenIssuer(TimeProvider timeProvider)
	{
		_timeProvider = timeProvider;
	}

	public Guid? LastDeviceId { get; private set; }

	public AccessToken Issue(Guid userId, string username, AuthScope scope, Guid? deviceId)
	{
		LastDeviceId = deviceId;

		var device = deviceId is { } id ? $":{id}" : string.Empty;

		return new AccessToken($"token:{username}:{AuthDefaults.ScopeClaimValue(scope)}{device}",
			_timeProvider.GetUtcNow().UtcDateTime.Add(AuthDefaults.AccessTokenLifetime));
	}
}

internal sealed class FakeProfileRegistry : IProfileRegistry
{
	private readonly List<Profile> _profiles = [];
	private readonly Dictionary<string, List<Folder>> _folders = new(StringComparer.Ordinal);

	public FakeProfileRegistry AddProfile(
		string id,
		string name,
		int defaultRows = 0,
		int defaultColumns = 0,
		string? backgroundColor = null,
		int? widgetSpacing = null,
		int? widgetBorderRadius = null)
	{
		_profiles.Add(new Profile
		{
			Id = id,
			Name = name,
			IsVirtual = IsVirtual(id),
			DefaultRows = defaultRows,
			DefaultColumns = defaultColumns,
			DefaultBackgroundColor = backgroundColor,
			DefaultWidgetSpacing = widgetSpacing,
			DefaultWidgetBorderRadius = widgetBorderRadius
		});
		return this;
	}

	public void RemoveProfile(string id) => _profiles.RemoveAll(p => p.Id == id);

	public FakeProfileRegistry SetFolders(string profileId, params Folder[] folders)
	{
		_folders[profileId] = folders.ToList();
		return this;
	}

	public IReadOnlyList<Profile> GetProfiles() => _profiles;

	public IReadOnlyList<Folder> GetFoldersForProfile(string profileId)
		=> _folders.TryGetValue(profileId, out var folders) ? folders : [];

	public bool IsVirtual(string profileId) => profileId.Contains("::", StringComparison.Ordinal);

	public Task<bool> RouteWidgetInteraction(string folderId, string widgetId, WidgetInteraction interaction)
		=> Task.FromResult(false);
}

internal sealed class FakeDeviceDeckNavigator : IDeviceDeckNavigator
{
	public List<(Guid DeviceId, Guid FolderId)> FolderCalls { get; } = [];
	public List<(Guid DeviceId, string ProfileId)> ProfileCalls { get; } = [];

	public bool ChangeFolderResult { get; set; } = true;
	public bool ChangeProfileResult { get; set; } = true;

	public Task<bool> ChangeFolderOnDeviceAsync(Guid deviceId,
		Guid folderId,
		Guid navigationToken,
		CancellationToken cancellationToken)
	{
		FolderCalls.Add((deviceId, folderId));
		return Task.FromResult(ChangeFolderResult);
	}

	public Task<bool> ChangeProfileOnDeviceAsync(Guid deviceId, string profileId, CancellationToken cancellationToken)
	{
		ProfileCalls.Add((deviceId, profileId));
		return Task.FromResult(ChangeProfileResult);
	}
}
