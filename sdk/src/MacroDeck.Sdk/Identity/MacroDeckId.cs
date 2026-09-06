using System.Text.RegularExpressions;
using MacroDeck.Plugin.Analyzers;

namespace MacroDeck.Sdk.Identity;

/// <summary>
/// The single place capability identifiers are validated. Owner ids, local ids and the rules that
/// separate them live here rather than in each registry, so the host, the SDK and the plugin protocol
/// cannot drift apart on what a legal id is.
/// </summary>
public static partial class MacroDeckId
{
	public const int MaxOwnerIdLength = MacroDeckIdPatterns.MaxOwnerIdLength;

	public const int MaxDeclaredLocalIdLength = MacroDeckIdPatterns.MaxDeclaredLocalIdLength;

	public const int MaxResourceLocalIdLength = MacroDeckIdPatterns.MaxResourceLocalIdLength;

	// Anchored with \A and \z, not ^ and $: in .NET '$' also matches before a trailing newline, which
	// would let "play\n" pass as a declared id and then miss every lookup for "play". The patterns
	// themselves live in MacroDeckIdPatterns, shared as linked source with MacroDeck.Plugin.Analyzers -
	// see that file's remarks.
	/// <summary>One lowercase-kebab segment: <c>time</c>, <c>music-player</c>, <c>obs2</c>.</summary>
	[GeneratedRegex(MacroDeckIdPatterns.SegmentPattern, RegexOptions.CultureInvariant)]
	private static partial Regex SegmentPattern();

	/// <summary>Two or more of those segments joined by dots: <c>app.macro-deck.spotify</c>.</summary>
	[GeneratedRegex(MacroDeckIdPatterns.PackageOwnerPattern, RegexOptions.CultureInvariant)]
	private static partial Regex PackageOwnerPattern();

	public static bool IsValidOwnerId(string? ownerId, OwnerIdKind kind)
		=> TryValidateOwnerId(ownerId, kind, out _);

	/// <summary>
	/// Accepts either owner kind. Used when reading an id back out of persisted data, where a stored
	/// trigger may legitimately name a host provider (<c>time</c>) or an integration
	/// (<c>app.macro-deck.obs</c>).
	/// </summary>
	public static bool IsValidOwnerId(string? ownerId)
		=> IsValidOwnerId(ownerId, OwnerIdKind.Package) || IsValidOwnerId(ownerId, OwnerIdKind.HostProvider);

	public static bool IsValidLocalId(string? localId, LocalIdKind kind)
		=> TryValidateLocalId(localId, kind, out _);

	/// <summary>
	/// Validates an owner id and, on failure, explains why in a sentence fit for a log line or an
	/// exception message.
	/// </summary>
	public static bool TryValidateOwnerId(string? ownerId, OwnerIdKind kind, out string? error)
	{
		if (string.IsNullOrEmpty(ownerId))
		{
			error = "Owner id must not be empty.";
			return false;
		}

		if (ownerId.Length > MaxOwnerIdLength)
		{
			error = $"Owner id must be at most {MaxOwnerIdLength} characters.";
			return false;
		}

		if (ownerId.Contains(QualifiedId.Separator, StringComparison.Ordinal))
		{
			error = $"Owner id must not contain '{QualifiedId.Separator}'.";
			return false;
		}

		var valid = kind == OwnerIdKind.Package
			? PackageOwnerPattern().IsMatch(ownerId)
			: SegmentPattern().IsMatch(ownerId);

		if (!valid)
		{
			error = kind == OwnerIdKind.Package
				? "Owner id must be reverse-domain, lowercase and hyphen-separated, with at least two " +
				"segments (e.g. 'com.example.my-plugin')."
				: "Host provider id must be a single lowercase, hyphen-separated segment (e.g. 'music-player').";
			return false;
		}

		error = null;
		return true;
	}

	/// <summary>
	/// Validates a local id against the rule for its kind. A local id may never contain the separator,
	/// whatever its kind: that is what stops an author from smuggling a fully qualified id - or another
	/// owner's namespace - into the local half.
	/// </summary>
	public static bool TryValidateLocalId(string? localId, LocalIdKind kind, out string? error)
	{
		if (string.IsNullOrEmpty(localId))
		{
			error = "Local id must not be empty.";
			return false;
		}

		if (localId.Contains(QualifiedId.Separator, StringComparison.Ordinal))
		{
			error = $"Local id must not contain '{QualifiedId.Separator}'; the host derives the qualified id.";
			return false;
		}

		if (kind == LocalIdKind.Declared)
		{
			if (localId.Length > MaxDeclaredLocalIdLength)
			{
				error = $"Local id must be at most {MaxDeclaredLocalIdLength} characters.";
				return false;
			}

			if (!SegmentPattern().IsMatch(localId))
			{
				error = "Local id must be lowercase and hyphen-separated, starting with a letter " +
					"(e.g. 'set-volume').";
				return false;
			}

			error = null;
			return true;
		}

		if (localId.Length > MaxResourceLocalIdLength)
		{
			error = $"Resource id must be at most {MaxResourceLocalIdLength} characters.";
			return false;
		}

		foreach (var character in localId)
		{
			if (char.IsWhiteSpace(character) || char.IsControl(character))
			{
				error = "Resource id must not contain whitespace or control characters.";
				return false;
			}
		}

		error = null;
		return true;
	}
}
