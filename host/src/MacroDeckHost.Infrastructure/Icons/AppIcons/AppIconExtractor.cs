using MacroDeckHost.Application.Icons;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Integrations.System.DesktopEntries;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Icons.AppIcons;

public sealed class AppIconExtractor : IAppIconExtractor
{
	private const int MaxSourceBytes = 64 * 1024 * 1024;
	private const int MaxShortcutBytes = 1024 * 1024;
	private const int MaxShortcutLines = 2048;
	private const int MaxIndirections = 4;
	private const string FallbackName = "icon";

	private static readonly HashSet<string> _containerExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".ico", ".icns", ".exe", ".dll"
	};

	private readonly ILogger _logger;

	public AppIconExtractor(ILogger logger)
	{
		_logger = logger;
	}

	public bool CanExtract(string path) => IconImportFiles.IsIconDropSource(path);

	// .lnk/.url/.desktop are pure path indirection: a shortcut only resolves on the machine it came
	// from, and uploaded bytes may point at a path that does not exist on this host.
	public bool CanExtractContent(string fileName)
		=> _containerExtensions.Contains(Path.GetExtension(fileName));

	public async Task<Result<ExtractedAppIcon, IconError>> ExtractFromContent(string fileName,
		Stream content,
		CancellationToken cancellationToken)
	{
		// The incoming stream is a forward-only multipart section: no Length, Position, or Seek. Buffer it
		// up front so ReadIcon's file-based helpers never have to touch the original stream.
		using var buffered = new MemoryStream();
		var copyResult = await CopyWithLimit(content, buffered, cancellationToken);
		if (!copyResult)
		{
			return Result.Fail<ExtractedAppIcon, IconError>(IconError.ValidationError, "File is too large");
		}

		var bytes = buffered.ToArray();
		var name = IconName(fileName);
		var extension = Path.GetExtension(fileName).ToLowerInvariant();
		byte[]? image;
		try
		{
			image = extension switch
			{
				".exe" or ".dll" => ReadExecutableIcon(bytes),
				".ico" => IcoImageDecoder.TryDecodeToPng(bytes),
				".icns" => IcnsIconReader.TryExtractBestIcon(bytes),
				_ => null
			};
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.Warning(ex, "Failed to read the icon of {FileName}", fileName);
			return Result.Fail<ExtractedAppIcon, IconError>(IconError.StorageFailure, ex.Message);
		}

		if (image is null || image.Length == 0)
		{
			return Result.Fail<ExtractedAppIcon, IconError>(IconError.UnsupportedFormat,
				$"No icon could be read from {FileName(fileName)}");
		}

		return Result.Ok<ExtractedAppIcon, IconError>(new ExtractedAppIcon(image, name + ".png"));
	}

	private static async Task<bool> CopyWithLimit(Stream source, MemoryStream destination, CancellationToken ct)
	{
		var buffer = new byte[81920];
		long total = 0;
		int read;
		while ((read = await source.ReadAsync(buffer, ct)) > 0)
		{
			total += read;
			if (total > MaxSourceBytes)
			{
				return false;
			}

			await destination.WriteAsync(buffer.AsMemory(0, read), ct);
		}

		return true;
	}

	private static byte[]? ReadExecutableIcon(byte[] bytes)
	{
		using var stream = new MemoryStream(bytes, writable: false);
		return ReadExecutableIcon(stream);
	}

	private static byte[]? ReadExecutableIcon(Stream stream)
	{
		var extracted = PortableExecutableIconReader.TryExtractBestIcon(stream);
		if (extracted is null)
		{
			return null;
		}

		return IcoImageDecoder.LooksLikeIco(extracted) ? IcoImageDecoder.TryDecodeToPng(extracted) : extracted;
	}

	public async Task<Result<ExtractedAppIcon, IconError>> Extract(string path,
		CancellationToken cancellationToken)
	{
		string? source;
		try
		{
			source = ResolveSource(path);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.Warning(ex, "Failed to resolve the icon source of {Path}", path);
			return Result.Fail<ExtractedAppIcon, IconError>(IconError.StorageFailure, ex.Message);
		}

		if (source is null)
		{
			return Result.Fail<ExtractedAppIcon, IconError>(IconError.UnsupportedFormat,
				$"No icon could be read from {FileName(path)}");
		}

		try
		{
			return await ReadIcon(source, IconName(path), cancellationToken);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Failed to read the icon of {Path}", source);
			return Result.Fail<ExtractedAppIcon, IconError>(IconError.StorageFailure, ex.Message);
		}
	}

	private string? ResolveSource(string path)
	{
		var current = IconImportFiles.TrimTrailingSeparators(path);
		for (var hop = 0; hop <= MaxIndirections; hop++)
		{
			if (Directory.Exists(current))
			{
				if (!MacAppBundleIconLocator.IsAppBundle(current))
				{
					return null;
				}

				var bundleIcon = MacAppBundleIconLocator.TryResolveIconFile(current);
				if (bundleIcon is null)
				{
					return null;
				}

				current = bundleIcon;
				continue;
			}

			if (!File.Exists(current))
			{
				return null;
			}

			var next = Path.GetExtension(current).ToLowerInvariant() switch
			{
				".lnk" => ResolveShellLink(current),
				".url" => ResolveInternetShortcut(current),
				".desktop" => ResolveDesktopEntry(current),
				_ => null
			};

			if (next is null)
			{
				return CarriesImageBytes(current) ? current : null;
			}

			current = IconImportFiles.TrimTrailingSeparators(next);
		}

		return null;
	}

	private static bool CarriesImageBytes(string path)
		=> IconImportFiles.IsSupportedImportEntry(path) ||
			_containerExtensions.Contains(Path.GetExtension(path));

	private string? ResolveShellLink(string path)
	{
		if (new FileInfo(path).Length > MaxShortcutBytes)
		{
			_logger.Debug("{Path} is too large to be a shell link", path);
			return null;
		}

		var link = WindowsShellLinkParser.TryParse(File.ReadAllBytes(path));
		if (link is null)
		{
			_logger.Debug("{Path} is not a readable shell link", path);
			return null;
		}

		// An explicit icon location wins: a shortcut may deliberately carry an icon its target does not.
		if (link.IconLocation is not null && File.Exists(link.IconLocation))
		{
			return link.IconLocation;
		}

		return link.TargetPath;
	}

	private static string? ResolveInternetShortcut(string path)
		=> InternetShortcutParser.TryReadIconFile(ReadCappedLines(path));

	private static string? ResolveDesktopEntry(string path)
	{
		var iconName = DesktopEntryParser.TryReadIconName(ReadCappedLines(path));
		return iconName is null ? null : XdgIconResolver.TryResolve(iconName, XdgIconResolver.DefaultSearchRoots());
	}

	private static IEnumerable<string> ReadCappedLines(string path)
		=> File.ReadLines(path).Take(MaxShortcutLines);

	private static async Task<Result<ExtractedAppIcon, IconError>> ReadIcon(string source,
		string name,
		CancellationToken cancellationToken)
	{
		var extension = Path.GetExtension(source);
		if (IconImportFiles.IsSupportedImportEntry(source))
		{
			if (new FileInfo(source).Length > MaxSourceBytes)
			{
				return Result.Fail<ExtractedAppIcon, IconError>(IconError.ValidationError, "File is too large");
			}

			var bytes = await File.ReadAllBytesAsync(source, cancellationToken);
			return Result.Ok<ExtractedAppIcon, IconError>(new ExtractedAppIcon(bytes, name + extension));
		}

		var image = extension.ToLowerInvariant() switch
		{
			".exe" or ".dll" => ReadExecutableIcon(source),
			".ico" => IcoImageDecoder.TryDecodeToPng(await ReadCappedBytes(source, cancellationToken)),
			".icns" => IcnsIconReader.TryExtractBestIcon(await ReadCappedBytes(source, cancellationToken)),
			_ => null
		};

		if (image is null || image.Length == 0)
		{
			return Result.Fail<ExtractedAppIcon, IconError>(IconError.UnsupportedFormat,
				$"No icon could be read from {FileName(source)}");
		}

		return Result.Ok<ExtractedAppIcon, IconError>(new ExtractedAppIcon(image, name + ".png"));
	}

	private static byte[]? ReadExecutableIcon(string source)
	{
		using var stream = File.OpenRead(source);
		return ReadExecutableIcon(stream);
	}

	private static async Task<byte[]> ReadCappedBytes(string source, CancellationToken cancellationToken)
	{
		if (new FileInfo(source).Length > MaxSourceBytes)
		{
			return [];
		}

		return await File.ReadAllBytesAsync(source, cancellationToken);
	}

	private static string FileName(string path) => Path.GetFileName(IconImportFiles.TrimTrailingSeparators(path));

	private static string IconName(string path)
	{
		var name = Path.GetFileNameWithoutExtension(IconImportFiles.TrimTrailingSeparators(path));
		return string.IsNullOrWhiteSpace(name) ? FallbackName : name;
	}
}
