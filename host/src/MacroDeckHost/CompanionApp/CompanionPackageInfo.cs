using System.Globalization;
using System.Text.RegularExpressions;

namespace MacroDeckHost.CompanionApp;

public sealed record CompanionPackageInfo(int VersionCode, string? VersionName, string? Installer)
{
	public const string PlayStoreInstaller = "com.android.vending";

	public bool FromPlayStore => string.Equals(Installer, PlayStoreInstaller, StringComparison.Ordinal);
}

public static partial class CompanionPackageInfoParser
{
	public static int? ParseSdkLevel(string output)
		=> int.TryParse(output.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var level) && level > 0
			? level
			: null;

	// An updated system app lists its factory version again under "Hidden system packages:", so only
	// the entry under "Packages:" counts.
	public static CompanionPackageInfo? ParsePackageInfo(string output, string package)
	{
		var header = $"Package [{package}]";
		var lines = output.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
		var section = Array.FindIndex(lines, line => line.Trim() == "Packages:");
		var start = section < 0
			? -1
			: Array.FindIndex(lines, section + 1, line => line.TrimStart().StartsWith(header, StringComparison.Ordinal));
		if (start < 0)
		{
			return null;
		}

		var indent = Indent(lines[start]);
		int? versionCode = null;
		string? versionName = null;
		string? installer = null;
		for (var index = start + 1; index < lines.Length; index++)
		{
			var line = lines[index];
			if (line.Trim().Length == 0)
			{
				continue;
			}

			if (Indent(line) <= indent)
			{
				break;
			}

			if (versionCode is null && VersionCodeRegex().Match(line) is { Success: true } code &&
				int.TryParse(code.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
			{
				versionCode = parsed;
			}
			else if (versionName is null && VersionNameRegex().Match(line) is { Success: true } name)
			{
				versionName = name.Groups[1].Value;
			}
			else if (installer is null && InstallerRegex().Match(line) is { Success: true } source)
			{
				installer = source.Groups[1].Value == "null" ? null : source.Groups[1].Value;
			}
			else if (UserZeroRegex().Match(line) is { Success: true } user && user.Groups[1].Value == "false")
			{
				return null;
			}
		}

		return versionCode is { } version ? new CompanionPackageInfo(version, versionName, installer) : null;
	}

	private static int Indent(string line) => line.Length - line.TrimStart().Length;

	[GeneratedRegex(@"^\s*versionCode=(\d+)")]
	private static partial Regex VersionCodeRegex();

	[GeneratedRegex(@"^\s*versionName=(\S+)")]
	private static partial Regex VersionNameRegex();

	[GeneratedRegex(@"^\s*installerPackageName=(\S+)")]
	private static partial Regex InstallerRegex();

	[GeneratedRegex(@"^\s*User 0:.*\binstalled=(true|false)")]
	private static partial Regex UserZeroRegex();
}
