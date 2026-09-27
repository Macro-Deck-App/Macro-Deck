using System.Collections.Concurrent;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Icons;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Processing;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Icons;

public sealed class ImageSharpIconVariantDeriver : IIconVariantDeriver, IDisposable
{
	private const long MaxMasterBytes = 64 * 1024 * 1024;
	private const long DefaultMaxDecodedPixels = 128L * 1024 * 1024;
	private static readonly TimeSpan _failureBackoff = TimeSpan.FromMinutes(10);
	private static readonly TimeSpan _mismatchBackoff = TimeSpan.FromMinutes(1);

	private readonly IIconStorage _storage;
	private readonly IIconPackCache _iconPackCache;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;
	private readonly long _maxDecodedPixels;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly ConcurrentDictionary<(Guid IconId, string Variant), Refusal> _refusals = new();

	public ImageSharpIconVariantDeriver(IIconStorage storage,
		IIconPackCache iconPackCache,
		TimeProvider timeProvider,
		ILogger logger)
		: this(storage, iconPackCache, timeProvider, logger, DefaultMaxDecodedPixels)
	{
	}

	internal ImageSharpIconVariantDeriver(IIconStorage storage,
		IIconPackCache iconPackCache,
		TimeProvider timeProvider,
		ILogger logger,
		long maxDecodedPixels)
	{
		_storage = storage;
		_iconPackCache = iconPackCache;
		_timeProvider = timeProvider;
		_logger = logger;
		_maxDecodedPixels = maxDecodedPixels;
	}

	public async Task<DerivedVariant> GetOrCreate(IconEntity icon,
		string masterContentHash,
		int size,
		CancellationToken cancellationToken)
	{
		var variant = IconVariants.Derived(size, masterContentHash);
		var key = (icon.Id, variant);
		if (_refusals.TryGetValue(key, out var refusal))
		{
			if (refusal.RetryAt is null || _timeProvider.GetUtcNow() < refusal.RetryAt)
			{
				return refusal.Result;
			}

			_refusals.TryRemove(key, out _);
		}

		if (Open(icon, variant) is { } cached)
		{
			return cached;
		}

		var (master, tooLarge) = await ReadMaster(icon, cancellationToken);
		if (tooLarge)
		{
			return Refuse(key, DerivedVariant.NotApplicable, retryAfter: null);
		}

		if (master is null)
		{
			return DerivedVariant.Unavailable;
		}

		if (MasterContentHash.Compute(master).Value != masterContentHash)
		{
			return Refuse(key, DerivedVariant.Unavailable, _mismatchBackoff);
		}

		await _gate.WaitAsync(cancellationToken);
		try
		{
			return Open(icon, variant) ?? await Derive(icon, masterContentHash, size, variant, master);
		}
		finally
		{
			_gate.Release();
		}
	}

	// Runs without the request's token: an aborted prefetch would otherwise throw away a finished encode.
	private async Task<DerivedVariant> Derive(IconEntity icon,
		string masterContentHash,
		int size,
		string variant,
		byte[] master)
	{
		var key = (icon.Id, variant);
		try
		{
			// Masters come from imported archives, so the decoded size of every frame is bounded before decoding.
			var info = Image.Identify(master);
			var framePixels = (long)info.Width * info.Height;
			var frames = Math.Max(1, info.FrameMetadataCollection.Count);
			if (Math.Max(info.Width, info.Height) <= size || framePixels * frames > _maxDecodedPixels)
			{
				return Refuse(key, DerivedVariant.NotApplicable, retryAfter: null);
			}

			using var image = Image.Load(new DecoderOptions { MaxFrames = (uint)(_maxDecodedPixels / framePixels) }, master);
			var encoder = IconWebpEncoding.For(isAnimated: image.Frames.Count > 1);
			image.Mutate(ctx => ctx.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(size, size) }));
			var bytes = await IconWebpEncoding.EncodeToBytes(image, encoder, CancellationToken.None);

			if (!await _storage.WriteVariantIfIconExists(icon.PackId, icon.Id, variant, bytes, CancellationToken.None))
			{
				return DerivedVariant.Unavailable;
			}

			if (!MasterExists(icon))
			{
				_storage.DeleteVariant(icon.PackId, icon.Id, variant);
				return DerivedVariant.Unavailable;
			}

			DeleteStaleDerivations(icon, masterContentHash);
			return Open(icon, variant) ?? DerivedVariant.Unavailable;
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Failed to derive the {Size}px variant of icon {IconId}", size, icon.Id);
			return Refuse(key, DerivedVariant.Unavailable, _failureBackoff);
		}
	}

	private void DeleteStaleDerivations(IconEntity icon, string masterContentHash)
	{
		if (_iconPackCache.GetIconById(icon.Id)?.MasterContentHash != masterContentHash)
		{
			return;
		}

		var current = "-" + IconVariants.MasterToken(masterContentHash);
		foreach (var stale in _storage.ListVariants(icon.PackId, icon.Id))
		{
			if (stale.Contains('-', StringComparison.Ordinal) && !stale.EndsWith(current, StringComparison.Ordinal))
			{
				_storage.DeleteVariant(icon.PackId, icon.Id, stale);
			}
		}
	}

	private DerivedVariant? Open(IconEntity icon, string variant)
		=> _storage.OpenVariant(icon.PackId, icon.Id, variant) is { } stream
			? new DerivedVariant(DerivedVariantOutcome.Derived, variant, stream)
			: null;

	private bool MasterExists(IconEntity icon)
	{
		using var stream = _storage.OpenVariant(icon.PackId, icon.Id, IconVariants.Master);
		return stream is not null;
	}

	private async Task<(byte[]? Bytes, bool TooLarge)> ReadMaster(IconEntity icon, CancellationToken cancellationToken)
	{
		try
		{
			await using var stream = _storage.OpenVariant(icon.PackId, icon.Id, IconVariants.Master);
			if (stream is null)
			{
				return (null, false);
			}

			if (stream.Length > MaxMasterBytes)
			{
				return (null, true);
			}

			using var buffer = new MemoryStream((int)stream.Length);
			await stream.CopyToAsync(buffer, cancellationToken);
			return (buffer.ToArray(), false);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			_logger.Debug(ex, "Could not read the master of icon {IconId}", icon.Id);
			return (null, false);
		}
	}

	private DerivedVariant Refuse((Guid IconId, string Variant) key, DerivedVariant result, TimeSpan? retryAfter)
	{
		_refusals[key] = new Refusal(result, retryAfter is null ? null : _timeProvider.GetUtcNow() + retryAfter);
		return result;
	}

	public void Dispose()
	{
		_gate.Dispose();
	}

	private sealed record Refusal(DerivedVariant Result, DateTimeOffset? RetryAt);
}
