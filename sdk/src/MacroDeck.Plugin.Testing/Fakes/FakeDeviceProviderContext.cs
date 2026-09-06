using System.Collections.Concurrent;
using MacroDeck.Sdk.Devices;

namespace MacroDeck.Plugin.Testing.Fakes;

/// <summary>Which <see cref="IDeviceProviderContext" /> method a <see cref="DeviceProviderCall" /> recorded.</summary>
public enum DeviceProviderCallKind
{
	/// <summary>Recorded by <see cref="IDeviceProviderContext.RegisterDeviceAsync" />.</summary>
	Register,

	/// <summary>Recorded by <see cref="IDeviceProviderContext.UpdateDeviceAsync" />.</summary>
	Update,

	/// <summary>Recorded by <see cref="IDeviceProviderContext.SetDevicePresenceAsync" />.</summary>
	Presence,

	/// <summary>Recorded by <see cref="IDeviceProviderContext.UnregisterDeviceAsync" />.</summary>
	Unregister,

	/// <summary>Recorded when a provider reports an interaction through a
	/// <see cref="FakeDeviceSession" /> this context opened.</summary>
	Interaction
}

/// <summary>One call recorded by <see cref="FakeDeviceProviderContext" />.</summary>
public sealed record DeviceProviderCall
{
	/// <summary>Which method was called.</summary>
	public required DeviceProviderCallKind Kind { get; init; }

	/// <summary>The provider-local device id the call addressed.</summary>
	public required string DeviceId { get; init; }

	/// <summary>The descriptor, for register and update calls.</summary>
	public DeviceDescriptor? Device { get; init; }

	/// <summary>The presence the call reported, for register, update and presence calls.</summary>
	public DevicePresence? Presence { get; init; }

	/// <summary>What the provider reported, for an interaction call.</summary>
	public DeviceInteraction? Interaction { get; init; }
}

/// <summary>
/// In-memory <see cref="IDeviceProviderContext" />, standing in for the host's device registry. It keeps
/// the same identity rules the host has - a re-registration under a known provider-local id is the same
/// device, and unregistering retains it and only takes it offline - so a provider tested against it sees
/// the reconnect behaviour it would see for real.
/// </summary>
public sealed class FakeDeviceProviderContext : IDeviceProviderContext
{
	private readonly ConcurrentDictionary<string, string> _deviceIds = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, DeviceDescriptor> _devices = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, bool> _online = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, FakeDeviceSession> _sessions = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, FakeDeviceSession> _sessionsById = new(StringComparer.Ordinal);
	private readonly List<DeviceProviderCall> _calls = [];
	private readonly List<(string DeviceId, DeviceInteraction Interaction)> _interactions = [];
	private readonly Lock _sync = new();

	/// <summary>Every call made against this context, in order.</summary>
	public IReadOnlyList<DeviceProviderCall> Calls
	{
		get
		{
			lock (_sync)
			{
				return [.. _calls];
			}
		}
	}

	/// <summary>The devices currently registered, keyed by their provider-local id.</summary>
	public IReadOnlyDictionary<string, DeviceDescriptor> Devices => _devices;

	/// <summary>Whether a registered device is currently reported as online.</summary>
	public bool IsOnline(string deviceId) => _online.TryGetValue(deviceId, out var online) && online;

	/// <summary>The host-assigned id a device was registered under, or null when it never was.</summary>
	public string? AssignedIdOf(string deviceId) => _deviceIds.GetValueOrDefault(deviceId);

	/// <summary>The sessions opened through <see cref="OpenSession" />, keyed by provider-local device id.</summary>
	public IReadOnlyDictionary<string, FakeDeviceSession> Sessions => _sessions;

	/// <summary>Every interaction reported through a session this context opened, in order.</summary>
	public IReadOnlyList<(string DeviceId, DeviceInteraction Interaction)> Interactions
	{
		get
		{
			lock (_sync)
			{
				return [.. _interactions];
			}
		}
	}

	/// <summary>
	/// Opens a session for an already-registered device and hands it to <paramref name="provider" />, so
	/// a provider's <see cref="IDeviceProvider.OnSessionOpenedAsync" /> can be exercised with no host and
	/// no transport at all. Registering the device first is required: the host only ever opens a session
	/// for a device it knows.
	/// </summary>
	public async Task<FakeDeviceSession> OpenSession(
		IDeviceProvider provider,
		string deviceId,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(provider);

		if (!_deviceIds.TryGetValue(deviceId, out var assigned))
		{
			throw new InvalidOperationException(
				$"No device '{deviceId}' is registered, so the host would never open a session for it.");
		}

		var session = new FakeDeviceSession(assigned, deviceId, this);
		_sessions[deviceId] = session;

		await provider.OnSessionOpenedAsync(session, cancellationToken).ConfigureAwait(false);
		return session;
	}

	/// <summary>
	/// Registers the session id the host handed the plugin over <c>session.open</c>, so the test host can
	/// route the plugin's own <c>devices</c> calls for it. Returns the same
	/// <see cref="FakeDeviceSession" /> an in-process provider is given, which is what lets one set of
	/// assertions cover both hosting models.
	/// </summary>
	public FakeDeviceSession TrackSession(string sessionId, string deviceId)
	{
		var session = _sessions.GetOrAdd(deviceId,
			static (id, state) => new FakeDeviceSession(state.AssignedId, id, state.Context),
			(AssignedId: _deviceIds.GetValueOrDefault(deviceId) ?? deviceId, Context: this));

		_sessionsById[sessionId] = session;
		return session;
	}

	/// <summary>The session a plugin's <c>devices</c> call addresses, or null for an id nobody tracked.</summary>
	internal FakeDeviceSession? ResolveSession(string? sessionId)
		=> sessionId is not null ? _sessionsById.GetValueOrDefault(sessionId) : null;

	internal void RecordInteraction(string deviceId, DeviceInteraction interaction)
	{
		lock (_sync)
		{
			_interactions.Add((deviceId, interaction));
		}

		Record(DeviceProviderCallKind.Interaction, deviceId, device: null, presence: null, interaction);
	}

	public Task<DeviceRegistration> RegisterDeviceAsync(
		DeviceDescriptor device,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(device);
		ArgumentException.ThrowIfNullOrWhiteSpace(device.Id);
		ArgumentException.ThrowIfNullOrWhiteSpace(device.Name);

		// Reusing the id a previous registration was given is what makes this a reconnect rather than a
		// new device - the host resolves the very same way, by (provider, provider-local id).
		var assigned = _deviceIds.GetOrAdd(device.Id, static _ => Guid.NewGuid().ToString());
		_devices[device.Id] = device;
		_online[device.Id] = device.Presence == DevicePresence.Online;

		Record(DeviceProviderCallKind.Register, device.Id, device, device.Presence);

		return Task.FromResult(new DeviceRegistration(assigned, device.Id));
	}

	public Task UpdateDeviceAsync(DeviceDescriptor device, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(device);

		if (_devices.ContainsKey(device.Id))
		{
			_devices[device.Id] = device;
			_online[device.Id] = device.Presence == DevicePresence.Online;
		}

		Record(DeviceProviderCallKind.Update, device.Id, device, device.Presence);

		return Task.CompletedTask;
	}

	public Task SetDevicePresenceAsync(
		string deviceId,
		DevicePresence presence,
		CancellationToken cancellationToken = default)
	{
		if (_devices.ContainsKey(deviceId))
		{
			_online[deviceId] = presence == DevicePresence.Online;
		}

		Record(DeviceProviderCallKind.Presence, deviceId, device: null, presence);

		return Task.CompletedTask;
	}

	public Task UnregisterDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
	{
		_online[deviceId] = false;

		Record(DeviceProviderCallKind.Unregister, deviceId, device: null, presence: null);

		return Task.CompletedTask;
	}

	private void Record(DeviceProviderCallKind kind,
		string deviceId,
		DeviceDescriptor? device,
		DevicePresence? presence,
		DeviceInteraction? interaction = null)
	{
		lock (_sync)
		{
			_calls.Add(new DeviceProviderCall
			{
				Kind = kind,
				DeviceId = deviceId,
				Device = device,
				Presence = presence,
				Interaction = interaction
			});
		}
	}
}
