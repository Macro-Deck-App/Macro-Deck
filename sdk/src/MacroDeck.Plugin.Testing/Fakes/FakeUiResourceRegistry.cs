using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Resources;

namespace MacroDeck.Plugin.Testing.Fakes;

/// <summary>
/// In-memory <see cref="IUiResourceRegistry" /> that enforces the same rules as Macro Deck: the name
/// grammar and media types of <see cref="UiResourceRules" />, the per-resource size limit, replacement
/// by name, and the per-plugin quota, which a test can lower through <see cref="MaxTotalBytes" /> and
/// <see cref="MaxCount" /> to exercise <see cref="UiResourceErrorCode.QuotaExceeded" />.
/// </summary>
public sealed class FakeUiResourceRegistry : IUiResourceRegistry
{
	private readonly Lock _gate = new();
	private readonly Dictionary<string, FakeUiResource> _resources = new(StringComparer.Ordinal);

	/// <summary>The combined size all resources may have. Defaults to Macro Deck's per-plugin quota.</summary>
	public int MaxTotalBytes { get; set; } = ProtocolLimits.MaxUiResourceBytesPerPlugin;

	/// <summary>How many resources may be registered at once. Defaults to Macro Deck's per-plugin quota.</summary>
	public int MaxCount { get; set; } = ProtocolLimits.MaxUiResourcesPerPlugin;

	/// <summary>Everything currently registered, by name.</summary>
	public IReadOnlyDictionary<string, FakeUiResource> Resources
	{
		get
		{
			lock (_gate)
			{
				return new Dictionary<string, FakeUiResource>(_resources, StringComparer.Ordinal);
			}
		}
	}

	/// <inheritdoc />
	public Task<UiResource> RegisterAsync(string name,
		ReadOnlyMemory<byte> content,
		string mediaType,
		CancellationToken cancellationToken = default)
	{
		if (!UiResourceRules.IsValidName(name))
		{
			throw new ArgumentException($"'{name}' is not a valid UI resource name.", nameof(name));
		}

		if (!UiResourceRules.IsSupportedMediaType(mediaType))
		{
			throw new ArgumentException($"'{mediaType}' is not a supported media type.", nameof(mediaType));
		}

		if (content.IsEmpty || content.Length > ProtocolLimits.MaxUiResourceBytes)
		{
			throw new ArgumentException($"The content is {content.Length} bytes.", nameof(content));
		}

		lock (_gate)
		{
			var replaced = _resources.GetValueOrDefault(name);
			var totalBytes = _resources.Values.Sum(resource => resource.Content.Length) -
				(replaced?.Content.Length ?? 0) + content.Length;
			var count = _resources.Count - (replaced is null ? 0 : 1) + 1;

			if (totalBytes > MaxTotalBytes || count > MaxCount)
			{
				return Task.FromException<UiResource>(new UiResourceException(UiResourceErrorCode.QuotaExceeded,
					"The plugin's UI resource quota is exhausted."));
			}

			var bytes = content.ToArray();
			var handle = new UiResource
			{
				ResourceId = "plugin-test." + name,
				ContentHash = AssetContentHash.Compute(bytes),
				MediaType = mediaType.ToLowerInvariant(),
				ByteLength = bytes.Length,
			};

			_resources[name] = new FakeUiResource(handle, bytes);

			return Task.FromResult(handle);
		}
	}

	/// <inheritdoc />
	public Task RemoveAsync(string name, CancellationToken cancellationToken = default)
	{
		if (!UiResourceRules.IsValidName(name))
		{
			throw new ArgumentException($"'{name}' is not a valid UI resource name.", nameof(name));
		}

		lock (_gate)
		{
			_resources.Remove(name);
		}

		return Task.CompletedTask;
	}
}

/// <summary>One resource held by <see cref="FakeUiResourceRegistry" />: the handle it answered and the
/// bytes behind it.</summary>
public sealed record FakeUiResource(UiResource Handle, byte[] Content);
