using MacroDeck.Plugin.Hosting.Capabilities.Actions;
using MacroDeck.Plugin.Protocol.Capabilities.Events;
using MacroDeck.Sdk.Events;
using MacroDeck.Plugin.Hosting.Localization;

namespace MacroDeck.Plugin.Hosting.Capabilities.Events;

/// <summary>Maps the SDK's <c>EventDefinition</c> to the wire DTO, reusing <see cref="ActionParameterMapper" />
/// for its parameter lists - an event's configuration and payload fields are described with exactly the
/// same schema an action's parameters are.</summary>
internal static class EventDescriptorMapper
{
	public static EventDescriptorDto ToDto(EventDefinition definition)
		=> new()
		{
			LocalId = definition.Id,
			Name = PluginText.ToWire(definition.Name),
			Description = PluginText.ToWireOrNull(definition.Description),
			Category = PluginText.ToWireOrNull(definition.Category),
			IconName = definition.IconName,
			DeliveryKind = definition.DeliveryKind.ToString(),
			ConfigurationParameters = [.. definition.ConfigurationParameters.Select(ActionParameterMapper.ToDto)],
			PayloadParameters = [.. definition.PayloadParameters.Select(ActionParameterMapper.ToDto)]
		};
}
