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
}
