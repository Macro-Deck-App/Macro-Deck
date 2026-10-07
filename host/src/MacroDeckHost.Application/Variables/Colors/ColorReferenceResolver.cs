using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Variables.Colors;

// A reference whose variable is missing, unavailable or not a Color resolves to an empty string, which
// every colour consumer already reads as unset.
public interface IColorReferenceResolver
{
	string Resolve(string value, VariableScope scope = VariableScope.Global, string? scopeRefId = null);

	RgbaColor? ResolveColor(ColorReference reference, VariableScope scope = VariableScope.Global,
		string? scopeRefId = null);

	string? ResolveData(string? json, Guid? widgetId);

	JsonElement ResolveData(JsonElement data, Guid? widgetId);
}

public sealed class ColorReferenceResolver : IColorReferenceResolver
{
	private readonly VariableRegistry _registry;

	public ColorReferenceResolver(VariableRegistry registry)
	{
		_registry = registry;
	}

	public string Resolve(string value, VariableScope scope = VariableScope.Global, string? scopeRefId = null)
		=> ColorReference.TryParse(value, out var reference)
			? ResolveColor(reference, scope, scopeRefId)?.ToString() ?? string.Empty
			: value;

	public RgbaColor? ResolveColor(ColorReference reference, VariableScope scope = VariableScope.Global,
		string? scopeRefId = null)
	{
		if (ColorOf(reference.Variable, scope, scopeRefId) is not { } color)
		{
			return null;
		}

		foreach (var step in reference.Steps)
		{
			RgbaColor? applied = step.Modifier switch
			{
				ColorModifier.Lighten => color.Lighten(step.Amount),
				ColorModifier.Darken => color.Darken(step.Amount),
				ColorModifier.Saturate => color.Saturate(step.Amount),
				ColorModifier.Desaturate => color.Desaturate(step.Amount),
				ColorModifier.Opacity => color.WithOpacity(step.Amount),
				ColorModifier.IncreaseOpacity => color.IncreaseOpacity(step.Amount),
				ColorModifier.ReduceOpacity => color.ReduceOpacity(step.Amount),
				ColorModifier.Hue => color.ShiftHue(step.Amount),
				_ => MixTarget(step, scope, scopeRefId) is { } other ? color.Mix(other, step.Amount) : null
			};

			if (applied is not { } next)
			{
				return null;
			}

			color = next;
		}

		return color;
	}

	public string? ResolveData(string? json, Guid? widgetId)
	{
		if (string.IsNullOrWhiteSpace(json) || !ColorReference.MightBeReference(json))
		{
			return json;
		}

		JsonNode? root;
		try
		{
			root = JsonNode.Parse(json);
		}
		catch (JsonException)
		{
			return json;
		}

		return root is not null && Rewrite(root, ScopeOf(widgetId), widgetId?.ToString())
			? root.ToJsonString()
			: json;
	}

	public JsonElement ResolveData(JsonElement data, Guid? widgetId)
	{
		if (data.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array) ||
			!ColorReference.MightBeReference(data.GetRawText()))
		{
			return data;
		}

		var root = JsonNode.Parse(data.GetRawText());
		return root is not null && Rewrite(root, ScopeOf(widgetId), widgetId?.ToString())
			? JsonSerializer.SerializeToElement(root)
			: data;
	}

	private static VariableScope ScopeOf(Guid? widgetId) => widgetId is null ? VariableScope.Global : VariableScope.Widget;

	private bool Rewrite(JsonNode node, VariableScope scope, string? scopeRefId)
	{
		var changed = false;
		switch (node)
		{
			case JsonObject obj:
				foreach (var key in obj.Select(pair => pair.Key).ToList())
				{
					changed |= RewriteChild(obj[key], replacement => obj[key] = replacement, scope, scopeRefId);
				}

				break;
			case JsonArray array:
				for (var i = 0; i < array.Count; i++)
				{
					var index = i;
					changed |= RewriteChild(array[i], replacement => array[index] = replacement, scope, scopeRefId);
				}

				break;
		}

		return changed;
	}

	private bool RewriteChild(JsonNode? child, Action<JsonNode> replace, VariableScope scope, string? scopeRefId)
	{
		if (child is JsonValue value)
		{
			if (value.GetValueKind() != JsonValueKind.String ||
				!ColorReference.TryParse(value.GetValue<string>(), out var reference))
			{
				return false;
			}

			replace(JsonValue.Create(ResolveColor(reference, scope, scopeRefId)?.ToString() ?? string.Empty));
			return true;
		}

		return child is not null && Rewrite(child, scope, scopeRefId);
	}

	private RgbaColor? MixTarget(ColorStep step, VariableScope scope, string? scopeRefId)
		=> step.MixColor ?? (step.MixVariable is { } name ? ColorOf(name, scope, scopeRefId) : null);

	private RgbaColor? ColorOf(string name, VariableScope scope, string? scopeRefId)
	{
		var entity = Find(name, scope, scopeRefId);
		return entity is { Type: VariableType.Color } &&
			_registry.IsAvailable(entity.Id) &&
			RgbaColor.TryParse(entity.Value, out var color)
				? color
				: null;
	}

	private VariableEntity? Find(string name, VariableScope scope, string? scopeRefId)
		=> (scope != VariableScope.Global && !string.IsNullOrEmpty(scopeRefId)
				? _registry.FindByName(scope, scopeRefId, name)
				: null) ??
			_registry.FindByName(VariableScope.Global, null, name);
}

public static class ColorReferenceResolverExtensions
{
	public static WidgetEntity Resolved(this IColorReferenceResolver colors, WidgetEntity widget)
	{
		var data = colors.ResolveData(widget.Data, widget.Id);
		return ReferenceEquals(data, widget.Data)
			? widget
			: new WidgetEntity
			{
				Id = widget.Id,
				CreatedAt = widget.CreatedAt,
				FolderId = widget.FolderId,
				Type = widget.Type,
				PositionX = widget.PositionX,
				PositionY = widget.PositionY,
				Width = widget.Width,
				Height = widget.Height,
				Data = data,
				IsPinned = widget.IsPinned,
				PinScope = widget.PinScope
			};
	}
}
