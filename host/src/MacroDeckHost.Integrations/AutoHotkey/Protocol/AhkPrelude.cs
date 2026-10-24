using System.Text;
using System.Text.RegularExpressions;

namespace MacroDeckHost.Integrations.AutoHotkey.Protocol;

internal sealed class AhkPreludeException : Exception
{
	public AhkPreludeException(string message, string? key)
		: base(message)
	{
		Key = key;
	}

	public string? Key { get; }
}

internal static partial class AhkPrelude
{
	public const int ProtocolVersion = 1;

	private static readonly Lazy<string> _helper = new(() => LoadLibrary("MacroDeck.ahk"));
	private static readonly Lazy<string> _minimal = new(() => LoadLibrary("MacroDeckMinimal.ahk"));

	public static string Build(IReadOnlyDictionary<string, string> parameters, bool includeHelper)
	{
		var builder = new StringBuilder();
		builder.Append(includeHelper ? _helper.Value : _minimal.Value);
		builder.Append('\n');
		builder.Append("MacroDeck._Start(").Append(ProtocolVersion).Append(")\n");

		foreach (var (key, value) in parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal))
		{
			if (!IsValidKey(key))
			{
				throw new AhkPreludeException("A parameter name is not a valid identifier.", key);
			}

			builder.Append("MacroDeck.Params[\"").Append(key).Append("\"] := ").Append(Literal(value)).Append('\n');
		}

		builder.Append("MacroDeck._Ready()\n");
		return builder.ToString();
	}

	public static bool IsValidKey(string? key)
	{
		return key is not null && KeyPattern().IsMatch(key);
	}

	// An AutoHotkey v2 quoted string: a backtick escapes, and a semicolon after a space would start a
	// comment even inside quotes, so every semicolon is escaped as well.
	public static string Literal(string value)
	{
		var builder = new StringBuilder(value.Length + 2);
		builder.Append('"');
		foreach (var character in value)
		{
			switch (character)
			{
				case '\0':
					throw new AhkPreludeException("A parameter value contains a NUL character.", null);
				case '`':
					builder.Append("``");
					break;
				case '"':
					builder.Append("`\"");
					break;
				case ';':
					builder.Append("`;");
					break;
				case '\r':
					builder.Append("`r");
					break;
				case '\n':
					builder.Append("`n");
					break;
				case '\t':
					builder.Append("`t");
					break;
				case '\b':
					builder.Append("`b");
					break;
				case '\f':
					builder.Append("`f");
					break;
				case '\v':
					builder.Append("`v");
					break;
				default:
					builder.Append(character);
					break;
			}
		}

		builder.Append('"');
		return builder.ToString();
	}

	private static string LoadLibrary(string fileName)
	{
		var assembly = typeof(AhkPrelude).Assembly;
		var name = assembly.GetManifestResourceNames()
			.First(resource => resource.EndsWith(".AutoHotkey.Protocol." + fileName, StringComparison.Ordinal));
		using var stream = assembly.GetManifestResourceStream(name)!;
		using var reader = new StreamReader(stream, Encoding.UTF8);
		return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
	}

	[GeneratedRegex(@"\A[A-Za-z_][A-Za-z0-9_]*\z", RegexOptions.CultureInvariant)]
	private static partial Regex KeyPattern();
}
