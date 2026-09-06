using System.Text.RegularExpressions;

namespace MacroDeck.Plugin.Analyzers;

/// <summary>
/// A netstandard2.0-compatible mirror of the two <c>MacroDeck.Sdk.Identity.MacroDeckId</c> validation
/// rules these analyzers check: a package owner id (<c>PluginHostBuilder.WithId</c>) and a declared local
/// id (an action's <c>Id</c>, a <c>DeclaredCapability.LocalId</c>).
///
/// <para>
/// <see cref="MacroDeckIdPatterns" /> is linked source, so the regex patterns and length limits are one
/// source of truth with <c>MacroDeckId</c>. The branching logic below is necessarily a separate copy,
/// because <c>MacroDeckId.cs</c>'s own <c>[GeneratedRegex]</c> usage cannot compile on netstandard2.0 -
/// the existing <c>MacroDeckId</c> tests, required to pass unchanged, are what keeps the two copies from
/// silently disagreeing.
/// </para>
///
/// <para>
/// <c>error</c> is a non-nullable out parameter that is always assigned - empty on success - rather than
/// a nullable one paired with <c>[NotNullWhen]</c>, which is not available on netstandard2.0.
/// </para>
/// </summary>
internal static class IdentityGrammar
{
	private const string Separator = "::";

	private static readonly Regex _segmentPattern =
		new(MacroDeckIdPatterns.SegmentPattern, RegexOptions.CultureInvariant | RegexOptions.Compiled);

	private static readonly Regex _packageOwnerPattern =
		new(MacroDeckIdPatterns.PackageOwnerPattern, RegexOptions.CultureInvariant | RegexOptions.Compiled);

	/// <summary>Mirrors <c>MacroDeckId.TryValidateOwnerId(ownerId, OwnerIdKind.Package, out error)</c>.</summary>
	public static bool TryValidatePackageOwnerId(string ownerId, out string error)
	{
		if (string.IsNullOrEmpty(ownerId))
		{
			error = "It must not be empty.";
			return false;
		}

		if (ownerId.Length > MacroDeckIdPatterns.MaxOwnerIdLength)
		{
			error = $"It must be at most {MacroDeckIdPatterns.MaxOwnerIdLength} characters.";
			return false;
		}

		if (ownerId.Contains(Separator))
		{
			error = $"It must not contain '{Separator}'.";
			return false;
		}

		if (!_packageOwnerPattern.IsMatch(ownerId))
		{
			error = "It must be reverse-domain, lowercase and hyphen-separated, with at least two " +
				"segments (e.g. 'com.example.my-plugin').";
			return false;
		}

		error = string.Empty;
		return true;
	}

	/// <summary>Mirrors <c>MacroDeckId.TryValidateLocalId(localId, LocalIdKind.Declared, out error)</c>.</summary>
	public static bool TryValidateDeclaredLocalId(string localId, out string error)
	{
		if (string.IsNullOrEmpty(localId))
		{
			error = "It must not be empty.";
			return false;
		}

		if (localId.Contains(Separator))
		{
			error = $"It must not contain '{Separator}'; the host derives the qualified id.";
			return false;
		}

		if (localId.Length > MacroDeckIdPatterns.MaxDeclaredLocalIdLength)
		{
			error = $"It must be at most {MacroDeckIdPatterns.MaxDeclaredLocalIdLength} characters.";
			return false;
		}

		if (!_segmentPattern.IsMatch(localId))
		{
			error = "It must be lowercase and hyphen-separated, starting with a letter (e.g. 'set-volume').";
			return false;
		}

		error = string.Empty;
		return true;
	}
}
