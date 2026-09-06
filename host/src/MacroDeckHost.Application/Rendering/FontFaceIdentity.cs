using System.Security.Cryptography;
using System.Text;

namespace MacroDeckHost.Application.Rendering;

/// <summary>
/// Builds the <c>{family-slug}-{weight}-{width}-{slant}</c> id that identifies a font face.
///
/// <para>
/// The format is derived only from font attributes, never from a file path or an enumeration index,
/// so the same face yields the same id on another machine and a profile stays portable. Both the
/// catalog (from a real installed face) and the widget font migration (from a legacy
/// family+bold+italic triple, for a family that may not be installed here) must produce byte-identical
/// ids for the same face, which is why the format lives in one place.
/// </para>
/// </summary>
public static class FontFaceIdentity
{
	/// <summary>The width of a face with no width information, matching SkiaSharp's normal width.</summary>
	public const int NormalWidth = 5;

	/// <summary>Slant of an upright face.</summary>
	public const string UprightSlant = "upright";

	/// <summary>Slant of an italic face.</summary>
	public const string ItalicSlant = "italic";

	/// <summary>Slant of an oblique face.</summary>
	public const string ObliqueSlant = "oblique";

	/// <summary>Weight of a regular face, used when a legacy configuration only says "not bold".</summary>
	public const int RegularWeight = 400;

	/// <summary>Weight of a bold face, used when a legacy configuration only says "bold".</summary>
	public const int BoldWeight = 700;

	/// <summary>
	/// Builds a face id. The result contains only <c>[a-z0-9-]</c>, so it is safe in a URL path segment
	/// and as a CSS family name suffix.
	/// </summary>
	public static string Build(string family, int weight, int width, string slant)
		=> $"{Slug(family)}-{weight}-{width}-{slant}";

	/// <summary>
	/// Builds a face id from a legacy family + bold + italic triple, assuming normal width. Used to
	/// migrate stored widget configuration, including for a family this machine does not have.
	/// </summary>
	public static string BuildLegacy(string family, bool bold, bool italic)
		=> Build(family,
			bold ? BoldWeight : RegularWeight,
			NormalWidth,
			italic ? ItalicSlant : UprightSlant);

	private static string Slug(string family)
	{
		var builder = new StringBuilder(family.Length);
		foreach (var character in family)
		{
			var lower = char.ToLowerInvariant(character);
			if (lower is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
			{
				builder.Append(lower);
			}
			else if (builder.Length > 0 && builder[^1] != '-')
			{
				builder.Append('-');
			}
		}

		var slug = builder.ToString().TrimEnd('-');

		// A family named entirely outside ASCII - which DirectWrite reports for CJK fonts on a
		// CJK-locale Windows - would otherwise slug to nothing, so every such family would share one id
		// and be told apart only by the catalog's enumeration-order suffix. That suffix is stable on one
		// machine but not across machines, which is exactly the portability a face id has to provide.
		return slug.Length > 0 ? slug : Fingerprint(family);
	}

	private static string Fingerprint(string family)
	{
		var hash = SHA256.HashData(Encoding.UTF8.GetBytes(family));
		return Convert.ToHexStringLower(hash.AsSpan(0, 6));
	}
}
