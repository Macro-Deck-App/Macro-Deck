using MacroDeck.Plugin.Protocol.Capabilities.Variables;
using MacroDeck.Sdk.Variables;

namespace MacroDeck.Plugin.Hosting.Capabilities.Variables;

/// <summary>Maps the SDK's <see cref="VariableDefinition" /> to the wire DTO, for both materialization
/// policies, and derives the capability local id an eager variable is addressed by - see
/// <see cref="VariablesCapabilityHandler" />'s remarks.</summary>
internal static class VariableDescriptorMapper
{
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

	/// <summary>
	/// The declared capability local id for an eager variable: its own id where the provider set one,
	/// otherwise one derived from its canonical name. Null when neither produces a valid id (a template
	/// name with no explicit id) - such a variable is still shown in the <c>describe</c> catalogue, it
	/// simply cannot be addressed as its own capability.
	/// </summary>
	public static string? LocalIdOf(VariableDefinition definition) => definition.ResolvedId;
}
