using MacroDeck.Plugin.Protocol.Capabilities.Actions;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Actions;

namespace MacroDeckHost.Application.Plugins.Capabilities.Mapping;

public static class ActionCatalogMapper
{
	public static RemoteActionDescriptor ToDomain(ActionDescriptorDto dto)
		=> new(dto.LocalId,
			dto.Name,
			dto.Description,
			[.. dto.Parameters.Select(ActionParameterMapper.ToDomain)],
			dto.DescriptiveUiSchema,
			dto.SupportsDynamicOptions,
			dto.ProvidesState,
			dto.ConfiguresWithUiTree,
			dto.ProvidesIcon);

	public static IReadOnlyList<RemoteActionDescriptor> ToDomain(ActionCatalogPayload payload)
		=> [.. payload.Actions.Select(ToDomain)];

	public static ActionDescriptorDto ToDto(RemoteActionDescriptor descriptor)
		=> new()
		{
			LocalId = descriptor.LocalId,
			Name = descriptor.Name,
			Description = descriptor.Description,
			Parameters = [.. descriptor.Parameters.Select(ActionParameterMapper.ToDto)],
			DescriptiveUiSchema = descriptor.DescriptiveUiSchema,
			SupportsDynamicOptions = descriptor.SupportsDynamicOptions,
			ProvidesState = descriptor.ProvidesState,
			ConfiguresWithUiTree = descriptor.ConfiguresWithUiTree,
			ProvidesIcon = descriptor.ProvidesIcon
		};

	public static ActionCatalogPayload ToDto(IReadOnlyList<RemoteActionDescriptor> descriptors)
		=> new() { Actions = [.. descriptors.Select(ToDto)] };
}
