using System.Text.RegularExpressions;

namespace MacroDeck.Plugin.Protocol.Assets;

/// <summary>
/// What a plugin may register as a UI resource: the name grammar and the media types. The SDK checks a
/// registration against these before sending anything, and the host enforces the same rules.
/// </summary>
public static partial class UiResourceRules
{
	/// <summary>The longest name <see cref="IsValidName" /> accepts.</summary>
	public const int MaxNameLength = 64;

	/// <summary>
	/// The media types a UI resource may have: raster images only. Scriptable formats such as SVG are not
	/// accepted from a plugin, the same rule that applies to icons a plugin supplies.
	/// </summary>
	public static readonly IReadOnlyList<string> SupportedMediaTypes =
		Array.AsReadOnly(new[] { "image/png", "image/jpeg", "image/webp", "image/gif" });

	[GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9_-]{0,63}\z", RegexOptions.CultureInvariant)]
	private static partial Regex NamePattern();

	/// <summary>
	/// True when <paramref name="name" /> is an ASCII letter or digit followed by up to 63 more letters,
	/// digits, hyphens or underscores. Never throws, including on <c>null</c>.
	/// </summary>
	public static bool IsValidName(string? name) => name is not null && NamePattern().IsMatch(name);

	/// <summary>True when <paramref name="mediaType" /> is one of <see cref="SupportedMediaTypes" />,
	/// compared case-insensitively.</summary>
	public static bool IsSupportedMediaType(string? mediaType)
		=> mediaType is not null &&
			SupportedMediaTypes.Contains(mediaType, StringComparer.OrdinalIgnoreCase);
}
