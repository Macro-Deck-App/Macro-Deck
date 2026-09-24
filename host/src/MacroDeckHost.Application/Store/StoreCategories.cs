using System.Text.Json;
using System.Text.RegularExpressions;
using MacroDeckHost.Application.Store.Model;

namespace MacroDeckHost.Application.Store;

// Read from untrusted registry JSON: an entry of any other shape is dropped on its own, so one bad
// category never costs the others or the refresh that carries them.
public static partial class StoreCategories
{
	public const int SupportedVersion = 1;

	public const string FallbackLanguage = "en";

	public const int MaxNameLength = 64;

	public static IReadOnlyList<StoreCategory> Normalize(int version, JsonElement? value)
	{
		if (version != SupportedVersion || value is not { ValueKind: JsonValueKind.Array } array)
		{
			return [];
		}

		var categories = new List<StoreCategory>();
		var seen = new HashSet<string>(StringComparer.Ordinal);
		foreach (var entry in array.EnumerateArray())
		{
			if (entry.ValueKind != JsonValueKind.Object ||
				!entry.TryGetProperty("id", out var idElement) ||
				idElement.ValueKind != JsonValueKind.String ||
				idElement.GetString() is not { } id ||
				!StoreTags.IsValid(id) ||
				!entry.TryGetProperty("names", out var namesElement) ||
				ReadNames(namesElement) is not { } names ||
				!seen.Add(id))
			{
				continue;
			}

			categories.Add(new StoreCategory { Id = id, Names = names });
		}

		return categories;
	}

	private static Dictionary<string, string>? ReadNames(JsonElement value)
	{
		if (value.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		var names = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var property in value.EnumerateObject())
		{
			var name = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()?.Trim() : null;
			if (!LanguagePattern().IsMatch(property.Name) ||
				string.IsNullOrEmpty(name) ||
				name.Length > MaxNameLength)
			{
				continue;
			}

			names[property.Name] = name;
		}

		return names.ContainsKey(FallbackLanguage) ? names : null;
	}

	[GeneratedRegex("^[a-z]{2}$", RegexOptions.CultureInvariant)]
	private static partial Regex LanguagePattern();
}
