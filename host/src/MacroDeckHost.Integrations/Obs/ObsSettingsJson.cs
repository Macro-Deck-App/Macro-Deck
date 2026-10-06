using System.Text.Json;

namespace MacroDeckHost.Integrations.Obs;

internal static class ObsSettingsJson
{
	public static bool TryGetKeys(string json, out List<string> keys)
	{
		keys = [];

		if (!TryParseObject(json, out var document))
		{
			return false;
		}

		using (document)
		{
			foreach (var property in document.RootElement.EnumerateObject())
			{
				keys.Add(property.Name);
			}

			return true;
		}
	}

	public static bool TryGetValueKind(string json, string key, out JsonValueKind? kind)
	{
		kind = null;

		if (!TryParseObject(json, out var document))
		{
			return false;
		}

		using (document)
		{
			if (document.RootElement.TryGetProperty(key, out var element) &&
				element.ValueKind != JsonValueKind.Null)
			{
				kind = element.ValueKind;
			}

			return true;
		}
	}

	private static bool TryParseObject(string json, out JsonDocument document)
	{
		try
		{
			document = JsonDocument.Parse(json);
		}
		catch (JsonException)
		{
			document = null!;
			return false;
		}

		if (document.RootElement.ValueKind == JsonValueKind.Object)
		{
			return true;
		}

		document.Dispose();
		return false;
	}
}
