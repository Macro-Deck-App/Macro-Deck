using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.PixelFormats;
using SkiaSharp;
using SkiaSharp.Resources;
using SkiaSharp.Skottie;

namespace MacroDeckHost.Infrastructure.Icons;

internal sealed class LottieAnimationRenderer : IDisposable
{
	private const double MaxFps = 30;

	private const int MaxFrames = 240;

	private const int MaxDurationSeconds = (int)(MaxFrames / MaxFps);

	private const int MaxArchiveEntries = 512;

	private const long MaxArchiveEntryBytes = 32L * 1024 * 1024;

	private const long MaxExtractedBytes = 64L * 1024 * 1024;

	private static readonly HashSet<string> _assetExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".ttf", ".otf", ".woff", ".woff2"
	};

	private readonly Animation _animation;
	private readonly ResourceProvider _resourceProvider;
	private readonly ResourceProvider? _assetProvider;
	private readonly string? _assetDirectory;
	private readonly double _fps;

	private LottieAnimationRenderer(Animation animation,
		ResourceProvider resourceProvider,
		ResourceProvider? assetProvider,
		string? assetDirectory,
		double fps,
		int frameCount)
	{
		_animation = animation;
		_resourceProvider = resourceProvider;
		_assetProvider = assetProvider;
		_assetDirectory = assetDirectory;
		_fps = fps;
		FrameCount = frameCount;
	}

	public int FrameCount { get; }

	public static bool LooksLikeLottie(string fileName, byte[] bytes)
		=> IsZip(bytes)
			? Path.GetExtension(fileName).Equals(".lottie", StringComparison.OrdinalIgnoreCase)
			: LooksLikeLottieJson(StripByteOrderMark(bytes));

	public static Result<LottieAnimationRenderer, IconError> Open(byte[] bytes)
	{
		byte[] animationJson;
		string? assetDirectory = null;

		if (IsZip(bytes))
		{
			var unpacked = TryUnpack(bytes);
			if (!unpacked.Success)
			{
				return Result.Fail<LottieAnimationRenderer, IconError>(unpacked.Error!.Value, unpacked.ErrorMessage);
			}

			(animationJson, assetDirectory) = unpacked.Data;
		}
		else
		{
			animationJson = bytes;
		}

		animationJson = StripByteOrderMark(animationJson);
		if (!LooksLikeLottieJson(animationJson))
		{
			DeleteDirectory(assetDirectory);
			return Result.Fail<LottieAnimationRenderer, IconError>(IconError.UnsupportedFormat,
				"Not a Lottie animation");
		}

		var assetProvider = assetDirectory is null
			? null
			: new FileResourceProvider(assetDirectory, preDecode: false);
		var resourceProvider = assetProvider is null
			? new DataUriResourceProvider(preDecode: false)
			: new DataUriResourceProvider(assetProvider, preDecode: false);

		Animation? animation = null;
		var handedOver = false;
		try
		{
			animation = TryBuild(animationJson, resourceProvider);
			if (animation is null || animation.Size.Width <= 0 || animation.Size.Height <= 0)
			{
				return Fail(IconError.UnsupportedFormat, "Not a Lottie animation");
			}

			var fps = animation.Fps > 0
				? Math.Min(animation.Fps, MaxFps)
				: MaxFps;

			var frameCount = Math.Max(1, (int)Math.Round(animation.Duration.TotalSeconds * fps));
			if (frameCount > MaxFrames)
			{
				return Fail(IconError.ValidationError,
					$"The Lottie animation is longer than {MaxDurationSeconds} seconds");
			}

			var renderer = new LottieAnimationRenderer(animation,
				resourceProvider,
				assetProvider,
				assetDirectory,
				fps,
				frameCount);
			handedOver = true;
			return Result.Ok<LottieAnimationRenderer, IconError>(renderer);
		}
		catch (Exception)
		{
			return Fail(IconError.UnsupportedFormat, "Not a readable Lottie animation");
		}
		finally
		{
			if (!handedOver)
			{
				animation?.Dispose();
				resourceProvider.Dispose();
				assetProvider?.Dispose();
				DeleteDirectory(assetDirectory);
			}
		}
	}

	private static Result<LottieAnimationRenderer, IconError> Fail(IconError error, string message)
		=> Result.Fail<LottieAnimationRenderer, IconError>(error, message);

	public Image<Rgba32> Render(int longestEdge, CancellationToken cancellationToken)
	{
		var size = _animation.Size;
		var scale = longestEdge / Math.Max(size.Width, size.Height);
		var width = Math.Max(1, (int)MathF.Round(size.Width * scale));
		var height = Math.Max(1, (int)MathF.Round(size.Height * scale));
		var destination = SKRect.Create(0, 0, width, height);
		var frameDelayMs = (uint)Math.Max(1, Math.Round(1000 / _fps));

		var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
		var readInfo = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);

		using var surface = SKSurface.Create(info);
		if (surface is null)
		{
			throw new InvalidOperationException("Failed to create a surface for the Lottie animation");
		}

		using var readBitmap = new SKBitmap(readInfo);
		var pixels = new byte[readInfo.BytesSize];
		Image<Rgba32>? image = null;
		try
		{
			for (var index = 0; index < FrameCount; index++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				_animation.SeekFrameTime(index / _fps);
				surface.Canvas.Clear(SKColors.Transparent);
				_animation.Render(surface.Canvas, destination);
				surface.Canvas.Flush();

				if (!surface.ReadPixels(readInfo, readBitmap.GetPixels(), readBitmap.RowBytes, 0, 0))
				{
					throw new InvalidOperationException("Failed to read back a Lottie frame");
				}

				Marshal.Copy(readBitmap.GetPixels(), pixels, 0, pixels.Length);

				if (image is null)
				{
					image = Image.LoadPixelData<Rgba32>(pixels, width, height);
					ApplyFrameMetadata(image.Frames[0].Metadata, frameDelayMs);
				}
				else
				{
					using var frameImage = Image.LoadPixelData<Rgba32>(pixels, width, height);
					var added = image.Frames.AddFrame(frameImage.Frames.RootFrame);
					ApplyFrameMetadata(added.Metadata, frameDelayMs);
				}
			}

			image!.Metadata.GetWebpMetadata().RepeatCount = 0;
			return image;
		}
		catch (Exception)
		{
			image?.Dispose();
			throw;
		}
	}

	public void Dispose()
	{
		_animation.Dispose();
		_resourceProvider.Dispose();
		_assetProvider?.Dispose();
		DeleteDirectory(_assetDirectory);
	}

	private static Animation? TryBuild(byte[] animationJson, ResourceProvider resourceProvider)
	{
		try
		{
			using var builder = Animation.CreateBuilder(AnimationBuilderFlags.PreferEmbeddedFonts);
			builder.SetResourceProvider(resourceProvider);
			builder.SetFontManager(SKFontManager.Default);
			using var data = SKData.CreateCopy(animationJson);
			return builder.Build(data);
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static void ApplyFrameMetadata(ImageFrameMetadata metadata, uint frameDelayMs)
	{
		var webpFrame = metadata.GetWebpMetadata();
		webpFrame.FrameDelay = frameDelayMs;
		webpFrame.BlendMethod = WebpBlendMethod.Source;
		webpFrame.DisposalMethod = WebpDisposalMethod.DoNotDispose;
	}

	private static byte[] StripByteOrderMark(byte[] bytes)
		=> bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
			? bytes[3..]
			: bytes;

	private static bool IsZip(byte[] bytes)
		=> bytes.Length > 4 && bytes[0] == 'P' && bytes[1] == 'K' && bytes[2] == 3 && bytes[3] == 4;

	private static Result<(byte[] Json, string? AssetDirectory), IconError> TryUnpack(byte[] bytes)
	{
		using var stream = new MemoryStream(bytes, writable: false);
		ZipArchive archive;
		try
		{
			archive = new ZipArchive(stream, ZipArchiveMode.Read);
		}
		catch (InvalidDataException)
		{
			return Result.Fail<(byte[], string?), IconError>(IconError.InvalidArchive,
				"The .lottie file is not a readable archive");
		}

		string? assetDirectory = null;
		try
		{
			var entries = archive.Entries.Take(MaxArchiveEntries).ToList();
			var animationEntry = FindAnimationEntry(entries);
			var json = animationEntry is null ? null : ReadEntry(animationEntry);
			if (json is null)
			{
				return Result.Fail<(byte[], string?), IconError>(IconError.UnsupportedFormat,
					"The .lottie file contains no animation");
			}

			long extractedBytes = 0;
			foreach (var entry in entries)
			{
				// Only the assets a Lottie layer can actually reference are unpacked, and the total stays
				// bounded: a small archive of highly compressible entries must not fill the temp directory.
				if (entry == animationEntry ||
					entry.Name.Length == 0 ||
					!IsRenderableAsset(entry.FullName))
				{
					continue;
				}

				var relative = SafeRelativePath(entry.FullName);
				var content = relative is null ? null : ReadEntry(entry);
				if (relative is null || content is null)
				{
					continue;
				}

				extractedBytes += content.Length;
				if (extractedBytes > MaxExtractedBytes)
				{
					break;
				}

				assetDirectory ??= CreateAssetDirectory();
				WriteAsset(assetDirectory, relative, content);
			}

			return Result.Ok<(byte[], string?), IconError>((json, assetDirectory));
		}
		catch (InvalidDataException)
		{
			DeleteDirectory(assetDirectory);
			return Result.Fail<(byte[], string?), IconError>(IconError.InvalidArchive,
				"The .lottie file is not a readable archive");
		}
		catch (Exception)
		{
			DeleteDirectory(assetDirectory);
			throw;
		}
		finally
		{
			archive.Dispose();
		}
	}

	private static void WriteAsset(string assetDirectory, string relativePath, byte[] content)
	{
		try
		{
			var target = Path.Combine(assetDirectory, relativePath);
			Directory.CreateDirectory(Path.GetDirectoryName(target)!);
			File.WriteAllBytes(target, content);
		}
		catch (Exception ex) when (ex is IOException
			or UnauthorizedAccessException
			or ArgumentException
			or NotSupportedException)
		{
		}
	}

	private static bool IsRenderableAsset(string fullName)
		=> _assetExtensions.Contains(Path.GetExtension(fullName));

	private static ZipArchiveEntry? FindAnimationEntry(List<ZipArchiveEntry> entries)
	{
		var manifest = entries.FirstOrDefault(e
			=> e.FullName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase));
		var animationId = manifest is null ? null : ReadFirstAnimationId(manifest);

		var byId = animationId is null
			? null
			: entries.FirstOrDefault(e => e.FullName.Equals($"animations/{animationId}.json",
				StringComparison.OrdinalIgnoreCase));

		return byId ??
			entries.FirstOrDefault(e
				=> e.FullName.StartsWith("animations/", StringComparison.OrdinalIgnoreCase) &&
				e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
	}

	private static string? ReadFirstAnimationId(ZipArchiveEntry manifest)
	{
		var bytes = ReadEntry(manifest);
		if (bytes is null)
		{
			return null;
		}

		try
		{
			using var document = JsonDocument.Parse(bytes);
			if (!document.RootElement.TryGetProperty("animations", out var animations) ||
				animations.ValueKind != JsonValueKind.Array)
			{
				return null;
			}

			foreach (var entry in animations.EnumerateArray())
			{
				if (entry.ValueKind == JsonValueKind.Object &&
					entry.TryGetProperty("id", out var id) &&
					id.ValueKind == JsonValueKind.String)
				{
					return id.GetString();
				}
			}
		}
		catch (JsonException)
		{
			return null;
		}

		return null;
	}

	private static string? SafeRelativePath(string fullName)
	{
		if (fullName.Contains("..", StringComparison.Ordinal) ||
			fullName.Contains(':', StringComparison.Ordinal) ||
			Path.IsPathRooted(fullName))
		{
			return null;
		}

		var normalized = fullName.Replace('\\', '/').TrimStart('/');
		return normalized.Length == 0 ? null : normalized.Replace('/', Path.DirectorySeparatorChar);
	}

	private static string CreateAssetDirectory()
	{
		var path = Path.Combine(Path.GetTempPath(), $"macrodeck-lottie-{Guid.NewGuid():N}");
		Directory.CreateDirectory(path);
		return path;
	}

	private static void DeleteDirectory(string? path)
	{
		if (path is null)
		{
			return;
		}

		try
		{
			Directory.Delete(path, recursive: true);
		}
		catch (Exception)
		{
		}
	}

	private static byte[]? ReadEntry(ZipArchiveEntry entry)
	{
		if (entry.Length > MaxArchiveEntryBytes)
		{
			return null;
		}

		try
		{
			using var stream = entry.Open();
			using var buffer = new MemoryStream();
			// The declared length is not trustworthy, so the copy itself is bounded as well.
			var copied = CopyBounded(stream,
				buffer,
				MaxArchiveEntryBytes);
			return copied ? buffer.ToArray() : null;
		}
		catch (InvalidDataException)
		{
			return null;
		}
	}

	private static bool CopyBounded(Stream source, Stream destination, long maxBytes)
	{
		var buffer = new byte[81920];
		long total = 0;
		int read;
		while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
		{
			total += read;
			if (total > maxBytes)
			{
				return false;
			}

			destination.Write(buffer, 0, read);
		}

		return true;
	}

	private static bool LooksLikeLottieJson(byte[] bytes)
	{
		try
		{
			using var document = JsonDocument.Parse(bytes);
			var root = document.RootElement;
			return root.ValueKind == JsonValueKind.Object &&
				root.TryGetProperty("v", out _) &&
				root.TryGetProperty("layers", out var layers) &&
				layers.ValueKind == JsonValueKind.Array &&
				HasNumber(root, "fr") &&
				HasNumber(root, "ip") &&
				HasNumber(root, "op");
		}
		catch (Exception)
		{
			return false;
		}
	}

	private static bool HasNumber(JsonElement element, string name)
		=> element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number;
}
