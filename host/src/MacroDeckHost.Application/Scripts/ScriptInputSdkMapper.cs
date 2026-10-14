using MacroDeckHost.Domain.Entities;
using SdkScriptInput = MacroDeck.Sdk.Scripts.ScriptInput;
using SdkScriptInputType = MacroDeck.Sdk.Scripts.ScriptInputType;

namespace MacroDeckHost.Application.Scripts;

public static class ScriptInputSdkMapper
{
	public static SdkScriptInput ToSdk(ScriptInput input)
	{
		return new SdkScriptInput
		{
			Name = input.Name,
			Type = ToSdk(input.Type),
			Label = input.Label,
			Description = input.Description,
			Required = input.Required,
			DefaultValue = input.DefaultValue
		};
	}

	private static SdkScriptInputType ToSdk(ScriptInputType type)
	{
		return type switch
		{
			ScriptInputType.Numeric => SdkScriptInputType.Numeric,
			ScriptInputType.Boolean => SdkScriptInputType.Boolean,
			ScriptInputType.Color => SdkScriptInputType.Color,
			_ => SdkScriptInputType.Text
		};
	}
}
