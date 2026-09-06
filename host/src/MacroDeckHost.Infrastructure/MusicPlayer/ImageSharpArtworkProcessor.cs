using MacroDeckHost.Application.MusicPlayer;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
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
		using var image = Image.Load(bytes);
		while (image.Frames.Count > 1)
		{
			image.Frames.RemoveFrame(1);
		}

		if (Math.Max(image.Width, image.Height) > MaxMasterEdge)
		{
			image.Mutate(ctx => ctx.Resize(new ResizeOptions
			{
				Mode = ResizeMode.Max,
				Size = new Size(MaxMasterEdge, MaxMasterEdge)
			}));
		}

		var encoder = new WebpEncoder { FileFormat = WebpFileFormatType.Lossy, Quality = Quality };
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

		return new ProcessedArtworkResult(master, variants);
	}

	private static async Task<byte[]> EncodeToBytes(Image image,
		WebpEncoder encoder,
		CancellationToken cancellationToken)
	{
		using var stream = new MemoryStream();
		await image.SaveAsync(stream, encoder, cancellationToken);
		return stream.ToArray();
	}
}
