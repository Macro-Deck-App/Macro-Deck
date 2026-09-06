using MacroDeck.Plugin.Hosting.Capabilities.Actions;
using MacroDeck.Plugin.Protocol.Capabilities.ConfigFlow;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Plugin.Hosting.Localization;
using MacroDeck.Localization;

namespace MacroDeck.Plugin.Hosting.Capabilities.ConfigFlow;

/// <summary>
/// Converts the SDK's <c>ConfigFlow</c> types into their wire DTOs. The plugin side only ever writes
/// this direction - the host reconstructs the SDK's <see cref="ConfigFlowResult" /> from what comes
/// back, since it is the host that hands it to <c>ConfigFlowManager</c> - mirroring how
/// <c>ActionParameterMapper</c> splits the same way between the two projects.
/// </summary>
internal static class ConfigFlowDescriptorMapper
{
	public static ConfigFlowCopyValueDto ToDto(ConfigFlowCopyValue value) =>
		new() { Label = PluginText.ToWire(value.Label), Value = value.Value };

	public static ConfigFlowInstructionDto ToDto(ConfigFlowInstruction instruction)
		=> new() { Text = PluginText.ToWire(instruction.Text), Values = [.. instruction.Values.Select(ToDto)] };

	public static ConfigFlowLinkDto ToDto(ConfigFlowLink link) =>
		new() { Label = PluginText.ToWire(link.Label), Url = link.Url };

	public static ConfigFlowStepDto ToDto(ConfigFlowStep step)
		=> new()
		{
			StepId = step.StepId,
			Title = PluginText.ToWireOrNull(step.Title),
			Description = PluginText.ToWireOrNull(step.Description),
			Values = [.. step.Values.Select(ToDto)],
			Instructions = [.. step.Instructions.Select(ToDto)],
			Links = [.. step.Links.Select(ToDto)],
			Fields = [.. step.Fields.Select(ActionParameterMapper.ToDto)],
			AdvancedFields = [.. step.AdvancedFields.Select(ActionParameterMapper.ToDto)]
		};

	public static ConfigFlowValueDto ToDto(ConfigFlowValue value) =>
		new() { Value = value.Value, IsSecret = value.IsSecret };

	public static ConfigFlowResultDto ToDto(ConfigFlowResult result)
		=> result.Kind switch
		{
			ConfigFlowResultKind.Step => new ConfigFlowResultDto
				{ Kind = nameof(ConfigFlowResultKind.Step), NextStep = ToDto(result.NextStep!) },
			ConfigFlowResultKind.Error => new ConfigFlowResultDto
			{
				Kind = nameof(ConfigFlowResultKind.Error),
				NextStep = ToDto(result.NextStep!),
				ErrorMessage = PluginText.ToWireOrNull(result.ErrorMessage),
				FieldErrors = ToWire(result.FieldErrors)
			},
			ConfigFlowResultKind.Complete => new ConfigFlowResultDto
			{
				Kind = nameof(ConfigFlowResultKind.Complete),
				EntryTitle = result.EntryTitle,
				Values = result.Values?.ToDictionary(pair => pair.Key,
					pair => ToDto(pair.Value),
					StringComparer.Ordinal)
			},
			ConfigFlowResultKind.External => new ConfigFlowResultDto
			{
				Kind = nameof(ConfigFlowResultKind.External), ExternalUrl = result.ExternalUrl,
				ResumeStepId = result.ResumeStepId
			},
			_ => throw new InvalidOperationException($"Unknown config flow result kind '{result.Kind}'.")
		};

	/// <summary>Field errors cross as strings: the protocol's own DTO types them that way.</summary>
	private static Dictionary<string, LocalizedText>? ToWire(
		IReadOnlyDictionary<string, LocalizedText>? fieldErrors)
	{
		if (fieldErrors is null)
		{
			return null;
		}

		var wire = new Dictionary<string, LocalizedText>(fieldErrors.Count, StringComparer.Ordinal);

		foreach (var error in fieldErrors)
		{
			wire[error.Key] = PluginText.ToWire(error.Value);
		}

		return wire;
	}
}
