using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacroDeck.Plugin.Packaging.Manifest;

// A declaration must never make a package unreadable, and a flag it cannot read must never turn into an
// explicit "no AI": either case degrades the whole declaration to "not declared" instead of throwing.
internal sealed class PackageAiDeclarationConverter : JsonConverter<PackageAiDeclaration?>
{
	public override PackageAiDeclaration? Read(ref Utf8JsonReader reader,
		Type typeToConvert,
		JsonSerializerOptions options)
	{
		using var document = JsonDocument.ParseValue(ref reader);
		var root = document.RootElement;
		if (root.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		if (!TryFlag(root, "interaction", out var interaction) ||
			!TryFlag(root, "generatedContent", out var generatedContent) ||
			!TryFlag(root, "generatedAssets", out var generatedAssets) ||
			!TryServices(root, out var services))
		{
			return null;
		}

		return new PackageAiDeclaration
		{
			Interaction = interaction,
			GeneratedContent = generatedContent,
			GeneratedAssets = generatedAssets,
			Services = services
		};
	}

	public override void Write(Utf8JsonWriter writer, PackageAiDeclaration? value, JsonSerializerOptions options)
	{
		if (value is null)
		{
			writer.WriteNullValue();
			return;
		}

		writer.WriteStartObject();
		writer.WriteBoolean("interaction", value.Interaction);
		writer.WriteBoolean("generatedContent", value.GeneratedContent);
		writer.WriteBoolean("generatedAssets", value.GeneratedAssets);
		if (value.Services is { Count: > 0 } services)
		{
			writer.WriteStartArray("services");
			foreach (var service in services)
			{
				writer.WriteStringValue(service);
			}

			writer.WriteEndArray();
		}

		writer.WriteEndObject();
	}

	private static bool TryFlag(JsonElement root, string name, out bool flag)
	{
		flag = false;
		if (!TryGetProperty(root, name, out var value))
		{
			return true;
		}

		switch (value.ValueKind)
		{
			case JsonValueKind.True:
				flag = true;
				return true;
			case JsonValueKind.False:
				return true;
			default:
				return false;
		}
	}

	private static bool TryServices(JsonElement root, out List<string>? services)
	{
		services = null;
		if (!TryGetProperty(root, "services", out var value))
		{
			return true;
		}

		if (value.ValueKind != JsonValueKind.Array)
		{
			return false;
		}

		var kept = new List<string>();
		foreach (var entry in value.EnumerateArray())
		{
			var service = entry.ValueKind == JsonValueKind.String ? entry.GetString()?.Trim() : null;
			if (string.IsNullOrEmpty(service) ||
				service.Length > PackageAiDeclaration.MaxServiceLength ||
				kept.Contains(service, StringComparer.OrdinalIgnoreCase))
			{
				continue;
			}

			kept.Add(service);
			if (kept.Count == PackageAiDeclaration.MaxServices)
			{
				break;
			}
		}

		services = kept.Count > 0 ? kept : null;
		return kept.Count > 0 || value.GetArrayLength() == 0;
	}

	private static bool TryGetProperty(JsonElement root, string name, out JsonElement value)
	{
		foreach (var property in root.EnumerateObject())
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
}
