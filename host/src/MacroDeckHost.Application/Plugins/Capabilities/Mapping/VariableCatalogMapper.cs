using MacroDeck.Plugin.Protocol.Capabilities.Variables;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Application.Plugins.Capabilities.Mapping;

public static class VariableCatalogMapper
{
	public static VariableDefinition ToDomain(VariableDefinitionDto dto)
		=> TryToDomain(dto) ?? throw new InvalidOperationException($"Unknown variable type '{dto.Type}'.");

	/// <summary>The definition, or <c>null</c> when its type is not one this host knows. Separate from
	/// <see cref="ToDomain" /> for the persisted-snapshot reader, which drops an entry it cannot read
	/// rather than losing the whole document.</summary>
	public static VariableDefinition? TryToDomain(VariableDefinitionDto dto)
	{
		if (!Enum.TryParse<VariableType>(dto.Type, ignoreCase: false, out var type))
		{
			return null;
		}

		return new VariableDefinition
		{
			Id = dto.Id,
			Name = dto.Name,
			Type = type,
			Materialization = string.Equals(dto.Materialization,
				VariableMaterializations.OnDemand,
				StringComparison.Ordinal)
				? VariableMaterialization.OnDemand
				: VariableMaterialization.Eager,
			DisplayName = dto.DisplayName ?? default,
			Description = dto.Description ?? default,
			Icon = dto.Icon,
			DecimalPlaces = dto.DecimalPlaces,
			RefreshInterval = dto.RefreshIntervalSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null,
			Unit = dto.Unit,
			SemanticKind = dto.SemanticKind,
			Attributes = dto.Attributes,
			ParentId = dto.ParentId,
			IsContainer = dto.IsContainer,
			IsBindable = dto.IsBindable,
			Configuration = dto.ConfigurationKey is { } key
				? new VariableConfiguration(key, dto.ConfigurationName ?? default)
				: null,
			Write = dto.Write is null
				? null
				: new VariableWriteCapability { CommitOnRelease = dto.Write.CommitOnRelease }
		};
	}

	public static VariableDefinitionDto ToDto(VariableDefinition definition)
		=> new()
		{
			Id = definition.Id,
			Name = definition.Name,
			Type = definition.Type.ToString(),
			Materialization = definition.Materialization == VariableMaterialization.OnDemand
				? VariableMaterializations.OnDemand
				: VariableMaterializations.Eager,
			DisplayName = definition.DisplayName,
			Description = definition.Description,
			Icon = definition.Icon,
			DecimalPlaces = definition.DecimalPlaces,
			RefreshIntervalSeconds = definition.RefreshInterval?.TotalSeconds,
			Unit = definition.Unit,
			SemanticKind = definition.SemanticKind,
			Attributes = definition.Attributes,
			ParentId = definition.ParentId,
			IsContainer = definition.IsContainer,
			IsBindable = definition.IsBindable,
			ConfigurationKey = definition.Configuration?.Key,
			ConfigurationName = definition.Configuration?.Name,
			Write = definition.Write is null
				? null
				: new VariableWriteCapabilityDto { CommitOnRelease = definition.Write.CommitOnRelease }
		};

	public static VariableCatalogPage ToDomain(VariableCatalogPageResult result)
		=> new() { Items = [.. result.Items.Select(ToDomain)], ContinuationToken = result.ContinuationToken };

	public static VariableValue ToDomain(VariableIdValueDto dto)
		=> new() { Id = dto.Id, Reading = VariableValueMapper.ToDomain(dto.Reading) };
}
