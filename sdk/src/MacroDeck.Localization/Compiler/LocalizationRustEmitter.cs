using System.Text;

namespace MacroDeck.Localization.Compiler;

/// <summary>
/// Renders part of a compiled resource set as a Rust module the desktop bootstrapper embeds.
/// </summary>
/// <remarks>
/// <para>
/// The bootstrapper cannot ask the host for text at the moment it needs it most: the tray menu is built
/// before the host is up, and the error dialogs it shows are precisely the ones that appear when the host
/// never comes up at all. So its slice of the catalog is compiled in, from the same <c>.resx</c> the host
/// and the Angular clients are generated from, and kept honest by a drift test - the same arrangement the
/// TypeScript module already has.
/// </para>
/// <para>
/// Only one key prefix is emitted. The bootstrapper needs some fifty strings; embedding the whole
/// application catalog would put a thousand strings it will never render into the binary.
/// </para>
/// </remarks>
internal static class LocalizationRustEmitter
{
	/// <summary>Renders the module.</summary>
	/// <param name="keyPrefix">Only keys starting with this are emitted, for example <c>Bootstrapper.</c>.</param>
	/// <param name="result">The compiled resource set.</param>
	public static string Emit(string keyPrefix, LocalizationCompilationResult result)
	{
		var builder = new StringBuilder();

		builder.Append("// Generated from the Macro Deck localization resources. Do not edit by hand.\n");
		builder.Append(
			"// Regenerate: MACRODECK_UPDATE_GENERATED=1 dotnet test sdk/tests/MacroDeck.Localization.Tests.UnitTests\n\n");
		builder.Append("/// The scope every key below belongs to.\n");
		builder.Append("pub const SCOPE: &str = \"").Append(Escape(result.Scope)).Append("\";\n\n");
		builder.Append(
			"/// The culture the compiled-in text is written in, and the last resort of the fallback chain.\n");
		builder.Append("pub const DEFAULT_CULTURE: &str = \"").Append(Escape(result.DefaultCulture)).Append("\";\n\n");

		EmitKeys(builder, keyPrefix, result);
		EmitCatalog(builder, keyPrefix, result);

		return builder.ToString();
	}

	/// <summary>The keys as constants, so a call site names one instead of spelling it out.</summary>
	private static void EmitKeys(StringBuilder builder,
		string keyPrefix,
		LocalizationCompilationResult result)
	{
		builder.Append("/// Every key this module carries.\n");
		// Left unformatted rather than shaped to rustfmt's taste: the drift test compares this file byte
		// for byte against a fresh generation, so `cargo fmt` rewriting it would put the two guards in
		// permanent conflict.
		builder.Append("#[rustfmt::skip]\n");
		// The catalog is emitted whole on every platform, but its call sites are not: the macOS-only app
		// menu and move-to-Applications prompt leave their keys unreferenced on Linux and Windows, where
		// `-D warnings` would otherwise turn that into a build failure.
		builder.Append("#[allow(dead_code)]\n");
		builder.Append("pub mod keys {\n");

		foreach (var entry in result.Entries)
		{
			if (!entry.Key.StartsWith(keyPrefix, StringComparison.Ordinal))
			{
				continue;
			}

			builder.Append("    pub const ").Append(ConstantName(entry.Key, keyPrefix))
				.Append(": &str = \"").Append(Escape(entry.Key)).Append("\";\n");
		}

		builder.Append("}\n\n");
	}

	/// <summary>
	/// Culture to key to template, as a sorted slice. A slice rather than a map because it is looked up a
	/// few dozen times over a process's life and a compile-time constant needs no allocation or lazy init.
	/// </summary>
	private static void EmitCatalog(StringBuilder builder,
		string keyPrefix,
		LocalizationCompilationResult result)
	{
		builder.Append("/// Culture to its templates, sorted by key.\n");
		builder.Append("#[rustfmt::skip]\n");
		builder.Append("pub static CATALOG: &[(&str, &[(&str, &str)])] = &[\n");

		var cultures = new List<string>(result.Catalog.Keys);
		cultures.Sort(StringComparer.Ordinal);

		foreach (var culture in cultures)
		{
			var templates = result.Catalog[culture];
			var keys = new List<string>();

			foreach (var key in templates.Keys)
			{
				if (key.StartsWith(keyPrefix, StringComparison.Ordinal))
				{
					keys.Add(key);
				}
			}

			keys.Sort(StringComparer.Ordinal);

			builder.Append("    (\"").Append(Escape(culture)).Append("\", &[\n");

			foreach (var key in keys)
			{
				builder.Append("        (\"").Append(Escape(key)).Append("\", \"")
					.Append(Escape(templates[key])).Append("\"),\n");
			}

			builder.Append("    ]),\n");
		}

		builder.Append("];\n");
	}

	/// <summary>A key as a SCREAMING_SNAKE_CASE constant name, with the shared prefix dropped.</summary>
	private static string ConstantName(string key, string keyPrefix)
	{
		var name = key.Substring(keyPrefix.Length);
		var builder = new StringBuilder(name.Length + 8);

		for (var index = 0; index < name.Length; index++)
		{
			var character = name[index];

			if (character == '.')
			{
				builder.Append('_');
				continue;
			}

			if (char.IsUpper(character) &&
				index > 0 &&
				name[index - 1] != '.' &&
				!char.IsUpper(name[index - 1]))
			{
				builder.Append('_');
			}

			builder.Append(char.ToUpperInvariant(character));
		}

		return builder.ToString();
	}

	private static string Escape(string? value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return string.Empty;
		}

		return value!.Replace("\\", "\\\\").Replace("\"", "\\\"")
			.Replace("\r", "\\r").Replace("\n", "\\n");
	}
}
