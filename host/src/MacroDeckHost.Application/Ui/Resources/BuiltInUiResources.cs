using System.Reflection;
using System.Text;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Resources;
using MacroDeckHost.Application.MusicPlayer;

namespace MacroDeckHost.Application.Ui.Resources;

public sealed class BuiltInUiResources : IUiResourceRegistry
{
	private const string BundledIconPrefix = "icon.";
	private static readonly TimeSpan _artworkTimeout = TimeSpan.FromSeconds(20);

	private static readonly (string Extension, string MediaType)[] _bundledIconTypes =
	[
		(".svg", "image/svg+xml"),
		(".png", "image/png"),
		(".webp", "image/webp")
	];

	private readonly string _ownerId;
	private readonly IUiResourceStore _store;
	private readonly Assembly? _iconAssembly;
	private readonly string? _iconPrefix;
	private readonly IMusicPlayerArtworkService? _artwork;
	private readonly Lock _gate = new();
	private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
	private readonly Dictionary<string, UiResource> _bundledIcons = new(StringComparer.Ordinal);
	private long _bytes;
	private bool _released;

	public BuiltInUiResources(
		string ownerId,
		IUiResourceStore store,
		Type? integrationType = null,
		IMusicPlayerArtworkService? artwork = null)
	{
		ArgumentException.ThrowIfNullOrEmpty(ownerId);
		ArgumentNullException.ThrowIfNull(store);

		_ownerId = ownerId;
		_store = store;
		_artwork = artwork;
		_iconAssembly = integrationType?.Assembly;
		_iconPrefix = integrationType?.Namespace is { Length: > 0 } ns ? ns + ".Icons." : null;
	}

	public string OwnerId => _ownerId;

	public Task<UiResource> RegisterAsync(
		string name,
		ReadOnlyMemory<byte> content,
		string mediaType,
		CancellationToken cancellationToken = default)
	{
		Validate(name);

		if (!UiResourceRules.IsSupportedMediaType(mediaType))
		{
			throw new ArgumentException(
				"A UI resource must be one of: " + string.Join(", ", UiResourceRules.SupportedMediaTypes) + ".",
				nameof(mediaType));
		}

		if (content.IsEmpty || content.Length > ProtocolLimits.MaxUiResourceBytes)
		{
			throw new ArgumentException($"A UI resource is 1 to {ProtocolLimits.MaxUiResourceBytes} bytes.",
				nameof(content));
		}

		cancellationToken.ThrowIfCancellationRequested();

		return Task.FromResult(Commit(name, content.ToArray(), mediaType.ToLowerInvariant()));
	}

	public Task RemoveAsync(string name, CancellationToken cancellationToken = default)
	{
		Validate(name);
		cancellationToken.ThrowIfCancellationRequested();

		lock (_gate)
		{
			if (_entries.Remove(name, out var entry))
			{
				_store.Remove(entry.Handle.ResourceId);
				_bytes -= entry.ByteLength;
			}
		}

		return Task.CompletedTask;
	}

	public Task<UiResource> GetPluginIconAsync(string key, string name, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		if (!UiResourceRules.IsValidName(key) || !UiResourceRules.IsValidName(name))
		{
			return Task.FromException<UiResource>(NotFound(key, name));
		}

		var resourceName = BundledIconPrefix + key + "." + name;

		lock (_gate)
		{
			if (_released)
			{
				return Task.FromException<UiResource>(Released());
			}

			if (_bundledIcons.TryGetValue(resourceName, out var cached))
			{
				return Task.FromResult(cached);
			}

			if (FindBundledIcon(key, name) is not { } icon)
			{
				return Task.FromException<UiResource>(NotFound(key, name));
			}

			UiResource handle;
			try
			{
				handle = _store.Register(new UiResourceRegistration
				{
					OwnerId = _ownerId,
					Name = resourceName,
					MediaType = icon.MediaType,
					Content = icon.Content
				});
			}
			catch (ArgumentException exception)
			{
				return Task.FromException<UiResource>(new UiResourceException(UiResourceErrorCode.Failed,
					$"The bundled icon '{key}/{name}' cannot be served as a UI resource.",
					exception));
			}

			_bundledIcons[resourceName] = handle;

			return Task.FromResult(handle);
		}
	}

	public Task<UiResource?> RegisterMusicPlayerArtworkAsync(
		string name,
		string instanceId,
		string artworkId,
		CancellationToken cancellationToken = default)
	{
		return RegisterMusicPlayerArtworkAsync(name, instanceId, artworkId, null, cancellationToken);
	}

	public Task<UiResource?> RegisterMusicPlayerArtworkAsync(
		string name,
		string instanceId,
		string artworkId,
		int maxSize,
		CancellationToken cancellationToken = default)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSize);
		return RegisterMusicPlayerArtworkAsync(name, instanceId, artworkId, (int?)maxSize, cancellationToken);
	}

	private async Task<UiResource?> RegisterMusicPlayerArtworkAsync(
		string name,
		string instanceId,
		string artworkId,
		int? maxSize,
		CancellationToken cancellationToken)
	{
		Validate(name);

		if (_artwork is null)
		{
			throw new UiResourceException(UiResourceErrorCode.Unsupported,
				"This context cannot register the artwork of another music player.");
		}

		if (string.IsNullOrEmpty(instanceId) || string.IsNullOrEmpty(artworkId))
		{
			return null;
		}

		ArtworkImageResult? image;
		using (var lookup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
		{
			lookup.CancelAfter(_artworkTimeout);
			try
			{
				image = await _artwork.GetImage(instanceId, artworkId, maxSize, lookup.Token).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
				throw new UiResourceException(UiResourceErrorCode.Failed,
					"The music player did not answer the artwork lookup in time.");
			}
		}

		if (image is null || image.Content.Length == 0)
		{
			return null;
		}

		if (!UiResourceRules.IsSupportedMediaType(image.ContentType)
			|| image.Content.Length > ProtocolLimits.MaxUiResourceBytes)
		{
			throw new UiResourceException(UiResourceErrorCode.Failed,
				"The artwork cannot be registered as a UI resource.");
		}

		return Commit(name, image.Content, image.ContentType.ToLowerInvariant());
	}

	public void Release()
	{
		lock (_gate)
		{
			foreach (var entry in _entries.Values)
			{
				_store.Remove(entry.Handle.ResourceId);
			}

			foreach (var icon in _bundledIcons.Values)
			{
				_store.Remove(icon.ResourceId);
			}

			_entries.Clear();
			_bundledIcons.Clear();
			_bytes = 0;
			_released = true;
		}
	}

	private UiResource Commit(string name, byte[] content, string mediaType)
	{
		var contentHash = AssetContentHash.Compute(content);

		lock (_gate)
		{
			if (_released)
			{
				throw Released();
			}

			_entries.TryGetValue(name, out var existing);

			if (existing is not null
				&& string.Equals(existing.Handle.ContentHash, contentHash, StringComparison.Ordinal)
				&& string.Equals(existing.Handle.MediaType, mediaType, StringComparison.Ordinal))
			{
				return existing.Handle;
			}

			var bytes = _bytes - (existing?.ByteLength ?? 0) + content.Length;
			var count = _entries.Count - (existing is null ? 0 : 1) + 1;

			if (bytes > ProtocolLimits.MaxUiResourceBytesPerPlugin || count > ProtocolLimits.MaxUiResourcesPerPlugin)
			{
				throw new UiResourceException(UiResourceErrorCode.QuotaExceeded,
					"The integration's UI resources would exceed their combined size or count.");
			}

			var handle = _store.Register(new UiResourceRegistration
			{
				OwnerId = _ownerId,
				Name = name,
				MediaType = mediaType,
				Content = content
			});

			_entries[name] = new Entry(handle, content.Length);
			_bytes = bytes;

			return handle;
		}
	}

	private BundledIcon? FindBundledIcon(string key, string name)
	{
		if (_iconAssembly is null || _iconPrefix is null)
		{
			return null;
		}

		var resourceNames = _iconAssembly.GetManifestResourceNames();

		foreach (var folder in new[] { key, ManifestFolderName(key) }.Distinct(StringComparer.Ordinal))
		{
			foreach (var (extension, mediaType) in _bundledIconTypes)
			{
				var resourceName = _iconPrefix + folder + "." + name + extension;
				if (!resourceNames.Contains(resourceName, StringComparer.Ordinal))
				{
					continue;
				}

				using var stream = _iconAssembly.GetManifestResourceStream(resourceName);
				if (stream is null)
				{
					continue;
				}

				using var memory = new MemoryStream();
				stream.CopyTo(memory);
				return new BundledIcon(memory.ToArray(), mediaType);
			}
		}

		return null;
	}

	// MSBuild names an embedded resource's folders as identifiers: anything but a letter, digit or
	// underscore becomes an underscore, and a leading digit gains one.
	private static string ManifestFolderName(string folder)
	{
		var builder = new StringBuilder(folder.Length + 1);
		if (char.IsAsciiDigit(folder[0]))
		{
			builder.Append('_');
		}

		foreach (var c in folder)
		{
			builder.Append(char.IsAsciiLetterOrDigit(c) || c == '_' ? c : '_');
		}

		return builder.ToString();
	}

	private static void Validate(string name)
	{
		if (!UiResourceRules.IsValidName(name))
		{
			throw new ArgumentException(
				"A UI resource name is a letter or digit followed by up to 63 letters, digits, hyphens or underscores.",
				nameof(name));
		}
	}

	private static UiResourceException Released()
	{
		return new UiResourceException(UiResourceErrorCode.Failed,
			"The integration has been shut down; its UI resources were released.");
	}

	private static UiResourceException NotFound(string key, string name)
	{
		return new UiResourceException(UiResourceErrorCode.PluginIconNotFound,
			$"The integration has no bundled icon '{name}' in the icon folder '{key}'.");
	}

	private sealed record Entry(UiResource Handle, int ByteLength);

	private sealed record BundledIcon(byte[] Content, string MediaType);
}
