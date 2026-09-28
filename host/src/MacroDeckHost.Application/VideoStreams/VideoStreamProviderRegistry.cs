using System.Collections.Concurrent;
using MacroDeck.Localization;
using MacroDeck.Plugin.Hosting.Capabilities.VideoStreamProvider;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Sdk.Identity;
using MacroDeck.Sdk.VideoStreams;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Capabilities;
using Mediator;
using Serilog;

namespace MacroDeckHost.Application.VideoStreams;

public sealed record VideoStreamProviderEntry(
	string QualifiedId,
	string OwnerId,
	string ProviderId,
	string RegistrationId,
	LocalizedText Name,
	LocalizedText? Description,
	IReadOnlyList<VideoStreamDescriptor> Streams);

internal sealed record VideoStreamRegistrationChange(string OwnerId, string ProviderId, string? RegistrationId);

public sealed class VideoStreamProviderRegistry : IDisposable
{
	private readonly Lock _gate = new();
	private readonly IIntegrationRegistry _integrations;
	private readonly TimeSpan _inProcessTimeout;
	private readonly IPluginCapabilityInvoker _invoker;
	private readonly ILogger _logger;
	private readonly Dictionary<string, Owner> _owners = new(StringComparer.Ordinal);
	private readonly IPluginSessionRegistry _pluginSessions;
	private readonly IPublisher _publisher;
	private readonly ConcurrentDictionary<string, PullRun> _pulls = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, RemoteVideoStreamEndpoint> _remoteEndpoints =
		new(StringComparer.Ordinal);

	private readonly TimeProvider _time;

	public VideoStreamProviderRegistry(IIntegrationRegistry integrations,
		IPluginSessionRegistry pluginSessions,
		IPluginCapabilityInvoker invoker,
		IPublisher publisher,
		TimeProvider time,
		ILogger logger,
		TimeSpan? inProcessTimeout = null)
	{
		_integrations = integrations;
		_pluginSessions = pluginSessions;
		_invoker = invoker;
		_publisher = publisher;
		_time = time;
		_logger = logger.ForContext<VideoStreamProviderRegistry>();
		_inProcessTimeout = inProcessTimeout ?? TimeSpan.FromSeconds(10);
	}

	internal event Action<VideoStreamRegistrationChange>? RegistrationChanged;

	public void Dispose()
	{
		foreach (var endpoint in _remoteEndpoints.Values)
		{
			endpoint.Dispose();
		}
	}

	public IReadOnlyList<VideoStreamProviderEntry> GetProviders()
	{
		List<VideoStreamProviderEntry> entries;
		lock (_gate)
		{
			entries = [.. _owners.Values.SelectMany(owner => owner.Providers.Values)];
		}

		return
		[
			.. entries.Where(entry => _integrations.IsEnabled(entry.OwnerId))
				.OrderBy(entry => entry.QualifiedId, StringComparer.Ordinal)
		];
	}

	public bool TryResolve(string? qualifiedProviderId, out VideoStreamProviderEntry entry)
	{
		entry = null!;
		if (!TrySplit(qualifiedProviderId, out var ownerId, out var providerId) || !_integrations.IsEnabled(ownerId))
		{
			return false;
		}

		lock (_gate)
		{
			if (_owners.TryGetValue(ownerId, out var owner) && owner.Providers.TryGetValue(providerId, out var found))
			{
				entry = found;
				return true;
			}
		}

		return false;
	}

	public bool HasOwner(string ownerId)
	{
		lock (_gate)
		{
			return _owners.ContainsKey(ownerId);
		}
	}

	public Task AttachRemoteAsync(string pluginId, string? pluginSessionId, IPluginConnection? connection)
	{
		ArgumentException.ThrowIfNullOrEmpty(pluginId);
		if (connection is not null &&
			pluginSessionId is not null &&
			!_pluginSessions.IsCurrentConnection(pluginSessionId, connection))
		{
			return Task.CompletedTask;
		}

		lock (_gate)
		{
			if (!_owners.TryGetValue(pluginId, out var owner))
			{
				owner = new Owner(pluginId,
					_remoteEndpoints.GetOrAdd(pluginId, id => new RemoteVideoStreamEndpoint(id, _invoker)),
					isRemote: true);
				_owners[pluginId] = owner;
			}

			if (!owner.IsRemote)
			{
				return Task.CompletedTask;
			}

			if (!string.Equals(owner.PluginSessionId, pluginSessionId, StringComparison.Ordinal) ||
				!ReferenceEquals(owner.Connection, connection))
			{
				owner.PluginSessionId = pluginSessionId;
				owner.Connection = connection;
				owner.Generation++;
			}
		}

		return PullProvidersAsync(pluginId);
	}

	public Task RequestProvidersPullAsync(string pluginId, string? pluginSessionId, IPluginConnection? connection)
		=> AttachRemoteAsync(pluginId, pluginSessionId, connection);

	public Task RequestStreamsPullAsync(string ownerId, string providerId)
	{
		lock (_gate)
		{
			if (!_owners.TryGetValue(ownerId, out var owner) || !owner.Providers.ContainsKey(providerId))
			{
				return Task.CompletedTask;
			}
		}

		return PullStreamsAsync(ownerId, providerId);
	}

	public void RemovePluginSession(string pluginId, string pluginSessionId)
	{
		List<VideoStreamProviderEntry> removed;
		lock (_gate)
		{
			if (!_owners.TryGetValue(pluginId, out var owner) ||
				!owner.IsRemote ||
				(owner.PluginSessionId is not null &&
					!string.Equals(owner.PluginSessionId, pluginSessionId, StringComparison.Ordinal)))
			{
				return;
			}

			removed = [.. owner.Providers.Values];
			_owners.Remove(pluginId);
		}

		AnnounceRemoved(removed);
	}

	public void RemoveOwner(string ownerId)
	{
		List<VideoStreamProviderEntry> removed;
		lock (_gate)
		{
			if (!_owners.Remove(ownerId, out var owner))
			{
				return;
			}

			removed = [.. owner.Providers.Values];
		}

		AnnounceRemoved(removed);
	}

	public Task PublishCatalogChangedAsync() => PublishAsync();

	internal bool TryGetEndpoint(string ownerId, out IVideoStreamEndpoint endpoint, out string? pluginSessionId)
	{
		lock (_gate)
		{
			if (_owners.TryGetValue(ownerId, out var owner))
			{
				endpoint = owner.Endpoint;
				pluginSessionId = owner.PluginSessionId;
				return true;
			}
		}

		endpoint = null!;
		pluginSessionId = null;
		return false;
	}

	internal string? CurrentRegistrationId(string ownerId, string providerId)
	{
		lock (_gate)
		{
			return _owners.TryGetValue(ownerId, out var owner) && owner.Providers.TryGetValue(providerId, out var entry)
				? entry.RegistrationId
				: null;
		}
	}

	internal InProcessVideoStreamEndpoint? InProcessEndpoint(string integrationId)
	{
		lock (_gate)
		{
			return _owners.TryGetValue(integrationId, out var owner) ? owner.Endpoint as InProcessVideoStreamEndpoint : null;
		}
	}

	internal async Task<VideoStreamProviderRegistration> RegisterInProcessAsync(string integrationId,
		IVideoStreamProvider provider)
	{
		ArgumentException.ThrowIfNullOrEmpty(integrationId);
		ArgumentNullException.ThrowIfNull(provider);

		InProcessVideoStreamEndpoint endpoint;
		lock (_gate)
		{
			if (!_owners.TryGetValue(integrationId, out var owner))
			{
				owner = new Owner(integrationId,
					new InProcessVideoStreamEndpoint(_time, _inProcessTimeout, _logger),
					isRemote: false);
				_owners[integrationId] = owner;
			}

			endpoint = owner.Endpoint as InProcessVideoStreamEndpoint ??
				throw new InvalidOperationException($"'{integrationId}' provides its video streams as a plugin.");
		}

		endpoint.Add(provider);
		await PullProvidersAsync(integrationId).ConfigureAwait(false);
		return new VideoStreamProviderRegistration(integrationId + QualifiedId.Separator + provider.Id, provider.Id);
	}

	internal async Task UnregisterInProcessAsync(string integrationId, string providerId)
	{
		if (InProcessEndpoint(integrationId) is { } endpoint && await endpoint.RemoveAsync(providerId).ConfigureAwait(false))
		{
			await PullProvidersAsync(integrationId).ConfigureAwait(false);
		}
	}

	public async Task WithdrawInProcessAsync(string integrationId)
	{
		if (InProcessEndpoint(integrationId) is not { } endpoint)
		{
			return;
		}

		await endpoint.RemoveAllAsync().ConfigureAwait(false);

		List<VideoStreamProviderEntry> removed;
		lock (_gate)
		{
			if (!_owners.TryGetValue(integrationId, out var owner) || !ReferenceEquals(owner.Endpoint, endpoint))
			{
				return;
			}

			_owners.Remove(integrationId);
			removed = [.. owner.Providers.Values];
		}

		AnnounceRemoved(removed);
	}

	private void AnnounceRemoved(List<VideoStreamProviderEntry> removed)
	{
		foreach (var entry in removed)
		{
			RegistrationChanged?.Invoke(new VideoStreamRegistrationChange(entry.OwnerId, entry.ProviderId, null));
		}

		if (removed.Count > 0)
		{
			_ = PublishAsync();
		}
	}

	private Task PullProvidersAsync(string ownerId) => CoalesceAsync("providers\n" + ownerId, () => DescribeAsync(ownerId));

	private Task PullStreamsAsync(string ownerId, string providerId)
		=> CoalesceAsync("streams\n" + ownerId + "\n" + providerId, () => ReadStreamsAsync(ownerId, providerId));

	private async Task DescribeAsync(string ownerId)
	{
		if (!TryCapture(ownerId, out var owner, out var generation, out var pluginSessionId, out var connection))
		{
			return;
		}

		IReadOnlyList<VideoStreamProviderInfo> described;
		try
		{
			described = await owner.Endpoint.DescribeAsync(CancellationToken.None).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Warning(exception, "Could not read the video stream providers of {OwnerId}", ownerId);
			return;
		}

		if (!IsCurrent(pluginSessionId, connection))
		{
			return;
		}

		var accepted = Accept(ownerId, described);
		var changes = new List<VideoStreamRegistrationChange>();
		var fresh = new List<string>();
		var changed = false;
		lock (_gate)
		{
			if (!_owners.TryGetValue(ownerId, out var current) ||
				!ReferenceEquals(current, owner) ||
				owner.Generation != generation)
			{
				return;
			}

			foreach (var providerId in owner.Providers.Keys.Where(id => !accepted.ContainsKey(id)).ToList())
			{
				owner.Providers.Remove(providerId);
				changes.Add(new VideoStreamRegistrationChange(ownerId, providerId, null));
				changed = true;
			}

			foreach (var (providerId, info) in accepted)
			{
				owner.Providers.TryGetValue(providerId, out var existing);
				if (existing is not null &&
					string.Equals(existing.RegistrationId, info.RegistrationId, StringComparison.Ordinal))
				{
					if (!Equals(existing.Name, info.Name) || !Equals(existing.Description, info.Description))
					{
						owner.Providers[providerId] = existing with { Name = info.Name, Description = info.Description };
						changed = true;
					}

					continue;
				}

				owner.Providers[providerId] = new VideoStreamProviderEntry(ownerId + QualifiedId.Separator + providerId,
					ownerId,
					providerId,
					info.RegistrationId,
					info.Name,
					info.Description,
					[]);
				if (existing is not null)
				{
					changes.Add(new VideoStreamRegistrationChange(ownerId, providerId, info.RegistrationId));
				}

				fresh.Add(providerId);
				changed = true;
			}
		}

		foreach (var change in changes)
		{
			RegistrationChanged?.Invoke(change);
		}

		if (changed)
		{
			await PublishAsync().ConfigureAwait(false);
		}

		await Task.WhenAll(fresh.Select(providerId => PullStreamsAsync(ownerId, providerId))).ConfigureAwait(false);
	}

	private async Task ReadStreamsAsync(string ownerId, string providerId)
	{
		if (!TryCapture(ownerId, out var owner, out var generation, out var pluginSessionId, out var connection))
		{
			return;
		}

		var registrationId = CurrentRegistrationId(ownerId, providerId);
		if (registrationId is null)
		{
			return;
		}

		IReadOnlyList<VideoStreamDescriptor> streams;
		try
		{
			streams = await owner.Endpoint.GetStreamsAsync(providerId, CancellationToken.None).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Warning(exception, "Could not read the video streams of {OwnerId}::{ProviderId}", ownerId, providerId);
			return;
		}

		if (!IsCurrent(pluginSessionId, connection))
		{
			return;
		}

		var accepted = AcceptStreams(ownerId, providerId, streams);
		lock (_gate)
		{
			if (!_owners.TryGetValue(ownerId, out var current) ||
				!ReferenceEquals(current, owner) ||
				owner.Generation != generation ||
				!owner.Providers.TryGetValue(providerId, out var entry) ||
				!string.Equals(entry.RegistrationId, registrationId, StringComparison.Ordinal))
			{
				return;
			}

			owner.Providers[providerId] = entry with { Streams = accepted };
		}

		await PublishAsync().ConfigureAwait(false);
	}

	private Dictionary<string, VideoStreamProviderInfo> Accept(string ownerId,
		IReadOnlyList<VideoStreamProviderInfo> described)
	{
		var accepted = new Dictionary<string, VideoStreamProviderInfo>(StringComparer.Ordinal);
		foreach (var info in described)
		{
			if (!MacroDeckId.IsValidLocalId(info.ProviderId, LocalIdKind.Resource) ||
				info.Name.IsEmpty ||
				string.IsNullOrEmpty(info.RegistrationId) ||
				accepted.ContainsKey(info.ProviderId))
			{
				_logger.Warning("{OwnerId} described a video stream provider that was left out: {ProviderId}",
					ownerId,
					info.ProviderId);
				continue;
			}

			if (accepted.Count == VideoStreamLimits.MaxProvidersPerPlugin)
			{
				_logger.Warning("{OwnerId} describes more than {MaxProviders} video stream providers; the rest are left out",
					ownerId,
					VideoStreamLimits.MaxProvidersPerPlugin);
				break;
			}

			accepted[info.ProviderId] = info;
		}

		return accepted;
	}

	private List<VideoStreamDescriptor> AcceptStreams(string ownerId,
		string providerId,
		IReadOnlyList<VideoStreamDescriptor> streams)
	{
		var accepted = new List<VideoStreamDescriptor>();
		var ids = new HashSet<string>(StringComparer.Ordinal);
		foreach (var stream in streams)
		{
			if (VideoStreamWire.ValidateDescriptor(stream) is not null || !ids.Add(stream.Id))
			{
				_logger.Warning("Video stream provider {OwnerId}::{ProviderId} listed a stream that was left out",
					ownerId,
					providerId);
				continue;
			}

			if (accepted.Count == VideoStreamLimits.MaxStreamsPerProvider)
			{
				_logger.Warning(
					"Video stream provider {OwnerId}::{ProviderId} lists more than {MaxStreams} streams; the rest are left out",
					ownerId,
					providerId,
					VideoStreamLimits.MaxStreamsPerProvider);
				break;
			}

			accepted.Add(stream);
		}

		return accepted;
	}

	private bool TryCapture(string ownerId,
		out Owner owner,
		out long generation,
		out string? pluginSessionId,
		out IPluginConnection? connection)
	{
		lock (_gate)
		{
			if (_owners.TryGetValue(ownerId, out owner!))
			{
				generation = owner.Generation;
				pluginSessionId = owner.PluginSessionId;
				connection = owner.Connection;
				return true;
			}
		}

		generation = 0;
		pluginSessionId = null;
		connection = null;
		return false;
	}

	private bool IsCurrent(string? pluginSessionId, IPluginConnection? connection)
		=> connection is null || pluginSessionId is null || _pluginSessions.IsCurrentConnection(pluginSessionId, connection);

	private async Task PublishAsync()
	{
		try
		{
			await _publisher.Publish(new VideoStreamCatalogChangedNotification()).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Warning(exception, "Announcing a video stream catalog change failed");
		}
	}

	private async Task CoalesceAsync(string key, Func<Task> run)
	{
		var state = _pulls.GetOrAdd(key, static _ => new PullRun());
		Task? wait = null;
		lock (state.Gate)
		{
			if (state.Running)
			{
				state.Next ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
				wait = state.Next.Task;
			}
			else
			{
				state.Running = true;
			}
		}

		if (wait is not null)
		{
			await wait.ConfigureAwait(false);
			return;
		}

		TaskCompletionSource? completing = null;
		while (true)
		{
			try
			{
				await run().ConfigureAwait(false);
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				_logger.Warning(exception, "Refreshing the video stream catalog failed");
			}

			completing?.TrySetResult();
			lock (state.Gate)
			{
				if (state.Next is null)
				{
					state.Running = false;
					return;
				}

				completing = state.Next;
				state.Next = null;
			}
		}
	}

	private static bool TrySplit(string? qualifiedId, out string ownerId, out string providerId)
	{
		ownerId = string.Empty;
		providerId = string.Empty;
		var separator = qualifiedId?.IndexOf(QualifiedId.Separator, StringComparison.Ordinal) ?? -1;
		if (separator <= 0 || separator + QualifiedId.Separator.Length >= qualifiedId!.Length)
		{
			return false;
		}

		ownerId = qualifiedId[..separator];
		providerId = qualifiedId[(separator + QualifiedId.Separator.Length)..];
		return true;
	}

	private sealed class Owner(string id, IVideoStreamEndpoint endpoint, bool isRemote)
	{
		public string Id { get; } = id;

		public IVideoStreamEndpoint Endpoint { get; } = endpoint;

		public bool IsRemote { get; } = isRemote;

		public string? PluginSessionId { get; set; }

		public IPluginConnection? Connection { get; set; }

		public long Generation { get; set; }

		public Dictionary<string, VideoStreamProviderEntry> Providers { get; } = new(StringComparer.Ordinal);
	}

	private sealed class PullRun
	{
		public readonly Lock Gate = new();
		public TaskCompletionSource? Next;
		public bool Running;
	}
}
