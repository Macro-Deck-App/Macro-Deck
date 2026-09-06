using System.Text;

namespace MacroDeck.Plugin.Cli;

/// <summary>One line of the unified <c>error &lt;code&gt;: &lt;message&gt;</c> / <c>warning &lt;code&gt;:
/// &lt;message&gt;</c> shape every command reports through - see <see cref="CliConsole.WriteError" /> and
/// <see cref="CliConsole.WriteWarning" />.</summary>
internal readonly record struct CliDiagnostic(string Code, string Message);

/// <summary>Text helpers shared by every diagnostic call site.</summary>
internal static class CliText
{
	/// <summary>Turns a PascalCase enum name (<c>OutputExists</c>) into the kebab-case a diagnostic code
	/// uses (<c>output-exists</c>). Moved here from <c>Manifests/ManifestValidator.cs</c>, which was its
	/// only caller until <c>pack</c>'s and <c>run</c>'s/<c>test</c>'s error sites needed the same
	/// conversion.</summary>
	public static string KebabCase(string value)
	{
		var builder = new StringBuilder();

		for (var i = 0; i < value.Length; i++)
		{
			var c = value[i];
			if (char.IsUpper(c) && i > 0)
			{
				builder.Append('-');
			}

			builder.Append(char.ToLowerInvariant(c));
		}

		return builder.ToString();
	}

	/// <summary>Resolves <paramref name="path" /> to an absolute path for display, so the same file never
	/// reads as two different strings depending on which command reported it (<c>'manifest.json'</c> from
	/// <c>validate</c> vs <c>'./manifest.json'</c> from <c>pack</c>). Returns <paramref name="path" />
	/// unchanged if it cannot be resolved - an invalid path is still worth showing to the user as they
	/// typed it.</summary>
	public static string DisplayPath(string path)
	{
		try
		{
			return Path.GetFullPath(path);
		}
		catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
		{
			return path;
		}
	}
}
