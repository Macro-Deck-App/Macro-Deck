using System.Text.Json;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Model.Identity;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Serialization;

namespace MacroDeckHost.Application.Notifications;

public enum NotificationFieldKind
{
	Text,

	Toggle,

	Slider,

	Segmented
}

public sealed record NotificationField(string Id, NotificationFieldKind Kind, int ChildCount, JsonElement Seed);

public sealed record NotificationContentInfo(IReadOnlyList<NotificationField> Fields, int SizeBytes);

public static class NotificationContent
{
	public static NotificationContentInfo Inspect(UiNode tree)
	{
		ArgumentNullException.ThrowIfNull(tree);

		byte[] bytes;
		try
		{
			bytes = UiCanonicalJson.SerializeToUtf8Bytes(tree);
		}
		catch (Exception exception) when (exception is JsonException or NotSupportedException)
		{
			throw Invalid("The content could not be serialized.");
		}

		if (bytes.Length > NotificationLimits.MaxContentBytes)
		{
			throw new NotificationException(NotificationErrorCode.LimitExceeded,
				$"The content is larger than {NotificationLimits.MaxContentBytes} bytes.");
		}

		var fields = new List<NotificationField>();
		var ids = new HashSet<string>(StringComparer.Ordinal);
		var count = 0;
		Walk(tree, 1, fields, ids, ref count);

		return new NotificationContentInfo(fields, bytes.Length);
	}

	public static bool TryValidateValues(
		IReadOnlyList<NotificationField> fields,
		IReadOnlyDictionary<string, JsonElement>? provided,
		out Dictionary<string, JsonElement> values,
		out string? error)
	{
		values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
		error = null;

		foreach (var field in fields)
		{
			values[field.Id] = field.Seed;
		}

		if (provided is null)
		{
			return true;
		}

		foreach (var (id, value) in provided)
		{
			var field = fields.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.Ordinal));
			if (field is null)
			{
				error = $"'{id}' is not an input of this notification.";
				return false;
			}

			if (!IsValid(field, value))
			{
				error = $"The value of '{id}' is not valid.";
				return false;
			}

			values[id] = value.Clone();
		}

		return true;
	}

	private static bool IsValid(NotificationField field, JsonElement value)
	{
		return field.Kind switch
		{
			NotificationFieldKind.Text => value.ValueKind == JsonValueKind.String
				&& (value.GetString()?.Length ?? 0) <= NotificationLimits.MaxTextFieldLength,
			NotificationFieldKind.Toggle => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
			NotificationFieldKind.Slider => value.ValueKind == JsonValueKind.Number
				&& value.TryGetDouble(out var level)
				&& double.IsFinite(level)
				&& level is >= 0 and <= 1,
			NotificationFieldKind.Segmented => value.ValueKind == JsonValueKind.Number
				&& value.TryGetInt32(out var index)
				&& index >= 0
				&& index < field.ChildCount,
			_ => false
		};
	}

	private static void Walk(
		UiNode node,
		int depth,
		List<NotificationField> fields,
		HashSet<string> ids,
		ref int count)
	{
		if (++count > NotificationLimits.MaxContentNodes)
		{
			throw new NotificationException(NotificationErrorCode.LimitExceeded,
				$"The content has more than {NotificationLimits.MaxContentNodes} nodes.");
		}

		if (depth > NotificationLimits.MaxContentDepth)
		{
			throw new NotificationException(NotificationErrorCode.LimitExceeded,
				$"The content is nested deeper than {NotificationLimits.MaxContentDepth} levels.");
		}

		if (!UiIdentifier.IsValid(node.Id) || !ids.Add(node.Id))
		{
			throw Invalid($"The node id '{node.Id}' is not valid or not unique.");
		}

		if (ToField(node) is { } field)
		{
			fields.Add(field);
		}

		foreach (var child in node.Children)
		{
			Walk(child, depth + 1, fields, ids, ref count);
		}

		if (node.Fallback is { } fallback)
		{
			Walk(fallback, depth + 1, fields, ids, ref count);
		}
	}

	private static NotificationField? ToField(UiNode node)
	{
		var kind = node.Type switch
		{
			UiComponents.TextField => NotificationFieldKind.Text,
			UiComponents.Toggle => NotificationFieldKind.Toggle,
			UiComponents.Slider => NotificationFieldKind.Slider,
			UiComponents.Segmented => NotificationFieldKind.Segmented,
			_ => (NotificationFieldKind?)null
		};

		if (kind is null || !DeclaresValueEvent(node))
		{
			return null;
		}

		var seed = kind switch
		{
			NotificationFieldKind.Text => Seed(node, UiComponentProperties.Text, JsonValueKind.String, "\"\""),
			NotificationFieldKind.Toggle => Seed(node, UiComponentProperties.On, null, "false"),
			NotificationFieldKind.Slider => Seed(node, UiComponentProperties.Level, JsonValueKind.Number, "0"),
			_ => Seed(node, UiComponentProperties.Selected, JsonValueKind.Number, "null")
		};

		return new NotificationField(node.Id, kind.Value, node.Children.Count, seed);
	}

	private static bool DeclaresValueEvent(UiNode node)
	{
		if (!node.Properties.TryGetValue(UiComponentProperties.Events, out var events)
			|| events.ValueKind != JsonValueKind.Array)
		{
			return false;
		}

		foreach (var item in events.EnumerateArray())
		{
			if (item.ValueKind == JsonValueKind.String
				&& item.GetString() is UiComponentEvents.Change or UiComponentEvents.Adjust)
			{
				return true;
			}
		}

		return false;
	}

	private static JsonElement Seed(UiNode node, string property, JsonValueKind? kind, string fallbackJson)
	{
		if (node.Properties.TryGetValue(property, out var value)
			&& (kind is null ? value.ValueKind is JsonValueKind.True or JsonValueKind.False : value.ValueKind == kind))
		{
			return value.Clone();
		}

		return JsonDocument.Parse(fallbackJson).RootElement.Clone();
	}

	private static NotificationException Invalid(string message)
	{
		return new NotificationException(NotificationErrorCode.Invalid, message);
	}
}
