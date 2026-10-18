using System.Collections;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MacroDeckHost.Integrations.DiscordWebhooks.Models;

// The flat key schema of the message parameter. Stored actions depend on these keys, so they never change.
internal static partial class DiscordMessageMap
{
	public const string Content = "content";
	public const string Username = "username";
	public const string AvatarUrl = "avatar_url";
	public const string AllowedParse = "allowed.parse";
	public const string AllowedUsers = "allowed.users";
	public const string AllowedRoles = "allowed.roles";
	public const string NamesPrefix = "names.";
	public const string EmbedsPrefix = "embeds.";

	public const string ParseUsers = "users";
	public const string ParseRoles = "roles";
	public const string ParseEveryone = "everyone";

	public const string TimestampNow = "now";

	private const string FieldsSegment = "fields";

	public static Dictionary<string, string> ToMap(DiscordMessageDraft draft)
	{
		var map = new Dictionary<string, string>(StringComparer.Ordinal);
		Put(map, Content, draft.Content);
		Put(map, Username, draft.Username);
		Put(map, AvatarUrl, draft.AvatarUrl);

		var parse = new List<string>();
		if (draft.PingUsers)
		{
			parse.Add(ParseUsers);
		}

		if (draft.PingRoles)
		{
			parse.Add(ParseRoles);
		}

		if (draft.PingEveryone)
		{
			parse.Add(ParseEveryone);
		}

		Put(map, AllowedParse, string.Join(",", parse));
		Put(map, AllowedUsers, string.Join(",", draft.AllowedUsers));
		Put(map, AllowedRoles, string.Join(",", draft.AllowedRoles));

		for (var i = 0; i < draft.Embeds.Count; i++)
		{
			var embed = draft.Embeds[i];
			var prefix = string.Create(CultureInfo.InvariantCulture, $"{EmbedsPrefix}{i}.");
			Put(map, prefix + "title", embed.Title);
			Put(map, prefix + "description", embed.Description);
			Put(map, prefix + "url", embed.Url);
			Put(map, prefix + "color", embed.Color);
			Put(map, prefix + "timestamp", embed.Timestamp);
			Put(map, prefix + "author.name", embed.AuthorName);
			Put(map, prefix + "author.url", embed.AuthorUrl);
			Put(map, prefix + "author.icon_url", embed.AuthorIconUrl);
			Put(map, prefix + "footer.text", embed.FooterText);
			Put(map, prefix + "footer.icon_url", embed.FooterIconUrl);
			Put(map, prefix + "image", embed.Image);
			Put(map, prefix + "thumbnail", embed.Thumbnail);

			for (var j = 0; j < embed.Fields.Count; j++)
			{
				var field = embed.Fields[j];
				var fieldPrefix = string.Create(CultureInfo.InvariantCulture, $"{prefix}{FieldsSegment}.{j}.");
				map[fieldPrefix + "name"] = field.Name;
				map[fieldPrefix + "value"] = field.Value;
				if (field.Inline)
				{
					map[fieldPrefix + "inline"] = "true";
				}
			}
		}

		foreach (var (id, name) in draft.Names)
		{
			Put(map, NamesPrefix + id, name);
		}

		return map;
	}

	public static DiscordMessageDraft Parse(IReadOnlyDictionary<string, string> map)
	{
		var parse = SplitList(Get(map, AllowedParse));
		var embeds = new SortedDictionary<int, Dictionary<string, string>>();
		var names = new Dictionary<string, string>(StringComparer.Ordinal);

		foreach (var (key, value) in map)
		{
			if (key.StartsWith(NamesPrefix, StringComparison.Ordinal))
			{
				var id = key[NamesPrefix.Length..];
				if (id.Length > 0 && value.Trim().Length > 0)
				{
					names[id] = value.Trim();
				}

				continue;
			}

			if (!key.StartsWith(EmbedsPrefix, StringComparison.Ordinal))
			{
				continue;
			}

			var rest = key[EmbedsPrefix.Length..];
			var dot = rest.IndexOf('.');
			if (dot <= 0 || !TryIndex(rest[..dot], out var index))
			{
				continue;
			}

			if (!embeds.TryGetValue(index, out var parts))
			{
				parts = new Dictionary<string, string>(StringComparer.Ordinal);
				embeds[index] = parts;
			}

			parts[rest[(dot + 1)..]] = value;
		}

		return new DiscordMessageDraft
		{
			Content = Get(map, Content),
			Username = Get(map, Username),
			AvatarUrl = Get(map, AvatarUrl),
			PingUsers = parse.Contains(ParseUsers, StringComparer.OrdinalIgnoreCase),
			PingRoles = parse.Contains(ParseRoles, StringComparer.OrdinalIgnoreCase),
			PingEveryone = parse.Contains(ParseEveryone, StringComparer.OrdinalIgnoreCase),
			AllowedUsers = SplitList(Get(map, AllowedUsers)),
			AllowedRoles = SplitList(Get(map, AllowedRoles)),
			Embeds = embeds.Values.Select(ParseEmbed).ToList(),
			Names = names
		};
	}

	public static IReadOnlyList<string> SplitList(string? value)
	{
		return string.IsNullOrWhiteSpace(value)
			? []
			: ListEntryRegex().Matches(value).Select(match => match.Value).ToList();
	}

	[GeneratedRegex(@"\{\{.*?\}\}|\{%.*?%\}|[^\s,;]+", RegexOptions.Singleline)]
	private static partial Regex ListEntryRegex();

	public static Dictionary<string, string> Read(object? raw)
	{
		var map = new Dictionary<string, string>(StringComparer.Ordinal);
		switch (raw)
		{
			case IReadOnlyDictionary<string, string> strings:
				foreach (var (key, value) in strings)
				{
					map[key] = value;
				}

				break;
			case JsonElement element:
				return Read(element);
			case string json when json.TrimStart().StartsWith('{'):
				try
				{
					using (var document = JsonDocument.Parse(json))
					{
						return Read(document.RootElement);
					}
				}
				catch (JsonException)
				{
					return map;
				}
			case IDictionary dictionary:
				foreach (DictionaryEntry entry in dictionary)
				{
					if (entry.Key is string key)
					{
						map[key] = Convert.ToString(entry.Value, CultureInfo.InvariantCulture) ?? string.Empty;
					}
				}

				break;
		}

		return map;
	}

	public static Dictionary<string, string> Read(JsonElement element)
	{
		var map = new Dictionary<string, string>(StringComparer.Ordinal);
		if (element.ValueKind != JsonValueKind.Object)
		{
			return map;
		}

		foreach (var property in element.EnumerateObject())
		{
			map[property.Name] = property.Value.ValueKind == JsonValueKind.String
				? property.Value.GetString() ?? string.Empty
				: property.Value.ToString();
		}

		return map;
	}

	private static DiscordEmbedDraft ParseEmbed(Dictionary<string, string> parts)
	{
		var fields = new SortedDictionary<int, Dictionary<string, string>>();
		foreach (var (key, value) in parts)
		{
			if (!key.StartsWith(FieldsSegment + ".", StringComparison.Ordinal))
			{
				continue;
			}

			var rest = key[(FieldsSegment.Length + 1)..];
			var dot = rest.IndexOf('.');
			if (dot <= 0 || !TryIndex(rest[..dot], out var index))
			{
				continue;
			}

			if (!fields.TryGetValue(index, out var field))
			{
				field = new Dictionary<string, string>(StringComparer.Ordinal);
				fields[index] = field;
			}

			field[rest[(dot + 1)..]] = value;
		}

		return new DiscordEmbedDraft
		{
			Title = Get(parts, "title"),
			Description = Get(parts, "description"),
			Url = Get(parts, "url"),
			Color = Get(parts, "color"),
			Timestamp = Get(parts, "timestamp"),
			AuthorName = Get(parts, "author.name"),
			AuthorUrl = Get(parts, "author.url"),
			AuthorIconUrl = Get(parts, "author.icon_url"),
			FooterText = Get(parts, "footer.text"),
			FooterIconUrl = Get(parts, "footer.icon_url"),
			Image = Get(parts, "image"),
			Thumbnail = Get(parts, "thumbnail"),
			Fields = fields.Values
				.Select(field => new DiscordFieldDraft(Get(field, "name"),
					Get(field, "value"),
					bool.TryParse(Get(field, "inline"), out var inline) && inline))
				.ToList()
		};
	}

	private static bool TryIndex(string text, out int index)
	{
		return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out index) && index < 1000;
	}

	private static string Get(IReadOnlyDictionary<string, string> map, string key)
	{
		return map.TryGetValue(key, out var value) ? value : string.Empty;
	}

	private static void Put(Dictionary<string, string> map, string key, string value)
	{
		if (value.Length > 0)
		{
			map[key] = value;
		}
	}
}
