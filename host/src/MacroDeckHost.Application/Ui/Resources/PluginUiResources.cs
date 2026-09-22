using System.Security.Cryptography;
using System.Text;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Ui.Model.Resources;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Assets;

namespace MacroDeckHost.Application.Ui.Resources;

public enum PluginUiResourceOutcome
{
	Registered,

	Removed,

	UploadRequired,

	InvalidName,

	UnsupportedMediaType,

	MediaTypeMismatch,

	SessionNotCurrent,

	QuotaExceeded
}

public sealed record PluginUiResourceResult(PluginUiResourceOutcome Outcome, UiResource? Resource = null);

public interface IPluginUiResources
{
	PluginUiResourceResult Register(string pluginId,
		string sessionId,
		string name,
		string contentHash,
		string mediaType);

	PluginUiResourceResult Remove(string pluginId, string sessionId, string name);
}

public sealed class PluginUiResources : IPluginUiResources, IDisposable
{
	private readonly IUiResourceStore _store;
	private readonly IPluginAssetReceiver _assets;
	private readonly IPluginSessionRegistry _sessions;
	private readonly Dictionary<string, PluginResources> _byPlugin = new(StringComparer.Ordinal);
	private readonly Lock _lock = new();

	public PluginUiResources(IUiResourceStore store, IPluginAssetReceiver assets, IPluginSessionRegistry sessions)
	{
		_store = store;
		_assets = assets;
		_sessions = sessions;
		_assets.AssetCommitted += OnAssetCommitted;
		_sessions.SessionEnded += OnSessionEnded;
	}

	public PluginUiResourceResult Register(string pluginId,
		string sessionId,
		string name,
		string contentHash,
		string mediaType)
	{
		if (!UiResourceRules.IsValidName(name))
		{
			return new PluginUiResourceResult(PluginUiResourceOutcome.InvalidName);
		}

		if (!UiResourceRules.IsSupportedMediaType(mediaType))
		{
			return new PluginUiResourceResult(PluginUiResourceOutcome.UnsupportedMediaType);
		}

		lock (_lock)
		{
			if (!IsCurrentSession(pluginId, sessionId))
			{
				return new PluginUiResourceResult(PluginUiResourceOutcome.SessionNotCurrent);
			}

			var plugin = ForSession(pluginId, sessionId);

			if (plugin.Entries.TryGetValue(name, out var existing) &&
				string.Equals(existing.Handle.ContentHash, contentHash, StringComparison.Ordinal) &&
				string.Equals(existing.Handle.MediaType, mediaType, StringComparison.OrdinalIgnoreCase))
			{
				return new PluginUiResourceResult(PluginUiResourceOutcome.Registered, existing.Handle);
			}

			if (!plugin.Pending.TryGetValue(contentHash, out var upload))
			{
				return new PluginUiResourceResult(PluginUiResourceOutcome.UploadRequired);
			}

			if (!string.Equals(upload.MediaType, mediaType, StringComparison.OrdinalIgnoreCase))
			{
				return new PluginUiResourceResult(PluginUiResourceOutcome.MediaTypeMismatch);
			}

			var replacedBytes = existing?.ByteLength ?? 0;
			var bytes = plugin.Bytes - replacedBytes + upload.Bytes.Length;
			var count = plugin.Entries.Count - (existing is null ? 0 : 1) + 1;

			if (bytes > ProtocolLimits.MaxUiResourceBytesPerPlugin || count > ProtocolLimits.MaxUiResourcesPerPlugin)
			{
				return new PluginUiResourceResult(PluginUiResourceOutcome.QuotaExceeded);
			}

			var handle = _store.Register(new UiResourceRegistration
			{
				OwnerId = OwnerId(pluginId),
				Name = name,
				MediaType = mediaType.ToLowerInvariant(),
				Content = upload.Bytes,
			});

			plugin.Entries[name] = new Entry(handle, upload.Bytes.Length);
			plugin.Bytes = bytes;
			plugin.Pending.Remove(contentHash);
			plugin.PendingBytes -= upload.Bytes.Length;

			return new PluginUiResourceResult(PluginUiResourceOutcome.Registered, handle);
		}
	}

	public PluginUiResourceResult Remove(string pluginId, string sessionId, string name)
	{
		if (!UiResourceRules.IsValidName(name))
		{
			return new PluginUiResourceResult(PluginUiResourceOutcome.InvalidName);
		}

		lock (_lock)
		{
			if (!IsCurrentSession(pluginId, sessionId))
			{
				return new PluginUiResourceResult(PluginUiResourceOutcome.SessionNotCurrent);
			}

			var plugin = ForSession(pluginId, sessionId);

			if (plugin.Entries.Remove(name, out var entry))
			{
				_store.Remove(entry.Handle.ResourceId);
				plugin.Bytes -= entry.ByteLength;
			}

			return new PluginUiResourceResult(PluginUiResourceOutcome.Removed);
		}
	}

	public void Dispose()
	{
		_assets.AssetCommitted -= OnAssetCommitted;
		_sessions.SessionEnded -= OnSessionEnded;
	}

	internal static string OwnerId(string pluginId)
		=> "plugin-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(pluginId)))[..16];

	// Mirrors PluginMessagingRegistrations: the registry maps a plugin to its new session before it
	// announces the old one pruned, so checking under the lock the release also takes cannot resurrect one.
	private bool IsCurrentSession(string pluginId, string sessionId)
	{
		var current = _sessions.Snapshot()
			.FirstOrDefault(session => string.Equals(session.PluginId, pluginId, StringComparison.Ordinal));

		return current is not null && string.Equals(current.SessionId, sessionId, StringComparison.Ordinal);
	}

	private PluginResources ForSession(string pluginId, string sessionId)
	{
		if (!_byPlugin.TryGetValue(pluginId, out var plugin))
		{
			plugin = new PluginResources();
			_byPlugin[pluginId] = plugin;
		}

		if (!string.Equals(plugin.SessionId, sessionId, StringComparison.Ordinal))
		{
			ReleaseEntries(plugin);
			plugin.SessionId = sessionId;
		}

		return plugin;
	}

	private void ReleaseEntries(PluginResources plugin)
	{
		foreach (var entry in plugin.Entries.Values)
		{
			_store.Remove(entry.Handle.ResourceId);
		}

		plugin.Entries.Clear();
		plugin.Bytes = 0;
	}

	private void OnAssetCommitted(object? sender, AssetCommittedEventArgs e)
	{
		if (!string.Equals(e.Kind, AssetKinds.UiResource, StringComparison.Ordinal))
		{
			return;
		}

		lock (_lock)
		{
			if (!_byPlugin.TryGetValue(e.PluginId, out var plugin))
			{
				plugin = new PluginResources();
				_byPlugin[e.PluginId] = plugin;
			}

			if (plugin.Pending.Remove(e.ContentHash, out var previous))
			{
				plugin.PendingBytes -= previous.Bytes.Length;
			}

			plugin.Pending.Add(e.ContentHash, new PendingUpload(e.Bytes, e.MimeType));
			plugin.PendingBytes += e.Bytes.Length;

			while (plugin.Pending.Count > ProtocolLimits.MaxUiResourcesPerPlugin ||
				plugin.PendingBytes > ProtocolLimits.MaxUiResourceBytesPerPlugin)
			{
				var oldest = plugin.Pending.GetAt(0);
				plugin.Pending.RemoveAt(0);
				plugin.PendingBytes -= oldest.Value.Bytes.Length;
			}
		}
	}

	private void OnSessionEnded(object? sender, PluginSessionEndedEventArgs e)
	{
		if (e.Reason != PluginSessionEndReason.Pruned)
		{
			return;
		}

		lock (_lock)
		{
			if (!_byPlugin.TryGetValue(e.PluginId, out var plugin) ||
				(plugin.SessionId is not null && !string.Equals(plugin.SessionId, e.SessionId, StringComparison.Ordinal)))
			{
				return;
			}

			ReleaseEntries(plugin);
			_byPlugin.Remove(e.PluginId);
		}
	}

	private sealed record Entry(UiResource Handle, int ByteLength);

	private sealed record PendingUpload(byte[] Bytes, string MediaType);

	private sealed class PluginResources
	{
		public readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal);

		public readonly OrderedDictionary<string, PendingUpload> Pending = new(StringComparer.Ordinal);

		public string? SessionId;

		public long Bytes;

		public long PendingBytes;
	}
}
