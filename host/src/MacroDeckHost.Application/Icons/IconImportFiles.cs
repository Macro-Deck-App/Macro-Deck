namespace MacroDeckHost.Application.Icons;

public static class IconImportFiles
{
	private static readonly HashSet<string> _imageExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg"
	};

	private static readonly HashSet<string> _archiveExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".zip", ".streamdeckiconpack", ".tpi"
	};

	private const string LottieExtension = ".lottie";

	private const string LottieJsonExtension = ".json";

	private static readonly HashSet<string> _appIconSourceExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".ico", ".icns", ".exe", ".dll", ".lnk", ".url", ".desktop", ".app"
	};

	// .dll is excluded from the folder sweep: a Program Files sweep would otherwise harvest hundreds of
	// resource DLLs that were never meant to become icons. A .dll dropped or picked directly still imports.
	private static readonly HashSet<string> _sweepableAppIconExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".ico", ".icns", ".exe", ".lnk", ".url", ".desktop", ".app"
	};

	private static readonly Dictionary<string, (string Key, string Value)> _appearanceTokens =
		new(StringComparer.OrdinalIgnoreCase)
		{
			[IconAppearanceTraits.Light] = (IconAppearanceTraits.ColorScheme, IconAppearanceTraits.Light),
			[IconAppearanceTraits.Dark] = (IconAppearanceTraits.ColorScheme, IconAppearanceTraits.Dark),
			[IconAppearanceTraits.Static] = (IconAppearanceTraits.Motion, IconAppearanceTraits.Static),
			[IconAppearanceTraits.Animated] = (IconAppearanceTraits.Motion, IconAppearanceTraits.Animated)
		};

	public static bool IsSupportedImage(string fileName) => _imageExtensions.Contains(Path.GetExtension(fileName));

	public static bool IsLottieContainer(string fileName)
		=> Path.GetExtension(fileName).Equals(LottieExtension, StringComparison.OrdinalIgnoreCase);

	public static bool IsLottieJson(string fileName)
		=> Path.GetExtension(fileName).Equals(LottieJsonExtension, StringComparison.OrdinalIgnoreCase);

	public static bool IsSupportedImportEntry(string fileName)
		=> IsSupportedImage(fileName) || IsLottieContainer(fileName);

	public static bool IsImportableIcon(string fileName)
		=> IsSupportedImportEntry(fileName) || IsLottieJson(fileName);

	public static bool IsUploadedIcon(string fileName)
		=> CarriesDirectory(fileName)
			? IsSupportedImportEntry(fileName)
			: IsImportableIcon(fileName);

	public static bool CarriesDirectory(string fileName)
		=> fileName.Contains('/', StringComparison.Ordinal) || fileName.Contains('\\', StringComparison.Ordinal);

	public static bool IsAppIconSource(string fileName)
		=> _appIconSourceExtensions.Contains(Path.GetExtension(TrimTrailingSeparators(fileName)));

	public static bool IsIconDropSource(string fileName)
		=> IsSupportedImportEntry(fileName) || IsAppIconSource(fileName);

	public static bool IsFolderImportEntry(string fileName)
		=> IsSupportedImportEntry(fileName) ||
			IsPackFile(fileName) ||
			_sweepableAppIconExtensions.Contains(Path.GetExtension(TrimTrailingSeparators(fileName)));

	public static string TrimTrailingSeparators(string path)
	{
		var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		return trimmed.Length == 0 ? path : trimmed;
	}

	public static bool IsArchive(string fileName) => _archiveExtensions.Contains(Path.GetExtension(fileName));

	public static bool IsStreamDeckIconPack(string fileName)
		=> Path.GetExtension(fileName).Equals(".streamdeckiconpack", StringComparison.OrdinalIgnoreCase);

	public static bool IsTouchPortalIconPack(string fileName)
		=> Path.GetExtension(fileName).Equals(".tpi", StringComparison.OrdinalIgnoreCase);

	public const string MacroDeckIconPackExtension = ".macroDeckIconPack";

	public static bool IsMacroDeckIconPack(string fileName)
		=> Path.GetExtension(fileName).Equals(MacroDeckIconPackExtension, StringComparison.OrdinalIgnoreCase);

	public static bool IsPackFile(string fileName) => IsArchive(fileName) || IsMacroDeckIconPack(fileName);

	public static bool TryParseAppearanceName(string fileName,
		out string baseName,
		out IReadOnlyDictionary<string, string> traits)
	{
		baseName = string.Empty;
		traits = new Dictionary<string, string>(StringComparer.Ordinal);
		var stem = Path.GetFileNameWithoutExtension(FileNameOf(fileName));
		var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
		var end = stem.Length;
		while (end > 0)
		{
			var dot = stem.LastIndexOf('.', end - 1);
			if (dot <= 0 || !_appearanceTokens.TryGetValue(stem[(dot + 1)..end], out var trait))
			{
				break;
			}

			if (!parsed.TryAdd(trait.Key, trait.Value))
			{
				return false;
			}

			end = dot;
		}

		if (parsed.Count == 0)
		{
			return false;
		}

		baseName = stem[..end];
		traits = parsed;
		return true;
	}

	public static string AppearanceBaseNameOf(string fileName) => Path.GetFileNameWithoutExtension(FileNameOf(fileName));

	public static string DirectoryOf(string fileName)
	{
		var normalized = fileName.Replace('\\', '/');
		var separator = normalized.LastIndexOf('/');
		return separator < 0 ? string.Empty : normalized[..separator];
	}

	public static IReadOnlyList<IconImportGroup<T>> GroupAppearanceFiles<T>(IReadOnlyList<T> items,
		Func<T, string> directoryOf,
		Func<T, string> fileNameOf)
	{
		var appearanceNames = new (string BaseName, IReadOnlyDictionary<string, string> Traits)?[items.Count];
		var baseIndexes = new Dictionary<(string Directory, string BaseName), int>();
		for (var index = 0; index < items.Count; index++)
		{
			var fileName = fileNameOf(items[index]);
			if (!IsSupportedImportEntry(fileName))
			{
				continue;
			}

			if (TryParseAppearanceName(fileName, out var baseName, out var traits))
			{
				appearanceNames[index] = (baseName, traits);
			}
			else
			{
				baseIndexes.TryAdd((directoryOf(items[index]), AppearanceBaseNameOf(fileName)), index);
			}
		}

		var attached = new Dictionary<int, List<IconImportAppearance<T>>>();
		var consumed = new bool[items.Count];
		for (var index = 0; index < items.Count; index++)
		{
			if (appearanceNames[index] is not { } name ||
				!baseIndexes.TryGetValue((directoryOf(items[index]), name.BaseName), out var baseIndex))
			{
				continue;
			}

			if (!attached.TryGetValue(baseIndex, out var appearances))
			{
				appearances = [];
				attached[baseIndex] = appearances;
			}

			var key = IconAppearanceTraits.ToKey(name.Traits);
			if (appearances.Count < IconAppearanceTraits.MaxAppearancesPerIcon &&
				appearances.TrueForAll(appearance => IconAppearanceTraits.ToKey(appearance.Traits) != key))
			{
				appearances.Add(new IconImportAppearance<T>(items[index], name.Traits));
				consumed[index] = true;
			}
		}

		var groups = new List<IconImportGroup<T>>(items.Count);
		for (var index = 0; index < items.Count; index++)
		{
			if (!consumed[index])
			{
				groups.Add(new IconImportGroup<T>(items[index], attached.GetValueOrDefault(index) ?? []));
			}
		}

		return groups;
	}

	private static string FileNameOf(string fileName)
	{
		var normalized = fileName.Replace('\\', '/');
		return normalized[(normalized.LastIndexOf('/') + 1)..];
	}
}

public sealed record IconImportAppearance<T>(T Item, IReadOnlyDictionary<string, string> Traits);

public sealed record IconImportGroup<T>(T Item, IReadOnlyList<IconImportAppearance<T>> Appearances);
