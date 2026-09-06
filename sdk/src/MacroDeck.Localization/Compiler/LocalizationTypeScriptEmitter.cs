using System.Text;

namespace MacroDeck.Localization.Compiler;

/// <summary>
/// Renders a compiled resource set as the TypeScript key map the Angular clients import, so the frontend
/// never hand-copies a key. Driven by the same <see cref="LocalizationCompilationResult" /> the C# emitter
/// consumes, which is what makes "one compiler, both APIs" true rather than aspirational: a key that does
/// not survive validation reaches neither side.
/// </summary>
internal static class LocalizationTypeScriptEmitter
{
	/// <summary>Renders the module.</summary>
	/// <param name="constName">The exported const's name, for example <c>Strings</c>.</param>
	/// <param name="result">The compiled resource set.</param>
	/// <param name="keyPrefixes">
	/// Only keys starting with one of these are emitted; null or empty emits the whole catalog. Each must
	/// end at a key-namespace boundary, so that a plural family's base key - which is what the members are
	/// built from - and its forms - which is what the defaults are built from - are kept or dropped
	/// together.
	/// </param>
	public static string Emit(string constName,
		LocalizationCompilationResult result,
		IReadOnlyList<string>? keyPrefixes = null)
	{
		var builder = new StringBuilder();

		builder.Append("// Generated from the Macro Deck localization resources. Do not edit by hand.\n");
		builder.Append(
			"// Regenerate: MACRODECK_UPDATE_GENERATED=1 dotnet test sdk/tests/MacroDeck.Localization.Tests.UnitTests\n\n");
		builder.Append("/** The scope every key below belongs to. */\n");
		builder.Append("export const ").Append(constName).Append("Scope = '").Append(Escape(result.Scope))
			.Append("' as const;\n\n");
		builder.Append("/** Fully qualified localization keys, as the host's catalog serves them. */\n");
		builder.Append("export const ").Append(constName).Append(" = {\n");

		var root = Node.Build(result, keyPrefixes);
		EmitMembers(builder, "\t", result.Scope, root);

		builder.Append("} as const;\n\n");

		builder.Append("/** Any key of this catalog - what a call site is checked against. */\n");
		builder.Append("export type ").Append(constName)
			.Append("Key = LocalizationKeyLeaves<typeof ").Append(constName).Append(">;\n\n");
		builder.Append("type LocalizationKeyLeaves<T> = T extends string ? T\n");
		builder.Append("\t: { [K in keyof T]: LocalizationKeyLeaves<T[K]> }[keyof T];\n\n");

		EmitDefaults(builder, constName, result, keyPrefixes);

		return builder.ToString();
	}

	/// <summary>
	/// Emits the default-language text for every key, which the client seeds its catalog with so a
	/// migrated string renders in English before the host has answered - and keeps rendering it for a key
	/// the host's catalog turns out not to carry.
	/// </summary>
	/// <remarks>
	/// Taken from the runtime catalog rather than from the generated members, so a plural family
	/// contributes its forms - the keys resolution actually looks up - rather than the base key, which is
	/// a member name and never a template.
	/// </remarks>
	private static void EmitDefaults(StringBuilder builder,
		string constName,
		LocalizationCompilationResult result,
		IReadOnlyList<string>? keyPrefixes)
	{
		builder.Append("/** The default-language text of every key, as the fallback before the host answers. */\n");
		builder.Append("export const ").Append(constName).Append("Defaults: Readonly<Record<string, string>> = {\n");

		if (result.Catalog.TryGetValue(result.DefaultCulture, out var templates))
		{
			var keys = new List<string>(templates.Keys);
			keys.Sort(StringComparer.Ordinal);

			foreach (var key in keys)
			{
				if (!Includes(keyPrefixes, key))
				{
					continue;
				}

				builder.Append("\t'").Append(Escape(result.Scope)).Append(':').Append(Escape(key))
					.Append("': '").Append(Escape(templates[key])).Append("',\n");
			}
		}

		builder.Append("};\n");
	}

	private static bool Includes(IReadOnlyList<string>? keyPrefixes, string key)
	{
		if (keyPrefixes is null || keyPrefixes.Count == 0)
		{
			return true;
		}

		for (var index = 0; index < keyPrefixes.Count; index++)
		{
			if (key.StartsWith(keyPrefixes[index], StringComparison.Ordinal))
			{
				return true;
			}
		}

		return false;
	}

	private static void EmitMembers(StringBuilder builder, string indent, string scope, Node node)
	{
		foreach (var entry in node.Entries)
		{
			builder.Append(indent).Append(Member(entry.Key)).Append(": '").Append(Escape(scope))
				.Append(':').Append(Escape(entry.Value)).Append("',\n");
		}

		foreach (var child in node.Children)
		{
			builder.Append(indent).Append(Member(child.Key)).Append(": {\n");
			EmitMembers(builder, indent + "\t", scope, child.Value);
			builder.Append(indent).Append("},\n");
		}
	}

	/// <summary>A key segment as an object member. Quoted only when it is not a plain identifier, so the
	/// common case stays readable.</summary>
	private static string Member(string name)
	{
		if (LocalizationTemplate.IsPlaceholderName(name))
		{
			return name;
		}

		return "'" + Escape(name) + "'";
	}

	private static string Escape(string? value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return string.Empty;
		}

		// Newlines matter here because a resource value may legitimately span lines; a raw one would end
		// the emitted string literal mid-module.
		return value!.Replace("\\", "\\\\").Replace("'", "\\'")
			.Replace("\r", "\\r").Replace("\n", "\\n");
	}

	/// <summary>The dotted keys arranged as the nested object they produce.</summary>
	private sealed class Node
	{
		private readonly Dictionary<string, Node> _byName = new(StringComparer.Ordinal);

		/// <summary>Member name to the full dotted key it maps to.</summary>
		public List<KeyValuePair<string, string>> Entries { get; } = new();

		public List<KeyValuePair<string, Node>> Children { get; } = new();

		public static Node Build(LocalizationCompilationResult result, IReadOnlyList<string>? keyPrefixes)
		{
			var root = new Node();

			foreach (var entry in result.Entries)
			{
				if (!Includes(keyPrefixes, entry.Key))
				{
					continue;
				}

				var segments = entry.Key.Split('.');
				var node = root;

				for (var index = 0; index < segments.Length - 1; index++)
				{
					node = node.Child(segments[index]);
				}

				node.Entries.Add(new KeyValuePair<string, string>(segments[segments.Length - 1], entry.Key));
			}

			return root;
		}

		private Node Child(string name)
		{
			if (_byName.TryGetValue(name, out var existing))
			{
				return existing;
			}

			var created = new Node();
			_byName.Add(name, created);
			Children.Add(new KeyValuePair<string, Node>(name, created));
			return created;
		}
	}
}
