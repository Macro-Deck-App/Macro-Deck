using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Sdk.Devices;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
// Aliased rather than imported: MacroDeckHost.Application.Auth also declares DeviceRegistration, which
// would collide with MacroDeck.Sdk.Devices.DeviceRegistration in this file.
using DeviceSessionGuard = MacroDeckHost.Application.Auth.DeviceSessionGuard;

namespace MacroDeckHost.Application.Devices;

public sealed class PluginDeviceRegistry : IPluginDeviceRegistry
{
	private static readonly JsonSerializerOptions _capabilitiesJson =
		new(JsonSerializerDefaults.Web) { WriteIndented = false };

	private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ProviderDevicePresenceTracker _presence;
	private readonly DeviceConnectionTracker _connectionTracker;
	private readonly DeviceSessionGuard _sessionGuard;
	private readonly ILayoutRegistry _layoutRegistry;
	private readonly TimeProvider _timeProvider;
	private readonly IIntegrationRegistry? _integrations;

	public PluginDeviceRegistry(
		IServiceScopeFactory scopeFactory,
		ProviderDevicePresenceTracker presence,
		DeviceConnectionTracker connectionTracker,
		DeviceSessionGuard sessionGuard,
		ILayoutRegistry layoutRegistry,
		TimeProvider timeProvider,
		IIntegrationRegistry? integrations = null)
	{
		_integrations = integrations;
		_scopeFactory = scopeFactory;
		_presence = presence;
		_connectionTracker = connectionTracker;
		_sessionGuard = sessionGuard;
		_layoutRegistry = layoutRegistry;
		_timeProvider = timeProvider;
	}

	public async Task<DeviceRegistration> RegisterAsync(
		string providerId,
		DeviceDescriptor device,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
		ArgumentNullException.ThrowIfNull(device);

		var localId = Require(device.Id, "The device id must not be empty.");
		var name = DeviceService.Sanitize(device.Name)
			?? throw new ArgumentException("The device name must not be empty.", nameof(device));

		await using var scope = _scopeFactory.CreateAsyncScope();
		var repository = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();

		var (registered, online, presenceChanged) = await Serialized(providerId,
			async () =>
			{
				var now = _timeProvider.GetUtcNow().UtcDateTime;
				var existing = await repository.GetByProviderIdentity(providerId, localId);

				if (existing is null)
				{
					existing = new DeviceEntity
					{
						Id = Guid.NewGuid(),
						// A provider device presents no credential of its own; see DeviceEntity.SecretHash.
						SecretHash = string.Empty,
						Name = name,
						NameIsCustom = false,
						ProposedName = name,
						ClientType = DeviceClientType.Provider,
						FormFactor = DeviceFormFactor.Unknown,
						LastSeenAt = now,
						CreatedAt = now,
						ProviderId = providerId,
						ProviderDeviceId = localId
					};

					Apply(existing, device, name, now);
					await repository.Create(existing);
					_sessionGuard.Track(existing.Id, existing.SessionsRevokedAt);
				}
				else
				{
					Apply(existing, device, name, now);
					await repository.Update(existing);
				}

				var online = device.Presence == DevicePresence.Online && !IsDisabled(providerId);
				var presenceChanged = _presence.Set(existing.Id, online);
				_connectionTracker.SetDeviceName(existing.Id, existing.Name);

				return (existing, online, presenceChanged);
			},
			cancellationToken);

		await Publish(scope, registered.Id, cancellationToken);
		await PublishPresence(scope, registered.Id, online, presenceChanged, cancellationToken);

		return new DeviceRegistration(registered.Id.ToString(), localId);
	}

	public async Task UpdateAsync(
		string providerId,
		DeviceDescriptor device,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
		ArgumentNullException.ThrowIfNull(device);

		var localId = Require(device.Id, "The device id must not be empty.");
		var name = DeviceService.Sanitize(device.Name)
			?? throw new ArgumentException("The device name must not be empty.", nameof(device));

		await using var scope = _scopeFactory.CreateAsyncScope();
		var repository = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();

		var (updated, online, presenceChanged) = await Serialized(providerId,
			async () =>
			{
				var existing = await repository.GetByProviderIdentity(providerId, localId);
				if (existing is null)
				{
					return (null, false, false);
				}

				Apply(existing, device, name, _timeProvider.GetUtcNow().UtcDateTime);
				await repository.Update(existing);

				var online = device.Presence == DevicePresence.Online && !IsDisabled(providerId);
				var presenceChanged = _presence.Set(existing.Id, online);
				_connectionTracker.SetDeviceName(existing.Id, existing.Name);

				return ((DeviceEntity?)existing, online, presenceChanged);
			},
			cancellationToken);

		if (updated is null)
		{
			return;
		}

		await Publish(scope, updated.Id, cancellationToken);
		await PublishPresence(scope, updated.Id, online, presenceChanged, cancellationToken);
	}

	public async Task SetPresenceAsync(
		string providerId,
		string providerDeviceId,
		DevicePresence presence,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(providerId);

		var localId = Require(providerDeviceId, "The device id must not be empty.");

		await using var scope = _scopeFactory.CreateAsyncScope();
		var repository = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();

		var (updated, online, presenceChanged) = await Serialized(providerId,
			async () =>
			{
				var existing = await repository.GetByProviderIdentity(providerId, localId);
				if (existing is null)
				{
					return (null, false, false);
				}

				var online = presence == DevicePresence.Online && !IsDisabled(providerId);
				if (online)
				{
					existing.LastSeenAt = _timeProvider.GetUtcNow().UtcDateTime;
					await repository.Update(existing);
				}

				return ((DeviceEntity?)existing, online, _presence.Set(existing.Id, online));
			},
			cancellationToken);

		if (updated is null)
		{
			return;
		}

		await Publish(scope, updated.Id, cancellationToken);
		await PublishPresence(scope, updated.Id, online, presenceChanged, cancellationToken);
	}

	public async Task UnregisterAsync(
		string providerId,
		string providerDeviceId,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(providerId);

		var localId = Require(providerDeviceId, "The device id must not be empty.");

		await using var scope = _scopeFactory.CreateAsyncScope();
		var repository = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();

		var (unregistered, wasOnline) = await Serialized(providerId,
			async () =>
			{
				var existing = await repository.GetByProviderIdentity(providerId, localId);
				return existing is null ? (null, false) : ((DeviceEntity?)existing, _presence.Forget(existing.Id));
			},
			cancellationToken);

		if (unregistered is null)
		{
			return;
		}

		await Publish(scope, unregistered.Id, cancellationToken);
		await PublishPresence(scope, unregistered.Id, false, wasOnline, cancellationToken);
		await PublishUnregistered(scope, unregistered.Id, cancellationToken);
	}

	public async Task UnregisterAllAsync(string providerId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(providerId);

		await using var scope = _scopeFactory.CreateAsyncScope();
		var repository = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();

		var forgotten = await Serialized(providerId,
			async () => (await repository.GetByProviderId(providerId))
				.Select(device => (device.Id, WasOnline: _presence.Forget(device.Id)))
				.ToList(),
			cancellationToken);

		foreach (var (deviceId, wasOnline) in forgotten)
		{
			await Publish(scope, deviceId, cancellationToken);
			await PublishPresence(scope, deviceId, false, wasOnline, cancellationToken);
			await PublishUnregistered(scope, deviceId, cancellationToken);
		}
	}

	private void Apply(DeviceEntity entity, DeviceDescriptor device, string proposedName, DateTime now)
	{
		entity.ProposedName = proposedName;
		if (!entity.NameIsCustom)
		{
			entity.Name = proposedName;
		}

		entity.Model = device.Model;
		entity.Manufacturer = device.Manufacturer;

		// A miss leaves the existing snapshot untouched - that is what keeps a fixed-grid device
		// constraining its profile while its provider is stopped (issue #384). Only a reference that
		// becomes null or changes to a different id clears it; an unchanged reference that still
		// misses (the provider is down) must not erase what was last resolved for it.
		if (!string.Equals(entity.LayoutReference, device.LayoutReference, StringComparison.Ordinal))
		{
			entity.LayoutSnapshot = device.LayoutReference is not null
				&& _layoutRegistry.TryResolve(device.LayoutReference, out var layout)
					? LayoutSnapshotSerializer.Serialize(layout)
					: null;
		}
		else if (device.LayoutReference is not null
			&& _layoutRegistry.TryResolve(device.LayoutReference, out var refreshedLayout))
		{
			entity.LayoutSnapshot = LayoutSnapshotSerializer.Serialize(refreshedLayout);
		}

		entity.LayoutReference = device.LayoutReference;
		entity.Capabilities = Serialize(device);
		entity.ClientType = DeviceClientType.Provider;
		entity.LastSeenAt = now;
	}

	// Notifications are published after the gate is released: their handlers open device sessions, and a
	// provider that registers or updates from that callback would otherwise wait on its own gate.
	private async Task<T> Serialized<T>(string providerId, Func<Task<T>> operation, CancellationToken cancellationToken)
	{
		var gate = _gates.GetOrAdd(providerId, static _ => new SemaphoreSlim(1, 1));
		await gate.WaitAsync(cancellationToken);
		try
		{
			return await operation();
		}
		finally
		{
			gate.Release();
		}
	}

	private bool IsDisabled(string providerId)
	{
		return _integrations?.IsExplicitlyDisabled(providerId) == true;
	}

	private static string? Serialize(DeviceDescriptor device)
	{
		var capabilities = device.Capabilities;
		var metadata = device.Metadata;

		if (capabilities is null && (metadata is null || metadata.Count == 0))
		{
			return null;
		}

		return JsonSerializer.Serialize(new StoredDeviceCapabilities(capabilities ?? DeviceCapabilities.None,
				metadata ?? new Dictionary<string, string>(StringComparer.Ordinal)),
			_capabilitiesJson);
	}

	private static Task Publish(AsyncServiceScope scope, Guid deviceId, CancellationToken cancellationToken)
	{
		return scope.ServiceProvider.GetRequiredService<IMediator>()
			.Publish(new DeviceChangedNotification(deviceId), cancellationToken)
			.AsTask();
	}

	// Presence for a provider device flips here and nowhere else: the background service derives it from
	// WebSocket connection counts, which a provider device never has, so without this a provider device
	// coming online would never reach anything that keys off DevicePresenceChangedNotification.
	private static Task PublishPresence(
		AsyncServiceScope scope,
		Guid deviceId,
		bool online,
		bool changed,
		CancellationToken cancellationToken)
	{
		return changed
			? scope.ServiceProvider.GetRequiredService<IMediator>()
				.Publish(new DevicePresenceChangedNotification(deviceId, online), cancellationToken)
				.AsTask()
			: Task.CompletedTask;
	}

	private static Task PublishUnregistered(
		AsyncServiceScope scope,
		Guid deviceId,
		CancellationToken cancellationToken)
	{
		return scope.ServiceProvider.GetRequiredService<IMediator>()
			.Publish(new DeviceUnregisteredNotification(deviceId), cancellationToken)
			.AsTask();
	}

	private static string Require(string? value, string message)
	{
		return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException(message) : value;
	}

	private sealed record StoredDeviceCapabilities(
		DeviceCapabilities Capabilities,
		IReadOnlyDictionary<string, string> Metadata);
}
