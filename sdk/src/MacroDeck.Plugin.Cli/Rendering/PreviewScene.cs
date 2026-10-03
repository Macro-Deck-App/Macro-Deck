using System.Text.Json;

namespace MacroDeck.Plugin.Cli.Rendering;

internal static class PreviewScene
{
	public static string Build(JsonElement root,
		PreviewSize size,
		PreviewRenderOptions options,
		IReadOnlyDictionary<string, string> resources,
		IReadOnlyDictionary<string, string> translations)
	{
		using var stream = new MemoryStream();

		using (var writer = new Utf8JsonWriter(stream))
		{
			writer.WriteStartObject();
			writer.WriteNumber("width", size.Width);
			writer.WriteNumber("height", size.Height);
			writer.WriteNumber("radius", options.Radius);
			writer.WriteString("theme", options.Theme == PreviewTheme.Light ? "light" : "dark");
			writer.WriteString("background", options.Background);
			writer.WriteString("locale", options.Locale);
			writer.WriteStartObject("resources");

			foreach (var (id, url) in resources)
			{
				writer.WriteString(id, url);
			}

			writer.WriteEndObject();
			writer.WriteStartObject("translations");

			foreach (var (key, template) in translations)
			{
				writer.WriteString(key, template);
			}

			writer.WriteEndObject();
			writer.WritePropertyName("root");
			root.WriteTo(writer);
			writer.WriteEndObject();
		}

		return System.Text.Encoding.UTF8.GetString(stream.ToArray());
	}

	public static IEnumerable<string> ResourceIds(JsonElement element)
	{
		switch (element.ValueKind)
		{
			case JsonValueKind.Object:
				foreach (var property in element.EnumerateObject())
				{
					if (property.Name == "resourceId" && property.Value.ValueKind == JsonValueKind.String)
					{
						yield return property.Value.GetString()!;
					}
					else
					{
						foreach (var id in ResourceIds(property.Value))
						{
							yield return id;
						}
					}
				}

				break;
			case JsonValueKind.Array:
				foreach (var item in element.EnumerateArray())
				{
					foreach (var id in ResourceIds(item))
					{
						yield return id;
					}
				}

				break;
		}
	}
}
