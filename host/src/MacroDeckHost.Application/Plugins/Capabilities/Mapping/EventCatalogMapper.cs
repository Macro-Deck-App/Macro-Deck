using MacroDeck.Plugin.Protocol.Capabilities.Events;
using MacroDeck.Sdk.Events;

namespace MacroDeckHost.Application.Plugins.Capabilities.Mapping;

public static class EventCatalogMapper
{
	public static EventDefinition ToDomain(EventDescriptorDto dto)
	{
		if (!Enum.TryParse<EventDeliveryKind>(dto.DeliveryKind, ignoreCase: false, out var deliveryKind))
		{
			throw new InvalidOperationException($"Unknown event delivery kind '{dto.DeliveryKind}'.");
		}

		return new EventDefinition
		{
			Id = dto.LocalId,
			Name = dto.Name,
			Description = dto.Description ?? default,
			Category = dto.Category ?? default,
			IconName = dto.IconName,
			DeliveryKind = deliveryKind,
			ConfigurationParameters = [.. dto.ConfigurationParameters.Select(ActionParameterMapper.ToDomain)],
			PayloadParameters = [.. dto.PayloadParameters.Select(ActionParameterMapper.ToDomain)]
		};
	}

	public static EventDescriptorDto ToDto(EventDefinition definition)
		=> new()
		{
			LocalId = definition.Id,
			// Only ever called for a descriptor that came back off the wire - where the protocol types this
			// text as a string - on its way into the persisted snapshot, so a literal is all there can be.
			Name = definition.Name.Literal ?? string.Empty,
			Description = definition.Description.Literal,
			Category = definition.Category.Literal,
			IconName = definition.IconName,
			DeliveryKind = definition.DeliveryKind.ToString(),
			ConfigurationParameters = [.. definition.ConfigurationParameters.Select(ActionParameterMapper.ToDto)],
			PayloadParameters = [.. definition.PayloadParameters.Select(ActionParameterMapper.ToDto)]
		};
}
