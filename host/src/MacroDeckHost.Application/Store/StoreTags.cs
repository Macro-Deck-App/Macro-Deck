using System.Text.Json;
using System.Text.RegularExpressions;

namespace MacroDeckHost.Application.Store;

// Mirrors the Creator Portal's tag rule. Read from untrusted registry JSON, so any other shape is dropped
// rather than failing the refresh that carries it.
public static partial class StoreTags
{
	public const int MaxTags = 10;

	public const int MaxTagLength = 32;

	public static IReadOnlyList<string> Normalize(JsonElement? value)
	{
		if (value is not { ValueKind: JsonValueKind.Array } array)
		{
			return [];
		}

		var tags = new List<string>();
		foreach (var entry in array.EnumerateArray())
		{
			var tag = entry.ValueKind == JsonValueKind.String ? entry.GetString()?.Trim().ToLowerInvariant() : null;
			if (tag is null || tag.Length > MaxTagLength || !TagPattern().IsMatch(tag) || tags.Contains(tag))
			{
				continue;
			}

			tags.Add(tag);
			if (tags.Count == MaxTags)
			{
				break;
			}
		}

		return tags;
	}

	[GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
	private static partial Regex TagPattern();
}
