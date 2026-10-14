using MacroDeck.Plugin.Protocol.Capabilities.ConfigFlow;
using MacroDeck.Localization;
using MacroDeck.Sdk.ConfigFlow;

namespace MacroDeckHost.Application.Plugins.Capabilities.Mapping;

public static class ConfigFlowResultMapper
{
	public static ConfigFlowCopyValue ToDomain(ConfigFlowCopyValueDto dto)
	{
		return new ConfigFlowCopyValue { Label = dto.Label, Value = dto.Value };
	}

	public static ConfigFlowInstruction ToDomain(ConfigFlowInstructionDto dto)
	{
		return new ConfigFlowInstruction { Text = dto.Text, Values = [.. dto.Values.Select(ToDomain)] };
	}

	public static ConfigFlowLink ToDomain(ConfigFlowLinkDto dto)
	{
		return new ConfigFlowLink { Label = dto.Label, Url = dto.Url };
	}

	public static ConfigFlowStep ToDomain(ConfigFlowStepDto dto)
	{
		return new ConfigFlowStep
		{
			StepId = dto.StepId,
			Title = dto.Title ?? default,
			Description = dto.Description ?? default,
			Values = [.. dto.Values.Select(ToDomain)],
			Instructions = [.. dto.Instructions.Select(ToDomain)],
			Links = [.. dto.Links.Select(ToDomain)],
			Fields = [.. dto.Fields.Select(ActionParameterMapper.ToDomain)],
			AdvancedFields = [.. dto.AdvancedFields.Select(ActionParameterMapper.ToDomain)]
		};
	}

	private static Dictionary<string, LocalizedText>? ToFieldErrors(
		IReadOnlyDictionary<string, LocalizedText>? fieldErrors)
	{
		return fieldErrors?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
	}

	public static ConfigFlowValue ToDomain(ConfigFlowValueDto dto)
	{
		return dto.IsSecret ? ConfigFlowValue.Secret(dto.Value) : ConfigFlowValue.Plain(dto.Value);
	}

	public static ConfigFlowResult ToDomain(ConfigFlowResultDto dto)
	{
		if (!Enum.TryParse<ConfigFlowResultKind>(dto.Kind, false, out var kind))
		{
			throw new InvalidOperationException($"Unknown config flow result kind '{dto.Kind}'.");
		}

		return kind switch
		{
			ConfigFlowResultKind.Step => ConfigFlowResult.Step(ToDomain(dto.NextStep!)),
			ConfigFlowResultKind.Error => ConfigFlowResult.Error(ToDomain(dto.NextStep!),
				dto.ErrorMessage ?? default,
				ToFieldErrors(dto.FieldErrors)),
			ConfigFlowResultKind.Complete when dto.WithoutEntry == true => ConfigFlowResult.CompleteWithoutEntry(),
			ConfigFlowResultKind.Complete => ConfigFlowResult.Complete(dto.EntryTitle ?? string.Empty,
				dto.Values?.ToDictionary(pair => pair.Key, pair => ToDomain(pair.Value), StringComparer.Ordinal)),
			ConfigFlowResultKind.External => ConfigFlowResult.External(dto.ExternalUrl ?? string.Empty,
				dto.ResumeStepId ?? string.Empty),
			_ => throw new InvalidOperationException($"Unknown config flow result kind '{dto.Kind}'.")
		};
	}
}
