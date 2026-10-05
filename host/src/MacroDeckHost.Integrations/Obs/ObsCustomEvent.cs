using System.Text.Json;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace MacroDeckHost.Integrations.Obs;

internal static partial class ObsCustomEvent
{
	public const int MaxPayloadChars = 16 * 1024;

	public const int MaxFields = 16;

	public const int MaxFieldValueChars = 1024;

	public const int MaxEventNameChars = 256;

	public const string FieldPrefix = "field_";

	public const string EventNameKey = "eventName";

	public const string DataKey = "data";

	private const int MaxDepth = 32;

	[GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
	private static partial Regex FieldNamePattern();

	public static string Serialize(JObject? data)
		=> data is null || ExceedsDepth(data, 0) ? string.Empty : data.ToString(Newtonsoft.Json.Formatting.None);

	public static IReadOnlyDictionary<string, object?>? Parse(string? json)
	{
		if (string.IsNullOrEmpty(json) || json.Length > MaxPayloadChars)
		{
			return null;
		}

		try
		{
			using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = MaxDepth });
			var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				[EventNameKey] = string.Empty,
				[DataKey] = json
			};

			if (document.RootElement.ValueKind != JsonValueKind.Object)
			{
				return payload;
			}

			var fields = 0;
			foreach (var property in document.RootElement.EnumerateObject())
			{
				if (property.NameEquals(EventNameKey))
				{
					if (property.Value.ValueKind == JsonValueKind.String)
					{
						payload[EventNameKey] = Truncate(property.Value.GetString(), MaxEventNameChars);
					}

					continue;
				}

				if (!FieldNamePattern().IsMatch(property.Name) || !TryReadScalar(property.Value, out var value))
				{
					continue;
				}

				var key = FieldPrefix + property.Name;
				var known = payload.ContainsKey(key);
				if (!known && fields >= MaxFields)
				{
					continue;
				}

				fields += known ? 0 : 1;
				payload[key] = value;
			}

			return payload;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static bool TryReadScalar(JsonElement element, out object? value)
	{
		switch (element.ValueKind)
		{
			case JsonValueKind.String:
				value = Truncate(element.GetString(), MaxFieldValueChars);
				return true;
			case JsonValueKind.True:
				value = true;
				return true;
			case JsonValueKind.False:
				value = false;
				return true;
			case JsonValueKind.Number when element.TryGetInt64(out var integer):
				value = integer;
				return true;
			case JsonValueKind.Number when element.TryGetDouble(out var number) && double.IsFinite(number):
				value = number;
				return true;
			default:
				value = null;
				return false;
		}
	}

	private static string Truncate(string? text, int max)
		=> text is null ? string.Empty : text.Length <= max ? text : text[..max];

	private static bool ExceedsDepth(JToken token, int depth)
	{
		if (depth > MaxDepth)
		{
			return true;
		}

		foreach (var child in token.Children())
		{
			if (ExceedsDepth(child, depth + 1))
			{
				return true;
			}
		}

		return false;
	}
}
