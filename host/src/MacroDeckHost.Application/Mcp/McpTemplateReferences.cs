using System.Text.Json;
using System.Text.RegularExpressions;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Secrets.Approvals;

namespace MacroDeckHost.Application.Mcp;

public sealed record McpBlockReferences(IReadOnlySet<string> Variables, IReadOnlySet<string> EventParameters);

public static partial class McpTemplateReferences
{
	public static McpBlockReferences OfNonSecretParameters(FlowBlockContent block)
	{
		var variables = new HashSet<string>(StringComparer.Ordinal);
		var events = new HashSet<string>(StringComparer.Ordinal);
		foreach (var parameter in block.Parameters)
		{
			if (parameter.Value.ValueKind == JsonValueKind.Object && SecretReferenceJson.TryGet(parameter.Value, out _))
			{
				continue;
			}

			Collect(parameter.Value, variables, events);
		}

		return new McpBlockReferences(variables, events);
	}

	private static void Collect(JsonElement value, HashSet<string> variables, HashSet<string> events)
	{
		switch (value.ValueKind)
		{
			case JsonValueKind.String:
				foreach (Match match in LiquidReference().Matches(value.GetString() ?? string.Empty))
				{
					var name = match.Groups["dotted"].Success
						? match.Groups["dotted"].Value
						: match.Groups["quoted"].Value;
					(match.Groups["root"].Value == "event" ? events : variables).Add(name);
				}

				break;
			case JsonValueKind.Object:
				foreach (var property in value.EnumerateObject())
				{
					if (property.Value.ValueKind == JsonValueKind.String && property.Name is "$var" or "$event")
					{
						(property.Name == "$event" ? events : variables).Add(property.Value.GetString()
							?? string.Empty);
					}
					else
					{
						Collect(property.Value, variables, events);
					}
				}

				break;
			case JsonValueKind.Array:
				foreach (var item in value.EnumerateArray())
				{
					Collect(item, variables, events);
				}

				break;
		}
	}

	[GeneratedRegex(
		"""\b(?<root>vars|event)(?:\.(?<dotted>[A-Za-z_][A-Za-z0-9_]*)|\[\s*["'](?<quoted>[^"']+)["']\s*\])""")]
	private static partial Regex LiquidReference();
}
