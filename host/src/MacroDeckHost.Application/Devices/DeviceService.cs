using System.Text;
using MacroDeck.Localization;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Deck;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Profiles;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Devices;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using Mediator;

namespace MacroDeckHost.Application.Devices;

public class DeviceService : IDeviceService
{
	private readonly IDeviceRepository _deviceRepository;
	private readonly IRefreshTokenRepository _refreshTokenRepository;
	private readonly DeviceConnectionTracker _connectionTracker;
	private readonly IUiTransport _uiTransport;
	private readonly IMediator _mediator;
	private readonly TimeProvider _timeProvider;
	private readonly IProfileRegistry _profileRegistry;
	private readonly IDeviceDeckNavigator _deckNavigator;
	private readonly StartupReadiness _readiness;
	private readonly ProviderDevicePresenceTracker _providerPresence;
	private readonly IIntegrationRegistry _integrationRegistry;

	public DeviceService(
		IDeviceRepository deviceRepository,
		IRefreshTokenRepository refreshTokenRepository,
		DeviceConnectionTracker connectionTracker,
		IUiTransport uiTransport,
		IMediator mediator,
		TimeProvider timeProvider,
		IProfileRegistry profileRegistry,
		IDeviceDeckNavigator deckNavigator,
		StartupReadiness readiness,
		ProviderDevicePresenceTracker providerPresence,
		IIntegrationRegistry integrationRegistry)
	{
		_deviceRepository = deviceRepository;
		_refreshTokenRepository = refreshTokenRepository;
		_connectionTracker = connectionTracker;
		_uiTransport = uiTransport;
		_mediator = mediator;
		_timeProvider = timeProvider;
		_profileRegistry = profileRegistry;
		_deckNavigator = deckNavigator;
		_readiness = readiness;
		_providerPresence = providerPresence;
		_integrationRegistry = integrationRegistry;
	}

	public async Task<DeviceRegistrationResult> RegisterOrReuse(DeviceRegistration registration, DateTime now)
	{
		var existing = await Resolve(registration);
		var result = existing is null ? await Mint(registration, now) : await Reuse(existing, registration, now);

		_connectionTracker.SetDeviceName(result.Device.Id, result.Device.Name);

		await _mediator.Publish(new DeviceChangedNotification(result.Device.Id));

		return result;
	}

	public async Task PurgeStale(DateTime now)
	{
		// A provider device is exempt: it is stale exactly while its hardware is away, and deleting one
		// would discard the identity a reconnecting device is supposed to be recognised by.
		var stale = (await _deviceRepository.GetStale(now - DeviceDefaults.StaleDeviceRetention))
			.Where(device => !device.IsProviderDevice)
			.Select(device => device.Id)
			.ToArray();

		if (stale.Length == 0)
		{
			return;
		}

		var live = (await _refreshTokenRepository.GetDeviceIdsWithLiveTokens(now)).ToHashSet();
		var removable = stale.Where(id => !live.Contains(id)).ToArray();

		await _deviceRepository.DeleteMany(removable);

		foreach (var id in removable)
		{
			await _mediator.Publish(new DeviceRemovedNotification(id));
		}
	}

	public async Task<IReadOnlyList<Device>> GetAll()
	{
		var now = UtcNow();
		var devices = await _deviceRepository.GetAll();
		var onlineCounts = _connectionTracker.OnlineDeviceConnectionCounts();
		var liveSessionIds = (await _refreshTokenRepository.GetDeviceIdsWithLiveTokens(now)).ToHashSet();
		var profileNames = ProfileNameMap();
		var providerNames = ProviderNameMap(devices);

		return devices
			.Select(device => BuildDto(device,
				onlineCounts.GetValueOrDefault(device.Id),
				liveSessionIds.Contains(device.Id),
				now,
				profileNames,
				providerNames,
				_providerPresence.IsOnline(device.Id)))
			.ToList();
	}

	public async Task<Device> ToDto(DeviceEntity device)
	{
		var now = UtcNow();
		var onlineCounts = _connectionTracker.OnlineDeviceConnectionCounts();
		var hasActiveSession = (await _refreshTokenRepository.GetDeviceIdsWithLiveTokens(now)).Contains(device.Id);

		return BuildDto(device,
			onlineCounts.GetValueOrDefault(device.Id),
			hasActiveSession,
			now,
			ProfileNameMap(),
			ProviderNameMap([device]),
			_providerPresence.IsOnline(device.Id));
	}

	public async Task<Result<DeviceEntity, DeviceError>> Rename(Guid id, string name)
	{
		var device = await _deviceRepository.GetById(id);
		if (device is null)
		{
			return Result.Fail<DeviceEntity, DeviceError>(DeviceError.NotFound);
		}

		var sanitized = Sanitize(name);
		if (sanitized is null)
		{
			return Result.Fail<DeviceEntity, DeviceError>(DeviceError.ValidationError, "Name must not be empty.");
		}

		device.Name = sanitized;
		device.NameIsCustom = true;
		await _deviceRepository.Update(device);

		_connectionTracker.SetDeviceName(id, sanitized);

		await _mediator.Publish(new DeviceChangedNotification(id));

		return Result.Ok<DeviceEntity, DeviceError>(device);
	}

	public async Task<Result<DeviceEntity, DeviceError>> SetStartupProfile(Guid id, string? profileId)
	{
		var device = await _deviceRepository.GetById(id);
		if (device is null)
		{
			return Result.Fail<DeviceEntity, DeviceError>(DeviceError.NotFound, "Device not found.");
		}

		if (profileId is not null && _profileRegistry.GetProfiles().All(p => p.Id != profileId))
		{
			return Result.Fail<DeviceEntity, DeviceError>(DeviceError.ValidationError,
				"That profile no longer exists.");
		}

		device.StartupProfileId = profileId;
		await _deviceRepository.Update(device);

		await _mediator.Publish(new DeviceChangedNotification(id));

		return Result.Ok<DeviceEntity, DeviceError>(device);
	}

	public async Task<string?> ResolveStartupProfileId(Guid deviceId)
	{
		var device = await _deviceRepository.GetById(deviceId);
		if (device?.StartupProfileId is not { } profileId)
		{
			return null;
		}

		if (_profileRegistry.GetProfiles().Any(p => p.Id == profileId))
		{
			return profileId;
		}

		if (_profileRegistry.IsVirtual(profileId))
		{
			return null;
		}

		// AuthController is not readiness-gated and Kestrel accepts connections before the profile
		// cache is populated, so an early login must not read "not resolvable yet" as "the persisted
		// profile was deleted". Only clear once startup is known-complete (a stricter gate than the
		// profile cache alone, deliberately: erring late merely defers the cleanup, erring early
		// would wipe every device's assignment on a login that races startup).
		if (!_readiness.IsReady)
		{
			return null;
		}

		device.StartupProfileId = null;
		await _deviceRepository.Update(device);

		await _mediator.Publish(new DeviceChangedNotification(deviceId));

		return null;
	}

	public async Task<Result<DeviceError>> OpenProfileOnDevice(Guid id, string profileId)
	{
		var device = await _deviceRepository.GetById(id);
		if (device is null)
		{
			return Result.Fail<DeviceError>(DeviceError.NotFound, "Device not found.");
		}

		if (_connectionTracker.OnlineDeviceConnectionCounts().GetValueOrDefault(id) <= 0 &&
			!_providerPresence.IsOnline(id))
		{
			return Result.Fail<DeviceError>(DeviceError.Offline, "The device is offline.");
		}

		var opened = await _deckNavigator.ChangeProfileOnDeviceAsync(id, profileId, CancellationToken.None);
		if (!opened)
		{
			return Result.Fail<DeviceError>(DeviceError.NotFound, "That profile no longer exists.");
		}

		return Result.Ok<DeviceError>();
	}

	public async Task ClearStartupProfileAssignments(string profileId)
	{
		var devices = await _deviceRepository.GetByStartupProfileId(profileId);
		foreach (var device in devices)
		{
			device.StartupProfileId = null;
			await _deviceRepository.Update(device);

			await _mediator.Publish(new DeviceChangedNotification(device.Id));
		}
	}

	public async Task<Result<DeviceError>> LogoutDevice(Guid id)
	{
		var device = await _deviceRepository.GetById(id);
		if (device is null)
		{
			return Result.Fail<DeviceError>(DeviceError.NotFound);
		}

		if (device.IsProviderDevice)
		{
			return Result.Fail<DeviceError>(DeviceError.ValidationError,
				"A provider-registered device has no session to sign out.");
		}

		var now = UtcNow();
		await _refreshTokenRepository.RevokeAllForDevice(id, now);

		await _uiTransport.SendToGroup(UiDeviceGroups.For(id), new DeviceSessionRevokedEvent());

		_connectionTracker.RevokeUntil(id, now.Add(AuthDefaults.AccessTokenLifetime).AddSeconds(30));

		_connectionTracker.AbortDevice(id);

		await _mediator.Publish(new DeviceChangedNotification(id));

		return Result.Ok<DeviceError>();
	}

	public async Task<Result<DeviceError>> RemoveDevice(Guid id)
	{
		var device = await _deviceRepository.GetById(id);
		if (device is null)
		{
			return Result.Fail<DeviceError>(DeviceError.NotFound);
		}

		var now = UtcNow();
		await _refreshTokenRepository.RevokeAllForDevice(id, now);
		await _uiTransport.SendToGroup(UiDeviceGroups.For(id), new DeviceSessionRevokedEvent());
		_connectionTracker.RevokeUntil(id, now.Add(AuthDefaults.AccessTokenLifetime).AddSeconds(30));
		_connectionTracker.AbortDevice(id);

		_providerPresence.Forget(id);

		await _deviceRepository.Delete(id);
		await _mediator.Publish(new DeviceRemovedNotification(id));

		return Result.Ok<DeviceError>();
	}

	internal static string? Sanitize(string? name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return null;
		}

		var builder = new StringBuilder(name.Length);
		var pendingSpace = false;

		foreach (var character in name)
		{
			if (char.IsControl(character) || char.IsWhiteSpace(character))
			{
				pendingSpace = builder.Length > 0;
				continue;
			}

			if (pendingSpace)
			{
				builder.Append(' ');
				pendingSpace = false;
			}

			builder.Append(character);

			if (builder.Length == DeviceDefaults.MaxNameLength)
			{
				break;
			}
		}

		if (builder.Length > 0 && char.IsHighSurrogate(builder[^1]))
		{
			builder.Length--;
		}

		return builder.Length == 0 ? null : builder.ToString();
	}

	private async Task<DeviceEntity?> Resolve(DeviceRegistration registration)
	{
		if (registration.DeviceId is not { } id || string.IsNullOrEmpty(registration.DeviceSecret))
		{
			return null;
		}

		var device = await _deviceRepository.GetById(id);
		// A provider device stores no secret; no presented credential may ever resolve to one.
		if (device is null || string.IsNullOrEmpty(device.SecretHash))
		{
			return null;
		}

		if (!TokenHasher.Verify(registration.DeviceSecret, device.SecretHash))
		{
			return null;
		}

		return device;
	}

	private async Task<DeviceRegistrationResult> Reuse(DeviceEntity existing,
		DeviceRegistration registration,
		DateTime now)
	{
		existing.LastSeenAt = now;
		existing.ClientType = registration.ClientType;
		existing.FormFactor = registration.FormFactor;
		existing.Platform = registration.Platform;
		existing.Browser = registration.Browser;
		existing.AppVersion = registration.AppVersion;

		var proposed = Sanitize(registration.ProposedName);
		existing.ProposedName = proposed;

		if (!existing.NameIsCustom && proposed is not null)
		{
			existing.Name = proposed;
		}

		await _deviceRepository.Update(existing);

		return new DeviceRegistrationResult(existing, null);
	}

	private async Task<DeviceRegistrationResult> Mint(DeviceRegistration registration, DateTime now)
	{
		var secret = TokenHasher.Generate();
		var proposed = Sanitize(registration.ProposedName);
		var device = new DeviceEntity
		{
			Id = Guid.NewGuid(),
			SecretHash = TokenHasher.Hash(secret),
			Name = proposed ?? DeviceDefaults.FallbackName,
			NameIsCustom = false,
			ProposedName = proposed,
			ClientType = registration.ClientType,
			FormFactor = registration.FormFactor,
			Platform = registration.Platform,
			Browser = registration.Browser,
			AppVersion = registration.AppVersion,
			LastSeenAt = now,
			CreatedAt = now
		};

		await _deviceRepository.Create(device);

		return new DeviceRegistrationResult(device, secret);
	}

	private static Device BuildDto(
		DeviceEntity entity,
		int connectionCount,
		bool hasActiveSession,
		DateTime now,
		Dictionary<string, string> profileNames,
		Dictionary<string, LocalizedText> providerNames,
		bool providerOnline)
	{
		// A provider device holds no session and opens no connection of its own: its provider is the
		// only thing that knows whether it is reachable.
		var online = entity.IsProviderDevice ? providerOnline : connectionCount > 0;

		var startupProfileName =
			entity.StartupProfileId is { } profileId && profileNames.TryGetValue(profileId, out var name)
				? name
				: null;

		return new Device
		{
			Id = entity.Id.ToString(),
			Name = entity.Name,
			NameIsCustom = entity.NameIsCustom,
			ProposedName = entity.ProposedName,
			ClientType = ClientTypeToWire(entity.ClientType),
			FormFactor = FormFactorToWire(entity.FormFactor),
			Platform = entity.Platform,
			Browser = entity.Browser,
			AppVersion = entity.AppVersion,
			Online = online,
			ConnectionCount = entity.IsProviderDevice ? 0 : connectionCount,
			HasActiveSession = !entity.IsProviderDevice && hasActiveSession,
			StartupProfileId = entity.StartupProfileId,
			StartupProfileName = startupProfileName,
			LastSeenAt = DateTime.SpecifyKind(online ? now : entity.LastSeenAt, DateTimeKind.Utc),
			CreatedAt = DateTime.SpecifyKind(entity.CreatedAt, DateTimeKind.Utc),
			ProviderId = entity.ProviderId,
			ProviderName = entity.ProviderId is { } providerId
				? providerNames.GetValueOrDefault(providerId)
				: default,
			ProviderDeviceId = entity.ProviderDeviceId,
			Model = entity.Model,
			Manufacturer = entity.Manufacturer,
			LayoutReference = entity.LayoutReference
		};
	}

	private Dictionary<string, string> ProfileNameMap()
		=> _profileRegistry.GetProfiles().ToDictionary(p => p.Id, p => p.Name);

	// The name travels as a LocalizedText, resolved by whoever displays it: a plugin's name can itself
	// be localized, and it must follow the reader's language rather than the language the device
	// happened to be registered in.
	private Dictionary<string, LocalizedText> ProviderNameMap(IReadOnlyList<DeviceEntity> devices)
	{
		var providerIds = devices
			.Select(device => device.ProviderId)
			.OfType<string>()
			.Distinct(StringComparer.Ordinal)
			.ToHashSet(StringComparer.Ordinal);

		var names = new Dictionary<string, LocalizedText>(StringComparer.Ordinal);
		if (providerIds.Count == 0)
		{
			return names;
		}

		foreach (var integration in _integrationRegistry.Integrations)
		{
			if (providerIds.Contains(integration.Id))
			{
				names[integration.Id] = integration.Name;
			}
		}

		return names;
	}

	private static string ClientTypeToWire(DeviceClientType type) => type switch
	{
		DeviceClientType.WebClient => "web-client",
		DeviceClientType.AdminUi => "admin-ui",
		DeviceClientType.Native => "native",
		DeviceClientType.Provider => "provider",
		_ => "unknown"
	};

	private static string FormFactorToWire(DeviceFormFactor factor) => factor switch
	{
		DeviceFormFactor.Desktop => "desktop",
		DeviceFormFactor.Tablet => "tablet",
		DeviceFormFactor.Phone => "phone",
		_ => "unknown"
	};

	private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
