using System.Text.Json;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Rendering;

internal static class ProviderParameters
{
	public static async Task<Dictionary<string, object?>> ResolveAsync(
		ActionBlock block,
		Guid widgetId,
		IVariableTemplateRenderer renderer)
	{
		var parameters = ActionParameterConverter.ToNullable(block.Parameters.ToDictionary(p => p.Name, p => p.Value));
		var references = block.Parameters.Where(p => IsVariableReference(p.Value)).ToList();
		if (references.Count == 0)
		{
			return parameters;
		}

		var context = await renderer.CreateContextAsync(VariableScope.Widget, widgetId.ToString());
		foreach (var reference in references)
		{
			parameters[reference.Name] = ActionConditionEvaluator.ResolveVariableReference(reference.Value, context);
		}

		return parameters;
	}

	public static bool HasVariableReference(ActionBlock block)
	{
		return block.Parameters.Any(p => IsVariableReference(p.Value));
	}

	private static bool IsVariableReference(JsonElement value)
	{
		return value.ValueKind == JsonValueKind.Object
			&& value.TryGetProperty("$var", out var name)
			&& name.ValueKind == JsonValueKind.String;
	}
}
