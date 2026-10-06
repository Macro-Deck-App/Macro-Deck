using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Widgets;
using MacroDeck.Ui.Model.Versioning;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Application.Widgets;

namespace MacroDeckHost.Application.Ui.Handlers;

/// <summary>Projects the widget type catalog onto the wire.</summary>
public static class WidgetTypeDtoMapper
{
	private static readonly JsonElement _emptyObject = JsonDocument.Parse("{}").RootElement;

	public static List<WidgetTypeDto> MapToDto(IEnumerable<WidgetTypeCatalogEntry> entries,
		IIntegrationRegistry integrations)
		=> [.. entries.Select(entry => MapToDto(entry, integrations))];

	public static WidgetTypeDto MapToDto(WidgetTypeCatalogEntry entry, IIntegrationRegistry integrations)
	{
		var supportsConfigUi = entry.Descriptor.HasConfiguration;

		return new WidgetTypeDto
		{
			Id = entry.WidgetTypeId,
			ProviderId = entry.ProviderId,
			IsBuiltIn = entry.IsBuiltIn,
			ProviderName = ProviderNameOf(entry, integrations),
			IsPluginProvided = !entry.IsBuiltIn && integrations.GetOrigin(entry.ProviderId) == IntegrationOrigin.Plugin,
			Name = entry.Descriptor.Name,
			Description = entry.Descriptor.Description ?? default,
			DefaultData = ParseDefaultData(entry.Descriptor.DefaultData),
			SupportsConfigUi = supportsConfigUi,
			ConfigUiModelVersion = supportsConfigUi ? UiModelVersions.Current : 0
		};
	}

	private static LocalizedText ProviderNameOf(WidgetTypeCatalogEntry entry, IIntegrationRegistry integrations)
	{
		if (entry.IsBuiltIn)
		{
			return default;
		}

		var integration = integrations.Integrations.FirstOrDefault(candidate =>
			string.Equals(candidate.Id, entry.ProviderId, StringComparison.Ordinal));
		if (integration is null)
		{
			return entry.ProviderId;
		}

		var name = integration is IWidgetTypeProvider provider
			? ProviderDisplayName.Resolve(provider.ProviderName, integration)
			: integration.Name;
		return name.IsEmpty ? entry.ProviderId : name;
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
