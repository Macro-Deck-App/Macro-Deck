using MacroDeckHost.Application.Icons;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Icons;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using SkiaSharp;
using Svg.Skia;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Icons;

public sealed class ImageSharpIconProcessor : IIconProcessor
{
	private const int MaxOriginalBytes = 64 * 1024 * 1024;
	private const int MaxMasterEdge = 1024;

	private const int MaxLottieMasterEdge = 512;

	private const int StaticQuality = 85;
	private const int AnimatedQuality = 75;
	private const string SvgFormatName = "Svg";
	private const string LottieFormatName = "Lottie";

	private static readonly SemaphoreSlim _lottieGate = new(1, 1);

	private readonly ILogger _logger;

	public ImageSharpIconProcessor(ILogger logger)
	{
		_logger = logger;
	}

	public async Task<Result<ProcessedIconResult, IconError>> Process(Stream original,
		string originalFileName,
		CancellationToken cancellationToken)
	{
		byte[] originalBytes;
		try
		{
			originalBytes = await ReadAllBytes(original, cancellationToken);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			return Result.Fail<ProcessedIconResult, IconError>(IconError.StorageFailure, ex.Message);
		}

		if (originalBytes.Length == 0)
		{
			return Result.Fail<ProcessedIconResult, IconError>(IconError.UnsupportedFormat, "File is empty");
		}

		if (originalBytes.Length > MaxOriginalBytes)
		{
			return Result.Fail<ProcessedIconResult, IconError>(IconError.ValidationError, "File is too large");
		}

		var sourceHash = SourceContentHash.Compute(originalBytes);

		if (LottieAnimationRenderer.LooksLikeLottie(originalFileName, originalBytes))
		{
			try
			{
				return await EncodeLottie(originalBytes, sourceHash, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				_logger.Warning(ex, "Failed to render Lottie animation {FileName}", originalFileName);
				return Result.Fail<ProcessedIconResult, IconError>(IconError.ProcessingFailed, ex.Message);
			}
		}

		var decodeBytes = originalBytes;
		string? originalFormat = null;
		if (LooksLikeSvg(originalFileName, originalBytes))
		{
			var rasterized = TryRasterizeSvg(originalBytes);
			if (rasterized is null)
			{
				return Result.Fail<ProcessedIconResult, IconError>(IconError.UnsupportedFormat, "Invalid SVG file");
			}

			decodeBytes = rasterized;
			originalFormat = SvgFormatName;
		}

		try
		{
			return await Encode(decodeBytes, originalFormat, sourceHash, cancellationToken);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (UnknownImageFormatException ex)
		{
			return Result.Fail<ProcessedIconResult, IconError>(IconError.UnsupportedFormat, ex.Message);
		}
		catch (InvalidImageContentException ex)
		{
			return Result.Fail<ProcessedIconResult, IconError>(IconError.UnsupportedFormat, ex.Message);
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Failed to process icon {FileName}", originalFileName);
			return Result.Fail<ProcessedIconResult, IconError>(IconError.ProcessingFailed, ex.Message);
		}
	}

	private static async Task<Result<ProcessedIconResult, IconError>> Encode(byte[] decodeBytes,
		string? originalFormat,
		SourceContentHash sourceHash,
		CancellationToken cancellationToken)
	{
		using var image = LoadImage(decodeBytes);
		originalFormat ??= image.Metadata.DecodedImageFormat?.Name ?? (LooksLikeGif(decodeBytes) ? "GIF" : "Unknown");

		var isAnimated = image.Frames.Count > 1;
		if (isAnimated)
		{
			CopyAnimationMetadata(image);
		}

		if (Math.Max(image.Width, image.Height) > MaxMasterEdge)
		{
			image.Mutate(ctx => ctx.Resize(new ResizeOptions
			{
				Mode = ResizeMode.Max,
				Size = new Size(MaxMasterEdge, MaxMasterEdge)
			}));
		}

		// Animated icons are lossy like static ones, and at a cheaper encoder effort. Lossless was the
		// original choice, and it does not survive a real animation: a 180-frame 400px GIF took 26 s
		// to encode and its 256px rendition came out at 11.7 MB - larger than the master, because
		// resampling turns palette-flat frames into noise lossless cannot compress. Lossy at
		// Level2 encodes the same icon in 4 s with a 0.8 MB rendition, and the alpha plane stays exact,
		// so a transparent frame still clears the one before it.
		var encoder = isAnimated
			? new WebpEncoder
			{
				FileFormat = WebpFileFormatType.Lossy,
				Quality = AnimatedQuality,
				Method = WebpEncodingMethod.Level2
			}
			: new WebpEncoder { FileFormat = WebpFileFormatType.Lossy, Quality = StaticQuality };

		var masterWebp = await EncodeToBytes(image, encoder, cancellationToken);

		var variants = new Dictionary<int, byte[]>();
		foreach (var size in IconVariants.TargetSizes)
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

		var result = new ProcessedIconResult(masterWebp,
			variants,
			image.Width,
			image.Height,
			isAnimated,
			isAnimated ? image.Frames.Count : null,
			originalFormat,
			sourceHash);

		return Result.Ok<ProcessedIconResult, IconError>(result);
	}

	private static async Task<Result<ProcessedIconResult, IconError>> EncodeLottie(byte[] originalBytes,
		SourceContentHash sourceHash,
		CancellationToken cancellationToken)
	{
		var opened = LottieAnimationRenderer.Open(originalBytes);
		if (!opened.Success)
		{
			return Result.Fail<ProcessedIconResult, IconError>(opened.Error!.Value, opened.ErrorMessage);
		}

		using var renderer = opened.Data!;
		var encoder = new WebpEncoder { FileFormat = WebpFileFormatType.Lossy, Quality = StaticQuality };

		await _lottieGate.WaitAsync(cancellationToken);
		try
		{
			byte[] masterWebp;
			int width;
			int height;
			int frameCount;
			using (var master = renderer.Render(MaxLottieMasterEdge, cancellationToken))
			{
				masterWebp = await EncodeToBytes(master, encoder, cancellationToken);
				width = master.Width;
				height = master.Height;
				frameCount = master.Frames.Count;
			}

			var variants = new Dictionary<int, byte[]>();
			foreach (var size in IconVariants.TargetSizes)
			{
				if (size >= Math.Max(width, height))
				{
					continue;
				}

				using var variant = renderer.Render(size, cancellationToken);
				variants[size] = await EncodeToBytes(variant, encoder, cancellationToken);
			}

			var isAnimated = frameCount > 1;
			var result = new ProcessedIconResult(masterWebp,
				variants,
				width,
				height,
				isAnimated,
				isAnimated ? frameCount : null,
				LottieFormatName,
				sourceHash);

			return Result.Ok<ProcessedIconResult, IconError>(result);
		}
		finally
		{
			_lottieGate.Release();
		}
	}

	private static Image LoadImage(byte[] bytes)
	{
		try
		{
			return Image.Load(bytes);
		}
		catch (Exception) when (LooksLikeGif(bytes))
		{
			var fallback = SkiaAnimatedGifDecoder.TryDecode(bytes);
			if (fallback is not null)
			{
				return fallback;
			}

			throw;
		}
	}

	private static bool LooksLikeGif(byte[] bytes)
		=> bytes.Length > 3 && bytes[0] == (byte)'G' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F';

	private static async Task<byte[]> EncodeToBytes(Image image,
		WebpEncoder encoder,
		CancellationToken cancellationToken)
	{
		using var stream = new MemoryStream();
		await image.SaveAsync(stream, encoder, cancellationToken);
		return stream.ToArray();
	}

	private static void CopyAnimationMetadata(Image image)
	{
		var isGif = image.Metadata.DecodedImageFormat is GifFormat;
		if (isGif)
		{
			image.Metadata.GetWebpMetadata().RepeatCount = image.Metadata.GetGifMetadata().RepeatCount;
		}

		foreach (var frame in image.Frames)
		{
			var webpFrame = frame.Metadata.GetWebpMetadata();
			if (isGif)
			{
				webpFrame.FrameDelay = (uint)(frame.Metadata.GetGifMetadata().FrameDelay * 10);
			}

			webpFrame.BlendMethod = WebpBlendMethod.Source;
			webpFrame.DisposalMethod = WebpDisposalMethod.DoNotDispose;
		}
	}

	private static bool LooksLikeSvg(string fileName, byte[] bytes)
	{
		if (Path.GetExtension(fileName).Equals(".svg", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		var prefix = System.Text.Encoding.UTF8
			.GetString(bytes, 0, Math.Min(bytes.Length, 256))
			.TrimStart('﻿', ' ', '\t', '\r', '\n');
		return prefix.StartsWith("<svg", StringComparison.OrdinalIgnoreCase) ||
			prefix.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase);
	}

	private static byte[]? TryRasterizeSvg(byte[] bytes)
	{
		using var svg = new SKSvg();
		using var stream = new MemoryStream(bytes);
		SKPicture? picture;
		try
		{
			picture = svg.Load(stream);
		}
		catch (Exception)
		{
			return null;
		}

		if (picture is null)
		{
			return null;
		}

		var rect = picture.CullRect;
		if (rect.Width <= 0 || rect.Height <= 0)
		{
			return null;
		}

		var scale = MaxMasterEdge / Math.Max(rect.Width, rect.Height);
		var width = Math.Max(1, (int)MathF.Round(rect.Width * scale));
		var height = Math.Max(1, (int)MathF.Round(rect.Height * scale));

		var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
		using var surface = SKSurface.Create(info);
		if (surface is null)
		{
			return null;
		}

		var canvas = surface.Canvas;
		canvas.Clear(SKColors.Transparent);
		canvas.Scale(scale);
		canvas.Translate(-rect.Left, -rect.Top);
		canvas.DrawPicture(picture);
		canvas.Flush();

		using var snapshot = surface.Snapshot();
		using var data = snapshot.Encode(SKEncodedImageFormat.Png, 100);
		return data.ToArray();
	}

	private static async Task<byte[]> ReadAllBytes(Stream stream, CancellationToken cancellationToken)
	{
		using var buffer = new MemoryStream();
		await stream.CopyToAsync(buffer, cancellationToken);
		return buffer.ToArray();
	}
}
