using MacroDeck.Sdk.Actions;
using MacroDeck.Plugin.Protocol.Capabilities.Actions;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Actions;

namespace MacroDeckHost.Application.Plugins.Capabilities.Mapping;

public static class ActionCatalogMapper
{
	public static RemoteActionDescriptor ToDomain(ActionDescriptorDto dto)
	{
		return new RemoteActionDescriptor(dto.LocalId,
			dto.Name,
			dto.Description,
			[.. dto.Parameters.Select(ActionParameterMapper.ToDomain)],
			dto.DescriptiveUiSchema,
			dto.SupportsDynamicOptions,
			dto.ProvidesState,
			dto.ConfiguresWithUiTree,
			dto.ProvidesIcon,
			dto.Role,
			ParseRiskCategory(dto.RiskCategory));
	}

	public static ActionRiskCategory ParseRiskCategory(string? value)
	{
		return Enum.TryParse<ActionRiskCategory>(value, true, out var category)
			&& Enum.IsDefined(category)
			&& !int.TryParse(value, out _)
				? category
				: ActionRiskCategory.None;
	}

	public static IReadOnlyList<RemoteActionDescriptor> ToDomain(ActionCatalogPayload payload)
	{
		return [.. payload.Actions.Select(ToDomain)];
	}

	public static ActionDescriptorDto ToDto(RemoteActionDescriptor descriptor)
	{
		return new ActionDescriptorDto
		{
			LocalId = descriptor.LocalId,
			Name = descriptor.Name,
			Description = descriptor.Description,
			Parameters = [.. descriptor.Parameters.Select(ActionParameterMapper.ToDto)],
			DescriptiveUiSchema = descriptor.DescriptiveUiSchema,
			SupportsDynamicOptions = descriptor.SupportsDynamicOptions,
			ProvidesState = descriptor.ProvidesState,
			ConfiguresWithUiTree = descriptor.ConfiguresWithUiTree,
			ProvidesIcon = descriptor.ProvidesIcon,
			Role = descriptor.Role,
			RiskCategory = descriptor.RiskCategory == ActionRiskCategory.None
				? null
				: descriptor.RiskCategory.ToString().ToLowerInvariant()
		};
	}

	public static ActionCatalogPayload ToDto(IReadOnlyList<RemoteActionDescriptor> descriptors)
	{
		return new ActionCatalogPayload { Actions = [.. descriptors.Select(ToDto)] };
	}
}
