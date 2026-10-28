using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeckHost.Application.Integrations.ConfigFlow;

namespace MacroDeckHost.Application.Portable;

public static class PortableSecretReferences
{
	public static IReadOnlySet<Guid> CarriedBy(PortableContent content, IReadOnlyDictionary<Guid, Guid> idMap)
	{
		return content.Secrets
			.Select(secret => idMap.TryGetValue(secret.Id, out var mapped) ? mapped : Guid.Empty)
			.Where(id => id != Guid.Empty)
			.ToHashSet();
	}

	public static string? Remap(string? data, IReadOnlyDictionary<Guid, Guid> idMap, IReadOnlySet<Guid> carried)
	{
		return KeepOnly(PortableGuidRemapper.Remap(data, idMap), carried);
	}

	// An archive may name any secret id; only the secrets it carries itself may stay referenced, otherwise
	// importing it would point the new blocks at secrets already on this host. Parsed, never pattern-matched.
	public static string? KeepOnly(string? data, IReadOnlySet<Guid> allowed)
	{
		if (string.IsNullOrEmpty(data) || TryParse(data) is not { } root)
		{
			return data;
		}

		return Strip(root, allowed, out var stripped) ? stripped?.ToJsonString() : data;
	}

	private static JsonNode? TryParse(string text)
	{
		try
		{
			return JsonNode.Parse(text);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static bool Strip(JsonNode? node, IReadOnlySet<Guid> allowed, out JsonNode? result)
	{
		result = node;
		switch (node)
		{
			case JsonObject reference when reference.ContainsKey(SecretReferenceJson.PropertyName):
				if (SecretReferenceJson.TryGet(JsonSerializer.SerializeToElement(reference), out var id)
					&& allowed.Contains(id))
				{
					return false;
				}

				result = JsonValue.Create(string.Empty);
				return true;
			case JsonObject container:
			{
				var changed = false;
				foreach (var name in container.Select(property => property.Key).ToList())
				{
					var child = container[name];
					if (Strip(child, allowed, out var replacement))
					{
						if (!ReferenceEquals(replacement, child))
						{
							container[name] = replacement;
						}

						changed = true;
					}
				}

				return changed;
			}
			case JsonArray array:
			{
				var changed = false;
				for (var index = 0; index < array.Count; index++)
				{
					var item = array[index];
					if (Strip(item, allowed, out var replacement))
					{
						if (!ReferenceEquals(replacement, item))
						{
							array[index] = replacement;
						}

						changed = true;
					}
				}

				return changed;
			}
			case JsonValue value when value.TryGetValue<string>(out var text) && LooksLikeJson(text):
			{
				var nested = TryParse(text);
				if (nested is null || !Strip(nested, allowed, out var strippedNested))
				{
					return false;
				}

				result = JsonValue.Create(strippedNested?.ToJsonString());
				return true;
			}
			default:
				return false;
		}
	}

	private static bool LooksLikeJson(string text)
	{
		var trimmed = text.AsSpan().TrimStart();
		return trimmed.Length > 0 && trimmed[0] is '{' or '[';
	}
}
