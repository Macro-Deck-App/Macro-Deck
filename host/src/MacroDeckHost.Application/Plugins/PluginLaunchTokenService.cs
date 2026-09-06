using System.Collections.Concurrent;
using MacroDeckHost.Application.Auth;

namespace MacroDeckHost.Application.Plugins;

public sealed record PluginLaunch(string PluginId, string LaunchId, string DisplayName, string Version);

public interface IPluginLaunchTokenService
{
	string Mint(string pluginId, string launchId, string displayName, string version);

	bool TryAcquire(string pluginId, string presentedSecret, out PluginLaunch? launch);

	void Bind(string launchId, string sessionId);

	void Release(string launchId);

	void Discard(string launchId);

	bool HasActiveLaunch(string pluginId);
}

public class PluginLaunchTokenService : IPluginLaunchTokenService
{
	private static readonly TimeSpan _unusedLifetime = TimeSpan.FromMinutes(2);

	private readonly TimeProvider _timeProvider;
	private readonly IPluginSessionRegistry _sessionRegistry;

	private readonly ConcurrentDictionary<string, LaunchEntry> _byHash = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, LaunchEntry> _byLaunchId = new(StringComparer.Ordinal);

	public PluginLaunchTokenService(TimeProvider timeProvider, IPluginSessionRegistry sessionRegistry)
	{
		_timeProvider = timeProvider;
		_sessionRegistry = sessionRegistry;
	}

	public string Mint(string pluginId, string launchId, string displayName, string version)
	{
		var raw = TokenHasher.Generate();
		var hash = TokenHasher.Hash(raw);
		var entry = new LaunchEntry(pluginId,
			launchId,
			displayName,
			version,
			hash,
			_timeProvider.GetUtcNow() + _unusedLifetime);

		if (_byLaunchId.TryRemove(launchId, out var previous))
		{
			_byHash.TryRemove(previous.TokenHash, out _);
		}

		_byLaunchId[launchId] = entry;
		_byHash[hash] = entry;

		Prune();

		return raw;
	}

	public bool TryAcquire(string pluginId, string presentedSecret, out PluginLaunch? launch)
	{
		Prune();
		launch = null;

		if (string.IsNullOrEmpty(presentedSecret))
		{
			return false;
		}

		var hash = TokenHasher.Hash(presentedSecret);
		if (!_byHash.TryGetValue(hash, out var entry))
		{
			return false;
		}

		if (!TokenHasher.Verify(presentedSecret, entry.TokenHash))
		{
			return false;
		}

		if (!string.Equals(entry.PluginId, pluginId, StringComparison.Ordinal))
		{
			return false;
		}

		if (!entry.HasEverBeenAcquired && _timeProvider.GetUtcNow() > entry.ExpiresAt)
		{
			return false;
		}

		if (!entry.TryMarkLive() && !TryReclaim(entry))
		{
			return false;
		}

		entry.HasEverBeenAcquired = true;
		launch = new PluginLaunch(entry.PluginId, entry.LaunchId, entry.DisplayName, entry.Version);
		return true;
	}

	public void Bind(string launchId, string sessionId)
	{
		if (_byLaunchId.TryGetValue(launchId, out var entry))
		{
			entry.Bind(sessionId);
		}
	}

	private bool TryReclaim(LaunchEntry entry)
	{
		if (entry.BoundSessionId is not { } boundSessionId)
		{
			return false;
		}

		if (_sessionRegistry.Snapshot()
			.Any(session => string.Equals(session.SessionId, boundSessionId, StringComparison.Ordinal)))
		{
			return false;
		}

		return entry.TryReclaim(boundSessionId);
	}

	public void Release(string launchId)
	{
		if (_byLaunchId.TryGetValue(launchId, out var entry))
		{
			entry.MarkUnused();
		}
	}

	public void Discard(string launchId)
	{
		if (_byLaunchId.TryRemove(launchId, out var entry))
		{
			_byHash.TryRemove(entry.TokenHash, out _);
		}
	}

	public bool HasActiveLaunch(string pluginId)
	{
		Prune();

		foreach (var entry in _byLaunchId.Values)
		{
			if (string.Equals(entry.PluginId, pluginId, StringComparison.Ordinal))
			{
				return true;
			}
		}

		return false;
	}

	private void Prune()
	{
		var now = _timeProvider.GetUtcNow();
		foreach (var (launchId, entry) in _byLaunchId)
		{
			if (!entry.HasEverBeenAcquired && now > entry.ExpiresAt)
			{
				_byLaunchId.TryRemove(launchId, out _);
				_byHash.TryRemove(entry.TokenHash, out _);
			}
		}
	}

	private sealed class LaunchEntry
	{
		private const int Unused = 0;
		private const int Live = 1;

		private int _state;
		private string? _boundSessionId;

		public LaunchEntry(string pluginId,
			string launchId,
			string displayName,
			string version,
			string tokenHash,
			DateTimeOffset expiresAt)
		{
			PluginId = pluginId;
			LaunchId = launchId;
			DisplayName = displayName;
			Version = version;
			TokenHash = tokenHash;
			ExpiresAt = expiresAt;
		}

		public string PluginId { get; }

		public string LaunchId { get; }

		public string DisplayName { get; }

		public string Version { get; }

		public string TokenHash { get; }

		public DateTimeOffset ExpiresAt { get; }

		public bool HasEverBeenAcquired { get; set; }

		public string? BoundSessionId => Volatile.Read(ref _boundSessionId);

		public bool TryMarkLive() => Interlocked.CompareExchange(ref _state, Live, Unused) == Unused;

		public void Bind(string sessionId) => Volatile.Write(ref _boundSessionId, sessionId);

		public bool TryReclaim(string observedSessionId)
			=> ReferenceEquals(Interlocked.CompareExchange(ref _boundSessionId, null, observedSessionId),
				observedSessionId);

		public void MarkUnused()
		{
			Volatile.Write(ref _boundSessionId, null);
			Interlocked.Exchange(ref _state, Unused);
		}
	}
}
