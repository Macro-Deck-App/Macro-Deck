namespace MacroDeck.LicenseTool.Model;

internal static class LicenseFiles
{
	private static readonly string[] LicensePrefixes = ["LICENSE", "LICENCE", "COPYING", "UNLICENSE"];

	private static readonly string[] NoticePrefixes = ["NOTICE", "THIRD-PARTY-NOTICES", "THIRDPARTYNOTICES", "THIRD_PARTY_NOTICES"];

	private static readonly (string Token, string LicensePrefix)[] NamedLicenses =
	[
		("APACHE", "Apache-"),
		("MIT", "MIT"),
		("BSD", "BSD-"),
		("ZLIB", "Zlib"),
		("ISC", "ISC"),
		("BOOST", "BSL-"),
		("BSL", "BSL-"),
		("MPL", "MPL-"),
		("CC0", "CC0-"),
		("UNICODE", "Unicode-"),
	];

	public static LicenseTextKind? Classify(string fileName)
	{
		var upper = fileName.ToUpperInvariant();
		if (NoticePrefixes.Any(prefix => upper.StartsWith(prefix, StringComparison.Ordinal)))
		{
			return LicenseTextKind.Notice;
		}

		if (LicensePrefixes.Any(prefix => upper.StartsWith(prefix, StringComparison.Ordinal)))
		{
			return LicenseTextKind.License;
		}

		return null;
	}

	public static bool NamesUnselectedLicense(string fileName, IReadOnlyCollection<string> selectedLicenses)
	{
		var stem = Path.GetFileNameWithoutExtension(fileName).ToUpperInvariant();
		if (stem.StartsWith("UNLICENSE", StringComparison.Ordinal))
		{
			return !selectedLicenses.Contains("Unlicense", StringComparer.OrdinalIgnoreCase);
		}

		var prefix = LicensePrefixes.FirstOrDefault(candidate => stem.StartsWith(candidate, StringComparison.Ordinal));
		if (prefix is null)
		{
			return false;
		}

		var qualifier = stem[prefix.Length..];
		foreach (var (token, licensePrefix) in NamedLicenses)
		{
			if (qualifier.Contains(token, StringComparison.Ordinal))
			{
				return !selectedLicenses.Any(license =>
					license.StartsWith(licensePrefix, StringComparison.OrdinalIgnoreCase));
			}
		}

		return false;
	}
}
