using System.Runtime.InteropServices;
using System.Security.Cryptography;
using SkiaSharp;
using Svg.Skia;

namespace MacroDeckHost.Rendering.Paint;

internal sealed record DecodedArtwork(
	SKPicture? Picture,
	AnimatedImage? Raster,
	SKSvg? Document = null,
	SvgMotion? Motion = null);

internal static class ArtworkCache
{
	private const long BudgetBytes = 64L * 1024 * 1024;

	private const long SweepIntervalMs = 1000;

	private static readonly Lock _gate = new();
	private static readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
	private static readonly LinkedList<Entry> _recent = [];

	private static long _bytes;
	private static long _sweptAt;

	// Lets the tests prove a resource is decoded once; nothing else observes decoding.
	internal static event Action<string>? Decoded;

	internal static string KeyOf(RenderResource resource)
	{
		var kind = Artwork.IsSvg(resource) ? "svg" : "raster";
		var hash = resource.ContentHash is { Length: > 0 } known
			? known
			: Convert.ToHexString(SHA256.HashData(resource.Data.Span));
		return $"{kind}:{hash}";
	}

	public static DecodedArtwork Get(RenderResource resource)
	{
		var key = KeyOf(resource);
		var owner = resource.Owner
			?? (MemoryMarshal.TryGetArray(resource.Data, out var segment) ? segment.Array : null);
		Entry entry;
		lock (_gate)
		{
			Sweep();
			if (_entries.TryGetValue(key, out var found))
			{
				entry = found;
				_recent.Remove(entry.Node);
			}
			else
			{
				var data = resource.Data;
				entry = new Entry(key, new Lazy<DecodedArtwork>(() => Decode(key, data)));
				_entries[key] = entry;
			}

			_recent.AddFirst(entry.Node);
			entry.Own(owner);
		}

		var decoded = entry.Value.Value;
		lock (_gate)
		{
			if (!entry.Counted && entry.Node.List is not null)
			{
				entry.Counted = true;
				entry.Size = SizeOf(decoded, resource.Data.Length);
				_bytes += entry.Size;
				Trim(entry);
			}
		}

		return decoded;
	}

	private static DecodedArtwork Decode(string key, ReadOnlyMemory<byte> data)
	{
		Decoded?.Invoke(key);
		try
		{
			if (key.StartsWith("svg:", StringComparison.Ordinal))
			{
				var svg = new SKSvg();
				using var stream = MemoryMarshal.TryGetArray(data, out var segment)
					? new MemoryStream(segment.Array!, segment.Offset, segment.Count, false)
					: new MemoryStream(data.ToArray(), false);
				var picture = svg.Load(stream);
				return new DecodedArtwork(picture, null, svg, svg.HasAnimations ? Motion(data) : null);
			}

			return new DecodedArtwork(null, AnimatedImageDecoder.Decode(data.Span));
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			return new DecodedArtwork(null, null);
		}
	}

	private static SvgMotion? Motion(ReadOnlyMemory<byte> data)
	{
		try
		{
			return SvgMotion.Create(data);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			return null;
		}
	}

	private static long SizeOf(DecodedArtwork decoded, int sourceLength)
	{
		var frames = decoded.Raster?.Frames.Sum(frame => (long)frame.Length) ?? 0;
		var documents = decoded.Motion is null ? 1 : 2;
		return Math.Max(1, frames + sourceLength * documents);
	}

	private static void Trim(Entry keep)
	{
		while (_bytes > BudgetBytes && _recent.Last is { } last && last.Value != keep)
		{
			Remove(last.Value);
		}
	}

	private static void Sweep()
	{
		var now = Environment.TickCount64;
		if (now - _sweptAt < SweepIntervalMs)
		{
			return;
		}

		_sweptAt = now;
		foreach (var entry in _recent.Where(entry => entry.Abandoned).ToList())
		{
			Remove(entry);
		}
	}

	private static void Remove(Entry entry)
	{
		if (entry.Node.List is not null)
		{
			_recent.Remove(entry.Node);
		}

		if (_entries.TryGetValue(entry.Key, out var current) && current == entry)
		{
			_entries.Remove(entry.Key);
		}

		if (entry.Counted)
		{
			_bytes -= entry.Size;
			entry.Counted = false;
		}
	}

	private sealed class Entry
	{
		private readonly List<WeakReference<object>> _owners = [];

		public Entry(string key, Lazy<DecodedArtwork> value)
		{
			Key = key;
			Value = value;
			Node = new LinkedListNode<Entry>(this);
		}

		public string Key { get; }

		public Lazy<DecodedArtwork> Value { get; }

		public LinkedListNode<Entry> Node { get; }

		public long Size { get; set; }

		public bool Counted { get; set; }

		public bool Abandoned => _owners.Count > 0 && _owners.TrueForAll(owner => !owner.TryGetTarget(out _));

		public void Own(object? owner)
		{
			if (owner is null)
			{
				return;
			}

			_owners.RemoveAll(existing => !existing.TryGetTarget(out _));
			if (!_owners.Exists(existing => existing.TryGetTarget(out var target) && ReferenceEquals(target, owner)))
			{
				_owners.Add(new WeakReference<object>(owner));
			}
		}
	}
}
