using System.Text;

namespace MacroDeckHost.Application.Logging;

public sealed class UserPathRedactor
{
	public const string HomePlaceholder = "~";
	public const string UserPlaceholder = "<user>";

	private static readonly string[] ExemptSegments =
		["Public", "Default", "Default User", "All Users", "Shared"];

	private static readonly Lazy<UserPathRedactor> Lazy = new(FromEnvironment);

	private readonly string? _home;
	private readonly (string Root, string Replacement)[] _extraRoots;
	private readonly bool _ignoreCase;
	private readonly string? _homeProbe;

	public static UserPathRedactor Current => Lazy.Value;

	public UserPathRedactor(
		string? homeDirectory,
		IEnumerable<KeyValuePair<string, string>> extraRoots,
		bool ignoreCase)
	{
		ArgumentNullException.ThrowIfNull(extraRoots);

		_ignoreCase = ignoreCase;
		_home = NormalizeRoot(homeDirectory);
		_homeProbe = LastSegment(_home);
		_extraRoots = extraRoots
			.Select(root => (Root: NormalizeRoot(root.Key), root.Value))
			.Where(root => root.Root is not null && !IsUnder(root.Root!, _home, ignoreCase))
			.OrderByDescending(root => root.Root!.Length)
			.Select(root => (root.Root!, root.Value))
			.ToArray();
	}

	public string Redact(string? text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return text ?? string.Empty;
		}

		var result = text;
		if (_home is not null)
		{
			result = ReplaceRoot(result, _home, HomePlaceholder, _homeProbe!);
		}

		foreach (var (root, replacement) in _extraRoots)
		{
			result = ReplaceRoot(result, root, replacement, LastSegment(root)!);
		}

		return ReplaceForeignUsers(result);
	}

	private static bool IsPathBody(char character)
		=> character > '\u007f' || char.IsAsciiLetterOrDigit(character) || character is '_' or '.' or '-';

	private static bool IsSeparator(char character) => character is '/' or '\\';

	private static bool IsSegmentTerminator(char character)
		=> character <= ' ' || character is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|';

	private static bool IsLeftBoundary(string text, int index)
		=> index == 0 || !IsPathBody(text[index - 1]);

	private static bool AsciiEquals(char left, char right, bool ignoreCase)
		=> ignoreCase
			? ToAsciiLower(left) == ToAsciiLower(right)
			: left == right;

	private static char ToAsciiLower(char character)
		=> char.IsAsciiLetterUpper(character) ? (char)(character + 32) : character;

	private static bool IsUrlBoundary(char character)
		=> char.IsWhiteSpace(character) ||
			character is '"' or '\'' or ',' or ';' or '(' or ')' or '[' or ']' or '{' or '}' or '<' or '>' or '\\';

	private string ReplaceRoot(string text, string root, string replacement, string probe)
	{
		var comparison = _ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
		if (text.IndexOf(probe, comparison) < 0)
		{
			return text;
		}

		StringBuilder? builder = null;
		var copied = 0;
		var index = 0;
		while (index < text.Length)
		{
			if (!IsLeftBoundary(text, index) || !TryMatchRoot(text, index, root, out var end))
			{
				index++;
				continue;
			}

			builder ??= new StringBuilder(text.Length);
			builder.Append(text, copied, index - copied).Append(replacement);
			copied = end;
			index = end;
		}

		return builder is null ? text : builder.Append(text, copied, text.Length - copied).ToString();
	}

	private bool TryMatchRoot(string text, int start, string root, out int end)
	{
		end = start;
		var textIndex = start;
		var rootIndex = 0;
		while (rootIndex < root.Length)
		{
			if (IsSeparator(root[rootIndex]))
			{
				if (textIndex >= text.Length || !IsSeparator(text[textIndex]))
				{
					return false;
				}

				while (rootIndex < root.Length && IsSeparator(root[rootIndex]))
				{
					rootIndex++;
				}

				while (textIndex < text.Length && IsSeparator(text[textIndex]))
				{
					textIndex++;
				}

				continue;
			}

			if (textIndex >= text.Length || !AsciiEquals(text[textIndex], root[rootIndex], _ignoreCase))
			{
				return false;
			}

			textIndex++;
			rootIndex++;
		}

		// A sibling directory must not match a shorter root: "C:\Users\me2" is not "C:\Users\me".
		if (textIndex < text.Length && IsPathBody(text[textIndex]))
		{
			return false;
		}

		end = textIndex;
		return true;
	}

	private static string ReplaceForeignUsers(string text)
	{
		StringBuilder? builder = null;
		var copied = 0;
		var index = 0;
		while (index < text.Length)
		{
			if (!IsLeftBoundary(text, index) ||
				!TryMatchProfileRoot(text, index, out var segmentStart, out var hasExemptNames) ||
				IsInsideNonFileUrl(text, index))
			{
				index++;
				continue;
			}

			var segmentEnd = segmentStart;
			while (segmentEnd < text.Length && !IsSegmentTerminator(text[segmentEnd]))
			{
				segmentEnd++;
			}

			if (segmentEnd == segmentStart || (hasExemptNames && IsExempt(text, segmentStart)))
			{
				index = segmentEnd > index ? segmentEnd : index + 1;
				continue;
			}

			builder ??= new StringBuilder(text.Length);
			builder.Append(text, copied, segmentStart - copied).Append(UserPlaceholder);
			copied = segmentEnd;
			index = segmentEnd;
		}

		return builder is null ? text : builder.Append(text, copied, text.Length - copied).ToString();
	}

	private static bool TryMatchProfileRoot(string text, int start, out int segmentStart, out bool hasExemptNames)
	{
		segmentStart = 0;
		hasExemptNames = true;

		var index = start;
		if (index + 1 < text.Length && char.IsAsciiLetter(text[index]) && text[index + 1] == ':')
		{
			index += 2;
			if (!TrySkipSeparators(text, ref index) ||
				!TrySkipLiteral(text, ref index, "Users", ignoreCase: true) ||
				!TrySkipSeparators(text, ref index))
			{
				return false;
			}
		}
		else if (index + 1 < text.Length && text[index] == '\\' && text[index + 1] == '\\')
		{
			index += 2;
			var serverStart = index;
			while (index < text.Length && !IsSegmentTerminator(text[index]))
			{
				index++;
			}

			if (index == serverStart ||
				!TrySkipSeparators(text, ref index) ||
				!TrySkipLiteral(text, ref index, "Users", ignoreCase: true) ||
				!TrySkipSeparators(text, ref index))
			{
				return false;
			}
		}
		else if (index < text.Length && text[index] == '/')
		{
			index++;
			if (TrySkipLiteral(text, ref index, "home", ignoreCase: false))
			{
				// Nothing under /home is a shared profile directory, so a real account named
				// "shared" or "public" must not be spared here.
				hasExemptNames = false;
			}
			else if (!TrySkipLiteral(text, ref index, "Users", ignoreCase: false))
			{
				return false;
			}

			if (!TrySkipSeparators(text, ref index))
			{
				return false;
			}
		}
		else
		{
			return false;
		}

		segmentStart = index;
		return true;
	}

	private static bool TrySkipSeparators(string text, ref int index)
	{
		var start = index;
		while (index < text.Length && IsSeparator(text[index]))
		{
			index++;
		}

		return index > start;
	}

	private static bool TrySkipLiteral(string text, ref int index, string literal, bool ignoreCase)
	{
		if (index + literal.Length > text.Length)
		{
			return false;
		}

		var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
		if (!text.AsSpan(index, literal.Length).Equals(literal, comparison))
		{
			return false;
		}

		index += literal.Length;
		return true;
	}

	private static bool IsInsideNonFileUrl(string text, int index)
	{
		var tokenStart = index;
		while (tokenStart > 0 && !IsUrlBoundary(text[tokenStart - 1]))
		{
			tokenStart--;
		}

		var schemeEnd = text.AsSpan(tokenStart, index - tokenStart).IndexOf("://", StringComparison.Ordinal);

		return schemeEnd >= 0 &&
			!text.AsSpan(tokenStart, schemeEnd).Equals("file", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsExempt(string text, int start)
	{
		foreach (var exempt in ExemptSegments)
		{
			if (start + exempt.Length > text.Length ||
				!text.AsSpan(start, exempt.Length).Equals(exempt, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			var after = start + exempt.Length;
			if (after == text.Length || IsSegmentTerminator(text[after]))
			{
				return true;
			}
		}

		return false;
	}

	private static string? NormalizeRoot(string? root)
	{
		if (string.IsNullOrWhiteSpace(root))
		{
			return null;
		}

		var trimmed = root.TrimEnd('/', '\\');

		return trimmed.Length == 0 || !IsAbsolute(trimmed) || IsDriveRoot(trimmed) ? null : trimmed;
	}

	private static bool IsAbsolute(string value)
		=> IsSeparator(value[0]) ||
			(value.Length >= 3 && char.IsAsciiLetter(value[0]) && value[1] == ':' && IsSeparator(value[2]));

	private static bool IsDriveRoot(string value)
		=> value.Length == 2 && char.IsAsciiLetter(value[0]) && value[1] == ':';

	private static string? LastSegment(string? root)
	{
		if (root is null)
		{
			return null;
		}

		var separator = root.LastIndexOfAny(['/', '\\']);

		return separator >= 0 && separator < root.Length - 1 ? root[(separator + 1)..] : root;
	}

	private static bool IsUnder(string candidate, string? root, bool ignoreCase)
	{
		if (root is null)
		{
			return false;
		}

		var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

		return candidate.StartsWith(root, comparison) &&
			(candidate.Length == root.Length || IsSeparator(candidate[root.Length]));
	}

	private static UserPathRedactor FromEnvironment()
	{
		var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		var extraRoots = new List<KeyValuePair<string, string>>
		{
			new(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "%APPDATA%"),
			new(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "%LOCALAPPDATA%")
		};

		return new UserPathRedactor(home, extraRoots, OperatingSystem.IsWindows() || OperatingSystem.IsMacOS());
	}
}
