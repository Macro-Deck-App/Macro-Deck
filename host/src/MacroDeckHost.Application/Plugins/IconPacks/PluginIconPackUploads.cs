using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeckHost.Application.Plugins.Assets;

namespace MacroDeckHost.Application.Plugins.IconPacks;

public interface IPluginIconPackUploads
{
	byte[]? Find(string pluginId, string contentHash);

	void Release(string pluginId);

	void Retain(string pluginId, IReadOnlyCollection<string> contentHashes);
}

public sealed class PluginIconPackUploads : IPluginIconPackUploads, IDisposable
{
	private readonly IPluginAssetReceiver _assets;
	private readonly IPluginSessionRegistry _sessions;
	private readonly Dictionary<string, OrderedDictionary<string, byte[]>> _byPlugin = new(StringComparer.Ordinal);
	private readonly Lock _lock = new();

	public PluginIconPackUploads(IPluginAssetReceiver assets, IPluginSessionRegistry sessions)
	{
		_assets = assets;
		_sessions = sessions;
		_assets.AssetCommitted += OnAssetCommitted;
		_sessions.SessionEnded += OnSessionEnded;
	}

	public byte[]? Find(string pluginId, string contentHash)
	{
		lock (_lock)
		{
			return _byPlugin.TryGetValue(pluginId, out var uploads) && uploads.TryGetValue(contentHash, out var bytes)
				? bytes
				: null;
		}
	}

	public void Release(string pluginId)
	{
		lock (_lock)
		{
			_byPlugin.Remove(pluginId);
		}
	}

	public void Retain(string pluginId, IReadOnlyCollection<string> contentHashes)
	{
		lock (_lock)
		{
			if (!_byPlugin.TryGetValue(pluginId, out var uploads))
			{
				return;
			}

			foreach (var hash in uploads.Keys.Where(hash => !contentHashes.Contains(hash, StringComparer.Ordinal)).ToList())
			{
				uploads.Remove(hash);
			}
		}
	}

	public void Dispose()
	{
		_assets.AssetCommitted -= OnAssetCommitted;
		_sessions.SessionEnded -= OnSessionEnded;
	}

	private void OnAssetCommitted(object? sender, AssetCommittedEventArgs e)
	{
		if (!string.Equals(e.Kind, AssetKinds.IconPack, StringComparison.Ordinal) || !HasDevelopmentSession(e.PluginId))
		{
			return;
		}

		lock (_lock)
		{
			if (!_byPlugin.TryGetValue(e.PluginId, out var uploads))
			{
				uploads = new OrderedDictionary<string, byte[]>(StringComparer.Ordinal);
				_byPlugin[e.PluginId] = uploads;
			}

			uploads.Remove(e.ContentHash);
			uploads.Add(e.ContentHash, e.Bytes);
			while (uploads.Count > PluginBundledIconPacks.MaxCount)
			{
				uploads.RemoveAt(0);
			}
		}
	}

	private bool HasDevelopmentSession(string pluginId)
		=> _sessions.Snapshot()
			.Any(session => string.Equals(session.PluginId, pluginId, StringComparison.Ordinal) &&
				session.State == PluginSessionState.Connected &&
				session.Origin == PluginSessionOrigin.SelfRegistered);

	private void OnSessionEnded(object? sender, PluginSessionEndedEventArgs e) => Release(e.PluginId);
}
