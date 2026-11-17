using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Infrastructure.Icons;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.MusicPlayer;

public sealed class ImageSharpArtworkProcessor : IArtworkProcessor
{
	private const int MaxOriginalBytes = 32 * 1024 * 1024;
	private const int MaxMasterEdge = 640;
	private const int Quality = 85;

	private readonly ILogger _logger;

	public ImageSharpArtworkProcessor(ILogger logger)
	{
		_logger = logger;
	}

	public async Task<ProcessedArtworkResult?> Process(byte[] original, CancellationToken cancellationToken)
	{
		if (original.Length == 0 || original.Length > MaxOriginalBytes)
		{
			return null;
		}

		try
		{
			return await Encode(original, cancellationToken);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Artwork could not be re-encoded, falling back to original bytes");
			return null;
		}
	}

	private static async Task<ProcessedArtworkResult> Encode(byte[] bytes, CancellationToken cancellationToken)
	{
		using var image = Image.Load<Rgba32>(ImageSharpDecoding.Options, bytes);
		while (image.Frames.Count > 1)
		{
			image.Frames.RemoveFrame(1);
		}

		// Bake the EXIF orientation into the pixels so every client draws the cover the same way.
		image.Mutate(ctx => ctx.AutoOrient());
		image.Metadata.ExifProfile = null;

		if (Math.Max(image.Width, image.Height) > MaxMasterEdge)
		{
			image.Mutate(ctx => ctx.Resize(new ResizeOptions
			{
				Mode = ResizeMode.Max,
				Size = new Size(MaxMasterEdge, MaxMasterEdge)
			}));
		}

		// Never WebP: every client gets this one image and Safari before 14 draws WebP blank.
		var opaque = IsOpaque(image);
		IImageEncoder encoder = opaque ? new JpegEncoder { Quality = Quality } : new PngEncoder();
		var contentType = opaque ? "image/jpeg" : "image/png";
		var master = await EncodeToBytes(image, encoder, cancellationToken);

		var variants = new Dictionary<int, byte[]>();
		foreach (var size in ArtworkVariants.TargetSizes)
		{
			if (size >= Math.Max(image.Width, image.Height))
			{
				continue;
			}

			using var variant = image.Clone(ctx => ctx.Resize(new ResizeOptions
			{
				Mode = ResizeMode.Max,
				Size = new Size(size, size)
			}));
			variants[size] = await EncodeToBytes(variant, encoder, cancellationToken);
		}

		return new ProcessedArtworkResult(master, variants, contentType);
	}

	private static bool IsOpaque(Image<Rgba32> image)
	{
		var opaque = true;
		image.ProcessPixelRows(accessor =>
		{
			for (var y = 0; y < accessor.Height && opaque; y++)
			{
				foreach (var pixel in accessor.GetRowSpan(y))
				{
					if (pixel.A != byte.MaxValue)
					{
						opaque = false;
						break;
					}
				}
			}
		});
		return opaque;
	}

	private static async Task<byte[]> EncodeToBytes(
		Image image,
		IImageEncoder encoder,
		CancellationToken cancellationToken)
	{
		using var stream = new MemoryStream();
		await image.SaveAsync(stream, encoder, cancellationToken);
		return stream.ToArray();
	}
}
