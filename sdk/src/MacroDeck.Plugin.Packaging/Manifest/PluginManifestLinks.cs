using System.Text.Json;

namespace MacroDeck.Plugin.Packaging.Manifest;

public enum PluginManifestLinkProblemSeverity
{
	Error,

	/// <summary>Only ever an unknown link type: legal, but nothing this build can label or show.</summary>
	Warning
}

/// <summary>One finding of <see cref="PluginManifestLinks.Validate"/>.</summary>
public sealed record PluginManifestLinkProblem
{
	/// <summary>Position in the <c>additionalLinks</c> array, or null when the value as a whole is wrong.</summary>
	public int? Index { get; init; }

	/// <summary><c>type</c>, <c>url</c> or <c>label</c>, or null when the problem concerns the whole entry.</summary>
	public string? Property { get; init; }

	public required PluginManifestLinkProblemSeverity Severity { get; init; }

	public required string Message { get; init; }
}

/// <summary>
/// The single definition of a valid <c>additionalLinks</c> value, shared by the CLI, the Store and the
/// host so none of them restates the rules. Works on the raw JSON rather than on
/// <see cref="PluginManifest.AdditionalLinks"/>, which the tolerant reader has already normalised.
/// <para>
/// Rules: the value is an array of objects. <c>type</c> is a non-blank string. <c>url</c> is an absolute
/// http or https URL. <see cref="PluginManifestLinkTypes.Custom"/> needs a non-blank <c>label</c> of at
/// most <see cref="MaxLabelLength"/> UTF-16 code units without control characters; a standard type must
/// not have one. A JSON null counts as absent. No two links share a URL (compared as
/// <see cref="Uri.AbsoluteUri"/>, fragment included), a standard type, or a custom label (trimmed,
/// case-insensitive). The first occurrence wins.
/// </para>
/// </summary>
public static class PluginManifestLinks
{
	public const int MaxLabelLength = 128;

	/// <summary>Every problem in <paramref name="additionalLinks"/>. An undefined or null element is an
	/// absent list and has none.</summary>
	public static IReadOnlyList<PluginManifestLinkProblem> Validate(JsonElement additionalLinks)
	{
		var problems = new List<PluginManifestLinkProblem>();
		if (additionalLinks.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
		{
			return problems;
		}

		if (additionalLinks.ValueKind != JsonValueKind.Array)
		{
			problems.Add(Error(null, null, "additionalLinks must be an array."));
			return problems;
		}

		var seen = new SeenLinks();
		var index = 0;
		foreach (var entry in additionalLinks.EnumerateArray())
		{
			Inspect(entry, index, seen, problems);
			index++;
		}

		return problems;
	}

	/// <summary>The links a UI may show, in declared order: every entry <see cref="Validate"/> would accept
	/// without an error, minus unknown types (nothing to label them with). Invalid entries and later
	/// duplicates are dropped instead of failing the list, standard types come back without a label and
	/// custom labels trimmed.</summary>
	public static IReadOnlyList<PluginManifestLink> Displayable(JsonElement additionalLinks)
	{
		var links = new List<PluginManifestLink>();
		if (additionalLinks.ValueKind != JsonValueKind.Array)
		{
			return links;
		}

		var seen = new SeenLinks();
		var index = 0;
		foreach (var entry in additionalLinks.EnumerateArray())
		{
			var problems = new List<PluginManifestLinkProblem>();
			var link = Inspect(entry, index, seen, problems);
			index++;

			if (link is not null && problems.Count == 0)
			{
				links.Add(link);
			}
		}

		return links;
	}

	internal static string? StringProperty(JsonElement entry, string name)
		=> TryGetProperty(entry, name, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;

	private static PluginManifestLink? Inspect(JsonElement entry,
		int index,
		SeenLinks seen,
		List<PluginManifestLinkProblem> problems)
	{
		if (entry.ValueKind != JsonValueKind.Object)
		{
			problems.Add(Error(index, null, $"Link {index} must be an object."));
			return null;
		}

		var countBefore = problems.Count;
		var type = ReadString(entry, index, "type", problems);
		var url = ReadString(entry, index, "url", problems);
		var label = ReadString(entry, index, "label", problems);

		if (type is null)
		{
			if (!Present(entry, "type"))
			{
				problems.Add(Error(index, "type", $"Link {index} must declare a type."));
			}
		}
		else if (string.IsNullOrWhiteSpace(type))
		{
			problems.Add(Error(index, "type", $"Link {index} has a blank type."));
		}

		if (url is null)
		{
			if (!Present(entry, "url"))
			{
				problems.Add(Error(index, "url", $"Link {index} must declare a url."));
			}
		}
		else if (!PluginManifestUrls.IsAbsoluteHttpUrl(url))
		{
			problems.Add(Error(index, "url", $"Link {index} url '{url}' must be an absolute http or https URL."));
		}

		var isStandard = PluginManifestLinkTypes.IsStandard(type);
		var isCustom = string.Equals(type, PluginManifestLinkTypes.Custom, StringComparison.Ordinal);

		if (isStandard && label is not null)
		{
			problems.Add(Error(index,
				"label",
				$"Link {index} is a '{type}' link, which Macro Deck labels itself; remove its label."));
		}
		else if (label is not null && !isStandard)
		{
			ValidateLabel(label, index, isCustom, problems);
		}
		else if (isCustom && !Present(entry, "label"))
		{
			problems.Add(Error(index, "label", $"Link {index} is a custom link and must declare a label."));
		}

		if (problems.Count > countBefore || type is null || url is null)
		{
			return null;
		}

		if (!isStandard && !isCustom)
		{
			problems.Add(new PluginManifestLinkProblem
			{
				Index = index,
				Property = "type",
				Severity = PluginManifestLinkProblemSeverity.Warning,
				Message = $"Link {index} has the unknown type '{type}' and will not be shown."
			});
			return null;
		}

		if (!seen.Urls.Add(new Uri(url, UriKind.Absolute).AbsoluteUri))
		{
			problems.Add(Error(index, "url", $"Link {index} url '{url}' is already declared by another link."));
			return null;
		}

		if (isStandard && !seen.Types.Add(type))
		{
			problems.Add(Error(index, "type", $"Link {index} repeats the '{type}' type."));
			return null;
		}

		var trimmedLabel = label?.Trim();
		if (isCustom && !seen.Labels.Add(trimmedLabel!))
		{
			problems.Add(Error(index, "label", $"Link {index} repeats the label '{trimmedLabel}'."));
			return null;
		}

		return new PluginManifestLink { Type = type, Url = url, Label = isCustom ? trimmedLabel : null };
	}

	private static void ValidateLabel(string label,
		int index,
		bool isCustom,
		List<PluginManifestLinkProblem> problems)
	{
		if (string.IsNullOrWhiteSpace(label))
		{
			problems.Add(Error(index,
				"label",
				isCustom
					? $"Link {index} is a custom link and must declare a label."
					: $"Link {index} has a blank label."));
		}
		else if (label.Any(char.IsControl))
		{
			problems.Add(Error(index, "label", $"Link {index} label must not contain control characters."));
		}
		else if (label.Length > MaxLabelLength)
		{
			problems.Add(Error(index, "label", $"Link {index} label exceeds the {MaxLabelLength}-character limit."));
		}
	}

	private static string? ReadString(JsonElement entry,
		int index,
		string name,
		List<PluginManifestLinkProblem> problems)
	{
		if (!TryGetProperty(entry, name, out var value) || value.ValueKind == JsonValueKind.Null)
		{
			return null;
		}

		if (value.ValueKind != JsonValueKind.String)
		{
			problems.Add(Error(index, name, $"Link {index} {name} must be a string."));
			return null;
		}

		return value.GetString();
	}

	private static bool Present(JsonElement entry, string name)
		=> TryGetProperty(entry, name, out var value) && value.ValueKind != JsonValueKind.Null;

	// Case-insensitive to match PluginManifestJson, which reads older PascalCase manifests.
	private static bool TryGetProperty(JsonElement entry, string name, out JsonElement value)
	{
		foreach (var property in entry.EnumerateObject())
		{
			if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
			{
				value = property.Value;
				return true;
			}
		}

		value = default;
		return false;
	}

	private static PluginManifestLinkProblem Error(int? index, string? property, string message)
		=> new()
		{
			Index = index,
			Property = property,
			Severity = PluginManifestLinkProblemSeverity.Error,
			Message = message
		};

	private sealed class SeenLinks
	{
		public HashSet<string> Urls { get; } = new(StringComparer.Ordinal);

		public HashSet<string> Types { get; } = new(StringComparer.Ordinal);

		public HashSet<string> Labels { get; } = new(StringComparer.OrdinalIgnoreCase);
	}
}
