using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Domain.Entities;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Png;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Icons;

public sealed class ImageSharpIconFallbackStore : IIconImageFallbackStore, IDisposable
{
	private const string CacheDirectoryName = "fallback-cache";

	private readonly IIconStorage _storage;
	private readonly IMacroDeckPaths _paths;
	private readonly ILogger _logger;
	private readonly SemaphoreSlim _transcodeLock = new(1, 1);

	public ImageSharpIconFallbackStore(IIconStorage storage, IMacroDeckPaths paths, ILogger logger)
	{
		_storage = storage;
		_paths = paths;
		_logger = logger;
	}

	public async Task<FallbackIconImage?> GetOrCreate(IconEntity icon,
		string variant,
		bool staticFrame,
		CancellationToken cancellationToken)
	{
		var animated = icon.IsAnimated && !staticFrame;
		var extension = animated ? ".gif" : ".png";
		var contentType = animated ? "image/gif" : "image/png";
		var cacheName = icon.IsAnimated && staticFrame
			? $"{icon.Id:N}-{variant}-static{extension}"
			: $"{icon.Id:N}-{variant}{extension}";
		var cachePath = Path.Combine(_paths.IconsDirectory, CacheDirectoryName, cacheName);

		if (File.Exists(cachePath))
		{
			return new FallbackIconImage(OpenRead(cachePath), contentType, extension);
		}

		await _transcodeLock.WaitAsync(cancellationToken);
		try
		{
			if (!File.Exists(cachePath) && !await Transcode(icon, variant, animated, cachePath, cancellationToken))
			{
				return null;
			}
		}
		finally
		{
			_transcodeLock.Release();
		}

		return new FallbackIconImage(OpenRead(cachePath), contentType, extension);
	}

	private async Task<bool> Transcode(IconEntity icon,
		string variant,
		bool animated,
		string cachePath,
		CancellationToken cancellationToken)
	{
		var source = _storage.OpenVariant(icon.PackId, icon.Id, variant) ??
			_storage.OpenVariant(icon.PackId, icon.Id, IconVariants.Master);
		if (source is null)
		{
			return false;
		}

		try
		{
			using var image = await Image.LoadAsync(source, cancellationToken);
			Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

			var tempPath = cachePath + ".tmp";
			await using (var output = File.Create(tempPath))
			{
				if (animated)
				{
					AnimatedGifTranscode.PrepareForGif(image);
					await image.SaveAsGifAsync(output, new GifEncoder(), cancellationToken);
				}
				else if (image.Frames.Count > 1)
				{
					using var firstFrame = image.Frames.CloneFrame(0);
					await firstFrame.SaveAsPngAsync(output, new PngEncoder(), cancellationToken);
				}
				else
				{
					await image.SaveAsPngAsync(output, new PngEncoder(), cancellationToken);
				}
			}

			File.Move(tempPath, cachePath, overwrite: true);
			return true;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.Warning(ex,
				"Failed to transcode icon {IconId} variant {Variant} for legacy browsers",
				icon.Id,
				variant);
			return false;
		}
		finally
		{
			await source.DisposeAsync();
		}
	}

	private static FileStream OpenRead(string path)
		=> new(path,
			FileMode.Open,
			FileAccess.Read,
			FileShare.Read,
			bufferSize: 64 * 1024,
			FileOptions.Asynchronous | FileOptions.SequentialScan);

	public void Dispose()
	{
		_transcodeLock.Dispose();
	}
}
