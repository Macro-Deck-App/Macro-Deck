using System.Text.Json;
using MacroDeck.Sdk.Identity;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Application.Caching;

namespace MacroDeckHost.Application.Widgets;

public interface IWidgetDataReader
{
	WidgetDataSnapshot? Read(string ownerId, string widgetId);
}

// The owner is the authenticated caller, never anything a request names, so a provider only ever reads
// widgets of the types it registered under its own id.
public sealed class WidgetDataReader : IWidgetDataReader
{
	private readonly IFolderCache _folders;

	public WidgetDataReader(IFolderCache folders)
	{
		_folders = folders;
	}

	public WidgetDataSnapshot? Read(string ownerId, string widgetId)
	{
		if (string.IsNullOrEmpty(ownerId) || !Guid.TryParse(widgetId, out var id))
		{
			return null;
		}

		var widget = _folders.GetAllFolders().SelectMany(folder => folder.Widgets).FirstOrDefault(w => w.Id == id);

		if (widget is null
			|| !QualifiedId.TryParse(widget.Type, out var type)
			|| !string.Equals(type.OwnerId, ownerId, StringComparison.Ordinal))
		{
			return null;
		}

		return new WidgetDataSnapshot(id.ToString(), widget.Type, Parse(widget.Data));
	}

	private static JsonElement Parse(string? json)
	{
		if (!string.IsNullOrWhiteSpace(json))
		{
			try
			{
				using var document = JsonDocument.Parse(json);
				if (document.RootElement.ValueKind == JsonValueKind.Object)
				{
					return document.RootElement.Clone();
				}
			}
			catch (JsonException)
			{
			}
		}

		using var empty = JsonDocument.Parse("{}");
		return empty.RootElement.Clone();
	}
}
