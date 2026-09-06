using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace MacroDeck.Plugin.Packaging.Versioning;

/// <summary>
/// Minimal SemVer parser for build-version classification and compatibility-range checks. It keeps the
/// prerelease identifiers intact so callers do not infer release channels from
/// string fragments. Ordering follows SemVer 2.0 section 11; build metadata is ignored, so two versions
/// differing only in metadata compare equal.
/// </summary>
public sealed class SemanticVersion : IComparable<SemanticVersion>
{
	private SemanticVersion(int major, int minor, int patch, string[] preReleaseIdentifiers)
	{
		Major = major;
		Minor = minor;
		Patch = patch;
		PreReleaseIdentifiers = preReleaseIdentifiers;
	}

	public int Major { get; }

	public int Minor { get; }

	public int Patch { get; }

	public IReadOnlyList<string> PreReleaseIdentifiers { get; }

	/// <summary>SemVer 2.0 precedence: numeric core first, then a version with prerelease identifiers
	/// ranks below the same core without them, then identifier by identifier.</summary>
	public int CompareTo(SemanticVersion? other)
	{
		if (other is null)
		{
			return 1;
		}

		var core = Major.CompareTo(other.Major);
		if (core != 0)
		{
			return core;
		}

		core = Minor.CompareTo(other.Minor);
		if (core != 0)
		{
			return core;
		}

		core = Patch.CompareTo(other.Patch);
		if (core != 0)
		{
			return core;
		}

		return ComparePreRelease(PreReleaseIdentifiers, other.PreReleaseIdentifiers);
	}

	/// <summary>Equality is precedence equality, so two versions differing only in build metadata are
	/// equal - the same rule <see cref="CompareTo"/> applies.</summary>
	public override bool Equals(object? obj)
	{
		return obj is SemanticVersion other && CompareTo(other) == 0;
	}

	public override int GetHashCode()
	{
		var hash = new HashCode();
		hash.Add(Major);
		hash.Add(Minor);
		hash.Add(Patch);
		foreach (var identifier in PreReleaseIdentifiers)
		{
			hash.Add(identifier, StringComparer.Ordinal);
		}

		return hash.ToHashCode();
	}

	public static bool operator ==(SemanticVersion? left, SemanticVersion? right)
	{
		return left is null ? right is null : left.Equals(right);
	}

	public static bool operator !=(SemanticVersion? left, SemanticVersion? right)
	{
		return !(left == right);
	}

	public static bool operator <(SemanticVersion? left, SemanticVersion? right)
	{
		return Compare(left, right) < 0;
	}

	public static bool operator <=(SemanticVersion? left, SemanticVersion? right)
	{
		return Compare(left, right) <= 0;
	}

	public static bool operator >(SemanticVersion? left, SemanticVersion? right)
	{
		return Compare(left, right) > 0;
	}

	public static bool operator >=(SemanticVersion? left, SemanticVersion? right)
	{
		return Compare(left, right) >= 0;
	}

	private static int Compare(SemanticVersion? left, SemanticVersion? right)
	{
		if (left is null)
		{
			return right is null ? 0 : -1;
		}

		return left.CompareTo(right);
	}

	private static int ComparePreRelease(IReadOnlyList<string> left, IReadOnlyList<string> right)
	{
		if (left.Count == 0 || right.Count == 0)
		{
			// An absent prerelease outranks a present one; both absent is equal.
			return right.Count.CompareTo(left.Count);
		}

		for (var index = 0; index < Math.Min(left.Count, right.Count); index++)
		{
			var comparison = ComparePreReleaseIdentifier(left[index], right[index]);
			if (comparison != 0)
			{
				return comparison;
			}
		}

		return left.Count.CompareTo(right.Count);
	}

	private static int ComparePreReleaseIdentifier(string left, string right)
	{
		var leftIsNumeric = left.All(char.IsAsciiDigit);
		var rightIsNumeric = right.All(char.IsAsciiDigit);

		if (leftIsNumeric && rightIsNumeric)
		{
			return long.Parse(left, CultureInfo.InvariantCulture)
				.CompareTo(long.Parse(right, CultureInfo.InvariantCulture));
		}

		// Numeric identifiers always rank below alphanumeric ones.
		if (leftIsNumeric != rightIsNumeric)
		{
			return leftIsNumeric ? -1 : 1;
		}

		return string.CompareOrdinal(left, right);
	}

	public static bool TryParse(string? value, [NotNullWhen(true)] out SemanticVersion? version)
	{
		version = null;
		if (string.IsNullOrEmpty(value))
		{
			return false;
		}

		var metadataIndex = value.IndexOf('+', StringComparison.Ordinal);
		var coreAndPreRelease = metadataIndex >= 0 ? value[..metadataIndex] : value;
		if (metadataIndex >= 0 && !HasValidIdentifiers(value[(metadataIndex + 1)..], false))
		{
			return false;
		}

		var preReleaseIndex = coreAndPreRelease.IndexOf('-', StringComparison.Ordinal);
		var core = preReleaseIndex >= 0 ? coreAndPreRelease[..preReleaseIndex] : coreAndPreRelease;
		if (!TryParseCore(core, out var major, out var minor, out var patch))
		{
			return false;
		}

		var preReleaseIdentifiers = Array.Empty<string>();
		if (preReleaseIndex >= 0)
		{
			var preRelease = coreAndPreRelease[(preReleaseIndex + 1)..];
			if (!HasValidIdentifiers(preRelease, true))
			{
				return false;
			}

			preReleaseIdentifiers = preRelease.Split('.');
		}

		version = new SemanticVersion(major, minor, patch, preReleaseIdentifiers);
		return true;
	}

	private static bool TryParseCore(string core, out int major, out int minor, out int patch)
	{
		major = 0;
		minor = 0;
		patch = 0;

		var identifiers = core.Split('.');
		if (identifiers.Length != 3 || !identifiers.All(IsCanonicalNumericIdentifier))
		{
			return false;
		}

		return int.TryParse(identifiers[0], out major) &&
			int.TryParse(identifiers[1], out minor) &&
			int.TryParse(identifiers[2], out patch);
	}

	private static bool HasValidIdentifiers(string value, bool rejectLeadingZeroesInNumericIdentifiers)
	{
		var identifiers = value.Split('.');
		return identifiers.Length > 0 &&
			identifiers.All(identifier =>
				IsValidIdentifier(identifier, rejectLeadingZeroesInNumericIdentifiers));
	}

	private static bool IsCanonicalNumericIdentifier(string value)
	{
		return value.Length > 0 && (value.Length == 1 || value[0] != '0') && value.All(char.IsAsciiDigit);
	}

	private static bool IsValidIdentifier(string value, bool rejectLeadingZeroesInNumericIdentifiers)
	{
		if (value.Length == 0 || !value.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'))
		{
			return false;
		}

		return !rejectLeadingZeroesInNumericIdentifiers ||
			!value.All(char.IsAsciiDigit) ||
			value.Length == 1 ||
			value[0] != '0';
	}
}
