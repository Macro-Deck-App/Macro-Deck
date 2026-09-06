using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Plugins.Runtime;
using Mediator;

namespace MacroDeckHost.Tests.PluginContractTests.Harness;

internal sealed class InMemoryIntegrationStateStore : IIntegrationStateStore
{
	private readonly Dictionary<string, bool> _states = new(StringComparer.Ordinal);

	public IReadOnlyDictionary<string, bool> Load() => _states;

	public void Save(IReadOnlyDictionary<string, bool> states)
	{
		_states.Clear();

		foreach (var (key, value) in states)
		{
			_states[key] = value;
		}
	}
}

internal sealed class EmptyPluginSupervisor : IPluginSupervisor
{
	public IReadOnlyList<PluginRuntimeSnapshot> Snapshot() => [];

	public Task<PluginSupervisorResult> Start(string pluginId, CancellationToken cancellationToken = default)
		=> throw new NotSupportedException();

	public Task<PluginSupervisorResult> Stop(string pluginId,
		PluginStopReason reason,
		CancellationToken cancellationToken = default)
		=> throw new NotSupportedException();

	public Task<PluginSupervisorResult> Restart(string pluginId, CancellationToken cancellationToken = default)
		=> throw new NotSupportedException();

	public Task StopAll(PluginStopReason reason, CancellationToken cancellationToken = default)
		=> throw new NotSupportedException();

	public Task Forget(string pluginId, CancellationToken cancellationToken = default)
		=> throw new NotSupportedException();

	public Task Reconcile(CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

internal sealed class EmptyInstallationCatalog : IPluginInstallationCatalog
{
	public IReadOnlyList<InstalledPlugin> Discover() => [];

	public bool TryResolveActive(string pluginId, out InstalledPluginVersion? version)
	{
		version = null;
		return false;
	}

	public void Invalidate()
	{
	}
}

internal sealed class NeverFindsManifest : IPluginManifestReader
{
	public PluginManifestReadResult Read(string manifestPath, string expectedPluginId, string expectedVersion)
		=> PluginManifestReadResult.Fail(PluginManifestError.NotFound, "Not found.");

	public PluginManifestReadResult ReadFromJson(string json,
		string? versionDirectory,
		string expectedPluginId,
		string expectedVersion)
		=> PluginManifestReadResult.Fail(PluginManifestError.NotFound, "Not found.");
}

internal sealed class RecordingNotificationStore : IUserNotificationStore
{
	public List<UserNotificationDraft> Raised { get; } = [];

	public int Capacity => 100;

	public UserNotification? Raise(UserNotificationDraft draft)
	{
		Raised.Add(draft);
		return null;
	}

	public UserNotification? RaiseIfAbsent(UserNotificationDraft draft) => Raise(draft);

	public IReadOnlyList<UserNotification> Snapshot() => [];

	public bool UpdateProgress(string dedupeKey, UserNotificationProgress progress) => false;

	public bool Dismiss(string id) => false;

	public bool DismissByKey(string dedupeKey) => false;

	public bool Retire(string dedupeKey) => false;

	public bool DismissAll() => false;

	public event Action? Changed
	{
		add { }
		remove { }
	}
}

internal sealed class RecordingMediator : IMediator
{
	public List<object> Published { get; } = [];

	public ValueTask Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
		where TNotification : INotification
	{
		if (notification is not null)
		{
			Published.Add(notification);
		}

		return default;
	}

	public ValueTask Publish(object notification, CancellationToken cancellationToken = default)
	{
		Published.Add(notification);
		return default;
	}

	public ValueTask<TResponse> Send<TResponse>(IRequest<TResponse> request,
		CancellationToken cancellationToken = default)
		=> throw new NotSupportedException();

	public ValueTask<TResponse> Send<TResponse>(ICommand<TResponse> command,
		CancellationToken cancellationToken = default)
		=> throw new NotSupportedException();

	public ValueTask<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default)
		=> throw new NotSupportedException();

	public ValueTask<object?> Send(object message, CancellationToken cancellationToken = default)
		=> throw new NotSupportedException();

	public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
		IStreamRequest<TResponse> request,
		CancellationToken cancellationToken = default)
		=> throw new NotSupportedException();

	public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
		IStreamCommand<TResponse> command,
		CancellationToken cancellationToken = default)
		=> throw new NotSupportedException();

	public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
		IStreamQuery<TResponse> query,
		CancellationToken cancellationToken = default)
		=> throw new NotSupportedException();

	public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
		=> throw new NotSupportedException();
}
