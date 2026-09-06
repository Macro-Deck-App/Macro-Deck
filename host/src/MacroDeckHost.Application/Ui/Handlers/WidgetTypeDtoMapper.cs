using System.Text.Json;
using MacroDeck.Ui.Model.Versioning;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Application.Widgets;

namespace MacroDeckHost.Application.Ui.Handlers;

/// <summary>Projects the widget type catalog onto the wire.</summary>
public static class WidgetTypeDtoMapper
{
	private static readonly JsonElement _emptyObject = JsonDocument.Parse("{}").RootElement;

	public static List<WidgetTypeDto> MapToDto(IEnumerable<WidgetTypeCatalogEntry> entries)
		=> [.. entries.Select(MapToDto)];

	public static WidgetTypeDto MapToDto(WidgetTypeCatalogEntry entry)
	{
		var supportsConfigUi = entry.Descriptor.HasConfiguration;

		return new WidgetTypeDto
		{
			Id = entry.WidgetTypeId,
			ProviderId = entry.ProviderId,
			IsBuiltIn = entry.IsBuiltIn,
			Name = entry.Descriptor.Name,
			Description = entry.Descriptor.Description ?? default,
			DefaultData = ParseDefaultData(entry.Descriptor.DefaultData),
			SupportsConfigUi = supportsConfigUi,
			ConfigUiModelVersion = supportsConfigUi ? UiModelVersions.Current : 0
		};
	}

	// A descriptor that declares nothing, or declares something unreadable, reports an empty object rather
	// than failing the whole catalog: one provider's bad literal must not stop the picker from listing
	// every other type. Registration already rejects a non-object, so this only catches a built-in literal.
	private static JsonElement ParseDefaultData(string? defaultData)
	{
		if (string.IsNullOrWhiteSpace(defaultData))
		{
			return _emptyObject;
		}

		try
		{
			using var document = JsonDocument.Parse(defaultData);
			return document.RootElement.ValueKind == JsonValueKind.Object
				? document.RootElement.Clone()
				: _emptyObject;
		}
		catch (JsonException)
		{
			return _emptyObject;
		}
	}
}
