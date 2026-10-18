using System.Security.Cryptography;
using System.Text;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Resources;
using MacroDeckHost.Integrations.Feeds.Protocol;
using Serilog;

namespace MacroDeckHost.Integrations.Feeds.Widgets;

internal interface IFeedImages
{
	event EventHandler? Changed;

	UiResource? Find(string url);

	void Track(string url, bool allowLocalNetwork);
}

internal sealed class FeedImageCache : IFeedImages, IDisposable
{
	public const int MaxImageBytes = 2 * 1024 * 1024;

	public const int MaxEntries = 200;

	public const long MaxTotalBytes = 32L * 1024 * 1024;

	private const string ImageAccept = "image/png, image/jpeg, image/webp, image/gif";

	private static readonly byte[] _pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

	private static readonly ILogger _logger = IntegrationLog.For<FeedImageCache>(FeedsIntegration.IntegrationId);

	private readonly IFeedFetcher _fetcher;
	private readonly Func<IUiResourceRegistry?> _resources;
	private readonly Lock _sync = new();
	private readonly Dictionary<string, LinkedListNode<Entry>> _entries = new(StringComparer.Ordinal);
	private readonly LinkedList<Entry> _order = new();
	private readonly CancellationTokenSource _disposing = new();

	private long _totalBytes;
	private bool _disposed;

	public FeedImageCache(IFeedFetcher fetcher, Func<IUiResourceRegistry?> resources)
	{
		_fetcher = fetcher;
		_resources = resources;
	}

	public event EventHandler? Changed;

	public UiResource? Find(string url)
	{
		lock (_sync)
		{
			if (!_entries.TryGetValue(url, out var node))
			{
				return null;
			}

			_order.Remove(node);
			_order.AddFirst(node);
			return node.Value.Resource;
		}
	}

	public void Track(string url, bool allowLocalNetwork)
	{
		if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !FeedClient.IsWebUrl(uri))
		{
			return;
		}

		List<string> evicted;
		lock (_sync)
		{
			if (_disposed || _entries.ContainsKey(url))
			{
				return;
			}

			_entries[url] = _order.AddFirst(new Entry(url, ResourceName(url)));
			evicted = Evict();
		}

		Remove(evicted);
		_ = FetchAsync(url, uri, allowLocalNetwork, _disposing.Token);
	}

	public void Dispose()
	{
		List<Entry> entries;
		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			entries = [.. _order];
			_entries.Clear();
			_order.Clear();
			_totalBytes = 0;
		}

		_disposing.Cancel();
		_disposing.Dispose();
		Remove(entries.Where(entry => entry.Resource is not null).Select(entry => entry.Name));
	}

	internal static string? MediaType(ReadOnlySpan<byte> bytes)
	{
		if (bytes.StartsWith(_pngSignature))
		{
			return "image/png";
		}

		if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
		{
			return "image/jpeg";
		}

		if (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8))
		{
			return "image/gif";
		}

		return bytes.Length >= 12 && bytes.StartsWith("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8)
			? "image/webp"
			: null;
	}

	internal static string ResourceName(string url)
	{
		return "feed-image-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..24];
	}

	private async Task FetchAsync(string url, Uri uri, bool allowLocalNetwork, CancellationToken cancellationToken)
	{
		UiResource? resource = null;
		var size = 0;
		try
		{
			var request = new FeedRequest(uri, allowLocalNetwork) { MaxBytes = MaxImageBytes, Accept = ImageAccept };
			var fetch = await _fetcher.FetchAsync(request, cancellationToken).ConfigureAwait(false);

			if (fetch is { Status: FeedFetchStatus.Ok, Content: { } bytes }
				&& MediaType(bytes) is { } mediaType
				&& _resources() is { } registry)
			{
				resource = await registry.RegisterAsync(ResourceName(url), bytes, mediaType, cancellationToken)
					.ConfigureAwait(false);
				size = bytes.Length;
			}
		}
		catch (Exception exception) when (exception is UiResourceException or ArgumentException)
		{
			_logger.Debug(exception, "A feed image could not be registered");
		}
		catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
		{
			return;
		}

		List<string> evicted;
		lock (_sync)
		{
			if (_disposed || !_entries.TryGetValue(url, out var node))
			{
				evicted = resource is null ? [] : [ResourceName(url)];
			}
			else
			{
				node.Value = node.Value with { Resource = resource, Bytes = size };
				_totalBytes += size;
				evicted = Evict();
			}
		}

		Remove(evicted);

		if (resource is not null)
		{
			Changed?.Invoke(this, EventArgs.Empty);
		}
	}

	private List<string> Evict()
	{
		var evicted = new List<string>();
		while ((_entries.Count > MaxEntries || _totalBytes > MaxTotalBytes) && _order.Last is { } last)
		{
			_order.RemoveLast();
			_entries.Remove(last.Value.Url);
			_totalBytes -= last.Value.Bytes;
			if (last.Value.Resource is not null)
			{
				evicted.Add(last.Value.Name);
			}
		}

		return evicted;
	}

	private void Remove(IEnumerable<string> names)
	{
		if (_resources() is not { } registry)
		{
			return;
		}

		foreach (var name in names)
		{
			_ = RemoveAsync(registry, name);
		}
	}

	private static async Task RemoveAsync(IUiResourceRegistry registry, string name)
	{
		try
		{
			await registry.RemoveAsync(name).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is UiResourceException or ArgumentException)
		{
		}
	}

	private sealed record Entry(string Url, string Name)
	{
		public UiResource? Resource { get; init; }

		public int Bytes { get; init; }
	}
}
