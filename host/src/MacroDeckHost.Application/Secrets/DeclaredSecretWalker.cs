using System.Text.Json;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Application.Integrations.ConfigFlow;

namespace MacroDeckHost.Application.Secrets;

// Security boundary: decrypting, masking, export and approval classify secret positions only here.
public static class DeclaredSecretWalker
{
	private static readonly JsonElement _empty = JsonSerializer.SerializeToElement(string.Empty);

	public static Dictionary<string, ActionParameter> Index(IReadOnlyList<ActionParameter>? definitions)
	{
		var byName = new Dictionary<string, ActionParameter>(StringComparer.Ordinal);
		foreach (var definition in definitions ?? [])
		{
			byName[definition.Name] = definition;
		}

		return byName;
	}

	public static bool IsSecret(ActionParameter definition)
	{
		return definition.Type is ActionParameterType.Secret or ActionParameterType.Password;
	}

	public static JsonElement Rewrite(ActionParameter definition, JsonElement value, ISecretPositionVisitor visitor)
	{
		if (IsSecret(definition))
		{
			return visitor.Secret(value);
		}

		if (definition.Type == ActionParameterType.Object
			&& definition.Children is { Count: > 0 } children
			&& value.ValueKind == JsonValueKind.Object)
		{
			var byName = Index(children);
			var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
			foreach (var property in value.EnumerateObject())
			{
				result[property.Name] = byName.TryGetValue(property.Name, out var child)
					? Rewrite(child, property.Value, visitor)
					: visitor.Undeclared(property.Value);
			}

			return JsonSerializer.SerializeToElement(result);
		}

		if (definition.Type == ActionParameterType.Array
			&& definition.ItemTemplate is { } itemTemplate
			&& value.ValueKind == JsonValueKind.Array)
		{
			var result = new List<JsonElement>();
			foreach (var item in value.EnumerateArray())
			{
				result.Add(Rewrite(itemTemplate, item, visitor));
			}

			return JsonSerializer.SerializeToElement(result);
		}

		return visitor.Other(value);
	}

	public static IReadOnlyCollection<Guid> SecretIds(ActionParameter definition, JsonElement value)
	{
		var collector = new SecretIdCollector();
		Rewrite(definition, value, collector);
		return collector.Ids;
	}

	public static bool ContainsReference(JsonElement value)
	{
		return value.ValueKind switch
		{
			JsonValueKind.Object => IsReferenceShaped(value)
				|| value.EnumerateObject().Any(property => ContainsReference(property.Value)),
			JsonValueKind.Array => value.EnumerateArray().Any(ContainsReference),
			_ => false
		};
	}

	public static bool IsReferenceShaped(JsonElement value)
	{
		return value.ValueKind == JsonValueKind.Object && value.TryGetProperty(SecretReferenceJson.PropertyName, out _);
	}

	public static JsonElement StripReferences(JsonElement value)
	{
		if (!ContainsReference(value))
		{
			return value;
		}

		return value.ValueKind switch
		{
			JsonValueKind.Object when IsReferenceShaped(value) => _empty,
			JsonValueKind.Object => JsonSerializer.SerializeToElement(value.EnumerateObject()
				.ToDictionary(property => property.Name, property => StripReferences(property.Value))),
			JsonValueKind.Array => JsonSerializer.SerializeToElement(value.EnumerateArray()
				.Select(StripReferences)
				.ToList()),
			_ => value
		};
	}

	private sealed class SecretIdCollector : ISecretPositionVisitor
	{
		public HashSet<Guid> Ids { get; } = [];

		public JsonElement Secret(JsonElement value)
		{
			if (SecretReferenceJson.TryGet(value, out var id))
			{
				Ids.Add(id);
			}

			return value;
		}

		public JsonElement Undeclared(JsonElement value)
		{
			return value;
		}

		public JsonElement Other(JsonElement value)
		{
			return value;
		}
	}
}

public interface ISecretPositionVisitor
{
	JsonElement Secret(JsonElement value);

	JsonElement Undeclared(JsonElement value);

	JsonElement Other(JsonElement value);
}
